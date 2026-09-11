import { QueryClientProvider, useQuery } from '@tanstack/react-query';
import { makeModuleQueryClient } from '../services/moduleQuery';
import { authHeaders } from '../services/auth';
import { moduleApi } from '../services/apiHost';
import '../styles/modules.css';
import '../styles/insight.css';

// Vitara Insight — what the health data MEANS, as opposed to what it says.
//
// Deliberately a separate tab from Vitara rather than another panel inside it. Vitara
// answers "how did I sleep": last night's numbers, straight from the ring. This answers
// "is anything off": the same numbers measured against sixty days of the user's own
// history. They are different questions, they are computed by a different process, and
// putting them on one screen made the derived figures read as just more readings.
const API = moduleApi(5110);
const qc  = makeModuleQueryClient(5 * 60_000);

// ── Types ────────────────────────────────────────────────────────────────────

interface Finding {
  key: string;
  type: string;
  metric: string;
  severity: string;
  summary: string;
  daysRunning: number;
}

// status is a SENTENCE, not a flag, and it is read before anything else here for the
// same reason San is told to read it first: an empty findings list can mean the
// analysis has never run, and that is not the same as nothing being wrong.
interface Summary {
  status: string;
  latestDataDay: string | null;
  computedThrough: string | null;
  daysBehind: number | null;
  activeFindings: number;
  bySeverity: Record<string, number>;
  findings: Finding[];
}

interface Baseline {
  metric: string;
  signature: string;
  mean: number;
  stdDev: number;
  median: number;
  p25: number;
  p75: number;
  n: number;
  isValid: boolean;
  windowDays: number;
  regimeStart: string | null;
}

interface BaselineSet {
  computedOn: string | null;
  daysAgo?: number;
  baselines: Baseline[];
}

// ── Fetching ─────────────────────────────────────────────────────────────────

async function get<T>(path: string): Promise<T> {
  const res = await fetch(`${API}${path}`, { headers: authHeaders() });
  if (!res.ok) throw new Error(`${res.status} ${res.statusText}`);
  return res.json() as Promise<T>;
}

// ── Presentation ─────────────────────────────────────────────────────────────

const SEVERITY_ORDER: Record<string, number> = { high: 3, notable: 2, info: 1 };

const METRIC_LABELS: Record<string, string> = {
  resting_hr: 'Resting heart rate',
  hrv_rmssd: 'HRV (RMSSD)',
  skin_temp_deviation: 'Skin temperature',
  total_sleep_minutes: 'Total sleep',
  deep_sleep_minutes: 'Deep sleep',
  rem_sleep_minutes: 'REM sleep',
  sleep_score: 'Sleep score',
  readiness_score: 'Readiness',
  activity_score: 'Activity score',
  breathing_rate: 'Breathing rate',
  spo2_average: 'SpO₂',
  steps: 'Steps',
  active_calories: 'Active calories',
  systolic_bp: 'Systolic BP',
  diastolic_bp: 'Diastolic BP',
  weight_kg: 'Weight',
  glucose: 'Glucose',
  vo2_max: 'VO₂ max',
  sleep_debt: 'Sleep debt',
  acwr: 'Training load ratio',
};

const label = (metric: string) => METRIC_LABELS[metric] ?? metric.replace(/_/g, ' ');

const TYPE_LABELS: Record<string, string> = {
  deviation: 'Off baseline',
  early_illness: 'Possible illness',
  regime_change: 'New normal',
  strain_risk: 'Strain',
  drift: 'Drifting',
  staleness: 'Missing data',
  lab_anchor: 'Lab result',
};

// ── Findings ─────────────────────────────────────────────────────────────────

function StatusBanner({ s }: { s: Summary }) {
  // Three states with genuinely different meanings, and the middle one is the trap:
  // nothing computed looks identical to nothing wrong unless it is said out loud.
  const kind =
    s.computedThrough === null ? 'unrun'
    : (s.daysBehind ?? 0) >= 3 ? 'stale'
    : 'current';

  return (
    <div className={`insight-status insight-status-${kind}`}>
      <span className="insight-status-dot" aria-hidden="true" />
      <div>
        <p className="insight-status-text">{s.status}</p>
        <p className="insight-status-meta">
          {s.latestDataDay
            ? <>Newest reading {s.latestDataDay}</>
            : <>No readings recorded yet</>}
          {s.computedThrough && <> · analysed through {s.computedThrough}</>}
        </p>
      </div>
    </div>
  );
}

