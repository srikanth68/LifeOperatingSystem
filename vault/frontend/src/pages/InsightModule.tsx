import { useMemo } from 'react';
import { QueryClientProvider, useQuery } from '@tanstack/react-query';
import { makeModuleQueryClient } from '../services/moduleQuery';
import { Shell, Info } from '../components/health/HealthKit';
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
//
// Laid out as a reading order: the verdict first (is anything off, and can that answer be
// trusted), then the estimated biological age with the reasons behind it, what is
// currently being flagged, where each metric sits against its own normal today, and only
// then the slower-moving relationships and the raw baseline numbers.
const API = moduleApi(5110);
const VITARA = moduleApi(5100);
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
  const res = await fetch(url, { headers: authHeaders() });
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

const SEVERITY_ORDER: Record<string, number> = { high: 3, notable: 2, info: 1 };
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
      <div className="insight-pulse" aria-hidden="true"><span /><span /><span /></div>
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

  const sorted = [...data.findings].sort(
    (a, b) => (SEVERITY_ORDER[b.severity] ?? 0) - (SEVERITY_ORDER[a.severity] ?? 0)
             || b.daysRunning - a.daysRunning,
  );

  return (
    <section className="insight-section">
      <h2 className="insight-h2">
        What&apos;s being flagged
        <Info label="How something gets flagged">
          A finding appears when a measurement stays away from your own usual range for long enough
          that chance is an unlikely explanation. The dots under each one are how many days it has
          been running, which is the difference between one odd morning and something worth acting on.
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
            {/* How long it has been true is the difference between one odd morning and
                something worth acting on, so it is drawn rather than buried in the text. */}
            <div className="insight-days" aria-label={`Day ${f.daysRunning}`}>
              {Array.from({ length: Math.min(f.daysRunning, 7) }, (_, i) => <span key={i} />)}
              <em>{f.daysRunning === 1 ? 'first seen today' : `day ${f.daysRunning}`}</em>
            </div>
          </li>
        ))}
      </ul>
    </section>
  );
}

// ── Today against your normal ────────────────────────────────────────────────

const Z_MAX = 3;

function TodayVsNormal() {
  const baselinesQ = useQuery({
    queryKey: ['insight-baselines'],
    queryFn: () => get<BaselineSet>(`${API}/api/health/baselines`),
  });
  const derivedQ = useQuery({
    queryKey: ['insight-derived', 14],
    queryFn: () => get<Derived[]>(`${API}/api/health/derived?days=14`),
  });

  const tiles = useMemo(() => {
    const baselines = (baselinesQ.data?.baselines ?? []).filter(b => b.isValid);
    // One tile per metric. Blood pressure and glucose can carry several context
    // baselines; the tile uses the best-supported one and the table lists them all.
    const best = new Map<string, Baseline>();
    for (const b of baselines) {
      const cur = best.get(b.metric);
      if (!cur || b.n > cur.n) best.set(b.metric, b);
    }

    const zSeries = new Map<string, Derived[]>();
    for (const d of derivedQ.data ?? []) {
      if (!d.metric.endsWith('_z')) continue;
      const metric = d.metric.slice(0, -2);
      zSeries.set(metric, [...(zSeries.get(metric) ?? []), d]);
    }

    return [...best.values()]
      .map(b => {
        const series = (zSeries.get(b.metric) ?? []).sort((x, y) => x.day.localeCompare(y.day));
        const latest = series.length ? series[series.length - 1] : undefined;
        return { baseline: b, series, latest };
      })
      .sort((a, b) =>
        Number(!!b.latest) - Number(!!a.latest)
        || Math.abs(b.latest?.value ?? 0) - Math.abs(a.latest?.value ?? 0)
        || label(a.baseline.metric).localeCompare(label(b.baseline.metric)));
  }, [baselinesQ.data, derivedQ.data]);

  if (baselinesQ.isLoading) return null;

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

function Sparkline({ series }: { series: Derived[] }) {
  const W = 240, H = 34, pad = 4;
  const n = series.length;
  const x = (i: number) => pad + (i / Math.max(1, n - 1)) * (W - 2 * pad);
  const y = (v: number) => H / 2 - (Math.max(-Z_MAX, Math.min(Z_MAX, v)) / Z_MAX) * (H / 2 - pad);
  const points = series.map((d, i) => `${x(i).toFixed(1)},${y(d.value).toFixed(1)}`).join(' ');
  const last = series[n - 1];

  return (
    <svg className="insight-spark" viewBox={`0 0 ${W} ${H}`} preserveAspectRatio="none" role="img"
         aria-label={`Last ${n} days relative to typical`}>
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
      )}

      <p className="insight-method">
        {data.method.test} · {data.method.multipleComparisons} · |ρ| ≥ {minRho}
        {data.computedOn && ` · computed ${data.computedOn}`}
      </p>
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
      <div className="insight-page">
        <div className="insight-top">
          <Verdict />
          <BioAgeCard />
        </div>

        <Findings />
        <TodayVsNormal />
        <Correlations />
        <BaselineTable />
      </div>
    </Shell>
  );
}

export default function InsightModule() {
  return (
    <QueryClientProvider client={qc}>
      <InsightPage />
    </QueryClientProvider>
  );
}
