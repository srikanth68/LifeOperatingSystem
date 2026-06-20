import { useState } from 'react';
import { QueryClient, QueryClientProvider, useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import {
  BarChart, Bar, XAxis, YAxis, Tooltip, ResponsiveContainer, CartesianGrid, Cell,
} from 'recharts';
import '../styles/modules.css';
import '../styles/vitara.css';

// ── Config ────────────────────────────────────────────────────────────────────

const API = 'http://localhost:5100';
const qc  = new QueryClient({ defaultOptions: { queries: { retry: 1, staleTime: 5 * 60_000, refetchOnWindowFocus: false } } });

// ── Types ─────────────────────────────────────────────────────────────────────

interface Sleep {
  id: string; day: string;
  bedtimeStart: string; bedtimeEnd: string;
  totalSleepMinutes: number; remMinutes: number; deepMinutes: number;
  lightMinutes: number; awakeMinutes: number;
  score?: number; avgHrv?: number; lowestHr?: number;
  avgBreathingRate?: number; avgSpo2?: number; efficiency: number;
}
interface Readiness {
  id: string; day: string; score?: number; level?: string;
  hrvBalance?: number; recoveryIndex?: number; restingHeartRate?: number;
  activityBalance?: number; sleepBalance?: number; temperatureDeviation?: number;
}
interface Activity {
  id: string; day: string; score?: number; steps: number;
  activeCalories: number; totalCalories: number;
  highActivityMinutes: number; mediumActivityMinutes: number;
  lowActivityMinutes: number; sedentaryMinutes: number;
}
interface BioAge {
  bioAge?: number; chronologicalAge: number; delta?: number;
  factors: { hrvScore?: number; restingHrScore?: number; sleepScore?: number; readinessScore?: number; recoveryTrend?: number; };
  dataQuality: string;
}
interface OuraStatus { linked: boolean; expired?: boolean; linkedAt?: string; }

// ── Fetchers ──────────────────────────────────────────────────────────────────

const get = <T,>(url: string): Promise<T> =>
  fetch(url).then(r => { if (!r.ok) throw new Error(r.status.toString()); return r.json(); });

// ── Helpers ───────────────────────────────────────────────────────────────────

const dayLabel  = (d: string) => new Date(d + 'T12:00:00').toLocaleDateString('en-US', { month: 'short', day: 'numeric' });
const shortDay  = (d: string) => new Date(d + 'T12:00:00').toLocaleDateString('en-US', { weekday: 'short' });

const scoreColor = (s?: number | null) =>
  s == null ? '#3d5880' : s >= 85 ? '#06c8a0' : s >= 70 ? '#d4a843' : '#e84444';

const avg = (arr: (number | undefined | null)[]) => {
  const vals = arr.filter((v): v is number => v != null);
  return vals.length ? vals.reduce((a, b) => a + b, 0) / vals.length : undefined;
};

const fmtMin = (m: number) => {
  const h = Math.floor(m / 60), min = m % 60;
  return h > 0 ? `${h}h ${min}m` : `${min}m`;
};

const fmtClock = (iso: string) =>
  new Date(iso).toLocaleTimeString('en-US', { hour: 'numeric', minute: '2-digit' });

// Sleep timeline helpers: position each night on a virtual day that starts at
// 18:00, so a bedtime/wake pair (e.g. 11pm-7am) maps to small positive offsets
// regardless of which side of midnight it falls on, and rows are comparable.
const TIMELINE_ANCHOR_HOUR = 18;

const timelineAnchor = (d: Date) => {
  const a = new Date(d);
  a.setHours(TIMELINE_ANCHOR_HOUR, 0, 0, 0);
  if (d.getHours() < 12) a.setDate(a.getDate() - 1);
  return a;
};

const timelineOffset = (anchor: Date, d: Date) => (d.getTime() - anchor.getTime()) / 3_600_000;

const timelineTickLabel = (h: number) => {
  const actual = ((TIMELINE_ANCHOR_HOUR + h) % 24 + 24) % 24;
  const hour12 = actual % 12 === 0 ? 12 : actual % 12;
  return `${hour12}${actual < 12 ? 'AM' : 'PM'}`;
};

// ── Chart theme ───────────────────────────────────────────────────────────────

const AX = { fill: '#3d5880', fontSize: 10 };
const GRID = { stroke: 'rgba(255,255,255,0.04)' };
const TT = {
  contentStyle: { background: '#0c1830', border: '1px solid #1a2f52', borderRadius: 8, fontSize: 11, color: '#dce8ff', padding: '6px 10px' },
  labelStyle:   { color: '#7a96c0', marginBottom: 2 },
  itemStyle:    { color: '#dce8ff' },
  cursor:       { fill: 'rgba(255,255,255,0.03)' },
};

// ── Stat card ─────────────────────────────────────────────────────────────────

function Stat({ label, value, unit, color }: {
  label: string; value?: number | string; unit?: string; color?: string;
}) {
  return (
    <div className="v2-stat-card">
      <div className="v2-stat-label">{label}</div>
      <div className="v2-stat-val" style={{ color: value != null ? (color ?? 'var(--vitara)') : '#3d5880' }}>
        {value ?? '—'}
        {value != null && unit && <span className="v2-stat-unit"> {unit}</span>}
      </div>
    </div>
  );
}

// ── Loading skeleton ──────────────────────────────────────────────────────────

function Skel({ h = 180 }: { h?: number }) {
  return <div className="v2-chart-skeleton" style={{ height: h }}/>;
}

// ── Setup screens ─────────────────────────────────────────────────────────────

function NotLinked() {
  return (
    <div className="v2-setup">
      <div className="v2-setup-icon">
        <svg viewBox="0 0 24 24" fill="none" stroke="var(--vitara)" strokeWidth="1.75" strokeLinecap="round" strokeLinejoin="round">
          <path d="M20.84 4.61a5.5 5.5 0 00-7.78 0L12 5.67l-1.06-1.06a5.5 5.5 0 00-7.78 7.78l1.06 1.06L12 21.23l7.78-7.78 1.06-1.06a5.5 5.5 0 000-7.78z"/>
        </svg>
      </div>
      <h2>Connect Oura Ring</h2>
      <p>Link your Oura Ring to unlock sleep architecture, readiness scores, and biological age tracking.</p>
      <a href="http://localhost:5100/api/oura/auth" target="_blank" rel="noreferrer" className="btn-primary v2-link-btn">
        Link Oura Ring →
      </a>
    </div>
  );
}

function BackendDown() {
  return (
    <div className="v2-setup">
      <div className="v2-setup-icon v2-setup-icon--error">
        <svg viewBox="0 0 24 24" fill="none" stroke="#e84444" strokeWidth="1.75" strokeLinecap="round">
          <circle cx="12" cy="12" r="10"/>
          <line x1="12" y1="8" x2="12" y2="12"/>
          <circle cx="12" cy="16" r="0.5" fill="#e84444"/>
        </svg>
      </div>
      <h2>Vitara Backend Not Running</h2>
      <p>Start the backend to load your health data from Oura Ring.</p>
      <code className="v2-code">cd vitara &amp;&amp; .\start.ps1</code>
    </div>
  );
}

// ── Overview ──────────────────────────────────────────────────────────────────

function Overview({ status }: { status: OuraStatus }) {
  const qClient = useQueryClient();
  const { data: sleep }     = useQuery<Sleep[]>({ queryKey: ['sleep', 7], queryFn: () => get(`${API}/api/sleep?days=7`) });
  const { data: readiness } = useQuery<Readiness[]>({ queryKey: ['readiness', 7], queryFn: () => get(`${API}/api/readiness?days=7`) });
  const { data: bioage }    = useQuery<BioAge>({ queryKey: ['bioage'], queryFn: () => get(`${API}/api/bioage`) });

  const sync = useMutation({
    mutationFn: () => fetch(`${API}/api/oura/sync`, { method: 'POST' }).then(r => {
      if (!r.ok) throw new Error(r.status.toString());
      return r.json();
    }),
    onSuccess: () => qClient.invalidateQueries(),
  });

  const lastSleep     = sleep?.at(-1);
  const lastReadiness = readiness?.at(-1);
  const younger       = (bioage?.delta ?? 0) < 0;
  const hasData       = (sleep?.length ?? 0) > 0;

  return (
    <div>
      {/* Oura status badge */}
      <div className="v2-status-row">
        <div className="v2-ring-badge linked">
          <span className="v2-ring-dot"/>
          Oura Ring Connected
        </div>
        {status.linkedAt && (
          <span className="v2-status-since">
            since {new Date(status.linkedAt).toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' })}
          </span>
        )}
        <button
          className={`v2-sync-btn ${sync.isPending ? 'syncing' : ''}`}
          onClick={() => sync.mutate()}
          disabled={sync.isPending}
        >
          {sync.isPending ? 'Syncing…' : sync.isSuccess ? `✓ Synced ${(sync.data as {sleep:number}).sleep} nights` : 'Sync Now'}
        </button>
        {sync.isError && <span className="v2-sync-err">Sync failed — check backend logs</span>}
      </div>

      {!hasData && !sync.isPending && (
        <div className="v2-no-data-cta">
          No data yet. Hit <strong>Sync Now</strong> to pull the last 30 days from Oura Ring.
        </div>
      )}

      {/* Hero cards */}
      <div className="v2-hero-grid">
        <div className="v2-hero-card">
          <div className="v2-hero-label">Biological Age</div>
          {bioage?.bioAge != null ? (
            <>
              <div className="v2-hero-val" style={{ color: younger ? 'var(--vitara)' : '#e84444' }}>
                {bioage.bioAge.toFixed(1)}
              </div>
              <div className={`v2-hero-delta ${younger ? 'good' : 'bad'}`}>
                {younger ? '↓' : '↑'} {Math.abs(bioage.delta!).toFixed(1)} yrs {younger ? 'younger' : 'older'}
              </div>
              <div className="v2-hero-sub">chronological {bioage.chronologicalAge} · {bioage.dataQuality} data</div>
            </>
          ) : (
            <div className="v2-hero-empty">
              {bioage?.dataQuality === 'insufficient' ? 'Need ≥ 3 days of data' : 'Syncing…'}
            </div>
          )}
        </div>

        <div className="v2-hero-card">
          <div className="v2-hero-label">Last Night</div>
          {lastSleep ? (
            <>
              <div className="v2-hero-val" style={{ color: scoreColor(lastSleep.score) }}>
                {lastSleep.score ?? '—'}
              </div>
              <div className="v2-hero-sub">sleep score · {fmtMin(lastSleep.totalSleepMinutes)}</div>
              {lastSleep.avgHrv && <div className="v2-hero-sub">HRV {lastSleep.avgHrv.toFixed(0)} ms</div>}
            </>
          ) : <div className="v2-hero-empty">No sleep data yet</div>}
        </div>

        <div className="v2-hero-card">
          <div className="v2-hero-label">Today's Readiness</div>
          {lastReadiness ? (
            <>
              <div className="v2-hero-val" style={{ color: scoreColor(lastReadiness.score) }}>
                {lastReadiness.score ?? '—'}
              </div>
              <div className="v2-hero-sub" style={{ color: scoreColor(lastReadiness.score), textTransform: 'capitalize' }}>
                {(lastReadiness.level ?? '').replace('_', ' ')}
              </div>
              {lastReadiness.restingHeartRate && <div className="v2-hero-sub">RHR {lastReadiness.restingHeartRate} bpm</div>}
            </>
          ) : <div className="v2-hero-empty">No readiness data yet</div>}
        </div>
      </div>

      {/* 7-day trail */}
      {(sleep?.length ?? 0) > 0 && (
        <>
          <div className="section-label">7-Day Sleep Trail</div>
          <div className="v2-trail-grid">
            {sleep!.map(s => (
              <div key={s.id} className="v2-trail-day">
                <div className="v2-trail-bar">
                  <div className="v2-trail-fill" style={{ height: `${s.score ?? 0}%`, background: scoreColor(s.score) }}/>
                </div>
                <div className="v2-trail-score" style={{ color: scoreColor(s.score) }}>{s.score ?? '—'}</div>
                <div className="v2-trail-day-label">{shortDay(s.day)}</div>
              </div>
            ))}
          </div>
        </>
      )}

      {/* 7-day readiness trail */}
      {(readiness?.length ?? 0) > 0 && (
        <>
          <div className="section-label">7-Day Readiness Trail</div>
          <div className="v2-trail-grid">
            {readiness!.map(r => (
              <div key={r.id} className="v2-trail-day">
                <div className="v2-trail-bar">
                  <div className="v2-trail-fill" style={{ height: `${r.score ?? 0}%`, background: scoreColor(r.score) }}/>
                </div>
                <div className="v2-trail-score" style={{ color: scoreColor(r.score) }}>{r.score ?? '—'}</div>
                <div className="v2-trail-day-label">{shortDay(r.day)}</div>
              </div>
            ))}
          </div>
        </>
      )}
    </div>
  );
}

// ── Sleep Architecture ────────────────────────────────────────────────────────

function SleepPillar() {
  const { data, isPending, isError } = useQuery<Sleep[]>({
    queryKey: ['sleep', 14],
    queryFn: () => get(`${API}/api/sleep?days=14`),
  });

  if (isPending) return <Skel h={300}/>;
  if (isError || !data?.length) return <div className="v2-no-data">No sleep data — sync will run after linking Oura Ring</div>;

  const a = {
    score: avg(data.map(s => s.score)),
    hrv:   avg(data.map(s => s.avgHrv)),
    deep:  avg(data.map(s => s.deepMinutes)),
    rem:   avg(data.map(s => s.remMinutes)),
    total: avg(data.map(s => s.totalSleepMinutes)),
    eff:   avg(data.map(s => s.efficiency)),
  };

  // One bar per calendar night: pick the longest session if Oura logged a nap separately.
  const byDay = new Map<string, Sleep>();
  for (const s of data) {
    const existing = byDay.get(s.day);
    if (!existing || s.totalSleepMinutes > existing.totalSleepMinutes) byDay.set(s.day, s);
  }
  const nights = [...byDay.values()].sort((x, y) => y.day.localeCompare(x.day)); // most recent first

  const rows = nights.map(s => {
    const start = new Date(s.bedtimeStart);
    const end   = new Date(s.bedtimeEnd);
    const anchor = timelineAnchor(start);
    const offset   = timelineOffset(anchor, start);
    const duration = timelineOffset(anchor, end) - offset;
    return {
      key: s.id, day: dayLabel(s.day), offset, duration,
      efficiency: s.efficiency,
      bedLabel: fmtClock(s.bedtimeStart), wakeLabel: fmtClock(s.bedtimeEnd),
      totalLabel: fmtMin(s.totalSleepMinutes),
      deepLabel: fmtMin(s.deepMinutes), remLabel: fmtMin(s.remMinutes),
      lightLabel: fmtMin(s.lightMinutes), awakeLabel: fmtMin(s.awakeMinutes),
    };
  });

  const rawMin = Math.min(...rows.map(r => r.offset));
  const rawMax = Math.max(...rows.map(r => r.offset + r.duration));
  const domainMin = Math.floor(rawMin / 3) * 3;
  const domainMax = Math.ceil(rawMax / 3) * 3;
  const ticks: number[] = [];
  for (let h = domainMin; h <= domainMax; h += 3) ticks.push(h);

  return (
    <div>
      <div className="v2-stats-row">
        <Stat label="Avg Score"  value={a.score?.toFixed(0)}  unit="/ 100" color={scoreColor(a.score)}/>
        <Stat label="Avg HRV"    value={a.hrv?.toFixed(0)}    unit="ms"/>
        <Stat label="Deep Sleep" value={a.deep?.toFixed(0)}   unit="min"/>
        <Stat label="REM Sleep"  value={a.rem?.toFixed(0)}    unit="min"/>
        <Stat label="Total Sleep" value={a.total != null ? fmtMin(Math.round(a.total)) : undefined}/>
        <Stat label="Efficiency" value={a.eff != null ? (a.eff * 100).toFixed(0) : undefined} unit="%"/>
      </div>

      <div className="section-label">Sleep Timeline · Last {rows.length} Nights</div>
      <div className="v2-legend">
        <span className="v2-legend-dot" style={{ background: '#06c8a0' }}/> Good efficiency
        <span className="v2-legend-dot" style={{ background: '#d4a843', marginLeft: '1rem' }}/> Fair
        <span className="v2-legend-dot" style={{ background: '#e84444', marginLeft: '1rem' }}/> Poor
      </div>
      <div className="v2-chart-wrap">
        <ResponsiveContainer width="100%" height={rows.length * 32 + 40}>
          <BarChart data={rows} layout="vertical" margin={{ top: 4, right: 16, bottom: 0, left: 0 }} barCategoryGap="30%">
            <CartesianGrid horizontal={false} {...GRID}/>
            <XAxis type="number" domain={[domainMin, domainMax]} ticks={ticks} tickFormatter={timelineTickLabel} tick={AX} tickLine={false} axisLine={false}/>
            <YAxis type="category" dataKey="day" tick={AX} tickLine={false} axisLine={false} width={52}/>
            <Tooltip content={<SleepTimelineTooltip/>} cursor={{ fill: 'rgba(255,255,255,0.03)' }}/>
            <Bar dataKey="offset" stackId="t" fill="transparent" isAnimationActive={false}/>
            <Bar dataKey="duration" stackId="t" radius={6} isAnimationActive={false}>
              {rows.map(r => <Cell key={r.key} fill={scoreColor(r.efficiency * 100)}/>)}
            </Bar>
          </BarChart>
        </ResponsiveContainer>
      </div>
    </div>
  );
}

interface SleepTimelineRow {
  day: string; bedLabel: string; wakeLabel: string; totalLabel: string;
  deepLabel: string; remLabel: string; lightLabel: string; awakeLabel: string;
}

function SleepTimelineTooltip({ active, payload }: {
  active?: boolean; payload?: { payload: SleepTimelineRow }[];
}) {
  if (!active || !payload?.length) return null;
  const d = payload[0].payload;
  return (
    <div style={TT.contentStyle}>
      <div style={TT.labelStyle}>{d.day}</div>
      <div>{d.bedLabel} – {d.wakeLabel} · {d.totalLabel}</div>
      <div style={{ fontSize: 10, color: '#7a96c0', marginTop: 4 }}>
        Deep {d.deepLabel} · REM {d.remLabel} · Light {d.lightLabel} · Awake {d.awakeLabel}
      </div>
    </div>
  );
}

// ── Readiness / Recovery ──────────────────────────────────────────────────────

function ReadinessPillar() {
  const { data, isPending, isError } = useQuery<Readiness[]>({
    queryKey: ['readiness', 14],
    queryFn: () => get(`${API}/api/readiness?days=14`),
  });

  if (isPending) return <Skel h={160}/>;
  if (isError || !data?.length) return <div className="v2-no-data">No readiness data available</div>;

  const a = {
    score: avg(data.map(r => r.score)),
    rhr:   avg(data.map(r => r.restingHeartRate)),
    hrv:   avg(data.map(r => r.hrvBalance)),
    recov: avg(data.map(r => r.recoveryIndex)),
  };

  const levels = data.reduce((acc, r) => {
    const l = r.level ?? 'unknown';
    acc[l] = (acc[l] ?? 0) + 1;
    return acc;
  }, {} as Record<string, number>);

  return (
    <div>
      <div className="v2-stats-row">
        <Stat label="Avg Score"   value={a.score?.toFixed(0)} unit="/ 100" color={scoreColor(a.score)}/>
        <Stat label="Avg RHR"     value={a.rhr?.toFixed(0)}   unit="bpm"/>
        <Stat label="HRV Balance" value={a.hrv?.toFixed(0)}   unit="/ 100"/>
        <Stat label="Recovery"    value={a.recov?.toFixed(0)} unit="/ 100"/>
      </div>

      {/* Level distribution */}
      <div className="v2-level-row">
        {(['optimal', 'good', 'pay_attention'] as const).map(l => (
          <div key={l} className={`v2-level-pill v2-level-${l.replace('_', '-')}`}>
            <span className="v2-level-count">{levels[l] ?? 0}</span>
            <span className="v2-level-label">{l.replace('_', ' ')}</span>
          </div>
        ))}
        <span className="v2-level-hint">of {data.length} days</span>
      </div>

      <div className="section-label">Daily Detail · Last {data.length} Days</div>
      <div className="v2-day-grid">
        {[...data].reverse().map(r => (
          <div key={r.id} className="v2-day-tile">
            <div className="v2-day-tile-date">{dayLabel(r.day)}</div>
            <div className="v2-day-tile-score" style={{ color: scoreColor(r.score) }}>{r.score ?? '—'}</div>
            <div className="v2-day-tile-row"><span>Level</span><span style={{ textTransform: 'capitalize' }}>{(r.level ?? '—').replace('_', ' ')}</span></div>
            <div className="v2-day-tile-row"><span>RHR</span><span>{r.restingHeartRate ? `${r.restingHeartRate} bpm` : '—'}</span></div>
            <div className="v2-day-tile-row"><span>HRV Bal</span><span>{r.hrvBalance ?? '—'}</span></div>
          </div>
        ))}
      </div>
    </div>
  );
}

// ── Activity ──────────────────────────────────────────────────────────────────

function ActivityPillar() {
  const { data, isPending, isError } = useQuery<Activity[]>({
    queryKey: ['activity', 14],
    queryFn: () => get(`${API}/api/activity?days=14`),
  });

  if (isPending) return <Skel h={160}/>;
  if (isError || !data?.length) return <div className="v2-no-data">No activity data available</div>;

  const a = {
    steps:   avg(data.map(d => d.steps)),
    cal:     avg(data.map(d => d.activeCalories)),
    score:   avg(data.map(d => d.score)),
    highMin: avg(data.map(d => d.highActivityMinutes)),
  };

  return (
    <div>
      <div className="v2-stats-row">
        <Stat label="Avg Steps"    value={a.steps != null ? Math.round(a.steps).toLocaleString() : undefined}/>
        <Stat label="Active Cal"   value={a.cal?.toFixed(0)}     unit="kcal"/>
        <Stat label="Avg Score"    value={a.score?.toFixed(0)}   unit="/ 100" color={scoreColor(a.score)}/>
        <Stat label="High-Int Min" value={a.highMin?.toFixed(0)} unit="min"/>
      </div>

      <div className="section-label">Daily Detail · Last {data.length} Days</div>
      <div className="v2-legend">
        <span className="v2-legend-dot" style={{ background: '#06c8a0' }}/> High
        <span className="v2-legend-dot" style={{ background: '#d4a843', marginLeft: '1rem' }}/> Medium
        <span className="v2-legend-dot" style={{ background: 'rgba(255,255,255,0.15)', marginLeft: '1rem' }}/> Low
      </div>
      <div className="v2-day-grid">
        {[...data].reverse().map(d => {
          const zoneTotal = d.highActivityMinutes + d.mediumActivityMinutes + d.lowActivityMinutes;
          return (
            <div key={d.id} className="v2-day-tile">
              <div className="v2-day-tile-date">{dayLabel(d.day)}</div>
              <div className="v2-day-tile-score" style={{ color: d.steps >= 8000 ? 'var(--vitara)' : '#4f9ef8' }}>{d.steps.toLocaleString()}</div>
              <div className="v2-day-tile-row"><span>Cal</span><span>{d.activeCalories} kcal</span></div>
              <div className="v2-day-tile-row"><span>Score</span><span style={{ color: scoreColor(d.score) }}>{d.score ?? '—'}</span></div>
              {zoneTotal > 0 && (
                <div className="v2-stage-bar">
                  <div className="v2-stage-seg" style={{ width: `${d.highActivityMinutes   / zoneTotal * 100}%`, background: '#06c8a0' }} title="High"/>
                  <div className="v2-stage-seg" style={{ width: `${d.mediumActivityMinutes / zoneTotal * 100}%`, background: '#d4a843' }} title="Medium"/>
                  <div className="v2-stage-seg" style={{ width: `${d.lowActivityMinutes    / zoneTotal * 100}%`, background: 'rgba(255,255,255,0.15)' }} title="Low"/>
                </div>
              )}
            </div>
          );
        })}
      </div>
    </div>
  );
}

// ── Bio Age Score ─────────────────────────────────────────────────────────────

function BioAgePillar() {
  const { data, isPending, isError } = useQuery<BioAge>({
    queryKey: ['bioage'],
    queryFn: () => get(`${API}/api/bioage`),
  });

  if (isPending) return <Skel h={200}/>;
  if (isError)   return <div className="v2-no-data">Could not compute biological age</div>;

  const younger = (data?.delta ?? 0) < 0;
  const qualColor = data?.dataQuality === 'good' ? 'var(--vitara)' : data?.dataQuality === 'limited' ? 'var(--gold)' : '#e84444';

  const FACTORS = [
    { label: 'HRV',            val: data?.factors.hrvScore?.toFixed(0),        unit: 'ms avg',     hint: 'Higher → younger' },
    { label: 'Resting HR',     val: data?.factors.restingHrScore?.toFixed(0),  unit: 'bpm avg',    hint: 'Lower → younger' },
    { label: 'Sleep Score',    val: data?.factors.sleepScore?.toFixed(0),      unit: '/ 100',      hint: 'Oura 30-day avg' },
    { label: 'Readiness',      val: data?.factors.readinessScore?.toFixed(0),  unit: '/ 100',      hint: 'Oura 30-day avg' },
    { label: 'Recovery Trend', val: data?.factors.recoveryTrend?.toFixed(3),   unit: 'pts/day',    hint: 'Positive = improving' },
  ];

  return (
    <div>
      <div className="v2-bioage-hero">
        <div className="v2-bioage-eyebrow">Biological Age</div>
        {data?.bioAge != null ? (
          <>
            <div className="v2-bioage-val" style={{ color: younger ? 'var(--vitara)' : '#e84444' }}>
              {data.bioAge.toFixed(1)}
            </div>
            <div className={`v2-bioage-delta ${younger ? 'good' : 'bad'}`}>
              {younger ? '↓' : '↑'} {Math.abs(data.delta!).toFixed(1)} years {younger ? 'younger' : 'older'} than chronological age {data.chronologicalAge}
            </div>
            <div className="v2-bioage-quality" style={{ color: qualColor }}>
              data quality: {data.dataQuality}
            </div>
          </>
        ) : (
          <div className="v2-bioage-empty">
            {data?.dataQuality === 'insufficient' ? 'Need ≥ 3 days of Oura data to compute' : 'Computing…'}
          </div>
        )}
      </div>

      {data?.bioAge != null && (
        <>
          <div className="section-label">Contributing Factors</div>
          <div className="v2-factor-grid">
            {FACTORS.map(f => (
              <div key={f.label} className="v2-factor-card">
                <div className="v2-factor-key">{f.label}</div>
                <div className="v2-factor-val">
                  {f.val ?? <span style={{ color: '#3d5880' }}>—</span>}
                  {f.val != null && <span className="v2-factor-unit"> {f.unit}</span>}
                </div>
                <div className="v2-factor-hint">{f.hint}</div>
              </div>
            ))}
          </div>
        </>
      )}
    </div>
  );
}

// ── Protocols ─────────────────────────────────────────────────────────────────

interface ProtocolResult {
  name: string; icon: string; target: string; desc: string;
  status: 'on-track' | 'behind' | 'suggested' | 'manual';
  progressPct?: number; metric?: string;
}

const PROTOCOL_STATUS_LABEL: Record<ProtocolResult['status'], string> = {
  'on-track': 'on track', behind: 'behind', suggested: 'suggested', manual: 'manual',
};

function ProtocolsPillar() {
  const { data, isPending, isError } = useQuery<ProtocolResult[]>({
    queryKey: ['protocols'],
    queryFn: () => get(`${API}/api/protocols`),
  });

  if (isPending) return <Skel h={220}/>;
  if (isError || !data?.length) return <div className="v2-no-data">Could not load protocols</div>;

  return (
    <div>
      <p className="v2-protocols-intro">
        Status and progress below are computed automatically from your synced sleep/readiness/activity data.
        Strength training and eating windows aren't visible to Oura, so those stay manual.
      </p>
      <div className="v2-protocol-list">
        {data.map(p => (
          <div key={p.name} className={`v2-protocol-card v2-protocol-${p.status}`}>
            <div className="v2-protocol-icon">{p.icon}</div>
            <div className="v2-protocol-body">
              <div className="v2-protocol-name">
                {p.name}
                <span className={`v2-protocol-badge v2-protocol-badge-${p.status}`}>{PROTOCOL_STATUS_LABEL[p.status]}</span>
              </div>
              <div className="v2-protocol-target">{p.target}</div>
              {p.metric && <div className="v2-protocol-metric">{p.metric}</div>}
              {p.progressPct != null && (
                <div className="v2-protocol-progress">
                  <div
                    className="v2-protocol-progress-fill"
                    style={{ width: `${p.progressPct}%`, background: p.status === 'on-track' ? 'var(--vitara)' : '#d4a843' }}
                  />
                </div>
              )}
              <div className="v2-protocol-desc">{p.desc}</div>
            </div>
          </div>
        ))}
      </div>
    </div>
  );
}

// ── Root ──────────────────────────────────────────────────────────────────────

type Pillar = 'overview' | 'sleep' | 'readiness' | 'activity' | 'bioage' | 'protocols';

const PILLARS: { id: Pillar; label: string }[] = [
  { id: 'overview',  label: 'Overview'  },
  { id: 'sleep',     label: 'Sleep'     },
  { id: 'readiness', label: 'Readiness' },
  { id: 'activity',  label: 'Activity'  },
  { id: 'bioage',    label: 'Bio Age'   },
  { id: 'protocols', label: 'Protocols' },
];

function VitaraInner() {
  const [pillar, setPillar] = useState<Pillar>('overview');

  const { data: status, isPending, isError } = useQuery<OuraStatus>({
    queryKey: ['oura-status'],
    queryFn: () => get(`${API}/api/oura/status`),
  });

  const MC = { '--mc': 'var(--vitara)' } as React.CSSProperties;

  return (
    <div>
      <div className="module-header" style={MC}>
        <div className="module-header-icon">
          <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.75" strokeLinecap="round" strokeLinejoin="round">
            <path d="M20.84 4.61a5.5 5.5 0 00-7.78 0L12 5.67l-1.06-1.06a5.5 5.5 0 00-7.78 7.78l1.06 1.06L12 21.23l7.78-7.78 1.06-1.06a5.5 5.5 0 000-7.78z"/>
          </svg>
        </div>
        <div>
          <h1 className="module-title">Vitara</h1>
          <div className="module-subtitle">Longevity Intelligence</div>
        </div>
      </div>

      {isPending && (
        <div className="v2-connecting">
          <div className="v2-connecting-dot"/>
          Connecting to Vitara…
        </div>
      )}

      {!isPending && isError && <BackendDown/>}

      {!isPending && !isError && status && !status.linked && <NotLinked/>}

      {!isPending && !isError && status?.linked && (
        <>
          <nav className="module-subnav" style={MC}>
            {PILLARS.map(p => (
              <button
                key={p.id}
                className={`module-tab ${pillar === p.id ? 'active' : ''}`}
                onClick={() => setPillar(p.id)}
              >
                {p.label}
              </button>
            ))}
          </nav>

          {pillar === 'overview'  && <Overview status={status}/>}
          {pillar === 'sleep'     && <SleepPillar/>}
          {pillar === 'readiness' && <ReadinessPillar/>}
          {pillar === 'activity'  && <ActivityPillar/>}
          {pillar === 'bioage'    && <BioAgePillar/>}
          {pillar === 'protocols' && <ProtocolsPillar/>}
        </>
      )}
    </div>
  );
}

export default function VitaraModule() {
  return (
    <QueryClientProvider client={qc}>
      <VitaraInner/>
    </QueryClientProvider>
  );
}
