import { useQuery, useQueryClient } from '@tanstack/react-query';
import { moduleApi } from '../services/apiHost';
import { vitaraHeaders, profileQuery } from '../services/profile';
import { formatClock } from '../services/timezone';
import { Chip, Spark, Delta } from '../components/health/HealthKit';
import { RangeBar } from '../components/health/InsightCharts';
import { greeting } from '../pages/VitaraModule';
import type { Dashboard, Sleep, Activity, Readiness, OuraStatus } from '../pages/VitaraModule';
import { TripleRing } from './TripleRing';
import type { RingSpec } from './TripleRing';

// The home screen of the phone app.
//
// Read top to bottom the way a morning goes: how are you (three rings and one sentence), what
// moved (a row of cards you swipe through), what is likely tomorrow, anything that needs a look,
// and last night. Every block asks for its own data, so a slow or missing one leaves a gap in
// that block and nothing else, and every number is measured against YOUR own recent average.

const VITARA = moduleApi(5100);
const INSIGHT = moduleApi(5110);

const get = <T,>(url: string): Promise<T> =>
  fetch(url, { headers: vitaraHeaders() }).then(r => {
    if (!r.ok) throw new Error(String(r.status));
    return r.json() as Promise<T>;
  });

const fmtMin = (m: number) => { const h = Math.floor(m / 60), min = Math.round(m % 60); return h > 0 ? `${h}h ${min}m` : `${min}m`; };
const word = (s?: number | null) => s == null ? 'no score yet' : s >= 85 ? 'optimal' : s >= 70 ? 'good' : s >= 50 ? 'fair' : 'rest';

interface Forecast { value: number; low: number; high: number; method: 'model' | 'today' | 'none' }
interface Finding { key: string; metric: string; severity: string; summary: string; daysRunning: number }
interface InsightSummary { computedThrough: string | null; activeFindings: number; findings: Finding[] }

export type TodayGo = (target: 'sleep' | 'recovery' | 'move' | 'insight') => void;

