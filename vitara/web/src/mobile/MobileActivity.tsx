import type { ReactNode } from 'react';
import { useQuery } from '@tanstack/react-query';
import { moduleApi } from '../services/apiHost';
import { vitaraHeaders, useProfileId } from '../services/profile';
import { formatClock, formatDay } from '../services/timezone';
import type { Dashboard, Activity } from '../pages/VitaraModule';
import { Rings } from './Rings';
import { OutOfReach } from './Parts';
import { clamp, ease, useReveal } from './motion';
import { avg, fmtMin } from './stats';

// Activity, the phone screen. There is no universal target to close a ring against, so each ring
// closes on YOUR usual: when you have done as much as you normally do by now, the ring is full,
// and anything beyond that is said in words rather than drawn past 100%.

const VITARA = moduleApi(5100);
const get = <T,>(url: string): Promise<T> =>
  fetch(url, { headers: vitaraHeaders() }).then(r => {
    if (!r.ok) throw new Error(String(r.status));
    return r.json() as Promise<T>;
  });

interface Workout { id: string; day: string; activity: string; startTime?: string; endTime?: string; calories?: number; distance?: number; intensity?: string; label?: string | null }

const cap = (s: string) => s.charAt(0).toUpperCase() + s.slice(1);

export function MobileActivity({ tick, header, detail }: {
  tick: number;
  header: (title: string, eyebrow: string) => ReactNode;
  detail: () => void;
}) {
  const person = useProfileId();
  const t = useReveal(`${person}:${tick}`);
  const k = ease(clamp(t / 0.85));

  const dash = useQuery<Dashboard>({ queryKey: ['dashboard'], queryFn: () => get(`${VITARA}/api/dashboard`), refetchInterval: 60_000 });
  const actQ = useQuery<Activity[]>({ queryKey: ['activity', 14], queryFn: () => get(`${VITARA}/api/activity?days=14`) });
  const woQ = useQuery<Workout[]>({ queryKey: ['workouts'], queryFn: () => get(`${VITARA}/api/workouts?days=30`) });

  if (dash.isPending) return <>{header('Activity', '')}<div className="vm-skel" aria-busy="true" /></>;
  if (dash.isError && !dash.data) return <>{header('Activity', '')}<OutOfReach onRetry={() => { void dash.refetch(); }} /></>;

  const days = [...(actQ.data ?? [])].sort((a, b) => a.day.localeCompare(b.day));
  const today = days[days.length - 1];
  const before = days.slice(0, -1);
  const eyebrow = 'Today · vs your usual';

  if (!today) {
    return (
      <div className="vm-screen">
        {header('Activity', eyebrow)}
        <div className="vm-tile vm-dashed">
          <b className="vm-card-title">Nothing yet today</b>
          <p className="vm-fine">Steps and activity arrive from your sensor or your phone. Nothing is shown as zero until there is something to count.</p>
        </div>
      </div>
    );
  }

  const mins = (a: Activity) => a.mediumActivityMinutes + a.highActivityMinutes;
  const rows = [
    { key: 'e', label: 'Energy', unit: 'kcal', hue: 'activity' as const, color: 'var(--gold)', now: today.activeCalories, usual: avg(before.map(a => a.activeCalories)) },
    { key: 'm', label: 'Active minutes', unit: 'min', hue: 'recovery' as const, color: 'var(--teal)', now: mins(today), usual: avg(before.map(mins)) },
    { key: 's', label: 'Steps', unit: '', hue: 'blue' as const, color: 'var(--blue)', now: today.steps, usual: dash.data?.weeklyAvg?.steps ?? avg(before.map(a => a.steps)) },
  ];
  const fmt = (n: number) => Math.round(n).toLocaleString('en-US');

  const stepsUsual = rows[2].usual;
  const stepsMax = Math.max(...days.map(a => a.steps), stepsUsual ?? 0) * 1.08 || 1;
  const rise = ease(clamp((t - 0.1) / 0.8));
  const workouts = [...(woQ.data ?? [])].sort((a, b) => (b.startTime ?? b.day).localeCompare(a.startTime ?? a.day)).slice(0, 5);
  const moderate = today.mediumActivityMinutes, hard = today.highActivityMinutes;

  return (
    <div className="vm-screen">
      {header('Activity', eyebrow)}

      <div className="vm-hero-row">
        <Rings items={rows.map(r => ({ key: r.key, label: r.label, value: r.usual ? r.now / r.usual : null, hue: r.hue }))}
               size={156} stroke={13} gap={5} t={t} />
        <div className="vm-act-legend">
          {rows.map(r => (
            <div key={r.key}>
              <span><i style={{ background: r.color }} aria-hidden="true" />{r.label}</span>
              <b>{fmt(r.now * k)}<small>{r.usual != null ? ` / ${fmt(r.usual)} usual` : ''}{r.unit && r.usual == null ? ` ${r.unit}` : ''}</small></b>
            </div>
          ))}
        </div>
      </div>

      <section className="vm-tile">
        <p className="vm-label"><span>Steps</span><em>{days.length} days</em></p>
        <div className="vm-chart-head">
          <b>{fmt(today.steps * k)}</b>
          {stepsUsual != null && Math.abs(today.steps - stepsUsual) > stepsUsual * 0.04 && (
            <span className="vm-chart-vs" style={{ color: 'var(--gold)' }}>
              {today.steps > stepsUsual ? '↑' : '↓'} {fmt(Math.abs(today.steps - stepsUsual))} {today.steps > stepsUsual ? 'more' : 'fewer'} than usual
            </span>
          )}
        </div>
        <div className="vm-nights-chart" style={{ height: 80 }}>
          {stepsUsual != null && <u className="vm-usual-line" style={{ bottom: (stepsUsual / stepsMax) * 80 }} />}
          <div className="vm-bars">
            {days.map((a, i) => (
              <div key={a.day} className="vm-bar vm-bar-gold" style={{ height: Math.max(2, (a.steps / stepsMax) * 80 * rise), opacity: i === days.length - 1 ? 1 : 0.7 }} />
            ))}
          </div>
        </div>
        <div className="vm-axis"><span>{formatDay(days[0].day, { day: 'numeric', month: 'short' })}</span><span>Today</span></div>
      </section>

      {(moderate + hard > 0 || today.lowActivityMinutes > 0) && (
        <section className="vm-tile">
          <p className="vm-label"><span>Active minutes</span><em>{moderate + hard} today</em></p>
          <div className="vm-split">
            <i style={{ flex: Math.max(moderate, 0.001), background: 'var(--teal)', opacity: 0.6 }} />
            <i style={{ flex: Math.max(hard, 0.001), background: 'var(--teal)' }} />
          </div>
          <div className="vm-split-key">
            <span>Moderate · <b>{moderate}</b></span>
            <span>Hard · <b>{hard}</b></span>
            <span>Light · <b>{fmtMin(today.lowActivityMinutes)}</b></span>
          </div>
        </section>
      )}

      {workouts.length > 0 && (
        <section>
          <p className="vm-label"><span>Workouts</span><em>recent</em></p>
          <div className="vm-list">
            {workouts.map(w => (
              <div key={w.id} className="vm-list-row">
                <span className="vm-list-ico"><i className={`shape-${w.intensity === 'hard' ? 'diamond' : w.intensity === 'moderate' ? 'square' : 'circle'}`} /></span>
                <div>
                  <b>{w.label || cap(w.activity)}</b>
                  <small>
                    {[
                      w.distance ? `${(w.distance / 1000).toFixed(1)} km` : null,
                      w.startTime && w.endTime ? `${Math.max(1, Math.round((Date.parse(w.endTime) - Date.parse(w.startTime)) / 60_000))} min` : null,
                      w.calories ? `${Math.round(w.calories)} kcal` : null,
                      w.intensity ?? null,
                    ].filter(Boolean).join(' · ')}
                  </small>
                </div>
                <span>{w.startTime ? formatClock(w.startTime) : formatDay(w.day, { month: 'short', day: 'numeric' })}<small>{formatDay(w.day, { month: 'short', day: 'numeric' })}</small></span>
              </div>
            ))}
          </div>
        </section>
      )}

      <button type="button" className="vm-row-link" onClick={detail}>
        <span>Full activity detail</span><em aria-hidden="true">›</em>
      </button>
    </div>
  );
}