function FindingCard({ f }: { f: Finding }) {
  return (
    <li className={`insight-finding sev-${f.severity}`}>
      <div className="insight-finding-head">
        <span className="insight-type">{TYPE_LABELS[f.type] ?? f.type}</span>
        <span className="insight-metric">{label(f.metric)}</span>
        {/* Day count is the difference between "your resting HR is up this morning"
            and something worth acting on, so it gets its own chip rather than being
            buried in the sentence. */}
        {f.daysRunning > 1 && <span className="insight-days">day {f.daysRunning}</span>}
      </div>
      <p className="insight-summary">{f.summary}</p>
    </li>
  );
}

function Findings() {
  const { data, isLoading, error } = useQuery({
    queryKey: ['insight-summary'],
    queryFn: () => get<Summary>('/api/health/summary'),
  });

  if (isLoading) return <p className="module-muted">Loading analysis…</p>;
  if (error) return <p className="module-error">Insight is not reachable ({String(error)}).</p>;
  if (!data) return null;

  const sorted = [...data.findings].sort(
    (a, b) => (SEVERITY_ORDER[b.severity] ?? 0) - (SEVERITY_ORDER[a.severity] ?? 0)
             || b.daysRunning - a.daysRunning,
  );

  return (
    <section className="module-section">
      <StatusBanner s={data} />

      {sorted.length === 0 ? (
        <p className="module-muted">
          {data.computedThrough
            ? 'Nothing is outside your normal range.'
            : 'Nothing has been checked yet — this will populate once the analysis runs.'}
        </p>
      ) : (
        <ul className="insight-findings">
          {sorted.map(f => <FindingCard key={f.key} f={f} />)}
        </ul>
      )}
    </section>
  );
}

// ── Baselines ────────────────────────────────────────────────────────────────

function Baselines() {
  const { data, isLoading, error } = useQuery({
    queryKey: ['insight-baselines'],
    queryFn: () => get<BaselineSet>('/api/health/baselines'),
  });

  if (isLoading) return <p className="module-muted">Loading baselines…</p>;
  if (error) return null;
  if (!data || data.baselines.length === 0) {
    return (
      <section className="module-section">
        <h2 className="module-h2">What's normal for you</h2>
        <p className="module-muted">No baselines computed yet.</p>
      </section>
    );
  }

  // Valid first. An invalid baseline is still shown rather than hidden -- "not enough
  // data yet" is a real answer, and omitting the metric looks identical to the metric
  // not existing -- but it should not sit above the ones that mean something.
  const rows = [...data.baselines].sort(
    (a, b) => Number(b.isValid) - Number(a.isValid) || a.metric.localeCompare(b.metric),
  );

  return (
    <section className="module-section">
      <h2 className="module-h2">What's normal for you</h2>
      <p className="module-muted insight-sub">
        Computed over {rows[0]?.windowDays ?? 60} days, excluding illness, travel and
        device changes. A metric needs 21 readings before its numbers mean anything.
      </p>

      <div className="insight-table-wrap">
        <table className="insight-table">
          <thead>
            <tr>
              <th>Metric</th>
              <th className="num">Typical</th>
              <th className="num">Spread</th>
              <th className="num">Range (p25–p75)</th>
              <th className="num">n</th>
              <th>Status</th>
            </tr>
          </thead>
          <tbody>
            {rows.map(b => (
              <tr key={`${b.metric}:${b.signature}`} className={b.isValid ? '' : 'insight-invalid'}>
                <td>
                  {label(b.metric)}
                  {b.signature && <span className="insight-sig">{b.signature}</span>}
                </td>
                <td className="num">{b.median.toFixed(1)}</td>
                <td className="num">±{b.stdDev.toFixed(1)}</td>
                <td className="num">{b.p25.toFixed(1)} – {b.p75.toFixed(1)}</td>
                <td className="num">{b.n}</td>
                <td>
                  {b.isValid
                    ? <span className="insight-ok">valid</span>
                    : <span className="insight-thin">needs {Math.max(0, 21 - b.n)} more</span>}
                  {b.regimeStart && <span className="insight-regime">since {b.regimeStart}</span>}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </section>
  );
}

// ── Page ─────────────────────────────────────────────────────────────────────

function InsightPage() {
  return (
    <div className="module-page insight-page">
      <header className="module-header">
        <h1 className="module-h1">Insight</h1>
        <p className="module-sub">
          What your health data means, measured against your own history — not a
          population average.
        </p>
      </header>

      <Findings />
      <Baselines />
    </div>
  );
}

export default function InsightModule() {
  return (
    <QueryClientProvider client={qc}>
      <InsightPage />
    </QueryClientProvider>
  );
}