export function MobileToday({ status, go }: { status?: OuraStatus; go: TodayGo }) {
  const dash = useQuery<Dashboard>({ queryKey: ['dashboard'], queryFn: () => get(`${VITARA}/api/dashboard`), refetchInterval: 60_000 });
  const sleepQ = useQuery<Sleep[]>({ queryKey: ['sleep', 7], queryFn: () => get(`${VITARA}/api/sleep?days=7`) });
  const actQ = useQuery<Activity[]>({ queryKey: ['activity', 7], queryFn: () => get(`${VITARA}/api/activity?days=7`) });
  const readyQ = useQuery<Readiness[]>({ queryKey: ['readiness', 14], queryFn: () => get(`${VITARA}/api/readiness?days=14`) });
  const forecastQ = useQuery<{ readiness: Forecast | null }>({ queryKey: ['forecasts'], queryFn: () => get(`${INSIGHT}/api/health/forecast`), retry: false });
  const summaryQ = useQuery<InsightSummary>({ queryKey: ['insight-summary'], queryFn: () => get(`${INSIGHT}/api/health/summary`), retry: false });

  const d = dash.data;
  if (dash.isPending) return <div className="vm-skel" aria-busy="true" />;
  if (dash.isError || !d) {
    return (
      <div className="vm-card vm-note">
        <b>Can&apos;t reach your data.</b>
        <p>Check you are on the same network as your server, then pull down to try again.</p>
      </div>
    );
  }

  const { line, sub } = greeting(d);
  const sleepScore = d.sleep?.score ?? null;
  const readiness = d.readiness?.score ?? null;
  const activityScore = d.activity?.score ?? null;

  // One entry per night / day, oldest first, for the little trend lines.
  const nights = oneNightPerDay(sleepQ.data ?? []);
  const days = [...(actQ.data ?? [])].sort((a, b) => a.day.localeCompare(b.day));
  const ready = [...(readyQ.data ?? [])].sort((a, b) => a.day.localeCompare(b.day));

  const rings: RingSpec[] = [
    { key: 'sleep', label: 'Sleep', score: sleepScore, color: 'var(--hx-4)' },
    { key: 'recovery', label: 'Recovery', score: readiness, color: 'var(--hx-1)' },
    { key: 'move', label: 'Activity', score: activityScore, color: 'var(--hx-3)' },
  ];

  const lastNight = nights[nights.length - 1];
  const forecast = forecastQ.data?.readiness ?? null;
  const lead = summaryQ.data?.findings?.[0];

  return (
    <div className="vm-today">
      {status && !status.linked && (
        <a className="vm-banner" href={`${VITARA}/api/oura/auth${profileQuery()}`} target="_blank" rel="noreferrer">
          <span className="hx-dot bad" /> Ring not connected <b>Link →</b>
        </a>
      )}
      {status?.linked && status.expired && (
        <a className="vm-banner warn" href={`${VITARA}/api/oura/auth${profileQuery()}`} target="_blank" rel="noreferrer">
          <span className="hx-dot warn" /> Oura needs reconnecting <b>Fix →</b>
        </a>
      )}

      {/* ── The rings ─────────────────────────────────────────────────── */}
      <section className="vm-hero">
        <TripleRing
          rings={rings}
          onPick={k => go(k as 'sleep' | 'recovery' | 'move')}
          center={
            <>
              <b style={{ color: readiness == null ? 'var(--text3)' : undefined }}>{readiness ?? '—'}</b>
              <span>readiness</span>
            </>
          }
        />
        <ul className="vm-hero-key">
          {rings.map(r => (
            <li key={r.key}>
              <button type="button" onClick={() => go(r.key as 'sleep' | 'recovery' | 'move')}>
                <i style={{ background: r.color }} aria-hidden="true" />
                <span>{r.label}</span>
                <b>{r.score ?? '—'}</b>
                <em>{word(r.score)}</em>
              </button>
            </li>
          ))}
        </ul>
        <h2 className="vm-verdict">{line}</h2>
        <p className="vm-verdict-sub">{sub}</p>
      </section>

      {/* ── Swipeable cards ───────────────────────────────────────────── */}
      <div className="vm-rail" role="list" aria-label="Today's measures, swipe sideways">
        <MetricCard
          label="HRV" value={d.sleep?.hrv != null ? Math.round(d.sleep.hrv) : null} unit="ms"
          delta={<Delta value={d.sleep?.hrv} reference={d.weeklyAvg?.hrv} goodWhen="higher" unit=" ms" />}
          spark={nights.map(n => n.avgHrv ?? null)} color="var(--hx-2)" onClick={() => go('recovery')}
        />
        <MetricCard
          label="Resting HR" value={d.readiness?.restingHr != null ? Math.round(d.readiness.restingHr) : null} unit="bpm"
          delta={<Delta value={d.readiness?.restingHr} reference={d.weeklyAvg?.rhr} goodWhen="lower" unit=" bpm" />}
          spark={ready.map(r => r.restingHeartRate ?? null)} color="var(--hx-5)" onClick={() => go('recovery')}
        />
        <MetricCard
          label="Time asleep" value={d.sleep ? fmtMin(d.sleep.totalMinutes) : null}
          delta={d.sleep ? <span className="hx-delta flat">{Math.round(d.sleep.efficiency * 100)}% efficient</span> : undefined}
          spark={nights.map(n => n.totalSleepMinutes)} color="var(--hx-4)" onClick={() => go('sleep')}
        />
        <MetricCard
          label="Steps" value={d.activity?.steps != null ? d.activity.steps.toLocaleString() : null}
          delta={<Delta value={d.activity?.steps} reference={d.weeklyAvg?.steps} goodWhen="higher" />}
          spark={days.map(a => a.steps)} color="var(--hx-1)" kind="bar" onClick={() => go('move')}
        />
      </div>

      {/* ── Tomorrow ──────────────────────────────────────────────────── */}
      {forecast && forecast.method !== 'none' && (
        <section className="vm-card" onClick={() => go('insight')} role="button" tabIndex={0}>
          <p className="vm-eyebrow">Tomorrow · readiness</p>
          <div className="vm-forecast">
            <b>{Math.round(forecast.value)}</b>
            <span>usually {Math.round(forecast.low)}–{Math.round(forecast.high)}</span>
          </div>
          <RangeBar low={forecast.low} high={forecast.high} value={forecast.value} bounds={[0, 100]} unit="/100" />
          <p className="vm-fine">{forecast.method === 'today' ? 'Same as today: nothing here beat that.' : 'From a model fitted on your own history.'}</p>
        </section>
      )}

      {/* ── A heads-up, only when there is one ───────────────────────── */}
      {lead && (
        <section className={`vm-card vm-heads sev-${lead.severity}`} onClick={() => go('insight')} role="button" tabIndex={0}>
          <p className="vm-eyebrow">
            Worth a look
            <Chip tone={lead.severity === 'high' ? 'bad' : 'warn'}>
              {lead.severity === 'high' ? 'act on it' : 'keep an eye on'}
            </Chip>
          </p>
          <p className="vm-heads-text">{lead.summary}</p>
          <p className="vm-fine">
            Day {lead.daysRunning}
            {summaryQ.data && summaryQ.data.activeFindings > 1 ? ` · ${summaryQ.data.activeFindings - 1} more in Insight` : ''}
          </p>
        </section>
      )}

      {/* ── Last night ────────────────────────────────────────────────── */}
      {d.sleep && (
        <section className="vm-card" onClick={() => go('sleep')} role="button" tabIndex={0}>
          <p className="vm-eyebrow">Last night <span>{lastNight ? `${formatClock(lastNight.bedtimeStart)}–${formatClock(lastNight.bedtimeEnd)}` : ''}</span></p>
          <Stages s={d.sleep} />
        </section>
      )}

      <p className="vm-foot">
        {summaryQ.data?.computedThrough ? `Insight analysed through ${summaryQ.data.computedThrough}` : 'Pull down to refresh'}
      </p>
    </div>
  );
}

