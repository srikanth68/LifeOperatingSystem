import { useState } from 'react';
import { QueryClientProvider, useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { makeModuleQueryClient } from '../services/moduleQuery';
import {
  BarChart, Bar, XAxis, YAxis, Tooltip, ResponsiveContainer, CartesianGrid, Cell,
  Area, AreaChart, LineChart, Line,
} from 'recharts';
import { authHeaders } from '../services/auth';
import { moduleApi } from '../services/apiHost';
import '../styles/modules.css';
import '../styles/vitara.css';

const API = moduleApi(5100);
const qc  = makeModuleQueryClient(5 * 60_000);

// ── Types ────────────────────────────────────────────────────────────────────

interface Dashboard {
  date: string;
  profile?: { age?: number; weight?: number; height?: number; biologicalSex?: string };
  sleep?: { score?: number; totalMinutes: number; deepMinutes: number; remMinutes: number; lightMinutes: number; efficiency: number; hrv?: number; lowestHr?: number; breathingRate?: number; spo2?: number; skinTemp?: number };
  readiness?: { score?: number; level?: string; restingHr?: number; hrvBalance?: number; recoveryIndex?: number; activityBalance?: number; sleepBalance?: number; tempDeviation?: number };
  activity?: { score?: number; steps: number; activeCalories: number; totalCalories: number; highMinutes: number; mediumMinutes: number; lowMinutes: number; distance: number };
  stress?: { summary?: string; stressMinutes?: number; recoveryMinutes?: number };
  resilience?: { level?: string; sleepRecovery?: number; daytimeRecovery?: number; stressScore?: number };
  spo2Data?: { average?: number; breathingDisturbance?: number };
  cardiovascularAge?: number;
  vo2Max?: number;
  weeklyAvg: { hrv: number; rhr: number; sleepScore: number; readinessScore: number; steps: number; activityScore: number };
  recentWorkouts?: { activity: string; calories?: number; distance?: number; intensity?: string; startTime?: string }[];
  heartRateSamples?: { timestamp: string; bpm: number }[];
}
interface Sleep {
  id: string; day: string; bedtimeStart: string; bedtimeEnd: string;
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
interface WorkoutItem {
  id: string; day: string; activity: string; startTime?: string; endTime?: string;
  calories?: number; distance?: number; intensity?: string; label?: string;
}
interface OuraStatus { linked: boolean; expired?: boolean; linkedAt?: string; lastSyncedAt?: string; }

function relTime(iso?: string): string {
  if (!iso) return 'never';
  const then = new Date(iso).getTime();
  if (Number.isNaN(then)) return 'never';
  const secs = Math.round((Date.now() - then) / 1000);
  if (secs < 45) return 'just now';
  const mins = Math.round(secs / 60);
  if (mins < 60) return `${mins}m ago`;
  const hrs = Math.round(mins / 60);
  if (hrs < 24) return `${hrs}h ago`;
  const days = Math.round(hrs / 24);
  if (days < 7) return `${days}d ago`;
  return new Date(iso).toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' });
}
interface ProtocolResult {
  name: string; icon: string; target: string; desc: string;
  status: 'on-track' | 'behind' | 'suggested' | 'manual';
  progressPct?: number; metric?: string;
}

// ── Helpers ───────────────────────────────────────────────────────────────────

const get = <T,>(url: string): Promise<T> =>
  fetch(url, { headers: authHeaders() }).then(r => { if (!r.ok) throw new Error(r.status.toString()); return r.json(); });

const send = async <T = unknown,>(url: string, method: string, body?: unknown): Promise<T> => {
  const opts: RequestInit = { method, headers: { ...authHeaders(), 'Content-Type': 'application/json' } };
  if (body !== undefined) opts.body = JSON.stringify(body);
  const r = await fetch(url, opts);
  if (!r.ok) throw new Error(r.status.toString());
  if (r.status === 204) return undefined as T;
  return r.json();
};

const dayLabel = (d: string) => new Date(d + 'T12:00:00').toLocaleDateString('en-US', { month: 'short', day: 'numeric' });
const shortDay = (d: string) => new Date(d + 'T12:00:00').toLocaleDateString('en-US', { weekday: 'short' });
const scoreColor = (s?: number | null) =>
  s == null ? '#3d5880' : s >= 85 ? '#06c8a0' : s >= 70 ? '#f59e0b' : '#ef4444';
const avg = (arr: (number | undefined | null)[]) => {
  const vals = arr.filter((v): v is number => v != null);
  return vals.length ? vals.reduce((a, b) => a + b, 0) / vals.length : undefined;
};
const fmtMin = (m: number) => { const h = Math.floor(m / 60), min = m % 60; return h > 0 ? `${h}h ${min}m` : `${min}m`; };
const fmtClock = (iso: string) => new Date(iso).toLocaleTimeString('en-US', { hour: 'numeric', minute: '2-digit' });

const TIMELINE_ANCHOR_HOUR = 18;
const timelineAnchor = (d: Date) => { const a = new Date(d); a.setHours(TIMELINE_ANCHOR_HOUR, 0, 0, 0); if (d.getHours() < 12) a.setDate(a.getDate() - 1); return a; };
const timelineOffset = (anchor: Date, d: Date) => (d.getTime() - anchor.getTime()) / 3_600_000;
const timelineTickLabel = (h: number) => { const actual = ((TIMELINE_ANCHOR_HOUR + h) % 24 + 24) % 24; const h12 = actual % 12 === 0 ? 12 : actual % 12; return `${h12}${actual < 12 ? 'AM' : 'PM'}`; };

const AX = { fill: '#3d5880', fontSize: 10 };
const GRID = { stroke: 'rgba(255,255,255,0.04)' };
const TT = {
  contentStyle: { background: '#0c1830', border: '1px solid #1a2f52', borderRadius: 8, fontSize: 11, color: '#dce8ff', padding: '6px 10px' },
  labelStyle: { color: '#7a96c0', marginBottom: 2 },
};

function Skel({ h = 180 }: { h?: number }) { return <div className="v-chart-skel" style={{ height: h }}/>; }

// ── Score Ring SVG ────────────────────────────────────────────────────────────

function ScoreRing({ score, color, size = 72, label }: { score?: number | null; color: string; size?: number; label?: string }) {
  const r = (size - 12) / 2;
  const circ = 2 * Math.PI * r;
  const pct = score != null ? Math.min(score, 100) / 100 : 0;
  return (
    <div className="v-ring-wrap">
      <svg className="v-ring-svg" width={size} height={size} viewBox={`0 0 ${size} ${size}`}>
        <circle className="v-ring-bg" cx={size/2} cy={size/2} r={r}/>
        <circle className="v-ring-fg" cx={size/2} cy={size/2} r={r}
          stroke={color} strokeDasharray={circ} strokeDashoffset={circ * (1 - pct)}/>
      </svg>
      <div>
        <div className="v-ring-val" style={{ color }}>{score ?? '--'}</div>
        {label && <div className="v-ring-label">{label}</div>}
      </div>
    </div>
  );
}

// ── Setup Screens ─────────────────────────────────────────────────────────────

function NotLinked() {
  return (
    <div className="v-setup">
      <div className="v-setup-icon">
        <svg viewBox="0 0 24 24" fill="none" stroke="var(--vitara)" strokeWidth="1.75" strokeLinecap="round" strokeLinejoin="round">
          <path d="M20.84 4.61a5.5 5.5 0 00-7.78 0L12 5.67l-1.06-1.06a5.5 5.5 0 00-7.78 7.78l1.06 1.06L12 21.23l7.78-7.78 1.06-1.06a5.5 5.5 0 000-7.78z"/>
        </svg>
      </div>
      <h2>Connect Oura Ring</h2>
      <p>Link your Oura Ring to unlock sleep architecture, readiness scores, cardiovascular age, stress tracking, and biological age intelligence.</p>
      <a href={`${API}/api/oura/auth`} target="_blank" rel="noreferrer" className="btn-primary">Link Oura Ring</a>
    </div>
  );
}

// Shown when a token row exists but is expired / can't refresh. Without this, a
// broken token leaves status.linked=true so NotLinked never renders — and there
// was no other way to re-trigger the OAuth flow from the UI.
function OuraExpiredBanner() {
  return (
    <div style={{
      display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: '1rem',
      flexWrap: 'wrap', padding: '0.75rem 1rem', marginBottom: '1rem', borderRadius: 8,
      background: 'rgba(239,68,68,0.08)', border: '1px solid rgba(239,68,68,0.3)',
    }}>
      <span style={{ fontSize: '0.9rem' }}>Your Oura session expired or couldn't refresh. Re-link to resume syncing.</span>
      <a href={`${API}/api/oura/auth`} target="_blank" rel="noreferrer" className="btn-primary">Re-link Oura</a>
    </div>
  );
}

function BackendDown() {
  return (
    <div className="v-setup">
      <div className="v-setup-icon v-setup-icon--error">
        <svg viewBox="0 0 24 24" fill="none" stroke="#ef4444" strokeWidth="1.75" strokeLinecap="round">
          <circle cx="12" cy="12" r="10"/><line x1="12" y1="8" x2="12" y2="12"/><circle cx="12" cy="16" r="0.5" fill="#ef4444"/>
        </svg>
      </div>
      <h2>Vitara Backend Not Running</h2>
      <p>Start the backend to load your health data from Oura Ring.</p>
    </div>
  );
}

// ── TODAY ─────────────────────────────────────────────────────────────────────

function TodayPage({ status }: { status: OuraStatus }) {
  const qClient = useQueryClient();
  const { data: d } = useQuery<Dashboard>({ queryKey: ['dashboard'], queryFn: () => get(`${API}/api/dashboard`), refetchInterval: 60_000 });
  const sync = useMutation({
    mutationFn: () => fetch(`${API}/api/oura/sync`, { method: 'POST', headers: authHeaders() }).then(r => { if (!r.ok) throw new Error(r.status.toString()); return r.json(); }),
    onSuccess: () => qClient.invalidateQueries(),
  });

  if (!d) return <Skel h={400}/>;

  const stressColor = d.stress?.summary === 'restored' ? 'var(--stress-low)' : d.stress?.summary === 'normal' ? 'var(--stress-mod)' : 'var(--stress-high)';
  const resLevel = d.resilience?.level;
  const resColor = resLevel === 'exceptional' || resLevel === 'strong' ? 'var(--vitara)' : resLevel === 'solid' ? '#4f9ef8' : resLevel === 'adequate' ? 'var(--gold)' : '#ef4444';

  return (
    <div>
      {/* Status bar */}
      <div className="v-status-bar">
        <span className="v-ring-dot"/>
        <span className="v-status-text">Oura Ring Connected</span>
        <span className="v-status-since" title={status.lastSyncedAt ? new Date(status.lastSyncedAt).toLocaleString() : undefined}>
          updated {relTime(status.lastSyncedAt)}
        </span>
        <button className={`v-sync-btn ${sync.isPending ? 'syncing' : ''}`} onClick={() => sync.mutate()} disabled={sync.isPending}>
          {sync.isPending ? 'Syncing...' : sync.isSuccess ? 'Synced' : 'Sync Now'}
        </button>
        {sync.isError && <span className="v-sync-err">Failed</span>}
      </div>

      {/* Hero cards */}
      <div className="v-hero">
        <div className="v-hero-card v-hero-card--accent">
          <div className="v-hero-label">Readiness</div>
          <ScoreRing score={d.readiness?.score} color={scoreColor(d.readiness?.score)} label={d.readiness?.level?.replace('_', ' ') ?? ''} />
        </div>
        <div className="v-hero-card">
          <div className="v-hero-label">Sleep</div>
          <ScoreRing score={d.sleep?.score} color={scoreColor(d.sleep?.score)} label={d.sleep ? fmtMin(d.sleep.totalMinutes) : '--'} />
        </div>
        <div className="v-hero-card">
          <div className="v-hero-label">Activity</div>
          <ScoreRing score={d.activity?.score} color={scoreColor(d.activity?.score)} label={d.activity ? `${d.activity.steps.toLocaleString()} steps` : '--'} />
        </div>
      </div>

      {/* Metrics grid */}
      <div className="v-metrics">
        <div className="v-metric">
          <div className="v-metric-label">Heart Rate</div>
          <div className="v-metric-val" style={{ color: 'var(--heart)' }}>
            {d.readiness?.restingHr ?? '--'}<span className="v-metric-unit"> bpm</span>
          </div>
          <div className="v-metric-sub">avg {d.weeklyAvg.rhr} bpm (7d)</div>
        </div>
        <div className="v-metric">
          <div className="v-metric-label">HRV</div>
          <div className="v-metric-val" style={{ color: '#818cf8' }}>
            {d.sleep?.hrv ?? '--'}<span className="v-metric-unit"> ms</span>
          </div>
          <div className="v-metric-sub">avg {d.weeklyAvg.hrv} ms (7d)</div>
        </div>
        <div className="v-metric">
          <div className="v-metric-label">Stress</div>
          <div className="v-metric-val" style={{ color: stressColor, textTransform: 'capitalize' }}>
            {d.stress?.summary ?? '--'}
          </div>
          {d.stress?.recoveryMinutes != null && <div className="v-metric-sub">{d.stress.recoveryMinutes}m recovery</div>}
        </div>
        <div className="v-metric">
          <div className="v-metric-label">Resilience</div>
          <div className="v-metric-val" style={{ color: resColor, textTransform: 'capitalize' }}>
            {resLevel ?? '--'}
          </div>
          {d.resilience?.sleepRecovery != null && <div className="v-metric-sub">sleep recovery {d.resilience.sleepRecovery}</div>}
        </div>
        <div className="v-metric">
          <div className="v-metric-label">SpO2</div>
          <div className="v-metric-val" style={{ color: '#06c8a0' }}>
            {d.spo2Data?.average != null ? `${d.spo2Data.average.toFixed(1)}` : (d.sleep?.spo2 != null ? d.sleep.spo2.toFixed(1) : '--')}<span className="v-metric-unit"> %</span>
          </div>
        </div>
        <div className="v-metric">
          <div className="v-metric-label">Cardio Age</div>
          <div className="v-metric-val" style={{ color: d.cardiovascularAge != null && d.profile?.age != null && d.cardiovascularAge < d.profile.age ? 'var(--vitara)' : '#ef4444' }}>
            {d.cardiovascularAge != null ? Math.round(d.cardiovascularAge) : '--'}
          </div>
          {d.profile?.age != null && <div className="v-metric-sub">chrono {d.profile.age}</div>}
        </div>
      </div>

      {/* Heart rate strip */}
      {d.heartRateSamples && d.heartRateSamples.length > 0 && (
        <>
          <div className="v-section">Heart Rate (24h)<span className="v-section-line"/></div>
          <div className="v-hr-strip">
            <div className="v-hr-header">
              <div className="v-hr-now">{d.heartRateSamples[d.heartRateSamples.length - 1]?.bpm ?? '--'} bpm</div>
              <div className="v-hr-range">
                {Math.min(...d.heartRateSamples.map(h => h.bpm))} - {Math.max(...d.heartRateSamples.map(h => h.bpm))} bpm range
              </div>
            </div>
            <ResponsiveContainer width="100%" height={80}>
              <AreaChart data={d.heartRateSamples.map(h => ({ t: new Date(h.timestamp).toLocaleTimeString('en-US', { hour: 'numeric' }), bpm: h.bpm }))} margin={{ top: 4, right: 0, bottom: 0, left: 0 }}>
                <defs>
                  <linearGradient id="hrGrad" x1="0" y1="0" x2="0" y2="1">
                    <stop offset="0%" stopColor="#ff6b8a" stopOpacity={0.3}/>
                    <stop offset="100%" stopColor="#ff6b8a" stopOpacity={0}/>
                  </linearGradient>
                </defs>
                <Area type="monotone" dataKey="bpm" stroke="#ff6b8a" fill="url(#hrGrad)" strokeWidth={1.5} dot={false} isAnimationActive={false}/>
                <Tooltip contentStyle={TT.contentStyle} labelStyle={TT.labelStyle}/>
              </AreaChart>
            </ResponsiveContainer>
          </div>
        </>
      )}

      {/* Sleep stages mini */}
      {d.sleep && (
        <>
          <div className="v-section">Last Night<span className="v-section-line"/></div>
          <SleepStagesBar deep={d.sleep.deepMinutes} rem={d.sleep.remMinutes} light={d.sleep.lightMinutes} />
        </>
      )}

      {/* Recent workouts */}
      {d.recentWorkouts && d.recentWorkouts.length > 0 && (
        <>
          <div className="v-section">Recent Workouts<span className="v-section-line"/></div>
          <div className="v-workout-list">
            {d.recentWorkouts.map((w, i) => (
              <div key={i} className="v-workout-card">
                <div className="v-workout-icon" style={{ background: 'rgba(6,200,160,0.08)' }}>
                  {w.activity === 'running' ? '🏃' : w.activity === 'cycling' ? '🚴' : w.activity === 'walking' ? '🚶' : w.activity === 'swimming' ? '🏊' : '💪'}
                </div>
                <div className="v-workout-body">
                  <div className="v-workout-name">{w.activity}</div>
                  <div className="v-workout-meta">
                    {w.calories != null && <span>{w.calories} cal</span>}
                    {w.distance != null && w.distance > 0 && <span>{(w.distance / 1000).toFixed(1)} km</span>}
                    {w.intensity && <span style={{ textTransform: 'capitalize' }}>{w.intensity}</span>}
                  </div>
                </div>
              </div>
            ))}
          </div>
        </>
      )}

      {/* VO2 Max card */}
      {d.vo2Max != null && (
        <div className="v-metrics" style={{ gridTemplateColumns: '1fr' }}>
          <div className="v-metric v-metric--inline">
            <div>
              <div className="v-metric-label">VO2 Max</div>
              <div className="v-metric-sub">Cardiorespiratory fitness</div>
            </div>
            <div className="v-metric-val" style={{ color: d.vo2Max >= 40 ? 'var(--vitara)' : d.vo2Max >= 30 ? 'var(--gold)' : '#ef4444' }}>
              {d.vo2Max.toFixed(1)}<span className="v-metric-unit"> mL/kg/min</span>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}

// ── Sleep Stages Bar ─────────────────────────────────────────────────────────

function SleepStagesBar({ deep, rem, light }: { deep: number; rem: number; light: number }) {
  const total = deep + rem + light || 1;
  return (
    <div style={{ marginBottom: '1rem' }}>
      <div className="v-sleep-stages">
        <div className="v-sleep-seg" style={{ width: `${deep / total * 100}%`, background: 'var(--sleep-deep)' }}/>
        <div className="v-sleep-seg" style={{ width: `${rem / total * 100}%`, background: 'var(--sleep-rem)' }}/>
        <div className="v-sleep-seg" style={{ width: `${light / total * 100}%`, background: 'var(--sleep-light)' }}/>
      </div>
      <div className="v-sleep-legend">
        <span><span className="v-sleep-legend-dot" style={{ background: 'var(--sleep-deep)' }}/> Deep {fmtMin(deep)}</span>
        <span><span className="v-sleep-legend-dot" style={{ background: 'var(--sleep-rem)' }}/> REM {fmtMin(rem)}</span>
        <span><span className="v-sleep-legend-dot" style={{ background: 'var(--sleep-light)' }}/> Light {fmtMin(light)}</span>
      </div>
    </div>
  );
}

// ── SLEEP ─────────────────────────────────────────────────────────────────────

function SleepPage() {
  const { data, isPending, isError } = useQuery<Sleep[]>({ queryKey: ['sleep', 14], queryFn: () => get(`${API}/api/sleep?days=14`) });
  if (isPending) return <Skel h={300}/>;
  if (isError || !data?.length) return <div className="v-empty">No sleep data yet</div>;

  const a = { score: avg(data.map(s => s.score)), hrv: avg(data.map(s => s.avgHrv)), deep: avg(data.map(s => s.deepMinutes)), rem: avg(data.map(s => s.remMinutes)), total: avg(data.map(s => s.totalSleepMinutes)), eff: avg(data.map(s => s.efficiency)) };

  const byDay = new Map<string, Sleep>();
  for (const s of data) { const ex = byDay.get(s.day); if (!ex || s.totalSleepMinutes > ex.totalSleepMinutes) byDay.set(s.day, s); }
  const nights = [...byDay.values()].sort((x, y) => y.day.localeCompare(x.day));

  const rows = nights.map(s => {
    const start = new Date(s.bedtimeStart), end = new Date(s.bedtimeEnd);
    const anchor = timelineAnchor(start);
    const offset = timelineOffset(anchor, start);
    const duration = timelineOffset(anchor, end) - offset;
    return { key: s.id, day: dayLabel(s.day), offset, duration, efficiency: s.efficiency, bedLabel: fmtClock(s.bedtimeStart), wakeLabel: fmtClock(s.bedtimeEnd), totalLabel: fmtMin(s.totalSleepMinutes), deepLabel: fmtMin(s.deepMinutes), remLabel: fmtMin(s.remMinutes), lightLabel: fmtMin(s.lightMinutes), awakeLabel: fmtMin(s.awakeMinutes) };
  });
  const rawMin = Math.min(...rows.map(r => r.offset)), rawMax = Math.max(...rows.map(r => r.offset + r.duration));
  const domainMin = Math.floor(rawMin / 3) * 3, domainMax = Math.ceil(rawMax / 3) * 3;
  const ticks: number[] = []; for (let h = domainMin; h <= domainMax; h += 3) ticks.push(h);

  return (
    <div>
      <div className="v-metrics">
        <Metric label="Avg Score" value={a.score?.toFixed(0)} unit="/ 100" color={scoreColor(a.score)}/>
        <Metric label="Avg HRV" value={a.hrv?.toFixed(0)} unit="ms"/>
        <Metric label="Deep Sleep" value={a.deep?.toFixed(0)} unit="min"/>
        <Metric label="REM Sleep" value={a.rem?.toFixed(0)} unit="min"/>
        <Metric label="Total Sleep" value={a.total != null ? fmtMin(Math.round(a.total)) : undefined}/>
        <Metric label="Efficiency" value={a.eff != null ? (a.eff * 100).toFixed(0) : undefined} unit="%"/>
      </div>

      <div className="v-section">Sleep Timeline<span className="v-section-line"/></div>
      <div className="v-chart">
        <ResponsiveContainer width="100%" height={rows.length * 32 + 40}>
          <BarChart data={rows} layout="vertical" margin={{ top: 4, right: 16, bottom: 0, left: 0 }} barCategoryGap="30%">
            <CartesianGrid horizontal={false} {...GRID}/>
            <XAxis type="number" domain={[domainMin, domainMax]} ticks={ticks} tickFormatter={timelineTickLabel} tick={AX} tickLine={false} axisLine={false}/>
            <YAxis type="category" dataKey="day" tick={AX} tickLine={false} axisLine={false} width={52}/>
            <Tooltip content={<SleepTooltip/>} cursor={{ fill: 'rgba(255,255,255,0.03)' }}/>
            <Bar dataKey="offset" stackId="t" fill="transparent" isAnimationActive={false}/>
            <Bar dataKey="duration" stackId="t" radius={6} isAnimationActive={false}>
              {rows.map(r => <Cell key={r.key} fill={scoreColor(r.efficiency * 100)}/>)}
            </Bar>
          </BarChart>
        </ResponsiveContainer>
      </div>

      <div className="v-section">7-Day Scores<span className="v-section-line"/></div>
      <div className="v-trail">
        {data.map(s => (
          <div key={s.id} className="v-trail-col">
            <div className="v-trail-bar"><div className="v-trail-fill" style={{ height: `${s.score ?? 0}%`, background: scoreColor(s.score) }}/></div>
            <div className="v-trail-score" style={{ color: scoreColor(s.score) }}>{s.score ?? '--'}</div>
            <div className="v-trail-day">{shortDay(s.day)}</div>
          </div>
        ))}
      </div>
    </div>
  );
}

function SleepTooltip({ active, payload }: { active?: boolean; payload?: { payload: { day: string; bedLabel: string; wakeLabel: string; totalLabel: string; deepLabel: string; remLabel: string; lightLabel: string; awakeLabel: string } }[] }) {
  if (!active || !payload?.length) return null;
  const d = payload[0].payload;
  return (
    <div style={TT.contentStyle}>
      <div style={TT.labelStyle}>{d.day}</div>
      <div>{d.bedLabel} - {d.wakeLabel} | {d.totalLabel}</div>
      <div style={{ fontSize: 10, color: '#7a96c0', marginTop: 4 }}>Deep {d.deepLabel} | REM {d.remLabel} | Light {d.lightLabel} | Awake {d.awakeLabel}</div>
    </div>
  );
}

// ── BODY ──────────────────────────────────────────────────────────────────────

interface WeighInItem { id: string; day: string; weightKg: number; }

// Weight is stored in kg (canonical — BMI + HealthKit sync depend on it) but the
// dashboard shows and accepts pounds.
const kgToLb = (kg: number) => kg * 2.20462;
const lbToKg = (lb: number) => lb / 2.20462;
interface AgeHistory {
  chronologicalAge: number | null;
  cardiovascularAge: { day: string; value: number }[];
  vo2Max: { day: string; value: number }[];
}

function BodyPage() {
  const qClient = useQueryClient();
  const { data: bio } = useQuery<{ bioAge?: number; chronologicalAge: number; delta?: number; cardiovascularAge?: number; vo2Max?: number; factors: { hrvScore?: number; restingHrScore?: number; sleepScore?: number; readinessScore?: number; recoveryTrend?: number }; dataQuality: string; ageSource: string }>({
    queryKey: ['bioage'], queryFn: () => get(`${API}/api/bioage`),
  });
  const { data: sleep } = useQuery<Sleep[]>({ queryKey: ['sleep', 30], queryFn: () => get(`${API}/api/sleep?days=30`) });
  const { data: profile } = useQuery<{ synced: boolean; height?: number }>({ queryKey: ['profile'], queryFn: () => get(`${API}/api/profile`) });
  const { data: weighIns } = useQuery<WeighInItem[]>({ queryKey: ['weighins'], queryFn: () => get(`${API}/api/weighins?days=180`) });
  const { data: ageHist } = useQuery<AgeHistory>({ queryKey: ['age-history'], queryFn: () => get(`${API}/api/bioage/history?days=90`) });

  // Weight is entered and shown in POUNDS, but stored as kilograms — kg stays the
  // canonical unit so BMI math and the iPhone HealthKit sync keep working unchanged.
  const [weight, setWeight] = useState('');
  const logWeight = useMutation({
    mutationFn: () => send(`${API}/api/weighins`, 'POST', { weightKg: lbToKg(parseFloat(weight)) }),
    onSuccess: () => { setWeight(''); qClient.invalidateQueries({ queryKey: ['weighins'] }); },
  });

  const younger = (bio?.delta ?? 0) < 0;
  const hrvTrend = sleep?.filter(s => s.avgHrv != null).map(s => ({ day: dayLabel(s.day), hrv: s.avgHrv!, rhr: s.lowestHr ?? 0 })) ?? [];

  const heightM = profile?.height;
  const bmiOf = (kg: number) => heightM && heightM > 0 ? kg / (heightM * heightM) : null;
  const weightChart = (weighIns ?? []).map(w => ({ day: dayLabel(w.day), weight: +kgToLb(w.weightKg).toFixed(1), bmi: bmiOf(w.weightKg) != null ? +bmiOf(w.weightKg)!.toFixed(1) : undefined }));
  const latestWeight = weighIns && weighIns.length > 0 ? weighIns[weighIns.length - 1] : null;
  const latestBmi = latestWeight ? bmiOf(latestWeight.weightKg) : null;

  // Merge cardio-age + vo2max histories by day for a dual-line chart.
  const ageDays = Array.from(new Set([...(ageHist?.cardiovascularAge ?? []).map(c => c.day), ...(ageHist?.vo2Max ?? []).map(v => v.day)])).sort();
  const ageChart = ageDays.map(d => ({
    day: dayLabel(d),
    cardio: ageHist?.cardiovascularAge.find(c => c.day === d)?.value,
    vo2: ageHist?.vo2Max.find(v => v.day === d)?.value,
  }));

  return (
    <div>
      {/* Bio Age Hero */}
      <div className="v-bioage">
        <div className="v-bioage-eyebrow">Biological Age</div>
        {bio?.bioAge != null ? (
          <>
            <div className="v-bioage-val" style={{ color: younger ? 'var(--vitara)' : '#ef4444' }}>{bio.bioAge.toFixed(1)}</div>
            <div className={`v-bioage-delta ${younger ? 'good' : 'bad'}`}>
              {younger ? 'v' : '^'} {Math.abs(bio.delta!).toFixed(1)} years {younger ? 'younger' : 'older'} than chronological age {bio.chronologicalAge}
            </div>
            <div className="v-bioage-source">age source: {bio.ageSource} | data: {bio.dataQuality}</div>
          </>
        ) : <div style={{ color: 'var(--text3)', padding: '1rem' }}>{bio?.dataQuality === 'insufficient' ? 'Need 3+ days of data' : 'Computing...'}</div>}
      </div>

      {/* Key body metrics */}
      <div className="v-metrics">
        <Metric label="Cardiovascular Age" value={bio?.cardiovascularAge != null ? Math.round(bio.cardiovascularAge).toString() : undefined} color={bio?.cardiovascularAge != null && bio.chronologicalAge > 0 && bio.cardiovascularAge < bio.chronologicalAge ? 'var(--vitara)' : '#ef4444'} sub={bio?.chronologicalAge ? `chrono ${bio.chronologicalAge}` : undefined}/>
        <Metric label="VO2 Max" value={bio?.vo2Max?.toFixed(1)} unit="mL/kg/min" color={bio?.vo2Max != null && bio.vo2Max >= 40 ? 'var(--vitara)' : 'var(--gold)'}/>
        <Metric label="Avg HRV" value={bio?.factors.hrvScore?.toFixed(0)} unit="ms" color="#818cf8"/>
        <Metric label="Resting HR" value={bio?.factors.restingHrScore?.toFixed(0)} unit="bpm" color="var(--heart)"/>
        <Metric label="Sleep Score" value={bio?.factors.sleepScore?.toFixed(0)} unit="/ 100" color={scoreColor(bio?.factors.sleepScore)}/>
        <Metric label="Readiness" value={bio?.factors.readinessScore?.toFixed(0)} unit="/ 100" color={scoreColor(bio?.factors.readinessScore)}/>
      </div>

      {/* HRV trend chart */}
      {hrvTrend.length > 3 && (
        <>
          <div className="v-section">HRV Trend (30d)<span className="v-section-line"/></div>
          <div className="v-chart">
            <ResponsiveContainer width="100%" height={160}>
              <AreaChart data={hrvTrend} margin={{ top: 4, right: 8, bottom: 0, left: 0 }}>
                <defs>
                  <linearGradient id="hrvGrad" x1="0" y1="0" x2="0" y2="1">
                    <stop offset="0%" stopColor="#818cf8" stopOpacity={0.3}/>
                    <stop offset="100%" stopColor="#818cf8" stopOpacity={0}/>
                  </linearGradient>
                </defs>
                <CartesianGrid {...GRID}/>
                <XAxis dataKey="day" tick={AX} tickLine={false} axisLine={false} interval={Math.floor(hrvTrend.length / 6)}/>
                <YAxis tick={AX} tickLine={false} axisLine={false} width={30}/>
                <Tooltip contentStyle={TT.contentStyle} labelStyle={TT.labelStyle}/>
                <Area type="monotone" dataKey="hrv" stroke="#818cf8" fill="url(#hrvGrad)" strokeWidth={2} dot={false}/>
              </AreaChart>
            </ResponsiveContainer>
          </div>
        </>
      )}

      {/* Weight & BMI */}
      <div className="v-section">Weight &amp; BMI<span className="v-section-line"/></div>
      <div className="v-weighin">
        <div className="v-metrics" style={{ flex: 1 }}>
          <Metric label="Latest Weight" value={latestWeight ? kgToLb(latestWeight.weightKg).toFixed(1) : undefined} unit="lb"/>
          <Metric label="BMI" value={latestBmi != null ? latestBmi.toFixed(1) : undefined} color={latestBmi != null && latestBmi >= 18.5 && latestBmi < 25 ? 'var(--vitara)' : 'var(--gold)'} sub={heightM ? undefined : 'set height in profile'}/>
        </div>
        <div className="v-weighin-form">
          <input type="number" step="0.1" placeholder="lb" value={weight} onChange={e => setWeight(e.target.value)} onKeyDown={e => e.key === 'Enter' && weight && logWeight.mutate()}/>
          <button className="v-log-save" disabled={!weight || logWeight.isPending} onClick={() => logWeight.mutate()}>{logWeight.isPending ? '…' : 'Log'}</button>
        </div>
      </div>
      {weightChart.length > 1 && (
        <div className="v-chart">
          <ResponsiveContainer width="100%" height={160}>
            <LineChart data={weightChart} margin={{ top: 4, right: 8, bottom: 0, left: 0 }}>
              <CartesianGrid {...GRID}/>
              <XAxis dataKey="day" tick={AX} tickLine={false} axisLine={false} interval={Math.max(0, Math.floor(weightChart.length / 6))}/>
              <YAxis tick={AX} tickLine={false} axisLine={false} width={36} domain={['dataMin - 2', 'dataMax + 2']}/>
              <Tooltip contentStyle={TT.contentStyle} labelStyle={TT.labelStyle}/>
              <Line type="monotone" dataKey="weight" stroke="var(--vitara)" strokeWidth={2} dot={false} name="Weight (lb)"/>
            </LineChart>
          </ResponsiveContainer>
        </div>
      )}

      {/* VO2max & Cardiovascular Age history */}
      {ageChart.length > 1 && (
        <>
          <div className="v-section">VO2max &amp; Cardiovascular Age (90d)<span className="v-section-line"/></div>
          <div className="v-chart">
            <ResponsiveContainer width="100%" height={180}>
              <LineChart data={ageChart} margin={{ top: 4, right: 8, bottom: 0, left: 0 }}>
                <CartesianGrid {...GRID}/>
                <XAxis dataKey="day" tick={AX} tickLine={false} axisLine={false} interval={Math.max(0, Math.floor(ageChart.length / 6))}/>
                <YAxis yAxisId="left" tick={AX} tickLine={false} axisLine={false} width={32}/>
                <YAxis yAxisId="right" orientation="right" tick={AX} tickLine={false} axisLine={false} width={32}/>
                <Tooltip contentStyle={TT.contentStyle} labelStyle={TT.labelStyle}/>
                <Line yAxisId="left" type="monotone" dataKey="vo2" stroke="var(--vitara)" strokeWidth={2} dot={false} name="VO2max" connectNulls/>
                <Line yAxisId="right" type="monotone" dataKey="cardio" stroke="var(--gold)" strokeWidth={2} dot={false} name="Cardio Age" connectNulls/>
              </LineChart>
            </ResponsiveContainer>
          </div>
        </>
      )}
    </div>
  );
}

// ── ACTIVITY ──────────────────────────────────────────────────────────────────

const WORKOUT_TYPES = ['strength', 'running', 'cycling', 'walking', 'swimming', 'yoga', 'hiit', 'other'];

function LogWorkoutForm() {
  const qClient = useQueryClient();
  const [open, setOpen] = useState(false);
  const [f, setF] = useState({ activity: 'strength', day: new Date().toISOString().slice(0, 10), calories: '', intensity: 'moderate', label: '' });

  const log = useMutation({
    mutationFn: () => send(`${API}/api/workouts`, 'POST', {
      day: f.day, activity: f.activity, intensity: f.intensity,
      calories: f.calories ? parseInt(f.calories) : null,
      label: f.label || null,
    }),
    onSuccess: () => { setF(s => ({ ...s, calories: '', label: '' })); setOpen(false); qClient.invalidateQueries({ queryKey: ['workouts'] }); },
  });

  return (
    <div className="v-logworkout">
      <button className="v-log-toggle" onClick={() => setOpen(o => !o)}>{open ? 'Cancel' : '+ Log Workout'}</button>
      {open && (
        <div className="v-log-form">
          <select value={f.activity} onChange={e => setF(s => ({ ...s, activity: e.target.value }))}>
            {WORKOUT_TYPES.map(t => <option key={t} value={t}>{t}</option>)}
          </select>
          <input type="date" value={f.day} onChange={e => setF(s => ({ ...s, day: e.target.value }))}/>
          <input type="number" placeholder="Calories" value={f.calories} onChange={e => setF(s => ({ ...s, calories: e.target.value }))}/>
          <select value={f.intensity} onChange={e => setF(s => ({ ...s, intensity: e.target.value }))}>
            <option value="easy">easy</option><option value="moderate">moderate</option><option value="hard">hard</option>
          </select>
          <input placeholder="Label (optional)" value={f.label} onChange={e => setF(s => ({ ...s, label: e.target.value }))}/>
          <button className="v-log-save" disabled={log.isPending} onClick={() => log.mutate()}>{log.isPending ? 'Saving…' : 'Save'}</button>
        </div>
      )}
    </div>
  );
}

function ActivityPage() {
  const { data, isPending } = useQuery<Activity[]>({ queryKey: ['activity', 14], queryFn: () => get(`${API}/api/activity?days=14`) });
  const { data: workouts } = useQuery<WorkoutItem[]>({ queryKey: ['workouts'], queryFn: () => get(`${API}/api/workouts?days=30`) });

  if (isPending) return <Skel h={200}/>;
  if (!data?.length) return <div className="v-empty"><LogWorkoutForm/><div style={{ marginTop: '1rem' }}>No activity data yet — log a workout above.</div></div>;

  const a = { steps: avg(data.map(d => d.steps)), cal: avg(data.map(d => d.activeCalories)), score: avg(data.map(d => d.score)), highMin: avg(data.map(d => d.highActivityMinutes)) };
  const stepsChart = data.map(d => ({ day: shortDay(d.day), steps: d.steps, cal: d.activeCalories }));

  return (
    <div>
      <LogWorkoutForm/>

      <div className="v-metrics">
        <Metric label="Avg Steps" value={a.steps != null ? Math.round(a.steps).toLocaleString() : undefined}/>
        <Metric label="Active Cal" value={a.cal?.toFixed(0)} unit="kcal"/>
        <Metric label="Avg Score" value={a.score?.toFixed(0)} unit="/ 100" color={scoreColor(a.score)}/>
        <Metric label="High-Int Min" value={a.highMin?.toFixed(0)} unit="min"/>
      </div>

      <div className="v-section">Daily Steps<span className="v-section-line"/></div>
      <div className="v-chart">
        <ResponsiveContainer width="100%" height={160}>
          <BarChart data={stepsChart} margin={{ top: 4, right: 8, bottom: 0, left: 0 }}>
            <CartesianGrid {...GRID}/>
            <XAxis dataKey="day" tick={AX} tickLine={false} axisLine={false}/>
            <YAxis tick={AX} tickLine={false} axisLine={false} width={40}/>
            <Tooltip contentStyle={TT.contentStyle} labelStyle={TT.labelStyle}/>
            <Bar dataKey="steps" radius={4} isAnimationActive={false}>
              {stepsChart.map((d, i) => <Cell key={i} fill={d.steps >= 8000 ? '#06c8a0' : d.steps >= 5000 ? '#f59e0b' : '#ef4444'}/>)}
            </Bar>
          </BarChart>
        </ResponsiveContainer>
      </div>

      {/* Workouts */}
      {workouts && workouts.length > 0 && (
        <>
          <div className="v-section">Workouts<span className="v-section-line"/></div>
          <div className="v-workout-list">
            {workouts.slice(0, 10).map(w => (
              <div key={w.id} className="v-workout-card">
                <div className="v-workout-icon" style={{ background: 'rgba(6,200,160,0.08)' }}>
                  {w.activity === 'running' ? '🏃' : w.activity === 'cycling' ? '🚴' : w.activity === 'walking' ? '🚶' : w.activity === 'swimming' ? '🏊' : '💪'}
                </div>
                <div className="v-workout-body">
                  <div className="v-workout-name">{w.label || w.activity}</div>
                  <div className="v-workout-meta">
                    <span>{dayLabel(w.day)}</span>
                    {w.calories != null && <span>{w.calories} cal</span>}
                    {w.distance != null && w.distance > 0 && <span>{(w.distance / 1000).toFixed(1)} km</span>}
                    {w.intensity && <span style={{ textTransform: 'capitalize' }}>{w.intensity}</span>}
                  </div>
                </div>
              </div>
            ))}
          </div>
        </>
      )}
    </div>
  );
}

// ── READINESS ─────────────────────────────────────────────────────────────────

function ReadinessPage() {
  const { data, isPending } = useQuery<Readiness[]>({ queryKey: ['readiness', 14], queryFn: () => get(`${API}/api/readiness?days=14`) });
  if (isPending) return <Skel h={200}/>;
  if (!data?.length) return <div className="v-empty">No readiness data</div>;

  const a = { score: avg(data.map(r => r.score)), rhr: avg(data.map(r => r.restingHeartRate)), hrv: avg(data.map(r => r.hrvBalance)), recov: avg(data.map(r => r.recoveryIndex)) };
  const levels = data.reduce((acc, r) => { const l = r.level ?? 'unknown'; acc[l] = (acc[l] ?? 0) + 1; return acc; }, {} as Record<string, number>);

  return (
    <div>
      <div className="v-metrics">
        <Metric label="Avg Score" value={a.score?.toFixed(0)} unit="/ 100" color={scoreColor(a.score)}/>
        <Metric label="Avg RHR" value={a.rhr?.toFixed(0)} unit="bpm"/>
        <Metric label="HRV Balance" value={a.hrv?.toFixed(0)} unit="/ 100"/>
        <Metric label="Recovery" value={a.recov?.toFixed(0)} unit="/ 100"/>
      </div>

      <div className="v-levels">
        {(['optimal', 'good', 'pay_attention'] as const).map(l => (
          <div key={l} className={`v-level-pill v-level-${l.replace('_', '-')}`}>
            <span className="v-level-count">{levels[l] ?? 0}</span>
            <span className="v-level-label">{l.replace('_', ' ')}</span>
          </div>
        ))}
      </div>

      <div className="v-section">Daily Detail<span className="v-section-line"/></div>
      <div className="v-day-grid">
        {[...data].reverse().map(r => (
          <div key={r.id} className="v-day-tile">
            <div className="v-day-tile-date">{dayLabel(r.day)}</div>
            <div className="v-day-tile-val" style={{ color: scoreColor(r.score) }}>{r.score ?? '--'}</div>
            <div className="v-day-tile-row"><span>Level</span><span style={{ textTransform: 'capitalize' }}>{(r.level ?? '--').replace('_', ' ')}</span></div>
            <div className="v-day-tile-row"><span>RHR</span><span>{r.restingHeartRate ? `${r.restingHeartRate} bpm` : '--'}</span></div>
            <div className="v-day-tile-row"><span>HRV</span><span>{r.hrvBalance ?? '--'}</span></div>
          </div>
        ))}
      </div>
    </div>
  );
}

// ── PROTOCOLS ─────────────────────────────────────────────────────────────────

const PROTOCOL_STATUS_LABEL: Record<string, string> = { 'on-track': 'on track', behind: 'behind', suggested: 'suggested', manual: 'manual' };

function ProtocolsPage() {
  const { data, isPending } = useQuery<ProtocolResult[]>({ queryKey: ['protocols'], queryFn: () => get(`${API}/api/protocols`) });
  if (isPending) return <Skel h={220}/>;
  if (!data?.length) return <div className="v-empty">No protocols configured</div>;

  return (
    <div>
      <div className="v-protocol-list">
        {data.map(p => (
          <div key={p.name} className="v-protocol-card">
            <div className="v-protocol-icon">{p.icon}</div>
            <div className="v-protocol-body">
              <div className="v-protocol-name">
                {p.name}
                <span className={`v-protocol-badge v-protocol-badge-${p.status}`}>{PROTOCOL_STATUS_LABEL[p.status] ?? p.status}</span>
              </div>
              <div className="v-protocol-target">{p.target}</div>
              {p.metric && <div className="v-protocol-metric">{p.metric}</div>}
              {p.progressPct != null && (
                <div className="v-protocol-progress">
                  <div className="v-protocol-progress-fill" style={{ width: `${p.progressPct}%`, background: p.status === 'on-track' ? 'var(--vitara)' : 'var(--gold)' }}/>
                </div>
              )}
              <div className="v-protocol-desc">{p.desc}</div>
            </div>
          </div>
        ))}
      </div>
    </div>
  );
}

// ── Shared Metric Card ───────────────────────────────────────────────────────

function Metric({ label, value, unit, color, sub }: { label: string; value?: string; unit?: string; color?: string; sub?: string }) {
  return (
    <div className="v-metric">
      <div className="v-metric-label">{label}</div>
      <div className="v-metric-val" style={{ color: value != null ? (color ?? 'var(--vitara)') : '#3d5880' }}>
        {value ?? '--'}
        {value != null && unit && <span className="v-metric-unit"> {unit}</span>}
      </div>
      {sub && <div className="v-metric-sub">{sub}</div>}
    </div>
  );
}

// ── NUTRITION ─────────────────────────────────────────────────────────────────

interface FoodResult { fdcId: number; name: string; brand: string | null; nutrients: { calories: number | null; protein: number | null; carbs: number | null; fat: number | null; fiber: number | null }; servingSize: number | null; servingUnit: string | null }
interface MealItem { id: string; foodName: string; fdcId: number | null; servingQty: number; servingUnit: string | null; calories: number; protein: number; carbs: number; fat: number; fiber: number | null; loggedAt: string }
interface MealsDay { day: string; totals: { calories: number; protein: number; carbs: number; fat: number; fiber: number | null }; meals: Record<string, MealItem[]> }
interface NutritionRow { day: string; calories: number; protein: number; carbs: number; fat: number; fiber: number | null; sugar: number | null; sodium: number | null; calorieGoal: number | null; proteinGoal: number | null; carbGoal: number | null; fatGoal: number | null; mealsJson: string | null }

const MEAL_TYPES = ['breakfast', 'lunch', 'dinner', 'snack'];
const MEAL_ICON: Record<string, string> = { breakfast: '🌅', lunch: '☀️', dinner: '🌙', snack: '🍎' };
const UNITS = ['g', 'oz', 'cup', 'tbsp', 'tsp', 'piece', 'serving', 'ml', 'kg', 'lb'];

function scalePreview(food: FoodResult, qty: number, unit: string) {
  const unitG: Record<string, number> = { g: 1, oz: 28.35, cup: 240, tbsp: 15, tsp: 5, ml: 1, kg: 1000, lb: 453.6 };
  let grams: number;
  if (unitG[unit]) grams = qty * unitG[unit];
  else if (food.servingSize) grams = qty * food.servingSize;
  else grams = qty * 100;
  const s = grams / 100;
  return {
    calories: Math.round((food.nutrients.calories ?? 0) * s),
    protein: Math.round(((food.nutrients.protein ?? 0) * s) * 10) / 10,
    carbs: Math.round(((food.nutrients.carbs ?? 0) * s) * 10) / 10,
    fat: Math.round(((food.nutrients.fat ?? 0) * s) * 10) / 10,
  };
}

function NutritionPage() {
  const qClient = useQueryClient();
  const today = new Date().toISOString().slice(0, 10);
  const [day, setDay] = useState(today);
  const [search, setSearch] = useState('');
  const [results, setResults] = useState<FoodResult[]>([]);
  const [searching, setSearching] = useState(false);
  const [mealType, setMealType] = useState('lunch');
  const [qty, setQty] = useState('1');
  const [unit, setUnit] = useState('serving');
  const [selected, setSelected] = useState<FoodResult | null>(null);
  const [editId, setEditId] = useState<string | null>(null);
  const [editQty, setEditQty] = useState('');
  const [editUnit, setEditUnit] = useState('');

  const { data: mealsData } = useQuery<MealsDay>({ queryKey: ['meals', day], queryFn: () => get(`${API}/api/meals?day=${day}`) });
  const { data: history } = useQuery<{ day: string; calories: number; protein: number; carbs: number; fat: number }[]>({
    queryKey: ['nutrition-history'], queryFn: () => get(`${API}/api/nutrition?days=14`)
  });
  const { data: nutritionRows } = useQuery<NutritionRow[]>({ queryKey: ['nutrition-rows'], queryFn: () => get(`${API}/api/nutrition?days=1`) });
  const todayRow = nutritionRows?.find(n => n.day === today);

  const [goalsOpen, setGoalsOpen] = useState(false);
  const [goals, setGoals] = useState({ cal: '', protein: '', carbs: '', fat: '' });
  const saveGoals = useMutation({
    mutationFn: () => send(`${API}/api/nutrition`, 'POST', [{
      day: today,
      calories: todayRow?.calories ?? 0, protein: todayRow?.protein ?? 0, carbs: todayRow?.carbs ?? 0, fat: todayRow?.fat ?? 0,
      fiber: todayRow?.fiber ?? null, sugar: todayRow?.sugar ?? null, sodium: todayRow?.sodium ?? null,
      calorieGoal: goals.cal ? parseInt(goals.cal) : (todayRow?.calorieGoal ?? null),
      proteinGoal: goals.protein ? parseFloat(goals.protein) : (todayRow?.proteinGoal ?? null),
      carbGoal: goals.carbs ? parseFloat(goals.carbs) : (todayRow?.carbGoal ?? null),
      fatGoal: goals.fat ? parseFloat(goals.fat) : (todayRow?.fatGoal ?? null),
      mealsJson: todayRow?.mealsJson ?? null,
    }]),
    onSuccess: () => { setGoalsOpen(false); qClient.invalidateQueries({ queryKey: ['nutrition-rows'] }); },
  });

  const invalidate = () => { qClient.invalidateQueries({ queryKey: ['meals', day] }); qClient.invalidateQueries({ queryKey: ['nutrition-history'] }); qClient.invalidateQueries({ queryKey: ['nutrition-rows'] }); };

  const doSearch = async () => {
    if (!search.trim()) return;
    setSearching(true);
    setSelected(null);
    try {
      const r: FoodResult[] = await get(`${API}/api/food/search?q=${encodeURIComponent(search)}&pageSize=8`);
      setResults(r);
    } catch { setResults([]); }
    setSearching(false);
  };

  const logFood = useMutation({
    mutationFn: (food: FoodResult) => {
      const q = parseFloat(qty) || 1;
      return send(`${API}/api/meals`, 'POST', {
        day, mealType, foodName: food.name, fdcId: food.fdcId,
        qty: q, unit,
        servingSizeG: food.servingSize,
        calPer100: food.nutrients.calories, protPer100: food.nutrients.protein,
        carbsPer100: food.nutrients.carbs, fatPer100: food.nutrients.fat, fiberPer100: food.nutrients.fiber,
      });
    },
    onSuccess: () => { invalidate(); setResults([]); setSearch(''); setSelected(null); setQty('1'); setUnit('serving'); },
  });

  const updateMeal = useMutation({
    mutationFn: ({ id, meal }: { id: string; meal: MealItem }) => {
      const q = parseFloat(editQty) || meal.servingQty;
      const u = editUnit || meal.servingUnit || 'serving';
      return send(`${API}/api/meals/${id}`, 'PUT', {
        mealType: meal.foodName, foodName: meal.foodName, fdcId: meal.fdcId,
        qty: q, unit: u,
        servingSizeG: null,
        calPer100: meal.calories / (meal.servingQty * ((meal.servingUnit === 'g' ? 1 : 100) / 100)),
        protPer100: meal.protein / (meal.servingQty * ((meal.servingUnit === 'g' ? 1 : 100) / 100)),
        carbsPer100: meal.carbs / (meal.servingQty * ((meal.servingUnit === 'g' ? 1 : 100) / 100)),
        fatPer100: meal.fat / (meal.servingQty * ((meal.servingUnit === 'g' ? 1 : 100) / 100)),
        fiberPer100: meal.fiber,
      });
    },
    onSuccess: () => { invalidate(); setEditId(null); },
  });

  const deleteMeal = useMutation({ mutationFn: (id: string) => send(`${API}/api/meals/${id}`, 'DELETE'), onSuccess: invalidate });

  const t = mealsData?.totals;
  const hasMeals = mealsData?.meals && Object.values(mealsData.meals).some((v: unknown) => (v as MealItem[])?.length > 0);

  const shiftDay = (offset: number) => {
    const d = new Date(day + 'T12:00:00');
    d.setDate(d.getDate() + offset);
    setDay(d.toISOString().slice(0, 10));
  };

  const preview = selected ? scalePreview(selected, parseFloat(qty) || 1, unit) : null;

  const historyChart = (history ?? []).map(h => ({
    day: new Date(h.day + 'T12:00:00').toLocaleDateString('en-US', { weekday: 'short', month: 'short', day: 'numeric' }),
    calories: Math.round(h.calories),
    protein: Math.round(h.protein),
    carbs: Math.round(h.carbs),
    fat: Math.round(h.fat),
  }));

  return (
    <div className="vn">
      {/* ── Date navigator ── */}
      <div className="vn-date-nav">
        <button className="vn-date-arrow" onClick={() => shiftDay(-1)}>‹</button>
        <button className="vn-date-label" onClick={() => setDay(today)}>
          {day === today ? 'Today' : new Date(day + 'T12:00:00').toLocaleDateString('en-US', { weekday: 'short', month: 'short', day: 'numeric' })}
        </button>
        <button className="vn-date-arrow" onClick={() => shiftDay(1)} disabled={day >= today}>›</button>
      </div>

      {/* ── Macro summary cards ── */}
      <div className="vn-macros">
        <div className="vn-macro vn-macro--cal">
          <div className="vn-macro-val">{Math.round(t?.calories ?? 0)}</div>
          <div className="vn-macro-label">kcal{todayRow?.calorieGoal ? ` / ${todayRow.calorieGoal}` : ''}</div>
        </div>
        <div className="vn-macro vn-macro--protein">
          <div className="vn-macro-val">{Math.round(t?.protein ?? 0)}g</div>
          <div className="vn-macro-label">Protein{todayRow?.proteinGoal ? ` / ${Math.round(todayRow.proteinGoal)}g` : ''}</div>
        </div>
        <div className="vn-macro vn-macro--carbs">
          <div className="vn-macro-val">{Math.round(t?.carbs ?? 0)}g</div>
          <div className="vn-macro-label">Carbs{todayRow?.carbGoal ? ` / ${Math.round(todayRow.carbGoal)}g` : ''}</div>
        </div>
        <div className="vn-macro vn-macro--fat">
          <div className="vn-macro-val">{Math.round(t?.fat ?? 0)}g</div>
          <div className="vn-macro-label">Fat{todayRow?.fatGoal ? ` / ${Math.round(todayRow.fatGoal)}g` : ''}</div>
        </div>
      </div>

      {/* ── Goals editor ── */}
      <div className="vn-goals">
        <button className="v-log-toggle" onClick={() => {
          if (!goalsOpen && todayRow) setGoals({
            cal: todayRow.calorieGoal?.toString() ?? '', protein: todayRow.proteinGoal?.toString() ?? '',
            carbs: todayRow.carbGoal?.toString() ?? '', fat: todayRow.fatGoal?.toString() ?? '',
          });
          setGoalsOpen(o => !o);
        }}>{goalsOpen ? 'Cancel' : '⚙ Set Daily Goals'}</button>
        {goalsOpen && (
          <div className="v-log-form">
            <input type="number" placeholder="kcal goal" value={goals.cal} onChange={e => setGoals(g => ({ ...g, cal: e.target.value }))}/>
            <input type="number" placeholder="protein g" value={goals.protein} onChange={e => setGoals(g => ({ ...g, protein: e.target.value }))}/>
            <input type="number" placeholder="carbs g" value={goals.carbs} onChange={e => setGoals(g => ({ ...g, carbs: e.target.value }))}/>
            <input type="number" placeholder="fat g" value={goals.fat} onChange={e => setGoals(g => ({ ...g, fat: e.target.value }))}/>
            <button className="v-log-save" disabled={saveGoals.isPending} onClick={() => saveGoals.mutate()}>{saveGoals.isPending ? '…' : 'Save Goals'}</button>
          </div>
        )}
      </div>

      {/* ── Log controls ── */}
      <div className="vn-controls">
        <div className="vn-meal-pills">
          {MEAL_TYPES.map(m => (
            <button key={m} className={`vn-pill ${mealType === m ? 'active' : ''}`} onClick={() => setMealType(m)}>
              {MEAL_ICON[m]} {m}
            </button>
          ))}
        </div>
        <div className="vn-search-row">
          <input className="vn-search-input" placeholder="Search food (e.g. idli, paneer, chicken breast)..." value={search} onChange={e => setSearch(e.target.value)} onKeyDown={e => e.key === 'Enter' && doSearch()} />
          <button className="vn-search-btn" onClick={doSearch} disabled={searching}>
            {searching ? '...' : 'Search'}
          </button>
        </div>
      </div>

      {/* ── Search results ── */}
      {results.length > 0 && (
        <div className="vn-results">
          {results.map(f => {
            const isSelected = selected?.fdcId === f.fdcId;
            return (
              <div key={f.fdcId} className={`vn-result-card ${isSelected ? 'vn-result-selected' : ''}`}>
                <div className="vn-result-body" onClick={() => { setSelected(isSelected ? null : f); setUnit(f.servingSize ? 'serving' : 'g'); setQty(f.servingSize ? '1' : '100'); }}>
                  <div className="vn-result-name">{f.name}</div>
                  <div className="vn-result-meta">
                    {f.brand && <span>{f.brand} · </span>}
                    <span className="vn-result-per100">per 100g: {Math.round(f.nutrients.calories ?? 0)} cal · {Math.round(f.nutrients.protein ?? 0)}P · {Math.round(f.nutrients.carbs ?? 0)}C · {Math.round(f.nutrients.fat ?? 0)}F</span>
                    {f.servingSize && <span className="vn-result-serving"> · 1 serving = {f.servingSize}{f.servingUnit}</span>}
                  </div>
                </div>
                {isSelected && (
                  <div className="vn-result-configure">
                    <input className="vn-qty-input" type="number" min="0.1" step="0.5" value={qty} onChange={e => setQty(e.target.value)} />
                    <select className="vn-unit-select" value={unit} onChange={e => setUnit(e.target.value)}>
                      {UNITS.map(u => <option key={u} value={u}>{u}</option>)}
                    </select>
                    {preview && (
                      <span className="vn-result-preview">
                        {preview.calories} cal · {preview.protein}P · {preview.carbs}C · {preview.fat}F
                      </span>
                    )}
                    <button className="vn-result-confirm" onClick={() => logFood.mutate(f)}>+ Add</button>
                  </div>
                )}
              </div>
            );
          })}
        </div>
      )}

      {/* ── Today's meals ── */}
      <div className="v-section">Meals<span className="v-section-line"/></div>
      {hasMeals ? MEAL_TYPES.map(mt => {
        const items = mealsData?.meals[mt];
        if (!items?.length) return null;
        const mtCal = items.reduce((s: number, m: MealItem) => s + m.calories, 0);
        return (
          <div key={mt} className="vn-meal-group">
            <div className="vn-meal-header">
              <span>{MEAL_ICON[mt]} {mt}</span>
              <span className="vn-meal-header-cal">{Math.round(mtCal)} cal</span>
            </div>
            {items.map((m: MealItem) => (
              <div key={m.id} className="vn-meal-item">
                {editId === m.id ? (
                  <div className="vn-meal-edit-row">
                    <span className="vn-meal-item-name">{m.foodName}</span>
                    <input className="vn-qty-input" type="number" value={editQty} onChange={e => setEditQty(e.target.value)} />
                    <select className="vn-unit-select" value={editUnit} onChange={e => setEditUnit(e.target.value)}>
                      {UNITS.map(u => <option key={u} value={u}>{u}</option>)}
                    </select>
                    <button className="vn-edit-save" onClick={() => updateMeal.mutate({ id: m.id, meal: m })}>Save</button>
                    <button className="vn-edit-cancel" onClick={() => setEditId(null)}>×</button>
                  </div>
                ) : (
                  <>
                    <div className="vn-meal-item-body" onClick={() => { setEditId(m.id); setEditQty(String(m.servingQty)); setEditUnit(m.servingUnit || 'serving'); }}>
                      <div className="vn-meal-item-name">{m.foodName}</div>
                      <div className="vn-meal-item-macros">
                        {Math.round(m.calories)} cal · {Math.round(m.protein)}g P · {Math.round(m.carbs)}g C · {Math.round(m.fat)}g F
                        <span className="vn-meal-item-qty"> · {m.servingQty} {m.servingUnit}</span>
                      </div>
                    </div>
                    <button className="vn-meal-item-del" onClick={() => deleteMeal.mutate(m.id)} title="Delete">×</button>
                  </>
                )}
              </div>
            ))}
          </div>
        );
      }) : (
        <div className="v-empty">No meals logged for {day === today ? 'today' : day}. Search a food above to start.</div>
      )}

      {/* ── History chart ── */}
      {historyChart.length > 1 && (
        <>
          <div className="v-section" style={{ marginTop: '1rem' }}>Calorie History<span className="v-section-line"/></div>
          <div className="v-chart">
            <ResponsiveContainer width="100%" height={160}>
              <BarChart data={historyChart} margin={{ top: 4, right: 8, bottom: 0, left: 0 }}>
                <CartesianGrid {...GRID}/>
                <XAxis dataKey="day" tick={AX} tickLine={false} axisLine={false} interval={Math.max(0, Math.floor(historyChart.length / 7) - 1)}/>
                <YAxis tick={AX} tickLine={false} axisLine={false} width={40}/>
                <Tooltip contentStyle={TT.contentStyle} labelStyle={TT.labelStyle} formatter={(v: number, name: string) => [`${v}${name === 'calories' ? ' kcal' : 'g'}`, name.charAt(0).toUpperCase() + name.slice(1)]}/>
                <Bar dataKey="calories" radius={4} isAnimationActive={false}>
                  {historyChart.map((d, i) => <Cell key={i} fill={d.calories >= 1800 ? '#06c8a0' : d.calories >= 1200 ? '#f59e0b' : '#ef4444'}/>)}
                </Bar>
              </BarChart>
            </ResponsiveContainer>
          </div>

          <div className="v-section">Macro Breakdown<span className="v-section-line"/></div>
          <div className="v-chart">
            <ResponsiveContainer width="100%" height={140}>
              <BarChart data={historyChart} margin={{ top: 4, right: 8, bottom: 0, left: 0 }}>
                <CartesianGrid {...GRID}/>
                <XAxis dataKey="day" tick={AX} tickLine={false} axisLine={false} interval={Math.max(0, Math.floor(historyChart.length / 7) - 1)}/>
                <YAxis tick={AX} tickLine={false} axisLine={false} width={30} unit="g"/>
                <Tooltip contentStyle={TT.contentStyle} labelStyle={TT.labelStyle} formatter={(v: number) => [`${v}g`]}/>
                <Bar dataKey="protein" stackId="m" fill="#06c8a0" radius={[0,0,0,0]} isAnimationActive={false}/>
                <Bar dataKey="carbs" stackId="m" fill="#f59e0b" radius={[0,0,0,0]} isAnimationActive={false}/>
                <Bar dataKey="fat" stackId="m" fill="#818cf8" radius={[4,4,0,0]} isAnimationActive={false}/>
              </BarChart>
            </ResponsiveContainer>
            <div className="vn-legend">
              <span><span className="vn-legend-dot" style={{ background: '#06c8a0' }}/> Protein</span>
              <span><span className="vn-legend-dot" style={{ background: '#f59e0b' }}/> Carbs</span>
              <span><span className="vn-legend-dot" style={{ background: '#818cf8' }}/> Fat</span>
            </div>
          </div>
        </>
      )}
    </div>
  );
}

// ── ROOT ──────────────────────────────────────────────────────────────────────

type Page = 'today' | 'sleep' | 'body' | 'activity' | 'readiness' | 'protocols' | 'nutrition';

const PAGES: { id: Page; label: string }[] = [
  { id: 'today',     label: 'Today' },
  { id: 'sleep',     label: 'Sleep' },
  { id: 'body',      label: 'Body' },
  { id: 'activity',  label: 'Activity' },
  { id: 'readiness', label: 'Readiness' },
  { id: 'nutrition', label: 'Nutrition' },
  { id: 'protocols', label: 'Protocols' },
];

function VitaraInner() {
  const [page, setPage] = useState<Page>('today');
  const { data: status, isPending, isError } = useQuery<OuraStatus>({ queryKey: ['oura-status'], queryFn: () => get(`${API}/api/oura/status`) });
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

      {isPending && <div className="v-connecting"><div className="v-connecting-dot"/>Connecting to Vitara...</div>}
      {!isPending && isError && <BackendDown/>}
      {!isPending && !isError && status && !status.linked && <NotLinked/>}
      {!isPending && !isError && status?.linked && (
        <>
          {status.expired && <OuraExpiredBanner/>}
          <nav className="module-subnav" style={MC}>
            {PAGES.map(p => (
              <button key={p.id} className={`module-tab ${page === p.id ? 'active' : ''}`} onClick={() => setPage(p.id)}>{p.label}</button>
            ))}
          </nav>
          {page === 'today'     && <TodayPage status={status}/>}
          {page === 'sleep'     && <SleepPage/>}
          {page === 'body'      && <BodyPage/>}
          {page === 'activity'  && <ActivityPage/>}
          {page === 'readiness' && <ReadinessPage/>}
          {page === 'nutrition' && <NutritionPage/>}
          {page === 'protocols' && <ProtocolsPage/>}
        </>
      )}
    </div>
  );
}

export default function VitaraModule() {
  return <QueryClientProvider client={qc}><VitaraInner/></QueryClientProvider>;
}
