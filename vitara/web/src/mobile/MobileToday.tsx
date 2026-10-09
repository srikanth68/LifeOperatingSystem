import type { ReactNode } from 'react';
import { useQuery } from '@tanstack/react-query';
import { moduleApi } from '../services/apiHost';
import { vitaraHeaders, profileQuery, getProfile, useProfileId } from '../services/profile';
import { formatClock, zonedNow } from '../services/timezone';
import { greeting } from '../pages/VitaraModule';
import type { Dashboard, Sleep, Activity, Readiness, OuraStatus } from '../pages/VitaraModule';
import { Rings } from './Rings';
import type { RingItem, RingMode } from './Rings';
import { UsualSpark, vsUsual, dateLine, OutOfReach } from './Parts';
import { clamp, ease, useReveal } from './motion';
import { avg, fmtMin, oneNightPerDay } from './stats';

// The home screen of the phone app.
//
// Read top to bottom the way a morning goes: how are you (three rings and one sentence), what
// moved (cards you swipe through, each against YOUR usual), what is likely tomorrow, anything
// that needs a look, and last night. Every block asks for its own data, so a slow or missing one
// leaves a gap in that block and nothing else.
//
// Honesty rules the design set and this keeps: an empty ring is a dashed track, never a zero;
// stale data says it is stale; a missing sensor is named, with a way to fix it; and nothing is
// red for being below your usual.

const VITARA = moduleApi(5100);
const INSIGHT = moduleApi(5110);

const get = <T,>(url: string): Promise<T> =>
  fetch(url, { headers: vitaraHeaders() }).then(r => {
    if (!r.ok) throw new Error(String(r.status));
    return r.json() as Promise<T>;
  });

interface Forecast { value: number; low: number; high: number; method: 'model' | 'today' | 'none' }
interface Finding { key: string; metric: string; severity: string; summary: string; daysRunning: number }
interface InsightSummary { computedThrough: string | null; activeFindings: number; findings: Finding[] }

export type TodayGo = (target: 'sleep' | 'recovery' | 'move' | 'insight') => void;

const partOfDay = () => {
  const h = zonedNow().getHours();
  return h < 5 ? 'Good night' : h < 12 ? 'Good morning' : h < 18 ? 'Good afternoon' : 'Good evening';
};

