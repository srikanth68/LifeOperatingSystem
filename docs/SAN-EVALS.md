# Measuring San: evals, guardrails, tools, and the training question

Everything in this repo that runs in CI exercises deterministic C#. Not one test sends
anything to Gemma. That gap is the reason this document exists, and it produced a real
mistake before it was closed.

---

## 1. Why

A prompt rewrite was called a win on the strength of **one sample**. Measured properly
at ten runs, the same change had gone from never inventing a bank balance to inventing
one in **six runs out of ten**. The rewrite was reverted.

That is the whole argument for an eval harness. A prompt edit, a quantisation change, a
model swap and a fine-tune all land the same way — invisibly — and the only defence is a
number you can compare against yesterday's number.

---

## 2. What San.Evals is

A **console tool**, not a test project. It needs a live model, it reports rates rather
than pass/fail, and it must never run as part of the normal gate.

```bash
dotnet run --project san/San.Evals -- \
  --url http://<model-host>:8080 \
  --runs 10 \
  --tools san/San.Evals/prompts/tools-live.json \
  --out baseline.json

# later, after a change
dotnet run --project san/San.Evals -- --url ... --compare baseline.json
```

| Flag | Meaning |
|---|---|
| `--runs` | Samples per case. Ten is a floor, not a target — see §7. |
| `--filter` | One category (`tool-select`, `fabrication`, …) |
| `--tools` | A catalogue export. Omitted, it uses the frozen 11-tool fixture. |
| `--prompt` | The system prompt you actually run |
| `--compare` | Prints only movement beyond ±15%, so noise stays quiet |
| `--temp` | Defaults to 0.7, matching production |

### Design decisions worth knowing

**It talks to llama.cpp directly, not through San's API.** Going through the module
would measure San plus its prompt assembly plus its tool loop plus whatever NorthStar
recalled that morning — and when the number moved, nothing would say which of those
moved it.

**One round trip, never a loop.** The tool cases measure the *first decision*: given
this request and this catalogue, what did it reach for? Running the loop would mean
executing real tools against real data to score a benchmark.

**Thinking is off**, matching interactive chat. Measuring with it on would score a
configuration nobody uses. (It also costs ~15×: 180 tokens and 6.4s versus 12 and 0.4s.)

**Failures are printed verbatim.** A rate says something regressed; only the text says
what the model actually did. `called nothing` is the single most informative thing a
failing tool case can say.

---

## 3. The 19 cases

Every one is a failure that really happened, or one a guardrail exists to catch.

| Category | n | Asks |
|---|---|---|
| `fabrication` | 4 | No data, no tools, no context — does it invent a figure? |
| `phantom-write` | 3 | No tools offered — does it claim it saved something anyway? |
| `tool-select` | 7 | Given that it *can* act, does it, and does it pick right? |
| `format` | 1 | Digits, not "seventy thousand four hundred fifty" |
| `voice` | 2 | Three sentences, no markdown — Kokoro reads these aloud |
| `knowledge` | 2 | Does the anti-fabrication prompt also block ordinary facts? |

Two halves asking different questions. **Text cases offer no tools** — a model that
cannot act and still says it saved a reminder has told a plain lie, worth measuring on
its own. **Tool cases hand over a catalogue** — this is the half a fine-tune would
target, so it is the half that gives training an acceptance criterion.

`knowledge` is a **dial, not a bug**. A prompt strict enough to stop invented balances
also stops "who wrote Dune". Read those two next to the fabrication rate, never alone.

**Scoring reuses the production guards.** `WriteClaimCheck` already decides what counts
as claiming a write San did not make. Having the eval decide that differently would
measure something the running system does not care about.

---

## 4. The catalogue matters more than expected

The single most useful thing this harness produced.

| | 11 (fixture) | 20 (filtered) | 48 (real) |
|---|---|---|---|
| overall | 95.7% | 84.3% | 74.3% |
| `complete_looks_first` | 10/10 | 9/10 | 2/10 |
| `habit_log_acts` | 7/10 | 0/10 | 0/10 |

The fixture said there was no headroom and training was unjustified. **That conclusion
was wrong**, and only the real catalogue showed it. Worse, the two failing cases behaved
differently — one tracked catalogue size, one ignored it — which is what separated two
distinct root causes that had looked like one problem.

The fixture is still useful (frozen, so runs stay comparable) but **it does not
transfer**. Measure against `tools-live.json`.

`scripts/refresh-eval-tools.py` keeps that export honest:

```bash
python scripts/refresh-eval-tools.py --check     # fails if a description drifted
python scripts/refresh-eval-tools.py             # refresh descriptions
python scripts/refresh-eval-tools.py --add-new   # append newly declared tools
```

Descriptions only. Re-deriving parameters is what broke hand-extraction twice: one regex
could not see past `async Task<string>` and matched a *later* method's parameters,
leaving twelve tools with none; another paired the wrong quotes around an escaped quote
and ate the text between them. `--add-new` is a separate flag because appending changes
catalogue size, and catalogue size is one of the things being measured.

---

## 5. What the measurements found

Two root causes, both **tool design, not model capability**.

### Tools demanding a GUID

Three tools told the model to go and fetch an id first:

```
reminder_complete   "reminderId* from reminders_list"
action_complete     "actionId* from actions_pending"
habit_checkin       "Needs the GUID - call karma_habits first"
```