function MetricCard({ label, value, unit, delta, spark, color, kind = 'line', onClick }: {
  label: string;
  value: string | number | null;
  unit?: string;
  delta?: React.ReactNode;
  spark: (number | null)[];
  color: string;
  kind?: 'line' | 'bar';
  onClick: () => void;
}) {
  const missing = value == null;
  return (
    <button type="button" role="listitem" className={`vm-metric ${missing ? 'is-empty' : ''}`} onClick={onClick}>
      <span className="vm-metric-label">{label}</span>
      <span className="vm-metric-value">
        <b>{missing ? '—' : value}</b>{!missing && unit && <small>{unit}</small>}
      </span>
      <span className="vm-metric-delta">{missing ? 'Nothing yet' : delta}</span>
      <span className="vm-metric-spark"><Spark data={spark} color={color} kind={kind} height={30} /></span>
    </button>
  );
}

// Deep, REM, light and awake as one bar, with the words under it.
function Stages({ s }: { s: NonNullable<Dashboard['sleep']> }) {
  const total = s.deepMinutes + s.remMinutes + s.lightMinutes || 1;
  return (
    <>
      <div className="hx-stages" style={{ marginTop: '0.6rem' }}>
        <span style={{ width: `${s.deepMinutes / total * 100}%`, background: 'var(--hx-2)' }} />
        <span style={{ width: `${s.remMinutes / total * 100}%`, background: 'var(--hx-4)' }} />
        <span style={{ width: `${s.lightMinutes / total * 100}%`, background: 'var(--hx-6)' }} />
      </div>
      <div className="vm-stage-key">
        <span><i style={{ background: 'var(--hx-2)' }} />Deep {fmtMin(s.deepMinutes)}</span>
        <span><i style={{ background: 'var(--hx-4)' }} />REM {fmtMin(s.remMinutes)}</span>
        <span><i style={{ background: 'var(--hx-6)' }} />Light {fmtMin(s.lightMinutes)}</span>
      </div>
      <p className="vm-big-sleep">{fmtMin(s.totalMinutes)} <span>asleep</span></p>
    </>
  );
}

// A nap is not a night: keep the longest session per day, oldest first.
function oneNightPerDay(sessions: Sleep[]): Sleep[] {
  const best = new Map<string, Sleep>();
  for (const s of sessions) {
    const cur = best.get(s.day);
    if (!cur || s.totalSleepMinutes > cur.totalSleepMinutes) best.set(s.day, s);
  }
  return [...best.values()].sort((a, b) => a.day.localeCompare(b.day));
}

export function useRefreshAll() {
  const qc = useQueryClient();
  return () => qc.invalidateQueries();
}