export function MobileToday({ status, go, tick, header }: {
  status?: OuraStatus;
  go: TodayGo;
  tick: number;                       // bumped by pull-to-refresh: replays the draw-in
  header: (greetingLine: string, eyebrow: string) => ReactNode;
}) {
  const person = useProfileId();
  const t = useReveal(`${person}:${tick}`);
  const k = ease(clamp(t / 0.85));    // the count-up clock for numbers

  const dash = useQuery<Dashboard>({ queryKey: ['dashboard'], queryFn: () => get(`${VITARA}/api/dashboard`), refetchInterval: 60_000 });
  const sleepQ = useQuery<Sleep[]>({ queryKey: ['sleep', 14], queryFn: () => get(`${VITARA}/api/sleep?days=14`) });
  const actQ = useQuery<Activity[]>({ queryKey: ['activity', 14], queryFn: () => get(`${VITARA}/api/activity?days=14`) });
  const readyQ = useQuery<Readiness[]>({ queryKey: ['readiness', 14], queryFn: () => get(`${VITARA}/api/readiness?days=14`) });
  const forecastQ = useQuery<{ readiness: Forecast | null }>({ queryKey: ['forecasts'], queryFn: () => get(`${INSIGHT}/api/health/forecast`), retry: false });
  const summaryQ = useQuery<InsightSummary>({ queryKey: ['insight-summary'], queryFn: () => get(`${INSIGHT}/api/health/summary`), retry: false });
  const nameQ = useQuery({ queryKey: ['profile-name', person], queryFn: () => getProfile(person), retry: false, staleTime: 5 * 60_000 });

  const d = dash.data;
  const firstName = nameQ.data?.name?.trim().split(/\s+/)[0];
  const hello = firstName ? `${partOfDay()}, ${firstName}` : partOfDay();
  const eyebrow = dateLine();

  if (dash.isPending) return <>{header(hello, eyebrow)}<div className="vm-skel" aria-busy="true" /></>;
  if (!d) return <>{header(hello, eyebrow)}<OutOfReach onRetry={() => { void dash.refetch(); }} /></>;

  // Last good data is on screen while the server cannot be reached: say so, and say since when.
  const offline = dash.isError || (typeof navigator !== 'undefined' && navigator.onLine === false);
  const mode: RingMode = offline ? 'stale' : 'live';
  const asOf = dash.dataUpdatedAt ? formatClock(new Date(dash.dataUpdatedAt)) : '';

  const sleepScore = d.sleep?.score ?? null;
  const readiness = d.readiness?.score ?? null;
  const activityScore = d.activity?.score ?? null;
  const nothingYet = sleepScore == null && readiness == null && activityScore == null && !d.sleep && !d.activity;

  const nights = oneNightPerDay(sleepQ.data ?? []);
  const days = [...(actQ.data ?? [])].sort((a, b) => a.day.localeCompare(b.day));
  const ready = [...(readyQ.data ?? [])].sort((a, b) => a.day.localeCompare(b.day));
  const usual = d.weeklyAvg;

  const rings: RingItem[] = [
    { key: 'sleep', label: 'Sleep', value: sleepScore == null ? null : sleepScore / 100, hue: 'sleep', mode: sleepScore == null ? 'empty' : mode },
    { key: 'recovery', label: 'Recovery', value: readiness == null ? null : readiness / 100, hue: 'recovery', mode: readiness == null ? 'empty' : mode },
    { key: 'move', label: 'Activity', value: activityScore == null ? null : activityScore / 100, hue: 'activity', mode: activityScore == null ? 'empty' : mode },
  ];
  const legend = [
    { key: 'sleep', label: 'Sleep', color: 'var(--sleep)', score: sleepScore },
    { key: 'recovery', label: 'Recovery', color: 'var(--teal)', score: readiness },
    { key: 'move', label: 'Activity', color: 'var(--gold)', score: activityScore },
  ] as const;

  const { line, sub } = greeting(d);
  const readinessDiff = readiness != null && usual?.readinessScore != null ? Math.round(readiness - usual.readinessScore) : null;
  const lastNight = nights[nights.length - 1];
  const forecast = forecastQ.data?.readiness ?? null;
  const lead = summaryQ.data?.findings?.[0];

  const missingCards = nothingYet;
  const cards: CardSpec[] = [
    {
      label: 'HRV', value: d.sleep?.hrv != null ? String(Math.round(d.sleep.hrv * k)) : null, unit: 'ms',
      vs: vsUsual(d.sleep?.hrv, usual?.hrv, ' ms', { up: 'above your usual', down: 'below your usual' }),
      data: nights.map(n => n.avgHrv ?? null), usual: usual?.hrv, accent: 'var(--teal)', needs: 'sensor', go: 'recovery',
    },
    {
      label: 'Resting heart rate', value: d.readiness?.restingHr != null ? String(Math.round(d.readiness.restingHr * k)) : null, unit: 'bpm',
      vs: vsUsual(d.readiness?.restingHr, usual?.rhr, ' bpm', { up: 'higher than usual', down: 'calmer than usual' }),
      data: ready.map(r => r.restingHeartRate ?? null), usual: usual?.rhr, accent: 'var(--teal)', needs: 'sensor', go: 'recovery',
    },
    {
      label: 'Time asleep', value: d.sleep ? fmtMin(d.sleep.totalMinutes * k) : null, unit: '',
      vs: d.sleep && nights.length > 1 ? sleepVsUsual(d.sleep.totalMinutes, nights.slice(0, -1)) : null,
      data: nights.map(n => n.totalSleepMinutes), usual: avg(nights.slice(0, -1).map(n => n.totalSleepMinutes)), accent: 'var(--sleep)', needs: 'night', go: 'sleep',
    },
    {
      label: 'Steps', value: d.activity?.steps != null ? Math.round(d.activity.steps * k).toLocaleString('en-US') : null, unit: '',
      vs: vsUsual(d.activity?.steps, usual?.steps, '', { up: 'more than usual', down: 'fewer than usual' }, 0.04),
      data: days.map(a => a.steps), usual: usual?.steps, accent: 'var(--gold)', needs: 'day', go: 'move',
    },
  ];

  return (
    <div className="vm-today">
      {header(hello, eyebrow)}

      {offline && (
        <div className="vm-tile vm-dashed vm-offline" role="status">
          <i className="vm-dashed-ring" aria-hidden="true" />
          <div>
            <b>Data out of reach</b>
            <p>Showing what we had{asOf ? ` at ${asOf}` : ''}. We&apos;ll catch up when you&apos;re back online.</p>
          </div>
          <button type="button" onClick={() => { void dash.refetch(); }}>Retry</button>
        </div>
      )}

      {/* ── The rings ─────────────────────────────────────────────────── */}
      <section className="vm-hero">
        <Rings
          items={rings} size={256} stroke={18} gap={6} t={t}
          onPick={key => go(key as 'sleep' | 'recovery' | 'move')}
          center={readiness != null
            ? (
              <>
                <span className="vm-eyebrow-s">Readiness</span>
                <b className="vm-hero-num">{Math.round(readiness * k)}</b>
                {readinessDiff != null && (
                  <span className="vm-hero-vs">
                    {readinessDiff === 0 ? '≈ your usual' : `${readinessDiff > 0 ? '↑' : '↓'} ${Math.abs(readinessDiff)} vs usual`}
                  </span>
                )}
              </>
            )
            : (
              <>
                <span className="vm-hero-title">{nothingYet ? 'Learning your usual' : 'Readiness needs recovery data'}</span>
                <span className="vm-hero-sub">{nothingYet ? 'Your first nights set the baseline' : 'Link a sensor below'}</span>
              </>
            )}
        />

        <ul className="vm-legend">
          {legend.map(l => (
            <li key={l.key}>
              <button type="button" onClick={() => go(l.key)}>
                <span><i style={{ background: l.color }} aria-hidden="true" />{l.label}</span>
                <b>{l.score != null ? Math.round(l.score) : nothingYet ? 'Learning' : 'Not linked'}</b>
              </button>
            </li>
          ))}
        </ul>

        <p className="vm-voice">{nothingYet ? 'Welcome. Wear your sensor tonight — after about a week, Vitara will know what your usual looks like.' : line}</p>
        {!nothingYet && sub && <p className="vm-voice-sub">{sub}</p>}
      </section>

      {/* ── Swipeable cards ───────────────────────────────────────────── */}
      <section aria-label="Your body today">
        <p className="vm-label"><span>Your body today</span><em>vs your usual</em></p>
        <div className="vm-rail" role="list">
          {cards.map(c => (
            <MetricCard key={c.label} c={c} t={t} missing={missingCards || c.value == null} offline={offline} asOf={asOf}
                        onClick={() => go(c.go)} />
          ))}
        </div>
      </section>

      {/* ── Last night ────────────────────────────────────────────────── */}
      {d.sleep && (
        <button type="button" className="vm-tile vm-tap" onClick={() => go('sleep')}>
          <p className="vm-label"><span>Last night</span>
            <em>{lastNight ? `${formatClock(lastNight.bedtimeStart)} – ${formatClock(lastNight.bedtimeEnd)} ›` : ''}</em></p>
          <Stages s={d.sleep} awake={lastNight?.awakeMinutes} t={t} />
        </button>
      )}

      {/* ── Tomorrow ──────────────────────────────────────────────────── */}
      {forecast && forecast.method !== 'none' && (
        <button type="button" className="vm-tile vm-tap vm-forecast-card" onClick={() => go('insight')}>
          <p className="vm-label"><span style={{ color: 'var(--blue)' }}>Tomorrow</span><em>Forecast</em></p>
          <div className="vm-forecast-row">
            <div className="vm-forecast-word">{forecastWord(forecast.value, readiness)}</div>
            <div className="vm-forecast-num"><small>recovery</small><b>~{Math.round(forecast.value)}</b></div>
          </div>
          <RangeTrack low={forecast.low} high={forecast.high} value={forecast.value} />
          <p className="vm-fine">
            {forecast.method === 'today' ? 'Same as today: nothing here beat that.' : 'From a model fitted on your own history.'}
          </p>
        </button>
      )}

      {/* ── A heads-up, only when there is one ───────────────────────── */}
      {lead && (
        <button type="button" className="vm-tile vm-tap" onClick={() => go('insight')}>
          <p className="vm-label">
            <span>Top finding</span>
            <span className={`vm-sev sev-${lead.severity}`}>
              <i aria-hidden="true" />{lead.severity === 'high' ? 'Act on it' : 'Keep an eye on'}
            </span>
          </p>
          <p className="vm-finding">{firstSentence(lead.summary)}</p>
          <p className="vm-fine">
            Day {lead.daysRunning}
            {summaryQ.data && summaryQ.data.activeFindings > 1 ? ` · ${summaryQ.data.activeFindings - 1} more in Insight` : ''}
          </p>
          <p className="vm-link">See the pattern →</p>
        </button>
      )}

      {/* ── Honest empty states ──────────────────────────────────────── */}
      {nothingYet && (
        <div className="vm-tile vm-dashed vm-learn">
          <p className="vm-label"><span>Getting to know you</span></p>
          <div className="vm-nights" aria-hidden="true">{Array.from({ length: 7 }, (_, i) => <i key={i} className={i === 0 ? 'on' : ''} />)}</div>
          <p>Your usual takes about 7 nights to learn. Forecasts and patterns arrive after about 14 days.</p>
        </div>
      )}

      {status && !status.linked && (
        <div className="vm-tile vm-dashed vm-link-card">
          <div className="vm-link-head"><i aria-hidden="true">+</i><b>No sensor linked</b></div>
          <p>
            {readiness == null
              ? 'Recovery and readiness need overnight heart data from a ring or watch.'
              : 'New readings stop arriving until a sensor is linked again. What you see here is the last it sent.'}
          </p>
          <a className="vm-cta" href={`${VITARA}/api/oura/auth${profileQuery()}`} target="_blank" rel="noreferrer">Link a sensor</a>
        </div>
      )}
      {status?.linked && status.expired && (
        <div className="vm-tile vm-dashed vm-link-card">
          <div className="vm-link-head"><i aria-hidden="true">!</i><b>Your sensor needs reconnecting</b></div>
          <p>The link expired, so nothing new is arriving.</p>
          <a className="vm-cta" href={`${VITARA}/api/oura/auth${profileQuery()}`} target="_blank" rel="noreferrer">Reconnect</a>
        </div>
      )}

      <p className="vm-end">
        {summaryQ.data?.computedThrough ? `Insight analysed through ${summaryQ.data.computedThrough}` : 'Pull down to refresh'}
      </p>
    </div>
  );
}

