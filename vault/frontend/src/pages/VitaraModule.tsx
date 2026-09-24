import { useState, Component } from 'react';
import type { ReactNode } from 'react';
import { QueryClientProvider, useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { makeModuleQueryClient } from '../services/moduleQuery';
import {
  Area, AreaChart, Bar, BarChart, CartesianGrid, Cell, Line, LineChart, ReferenceLine, ResponsiveContainer, Tooltip, XAxis, YAxis,
} from 'recharts';
import { authHeaders } from '../services/auth';
import { moduleApi } from '../services/apiHost';
import { VitaraMetricsCatalogue } from '../components/VitaraMetricsCatalogue';
import { Shell as HxShell, Tabs as HxTabs, Card, Ring, Stat, Chip, Delta as HxDelta, SectionHead, Empty, Info } from '../components/health/HealthKit';
import '../styles/modules.css';
import '../styles/vitara.css';

const API = moduleApi(5100);
const qc  = makeModuleQueryClient(5 * 60_000);

// ── Types ────────────────────────────────────────────────────────────────────

// Every metric block carries its OWN day and age. Oura syncs once daily and a night
// the ring wasn't worn leaves a gap, so "latest" can be several days old — the API has
// always said so per block, and this interface used to drop it on the floor, which let
// the dashboard present four-day-old numbers as today's vitals with nothing to give it
// away. `Freshness` renders that age wherever a value is shown.
interface Dated { day: string; daysAgo: number }

interface ImpactFinding { dimension: string; group: string; samples: number; avgAfter: number; delta: number }
interface ImpactReport { baseline: number; baselineDays: number; findings: ImpactFinding[]; note?: string }

interface Dashboard {
  date: string;
  profile?: { age?: number; weight?: number; height?: number; biologicalSex?: string };
  sleep?: Dated & { score?: number; totalMinutes: number; deepMinutes: number; remMinutes: number; lightMinutes: number; efficiency: number; hrv?: number; lowestHr?: number; breathingRate?: number; spo2?: number; skinTemp?: number };
  readiness?: Dated & { score?: number; level?: string; restingHr?: number; hrvBalance?: number; recoveryIndex?: number; activityBalance?: number; sleepBalance?: number; tempDeviation?: number };
  activity?: Dated & { score?: number; steps: number; activeCalories: number; totalCalories: number; highMinutes: number; mediumMinutes: number; lowMinutes: number; distance: number };
  stress?: Dated & { summary?: string; stressMinutes?: number; recoveryMinutes?: number };
  resilience?: Dated & { level?: string; sleepRecovery?: number; daytimeRecovery?: number; stressScore?: number };
  spo2Data?: Dated & { average?: number; breathingDisturbance?: number };
  cardiovascularAge?: number;
  vo2Max?: number;
  weeklyAvg?: { hrv: number; rhr: number; sleepScore: number; readinessScore: number; steps: number; activityScore: number };
  recentWorkouts?: { activity: string; calories?: number; distance?: number; intensity?: string; startTime?: string }[];
  latestHeartRate?: { timestamp: string; bpm: number };
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

// Chart chrome, on paper: hairline grid one step off the surface, recessive axis
// text, and a tooltip that reads as a small card rather than a dark tooltip bubble.
const AX = { fill: 'var(--text3)', fontSize: 11 };
const GRID = { stroke: 'var(--border)' };
const TT = {
  contentStyle: {
    background: 'var(--surface)', border: '1px solid var(--border)', borderRadius: 12,
    fontSize: 12, color: 'var(--text)', padding: '8px 12px',
    boxShadow: '0 8px 24px -12px rgba(19,32,27,0.25)',
  },
  labelStyle: { color: 'var(--text3)', marginBottom: 4 },
};

function Skel({ h = 180 }: { h?: number }) { return <div className="v-chart-skel" style={{ height: h }}/>; }

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

// Tile accents. Decoration, grouped by what the measurement is about -- heart, night,
// movement, temperature, body, breath -- so a wall of numbers reads as a set rather than
// a spreadsheet. Meaning never rides on these: status is always a word or a chip.
const HEART = 'var(--hx-5)';
const NIGHT = 'var(--hx-2)';
const MOVE  = 'var(--hx-1)';
const TEMP  = 'var(--hx-3)';
const BODY  = 'var(--hx-4)';
const AIR   = 'var(--hx-6)';

function toneFor(score?: number | null): string {
  if (score == null) return 'var(--text3)';
  if (score >= 85) return 'var(--hx-good)';
  if (score >= 70) return 'var(--hx-1)';
  if (score >= 50) return 'var(--hx-warn)';
  return 'var(--hx-bad)';
}

// One sentence, written from what actually arrived. The page used to open with three
// score rings and no claim at all -- the reader had to assemble the answer themselves.
function headline(d: Dashboard): { line: string; sub: string } {
  const r = d.readiness?.score;
  const slept = d.sleep ? fmtMin(d.sleep.totalMinutes) : null;

  if (r == null && !slept) {
    return {
      line: 'Nothing has arrived for today yet.',
      sub: 'Your ring uploads when you open the Oura app, usually in the morning. Everything below is the last reading each measurement had.',
    };
  }

  const mood = r == null ? 'Today' : r >= 85 ? 'Well recovered' : r >= 70 ? 'Steady' : r >= 50 ? 'Take it easy' : 'Run down';
  const parts: string[] = [];
  if (slept) parts.push('you slept ' + slept);
  if (d.readiness?.restingHr != null) parts.push('resting heart rate ' + d.readiness.restingHr + ' bpm');
  if (d.activity?.steps != null) parts.push(d.activity.steps.toLocaleString() + ' steps so far');

  return {
    line: r == null ? 'Today' : mood + ' — readiness ' + Math.round(r),
    sub: parts.length ? parts.join(', ') + '.' : 'No detail behind it yet today.',
  };
}

function TodayPage({ status }: { status: OuraStatus }) {
  const qClient = useQueryClient();
  const { data: d } = useQuery<Dashboard>({ queryKey: ['dashboard'], queryFn: () => get(`${API}/api/dashboard`), refetchInterval: 60_000 });
  const sync = useMutation({
    mutationFn: () => fetch(`${API}/api/oura/sync`, { method: 'POST', headers: authHeaders() }).then(r => { if (!r.ok) throw new Error(r.status.toString()); return r.json(); }),
    onSuccess: () => qClient.invalidateQueries(),
  });

  if (!d) return <Skel h={400}/>;

  // Hours since Oura last uploaded. Null when it has never synced, which the empty
  // state already covers -- the banner below is for a connection that WAS working.
  const staleHours = status.lastSyncedAt
    ? (Date.now() - new Date(status.lastSyncedAt).getTime()) / 3_600_000
    : null;

  const { line, sub } = headline(d);
  const hr = d.latestHeartRate?.bpm ?? d.readiness?.restingHr ?? null;
  const samples = d.heartRateSamples ?? [];

  return (
    <div>
      {staleHours != null && staleHours >= 36 && (
        <div className="hx-warn-banner">
          <span className="hx-dot warn" style={{ marginTop: 6 }}/>
          <span>
            <b>Your ring last uploaded {relTime(status.lastSyncedAt)}.</b> Everything below predates that.
            Open the Oura app on your phone to let it upload, then press Sync.
          </span>
        </div>
      )}

      {/* Hero: the answer first, the scores beside it. */}
      <div className="hx-hero">
        <Card className="hx-hero-main">
          <Ring score={d.readiness?.score} label="readiness" tone={toneFor(d.readiness?.score)} />
          <div className="hx-hero-copy">
            <p className="hx-eyebrow">
              {new Date(d.date ?? Date.now()).toLocaleDateString('en-US', { weekday: 'long', month: 'long', day: 'numeric' })}
            </p>
            <h2 className="hx-headline">{line}</h2>
            <p className="hx-sub">{sub}</p>
            {d.readiness?.level && (
              <div style={{ marginTop: '0.6rem' }}>
                <Chip tone={(d.readiness.score ?? 0) >= 70 ? 'good' : (d.readiness.score ?? 0) >= 50 ? 'warn' : 'bad'}>
                  {d.readiness.level.replace('_', ' ')}
                </Chip>
              </div>
            )}
          </div>
        </Card>

        <div className="hx-grid hx-grid-4">
          <div className="hx-stat" style={{ ['--tile' as string]: NIGHT }}>
            <div className="hx-stat-head">
              <span className="hx-stat-label">
                Sleep
                <Info>Total time actually asleep, not time in bed. The score weighs how long you slept, when, and how broken it was.</Info>
              </span>
              {d.sleep?.daysAgo ? <Chip tone="warn">{d.sleep.daysAgo}d old</Chip> : null}
            </div>
            <div className="hx-stat-value">
              <span className="hx-stat-num" style={d.sleep ? { color: NIGHT } : undefined}>{d.sleep ? fmtMin(d.sleep.totalMinutes) : '—'}</span>
            </div>
            <span className="hx-stat-sub">{d.sleep?.score != null ? `score ${d.sleep.score}` : 'No night recorded yet'}</span>
          </div>

          <Stat
            label="Steps"
            accent={MOVE}
            value={d.activity?.steps != null ? d.activity.steps.toLocaleString() : null}
            sub={d.weeklyAvg?.steps != null ? `usually ${Math.round(d.weeklyAvg.steps).toLocaleString()}` : 'no weekly average yet'}
            empty="Nothing counted today"
            info="Counted by the ring. The comparison is your own recent average, not a ten-thousand-step target."
          />

          <Stat
            label="Heart rate"
            accent={HEART}
            value={hr}
            unit="bpm"
            sub={d.latestHeartRate ? `latest, ${relTime(d.latestHeartRate.timestamp)}` : 'overnight resting rate'}
            empty="No reading today"
            info="The most recent beat-rate the ring recorded. Your overnight resting rate sits on the Recovery tab."
          />

          <Stat
            label="HRV"
            accent={NIGHT}
            value={d.sleep?.hrv != null ? Math.round(d.sleep.hrv) : null}
            unit="ms"
            sub={<HxDelta value={d.sleep?.hrv} reference={d.weeklyAvg?.hrv} goodWhen="higher" unit=" ms"/>}
            empty="Not measured last night"
            info="Heart rate variability: the spacing between beats while you slept. Higher usually means better recovered, and only your own trend is meaningful — never someone else's number."
          />
        </div>
      </div>

      {/* Everything else the ring reported, each saying so when it reported nothing. */}
      <SectionHead title="Overnight" note="Measured while you slept, against your own recent average." />
      <div className="hx-grid hx-grid-4">
        <Stat
          label="Resting heart rate"
          accent={HEART}
          value={d.readiness?.restingHr}
          unit="bpm"
          sub={<HxDelta value={d.readiness?.restingHr} reference={d.weeklyAvg?.rhr} goodWhen="lower" unit=" bpm"/>}
          empty="No overnight reading"
          info="The lowest sustained rate your heart held overnight. It rises with illness, alcohol, a late meal or a hard training block."
        />
        <Stat
          label="Breathing rate"
          accent={NIGHT}
          value={d.sleep?.breathingRate != null ? d.sleep.breathingRate.toFixed(1) : null}
          unit="/min"
          sub="while asleep"
          empty="Not measured last night"
          info="Breaths per minute while you slept. It is remarkably steady night to night, which is exactly what makes a change worth noticing."
        />
        <Stat
          label="Blood oxygen"
          accent={AIR}
          value={d.spo2Data?.average != null ? d.spo2Data.average.toFixed(1) : (d.sleep?.spo2 != null ? d.sleep.spo2.toFixed(1) : null)}
          unit="%"
          sub={d.spo2Data?.breathingDisturbance != null ? `disturbance index ${d.spo2Data.breathingDisturbance}` : 'averaged through the night'}
          empty="Not measured last night"
          info="The share of your blood carrying oxygen, averaged across the night. Readings that sit below about 95% night after night are worth raising with a doctor."
        />
        <Stat
          label="Skin temperature"
          accent={TEMP}
          value={d.sleep?.skinTemp != null ? `${d.sleep.skinTemp > 0 ? '+' : ''}${d.sleep.skinTemp.toFixed(2)}` : null}
          unit="°C"
          sub="vs your own usual"
          empty="Not measured last night"
          info="A deviation from your own baseline, not an absolute temperature. A sustained rise is often the earliest sign that something is coming."
        />
        <Stat
          label="Stress"
          accent={TEMP}
          value={d.stress?.summary ? d.stress.summary.replace('_', ' ') : null}
          sub={d.stress?.recoveryMinutes != null ? `${d.stress.recoveryMinutes} min of recovery` : 'daytime reading'}
          empty="No stress reading today"
          info="Built from daytime heart rate and skin signals. Recovery minutes are the time your body spent settling back down again."
        />
        <Stat
          label="Resilience"
          accent={BODY}
          value={d.resilience?.level ? d.resilience.level.replace('_', ' ') : null}
          sub={d.resilience?.sleepRecovery != null ? `sleep recovery ${d.resilience.sleepRecovery}` : 'long-run measure'}
          empty="Needs a few weeks of wear"
          info="How well you bounce back from load, built from weeks of sleep and daytime recovery rather than from any single day."
        />
        <Stat
          label="Cardiovascular age"
          accent={HEART}
          value={d.cardiovascularAge != null ? Math.round(d.cardiovascularAge) : null}
          unit="yrs"
          sub={d.profile?.age != null ? `you are ${d.profile.age}` : 'estimated'}
          empty="Needs more wear to estimate"
          info="Oura's estimate of how old your vascular measurements look. An estimate from a ring, not a diagnosis."
        />
        <Stat
          label="VO₂ max"
          accent={MOVE}
          value={d.vo2Max != null ? d.vo2Max.toFixed(1) : null}
          unit="ml/kg/min"
          sub="aerobic fitness"
          empty="Not estimated yet"
          info="The oxygen your body can use at full effort — the standard measure of aerobic fitness. It moves over months of training, not over days."
        />
      </div>

      {/* Last night, as one bar. */}
      <SectionHead
        title="Last night"
        note={d.sleep ? `${fmtMin(d.sleep.totalMinutes)} asleep · ${d.sleep.efficiency}% efficient` : undefined}
      />
      {d.sleep ? (
        <div className="hx-chart">
          <div className="hx-chart-head">
            <span className="hx-chart-title">Sleep stages</span>
            <span className="hx-legend">
              <span><i style={{ background: 'var(--hx-2)' }}/>Deep {fmtMin(d.sleep.deepMinutes)}</span>
              <span><i style={{ background: 'var(--hx-4)' }}/>REM {fmtMin(d.sleep.remMinutes)}</span>
              <span><i style={{ background: 'var(--hx-6)' }}/>Light {fmtMin(d.sleep.lightMinutes)}</span>
            </span>
          </div>
          {/* One bar, three segments, separated by the surface rather than by borders.
              The legend above names them, so the bar carries no labels of its own. */}
          <div className="hx-stages">
            {[
              { m: d.sleep.deepMinutes, c: 'var(--hx-2)', n: 'Deep' },
              { m: d.sleep.remMinutes, c: 'var(--hx-4)', n: 'REM' },
              { m: d.sleep.lightMinutes, c: 'var(--hx-6)', n: 'Light' },
            ].map(seg => (
              <span
                key={seg.n}
                title={`${seg.n} ${fmtMin(seg.m)}`}
                style={{
                  background: seg.c,
                  width: `${(seg.m / (d.sleep!.deepMinutes + d.sleep!.remMinutes + d.sleep!.lightMinutes || 1)) * 100}%`,
                }}
              />
            ))}
          </div>
        </div>
      ) : (
        <Empty title="No night recorded.">Wear the ring overnight and it will appear here after the next sync.</Empty>
      )}

      {/* 24h heart rate. */}
      <SectionHead
        title="Heart rate today"
        note={samples.length > 0 ? `${Math.min(...samples.map(h => h.bpm))}–${Math.max(...samples.map(h => h.bpm))} bpm` : undefined}
      />
      {samples.length > 0 ? (
        <div className="hx-chart">
          <ResponsiveContainer width="100%" height={140}>
            <AreaChart
              data={samples.map(h => ({ t: new Date(h.timestamp).toLocaleTimeString('en-US', { hour: 'numeric' }), bpm: h.bpm }))}
              margin={{ top: 6, right: 6, bottom: 0, left: 0 }}
            >
              <defs>
                <linearGradient id="hxHr" x1="0" y1="0" x2="0" y2="1">
                  <stop offset="0%" stopColor="var(--hx-5)" stopOpacity={0.16}/>
                  <stop offset="100%" stopColor="var(--hx-5)" stopOpacity={0}/>
                </linearGradient>
              </defs>
              <CartesianGrid stroke="var(--border)" vertical={false}/>
              <XAxis dataKey="t" tick={{ fill: 'var(--text3)', fontSize: 11 }} tickLine={false} axisLine={false} minTickGap={40}/>
              <YAxis width={34} tick={{ fill: 'var(--text3)', fontSize: 11 }} tickLine={false} axisLine={false}/>
              <Area type="monotone" dataKey="bpm" stroke="var(--hx-5)" fill="url(#hxHr)" strokeWidth={2} dot={false} isAnimationActive={false}/>
              <Tooltip contentStyle={TT.contentStyle} labelStyle={TT.labelStyle}/>
            </AreaChart>
          </ResponsiveContainer>
        </div>
      ) : (
        <Empty title="No heart-rate samples today.">These arrive with the ring's next upload.</Empty>
      )}

      {/* Workouts. */}
      <SectionHead title="Recent workouts" />
      {d.recentWorkouts && d.recentWorkouts.length > 0 ? (
        <div className="hx-grid hx-grid-3">
          {d.recentWorkouts.map((w, i) => (
            <div key={i} className="hx-stat">
              <div className="hx-stat-head">
                <span className="hx-stat-label" style={{ textTransform: 'capitalize' }}>{w.activity}</span>
                {w.intensity && <Chip>{w.intensity}</Chip>}
              </div>
              <span className="hx-stat-sub">
                {[
                  w.calories != null ? `${w.calories} cal` : null,
                  w.distance ? `${(w.distance / 1000).toFixed(1)} km` : null,
                  w.startTime ? relTime(w.startTime) : null,
                ].filter(Boolean).join(' · ') || 'No detail recorded'}
              </span>
            </div>
          ))}
        </div>
      ) : (
        <Empty title="No workouts in the last week.">Sessions the ring detects, or ones you log, show up here.</Empty>
      )}

      <div style={{ display: 'flex', justifyContent: 'flex-end', marginTop: '1.25rem' }}>
        <button className="hx-btn hx-btn-ghost" onClick={() => sync.mutate()} disabled={sync.isPending}>
          {sync.isPending ? 'Syncing…' : sync.isError ? 'Sync failed — try again' : 'Sync with Oura'}
        </button>
      </div>
    </div>
  );
}

// ── SLEEP ─────────────────────────────────────────────────────────────────────

function SleepPage() {
  const { data, isPending, isError, error } = useQuery<Sleep[]>({ queryKey: ['sleep', 14], queryFn: () => get(`${API}/api/sleep?days=14`) });

  if (isPending) return <Skel h={300}/>;
  if (isError) return <Empty title="Couldn't load your sleep.">{String(error)}</Empty>;
  if (!data?.length) {
    return (
      <Empty title="No nights recorded yet.">
        Wear the ring overnight; the night appears here after your next sync. Nights imported
        from Apple Health show up here too.
      </Empty>
    );
  }

  const a = {
    score: avg(data.map(s => s.score)), hrv: avg(data.map(s => s.avgHrv)),
    deep: avg(data.map(s => s.deepMinutes)), rem: avg(data.map(s => s.remMinutes)),
    total: avg(data.map(s => s.totalSleepMinutes)), eff: avg(data.map(s => s.efficiency)),
  };

  const byDay = new Map<string, Sleep>();
  for (const s of data) { const ex = byDay.get(s.day); if (!ex || s.totalSleepMinutes > ex.totalSleepMinutes) byDay.set(s.day, s); }
  const nights = [...byDay.values()].sort((x, y) => y.day.localeCompare(x.day));
  const lastNight = nights[0];

  const rows = nights.map(s => {
    const start2 = new Date(s.bedtimeStart), end2 = new Date(s.bedtimeEnd);
    const anchor = timelineAnchor(start2);
    const offset = timelineOffset(anchor, start2);
    const duration = timelineOffset(anchor, end2) - offset;
    return {
      key: s.id, day: dayLabel(s.day), offset, duration, efficiency: s.efficiency,
      bedLabel: fmtClock(s.bedtimeStart), wakeLabel: fmtClock(s.bedtimeEnd),
      totalLabel: fmtMin(s.totalSleepMinutes), deepLabel: fmtMin(s.deepMinutes),
      remLabel: fmtMin(s.remMinutes), lightLabel: fmtMin(s.lightMinutes), awakeLabel: fmtMin(s.awakeMinutes),
    };
  });
  const rawMin = Math.min(...rows.map(r => r.offset)), rawMax = Math.max(...rows.map(r => r.offset + r.duration));
  const domainMin = Math.floor(rawMin / 3) * 3, domainMax = Math.ceil(rawMax / 3) * 3;
  const ticks: number[] = []; for (let h = domainMin; h <= domainMax; h += 3) ticks.push(h);

  const stageTotal = lastNight ? (lastNight.deepMinutes + lastNight.remMinutes + lastNight.lightMinutes) || 1 : 1;

  return (
    <div>
      {/* Last night first; the fortnight's average is context underneath. A 14-day mean
          is the wrong headline for something you check each morning -- it barely moves,
          so it cannot answer "how did I sleep", and it averages a bad night away. */}
      <div className="hx-hero">
        <Card className="hx-hero-main">
          <Ring score={lastNight?.score} label="sleep score" tone={toneFor(lastNight?.score)}/>
          <div className="hx-hero-copy">
            <p className="hx-eyebrow">Last night · {lastNight ? dayLabel(lastNight.day) : ''}</p>
            <h2 className="hx-headline">{lastNight ? fmtMin(lastNight.totalSleepMinutes) : '—'} asleep</h2>
            <p className="hx-sub">
              {lastNight
                ? `${fmtClock(lastNight.bedtimeStart)} to ${fmtClock(lastNight.bedtimeEnd)} · ${Math.round(lastNight.efficiency * 100)}% of your time in bed`
                : 'No night recorded.'}
            </p>
            {lastNight && (
              <>
                <div className="hx-stages" style={{ marginTop: '0.75rem' }}>
                  <span style={{ width: `${lastNight.deepMinutes / stageTotal * 100}%`, background: 'var(--hx-2)' }} title={`Deep ${fmtMin(lastNight.deepMinutes)}`}/>
                  <span style={{ width: `${lastNight.remMinutes / stageTotal * 100}%`, background: 'var(--hx-4)' }} title={`REM ${fmtMin(lastNight.remMinutes)}`}/>
                  <span style={{ width: `${lastNight.lightMinutes / stageTotal * 100}%`, background: 'var(--hx-6)' }} title={`Light ${fmtMin(lastNight.lightMinutes)}`}/>
                </div>
                <span className="hx-legend" style={{ marginTop: '0.5rem' }}>
                  <span><i style={{ background: 'var(--hx-2)' }}/>Deep {fmtMin(lastNight.deepMinutes)}</span>
                  <span><i style={{ background: 'var(--hx-4)' }}/>REM {fmtMin(lastNight.remMinutes)}</span>
                  <span><i style={{ background: 'var(--hx-6)' }}/>Light {fmtMin(lastNight.lightMinutes)}</span>
                  <span><i style={{ background: 'var(--border2)' }}/>Awake {fmtMin(lastNight.awakeMinutes)}</span>
                </span>
              </>
            )}
          </div>
        </Card>

        <div className="hx-grid hx-grid-4">
          <Stat label="Deep" accent="var(--sleep-deep)" value={lastNight ? fmtMin(lastNight.deepMinutes) : null}
                sub={<HxDelta value={lastNight?.deepMinutes} reference={a.deep} goodWhen="higher" unit=" min"/>}
                info="The deepest stage, when most physical repair happens. It is usually the first thing a short night takes away."
                empty="Not measured"/>
          <Stat label="REM" accent="var(--sleep-rem)" value={lastNight ? fmtMin(lastNight.remMinutes) : null}
                sub={<HxDelta value={lastNight?.remMinutes} reference={a.rem} goodWhen="higher" unit=" min"/>}
                info="The dreaming stage, tied to memory and mood. It comes mostly in the second half of the night, so waking early cuts it first."
                empty="Not measured"/>
          <Stat label="HRV" accent="var(--hx-2)" value={lastNight?.avgHrv != null ? Math.round(lastNight.avgHrv) : null} unit="ms"
                sub={<HxDelta value={lastNight?.avgHrv} reference={a.hrv} goodWhen="higher" unit=" ms"/>}
                info="Heart rate variability averaged across the night. Compared against your own recent nights, never against anyone else's."
                empty="Not measured"/>
          <Stat label="Efficiency" accent="var(--sleep-light)" value={lastNight != null ? Math.round(lastNight.efficiency * 100) : null} unit="%"
                sub={a.eff != null ? `usually ${Math.round(a.eff * 100)}%` : 'no average yet'}
                info="The share of your time in bed that you were actually asleep. Above about 85% is generally considered good."
                empty="Not measured"/>
        </div>
      </div>

      <SectionHead title="When you slept" note="Each bar is one night, from lights out to waking." />
      <div className="hx-chart">
        <ResponsiveContainer width="100%" height={rows.length * 30 + 44}>
          <BarChart data={rows} layout="vertical" margin={{ top: 4, right: 16, bottom: 0, left: 0 }} barCategoryGap="34%">
            <CartesianGrid horizontal={false} {...GRID}/>
            <XAxis type="number" domain={[domainMin, domainMax]} ticks={ticks} tickFormatter={timelineTickLabel} tick={AX} tickLine={false} axisLine={false}/>
            <YAxis type="category" dataKey="day" tick={AX} tickLine={false} axisLine={false} width={56}/>
            <Tooltip content={<SleepTooltip/>} cursor={{ fill: 'rgba(15,138,114,0.06)' }}/>
            <Bar dataKey="offset" stackId="t" fill="transparent" isAnimationActive={false}/>
            {/* One series, one colour: the bar's LENGTH is the story, and colouring each
                night by its own score would double-encode it in the only free channel. */}
            <Bar dataKey="duration" stackId="t" radius={5} fill="var(--hx-1)" isAnimationActive={false}/>
          </BarChart>
        </ResponsiveContainer>
      </div>

      <SectionHead title="Fourteen nights" note={a.total != null ? `usually ${fmtMin(Math.round(a.total))} asleep` : undefined} />
      <div className="hx-chart">
        <ResponsiveContainer width="100%" height={180}>
          <BarChart data={[...nights].reverse().map(s => ({ day: shortDay(s.day), hours: +(s.totalSleepMinutes / 60).toFixed(2), score: s.score ?? null }))}
                    margin={{ top: 8, right: 8, bottom: 0, left: 0 }}>
            <CartesianGrid vertical={false} {...GRID}/>
            <XAxis dataKey="day" tick={AX} tickLine={false} axisLine={false}/>
            <YAxis width={30} tick={AX} tickLine={false} axisLine={false} unit="h"/>
            <Tooltip contentStyle={TT.contentStyle} labelStyle={TT.labelStyle} formatter={(v: number) => [`${v} h`, 'asleep']}/>
            {a.total != null && <ReferenceLine y={+(a.total / 60).toFixed(2)} stroke="var(--text3)" strokeWidth={1}/>}
            <Bar dataKey="hours" radius={[5, 5, 0, 0]} fill="var(--hx-1)" isAnimationActive={false}/>
          </BarChart>
        </ResponsiveContainer>
        <p className="hx-chart-note" style={{ marginTop: '0.5rem' }}>The line is your own 14-night average.</p>
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
  const { data: bio } = useQuery<{ bioAge?: number; chronologicalAge: number; delta?: number; cardiovascularAge?: number; vo2Max?: number; factors: { hrvScore?: number; restingHrScore?: number; sleepScore?: number; readinessScore?: number; recoveryTrend?: number }; dataQuality: string; ageSource: string; label?: string; disclaimer?: string }>({
    queryKey: ['bioage'], queryFn: () => get(`${API}/api/bioage`),
  });
  const { data: profile } = useQuery<{ synced: boolean; height?: number }>({ queryKey: ['profile'], queryFn: () => get(`${API}/api/profile`) });
  const { data: weighIns } = useQuery<WeighInItem[]>({ queryKey: ['weighins'], queryFn: () => get(`${API}/api/weighins?days=180`) });
  const { data: ageHist } = useQuery<AgeHistory>({ queryKey: ['age-history'], queryFn: () => get(`${API}/api/bioage/history?days=90`) });

  // Weight is entered and shown in POUNDS, but stored as kilograms -- kg stays the
  // canonical unit so BMI math and the iPhone HealthKit sync keep working unchanged.
  const [weight, setWeight] = useState('');
  const logWeight = useMutation({
    mutationFn: () => send(`${API}/api/weighins`, 'POST', { weightKg: lbToKg(parseFloat(weight)) }),
    onSuccess: () => { setWeight(''); qClient.invalidateQueries({ queryKey: ['weighins'] }); },
  });

  const younger = (bio?.delta ?? 0) < 0;
  const heightM = profile?.height;
  const bmiOf = (kg: number) => heightM && heightM > 0 ? kg / (heightM * heightM) : null;
  const weightChart = (weighIns ?? []).map(w => ({ day: dayLabel(w.day), weight: +kgToLb(w.weightKg).toFixed(1) }));
  const latestWeight = weighIns && weighIns.length > 0 ? weighIns[weighIns.length - 1] : null;
  const latestBmi = latestWeight ? bmiOf(latestWeight.weightKg) : null;

  const vo2Series = (ageHist?.vo2Max ?? []).map(v => ({ day: dayLabel(v.day), value: v.value }));
  const cardioSeries = (ageHist?.cardiovascularAge ?? []).map(c => ({ day: dayLabel(c.day), value: c.value }));

  return (
    <div>
      {/* The estimate, labelled, with the disclaimer the API itself carries. */}
      <div className="hx-hero">
        <Card className="hx-hero-main">
          <div className="hx-hero-copy">
            <p className="hx-eyebrow">Biological age · {bio?.label ?? 'Estimate'}</p>
            {bio?.bioAge != null ? (
              <>
                <h2 className="hx-headline" style={{ fontSize: '2.4rem', lineHeight: 1.05 }}>{bio.bioAge.toFixed(1)}</h2>
                <p className="hx-sub">
                  <b style={{ color: younger ? 'var(--hx-good)' : 'var(--hx-bad)' }}>
                    {Math.abs(bio.delta!).toFixed(1)} years {younger ? 'younger' : 'older'}
                  </b>{' '}
                  than your age, {bio.chronologicalAge}.
                </p>
                {bio.disclaimer && <p className="hx-sub" style={{ marginTop: '0.6rem', fontSize: '0.78rem', color: 'var(--text3)' }}>{bio.disclaimer}</p>}
              </>
            ) : (
              <p className="hx-sub">
                {bio?.dataQuality === 'insufficient'
                  ? 'Needs at least three days of sleep and readiness data before it can be estimated.'
                  : 'Working it out…'}
              </p>
            )}
          </div>
        </Card>

        <div className="hx-grid hx-grid-4">
          <Stat label="Cardiovascular age" accent={HEART} value={bio?.cardiovascularAge != null ? Math.round(bio.cardiovascularAge) : null} unit="yrs"
                sub={bio?.chronologicalAge ? `you are ${bio.chronologicalAge}` : 'estimated'}
                info="Oura's estimate of how old your vascular measurements look. An estimate from a ring, not a diagnosis."
                empty="Needs more wear"/>
          <Stat label="VO₂ max" accent={MOVE} value={bio?.vo2Max?.toFixed(1)} unit="ml/kg/min" sub="aerobic fitness"
                info="The oxygen your body can use at full effort. It moves over months of training, not over days."
                empty="Not estimated yet"/>
          <Stat label="Weight" accent={BODY} value={latestWeight ? kgToLb(latestWeight.weightKg).toFixed(1) : null} unit="lb"
                sub={latestWeight ? `recorded ${dayLabel(latestWeight.day)}` : undefined}
                info="Shown in pounds, stored in kilograms so the phone sync and BMI stay consistent."
                empty="Nothing recorded yet"/>
          <Stat label="BMI" accent={BODY} value={latestBmi != null ? latestBmi.toFixed(1) : null}
                sub={heightM ? 'weight against height' : 'Set your height on the Record tab'}
                info="Weight against height. It says nothing about muscle or about where fat sits, which is why waist-to-height is the better guide."
                empty="Needs weight and height"/>
        </div>
      </div>

      <SectionHead title="Record a weight" info="Enter pounds. It is stored in kilograms so the phone sync and the BMI calculation stay consistent with each other."/>
      <Card>
        <div style={{ display: 'flex', gap: '0.6rem', alignItems: 'center', flexWrap: 'wrap' }}>
          <input type="number" step="0.1" placeholder="lb" value={weight}
                 onChange={e => setWeight(e.target.value)}
                 onKeyDown={e => e.key === 'Enter' && weight && logWeight.mutate()}
                 style={{ maxWidth: 140, borderRadius: 10, padding: '0.5rem 0.7rem' }}/>
          <button className="hx-btn" disabled={!weight || logWeight.isPending} onClick={() => logWeight.mutate()}>
            {logWeight.isPending ? 'Saving…' : 'Save'}
          </button>
          {logWeight.isError && <span className="hx-delta bad">Couldn't save — try again</span>}
        </div>
      </Card>

      <SectionHead title="Weight" note="The last six months."/>
      {weightChart.length > 1 ? (
        <div className="hx-chart">
          <ResponsiveContainer width="100%" height={180}>
            <LineChart data={weightChart} margin={{ top: 8, right: 8, bottom: 0, left: 0 }}>
              <CartesianGrid vertical={false} {...GRID}/>
              <XAxis dataKey="day" tick={AX} tickLine={false} axisLine={false} interval={Math.max(0, Math.floor(weightChart.length / 6))}/>
              <YAxis tick={AX} tickLine={false} axisLine={false} width={40} domain={['dataMin - 2', 'dataMax + 2']} unit=" lb"/>
              <Tooltip contentStyle={TT.contentStyle} labelStyle={TT.labelStyle}/>
              <Line type="monotone" dataKey="weight" stroke="var(--hx-1)" strokeWidth={2} dot={false} isAnimationActive={false}/>
            </LineChart>
          </ResponsiveContainer>
        </div>
      ) : (
        <Empty title="Not enough weigh-ins to draw a line.">Record a second one and the trend starts here.</Empty>
      )}

      {/* Two charts, not one with two axes: VO2 max and cardiovascular age are measured
          in different things, and stacking them on one plot invents a relationship by
          choosing where the scales line up. */}
      <SectionHead title="Fitness over ninety days"/>
      <div className="hx-grid hx-grid-3">
        <div className="hx-chart">
          <div className="hx-chart-head"><span className="hx-chart-title">VO₂ max</span><span className="hx-chart-note">ml/kg/min</span></div>
          {vo2Series.length > 1 ? (
            <ResponsiveContainer width="100%" height={150}>
              <LineChart data={vo2Series} margin={{ top: 6, right: 8, bottom: 0, left: 0 }}>
                <CartesianGrid vertical={false} {...GRID}/>
                <XAxis dataKey="day" tick={AX} tickLine={false} axisLine={false} interval={Math.max(0, Math.floor(vo2Series.length / 4))}/>
                <YAxis tick={AX} tickLine={false} axisLine={false} width={34} domain={['dataMin - 1', 'dataMax + 1']}/>
                <Tooltip contentStyle={TT.contentStyle} labelStyle={TT.labelStyle}/>
                <Line type="monotone" dataKey="value" stroke="var(--hx-1)" strokeWidth={2} dot={false} isAnimationActive={false} connectNulls/>
              </LineChart>
            </ResponsiveContainer>
          ) : <p className="hx-chart-note">Not enough estimates yet.</p>}
        </div>

        <div className="hx-chart">
          <div className="hx-chart-head"><span className="hx-chart-title">Cardiovascular age</span><span className="hx-chart-note">years</span></div>
          {cardioSeries.length > 1 ? (
            <ResponsiveContainer width="100%" height={150}>
              <LineChart data={cardioSeries} margin={{ top: 6, right: 8, bottom: 0, left: 0 }}>
                <CartesianGrid vertical={false} {...GRID}/>
                <XAxis dataKey="day" tick={AX} tickLine={false} axisLine={false} interval={Math.max(0, Math.floor(cardioSeries.length / 4))}/>
                <YAxis tick={AX} tickLine={false} axisLine={false} width={34} domain={['dataMin - 1', 'dataMax + 1']}/>
                <Tooltip contentStyle={TT.contentStyle} labelStyle={TT.labelStyle}/>
                {ageHist?.chronologicalAge != null && <ReferenceLine y={ageHist.chronologicalAge} stroke="var(--text3)" strokeWidth={1}/>}
                <Line type="monotone" dataKey="value" stroke="var(--hx-3)" strokeWidth={2} dot={false} isAnimationActive={false} connectNulls/>
              </LineChart>
            </ResponsiveContainer>
          ) : <p className="hx-chart-note">Not enough estimates yet.</p>}
          {ageHist?.chronologicalAge != null && cardioSeries.length > 1 && (
            <p className="hx-chart-note" style={{ marginTop: '0.4rem' }}>The line is your actual age, {ageHist.chronologicalAge}.</p>
          )}
        </div>
      </div>
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
  const { data, isPending, isError, error } = useQuery<Activity[]>({ queryKey: ['activity', 14], queryFn: () => get(`${API}/api/activity?days=14`) });
  const { data: workouts } = useQuery<WorkoutItem[]>({ queryKey: ['workouts'], queryFn: () => get(`${API}/api/workouts?days=30`) });

  if (isPending) return <Skel h={200}/>;
  if (isError) return <Empty title="Couldn't load your activity.">{String(error)}</Empty>;

  const a = {
    steps: avg(data?.map(d => d.steps) ?? []), cal: avg(data?.map(d => d.activeCalories) ?? []),
    score: avg(data?.map(d => d.score) ?? []), highMin: avg(data?.map(d => d.highActivityMinutes) ?? []),
  };
  const today = data?.length ? latest(data) : undefined;   // endpoints return oldest-first
  const stepsChart = (data ?? []).map(d => ({ day: shortDay(d.day), steps: d.steps, cal: d.activeCalories }));

  return (
    <div>
      <div className="hx-hero">
        <Card className="hx-hero-main">
          <Ring score={today?.score} label="activity" tone={toneFor(today?.score)}/>
          <div className="hx-hero-copy">
            <p className="hx-eyebrow">{today ? dayLabel(today.day) : 'Today'}</p>
            <h2 className="hx-headline">
              {today ? `${today.steps.toLocaleString()} steps` : 'Nothing counted yet'}
            </h2>
            <p className="hx-sub">
              {today
                ? `${Math.round(today.activeCalories)} kcal beyond resting · ${today.highActivityMinutes} min at high intensity`
                : 'Wear the ring, or log a workout below, and the day starts filling in.'}
            </p>
          </div>
        </Card>

        <div className="hx-grid hx-grid-4">
          <Stat label="Steps" accent={MOVE} value={today?.steps?.toLocaleString()}
                sub={<HxDelta value={today?.steps} reference={a.steps} goodWhen="higher"/>}
                info="Counted by the ring. The comparison is your own recent average, not a ten-thousand-step target."
                empty="Nothing counted today"/>
          <Stat label="Active calories" accent={TEMP} value={today?.activeCalories != null ? Math.round(today.activeCalories) : null} unit="kcal"
                sub={<HxDelta value={today?.activeCalories} reference={a.cal} goodWhen="higher" unit=" kcal"/>}
                info="Burned above resting, estimated from movement and heart rate. It excludes the calories you burn just being alive."
                empty="Nothing counted today"/>
          <Stat label="High intensity" accent={HEART} value={today?.highActivityMinutes} unit="min"
                sub={<HxDelta value={today?.highActivityMinutes} reference={a.highMin} goodWhen="higher" unit=" min"/>}
                info="Minutes spent at hard effort. Everything gentler is counted as medium or low activity instead."
                empty="None today"/>
          <Stat label="Distance" accent={AIR} value={today?.steps != null ? ((today.steps * 0.00075).toFixed(1)) : null} unit="km"
                sub="from your steps"
                info="Estimated from your step count at an average stride length, so treat it as a rough figure rather than a measurement."
                empty="Nothing counted today"/>
        </div>
      </div>

      <SectionHead title="Log a workout" note="Anything the ring won't see: weights, classes, a walk without it."/>
      <Card><LogWorkoutForm/></Card>

      {data?.length ? (
        <>
          <SectionHead title="Fourteen days" note={a.steps != null ? `usually ${Math.round(a.steps).toLocaleString()} steps` : undefined}/>
          <div className="hx-chart">
            <ResponsiveContainer width="100%" height={180}>
              <BarChart data={stepsChart} margin={{ top: 8, right: 8, bottom: 0, left: 0 }}>
                <CartesianGrid vertical={false} {...GRID}/>
                <XAxis dataKey="day" tick={AX} tickLine={false} axisLine={false}/>
                <YAxis tick={AX} tickLine={false} axisLine={false} width={44}/>
                <Tooltip contentStyle={TT.contentStyle} labelStyle={TT.labelStyle}/>
                {/* One series, one colour. Colouring each bar by whether it cleared a
                    round number re-states the bar's own height in the only free channel,
                    and 8,000 is somebody else's target anyway -- the reference line is
                    YOUR average. */}
                {a.steps != null && <ReferenceLine y={Math.round(a.steps)} stroke="var(--text3)" strokeWidth={1}/>}
                <Bar dataKey="steps" radius={[5, 5, 0, 0]} fill="var(--hx-1)" isAnimationActive={false}/>
              </BarChart>
            </ResponsiveContainer>
            <p className="hx-chart-note" style={{ marginTop: '0.5rem' }}>The line is your own 14-day average.</p>
          </div>
        </>
      ) : (
        <Empty title="No days recorded yet.">Activity arrives with the ring's daily sync.</Empty>
      )}

      <WorkoutImpactPanel/>

      <SectionHead title="Workouts" note="The last thirty days."/>
      {workouts && workouts.length > 0 ? (
        <div className="hx-grid hx-grid-3">
          {workouts.slice(0, 12).map(w => (
            <div key={w.id} className="hx-stat">
              <div className="hx-stat-head">
                <span className="hx-stat-label" style={{ textTransform: 'capitalize' }}>{w.label || w.activity}</span>
                {w.intensity && <Chip>{w.intensity}</Chip>}
              </div>
              <div className="hx-stat-value">
                <span className="hx-stat-num">{w.calories != null ? Math.round(w.calories) : '—'}</span>
                {w.calories != null && <span className="hx-stat-unit">kcal</span>}
              </div>
              <span className="hx-stat-sub">
                {[dayLabel(w.day), w.distance ? `${(w.distance / 1000).toFixed(1)} km` : null].filter(Boolean).join(' · ')}
              </span>
            </div>
          ))}
        </div>
      ) : (
        <Empty title="No workouts in the last month.">Sessions the ring detects, and ones you log above, both appear here.</Empty>
      )}
    </div>
  );
}

// ── RECOVERY ─────────────────────────────────────────────────

function ReadinessPage() {
  const { data, isPending, isError, error } = useQuery<Readiness[]>({ queryKey: ['readiness', 14], queryFn: () => get(`${API}/api/readiness?days=14`) });

  if (isPending) return <Skel h={200}/>;
  if (isError) return <Empty title="Couldn't load your recovery data.">{String(error)}</Empty>;
  if (!data?.length) {
    return (
      <Empty title="No recovery readings yet.">
        Your ring works this out from your overnight heart rate, HRV and temperature. It
        appears after the first full night of wear.
      </Empty>
    );
  }

  const a = {
    score: avg(data.map(r => r.score)), rhr: avg(data.map(r => r.restingHeartRate)),
    hrv: avg(data.map(r => r.hrvBalance)), recov: avg(data.map(r => r.recoveryIndex)),
  };
  const today = latest(data);
  const levels = data.reduce((acc, r) => { const l = r.level ?? 'unknown'; acc[l] = (acc[l] ?? 0) + 1; return acc; }, {} as Record<string, number>);
  const trend = [...data].sort((x, y) => x.day.localeCompare(y.day)).map(r => ({ day: shortDay(r.day), score: r.score ?? null }));

  return (
    <div>
      <div className="hx-hero">
        <Card className="hx-hero-main">
          <Ring score={today?.score} label="readiness" tone={toneFor(today?.score)}/>
          <div className="hx-hero-copy">
            <p className="hx-eyebrow">{today ? dayLabel(today.day) : 'Today'}</p>
            <h2 className="hx-headline">
              {today?.level ? today.level.replace('_', ' ') : today?.score != null ? 'Recovery' : 'Nothing today yet'}
              <Info label="What readiness means">
                How recovered you are, read from your overnight heart rate, heart rate variability and
                temperature — measured against your own recent nights, never against anyone else.
              </Info>
            </h2>
            <p className="hx-sub">
              {today
                ? [today.restingHeartRate != null ? `${today.restingHeartRate} bpm resting` : null,
                   today.hrvBalance != null ? `HRV balance ${today.hrvBalance}` : null,
                   today.temperatureDeviation != null
                     ? `${today.temperatureDeviation > 0 ? '+' : ''}${today.temperatureDeviation.toFixed(2)} °C`
                     : null].filter(Boolean).join(' · ') || 'Scored, but the underlying readings are missing.'
                : 'Wear the ring overnight and this fills in after the next sync.'}
            </p>
            <div style={{ display: 'flex', gap: '0.4rem', marginTop: '0.7rem', flexWrap: 'wrap' }}>
              {(['optimal', 'good', 'pay_attention'] as const).map(l => (
                <Chip key={l} tone={l === 'optimal' ? 'good' : l === 'good' ? 'neutral' : 'warn'}>
                  {levels[l] ?? 0} {l.replace('_', ' ')}
                </Chip>
              ))}
              <Chip>of the last {data.length} days</Chip>
            </div>
          </div>
        </Card>

        <div className="hx-grid hx-grid-4">
          <Stat label="Resting heart rate" accent={HEART} value={today?.restingHeartRate} unit="bpm"
                sub={<HxDelta value={today?.restingHeartRate} reference={a.rhr} goodWhen="lower" unit=" bpm"/>}
                info="The lowest sustained rate your heart held overnight. It rises with illness, alcohol, a late meal or a hard training block."
                empty="No overnight reading"/>
          <Stat label="HRV balance" accent={NIGHT} value={today?.hrvBalance} unit="/100"
                sub={<HxDelta value={today?.hrvBalance} reference={a.hrv} goodWhen="higher"/>}
                info="Last night's heart rate variability scored against your own last two weeks. Fifty is your normal."
                empty="Needs more nights"/>
          <Stat label="Recovery index" accent={BODY} value={today?.recoveryIndex} unit="/100"
                sub={<HxDelta value={today?.recoveryIndex} reference={a.recov} goodWhen="higher"/>}
                info="How early in the night your heart rate settled to its lowest point. Settling early scores higher; alcohol and late food push it later."
                empty="Needs more nights"/>
          <Stat label="Temperature" accent={TEMP} value={today?.temperatureDeviation != null ? `${today.temperatureDeviation > 0 ? '+' : ''}${today.temperatureDeviation.toFixed(2)}` : null}
                unit="°C" sub="vs your own usual"
                info="A deviation from your own baseline, not an absolute temperature. A sustained rise is often the earliest sign of illness."
                empty="Not measured"/>
        </div>
      </div>

      <SectionHead title="Fourteen days" note={a.score != null ? `usually ${Math.round(a.score)}` : undefined}/>
      <div className="hx-chart">
        <ResponsiveContainer width="100%" height={190}>
          <AreaChart data={trend} margin={{ top: 8, right: 8, bottom: 0, left: 0 }}>
            <defs>
              <linearGradient id="hxReady" x1="0" y1="0" x2="0" y2="1">
                <stop offset="0%" stopColor="var(--hx-1)" stopOpacity={0.18}/>
                <stop offset="100%" stopColor="var(--hx-1)" stopOpacity={0}/>
              </linearGradient>
            </defs>
            <CartesianGrid vertical={false} {...GRID}/>
            <XAxis dataKey="day" tick={AX} tickLine={false} axisLine={false} minTickGap={16}/>
            <YAxis width={30} domain={[0, 100]} tick={AX} tickLine={false} axisLine={false}/>
            <Tooltip contentStyle={TT.contentStyle} labelStyle={TT.labelStyle}/>
            {a.score != null && <ReferenceLine y={Math.round(a.score)} stroke="var(--text3)" strokeWidth={1}/>}
            <Area type="monotone" dataKey="score" stroke="var(--hx-1)" strokeWidth={2} fill="url(#hxReady)" dot={false} isAnimationActive={false} connectNulls/>
          </AreaChart>
        </ResponsiveContainer>
      </div>

      <SectionHead title="Day by day"/>
      <div className="hx-grid hx-grid-4">
        {[...data].reverse().map(r => (
          <div key={r.id} className="hx-stat">
            <div className="hx-stat-head">
              <span className="hx-stat-label">{dayLabel(r.day)}</span>
              {r.level && <Chip tone={r.level === 'optimal' ? 'good' : r.level === 'good' ? 'neutral' : 'warn'}>{r.level.replace('_', ' ')}</Chip>}
            </div>
            <div className="hx-stat-value">
              <span className="hx-stat-num" style={{ color: toneFor(r.score) }}>{r.score ?? '—'}</span>
            </div>
            <span className="hx-stat-sub">
              {[r.restingHeartRate ? `${r.restingHeartRate} bpm` : null, r.hrvBalance != null ? `HRV ${r.hrvBalance}` : null]
                .filter(Boolean).join(' · ') || 'No detail recorded'}
            </span>
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

// Latest reading in an oldest-first series — the shape every Vitara list endpoint
// returns (all repository queries OrderBy(day) ascending).
const latest = <T,>(arr?: T[]): T | undefined => arr?.[arr.length - 1];

// Readiness the morning after each kind of session, against the morning after a rest
// day. The server refuses to report thin or negligible effects, so an empty panel here
// means "not enough evidence yet" rather than "no effect" — said out loud, because the
// two are easy to confuse and only one of them is a finding.
function WorkoutImpactPanel() {
  const { data, isPending, isError } = useQuery<ImpactReport>({
    queryKey: ['workout-impact'],
    queryFn: () => get(`${API}/api/workouts/impact?days=90`),
  });

  if (isPending) return <Skel h={120}/>;
  if (isError) return null;   // never block the page on an extra
  if (!data) return null;

  return (
    <>
      <div className="v-section">Recovery Cost<span className="v-section-line"/></div>
      {data.findings.length === 0 ? (
        <div className="v-impact-note">{data.note ?? 'Nothing stands out yet.'}</div>
      ) : (
        <>
          <div className="v-impact-note">
            After a rest day your readiness averages <strong>{data.baseline}</strong> ({data.baselineDays} days).
          </div>
          <div className="v-impact-list">
            {data.findings.map(f => (
              <div key={`${f.dimension}-${f.group}`} className="v-impact-row">
                <span className="v-impact-group">{f.group.replace(/_/g, ' ')}</span>
                <span className="v-impact-dim">{f.dimension}</span>
                <span className={`v-impact-delta ${f.delta < 0 ? 'v-delta--bad' : 'v-delta--good'}`}>
                  {f.delta > 0 ? '+' : ''}{f.delta}
                </span>
                <span className="v-impact-detail">
                  next-day {f.avgAfter} · {f.samples} session{f.samples === 1 ? '' : 's'}
                </span>
              </div>
            ))}
          </div>
        </>
      )}
    </>
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



// ── MANUAL MEASUREMENTS ───────────────────────────────────────────────────────

// Readings a person takes themselves — the medium tier.
//
// Blood pressure, glucose, waist, and the lab values that arrive twice a year. These
// had nowhere to go until now: the health import could parse them and then reported
// them as unstorable, and six of the ten algorithm categories in the health spec are
// blocked on having somewhere to put them.
//
// The context questions are SERVED, not hardcoded here. BaselineKeys decides which
// fields actually split a baseline — position and time of day for blood pressure,
// fasting for glucose — and a form that made that decision separately would eventually
// disagree with the thing computing the baselines.

interface MetricSpec { metric: string; unit: string; tier: string; label: string; context: string[] }

interface Measurement {
  id: string;
  metric: string;
  label: string;
  value: number;
  unit: string;
  at: string;
  source: string;
  note: string | null;
  signature: string;
}

function MeasurePanel() {
  const queryClient = useQueryClient();

  const { data: specs } = useQuery({
    queryKey: ['measurement-metrics'],
    queryFn: async () => {
      const res = await fetch(`${API}/api/measurements/metrics`, { headers: authHeaders() });
      return (await res.json()) as MetricSpec[];
    },
    staleTime: 60 * 60_000,
  });

  const { data: recent } = useQuery({
    queryKey: ['measurements'],
    queryFn: async () => {
      const res = await fetch(`${API}/api/measurements?days=120`, { headers: authHeaders() });
      return (await res.json()) as Measurement[];
    },
  });

  const [metric, setMetric] = useState('systolic_bp');
  const [value, setValue]   = useState('');
  const [note, setNote]     = useState('');
  const [position, setPosition]   = useState('seated');
  const [timeOfDay, setTimeOfDay] = useState('morning');
  const [fasting, setFasting]     = useState(true);
  const [error, setError]   = useState<string | null>(null);

  const spec = specs?.find(s => s.metric === metric);
  const asks = (field: string) => spec?.context.includes(field) ?? false;

  const save = useMutation({
    mutationFn: async () => {
      const body: Record<string, unknown> = { metric, value: Number(value), note: note || null };
      if (asks('position'))  body.position  = position;
      if (asks('timeOfDay')) body.timeOfDay = timeOfDay;
      if (asks('fasting'))   body.fasting   = fasting;

      const res = await fetch(`${API}/api/measurements`, {
        method: 'POST',
        headers: { ...authHeaders(), 'Content-Type': 'application/json' },
        body: JSON.stringify(body),
      });

      const json = await res.json();
      if (!res.ok) throw new Error(json?.error ?? 'Could not save that reading.');
      return json;
    },
    onSuccess: () => {
      setValue('');
      setNote('');
      setError(null);
      queryClient.invalidateQueries({ queryKey: ['measurements'] });
    },
    onError: (e: Error) => setError(e.message),
  });

  const remove = useMutation({
    mutationFn: (id: string) =>
      fetch(`${API}/api/measurements/${id}`, { method: 'DELETE', headers: authHeaders() }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['measurements'] }),
  });

  return (
    <div className="module-section">
      <h2 className="module-h2">Record a reading</h2>
      <p className="module-muted vitara-import-sub">
        Blood pressure, glucose, waist, lab results — anything nothing measures for you.
        These feed the same baselines Oura's data does.
      </p>

      <div className="vitara-measure-form">
        <select value={metric} onChange={e => setMetric(e.target.value)}>
          {specs?.map(s => <option key={s.metric} value={s.metric}>{s.label}</option>)}
        </select>

        <input
          type="number"
          step="any"
          placeholder="Value"
          value={value}
          onChange={e => setValue(e.target.value)}
        />
        <span className="vitara-measure-unit">{spec?.unit}</span>

        {/* Only the context that actually splits this metric's baseline is asked for.
            Asking about arm position for a lab result would be noise, and asking about
            nothing at all would put standing-evening readings in the seated-morning
            baseline. */}
        {asks('position') && (
          <select value={position} onChange={e => setPosition(e.target.value)}>
            <option value="seated">Seated</option>
            <option value="standing">Standing</option>
            <option value="supine">Lying down</option>
          </select>
        )}

        {asks('timeOfDay') && (
          <select value={timeOfDay} onChange={e => setTimeOfDay(e.target.value)}>
            <option value="waking">On waking</option>
            <option value="morning">Morning</option>
            <option value="afternoon">Afternoon</option>
            <option value="evening">Evening</option>
            <option value="night">Night</option>
          </select>
        )}

        {asks('fasting') && (
          <label className="san-checkbox-label">
            <input type="checkbox" checked={fasting} onChange={e => setFasting(e.target.checked)} />
            Fasting
          </label>
        )}

        <input
          placeholder="Note (optional)"
          value={note}
          onChange={e => setNote(e.target.value)}
          style={{ flex: 1, minWidth: '140px' }}
        />

        <button
          className="btn-primary"
          disabled={!value || save.isPending}
          onClick={() => save.mutate()}
        >
          {save.isPending ? 'Saving…' : 'Save'}
        </button>
      </div>

      {error && <p className="module-error">{error}</p>}

      {recent && recent.length > 0 && (
        <div className="insight-table-wrap">
          <table className="vitara-import-table">
            <thead>
              <tr>
                <th>When</th><th>What</th><th className="num">Value</th><th>Conditions</th><th></th>
              </tr>
            </thead>
            <tbody>
              {recent.slice(0, 30).map(m => (
                <tr key={m.id}>
                  <td>{m.at}</td>
                  <td>{m.label}</td>
                  <td className="num">{m.value} {m.unit}</td>
                  {/* The signature is shown because it is the answer to "why is my
                      evening reading not being compared with my morning ones". */}
                  <td className="vitara-measure-sig">
                    {m.signature || (m.source === 'manual' ? '—' : m.source)}
                    {m.note && <span className="vitara-measure-note">{m.note}</span>}
                  </td>
                  <td>
                    <button
                      className="btn-danger-ghost"
                      style={{ fontSize: '0.7rem' }}
                      onClick={() => remove.mutate(m.id)}
                    >
                      Delete
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}


// ── APPLE HEALTH XML IMPORT ───────────────────────────────────────────────────

// Apple's own export.xml, as opposed to a spreadsheet from an export app.
//
// A real one measured 810MB with 1.7 million records from nineteen different apps, so
// this cannot work like the CSV panel. The file is uploaded ONCE and parsed straight
// to daily aggregates; the commit works from those. Sending 810MB over the mesh twice
// to get a preview would be absurd.
//
// THE SOURCE PICKER IS THE WHOLE POINT. Oura writes into Apple Health, and Oura also
// reaches Vitara through its own API with sleep stages, RMSSD and a skin-temperature
// deviation Apple never receives. It is listed with its real counts and left unticked
// rather than hidden, because which copy to keep is a decision, not a rule.

interface XmlSource {
  source: string;
  records: number;
  days: number;
  metrics: string[];
  recommendedOff: boolean;
}

interface XmlScan {
  token: string | null;
  megabytes: number;
  recordsSeen: number;
  recordsMapped: number;
  firstDay: string | null;
  lastDay: string | null;
  days: number;
  heightMetres: number | null;
  sources: XmlSource[];
  ignored: string[];
  warnings: string[];
}

function XmlImportPanel() {
  const [file, setFile]       = useState<File | null>(null);
  const [scan, setScan]       = useState<XmlScan | null>(null);
  const [chosen, setChosen]   = useState<Set<string>>(new Set());
  const [setHeight, setSetHeight] = useState(true);
  const [busy, setBusy]       = useState(false);
  const [done, setDone]       = useState<string | null>(null);
  const [error, setError]     = useState<string | null>(null);

  const upload = async () => {
    if (!file) return;
    setBusy(true); setError(null); setDone(null);

    try {
      const body = new FormData();
      body.append('file', file);
      const res = await fetch(`${API}/api/healthimport/xml`, {
        method: 'POST', headers: authHeaders(), body,
      });
      const json = await res.json();
      if (!res.ok) { setError(json?.error ?? `${res.status}`); return; }

      const s = json as XmlScan;
      setScan(s);
      // Everything except the sources we already have a better copy of.
      setChosen(new Set(s.sources.filter(x => !x.recommendedOff).map(x => x.source)));
    } catch (e) {
      setError(String(e));
    } finally {
      setBusy(false);
    }
  };

  const commit = async () => {
    if (!scan?.token) return;
    setBusy(true); setError(null);

    try {
      const res = await fetch(`${API}/api/healthimport/xml/commit`, {
        method: 'POST',
        headers: { ...authHeaders(), 'Content-Type': 'application/json' },
        body: JSON.stringify({ token: scan.token, sources: [...chosen], setHeight }),
      });
      const json = await res.json();
      if (!res.ok) { setError(json?.error ?? `${res.status}`); return; }

      const written = json.written as Record<string, number>;
      setDone(Object.entries(written).map(([k, v]) => `${v} ${k}`).join(' · ') || 'Nothing written.');
      setScan(null);
    } catch (e) {
      setError(String(e));
    } finally {
      setBusy(false);
    }
  };

  const toggle = (name: string) => {
    const next = new Set(chosen);
    if (next.has(name)) next.delete(name); else next.add(name);
    setChosen(next);
  };

  const selectedRecords = scan?.sources
    .filter(x => chosen.has(x.source))
    .reduce((n, x) => n + x.records, 0) ?? 0;

  return (
    <div className="module-section">
      <h2 className="module-h2">Import Apple Health export</h2>
      <p className="module-muted vitara-import-sub">
        The <code>export.xml</code> from inside your Apple Health export zip — not
        <code>export_cda.xml</code>, which is the same data in a clinical format we do
        not need. Large files are fine; it streams. Nothing is written until you confirm.
      </p>

      <div className="vitara-import-row">
        <input type="file" accept=".xml" onChange={e => { setFile(e.target.files?.[0] ?? null); setScan(null); setDone(null); }} />
        <button className="btn-ghost" disabled={!file || busy} onClick={upload}>
          {busy && !scan ? 'Reading… this takes a minute' : 'Read file'}
        </button>
      </div>

      {error && <p className="module-error">{error}</p>}
      {done && <p className="vitara-import-ok">Saved. {done}</p>}

      {scan && (
        <div className="vitara-import-result">
          <p className="module-muted">
            {scan.megabytes} MB · {scan.recordsSeen.toLocaleString()} records ·{' '}
            {scan.days.toLocaleString()} days
            {scan.firstDay && <> · {scan.firstDay} to {scan.lastDay}</>}
          </p>

          <div>
            <p className="vitara-import-samplehead">Choose what to import</p>
            <ul className="vitara-src-list">
              {scan.sources.map(src => (
                <li key={src.source} className={chosen.has(src.source) ? '' : 'vitara-src-off'}>
                  <label>
                    <input
                      type="checkbox"
                      checked={chosen.has(src.source)}
                      onChange={() => toggle(src.source)}
                    />
                    <span className="vitara-src-name">{src.source}</span>
                    <span className="vitara-src-count">
                      {src.records.toLocaleString()} records
                      {src.days > 0 && <> · {src.days.toLocaleString()} days</>}
                    </span>
                  </label>

                  {/* Said on the row it applies to, not in a footnote. The reason is
                      specific to this source and belongs next to the decision. */}
                  {src.recommendedOff && (
                    <p className="vitara-src-why">
                      Already synced directly from Oura, with sleep stages and HRV that
                      Apple Health never receives. Importing would replace better data
                      with a coarser copy.
                    </p>
                  )}

                  {src.metrics.length > 0 && (
                    <p className="vitara-src-metrics">{src.metrics.map(m => m.replace(/_/g, ' ')).join(' · ')}</p>
                  )}
                </li>
              ))}
            </ul>
          </div>

          {scan.heightMetres && (
            <label className="san-checkbox-label">
              <input type="checkbox" checked={setHeight} onChange={e => setSetHeight(e.target.checked)} />
              Set my height to {(scan.heightMetres * 100).toFixed(0)}cm — needed for BMI and waist-to-height
            </label>
          )}

          {scan.ignored.length > 0 && (
            <p className="vitara-import-ignored">
              <strong>Not imported:</strong> {scan.ignored.join(', ')}
            </p>
          )}

          {scan.warnings.length > 0 && (
            <ul className="vitara-import-warnings">
              {scan.warnings.map((w, i) => <li key={i}>{w}</li>)}
            </ul>
          )}

          <div className="vitara-import-row">
            <button className="btn-primary" disabled={busy || chosen.size === 0} onClick={commit}>
              {busy ? 'Saving…' : `Import ${selectedRecords.toLocaleString()} records`}
            </button>
            <button className="btn-ghost" disabled={busy} onClick={() => setScan(null)}>Cancel</button>
          </div>
        </div>
      )}
    </div>
  );
}

// ── APPLE HEALTH IMPORT ───────────────────────────────────────────────────────

// Uploading an Apple Health export as a spreadsheet.
//
// TWO STEPS, NOT ONE. Apple's own export is XML in a zip, so whatever lands here came
// out of a third-party export app -- and those disagree about column names, date
// formats, units, and whether a row is a day or a single sample, with nothing in the
// file saying which. The server guesses, and a guess about health data belongs in
// front of the user before it reaches the database rather than after.
//
// So: upload reads and reports, and nothing is written until Save is pressed.

interface MappedColumn { column: string; metric: string; rows: number }

interface ImportResult {
  shape: string;
  rowsRead: number;
  firstDay: string | null;
  lastDay: string | null;
  days: number;
  readings: number;
  recognised: MappedColumn[];
  ignored: string[];
  warnings: string[];
  sample: { day: string; metric: string; value: number }[];
  written: Record<string, number> | null;
  committed: boolean;
}

function ImportPanel() {
  const [file, setFile]       = useState<File | null>(null);
  const [result, setResult]   = useState<ImportResult | null>(null);
  const [error, setError]     = useState<string | null>(null);
  const [busy, setBusy]       = useState(false);

  const send = async (endpoint: 'preview' | 'commit') => {
    if (!file) return;
    setBusy(true);
    setError(null);

    try {
      const body = new FormData();
      body.append('file', file);
      const res = await fetch(`${API}/api/healthimport/${endpoint}`, {
        method: 'POST',
        headers: authHeaders(),
        body,
      });

      const json = await res.json();
      if (!res.ok) { setError(json?.error ?? `${res.status} ${res.statusText}`); setResult(null); }
      else setResult(json as ImportResult);
    } catch (e) {
      setError(String(e));
    } finally {
      setBusy(false);
    }
  };

  const pick = (f: File | null) => { setFile(f); setResult(null); setError(null); };

  return (
    <div className="module-section">
      <h2 className="module-h2">Import Apple Health</h2>
      <p className="module-muted vitara-import-sub">
        A .csv, .tsv or .xlsx export from an Apple Health export app. Both common
        layouts work — one row per day with a column per metric, or one row per sample.
        Nothing is saved until you confirm.
      </p>

      <div className="vitara-import-row">
        <input
          type="file"
          accept=".csv,.tsv,.txt,.xlsx,.xlsm"
          onChange={e => pick(e.target.files?.[0] ?? null)}
        />
        <button className="btn-ghost" disabled={!file || busy} onClick={() => send('preview')}>
          {busy ? 'Reading…' : 'Read file'}
        </button>
        {result && !result.committed && result.readings > 0 && (
          <button className="btn-primary" disabled={busy} onClick={() => send('commit')}>
            Save {result.readings} readings
          </button>
        )}
      </div>

      {error && <p className="module-error">{error}</p>}

      {result && (
        <div className="vitara-import-result">
          {result.committed ? (
            <p className="vitara-import-ok">
              Saved. {Object.entries(result.written ?? {})
                .map(([k, v]) => (v > 0 ? `${v} ${k}` : k))
                .join(' · ') || 'Nothing was stored.'}
            </p>
          ) : (
            <p className="module-muted">
              {result.readings > 0
                ? `Read ${result.readings} readings across ${result.days} day(s)` +
                  (result.firstDay ? `, ${result.firstDay} to ${result.lastDay}.` : '.')
                : 'Nothing usable was found in that file.'}
            </p>
          )}

          {result.recognised.length > 0 && (
            <table className="vitara-import-table">
              <thead>
                <tr><th>Column</th><th>Read as</th><th className="num">Values</th></tr>
              </thead>
              <tbody>
                {result.recognised.map(m => (
                  <tr key={m.column + m.metric} className={m.rows === 0 ? 'vitara-import-dead' : ''}>
                    <td>{m.column}</td>
                    <td>{m.metric.replace(/_/g, ' ')}</td>
                    <td className="num">{m.rows}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}

          {/* Warnings are shown above the ignored list, because a recognised column
              that produced nothing is a real problem while an unknown column usually
              is not. */}
          {result.warnings.length > 0 && (
            <ul className="vitara-import-warnings">
              {result.warnings.map((w, i) => <li key={i}>{w}</li>)}
            </ul>
          )}

          {result.ignored.length > 0 && (
            <p className="vitara-import-ignored">
              <strong>Not imported:</strong> {result.ignored.join(', ')}
            </p>
          )}

          {!result.committed && result.sample.length > 0 && (
            <>
              <p className="vitara-import-samplehead">First few rows as read:</p>
              <table className="vitara-import-table">
                <thead><tr><th>Day</th><th>Metric</th><th className="num">Value</th></tr></thead>
                <tbody>
                  {result.sample.map((r, i) => (
                    <tr key={i}>
                      <td>{r.day}</td>
                      <td>{r.metric.replace(/_/g, ' ')}</td>
                      <td className="num">{r.value}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </>
          )}
        </div>
      )}
    </div>
  );
}

// One panel throwing must not blank the tab.
//
// A dashboard payload missing a block took the whole module down to a white screen: no
// message, nothing to retry, indistinguishable from the app being broken. React needs a
// class for this; it is the only one in the codebase and it earns its place.
class PanelBoundary extends Component<{ name: string; children: ReactNode }, { error: Error | null }> {
  state: { error: Error | null } = { error: null };

  static getDerivedStateFromError(error: Error) { return { error }; }

  render() {
    if (!this.state.error) return this.props.children;
    return (
      <div className="v-panel-error">
        <p><b>{this.props.name} couldn't be drawn.</b> The rest of the tab still works, and nothing was lost — this is a display problem, not missing data.</p>
        <p className="v-panel-error-detail">{this.state.error.message}</p>
        <button className="btn-ghost" onClick={() => this.setState({ error: null })}>Try again</button>
      </div>
    );
  }
}

// ── ROOT ──────────────────────────────────────────────────────────────────────

type Page = 'today' | 'all' | 'sleep' | 'body' | 'activity' | 'readiness' | 'protocols' | 'nutrition' | 'import' | 'measure';

const PAGES: { id: Page; label: string }[] = [
  { id: 'today',     label: 'Today' },
  { id: 'all',       label: 'Everything we track' },
  { id: 'sleep',     label: 'Sleep' },
  { id: 'readiness', label: 'Recovery' },
  { id: 'activity',  label: 'Activity' },
  { id: 'body',      label: 'Body' },
  { id: 'nutrition', label: 'Food' },
  { id: 'protocols', label: 'Protocols' },
  { id: 'measure',   label: 'Record a reading' },
  { id: 'import',    label: 'Import' },
];

function VitaraInner() {
  const [page, setPage] = useState<Page>('today');
  const { data: status, isPending, isError } = useQuery<OuraStatus>({ queryKey: ['oura-status'], queryFn: () => get(`${API}/api/oura/status`) });

  const heart = (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.75" strokeLinecap="round" strokeLinejoin="round">
      <path d="M20.84 4.61a5.5 5.5 0 00-7.78 0L12 5.67l-1.06-1.06a5.5 5.5 0 00-7.78 7.78l1.06 1.06L12 21.23l7.78-7.78 1.06-1.06a5.5 5.5 0 000-7.78z"/>
    </svg>
  );

  const linkState = !status ? null : !status.linked
    ? <span className="hx-pill"><span className="hx-dot bad"/>Ring not connected</span>
    : status.expired
      ? <span className="hx-pill"><span className="hx-dot warn"/>Reconnect needed</span>
      : <span className="hx-pill"><span className="hx-dot"/>Synced <b>{relTime(status.lastSyncedAt)}</b></span>;

  return (
    <HxShell
      title="Vitara"
      subtitle="Your health, measured — and compared only with you"
      icon={heart}
      right={linkState}
      tabs={status && !isError ? <HxTabs tabs={PAGES} active={page} onPick={setPage}/> : undefined}
    >
      {isPending && <div className="hx-empty">Connecting…</div>}
      {!isPending && isError && <BackendDown/>}
      {/* Import is shown whether or not Oura is linked. A manual upload is the
          fallback for having no ring connected, so gating it behind a working
          connection would hide it in the one case it exists for. */}
      {!isPending && !isError && status && (
        <>
          {/* Shown above the pages rather than instead of them. Without a ring the old
              screen was a dead end: no way to see what the system tracks, and the
              manual and import routes -- the two things that work with no ring at all
              -- were the only things on the page. */}
          {!status.linked && <NotLinked/>}
          {status.linked && status.expired && <OuraExpiredBanner/>}

          {/* Keyed by page: without it a caught error stays caught, and every other tab
              shows the failure of the one that actually broke. */}
          <PanelBoundary key={page} name={PAGES.find(p => p.id === page)?.label ?? 'This page'}>
            {page === 'today'     && <TodayPage status={status}/>}
            {page === 'all'       && <VitaraMetricsCatalogue/>}
            {page === 'sleep'     && <SleepPage/>}
            {page === 'body'      && <BodyPage/>}
            {page === 'activity'  && <ActivityPage/>}
            {page === 'readiness' && <ReadinessPage/>}
            {page === 'nutrition' && <NutritionPage/>}
            {page === 'protocols' && <ProtocolsPage/>}
            {page === 'measure'   && <MeasurePanel/>}
            {page === 'import'    && <><XmlImportPanel/><ImportPanel/></>}
          </PanelBoundary>
        </>
      )}
    </HxShell>
  );
}

export default function VitaraModule() {
  return <QueryClientProvider client={qc}><VitaraInner/></QueryClientProvider>;
}
