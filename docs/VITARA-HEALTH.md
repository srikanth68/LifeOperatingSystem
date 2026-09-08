# Vitara: from measurements to conclusions

Vitara used to store what Oura reported. It now also computes what those numbers mean
against sixty days of your own history, and says so when something is off.

This document covers that layer: what it does, the decisions inside it that are not
obvious, and what is still missing.

---

## 1. The shape of it

```
Oura sync   →   typed tables        SleepSession, DailyReadiness, DailyActivity …
                     │                (unchanged — still serve the dashboard)
                     ▼
ObservationProjector →  Observations   flat rows: metric, value, day, context
                     ▼
BaselineCalculator   →  Baselines      what normal is, minus what should not count
                     ▼
   z-scores, ACWR, sleep debt, slopes  →  DerivedMetrics
                     ▼
FindingRun           →  Findings       what is worth saying
                     ▼
/api/health/*  ·  health_findings  ·  HealthFindingsWorker → your Telegram
```

Everything above the findings line is **pure arithmetic with no model involvement
anywhere**. The model is shown findings and asked what is worth saying; it is never
asked what happened.

That division is the whole architecture, and it is why identity keys are derived from
content in code. A model asked to generate a key produces a different one every run for
the same condition — and then nothing can be deduplicated, cooled down, or resolved. The
same elevated resting heart rate would arrive as a fresh notification every morning.

---

## 2. Projection

`ObservationProjector` turns typed rows into flat observations. It is the bridge that
made the analytics layer possible **without rewriting anything** — the typed tables stay
exactly as they are and keep serving the dashboard and San; each one *also* becomes a
handful of observation rows the baseline machinery treats uniformly.

Pure and synchronous: no database, no clock. Same rows in, same observations out, which
is what makes it testable and makes re-running a backfill safe.

Three decisions inside it:

**Missing readings are omitted, never written as zero.** A missing HRV is not an HRV of
zero, and a baseline that averages in the nights the ring was not worn is describing
someone else.

**Oura's `day` field is used as-is**, not re-derived from timestamps. Oura already
reports the night in local terms; re-deriving from a UTC instant is exactly how sleep
starting at 11pm ends up filed under tomorrow.

**Skin temperature deviation is promoted to a first-class metric.** Combined with
resting HR and HRV it is the earliest pre-symptomatic illness signal available from this
data, and it was sitting as an incidental field on a sleep row.

---

## 3. What counts as "normal"

### Baselines exclude what should not count

A mean is not enough. Two weeks of illness inside a rolling window raises the baseline,
and the system then stops flagging exactly what it exists to flag — while every number
it reports still looks perfectly reasonable.

So `BaselineCalculator` takes excluded periods, travel, and device changes, and records
what it dropped so a baseline can explain itself later.

Cross-timezone travel **corrupts** circadian metrics rather than merely influencing
them. Sleep timing in a different timezone is not a worse night; it is a different
question, and pooling the two produces a baseline describing neither.

### Context signatures — the important design decision

Baselines are computed per `(metric, context signature)`. The obvious reading is to key
on every context field, and that is unusable arithmetic:

> Blood pressure keyed on position, arm, fasting, time of day, caffeine and alcohol is
> dozens of signatures, each needing ~30 readings before it says anything. Nobody
> accumulates thirty morning-seated-left-arm-no-caffeine-no-alcohol-fasted readings.
> Every bucket stays permanently invalid and the feature silently never works.

So each metric declares **only** the context that genuinely changes the number:

```
systolic_bp   →   position, timeOfDay
glucose       →   fasting
everything else → nothing
```

The rest is still recorded on the observation — so it can explain an outlier later — but
does not fragment the baseline.

### Robust statistics, not the textbook ones

The spec said: compute the spread, then discard anything more than three deviations from
the spread you just computed. That is **circular** — outliers inflate σ enough to hide
themselves — and unstable at n≈60 on data that is rarely symmetric.

| Used | Instead of | Why |
|---|---|---|
| Median absolute deviation (×1.4826) | 3σ | Not circular; outliers cannot mask themselves |
| Theil–Sen slope | Least squares | ~29% breakdown point |
| Mann–Kendall with ties correction, p<0.01 | A bare slope | A slope always exists; fit a line to noise and a line comes back |