// ── Pieces ────────────────────────────────────────────────────────────────────

interface CardSpec {
  label: string;
  value: string | null;
  unit: string;
  vs: { arrow: string; text: string } | null;
  data: (number | null)[];
  usual?: number | null;
  accent: string;
  needs: 'sensor' | 'night' | 'day';
  go: 'sleep' | 'recovery' | 'move';
}

function MetricCard({ c, t, missing, offline, asOf, onClick }: {
  c: CardSpec; t: number; missing: boolean; offline: boolean; asOf: string; onClick: () => void;
}) {
  if (missing) {
    return (
      <button type="button" role="listitem" className="vm-metric vm-dashed is-empty" onClick={onClick}>
        <span className="vm-metric-head"><b>{c.label}</b></span>
        <span className="vm-rule" aria-hidden="true" />
        <span className="vm-metric-miss">{c.needs === 'sensor' ? 'Needs a sensor' : 'Nothing yet'}</span>
        <span className="vm-metric-miss-sub">No value shown, never a 0</span>
      </button>
    );
  }
  return (
    <button type="button" role="listitem" className="vm-metric" onClick={onClick} style={offline ? { opacity: 0.8 } : undefined}>
      <span className="vm-metric-head"><b>{c.label}</b><em>{offline && asOf ? `as of ${asOf}` : 'today'}</em></span>
      <span className="vm-metric-value"><b>{c.value}</b>{c.unit && <small>{c.unit}</small>}</span>
      <span className="vm-metric-vs">
        {c.vs
          ? <><i style={{ color: c.accent }} aria-hidden="true">{c.vs.arrow}</i>{c.vs.text}</>
          : 'no comparison yet'}
      </span>
      <UsualSpark data={c.data} usual={c.usual} color={c.accent} t={t} />
      <span className="vm-metric-foot"><span>14 days</span><span>- - usual</span></span>
    </button>
  );
}

