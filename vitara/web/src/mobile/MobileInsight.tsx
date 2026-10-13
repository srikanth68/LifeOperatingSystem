import { useMemo, useState } from 'react';
import type { ReactNode } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { moduleApi } from '../services/apiHost';
import { vitaraHeaders } from '../services/profile';
import { addDays, todayInTz } from '../services/timezone';
import { InsightTabBody, label as metricLabel, fmtValue } from '../pages/InsightModule';
import type { TabId } from '../pages/InsightModule';
import { ScenarioSection } from '../components/health/Direction';
import type { Readiness } from '../pages/VitaraModule';
import { dateLine } from './Parts';

// Insight, drawn for the phone (design canvas: Main, Direction, Explain).
//
// Picture first, a number on every chart, and the long sentence behind "Why?": each card
// opens one sheet that says what the picture means and how sure it is. Overview and
// Direction are designed here; Patterns, You and Check borrow the desktop sections until
// they get their own pass.
const API = moduleApi(5110);
const VITARA = moduleApi(5100);

async function get<T>(url: string): Promise<T> {
  const res = await fetch(url, { headers: vitaraHeaders() });
  if (!res.ok) throw new Error(`${res.status} ${res.statusText}`);
  return res.json() as Promise<T>;
}

// ── Types (only the fields drawn here) ──────────────────────────────────────

interface Finding { key: string; type: string; metric: string; severity: string; summary: string; daysRunning: number }
interface Summary {
  computedThrough: string | null;
  daysBehind: number | null;
  activeFindings: number;
  coverage?: { metricsWithNormal: number; stillLearning: number; stillLearningMetrics: string[] };
  findings: Finding[];
}
interface Baseline { metric: string; mean: number; stdDev: number; median: number; p25: number; p75: number; n: number; isValid: boolean }
interface Derived { metric: string; day: string; value: number; inputs: string | null }
interface Evidence { modelMae: number | null; persistenceMae: number; testedOnDays: number; modelWins: boolean; verdict: string }
interface Forecast { day: string; value: number; low: number; high: number; method: 'model' | 'today' | 'none'; basis: string; evidence: Evidence }
interface Split { cut: number | null; low: number; lowN: number | null; high: number; highN: number | null }
interface Correlation { driver: string; outcome: string; lagDays: number; rho: number; n: number; split?: Split | null }
interface CatalogueRow { key: string; label: string; unit: string; decimals: number }
interface Hold {
  metric: string; label: string; unit: string | null; signature: string; changePoint: string; direction: string;
  before: number; after: number; open: boolean; heldForDays: number;
}
interface DomainMetric { metric: string; label: string; unit: string | null; latest: number | null; trajectory: string; trajectoryReason: string }
interface Domain { key: string; label: string; metrics: DomainMetric[]; gaps: { missingKeyTests: string[] } }
interface Projection {
  label: string; projected: boolean; horizonDays: number; currentLevel: number | null;
  projectedLow: number | null; projectedHigh: number | null; basis: string; grade: string; caveat: string;
  refusal: string | null; history: { day: string; value: number; n: number }[];
}

// ── Numbers ─────────────────────────────────────────────────────────────────

function useCatalogue() {
  const q = useQuery({
    queryKey: ['insight-metrics'],
    queryFn: () => get<{ metrics: CatalogueRow[] }>(`${API}/api/health/metrics`),
    staleTime: 10 * 60_000,
  });
  return useMemo(() => new Map((q.data?.metrics ?? []).map(m => [m.key, m])), [q.data]);
}

// "1 h 32" for minutes, "6.5 h" for hours, otherwise the number at the catalogue's precision.
function num(v: number | null | undefined, unit?: string | null, decimals = 0): string {
  if (v == null || !Number.isFinite(v)) return '—';
  if (unit === 'min') {
    const m = Math.round(Math.abs(v));
    const s = v < 0 ? '−' : '';
    return m >= 60 ? `${s}${Math.floor(m / 60)} h ${String(m % 60).padStart(2, '0')}` : `${s}${m} min`;
  }
  const n = Math.abs(v) >= 1000 ? Math.round(v).toLocaleString('en-US') : v.toFixed(decimals);
  return unit && unit !== 'score' && unit !== 'steps' ? `${n} ${unit}` : n;
}

function useNamer() {
  const cat = useCatalogue();
  return {
    name: (m: string) => cat.get(m)?.label ?? metricLabel(m),
    fmt: (m: string, v: number | null | undefined, withUnit = true) => {
      const c = cat.get(m);
      if (!c) return v == null ? '—' : fmtValue(m, v);
      return num(v, withUnit ? c.unit : (c.unit === 'min' ? 'min' : null), c.decimals);
    },
  };
}