The model will not reliably chain that lookup and cannot invent a GUID, so it stalls and
asks the user for something the user does not have either. That is verbatim the
conversation this whole effort started from:

> *"I need the property ID for Scoter Street to mark that task complete. Can you provide
> that?"* — to someone who has never seen a GUID.

**`NameResolver`** moves the chain into code: the model names the thing, deterministic
matching resolves it. Ids still work. Ambiguity goes back to the user rather than being
guessed — "Pay" matching both the Spectrum bill and the credit card bills must not
silently tick off the wrong one — and a failed match returns the list of what is open.

Result: `complete_looks_first` **2/10 → 29/30**.

### A tool losing a name-match

`journal_add` was never called for "log it", despite a description that says to use it
when the user says to log something. Four tools competed on that word and only one
spelled it:

```
food_log · weight_log · workout_log     name says log
journal_add                             description says log
```

The model matched the name and stopped: *"I can log a workout, but I don't have a
specific function to log reading as a structured activity."* Renamed to **`journal_log`**.

---

## 6. The guardrails

Deterministic checks in `San.Application`, running in production, not just in the eval.

### `WriteClaimCheck`

Answers one question: *does this reply announce a write that no tool performed?* The
loop knows every tool it ran, so a completion claim can be checked rather than trusted.

| Branch | Catches |
|---|---|
| `ClaimPattern` | "I have saved 10 reminders", "the reminder has been created" |
| `IntentPattern` | "I will now mark the task complete" — a declaration, not an offer |
| `TerseCompletion` | "Habit reading done for today." — no subject, no verb |
| `ToolCallLiteral` | `action_complete(action="tree trimming")` written as prose |

Each was added because it actually happened. Three guards against false positives:

- A **modal lookbehind** separates "I set the reminder" from "should I set reminders?" —
  San asking permission is the most common sentence in that shape.
- `Conditional` spares "I will add these once you tell me the timing" — a promise
  waiting on the user, which San is right to make.
- `Interrogative` spares "Is the reading habit done for today?" — nudging San for asking
  would teach it to stop asking.

**The tool-call literal is flagged, never executed.** Running a call the model never
formally made would mean writing to real data off a regex over free text, and the
argument would be exactly as unverified as the call.

When any branch fires, the loop gives the model **one** chance to either do the work or
retract the claim. The nudge is worded to stay harmless if the check misfires.

### `ToolCallRecorder`

Wraps the executor and records every call — arguments and results, capped at 600
characters each. Exceptions are recorded and rethrown. This is the training-data
capture; see below.

### `ChatTools.ExcludedByDefault`

Four contact tools, dropped at the user's request, reversible via `CHAT_TOOLS_EXCLUDE`.
Deliberately **not** used to shrink the catalogue for eval reasons — see §7.

---

## 7. Where measurement went wrong twice

Both mistakes are recorded here because the harness exists to prevent exactly this
class of error, and it caught neither on its own.

**Reading a rate off n=10.** After the `journal_log` rename, `habit_log_acts` went 2/10
→ 5/10 and it looked like the rename had roughly doubled it. Pooled across every run at
the same configuration: 4/20 before, 16/40 after — z = 1.55, **not significant at 0.05**.
The rename probably helped. 210 calls do not prove it.

**Reading a failure mode off three samples.** The harness stores only **three failure
samples per case**. At n=10 that was 3 of 5 failures — effectively all of them, so
"every 'I don't have a tool' failure is gone" looked like a real qualitative shift. At
n=30 it is 3 of 19, and the old failure shape was plainly back. A sampling artifact read
as a finding.

> **Rules of thumb.** Use n≥30 for any claim about causation. Pool runs at identical
> configuration rather than comparing single runs. Treat the failure samples as
> illustrative, never as a census.

---

## 8. Current state

`tool-select`, n=30, 210 calls, real catalogue:

```
agenda_not_fanout         30/30
create_reminder_acts      30/30
complete_looks_first      29/30      was 2/10
search_not_single_module  30/30
rent_status_tool          30/30
no_tool_applies           30/30
habit_log_acts            11/30      still weak
                          ------
overall                    90.5%     was 74.3%
```

The catalogue has since grown to 50 (`health_findings`, `health_baselines`), so this
baseline is **no longer directly comparable** — re-baseline before the next comparison.

---

## 9. The training question

**Nothing has been fine-tuned.** That is a conclusion, not a gap.

Every failure diagnosed so far turned out to be tool design — a signature demanding a
GUID, a name losing a match — and each was fixed in code for a fraction of the cost of a
training run, with a deterministic guarantee a fine-tune could not offer.

What exists if it becomes justified:

- **`ToolCallRecorder`** captures real tool calls with arguments and results.
- **`San.Evals`** gives training an acceptance criterion. Without it, "the fine-tune
  helped" would be the n=1 mistake again with a GPU bill attached.

The one honest candidate today is `habit_log_acts` at 11/30. Its failures are uniform
and specific: the model refuses to call `habit_checkin` without first confirming the
habit exists — **even though the description now tells it explicitly that a wrong name
returns the list and not to ask.** It does not believe the instruction. That is a
behavioural problem rather than a code one, which is the first real argument for
training this project has produced.

Before spending on it, two cheaper things remain untried: a `web_search` tool (the only
fix for the `knowledge` category, which is a cutoff problem, not a prompt one) and
measuring whether the guard's nudge already recovers these failures in production —
the eval is single-turn and cannot see that.