// Deep, REM, light and awake as one strip. This is the proportion of the night, not its timeline:
// the order of the stages through the night is on the Sleep screen.
function Stages({ s, awake, t }: { s: NonNullable<Dashboard['sleep']>; awake?: number; t: number }) {
  // The dashboard's efficiency is already a percentage (94), not a fraction (0.94).
  const awakeMin = awake ?? Math.max(0, Math.round(s.totalMinutes / Math.max(s.efficiency / 100, 0.01) - s.totalMinutes));
  const parts = [
    { name: 'Deep', min: s.deepMinutes, c: 'var(--deep)' },
    { name: 'REM', min: s.remMinutes, c: 'var(--rem)' },
    { name: 'Light', min: s.lightMinutes, c: 'var(--lt)' },
    { name: 'Awake', min: awakeMin, c: 'var(--awake)' },
  ];
  const grow = ease(clamp((t - 0.1) / 0.8));
  return (
    <>
      <div className="vm-strip" aria-hidden="true" style={{ clipPath: `inset(0 ${((1 - grow) * 100).toFixed(1)}% 0 0)` }}>
        {parts.map(p => <span key={p.name} style={{ flex: `${p.min} 0 0`, background: p.c }} />)}
      </div>
      <div className="vm-stage-key">
        {parts.map(p => (
          <div key={p.name}>
            <span><i style={{ background: p.c }} />{p.name}</span>
            <b>{fmtMin(p.min)}</b>
          </div>
        ))}
      </div>
      <p className="vm-fine" style={{ marginTop: '0.6rem' }}>{fmtMin(s.totalMinutes)} asleep · {Math.round(s.efficiency)}% efficiency</p>
    </>
  );
}