// Each metric's normal, today's reading against it, and its last two weeks as real values.
// The z-scores carry the baseline they were measured against, so the value is rebuilt
// exactly: value = mean + z × sd.
function useTiles() {
  const bq = useQuery({ queryKey: ['insight-baselines'], queryFn: () => get<{ baselines: Baseline[] }>(`${API}/api/health/baselines`) });
  const dq = useQuery({ queryKey: ['insight-derived', 60], queryFn: () => get<Derived[]>(`${API}/api/health/derived?days=60`) });

  const tiles = useMemo(() => {
    const best = new Map<string, Baseline>();
    for (const b of bq.data?.baselines ?? []) {
      if (!b.isValid) continue;
      const cur = best.get(b.metric);
      if (!cur || b.n > cur.n) best.set(b.metric, b);
    }
    const since = addDays(todayInTz(), -14);
    const zs = new Map<string, { day: string; z: number; value: number }[]>();
    for (const d of dq.data ?? []) {
      if (!d.metric.endsWith('_z') || d.day < since) continue;
      const metric = d.metric.slice(0, -2);
      let mean: number | null = null, sd: number | null = null;
      try { const j = d.inputs ? JSON.parse(d.inputs) : null; mean = j?.mean ?? null; sd = j?.sd ?? null; } catch { /* no inputs: no value */ }
      const b = best.get(metric);
      const value = mean != null && sd != null ? mean + d.value * sd : b ? b.mean + d.value * b.stdDev : NaN;
      zs.set(metric, [...(zs.get(metric) ?? []), { day: d.day, z: d.value, value }]);
    }
    return [...best.values()].map(b => {
      const series = (zs.get(b.metric) ?? []).sort((x, y) => x.day.localeCompare(y.day));
      const latest = series[series.length - 1];
      return { metric: b.metric, b, series, latest };
    }).sort((x, y) => Math.abs(y.latest?.z ?? 0) - Math.abs(x.latest?.z ?? 0));
  }, [bq.data, dq.data]);

  return { tiles, loading: bq.isLoading || dq.isLoading };
}

type Tile = ReturnType<typeof useTiles>['tiles'][number];

// ── The Why sheet ───────────────────────────────────────────────────────────

type Why = { title: string; body: ReactNode };

function WhySheet({ why, close }: { why: Why | null; close: () => void }) {
  return (
    <>
      <div className={`vm-scrim ${why ? 'is-on' : ''}`} onClick={close} aria-hidden="true" />
      <div className={`vm-sheet vmi-why ${why ? 'is-on' : ''}`} role="dialog" aria-label={why?.title ?? 'Why'} aria-hidden={!why}>
        <span className="vm-grab" aria-hidden="true" />
        <div className="vmi-why-head">
          <h2>{why?.title}</h2>
          <button className="vmi-close" onClick={close} aria-label="Close" tabIndex={why ? 0 : -1}>×</button>
        </div>
        <div className="vmi-why-body">{why?.body}</div>
      </div>
    </>
  );
}

const WhyLink = ({ onClick, children = 'Why? ›' }: { onClick: () => void; children?: ReactNode }) => (
  <button type="button" className="vmi-link" onClick={onClick}>{children}</button>
);

// ── Charts ──────────────────────────────────────────────────────────────────

const f1 = (n: number) => n.toFixed(1);

// A line against the person's usual band, with the band's edges and the latest value written on it.
function BandLine({ values, band, label: fmt, width = 318, height = 64, tone = 'var(--gold)' }: {
  values: number[]; band: [number, number] | null; label: (v: number) => string; width?: number; height?: number; tone?: string;
}) {
  if (values.length < 2) return null;
  const all = values.concat(band ?? []);
  let lo = Math.min(...all), hi = Math.max(...all);
  const pad = (hi - lo || 1) * 0.15; lo -= pad; hi += pad;
  const L = 26, R = 30;
  const X = (i: number) => L + (i / (values.length - 1)) * (width - L - R);
  const Y = (v: number) => height - 6 - ((v - lo) / (hi - lo)) * (height - 16);
  const last = values[values.length - 1];
  return (
    <svg className="vmi-chart" viewBox={`0 0 ${width} ${height}`} width="100%" height={height} role="img"
         aria-label={band ? `Latest ${fmt(last)}, usual ${fmt(band[0])} to ${fmt(band[1])}` : `Latest ${fmt(last)}, last ${values.length} days`}>
      {band ? <>
        <rect x={L} y={f1(Y(band[1]))} width={width - L - R} height={f1(Math.max(3, Y(band[0]) - Y(band[1])))} rx="6" className="vmi-band" />
        <text x="0" y={f1(Y(band[1]) + 4)} className="vmi-t vmi-t-teal">{fmt(band[1])}</text>
        <text x="0" y={f1(Y(band[0]) + 4)} className="vmi-t vmi-t-teal">{fmt(band[0])}</text>
      </> : <text x="0" y={f1(Y(values[0]) + 4)} className="vmi-t">{fmt(values[0])}</text>}
      <polyline points={values.map((v, i) => `${f1(X(i))},${f1(Y(v))}`).join(' ')} className="vmi-line" />
      <circle cx={f1(X(values.length - 1))} cy={f1(Y(last))} r="4" style={{ fill: tone }} />
      <text x={width} y={f1(Y(last) + 4)} textAnchor="end" className="vmi-t vmi-t-b" style={{ fill: tone }}>{fmt(last)}</text>
    </svg>
  );
}

// Where today sits on ±3σ, with the usual band (p25–p75 in σ) behind it.
function Strip({ t }: { t: Tile }) {
  const z = t.latest?.z;
  const P = (s: number) => `${((Math.max(-3, Math.min(3, s)) + 3) / 6) * 100}%`;
  const zl = t.b.stdDev ? (t.b.p25 - t.b.mean) / t.b.stdDev : -0.7;
  const zh = t.b.stdDev ? (t.b.p75 - t.b.mean) / t.b.stdDev : 0.7;
  const off = z != null && Math.abs(z) >= 1;
  return (
    <div className="vmi-strip" aria-hidden="true">
      <i className="vmi-strip-band" style={{ left: P(zl), right: `calc(100% - ${P(zh)})` }} />
      {z != null && <b className={`vmi-strip-dot ${off ? 'is-off' : ''}`} style={{ left: `calc(${P(z)} - 6px)` }} />}
    </div>
  );
}