Below `MinBaselineN` (21 readings) a baseline is marked **invalid** rather than hidden.
"Not enough data yet" is a real answer; silently omitting the metric looks identical to
the metric not existing. No z-score is computed against an invalid baseline — a
confident number built on four readings is the one that gets acted on.

---

## 4. Two places the spec was wrong

### Sleep need cannot be measured from sleep duration

The spec said to derive habitual need "from their long-run distribution, not a fixed 8
hours". That sounds right and is not:

> If someone is chronically short, taking the centre of what they actually sleep
> computes their **deficit as their requirement**, and then reports, forever, that they
> are not in debt. The system would be most confident exactly when it was most wrong.

There is no way to recover need from duration alone. What `SleepDebt` does instead:

1. **Let the user say.** If they know, that beats any inference.
2. Failing that, use the **upper quartile**, not the middle. The longest nights are the
   ones closest to unconstrained — weekends, no alarm — so p75 is a floor on need rather
   than a description of the habit. Still an underestimate for a chronic under-sleeper,
   and labelled an estimate so nobody mistakes it for a measurement.

The basis travels with the number wherever it goes. "You are four hours short" means
something different when the need behind it was inferred rather than stated.

### A regime detector without a dwell requirement disables the system it feeds

A sustained step change is not a deviation to be averaged away — it is a new normal, and
a rolling baseline that absorbs it over sixty days describes neither the old level nor
the new one while never mentioning that anything happened.

`RegimeDetector` uses a two-window mean-shift test. Nothing cleverer is warranted: the
signal is a step in a short noisy series, and a method nobody can check by hand produces
conclusions nobody can argue with.

**The dwell requirement is the load-bearing part.** Without requiring the new level to
persist (14 days) a twitchy detector resets on every wobble — and once "normal" is
redefined every few days, **nothing is ever abnormal again**. The detector would quietly
disable the entire system it feeds, and it would look like it was working the whole
time.

Two bugs found while building it: it fired on pure noise (MAD → 0 on steady metrics),
then could not detect anything on zero-variance data. Fixed with a pooled MAD, a
distribution-separation test, and explicit degenerate-case handling.

---

## 5. The detectors

| Type | Fires when | Severity |
|---|---|---|
| `deviation` | Outside personal normal **and still outside it tomorrow** | info / notable |
| `early_illness` | Resting HR up, HRV down, temp up — **two of three, sustained** | notable / high |
| `regime_change` | A step big enough to matter that then stays put | varies |
| `strain_risk` | ACWR outside 0.8–1.3, or ≥5h accumulated sleep debt | **info only** |
| `drift` | A trend that passed the significance test | info |
| `staleness` | Expected data that did not arrive | info |

**Sustained is the cheapest and most effective noise filter available.** A single odd
night is a single odd night, and a system that says so every time gets muted. Deviation
requires the breach on consecutive days *in the same direction* — a metric bouncing above
and below is unsettled, not deviating.

**The illness detector is the most valuable and the most dangerous.** Fire it spuriously
twice and it stops being read, including on the morning it is right. So: two of three
components, each day independently, sustained. Any one alone moves for a late meal, a
hard session, a warm room.

**ACWR is capped at `info` on purpose.** Its evidence base is weaker than its popularity
suggests — numerator and denominator share data, and the threshold bands have not held
up to scrutiny. Worth showing, not worth alarming anyone about.

**Missing data is a finding, not a gap to paper over.** A ring left in a drawer produces
exactly the same silence as a week of perfect health.

Every threshold is overridable by environment variable. The spec left them unspecified,
and this project has already watched a notification channel become something to ignore.
The lesson was not "send fewer" — it was that **a detector nobody can tune eventually
gets muted wholesale, taking the true positives with it.**

---

## 6. What runs the detectors

`FindingRun` is the newest piece and the one that made the rest speak. Before it, the
detectors were pure functions called only by their own tests: the statistics were
correct and the system said nothing.

Three judgement calls live here.

**Deviation fires for ten curated metrics, not everything with a baseline.** Two sigma
sustained over two days is about one day in a thousand per metric — negligible until
multiplied by thirty metrics and 365 days. Worse, these metrics are correlated: deep,
REM and total sleep move together, so one poor week arrives as four findings saying the
same thing. The others are still measured, still baselined, still visible on request;
they just do not start a conversation.