// A track with the likely range as a band and the forecast as a dot.
function RangeTrack({ low, high, value }: { low: number; high: number; value: number }) {
  const p = (v: number) => `${clamp(v / 100) * 100}%`;
  return (
    <div className="vm-range" aria-label={`Likely between ${Math.round(low)} and ${Math.round(high)}`}>
      <i className="vm-range-track" />
      <i className="vm-range-band" style={{ left: p(low), width: `${clamp((high - low) / 100) * 100}%` }} />
      <i className="vm-range-dot" style={{ left: p(value) }} />
      <span style={{ left: p(low) }}>{Math.round(low)}</span>
      <span style={{ left: p(high) }}>{Math.round(high)}</span>
    </div>
  );
}

const forecastWord = (value: number, today: number | null) => {
  if (today == null) return 'Likely';
  const diff = value - today;
  return Math.abs(diff) <= 4 ? 'Likely steady' : diff > 0 ? 'Likely better' : 'Likely lower';
};

// A finding's summary is a paragraph; the home screen carries only its first sentence.
const firstSentence = (text: string) => {
  const m = /^.*?[.!?](?=\s+[A-Z0-9]|$)/s.exec(text.trim());
  return m ? m[0] : text;
};

// Time asleep against the average of the nights before it.
function sleepVsUsual(minutes: number, before: Sleep[]) {
  const usual = avg(before.map(n => n.totalSleepMinutes));
  if (usual == null) return null;
  const diff = Math.round(minutes - usual);
  if (Math.abs(diff) <= 5) return { arrow: '≈', text: 'about your usual' };
  return diff > 0 ? { arrow: '↑', text: `${diff} min more than usual` } : { arrow: '↓', text: `${-diff} min less than usual` };
}