// ── Overview ────────────────────────────────────────────────────────────────

function Verdict({ why }: { why: (w: Why) => void }) {
  const sq = useQuery({ queryKey: ['insight-summary'], queryFn: () => get<Summary>(`${API}/api/health/summary`) });
  const { tiles, loading } = useTiles();
  const { name, fmt } = useNamer();

  if (sq.isLoading || loading) return <section className="vm-tile vmi-skel" aria-busy="true" />;
  if (!sq.data) return <section className="vm-tile vm-msg"><b>Insight isn't reachable</b><p>Pull down to try again.</p></section>;

  const read = tiles.filter(t => t.latest);
  const off = read.filter(t => Math.abs(t.latest!.z) >= 1);
  const normal = read.length - off.length;
  const learning = sq.data.coverage?.stillLearning ?? 0;
  const notToday = tiles.length - read.length;
  const total = normal + off.length + learning;

  if (sq.data.computedThrough === null) {
    return <section className="vm-tile vm-msg"><b>Not analysed yet</b><p>Nothing has been checked, so no findings does not mean nothing is wrong.</p></section>;
  }

  // The ring: normal, off, learning, as arcs of one circle.
  const C = 2 * Math.PI * 48, gap = 4;
  const arc = (n: number) => (total ? (n / total) * C : 0);
  const segs = [
    { n: normal, cls: 'vmi-arc-ok' }, { n: off.length, cls: 'vmi-arc-off' }, { n: learning, cls: 'vmi-arc-learn' },
  ];
  let at = 0;

  return (
    <section className="vm-tile vmi-verdict" aria-label="Against your normal">
      <svg width="116" height="116" viewBox="0 0 116 116" role="img"
           aria-label={`${normal} of ${total} measures inside your normal, ${off.length} off it, ${learning} still learning`}>
        <circle cx="58" cy="58" r="48" className="vmi-arc-track" />
        {segs.map(s => {
          const len = Math.max(0, arc(s.n) - (s.n ? gap : 0));
          const el = s.n ? <circle key={s.cls} cx="58" cy="58" r="48" className={s.cls}
                                   strokeDasharray={`${f1(len)} ${f1(C)}`} strokeDashoffset={f1(-at)} transform="rotate(-90 58 58)" /> : null;
          at += arc(s.n);
          return el;
        })}
        <text x="58" y="58" textAnchor="middle" className="vmi-ring-num">{normal}</text>
        <text x="58" y="76" textAnchor="middle" className="vmi-t">of {total} normal</text>
      </svg>
      <div className="vmi-verdict-body">
        <p className={`vmi-verdict-word ${off.length ? 'is-off' : 'is-ok'}`}>
          {off.length ? `${off.length} off your normal` : 'All inside your normal'}
        </p>
        {off.slice(0, 3).map(t => (
          <button key={t.metric} type="button" className="vmi-chip" onClick={() => why({
            title: name(t.metric),
            body: <p>Today {fmt(t.metric, t.latest!.value)}. Your usual is {fmt(t.metric, t.b.p25)} to {fmt(t.metric, t.b.p75)}: where the middle half of your last sixty days fell. It is {Math.abs(t.latest!.z).toFixed(1)} standard deviations {t.latest!.z > 0 ? 'above' : 'below'} your typical {fmt(t.metric, t.b.median)}.</p>,
          })}>
            <svg width="16" height="16" viewBox="0 0 24 24" aria-hidden="true"><path d={t.latest!.z > 0 ? 'M12 19V5M6 11l6-6 6 6' : 'M12 5v14M6 13l6 6 6-6'} /></svg>
            <span>{name(t.metric)}</span>
            <b>{fmt(t.metric, t.latest!.value, false)}</b>
            <small>/ {fmt(t.metric, t.b.p25, false)}–{fmt(t.metric, t.b.p75, false)}</small>
          </button>
        ))}
        <div className="vmi-keys">
          <span><i className="k-ok" />{normal} normal</span>
          {learning > 0 && <span><i className="k-learn" />{learning} learning</span>}
          {notToday > 0 && <span><i className="k-none" />{notToday} no reading</span>}
        </div>
        <WhyLink onClick={() => why({
          title: 'How the ring is counted',
          body: <>
            <p>Each measure is checked against your own usual, not a population average. Inside means within one standard deviation of your typical day.</p>
            <p className="vmi-muted">Learning means a measure has fewer than 21 readings, so it is recorded but not judged yet.{sq.data!.coverage?.stillLearningMetrics.length ? ` Still learning: ${sq.data!.coverage.stillLearningMetrics.join(', ')}.` : ''}</p>
            <p className="vmi-muted">There is deliberately no single health score.</p>
          </>,
        })} />
      </div>
    </section>
  );
}