**When the illness detector fires, its three components are suppressed.** "Resting HR is
high, HRV is low, temperature is up, and you may be getting ill" is four notifications
about one thing.

**Staleness applies to the dense tier only.** A weight entered by hand every few days or
a lab drawn twice a year is not missing, it is scheduled. And a metric *never* recorded
is not stale either — that would be the system complaining about its own emptiness on
day one.

---

## 7. Findings have a lifecycle

`SyncFindingsAsync` is a **reconcile, not an insert**.

| Outcome | Meaning |
|---|---|
| **Opened** | New. Matched no open key. |
| **Continued** | Still true. Key matched — updates severity and evidence, **keeps `FirstDetectedLocal`**. |
| **Resolved** | Was open, not detected this pass. Marked, not deleted. |

Keeping the original detection date is what lets a finding be reported as **"day 5"**
rather than as something new every morning — the difference between a system tracking a
condition and one repeating itself.

Resolution is recorded rather than deleted because the condition ending is information
too. "Your resting heart rate is back to normal" is worth as much as the original
finding, and a deleted row can never say it.

An empty detection set is meaningful: it means everything open has resolved.

---

## 8. Getting it to you

### Read surface

```
GET /api/health/findings    active findings, with daysRunning and daysSinceDetected
GET /api/health/baselines   what normal is per metric, including invalid ones
GET /api/health/derived     z-scores, ratios, slopes, with the inputs that fed them
GET /api/health/summary     one call shaped for a model rather than a chart
```

Every response leads with **recency**. San reads these, and an analysis with no date
attached reads as today's no matter how old it is. `daysSinceDetected` exists because a
finding last seen four days ago is stale even if nothing formally resolved it — which is
what happens when the worker stops running, and a stale finding read as current is worse
than no finding at all.

Derived values carry their `inputs` blob. A derived number nobody can trace back to its
inputs is indistinguishable from one the system made up.

### Tools

| Tool | For |
|---|---|
| `vitara_health` | Raw last-night numbers — sleep, readiness, workouts |
| `health_findings` | What Vitara has **concluded** |
| `health_baselines` | What is normal *for you*, so "high" means high for you |

### Notification

`HealthFindingsWorker` polls every 3 hours and hands findings to San's existing
`FindingDispatcher`, which already does the hard part: keyed deduplication, cooldowns
that lengthen when something is ignored, and the separation between "message the user"
and "tell NorthStar". A second notification path would mean a second cooldown to tune
and a second place for the same finding to arrive twice.

Severity maps `high → high`, `notable → medium`, `info → low`. **Nothing maps to
critical.** A system inferring from a ring is not equipped to declare an emergency; the
most it should do is say something is worth a look.

---

## 9. Scheduling, and why it is not a nightly job

The spec called for a midnight job. That is wrong for how you actually use this:

> You sync Oura in the morning. At midnight the night that just finished has not
> arrived yet — a job running then computes on absence, writes a baseline missing its
> most recent day, and reports staleness for data that is merely not downloaded.

So the worker is **driven by the data, not the clock**. It wakes hourly, compares the
newest observation against the newest baseline, and does nothing unless there is
something new. Correct whenever the sync happens, and self-healing when the sync is late
or fails — the work happens on the next wake after the data lands.

---

## 10. What is still missing

```yaml
data audit:     never run — needs you on the box (spec step 1)
backfill:       observations only project the last 120 days
manual entry:   no UI for BP, glucose, weight-in-context
labs:           lab_anchor finding type defined, no ingestion
model layer:    findings are shown to the model; no summarisation prompt yet
```

The **data audit is the blocker**. Steps 3–9 all assume it, and there is no point
building a backfill before knowing what is actually in that database.

Also worth knowing: `FindingRun`'s attribution for regime changes currently only reports
that another metric shifted nearby. Real attribution — a device change, a travel period,
a season — is designed for and not implemented. It is deliberately phrased as a possible
explanation and never a cause; asserting one is a claim the data cannot support.

---

## 11. Test coverage

197 tests in `Vitara.Tests`, of which the ones worth knowing about are the ones that
encode a decision rather than a calculation:

- A deviation against an **unproven** baseline is not reported
- A metric outside the curated list stays quiet even at −3σ
- Illness suppresses its own components
- A metric never recorded is not stale; one that stopped arriving is
- Keys are **stable across two identical passes** — the property the entire ledger
  depends on
