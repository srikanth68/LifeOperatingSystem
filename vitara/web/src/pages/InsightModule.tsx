import { useMemo, useState } from 'react';
import { QueryClientProvider, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { makeModuleQueryClient } from '../services/moduleQuery';
import { Shell, Info, Chip, Row } from '../components/health/HealthKit';
import { DonutSplit, Legend, SleepDial, RangeBar, RangeGauge, StackBar } from '../components/health/InsightCharts';
import type { LabReading } from '../components/health/InsightCharts';
import { RelationshipWeb, PatternRing, Bead, TrialTrack, ShiftStrip, Tug } from '../components/health/PatternCharts';
import type { ReactNode } from 'react';
import { vitaraHeaders, useProfileKey } from '../services/profile';
import { moduleApi } from '../services/apiHost';
import '../styles/modules.css';
import '../styles/insight.css';

import { addDays, todayInTz } from '../services/timezone';
// Vitara Insight — what the health data MEANS, as opposed to what it says.
//
// Deliberately a separate tab from Vitara rather than another panel inside it. Vitara
// answers "how did I sleep": last night's numbers, straight from the ring. This answers
// "is anything off": the same numbers measured against sixty days of the user's own
// history. They are different questions, they are computed by a different process, and
// putting them on one screen made the derived figures read as just more readings.
//
// Laid out as a reading order: the verdict first (is anything off, and can that answer be
// trusted), then the estimated biological age with the reasons behind it, what is
// currently being flagged, where each metric sits against its own normal today, and only
// then the slower-moving relationships and the raw baseline numbers.
const API = moduleApi(5110);
const VITARA = moduleApi(5100);

// ── Types ────────────────────────────────────────────────────────────────────

interface Finding {
  key: string;
  type: string;
  metric: string;
  severity: string;
  summary: string;
  daysRunning: number;
  // Only on lab findings: the value and the range it was read against, so it can be drawn.
  lab?: LabReading | null;
}

interface Coverage {
  metricsWithNormal: number;
  stillLearning: number;
  stillLearningMetrics: string[];
  note: string;
}

interface Summary {
  status: string;
  latestDataDay: string | null;
  computedThrough: string | null;
  daysBehind: number | null;
  activeFindings: number;
  bySeverity: Record<string, number>;
  coverage?: Coverage;
  // How many of the active findings lead today. The rest are standing and unchanged,
  // listed rather than hidden -- see Surfacing on the server.
  surfacing?: { cap: number; held: number; note: string };
  findings: Finding[];
  standing?: Finding[];
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

interface Derived {
  metric: string;
  day: string;
  value: number;
}

interface Correlation {
  driver: string;
  outcome: string;
  lagDays: number;
  rho: number;
  direction: string;
  n: number;
  pValue: number;
  windowDays: number;
}

interface CorrelationSet {
  computedOn: string | null;
  caveat: string;
  method: { test: string; minPairedDays: number; minAbsRho: number; multipleComparisons: string };
  correlations: Correlation[];
}

interface ForecastEvidence {
  modelMae: number | null;
  persistenceMae: number;
  averageMae: number;
  skill: number | null;
  testedOnDays: number;
  modelWins: boolean;
  verdict: string;
}

interface Forecast {
  day: string;
  value: number;
  low: number;
  high: number;
  method: 'model' | 'today' | 'none';
  basis: string;
  evidence: ForecastEvidence;
}

interface BioSignature {
  version: number;
  generatedOn: string;
  confidence: { settled: number; learning: number; daysOfHistory: number; note: string };
  sleepClock: {
    bedtime: string; wake: string; bedtimeVariabilityMinutes: number;
    socialJetlagMinutes: number; nights: number; note: string;
  } | null;
  week: {
    byDay: Record<string, number>; best: string; worst: string;
    spread: number; weeks: number; note: string;
  } | null;
  recovery: { days: number | null; episodes: number; note: string };
  respondsTo: { driver: string; outcome: string; lagDays: number; rho: number; n: number }[];
  respondsToCaveat: string;
  tomorrow: { readiness: Forecast | null; restingHeartRate: Forecast | null };
}

interface Scenario {
  lever: string;
  delta: number;
  question: string;
  from: number | null;
  to: number | null;
  change: number | null;
  supported: boolean;
  answer: string;
}

interface Simulation {
  target: string;
  unit: string;
  caveat: string;
  scenarios: Scenario[];
}

interface Forecasts {
  readiness: Forecast | null;
  restingHeartRate: Forecast | null;
  hrv: Forecast | null;
  timeAsleep: Forecast | null;
}

// An audit of the software rather than of the body. Three states, never two: every
// other surface here renders "nothing is happening" and "I cannot see" identically,
// as silence, and those are opposite facts.
interface CapabilityRow {
  key: string;
  group: string;
  label: string;
  state: 'speaking' | 'quiet' | 'blind';
  says: string;
  needs: string[];
  everSaid: number;
  grade: string | null;
}

interface SelfCheck {
  asOf: string;
  speaking: number;
  quiet: number;
  blind: number;
  headline: string[];
  capabilities: CapabilityRow[];
  coverage: { metric: string; label: string; group: string; readings: number; last: string | null; daysSinceLast: number | null; baseline: string; state: string }[];
}

// Grading your own decisions. The verdict is often "cannot say", and those are the
// honest answers far more often than a result is.
interface Target { key: string; label: string; unit: string; group: string; better: string }

interface Evaluated {
  id: string;
  name: string;
  kind: string;
  dose: string | null;
  startedOn: string;
  endedOn: string | null;
  running: boolean;
  daysIn: number;
  targetMetric: string | null;
  targetLabel: string | null;
  verdict: string;
  statement: string;
  confidence: 'none' | 'low' | 'moderate';
  caveats: string[];
  earliestVerdict: string | null;
  before: { median: number; n: number; p25?: number; p75?: number } | null;
  after: { median: number; n: number; p25?: number; p75?: number } | null;
  change: number | null;
  rebound: { comparableWindows: number; median: number; p75: number } | null;
  alsoChanged: { metric: string; label: string; change: number; direction: string; detail: string }[];
  alsoChangedNote: string;
}

interface InterventionScan {
  runInDays: number;
  windowDays: number;
  interventions: Evaluated[];
  evidence: { grade: string; label: string; claim: string; caveat: string } | null;
}

// Several measures read together. The negatives are served too, and are usually the
// more useful half: "no metabolic pattern, and three of these eight are not being
// measured often enough to contribute" is the sentence that names the next blood test.
interface PatternComponent {
  metric: string;
  label: string;
  movement: 'unmeasured' | 'steady' | 'favourable' | 'unfavourable';
  detail: string;
  basis: string;
  counts: boolean;
  grade: 'A' | 'B' | 'C' | 'D' | null;
}

interface PatternRow {
  key: string;
  title: string;
  statement: string;
  interpretation: string;
  fires: boolean;
  heldBecause: string | null;
  moving: number;
  counted: number;
  unmeasured: number;
  components: PatternComponent[];
  wouldHelp: { metric: string; label: string; detail: string }[];
}

interface PatternScan {
  today: string;
  patterns: PatternRow[];
  evidence: { grade: string; label: string; claim: string; caveat: string } | null;
}

interface VisitBrief {
  generatedOn: string;
  scope: string;
  verdict: string;
  bring: { topic: string; what: string; since: string | null; severity: string; ask: string | null; lab?: LabReading | null }[];
  questions: string[];
  notLookedAt: string[];
  coverage: string;
  disclaimer: string;
}

interface IllnessEval {
  windowDays: number;
  daysEvaluated: number;
  episodeCount: number;
  caught: number;
  missed: number;
  falseAlarmCount: number;
  medianLeadDays: number | null;
  verdict: string;
  episodes: { start: string; end: string; caught: boolean; leadDays: number | null; signalStart: string | null; notes: string | null }[];
  falseAlarms: { start: string; end: string; days: number }[];
}

interface BioContribution {
  key: string;
  name: string;
  value: number;
  unit: string;
  years: number;
  weight: number;
}

interface BioAge {
  bioAge?: number | null;
  chronologicalAge: number;
  delta?: number | null;
  contributions?: BioContribution[];
  clamped?: boolean;
  label?: string;
  disclaimer?: string;
  method?: string;
  dataQuality: string;
  ageSource: string;
}

// ── Fetching ─────────────────────────────────────────────────────────────────

async function get<T>(url: string): Promise<T> {
  const res = await fetch(url, { headers: vitaraHeaders() });
  if (!res.ok) throw new Error(`${res.status} ${res.statusText}`);
  return res.json() as Promise<T>;
}

// ── Presentation helpers ─────────────────────────────────────────────────────

const METRIC_LABELS: Record<string, string> = {
  resting_hr: 'Resting heart rate',
  hrv_rmssd: 'HRV',
  hrv_sdnn: 'HRV (SDNN)',
  skin_temp_deviation: 'Skin temperature',
  total_sleep_minutes: 'Total sleep',
  deep_sleep_minutes: 'Deep sleep',
  rem_sleep_minutes: 'REM sleep',
  sleep_efficiency: 'Sleep efficiency',
  sleep_score: 'Sleep score',
  readiness_score: 'Readiness',
  activity_score: 'Activity score',
  breathing_rate: 'Breathing rate',
  spo2_average: 'SpO₂',
  steps: 'Steps',
  active_calories: 'Active calories',
  stress_high_seconds: 'High-stress time',
  systolic_bp: 'Systolic BP',
  diastolic_bp: 'Diastolic BP',
  weight_kg: 'Weight',
  glucose: 'Glucose',
  vo2_max: 'VO₂ max',
  sleep_debt: 'Sleep debt',
  acwr: 'Training load ratio',
};

const UNITS: Record<string, string> = {
  resting_hr: 'bpm', hrv_rmssd: 'ms', hrv_sdnn: 'ms', breathing_rate: '/min', spo2_average: '%',
  weight_kg: 'kg', systolic_bp: 'mmHg', diastolic_bp: 'mmHg', glucose: 'mg/dL', sleep_efficiency: '%',
};

const label = (metric: string) => METRIC_LABELS[metric] ?? metric.replace(/_/g, ' ');

function fmtValue(metric: string, v: number): string {
  if (metric.endsWith('_minutes')) {
    const h = Math.floor(v / 60);
    const m = Math.round(v % 60);
    return h ? `${h}h ${m}m` : `${m}m`;
  }
  if (metric === 'stress_high_seconds') return `${Math.round(v / 60)}m`;
  if (metric === 'steps' || metric === 'active_calories') return Math.round(v).toLocaleString();
  if (metric === 'skin_temp_deviation') return `${signed(v, 2)}°`;
  const unit = UNITS[metric];
  // Heart rate, HRV and scores are whole numbers in practice; a ".0" on each adds noise.
  const n = Math.abs(v) >= 10 ? v.toFixed(0) : v.toFixed(1);
  return unit ? `${n} ${unit}` : n;
}

const TYPE_LABELS: Record<string, string> = {
  deviation: 'Off baseline',
  early_illness: 'Possible illness',
  regime_change: 'New normal',
  strain_risk: 'Strain',
  drift: 'Drifting',
  staleness: 'Missing data',
  lab_anchor: 'Lab result',
};

const SEVERITY_LABEL: Record<string, string> = { high: 'Worth acting on', notable: 'Keep an eye on', info: 'For information' };

// Severity is status, so it never travels on colour alone: every use pairs the colour
// with this shape and a word.
function SeverityIcon({ severity }: { severity: string }) {
  if (severity === 'high') {
    return (
      <svg className="insight-icon sev-high" viewBox="0 0 16 16" aria-hidden="true">
        <path d="M5 1h6l4 4v6l-4 4H5l-4-4V5z" /><path className="glyph" d="M8 4.5v4.5M8 11.2v.3" />
      </svg>
    );
  }
  if (severity === 'notable') {
    return (
      <svg className="insight-icon sev-notable" viewBox="0 0 16 16" aria-hidden="true">
        <path d="M8 1.5 15 14H1z" /><path className="glyph" d="M8 6v3.8M8 11.6v.3" />
      </svg>
    );
  }
  return (
    <svg className="insight-icon sev-info" viewBox="0 0 16 16" aria-hidden="true">
      <circle cx="8" cy="8" r="7" /><path className="glyph" d="M8 7.2v4.3M8 4.6v.3" />
    </svg>
  );
}

// How far today's reading sits from the user's own typical value, in words first.
const zBand = (z: number) => (Math.abs(z) >= 2 ? 'high' : Math.abs(z) >= 1 ? 'notable' : 'usual');
const zWords = (z: number) =>
  Math.abs(z) < 1 ? 'Within your usual range'
  : Math.abs(z) >= 2 ? `Well ${z > 0 ? 'above' : 'below'} usual`
  : `${z > 0 ? 'Above' : 'Below'} usual`;
function signed(v: number, digits = 1): string {
  return `${v > 0 ? '+' : v < 0 ? '−' : ''}${Math.abs(v).toFixed(digits)}`;
}

// ── Verdict ──────────────────────────────────────────────────────────────────

function Verdict() {
  const { data, isLoading, error } = useQuery({
    queryKey: ['insight-summary'],
    queryFn: () => get<Summary>(`${API}/api/health/summary`),
  });
  const { tiles } = useMetricTiles();

  // Where the measurements that have a normal sit today. A count of measurements, never a
  // score: there is deliberately no single number for "how healthy are you".
  const ring = useMemo(() => {
    const band = (b: string) => tiles.filter(t => (t.latest ? zBand(t.latest.value) : 'none') === b).length;
    const unchecked = tiles.filter(t => !t.latest).length + (data?.coverage?.stillLearning ?? 0);
    const parts = [
      { key: 'usual', label: 'In your usual range', value: band('usual'), color: 'var(--insight-usual)' },
      { key: 'notable', label: 'A little off', value: band('notable'), color: 'var(--insight-notable)' },
      { key: 'high', label: 'Well off', value: band('high'), color: 'var(--insight-high)' },
      { key: 'unchecked', label: 'Not checked today', value: unchecked, color: 'var(--border2)' },
    ];
    const measured = parts[0].value + parts[1].value + parts[2].value;
    return { parts, measured, usual: parts[0].value };
  }, [tiles, data]);

  if (isLoading) return <section className="insight-card insight-verdict is-loading" aria-busy="true" />;
  if (error || !data) {
    return (
      <section className="insight-card insight-verdict state-unrun">
        <p className="insight-eyebrow">Health read</p>
        <h2 className="insight-verdict-title">Insight isn't reachable</h2>
        <p className="insight-muted">{String(error ?? 'No response')}</p>
      </section>
    );
  }

  // Three states with genuinely different meanings, and the middle one is the trap:
  // nothing computed looks identical to nothing wrong unless it is said out loud.
  const state =
    data.computedThrough === null ? 'unrun'
    : (data.daysBehind ?? 0) >= 3 ? 'stale'
    : data.activeFindings > 0 ? 'flagged'
    : 'clear';

  const title =
    state === 'unrun' ? 'Not analysed yet'
    : state === 'stale' ? `Analysis is ${data.daysBehind} days old`
    : state === 'flagged' ? `${data.activeFindings} ${data.activeFindings === 1 ? 'thing' : 'things'} worth a look`
    : 'Everything within your normal';

  const detail =
    state === 'unrun' ? "Baselines haven't been computed, so no findings doesn't mean nothing is wrong — nothing has been checked."
    : state === 'stale' ? 'The analysis has stopped running. What follows may be out of date.'
    : state === 'flagged' ? 'Each is measured against your own history, not a population average.'
    : 'No metric is outside the range your own last sixty days would predict.';

  return (
    <section className={`insight-card insight-verdict state-${state}`}>
      {state !== 'unrun' && ring.measured > 0 ? (
        <div className="ix-ring-col">
          <DonutSplit
            parts={ring.parts}
            ariaLabel={`${ring.usual} of ${ring.measured} measurements are in your usual range today`}
          >
            <span className="ix-donut-num">{ring.usual}<small>/{ring.measured}</small></span>
            <span className="ix-donut-cap">in range today</span>
          </DonutSplit>
          <Legend parts={ring.parts} />
        </div>
      ) : (
        <div className="insight-pulse" aria-hidden="true"><span /><span /><span /></div>
      )}
      <div className="insight-verdict-body">
        <p className="insight-eyebrow">Health read</p>
        <h2 className="insight-verdict-title">{title}</h2>
        <p className="insight-muted">{detail}</p>

        {data.activeFindings > 0 && (
          <ul className="insight-sev-chips">
            {['high', 'notable', 'info'].filter(s => data.bySeverity[s]).map(s => (
              <li key={s} className={`insight-chip sev-${s}`}>
                <SeverityIcon severity={s} /> {data.bySeverity[s]} · {SEVERITY_LABEL[s]}
              </li>
            ))}
          </ul>
        )}

        {/* "Everything within your normal" is true and misleading when half the
            metrics have no normal to be outside of, so the count travels with it. */}
        {data.coverage && data.coverage.stillLearning > 0 && (
          <p className="insight-learning">
            <b>{data.coverage.metricsWithNormal}</b> measurements have a normal to be checked against;{' '}
            <b>{data.coverage.stillLearning}</b> are still learning theirs.
            <Info label="What still learning means">
              A normal needs enough readings behind it to mean anything. Until then a measurement is
              recorded and shown, but not checked — so it can be neither flagged nor called fine.
              {data.coverage.stillLearningMetrics.length > 0 && (
                <> Still learning: {data.coverage.stillLearningMetrics.join(', ')}.</>
              )}
            </Info>
          </p>
        )}

        <p className="insight-meta">
          {data.latestDataDay ? <>Newest reading {data.latestDataDay}</> : <>No readings yet</>}
          {data.computedThrough && <> · analysed through {data.computedThrough}</>}
        </p>
      </div>
    </section>
  );
}

// ── Biological age (estimate) ────────────────────────────────────────────────

function BioAgeCard() {
  const { data, isLoading, error } = useQuery({
    queryKey: ['bioage'],
    queryFn: () => get<BioAge>(`${VITARA}/api/bioage`),
  });

  const contributions = useMemo(
    () => [...(data?.contributions ?? [])].sort((a, b) => Math.abs(b.years) - Math.abs(a.years)),
    [data],
  );

  if (isLoading) return <section className="insight-card insight-bio is-loading" aria-busy="true" />;
  if (error || !data) return null;

  const hasAge = data.bioAge != null && data.delta != null;
  // Scale shared by every bar, rounded up to a whole year so a small effect isn't
  // stretched to look as large as a big one.
  const span = Math.max(1, Math.ceil(Math.max(0, ...contributions.map(c => Math.abs(c.years)))));
  const younger = (data.delta ?? 0) < 0;

  return (
    <section className="insight-card insight-bio">
      <div className="insight-bio-head">
        <p className="insight-eyebrow">Biological age</p>
        <span className="insight-badge" title={data.disclaimer}>{data.label ?? 'Estimate'}</span>
      </div>

      {!hasAge ? (
        <p className="insight-muted">
          {data.dataQuality === 'insufficient'
            ? 'Needs at least three days of sleep and readiness data.'
            : 'Not enough data to estimate yet.'}
        </p>
      ) : (
        <>
          <div className="insight-bio-figure">
            <span className="insight-hero-number">{data.bioAge!.toFixed(1)}</span>
            <span className="insight-bio-delta">
              <span className={`insight-bio-arrow ${younger ? 'younger' : 'older'}`} aria-hidden="true">
                {younger ? '▼' : '▲'}
              </span>
              {Math.abs(data.delta!).toFixed(1)} years {younger ? 'younger' : 'older'} than your age, {data.chronologicalAge}
            </span>
          </div>

          {contributions.length > 0 && (
            <div className="insight-contrib" role="table" aria-label="What moves the estimate">
              <div className="insight-contrib-axis" role="row" aria-hidden="true">
                <span />
                <span className="insight-contrib-poles"><span>← younger</span><span>older →</span></span>
                <span />
              </div>
              {contributions.map(c => {
                const pct = (Math.abs(c.years) / span) * 50;
                const dir = c.years < 0 ? 'younger' : 'older';
                return (
                  <div
                    key={c.key}
                    className="insight-contrib-row"
                    role="row"
                    title={`${c.name}: ${fmtFactor(c)} · ${signed(c.years, 2)} years · ${(c.weight * 100).toFixed(0)}% of the blend`}
                  >
                    <span className="insight-contrib-name" role="cell">
                      {c.name}
                      <span className="insight-contrib-value">{fmtFactor(c)}</span>
                    </span>
                    <span className="insight-contrib-track" role="cell">
                      <span className="insight-contrib-mid" />
                      <span
                        className={`insight-contrib-bar ${dir}`}
                        style={dir === 'younger'
                          ? { right: '50%', width: `${pct}%` }
                          : { left: '50%', width: `${pct}%` }}
                      />
                    </span>
                    {/* A small effect keeps its second decimal, so it doesn't read "+0.0". */}
                    <span className="insight-contrib-years" role="cell">{signed(c.years, Math.abs(c.years) < 0.1 ? 2 : 1)} y</span>
                  </div>
                );
              })}
            </div>
          )}
        </>
      )}

      <p className="insight-disclaimer">{data.disclaimer ?? 'A wellness estimate from ring data, not a medical measurement.'}</p>
      {data.method && (
        <details className="insight-howto">
          <summary>How it's calculated</summary>
          <p>{data.method}</p>
          <p>
            Data: {data.dataQuality} · age from {data.ageSource}
            {data.clamped && ' · the blend hit the ±15-year cap, so the figure is capped'}
          </p>
        </details>
      )}
    </section>
  );
}

function fmtFactor(c: BioContribution): string {
  if (c.unit === 'pts/day') return `${signed(c.value, 2)} pts/day`;
  if (c.unit === '/100') return `${c.value.toFixed(0)}/100`;
  if (c.unit === 'years') return `${c.value.toFixed(0)} yrs`;
  return `${c.value.toFixed(0)} ${c.unit}`;
}

// ── Findings ─────────────────────────────────────────────────────────────────

function Findings() {
  const { data } = useQuery({
    queryKey: ['insight-summary'],
    queryFn: () => get<Summary>(`${API}/api/health/summary`),
  });
  if (!data || data.findings.length === 0) return null;

  // The server has already chosen the order and which few lead; re-sorting here would
  // put the tab and San in disagreement about what today's news is.
  const sorted = data.findings;
  const standing = data.standing ?? [];

  return (
    <section className="insight-section">
      <h2 className="insight-h2">
        What&apos;s being flagged
        <Info label="How something gets flagged">
          A finding appears when a measurement stays away from your own usual range for long enough
          that chance is an unlikely explanation. The dots under each one are how many days it has
          been running, which is the difference between one odd morning and something worth acting on.
          At most {data.surfacing?.cap ?? 3} lead at a time, newest first at equal severity — anything
          serious always leads, and nothing is ever dropped.
        </Info>
      </h2>
      <ul className="insight-findings">
        {sorted.map(f => (
          <li key={f.key} className={`insight-finding sev-${f.severity}`}>
            <div className="insight-finding-head">
              <SeverityIcon severity={f.severity} />
              <span className="insight-sev-word">{SEVERITY_LABEL[f.severity] ?? f.severity}</span>
              <span className="insight-type">{TYPE_LABELS[f.type] ?? f.type}</span>
              <span className="insight-metric">{label(f.metric)}</span>
            </div>
            <p className="insight-summary">{f.summary}</p>
            {f.lab ? <RangeGauge reading={f.lab} /> : <FindingTrend metric={f.metric} />}
            {/* How long it has been true is the difference between one odd morning and
                something worth acting on, so it is drawn rather than buried in the text. */}
            <div className="insight-days" aria-label={`Day ${f.daysRunning}`}>
              {Array.from({ length: Math.min(f.daysRunning, 7) }, (_, i) => <span key={i} />)}
              <em>{f.daysRunning === 1 ? 'first seen today' : `day ${f.daysRunning}`}</em>
            </div>
          </li>
        ))}
      </ul>

      {/* Held back from the lead, not hidden. Something running for its fortieth day has
          been said thirty-nine times; it is still true and still here. */}
      {standing.length > 0 && (
        <details className="insight-standing">
          <summary>
            {standing.length === 1 ? 'One more finding' : `${standing.length} more findings`} standing and unchanged
          </summary>
          <ul className="insight-standing-list">
            {standing.map(f => (
              <li key={f.key}>
                <SeverityIcon severity={f.severity} />
                <span className="insight-standing-text">{f.summary}</span>
                <em>{f.daysRunning === 1 ? 'first seen today' : `day ${f.daysRunning}`}</em>
              </li>
            ))}
          </ul>
        </details>
      )}
    </section>
  );
}

// ── Today against your normal ────────────────────────────────────────────────

const Z_MAX = 3;

// One tile per metric with a normal: where today sits against it, and the last two weeks.
// Shared by the page's headline ring and by the tiles themselves, so the ring can never
// count something the tiles do not show.
function useMetricTiles() {
  const baselinesQ = useQuery({
    queryKey: ['insight-baselines'],
    queryFn: () => get<BaselineSet>(`${API}/api/health/baselines`),
  });
  const { byMetric: zByMetric } = useZSeries();

  const tiles = useMemo(() => {
    const baselines = (baselinesQ.data?.baselines ?? []).filter(b => b.isValid);
    // One tile per metric. Blood pressure and glucose can carry several context
    // baselines; the tile uses the best-supported one and the table lists them all.
    const best = new Map<string, Baseline>();
    for (const b of baselines) {
      const cur = best.get(b.metric);
      if (!cur || b.n > cur.n) best.set(b.metric, b);
    }

    // The tiles show the last two weeks. The series behind them is longer, so a reading
    // from three weeks ago must not count as today's: cut to the window first.
    const since = addDays(todayInTz(), -14);

    return [...best.values()]
      .map(b => {
        const series = (zByMetric.get(b.metric) ?? []).filter(d => d.day >= since);
        const latest = series.length ? series[series.length - 1] : undefined;
        return { baseline: b, series, latest };
      })
      .sort((a, b) =>
        Number(!!b.latest) - Number(!!a.latest)
        || Math.abs(b.latest?.value ?? 0) - Math.abs(a.latest?.value ?? 0)
        || label(a.baseline.metric).localeCompare(label(b.baseline.metric)));
  }, [baselinesQ.data, zByMetric]);

  return { tiles, loading: baselinesQ.isLoading };
}

// The z-score history per metric: how far each day sat from its own usual. Sixty days,
// asked for once and shared, so the tiles and the findings read the same line.
function useZSeries() {
  const q = useQuery({
    queryKey: ['insight-derived', 60],
    queryFn: () => get<Derived[]>(`${API}/api/health/derived?days=60`),
  });

  const byMetric = useMemo(() => {
    const out = new Map<string, Derived[]>();
    for (const d of q.data ?? []) {
      if (!d.metric.endsWith('_z')) continue;
      const metric = d.metric.slice(0, -2);
      out.set(metric, [...(out.get(metric) ?? []), d]);
    }
    for (const list of out.values()) list.sort((x, y) => x.day.localeCompare(y.day));
    return out;
  }, [q.data]);

  return { byMetric, loading: q.isLoading };
}

// The finding's own line, when it has one. Some findings are about something that has no
// z-score (sleep debt, a lab, a composite pattern); those simply get no picture rather than
// a made-up one.
function FindingTrend({ metric }: { metric: string }) {
  const { byMetric } = useZSeries();
  const series = byMetric.get(metric);
  if (!series || series.length < 8) return null;

  return (
    <div className="insight-finding-trend">
      <Sparkline series={series} height={44} />
      <span>last {series.length} days against your usual</span>
    </div>
  );
}

function TodayVsNormal() {
  const { tiles, loading } = useMetricTiles();

  if (loading) return null;

  return (
    <section className="insight-section">
      <h2 className="insight-h2">
        Today against your normal
        <Info label="How to read these tiles">
          The shaded band is your usual range — where two-thirds of your last sixty days fell. The
          line underneath is the last two weeks. Whatever sits furthest from normal comes first.
        </Info>
      </h2>

      {tiles.length === 0 ? (
        <p className="insight-muted">No metric has enough history yet. Each needs 21 readings before its normal means anything.</p>
      ) : (
        <ul className="insight-tiles">
          {tiles.map(({ baseline: b, series, latest }) => {
            const band = latest ? zBand(latest.value) : 'none';
            return (
              <li key={b.metric} className={`insight-tile band-${band}`}>
                <div className="insight-tile-head">
                  <span className="insight-tile-name">{label(b.metric)}</span>
                  {latest && <span className="insight-tile-z">{signed(latest.value)}σ</span>}
                </div>
                <p className="insight-tile-verdict">
                  {latest ? zWords(latest.value) : 'No reading today'}
                </p>

                <PositionStrip z={latest?.value ?? null} band={band} />
                {series.length > 1 && <Sparkline series={series} />}

                <p className="insight-tile-typical">
                  Typical {fmtValue(b.metric, b.median)}
                  <span> · usual {fmtValue(b.metric, b.p25)} – {fmtValue(b.metric, b.p75)}</span>
                </p>
              </li>
            );
          })}
        </ul>
      )}
    </section>
  );
}

// Where today sits on a ±3σ scale: the usual band shaded, the typical value as a tick.
function PositionStrip({ z, band }: { z: number | null; band: string }) {
  const W = 240, H = 22, pad = 6;
  const x = (v: number) => pad + ((Math.max(-Z_MAX, Math.min(Z_MAX, v)) + Z_MAX) / (2 * Z_MAX)) * (W - 2 * pad);
  return (
    <svg className="insight-strip" viewBox={`0 0 ${W} ${H}`} preserveAspectRatio="none" role="img"
         aria-label={z == null ? 'No reading today' : `${signed(z)} standard deviations from typical`}>
      <line className="axis" x1={pad} x2={W - pad} y1={H / 2} y2={H / 2} />
      <rect className="usual" x={x(-1)} width={x(1) - x(-1)} y={H / 2 - 5} height={10} rx={3} />
      <line className="typical" x1={x(0)} x2={x(0)} y1={H / 2 - 7} y2={H / 2 + 7} />
      {z != null && (
        <circle className={`dot band-${band}`} cx={x(z)} cy={H / 2} r={5}>
          <title>{`${signed(z)}σ from typical`}</title>
        </circle>
      )}
    </svg>
  );
}

function Sparkline({ series, height = 34 }: { series: Derived[]; height?: number }) {
  const W = 240, H = height, pad = 4;
  const n = series.length;
  const x = (i: number) => pad + (i / Math.max(1, n - 1)) * (W - 2 * pad);
  const y = (v: number) => H / 2 - (Math.max(-Z_MAX, Math.min(Z_MAX, v)) / Z_MAX) * (H / 2 - pad);
  const points = series.map((d, i) => `${x(i).toFixed(1)},${y(d.value).toFixed(1)}`).join(' ');
  const last = series[n - 1];

  return (
    <svg className="insight-spark" viewBox={`0 0 ${W} ${H}`} preserveAspectRatio="none" role="img"
         style={{ height }} aria-label={`Last ${n} days relative to typical`}>
      <rect className="usual" x={pad} width={W - 2 * pad} y={y(1)} height={y(-1) - y(1)} />
      <line className="typical" x1={pad} x2={W - pad} y1={y(0)} y2={y(0)} />
      <polyline className="line" points={points} />
      {series.map((d, i) => (
        <circle key={d.day} className="hit" cx={x(i)} cy={y(d.value)} r={6}>
          <title>{`${d.day}: ${signed(d.value)}σ`}</title>
        </circle>
      ))}
      <circle className={`end band-${zBand(last.value)}`} cx={x(n - 1)} cy={y(last.value)} r={3.5} />
    </svg>
  );
}

// ── What moves with what ─────────────────────────────────────────────────────

function Correlations() {
  const { data } = useQuery({
    queryKey: ['insight-correlations'],
    queryFn: () => get<CorrelationSet>(`${API}/api/health/correlations`),
  });
  if (!data) return null;

  const rows = [...data.correlations].sort((a, b) => Math.abs(b.rho) - Math.abs(a.rho));
  const minRho = data.method.minAbsRho;

  return (
    <section className="insight-section">
      <h2 className="insight-h2">
        What moves with what
        {/* The caveat comes from the API rather than being written here, so every
            surface that shows these numbers carries the same words. */}
        <Info label="What these relationships are and are not">{data.caveat}</Info>
      </h2>

      {rows.length === 0 ? (
        <p className="insight-muted">
          Nothing cleared the bar. That's the usual result — with {data.method.minPairedDays}+ paired days
          required and a correction for testing many pairs at once, only a strong, consistent relationship
          shows up here.
        </p>
      ) : (
        <>
          <RelationshipWeb links={rows} name={label} />
          <details className="insight-standing" style={{ marginTop: '0.8rem' }}>
            <summary>The same, in words</summary>
          <ul className="insight-corr">
            {rows.map(c => {
              const left = 50 + Math.min(c.rho, 0) * 50;
              const width = Math.abs(c.rho) * 50;
              return (
                <li key={`${c.driver}-${c.outcome}-${c.lagDays}`} className="insight-corr-row">
                  <p className="insight-corr-text">
                    When <strong>{label(c.driver)}</strong> is higher,{' '}
                    <strong>{label(c.outcome)}</strong> tends to be {c.rho > 0 ? 'higher' : 'lower'}
                    {c.lagDays === 0 ? ' the same day' : ' the next day'}.
                  </p>
                  <div className="insight-corr-plot" title={`rho ${c.rho.toFixed(2)} over ${c.n} days`}>
                    <span className="quiet" style={{ left: `${50 - minRho * 50}%`, width: `${minRho * 100}%` }} />
                    <span className="mid" />
                    <span className="stem" style={{ left: `${left}%`, width: `${width}%` }} />
                    <span className="dot" style={{ left: `${50 + c.rho * 50}%` }} />
                  </div>
                  <p className="insight-corr-stat">ρ {signed(c.rho, 2)} · {c.n} days</p>
                </li>
              );
            })}
          </ul>
          </details>
        </>
      )}

      <p className="insight-method">
        {data.method.test} · {data.method.multipleComparisons} · |ρ| ≥ {minRho}
        {data.computedOn && ` · computed ${data.computedOn}`}
      </p>
    </section>
  );
}

// ── The sheet for an appointment ─────────────────────────────────

// The closest thing here to what people mean by an AI doctor, and deliberately not
// one. It names no condition and recommends nothing; it remembers, which is the part
// a ten-minute appointment actually fails at.
function SelfCheckSection() {
  const { data } = useQuery({
    queryKey: ['self-check'],
    queryFn: () => get<SelfCheck>(`${API}/api/health/self-check`),
  });
  const [open, setOpen] = useState(false);
  if (!data) return null;

  const groups = [...new Set(data.capabilities.map(c => c.group))];

  return (
    <section className="insight-section">
      <h2 className="insight-h2">
        What this can actually see
        <Info label="Why this page exists">
          Every other section here shows you something about your body. This one is about the software:
          whether each part of it is finding nothing, or cannot look. Those are opposite facts and they
          look identical everywhere else.
        </Info>
      </h2>

      <div className="insight-top" style={{ marginBottom: '0.9rem' }}>
        <div className="insight-card">
          <p className="insight-eyebrow">Right now</p>
          <p className="insight-summary" style={{ fontSize: '1rem' }}>
            <b>{data.speaking}</b> speaking · <b>{data.quiet}</b> quiet · <b>{data.blind}</b> blind
          </p>
          <StackBar
            ariaLabel={`${data.speaking} parts speaking, ${data.quiet} quiet, ${data.blind} blind`}
            parts={[
              { key: 's', label: 'speaking', value: data.speaking, color: 'var(--insight-usual)' },
              { key: 'q', label: 'quiet', value: data.quiet, color: 'var(--border2)' },
              { key: 'b', label: 'blind', value: data.blind, color: 'var(--insight-notable)' },
            ]}
          />
          {/* No total and no percentage. A single number summarising how well this is
              working would be the same kind of composite the app grades as
              experimental everywhere else. */}
          <ul className="insight-asks insight-asks-muted" style={{ marginTop: '0.6rem' }}>
            {data.headline.map(h => <li key={h}>{h}</li>)}
          </ul>
        </div>
      </div>

      {groups.map(g => (
        <div key={g} className="insight-card" style={{ marginBottom: '0.7rem' }}>
          <p className="insight-eyebrow">{g}</p>
          <div className="hx-rows">
            {data.capabilities.filter(c => c.group === g).map(c => (
              <Row
                key={c.key}
                tone={c.state === 'speaking' ? 'good' : c.state === 'blind' ? 'warn' : undefined}
                title={c.label}
                note={
                  <>
                    {c.says}
                    {c.needs.length > 0 && (
                      <> <b>Needs:</b> {c.needs.slice(0, 3).join('; ')}{c.needs.length > 3 ? ` and ${c.needs.length - 3} more` : ''}</>
                    )}
                  </>
                }
                right={
                  c.state === 'speaking' ? <Chip tone="good">speaking</Chip>
                  : c.state === 'blind' ? <Chip tone="warn">blind</Chip>
                  : <Chip>quiet</Chip>
                }
              />
            ))}
          </div>
        </div>
      ))}

      <button className="hx-btn" onClick={() => setOpen(o => !o)} style={{ marginTop: '0.4rem' }}>
        {open ? 'Hide every metric' : `Show all ${data.coverage.length} metrics`}
      </button>

      {open && (
        <div className="insight-card" style={{ marginTop: '0.7rem' }}>
          <div className="hx-rows">
            {data.coverage.map(m => (
              <Row
                key={m.metric}
                tone={m.state === 'current' ? 'good' : m.state === 'never' ? undefined : 'warn'}
                title={m.label}
                note={
                  m.readings === 0
                    ? 'never recorded'
                    : `${m.readings.toLocaleString()} readings · last ${m.daysSinceLast === 0 ? 'today' : `${m.daysSinceLast} days ago`} · baseline ${m.baseline}`
                }
                right={<Chip>{m.state}</Chip>}
              />
            ))}
          </div>
        </div>
      )}
    </section>
  );
}

// The only question here whose answer can embarrass the app, and the reason it is
// worth asking: everything else describes what happened, and this checks whether a
// decision was any good.
//
// The target is chosen when the intervention is created, never afterwards. That
// ordering is the feature — picking the metric after seeing which one moved is how a
// tracker proves that everything works.
function InterventionsSection() {
  const qc = useQueryClient();
  const { data } = useQuery({
    queryKey: ['interventions'],
    queryFn: () => get<InterventionScan>(`${API}/api/health/interventions`),
  });
  const { data: targets } = useQuery({
    queryKey: ['targets'],
    queryFn: () => get<Target[]>(`${VITARA}/api/interventions/targets`),
  });

  const [name, setName] = useState('');
  const [kind, setKind] = useState('protocol');
  const [target, setTarget] = useState('');
  const [startedOn, setStartedOn] = useState(todayInTz());
  const [error, setError] = useState<string | null>(null);

  const add = useMutation({
    mutationFn: async () => {
      const r = await fetch(`${VITARA}/api/interventions`, {
        method: 'POST',
        headers: { ...vitaraHeaders(), 'Content-Type': 'application/json' },
        body: JSON.stringify({ name, kind, targetMetric: target || null, startedOn }),
      });
      const json = await r.json();
      if (!r.ok) throw new Error(json?.error ?? `${r.status}`);
      return json;
    },
    onSuccess: () => {
      setName(''); setTarget(''); setError(null);
      qc.invalidateQueries({ queryKey: ['interventions'] });
    },
    onError: (e: Error) => setError(e.message),
  });

  const stop = useMutation({
    mutationFn: (id: string) =>
      fetch(`${VITARA}/api/interventions/${id}/stop`, { method: 'POST', headers: vitaraHeaders() }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['interventions'] }),
  });

  if (!data) return null;

  return (
    <section className="insight-section">
      <h2 className="insight-h2">
        Did it work
        {data.evidence && (
          <Info label="How much weight this carries">
            {data.evidence.claim} Evidence: {data.evidence.label}. {data.evidence.caveat}
          </Info>
        )}
      </h2>

      <p className="insight-muted insight-lede">
        The first {data.runInDays} days of anything are not counted, and {data.windowDays} are needed after
        that. Pick what it is meant to change now — choosing afterwards finds whichever metric happened to move.
      </p>

      <div className="insight-card" style={{ marginBottom: '0.9rem' }}>
        <div className="hx-form">
          <input
            placeholder="What did you start?"
            value={name}
            onChange={e => setName(e.target.value)}
            style={{ flex: 1, minWidth: '11rem' }}
          />
          <select value={kind} onChange={e => setKind(e.target.value)}>
            <option value="protocol">protocol</option>
            <option value="supplement">supplement</option>
            <option value="medication">medication</option>
            <option value="dose_change">dose change</option>
          </select>
          <select value={target} onChange={e => setTarget(e.target.value)} style={{ minWidth: '12rem' }}>
            <option value="">meant to change… (no verdict without this)</option>
            {(targets ?? []).map(t => (
              <option key={t.key} value={t.key}>{t.label} — {t.better} is better</option>
            ))}
          </select>
          <input type="date" value={startedOn} onChange={e => setStartedOn(e.target.value)} />
          <button className="hx-btn" disabled={!name.trim() || add.isPending} onClick={() => add.mutate()}>
            {add.isPending ? 'Saving…' : 'Start tracking'}
          </button>
        </div>
        {error && <p className="hx-error" style={{ marginTop: '0.5rem' }}>{error}</p>}
      </div>

      {data.interventions.length === 0 ? (
        <p className="insight-muted">Nothing recorded yet.</p>
      ) : (
        data.interventions.map(i => (
          <Verdictcard key={i.id} e={i} onStop={() => stop.mutate(i.id)}
                       runInDays={data.runInDays} windowDays={data.windowDays} />
        ))
      )}
    </section>
  );
}

function Verdictcard({ e, onStop, runInDays, windowDays }: {
  e: Evaluated; onStop: () => void; runInDays: number; windowDays: number;
}) {
  // Only two verdicts are a result. Everything else is a reason there is not one yet,
  // and those are coloured neutrally on purpose — a grey "cannot say" must not read
  // as a failure, or nobody records the next one.
  const tone =
    e.verdict === 'improved' ? 'good'
    : e.verdict === 'worsened' ? 'warn'
    : undefined;

  return (
    <div className="insight-card" style={{ marginBottom: '0.9rem' }}>
      <div className="insight-finding-head" style={{ justifyContent: 'space-between' }}>
        <span>
          <span className="insight-metric">{e.name}</span>
          <span className="insight-type">
            {e.kind.replace('_', ' ')} · {e.daysIn} days{e.targetLabel ? ` · for ${e.targetLabel}` : ''}
          </span>
        </span>
        <span style={{ display: 'flex', gap: '0.4rem', alignItems: 'center' }}>
          <Chip tone={tone}>{e.verdict.replace(/_/g, ' ')}</Chip>
          {e.running && (
            <button className="hx-icon-btn" onClick={onStop} aria-label="Mark this as stopped">stop</button>
          )}
        </span>
      </div>

      <p className="insight-summary" style={{ marginTop: '0.5rem' }}>{e.statement}</p>

      {/* How far along it is, drawn: "cannot say yet" is easier to believe when the road left is visible. */}
      <TrialTrack daysIn={e.daysIn} runInDays={runInDays} windowDays={windowDays} running={e.running} />

      {e.before && e.after && e.targetMetric && (
        <ShiftStrip
          before={e.before}
          after={e.after}
          format={v => fmtValue(e.targetMetric as string, v)}
          tone={tone}
        />
      )}

      {e.rebound && (
        /* The number the whole verdict turns on, shown rather than only applied. */
        <p className="insight-muted" style={{ fontSize: '0.75rem', marginTop: '0.4rem' }}>
          Measured against {e.rebound.comparableWindows} earlier stretches of yours that were just as far below
          par and recovered on their own by about {e.rebound.median}.
        </p>
      )}

      {e.caveats.length > 0 && (
        <ul className="insight-asks insight-asks-muted" style={{ marginTop: '0.5rem' }}>
          {e.caveats.map(c => <li key={c}>{c}</li>)}
        </ul>
      )}

      {e.alsoChanged.length > 0 && (
        <>
          <p className="insight-eyebrow" style={{ marginTop: '0.7rem' }}>Also moved</p>
          <div className="hx-rows">
            {e.alsoChanged.map(c => (
              <Row
                key={c.metric}
                tone={c.direction === 'better' ? 'good' : c.direction === 'worse' ? 'warn' : undefined}
                title={c.label}
                note={c.detail}
                right={<Chip>{c.direction}</Chip>}
              />
            ))}
          </div>
          <p className="insight-muted" style={{ fontSize: '0.72rem', marginTop: '0.4rem' }}>{e.alsoChangedNote}</p>
        </>
      )}
    </div>
  );
}

// Patterns are the only thing on this page that reads across metrics. Everything else
// asks a question about one number, which is the right question for an acute signal
// and the wrong one for a slow correlated drift — each component of which sits, on its
// own, well inside the range where nobody would mention it.
function PatternsSection() {
  const { data } = useQuery({
    queryKey: ['patterns'],
    queryFn: () => get<PatternScan>(`${API}/api/health/patterns`),
  });
  if (!data) return null;

  const firing = data.patterns.filter(p => p.fires);
  const quiet = data.patterns.filter(p => !p.fires);

  return (
    <section className="insight-section">
      <h2 className="insight-h2">
        Moving together
        {data.evidence && (
          <Info label="How much weight this carries">
            {data.evidence.claim} Evidence: {data.evidence.label}. {data.evidence.caveat}
          </Info>
        )}
      </h2>

      <p className="insight-muted insight-lede">
        {firing.length === 0
          ? 'Nothing is moving as a group. Each measure below is still watched on its own.'
          : `${firing.length} ${firing.length === 1 ? 'group of measures is' : 'groups of measures are'} moving together.`}
      </p>

      {firing.map(p => (
        <div key={p.key} className="insight-card" style={{ marginBottom: '0.9rem' }}>
          <p className="insight-eyebrow">{p.title}</p>
          <p className="insight-summary" style={{ marginBottom: '0.5rem' }}>{p.statement}</p>
          <p className="insight-muted" style={{ fontSize: '0.8rem', marginBottom: '0.7rem' }}>{p.interpretation}</p>
          <div className="ix-pattern">
            <PatternRing
              beads={p.components.map(c => ({ label: c.label, movement: c.movement, counts: c.counts }))}
              moving={p.moving}
              counted={p.counted}
            />
            <div className="ix-pattern-list"><ComponentList components={p.components} /></div>
          </div>
        </div>
      ))}

      {quiet.length > 0 && (
        <div className="insight-card">
          {/* The quiet ones are listed rather than hidden. A pattern that is not
              firing because nobody measured its components is a different fact from
              one that is not firing because nothing is happening, and only one of
              them is reassuring. */}
          <p className="insight-eyebrow">Not showing a pattern</p>
          <ul className="insight-asks insight-asks-muted">
            {quiet.map(p => (
              <li key={p.key}>
                <b style={{ fontWeight: 600 }}>{p.title}</b> — {p.heldBecause}.
                {p.wouldHelp.length > 0 && (
                  <> Measuring {p.wouldHelp.map(w => w.label.toLowerCase()).join(', ')} would make this answerable.</>
                )}
              </li>
            ))}
          </ul>
        </div>
      )}
    </section>
  );
}

function ComponentList({ components }: { components: PatternComponent[] }) {
  return (
    <div className="hx-rows">
      {components.map((c, i) => (
        <Row
          key={c.metric}
          icon={<Bead n={i + 1} movement={c.movement} />}
          tone={c.movement === 'unfavourable' ? 'warn' : c.movement === 'favourable' ? 'good' : undefined}
          title={c.label}
          note={
            <>
              {c.detail} · {c.basis}
              {/* Said rather than silently dropped: a derived value restating its own
                  inputs is still worth seeing, it just must not be counted twice. */}
              {!c.counts && <> · shown but not counted, it is derived from another measure here</>}
            </>
          }
          right={
            c.movement === 'unmeasured' ? <Chip>not measured</Chip>
            : c.movement === 'steady' ? <Chip>steady</Chip>
            : c.movement === 'favourable' ? <Chip tone="good">better</Chip>
            : <Chip tone="warn">worse</Chip>
          }
        />
      ))}
    </div>
  );
}

function VisitBriefSection() {
  const { data } = useQuery({
    queryKey: ['visit-brief'],
    queryFn: () => get<VisitBrief>(`${API}/api/health/visit-brief`),
  });
  if (!data) return null;

  return (
    <section className="insight-section">
      <h2 className="insight-h2">
        Take to your doctor
        <Info label="What this is and is not">
          {data.scope} {data.disclaimer}
        </Info>
      </h2>

      <p className="insight-muted insight-lede">{data.verdict}</p>

      {data.bring.length > 0 && (
        <ul className="insight-findings" style={{ marginBottom: '0.9rem' }}>
          {data.bring.map(b => (
            <li key={b.topic + b.what} className={`insight-finding sev-${b.severity}`}>
              <div className="insight-finding-head">
                <SeverityIcon severity={b.severity} />
                <span className="insight-metric">{b.topic}</span>
                {b.since && <span className="insight-type">{b.since}</span>}
              </div>
              <p className="insight-summary">{b.what}</p>
              {b.lab && <RangeGauge reading={b.lab} />}
            </li>
          ))}
        </ul>
      )}

      <div className="insight-top">
        <div className="insight-card">
          <p className="insight-eyebrow">Questions worth asking</p>
          <ul className="insight-asks">
            {data.questions.map(q => <li key={q}>{q}</li>)}
          </ul>
        </div>

        <div className="insight-card">
          {/* The most important panel on the page. A sheet that reads as complete
              invites its own gaps to be taken as reassurance. */}
          <p className="insight-eyebrow">What this does not look at</p>
          <ul className="insight-asks insight-asks-muted">
            {data.notLookedAt.map(x => <li key={x}>{x}</li>)}
          </ul>
          <p className="insight-muted" style={{ marginTop: '0.6rem', fontSize: '0.75rem' }}>{data.coverage}</p>
        </div>
      </div>
    </section>
  );
}

// ── Tomorrow, and how this body runs ────────────────────────────────────

// The one number on this page that can be wrong in public, so it is shown with its own
// track record attached rather than on its own.
function Tomorrow({ what, unit, forecast, bounds }: { what: string; unit: string; forecast: Forecast | null; bounds?: [number, number] }) {
  if (!forecast) return null;

  const naive = forecast.method === 'today';

  return (
    <div className="insight-card insight-forecast">
      <p className="insight-eyebrow">Tomorrow · {what}</p>
      <div className="insight-forecast-value">
        <b>{Math.round(forecast.value)}</b>
        <span>{unit}</span>
      </div>
      <RangeBar low={forecast.low} high={forecast.high} value={forecast.value} bounds={bounds} unit={unit} />
      <p className="insight-forecast-range">
        usually lands between {Math.round(forecast.low)} and {Math.round(forecast.high)}
      </p>
      <p className={`insight-forecast-method ${naive ? 'is-naive' : ''}`}>
        {naive ? 'same as today' : 'from your own model'}
        <Info label="How this forecast is judged">
          {forecast.basis} {forecast.evidence.verdict}
        </Info>
      </p>
    </div>
  );
}

// Four of them now. Readiness and resting heart rate lead because they are what people
// ask about; HRV and time asleep are the same machinery and the same honesty.
function TomorrowRow() {
  const { data } = useQuery({
    queryKey: ['forecasts'],
    queryFn: () => get<Forecasts>(`${API}/api/health/forecast`),
  });
  if (!data) return null;

  return (
    <div className="insight-forecast-row">
      <Tomorrow what="readiness" unit="/100" forecast={data.readiness} bounds={[0, 100]} />
      <Tomorrow what="resting heart rate" unit="bpm" forecast={data.restingHeartRate} />
      <Tomorrow what="HRV" unit="ms" forecast={data.hrv} bounds={[0, 200]} />
      <Tomorrow what="time asleep" unit="min" forecast={data.timeAsleep} bounds={[0, 720]} />
    </div>
  );
}

// The counterfactual. Everything about this section is designed to be refusable: the
// scenarios that cannot be answered are shown next to the ones that can, saying why.
function WhatIf() {
  const { data } = useQuery({
    queryKey: ['simulate', 'readiness'],
    queryFn: () => get<Simulation>(`${API}/api/health/simulate?target=readiness`),
  });
  if (!data || data.scenarios.length === 0) return null;

  const answered = data.scenarios.filter(x => x.supported);
  // One scale for every question, so a small answer looks small beside a large one.
  const biggest = Math.max(1, ...answered.map(x => Math.abs(x.change ?? 0)));
  const refused = data.scenarios.filter(x => !x.supported);

  return (
    <section className="insight-section">
      <h2 className="insight-h2">
        What if
        <Info label="Where these numbers come from">
          {data.caveat} A scenario you have almost never lived is refused rather than answered, because
          the model would happily answer it in exactly the same confident tone.
        </Info>
      </h2>

      {answered.length === 0 ? (
        <p className="insight-muted">{refused[0]?.answer}</p>
      ) : (
        <ul className="insight-whatif">
          {answered.map(x => (
            <li key={`${x.lever}${x.delta}`}>
              <p className="insight-whatif-q">{x.question}</p>
              <Tug change={x.change ?? 0} max={biggest} />
              <p className="insight-whatif-a">
                <b className={x.change! > 0 ? 'up' : x.change! < 0 ? 'down' : ''}>
                  {x.change! > 0 ? '+' : ''}{x.change} {data.unit}
                </b>
                <span>tomorrow&apos;s {data.target}</span>
                <Info label="What this means">{x.answer}</Info>
              </p>
            </li>
          ))}
        </ul>
      )}

      {answered.length > 0 && refused.length > 0 && (
        <details className="insight-standing">
          <summary>{refused.length} {refused.length === 1 ? 'question' : 'questions'} your data cannot answer</summary>
          <ul className="insight-standing-list">
            {refused.map(x => (
              <li key={`${x.lever}${x.delta}`}>
                <span className="insight-standing-text"><b>{x.question}</b> {x.answer}</span>
              </li>
            ))}
          </ul>
        </details>
      )}
    </section>
  );
}

// The signature as a file. Derived statistics and fitted weights only -- what leaves
// can say what tomorrow looks like and cannot say what last Tuesday was.
function ExportButton() {
  const [busy, setBusy] = useState(false);

  const save = async () => {
    setBusy(true);
    try {
      const res = await fetch(`${API}/api/health/signature/export`, { headers: vitaraHeaders() });
      if (!res.ok) throw new Error(`${res.status}`);

      const url = URL.createObjectURL(new Blob([JSON.stringify(await res.json(), null, 2)], { type: 'application/json' }));
      const link = document.createElement('a');
      link.href = url;
      link.download = `bio-signature-${todayInTz()}.json`;
      link.click();
      URL.revokeObjectURL(url);
    } catch {
      // Nothing to recover: the button simply does not produce a file, and the tab
      // around it is unaffected.
    }
    setBusy(false);
  };

  return (
    // The note sits BESIDE the button, not inside it: a button nested in a button is invalid
    // markup, and a screen reader cannot reach the inner one.
    <span className="insight-export-wrap">
      <button className="insight-export" onClick={save} disabled={busy}>
        {busy ? 'Preparing\u2026' : 'Export signature'}
      </button>
      <Info label="What is in the file">
        Your normals, sleep clock, weekly shape, recovery, what you respond to, and the fitted weights
        of any forecast that earned its place — in the units you read. No individual readings, no
        specific days, no raw sleep or heart-rate data leave with it.
      </Info>
    </span>
  );
}

function TomorrowSection() {
  return (
    <section className="insight-section">
      <h2 className="insight-h2">
        Tomorrow
        <Info label="How tomorrow is predicted">
          Fitted on your own history and nobody else's, then tested one day at a time against two
          duller answers: assuming tomorrow is like today, and assuming an average day. Assuming
          tomorrow is like today is a genuinely good forecast, so when the model cannot beat it that
          is what you are shown — said plainly rather than hidden.
        </Info>
      </h2>
      <TomorrowRow />
    </section>
  );
}

function BioSignatureSection() {
  const { data } = useQuery({
    queryKey: ['bio-signature'],
    queryFn: () => get<BioSignature>(`${API}/api/health/signature`),
  });
  if (!data) return null;

  const { sleepClock: clock, week, recovery } = data;
  const nothingYet = !clock && !week && recovery.days === null && data.respondsTo.length === 0;

  return (
    <>
      <section className="insight-section">
        <h2 className="insight-h2">
          How you run
          <Info label="What this is">
            A portrait of your own physiology, built only from your record: when you sleep, which day of
            the week is weakest, how long you take to come back from a hard day, and what your numbers
            move with. Nothing here is compared with anyone else. {data.confidence.note}
          </Info>
          <ExportButton />
        </h2>

        {nothingYet ? (
          <p className="insight-muted">
            Not enough history yet. This fills in on its own — the sleep clock needs about three weeks,
            the weekly shape about six.
          </p>
        ) : (
          <div className="insight-sig-grid">
            {clock && (
              <div className="insight-card insight-sig">
                <p className="insight-eyebrow">Your clock</p>
                <div className="ix-clock">
                  <SleepDial bedtime={clock.bedtime} wake={clock.wake} wanderMinutes={clock.bedtimeVariabilityMinutes} />
                  <div className="ix-clock-copy">
                    <p className="insight-sig-lead">{clock.bedtime} → {clock.wake}</p>
                    <p className="insight-sig-note">
                      give or take {Math.round(clock.bedtimeVariabilityMinutes)} min, over {clock.nights} nights
                      <Info label="About your sleep clock">{clock.note}</Info>
                    </p>
                    <ul className="ix-key">
                      <li><i aria-hidden="true" />bed</li>
                      <li className="wake"><i aria-hidden="true" />wake</li>
                    </ul>
                  </div>
                </div>
              </div>
            )}

            {week && (
              <div className="insight-card insight-sig">
                <p className="insight-eyebrow">Your week</p>
                <p className="insight-sig-lead">{week.spread < 3 ? 'even' : week.worst.slice(0, 3)}</p>
                <p className="insight-sig-note">
                  {week.spread < 3
                    ? 'no day stands out'
                    : <>weakest day, by {Math.round(week.spread)} points</>}
                  <Info label="About your week">{week.note}</Info>
                </p>
                {week.spread >= 3 && <WeekBars byDay={week.byDay} worst={week.worst} best={week.best} />}
              </div>
            )}

            <div className="insight-card insight-sig">
              <p className="insight-eyebrow">Coming back</p>
              <p className="insight-sig-lead">{recovery.days === null ? '—' : `${recovery.days}d`}</p>
              <p className="insight-sig-note">
                {recovery.days === null ? 'not enough hard days yet' : `after a hard day, over ${recovery.episodes}`}
                <Info label="About recovery">{recovery.note}</Info>
              </p>
            </div>

            {data.respondsTo.length > 0 && (
              <div className="insight-card insight-sig insight-sig-wide">
                <p className="insight-eyebrow">
                  You respond to
                  <Info label="About these">{data.respondsToCaveat}</Info>
                </p>
                <ul className="insight-sig-list">
                  {data.respondsTo.map(r => (
                    <li key={`${r.driver}-${r.outcome}-${r.lagDays}`}>
                      <span>{r.driver} → {r.outcome}</span>
                      <em>{r.rho > 0 ? '+' : ''}{r.rho.toFixed(2)}{r.lagDays === 1 ? ' next day' : ' same day'}</em>
                    </li>
                  ))}
                </ul>
              </div>
            )}
          </div>
        )}
      </section>
    </>
  );
}

// Seven bars, the weakest and strongest picked out. Colour is decoration here -- both
// ends are named in the text beside them.
function WeekBars({ byDay, worst, best }: { byDay: Record<string, number>; worst: string; best: string }) {
  const order = ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday', 'Sunday'];
  const values = order.map(d => byDay[d]).filter(v => v !== undefined);
  if (values.length === 0) return null;

  const min = Math.min(...values);
  const max = Math.max(...values);
  const span = Math.max(1, max - min);

  return (
    <div className="insight-week">
      {order.map(day => {
        const v = byDay[day];
        if (v === undefined) return <span key={day} className="insight-week-day is-empty" />;
        const height = 18 + ((v - min) / span) * 30;
        const tone = day === worst ? 'is-worst' : day === best ? 'is-best' : '';
        return (
          <span key={day} className={`insight-week-day ${tone}`} title={`${day}: ${v.toFixed(1)}`}>
            <i style={{ height }} />
            <b>{day[0]}</b>
          </span>
        );
      })}
    </div>
  );
}

// ── Has the illness warning ever worked? ──────────────────────────────

// The only claim here that can be checked against what actually happened, so it is.
// A detector that has never caught anything should say so where the reader can see it,
// not only in a test suite.
function IllnessRecord() {
  const { data } = useQuery({
    queryKey: ['illness-eval'],
    queryFn: () => get<IllnessEval>(`${API}/api/health/illness-eval`),
  });
  if (!data) return null;

  const nothingToSay = data.episodeCount === 0 && data.falseAlarmCount === 0;

  return (
    <section className="insight-section">
      <h2 className="insight-h2">
        Has the illness warning worked
        <Info label="How this is checked">
          The days you marked yourself ill are the only ground truth in this system, so they are used to
          score the warning that is supposed to precede them. A signal arriving on the day you marked it
          is confirmation rather than warning, and is counted separately. Runs of signal with no illness
          near them are counted as false alarms even if you did feel rough — that is what keeps the
          number honest.
        </Info>
      </h2>

      {nothingToSay ? (
        <p className="insight-muted">
          Nothing to check against yet. Mark the days you were ill — those days are kept out of your
          baselines anyway — and this becomes a real answer about whether the warning works for you.
        </p>
      ) : (
        <>
          <div className="insight-eval-row">
            <div className="insight-eval-stat">
              <b>{data.caught}/{data.episodeCount}</b>
              <span>illnesses with a signal</span>
            </div>
            <div className="insight-eval-stat">
              <b>{data.medianLeadDays === null ? '—' : `${data.medianLeadDays > 0 ? '+' : ''}${data.medianLeadDays}d`}</b>
              <span>{(data.medianLeadDays ?? 0) > 0 ? 'median warning' : 'median timing'}</span>
            </div>
            <div className="insight-eval-stat">
              <b>{data.falseAlarmCount}</b>
              <span>false alarms</span>
            </div>
          </div>
          <p className="insight-muted insight-lede">{data.verdict}</p>
        </>
      )}
    </section>
  );
}

// ── All baselines (the table view) ───────────────────────────────────────────

function BaselineTable() {
  const { data } = useQuery({
    queryKey: ['insight-baselines'],
    queryFn: () => get<BaselineSet>(`${API}/api/health/baselines`),
  });
  if (!data || data.baselines.length === 0) return null;

  // Valid first. An invalid baseline is still listed -- "not enough data yet" is a real
  // answer, and omitting the metric looks identical to the metric not existing.
  const rows = [...data.baselines].sort(
    (a, b) => Number(b.isValid) - Number(a.isValid) || a.metric.localeCompare(b.metric),
  );

  return (
    <details className="insight-section insight-table-details">
      <summary>
        <span className="insight-h2">Every baseline, in numbers</span>
        <span className="insight-muted"> · {rows.length} metrics over {rows[0]?.windowDays ?? 60} days
          {data.computedOn && `, computed ${data.computedOn}`}</span>
      </summary>
      <div className="insight-table-wrap">
        <table className="insight-table">
          <thead>
            <tr>
              <th>Metric</th>
              <th className="num">Typical</th>
              <th className="num">Spread</th>
              <th className="num">Usual (p25–p75)</th>
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
                <td className="num">{fmtValue(b.metric, b.median)}</td>
                <td className="num">±{b.stdDev.toFixed(1)}</td>
                <td className="num">{fmtValue(b.metric, b.p25)} – {fmtValue(b.metric, b.p75)}</td>
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
    </details>
  );
}

// ── Page ─────────────────────────────────────────────────────────────────────

type TabId = 'overview' | 'patterns' | 'you' | 'check';

const ICON = { fill: 'none', stroke: 'currentColor', strokeWidth: 1.75, strokeLinecap: 'round', strokeLinejoin: 'round' } as const;

const TABS: { id: TabId; label: string; icon: ReactNode }[] = [
  { id: 'overview', label: 'Overview', icon: <svg className="ix-ico" viewBox="0 0 24 24" {...ICON}><rect x="3" y="3" width="7.5" height="9" rx="2"/><rect x="13.5" y="3" width="7.5" height="5.5" rx="2"/><rect x="13.5" y="11.5" width="7.5" height="9.5" rx="2"/><rect x="3" y="15" width="7.5" height="6" rx="2"/></svg> },
  { id: 'patterns', label: 'Patterns', icon: <svg className="ix-ico" viewBox="0 0 24 24" {...ICON}><circle cx="5.5" cy="17.5" r="2.5"/><circle cx="18.5" cy="6.5" r="2.5"/><circle cx="18" cy="18" r="2"/><path d="M7.7 16.2 16.3 8M8 17.7l8 .2"/></svg> },
  { id: 'you', label: 'You', icon: <svg className="ix-ico" viewBox="0 0 24 24" {...ICON}><circle cx="12" cy="8" r="4"/><path d="M4.5 20.5c1-4 4-6 7.5-6s6.5 2 7.5 6"/></svg> },
  { id: 'check', label: 'Check', icon: <svg className="ix-ico" viewBox="0 0 24 24" {...ICON}><path d="M12 3 4.5 6v5.5c0 4.6 3.1 8 7.5 9.5 4.4-1.5 7.5-4.9 7.5-9.5V6z"/><path d="m8.8 12 2.4 2.4 4.2-4.6"/></svg> },
];

const TAB_KEY = 'insight.tab';
const readTab = (): TabId => {
  try {
    const v = localStorage.getItem(TAB_KEY);
    return TABS.some(t => t.id === v) ? (v as TabId) : 'overview';
  } catch { return 'overview'; }
};

function InsightNav({ active, onPick }: { active: TabId; onPick: (id: TabId) => void }) {
  return (
    <nav className="ix-nav" role="tablist" aria-label="Insight sections">
      {TABS.map(t => (
        <button key={t.id} role="tab" aria-selected={active === t.id} onClick={() => onPick(t.id)}>
          {t.icon}
          <span>{t.label}</span>
        </button>
      ))}
    </nav>
  );
}

// The page body without its header, so the phone shell can embed it under its own.
export function InsightContent() {
  const [tab, setTab] = useState<TabId>(readTab);
  const pick = (id: TabId) => {
    setTab(id);
    try { localStorage.setItem(TAB_KEY, id); } catch { /* fine: it just will not be remembered */ }
    window.scrollTo?.({ top: 0 });
  };

  return (
    <div className="insight-page">
        <InsightNav active={tab} onPick={pick} />

        {/* key= restarts the entrance animation on every switch. Only the open panel is
            mounted, so a tab that is never opened never fetches. */}
        <div className="ix-panel" key={tab} role="tabpanel">
          {tab === 'overview' && (
            <>
              <div className="insight-top">
                <Verdict />
                <BioAgeCard />
              </div>
              <TomorrowSection />
              <Findings />
              <TodayVsNormal />
            </>
          )}

          {tab === 'patterns' && (
            <>
              <PatternsSection />
              <Correlations />
              <InterventionsSection />
              <WhatIf />
            </>
          )}

          {tab === 'you' && (
            <>
              <BioSignatureSection />
              <VisitBriefSection />
              <IllnessRecord />
              <BaselineTable />
            </>
          )}

          {tab === 'check' && <SelfCheckSection />}
        </div>
      </div>
  );
}

function InsightPage() {
  return (
    <Shell
      title="Insight"
      subtitle="What the numbers mean, against your own history"
      accent="var(--hx-2)"
      icon={
        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.75" strokeLinecap="round" strokeLinejoin="round">
          <path d="M3 13h3l3 7 4-16 3 9h5"/>
        </svg>
      }
      right={
        <span className="hx-pill">
          Compared with you
          <Info label="What this page compares against">
            Every number here is measured against your own last sixty days, not against a population
            average. That is why a value can be flagged while still being perfectly normal for someone
            else — and why it takes a few weeks of wear before any of it means anything.
          </Info>
        </span>
      }
    >
      <InsightContent />
    </Shell>
  );
}

export default function InsightModule() {
  // The same person as Vitara -- one choice, read by both -- and a fresh cache when it changes.
  const profileKey = useProfileKey();
  const client = useMemo(() => makeModuleQueryClient(5 * 60_000), [profileKey]);

  return (
    <QueryClientProvider client={client} key={profileKey}>
      <InsightPage />
    </QueryClientProvider>
  );
}