function WhatMovesYou({ why, more }: { why: (w: Why) => void; more: () => void }) {
  const q = useQuery({ queryKey: ['insight-correlations'], queryFn: () => get<{ correlations: Correlation[]; caveat: string }>(`${API}/api/health/correlations`) });
  const { name, fmt } = useNamer();
  const list = [...(q.data?.correlations ?? [])].sort((a, b) => Number(!!b.split) - Number(!!a.split) || Math.abs(b.rho) - Math.abs(a.rho));
  const c = list[0];

  if (!q.data) return null;
  if (!c) {
    return (
      <section className="vm-tile vmi-moves">
        <p className="vmi-eyebrow vmi-violet">What moves you</p>
        <p className="vmi-muted">Nothing yet. A relationship needs 30 paired days and has to survive a test for chance across every pair.</p>
      </section>
    );
  }

  // Said as "the outcome runs higher when the driver is over its middle value": the driver's
  // own words can run either way (more hours between caffeine and bed is EARLIER caffeine),
  // so the sentence names the cut instead of saying "more caffeine".
  const s = c.split;
  const when = c.lagDays === 1 ? ' the next day' : '';
  const drv = name(c.driver).toLowerCase(), out = name(c.outcome);
  const line = s
    ? `${out}${when} runs ${s.high > s.low ? 'higher' : 'lower'} when ${drv} is over ${fmt(c.driver, s.cut)}`
    : `${out}${when} moves ${c.rho > 0 ? 'with' : 'against'} ${drv}`;

  return (
    <section className="vm-tile vmi-moves" aria-label="What moves you">
      <p className="vmi-eyebrow vmi-violet"><span>What moves you</span><em>from {c.n} of your days</em></p>
      <p className="vmi-moves-line">{line}</p>
      {s && (
        <div className="vmi-pair" role="img" aria-label={`${out}: ${fmt(c.outcome, s.low)} when ${drv} is up to ${fmt(c.driver, s.cut)}, ${fmt(c.outcome, s.high)} when over`}>
          <div><small>{drv} ≤ {fmt(c.driver, s.cut)}</small><b>{fmt(c.outcome, s.low)}</b><small>{s.lowN} days</small></div>
          <div><small>{drv} &gt; {fmt(c.driver, s.cut)}</small><b className="vmi-violet">{fmt(c.outcome, s.high)}</b><small>{s.highN} days</small></div>
        </div>
      )}
      <p className="vmi-muted vmi-small">strength <b>ρ {c.rho > 0 ? '+' : '−'}{Math.abs(c.rho).toFixed(2)}</b>{list.length > 1 ? ` · ${list.length - 1} more found` : ''}</p>
      <div className="vmi-links">
        <WhyLink onClick={() => why({
          title: 'What moves you',
          body: <>
            {s && <p>On days {drv} was over {fmt(c.driver, s.cut)}, {out.toLowerCase()}{when} was typically {fmt(c.outcome, s.high)}. On the others, {fmt(c.outcome, s.low)}.</p>}
            <p className="vmi-muted">{q.data!.caveat}</p>
            <p className="vmi-muted">Tested with a rank correlation over {c.n} paired days, and kept only after a correction for testing many pairs at once.</p>
          </>,
        })} />
        {list.length > 1 && <WhyLink onClick={more}>All of them ›</WhyLink>}
      </div>
    </section>
  );
}

function TomorrowCard({ why }: { why: (w: Why) => void }) {
  const fq = useQuery({ queryKey: ['forecasts'], queryFn: () => get<{ readiness: Forecast | null }>(`${API}/api/health/forecast`) });
  const rq = useQuery<Readiness[]>({ queryKey: ['readiness', 14], queryFn: () => get(`${VITARA}/api/readiness?days=14`) });
  const f = fq.data?.readiness;
  if (!f) return null;

  const week = [...(rq.data ?? [])].sort((a, b) => a.day.localeCompare(b.day)).filter(r => r.score != null).slice(-7).map(r => r.score!);
  const W = 350, H = 96;
  const all = week.concat([f.low, f.high]);
  const lo = Math.min(...all) - 4, hi = Math.max(...all) + 4;
  const Y = (v: number) => 80 - ((v - lo) / (hi - lo)) * 64;
  const X = (i: number) => 12 + (i / Math.max(1, week.length - 1)) * 258;
  const lastI = week.length - 1;
  const ev = f.evidence;
  const max = Math.max(ev.modelMae ?? 0, ev.persistenceMae, 0.1);
  const naive = f.method !== 'model';

  return (
    <section className="vm-tile vmi-tomorrow" aria-label="Tomorrow">
      <p className="vmi-eyebrow vmi-blue"><span>Tomorrow's readiness</span>
        <WhyLink onClick={() => why({ title: 'How good is this forecast?', body: <><p>{ev.verdict}</p><p className="vmi-muted">{f.basis}</p></> })}>How good is this? ›</WhyLink>
      </p>
      {week.length >= 2 && (
        <svg viewBox={`0 0 ${W} ${H}`} width="100%" height={H} role="img"
             aria-label={`Readiness over the last ${week.length} days, and tomorrow's likely range ${Math.round(f.low)} to ${Math.round(f.high)}`}>
          <polyline points={week.map((v, i) => `${f1(X(i))},${f1(Y(v))}`).join(' ')} className="vmi-line vmi-line-soft" />
          {week.map((v, i) => <text key={i} x={f1(X(i))} y={f1(Y(v) - 8)} textAnchor="middle" className={`vmi-t ${i === lastI ? 'vmi-t-b' : ''}`}>{Math.round(v)}</text>)}
          <circle cx={f1(X(lastI))} cy={f1(Y(week[lastI]))} r="4" className="vmi-dot" />
          <line x1={f1(X(lastI) + 4)} y1={f1(Y(week[lastI]))} x2="300" y2={f1(Y(f.value))} className="vmi-dash" />
          <rect x="300" y={f1(Y(f.high))} width="26" height={f1(Math.max(12, Y(f.low) - Y(f.high)))} rx="13" className="vmi-capsule" />
          <circle cx="313" cy={f1(Y(f.value))} r="5" className="vmi-capsule-dot" />
          <text x="296" y={f1(Y(f.high) + 4)} textAnchor="end" className="vmi-t vmi-t-blue">{Math.round(f.high)}</text>
          <text x="296" y={f1(Y(f.low) + 4)} textAnchor="end" className="vmi-t vmi-t-blue">{Math.round(f.low)}</text>
          <text x="313" y="94" textAnchor="middle" className="vmi-t vmi-t-blue">tomorrow</text>
          <text x="12" y="94" className="vmi-t vmi-t-dim">last {week.length} days</text>
        </svg>
      )}
      <div className="vmi-big"><b>{Math.round(f.low)}–{Math.round(f.high)}</b><span>likely, most likely {Math.round(f.value)}</span></div>
      {/* Its track record as two bars, the forecast's average miss against guessing
          "tomorrow = today". Not "closer on N days": a forecast that wins big on the hard
          days and loses by a hair on the easy ones is better and still closer on fewer. */}
      <div className="vmi-record">
        <p className="vmi-row vmi-muted"><span>Track record</span><span>tested on {ev.testedOnDays} days</span></p>
        {ev.modelMae != null && (
          <div className="vmi-bar"><span>This forecast</span><i><em style={{ width: `${(ev.modelMae / max) * 100}%` }} className="is-model" /></i><b>off by {ev.modelMae.toFixed(1)}</b></div>
        )}
        <div className="vmi-bar"><span>“Tomorrow = today”</span><i><em style={{ width: `${(ev.persistenceMae / max) * 100}%` }} /></i><b>off by {ev.persistenceMae.toFixed(1)}</b></div>
        <p className="vmi-muted vmi-small">
          {naive ? 'Not beating the plain guess yet, so tomorrow is shown as today.' : 'Shorter is better. It is used because it beats the plain guess.'}
        </p>
      </div>
    </section>
  );
}

function FindingCards({ why }: { why: (w: Why) => void }) {
  const sq = useQuery({ queryKey: ['insight-summary'], queryFn: () => get<Summary>(`${API}/api/health/summary`) });
  const { tiles } = useTiles();
  const dq = useQuery({ queryKey: ['insight-derived', 60], queryFn: () => get<Derived[]>(`${API}/api/health/derived?days=60`) });
  const { name, fmt } = useNamer();
  const findings = sq.data?.findings ?? [];
  if (findings.length === 0) return null;
  const byMetric = new Map(tiles.map(t => [t.metric, t]));
  // Findings about a computed measure (sleep owed, load) have no z-score; their own daily
  // values are the picture instead, without a band.
  const since = addDays(todayInTz(), -14);
  const computed = (m: string) => (dq.data ?? []).filter(d => d.metric === m && d.day >= since)
    .sort((a, b) => a.day.localeCompare(b.day)).map(d => d.value);

  return (
    <section className="vmi-stack" aria-label="Findings">
      <h2 className="vmi-h2"><span>Flagged</span><em>{findings.length} {findings.length === 1 ? 'finding' : 'findings'}</em></h2>
      {findings.map(f => {
        const t = byMetric.get(f.metric);
        const values = t ? t.series.map(s => s.value).filter(Number.isFinite) : computed(f.metric);
        const z = t?.latest?.z;
        const word = z != null ? (z > 0 ? 'high' : 'low') : f.type === 'drift' ? 'drifting' : 'building';
        return (
          <div key={f.key} className="vm-tile vmi-finding">
            <div className="vmi-row">
              <b className="vmi-finding-name">{name(f.metric)}</b>
              <span className={`vmi-tag sev-${f.severity}`}>{word} · day {f.daysRunning}</span>
            </div>
            {values.length >= 2 && <BandLine values={values} band={t ? [t.b.p25, t.b.p75] : null} label={v => fmt(f.metric, v, false)} />}
            <div className="vmi-row vmi-muted">
              <span>{t?.latest ? <><b className="vmi-num">{fmt(f.metric, t.latest.value)}</b> · usual {fmt(f.metric, t.b.p25, false)}–{fmt(f.metric, t.b.p75, false)}</>
                : values.length ? <><b className="vmi-num">{fmt(f.metric, values[values.length - 1])}</b> · {f.daysRunning === 1 ? 'first seen today' : `${f.daysRunning} days running`}</>
                : f.daysRunning === 1 ? 'first seen today' : `running ${f.daysRunning} days`}</span>
              <WhyLink onClick={() => why({ title: name(f.metric), body: <><p>{f.summary}</p><p className="vmi-muted">Measured against your own history, not a population average. Not a diagnosis.</p></> })} />
            </div>
          </div>
        );
      })}
    </section>
  );
}

function TodayStrips() {
  const { tiles } = useTiles();
  const { name, fmt } = useNamer();
  const read = tiles.filter(t => t.latest);
  if (read.length === 0) return null;
  return (
    <section className="vm-tile" aria-label="Today against your normal">
      <h2 className="vmi-h2"><span>Today</span><em>band = your usual</em></h2>
      <div className="vmi-strips">
        {read.map(t => (
          <div key={t.metric} className="vmi-strip-row">
            <span className="vmi-muted">{name(t.metric)}</span>
            <Strip t={t} />
            <span className="vmi-strip-val">
              <b className={Math.abs(t.latest!.z) >= 1 ? 'vmi-gold' : ''}>{fmt(t.metric, t.latest!.value, false)}</b>
              <small>{fmt(t.metric, t.b.p25, false)}–{fmt(t.metric, t.b.p75)}</small>
            </span>
          </div>
        ))}
      </div>
    </section>
  );
}

// ── Direction ───────────────────────────────────────────────────────────────

const useProjection = (metric: string, historyFrom?: string) => useQuery({
  queryKey: ['projection', metric, 'history', historyFrom ?? ''],
  queryFn: () => get<Projection>(`${API}/api/health/projection?metric=${encodeURIComponent(metric)}&horizonDays=90${historyFrom ? `&historyFrom=${historyFrom}` : ''}`),
});

function HoldCard({ h, why, addChange }: { h: Hold; why: (w: Why) => void; addChange: () => void }) {
  const qc = useQueryClient();
  // Six weeks before the step, so the picture shows the level it stepped from.
  const p = useProjection(h.metric, addDays(h.changePoint, -42));
  const accept = useMutation({
    mutationFn: async () => {
      const r = await fetch(`${VITARA}/api/regime-acceptances`, {
        method: 'POST',
        headers: { ...vitaraHeaders(), 'Content-Type': 'application/json' },
        body: JSON.stringify({ metric: h.metric, signature: h.signature, changePoint: h.changePoint }),
      });
      const json = await r.json();
      if (!r.ok) throw new Error(json?.error ?? `${r.status}`);
      return json as { note: string };
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: ['regime-holds'] }),
  });

  const hist = p.data?.history ?? [];
  const W = 350, H = 110;
  const vals = hist.map(x => x.value).concat([h.before, h.after]);
  const lo = Math.min(...vals), hi = Math.max(...vals), pad = (hi - lo || 1) * 0.25;
  const Y = (v: number) => 84 - ((v - (lo - pad)) / (hi - lo + 2 * pad)) * 64;
  const X = (i: number) => 4 + (i / Math.max(1, hist.length - 1)) * (W - 8);
  const stepI = hist.findIndex(x => x.day >= h.changePoint);
  const sx = stepI >= 0 ? X(stepI) : W / 2;
  const u = h.unit;
  const n = (v: number) => num(v, u === 'min' ? 'min' : null, Math.abs(v) < 10 ? 1 : 0);
  const stepDay = new Date(h.changePoint + 'T12:00:00').toLocaleDateString('en-GB', { day: 'numeric', month: 'short' });

  return (
    <section className="vm-tile vmi-hold" aria-label="Normal being held">
      <p className="vmi-eyebrow vmi-gold"><span>Your normal is being held</span><em>{h.heldForDays} days</em></p>
      <b className="vmi-finding-name">{h.label}</b>
      {hist.length >= 3 && (
        <svg viewBox={`0 0 ${W} ${H}`} width="100%" height={H} role="img"
             aria-label={`${h.label} stepped from ${n(h.before)} to ${n(h.after)} around ${stepDay}; your normal is still held at ${n(h.before)}`}>
          <line x1={f1(sx)} y1={f1(Y(h.before))} x2={W} y2={f1(Y(h.before))} className="vmi-held" />
          <polyline points={hist.map((x, i) => `${f1(X(i))},${f1(Y(x.value))}`).join(' ')} className="vmi-line" />
          <line x1={f1(sx)} y1="10" x2={f1(sx)} y2="92" className="vmi-step" />
          <text x={f1(sx + 4)} y="18" className="vmi-t vmi-t-gold">step, {stepDay}</text>
          <text x="4" y="104" className="vmi-t">held at {n(h.before)}</text>
          <text x={W - 2} y="18" textAnchor="end" className="vmi-t vmi-t-b">now {n(h.after)}</text>
        </svg>
      )}
      <div className="vmi-buttons">
        <button type="button" className="vmi-btn is-primary" disabled={accept.isPending || accept.isSuccess} onClick={() => accept.mutate()}>It's my new normal</button>
        <button type="button" className="vmi-btn" onClick={addChange}>Add what changed</button>
      </div>
      {accept.data && <p className="vmi-muted vmi-small">{accept.data.note}</p>}
      {accept.error && <p className="vmi-small vmi-coral">{(accept.error as Error).message}</p>}
      <WhyLink onClick={() => why({ title: 'Why your normal is held', body: <HoldWhy h={h} n={n} stepDay={stepDay} /> })}>Why is it held? ›</WhyLink>
    </section>
  );
}

function HoldWhy({ h, n, stepDay }: { h: Hold; n: (v: number) => string; stepDay: string }) {
  return (
    <>
      <div className="vmi-three">
        <div><svg width="70" height="40" viewBox="0 0 70 40" aria-hidden="true"><polyline points="2,32 24,32 24,10 68,10" className="vmi-line" /></svg><span>It stepped the wrong way</span></div>
        <div><svg width="40" height="40" viewBox="0 0 24 24" aria-hidden="true" className="vmi-q"><circle cx="12" cy="12" r="9" /><path d="M9.5 9.5a2.5 2.5 0 1 1 3.5 2.3c-.6.3-1 .8-1 1.5V14M12 17.5v.01" /></svg><span>Nothing you recorded explains it</span></div>
        <div><svg width="70" height="40" viewBox="0 0 70 40" aria-hidden="true"><line x1="2" y1="30" x2="68" y2="30" className="vmi-held" /><polyline points="2,30 24,30 24,10 68,10" className="vmi-line vmi-line-dim" /></svg><span>So the old level stays your normal</span></div>
      </div>
      <p>{h.label} moved from about {n(h.before)} to {n(h.after)} around {stepDay} and has stayed there. Nothing recorded since, like a new medication, a new ring or an illness, lines up with it.</p>
      <p className="vmi-muted">Most apps would quietly make {n(h.after)} your new normal within two months. Vitara keeps comparing you with {n(h.before)}, so the change stays visible.</p>
      <p className="vmi-eyebrow">It lets go when</p>
      <ol className="vmi-steps">
        <li>you record what changed</li>
        <li>you say it's your new normal</li>
        <li>it comes back on its own</li>
      </ol>
      <p className="vmi-grade"><b>B</b><span>Evidence: strong, depends on context. Not a diagnosis.</span></p>
    </>
  );
}

const DOT: Record<string, string> = { improving: 'd-good', worsening: 'd-bad', rising: 'd-move', falling: 'd-move', stable: 'd-steady' };
const MOVED = ['improving', 'worsening', 'rising', 'falling'];

function Areas({ domains }: { domains: Domain[] }) {
  return (
    <section className="vm-tile" aria-label="Areas of health">
      <h2 className="vmi-h2"><span>Each area</span><em>one dot per measure</em></h2>
      <div className="vmi-areas">
        {domains.map(d => {
          const c = (s: string) => d.metrics.filter(m => m.trajectory === s).length;
          const unknown = d.metrics.filter(m => !DOT[m.trajectory]).length;
          const moved = d.metrics.find(m => MOVED.includes(m.trajectory));
          const parts = [
            c('improving') && `${c('improving')} better`, c('worsening') && `${c('worsening')} worse`,
            (c('rising') + c('falling')) && `${c('rising') + c('falling')} moving`, c('stable') && `${c('stable')} steady`, unknown && `${unknown} ?`,
          ].filter(Boolean).join(' · ');
          return (
            <div key={d.key} className="vmi-area">
              <b>{d.label}</b>
              <div className="vmi-dots" role="img" aria-label={parts}>
                {d.metrics.map(m => <i key={m.metric} className={DOT[m.trajectory] ?? 'd-none'} title={`${m.label}: ${m.trajectory}`} />)}
              </div>
              <span className="vmi-area-note">
                {moved ? `${moved.label.toLowerCase()} ${num(moved.latest, moved.unit, Math.abs(moved.latest ?? 0) < 10 ? 1 : 0)}`
                  : d.gaps.missingKeyTests[0] ? <em>+ {d.gaps.missingKeyTests[0]}</em> : null}
              </span>
              <span className="vmi-muted vmi-small">{parts}</span>
            </div>
          );
        })}
      </div>
      <div className="vmi-keys vmi-keys-wrap">
        <span><i className="d-good" />improving</span><span><i className="d-bad" />worsening</span>
        <span><i className="d-steady" />steady</span><span><i className="d-none" />not enough data</span>
        <span><em>+</em> the test that would help</span>
      </div>
    </section>
  );
}

function MovingChart({ m, why }: { m: DomainMetric; why: (w: Why) => void }) {
  const p = useProjection(m.metric);
  const hist = p.data?.history ?? [];
  if (hist.length < 3) return null;
  const pr = p.data!;
  const n = (v: number) => num(v, m.unit === 'min' ? 'min' : null, Math.abs(v) < 10 ? 1 : 0);
  const W = 350, H = 96, split = 238;
  const vals = hist.map(x => x.value).concat(pr.projected && pr.projectedLow != null && pr.projectedHigh != null ? [pr.projectedLow, pr.projectedHigh] : []);
  const lo = Math.min(...vals), hi = Math.max(...vals), pad = (hi - lo || 1) * 0.2;
  const Y = (v: number) => 80 - ((v - (lo - pad)) / (hi - lo + 2 * pad)) * 66;
  const X = (i: number) => 4 + (i / Math.max(1, hist.length - 1)) * (split - 4);
  const last = hist[hist.length - 1].value;
  const tone = m.trajectory === 'worsening' ? 'warn' : m.trajectory === 'improving' ? 'good' : 'move';

  return (
    <div className="vmi-moving">
      <div className="vmi-row"><b className="vmi-finding-name">{m.label}</b><span className={`vmi-tag t-${tone}`}>{m.trajectory}</span></div>
      <svg viewBox={`0 0 ${W} ${H}`} width="100%" height={H} role="img"
           aria-label={pr.projected ? `If it continued for ${pr.horizonDays} days it would be between ${n(pr.projectedLow!)} and ${n(pr.projectedHigh!)}` : `${m.label}, last 90 days`}>
        <polyline points={hist.map((x, i) => `${f1(X(i))},${f1(Y(x.value))}`).join(' ')} className="vmi-line" />
        <text x="4" y={f1(Y(hist[0].value) + 14)} className="vmi-t">{n(hist[0].value)}</text>
        <text x={split - 6} y={f1(Y(last) - 8)} textAnchor="end" className="vmi-t vmi-t-b">{n(last)} now</text>
        {pr.projected && pr.projectedLow != null && pr.projectedHigh != null ? (
          <>
            <line x1={split} y1="4" x2={split} y2="90" className="vmi-axis" />
            <polygon points={`${split},${f1(Y(last))} ${W - 4},${f1(Y(pr.projectedHigh))} ${W - 4},${f1(Y(pr.projectedLow))}`} className={`vmi-fan t-${tone}`} />
            <text x={split + 4} y="92" className="vmi-t">if it continues</text>
            <text x={W - 4} y={f1(Math.min(Y(pr.projectedHigh), Y(pr.projectedLow)) - 6)} textAnchor="end" className={`vmi-t vmi-t-b t-${tone}`}>{n(pr.projectedLow)}–{n(pr.projectedHigh)}</text>
          </>
        ) : (
          <text x={W - 4} y="92" textAnchor="end" className="vmi-t vmi-t-dim">no projection</text>
        )}
      </svg>
      <WhyLink onClick={() => why({
        title: `${m.label}: ${m.trajectory}`,
        body: <>
          <p>{m.trajectoryReason}</p>
          {pr.projected ? <p className="vmi-muted">{pr.basis}</p> : pr.refusal && <p className="vmi-muted">{pr.refusal}</p>}
          <p className="vmi-muted">{pr.caveat}</p>
        </>,
      })}>How directions are judged ›</WhyLink>
    </div>
  );
}

function MiniMoving({ m }: { m: DomainMetric }) {
  const p = useProjection(m.metric);
  const hist = p.data?.history ?? [];
  if (hist.length < 2) return null;
  const vals = hist.map(x => x.value);
  const lo = Math.min(...vals), hi = Math.max(...vals) || 1;
  const pts = vals.map((v, i) => `${f1(2 + (i / (vals.length - 1)) * 116)},${f1(26 - ((v - lo) / (hi - lo || 1)) * 22)}`).join(' ');
  const n = (v: number) => num(v, m.unit === 'min' ? 'min' : m.unit, Math.abs(v) < 10 ? 1 : 0);
  const tone = m.trajectory === 'worsening' ? 'warn' : m.trajectory === 'improving' ? 'good' : 'move';
  return (
    <div className="vmi-mini">
      <span>{m.label} <small>{n(vals[0])} → {n(vals[vals.length - 1])}</small></span>
      <svg width="90" height="30" viewBox="0 0 120 30" aria-hidden="true"><polyline points={pts} className={`vmi-line t-${tone}`} /></svg>
      <span className={`vmi-tag t-${tone}`}>{m.trajectory}</span>
    </div>
  );
}

function DirectionView({ why, addChange }: { why: (w: Why) => void; addChange: () => void }) {
  const hq = useQuery({ queryKey: ['regime-holds'], queryFn: () => get<Hold[]>(`${API}/api/health/regime-holds`) });
  const dq = useQuery({ queryKey: ['domains'], queryFn: () => get<{ domains: Domain[] }>(`${API}/api/health/domains`) });
  const [whatIf, setWhatIf] = useState(false);

  const holds = (hq.data ?? []).filter(h => h.open);
  const domains = dq.data?.domains ?? [];
  const seen = new Set<string>();
  const moving = domains.flatMap(d => d.metrics).filter(m => MOVED.includes(m.trajectory) && !seen.has(m.metric) && seen.add(m.metric))
    .sort((a, b) => Number(b.trajectory === 'worsening') - Number(a.trajectory === 'worsening'));

  if (dq.isLoading) return <section className="vm-tile vmi-skel" aria-busy="true" />;

  return (
    <>
      {holds.map(h => <HoldCard key={`${h.metric}-${h.changePoint}`} h={h} why={why} addChange={addChange} />)}
      {domains.length > 0 && <Areas domains={domains} />}
      <section className="vm-tile" aria-label="Moving now">
        <h2 className="vmi-h2"><span>Moving now</span><em>last 90 days</em></h2>
        {moving.length === 0 && <p className="vmi-muted">Nothing is moving: every measure with enough data is steady.</p>}
        {moving[0] && <MovingChart m={moving[0]} why={why} />}
        {moving.slice(1, 5).map(m => <MiniMoving key={m.metric} m={m} />)}
      </section>
      <button type="button" className="vm-tile vmi-whatif" onClick={() => setWhatIf(v => !v)} aria-expanded={whatIf}>
        <span>What if I…</span><em>{whatIf ? '×' : '›'}</em>
      </button>
      {whatIf && <div className="vmi-borrowed"><ScenarioSection /></div>}
    </>
  );
}

// ── The screen ──────────────────────────────────────────────────────────────

const TABS: { id: TabId; label: string }[] = [
  { id: 'overview', label: 'Overview' }, { id: 'direction', label: 'Direction' },
  { id: 'patterns', label: 'Patterns' }, { id: 'you', label: 'You' }, { id: 'check', label: 'Check' },
];
const TAB_KEY = 'insight.tab';
const readTab = (): TabId => {
  try { const v = localStorage.getItem(TAB_KEY); return TABS.some(t => t.id === v) ? (v as TabId) : 'overview'; } catch { return 'overview'; }
};

export function MobileInsight({ header, addChange }: { header: (h: string, e: string) => ReactNode; addChange: () => void }) {
  const [tab, setTab] = useState<TabId>(readTab);
  const [why, setWhy] = useState<Why | null>(null);
  const pick = (id: TabId) => {
    setTab(id);
    try { localStorage.setItem(TAB_KEY, id); } catch { /* not remembered, fine */ }
    window.scrollTo?.({ top: 0 });
  };

  return (
    <div className="vm-screen vmi">
      {header('Insight', dateLine())}
      <nav className="vmi-seg" role="tablist" aria-label="Insight sections">
        {TABS.map(t => (
          <button key={t.id} role="tab" aria-selected={tab === t.id} className={tab === t.id ? 'is-on' : ''} onClick={() => pick(t.id)}>{t.label}</button>
        ))}
      </nav>
      <div className="vmi-panel" key={tab} role="tabpanel">
        {tab === 'overview' && (
          <>
            <Verdict why={setWhy} />
            <WhatMovesYou why={setWhy} more={() => pick('patterns')} />
            <TomorrowCard why={setWhy} />
            <FindingCards why={setWhy} />
            <TodayStrips />
          </>
        )}
        {tab === 'direction' && <DirectionView why={setWhy} addChange={addChange} />}
        {(tab === 'patterns' || tab === 'you' || tab === 'check') && (
          <div className="vmi-borrowed insight-page"><InsightTabBody tab={tab} /></div>
        )}
      </div>
      <WhySheet why={why} close={() => setWhy(null)} />
    </div>
  );
}
