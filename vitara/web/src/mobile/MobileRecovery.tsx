import type { ReactNode } from 'react';
import { useQuery } from '@tanstack/react-query';
import { moduleApi } from '../services/apiHost';
import { vitaraHeaders, useProfileId, profileQuery } from '../services/profile';
import { formatDay, weekdayOf } from '../services/timezone';
import type { Dashboard, Sleep, Readiness, Activity } from '../pages/VitaraModule';
import { BandChart, OutOfReach } from './Parts';
import { clamp, ease, useReveal } from './motion';
import { avg, oneNightPerDay, quantile } from './stats';

// Recovery, the phone screen: how recovered you are today as weather, the two overnight heart
// measures against your own band, and how the week's activity sat beside how recovered you were.
//
// "Weather" is only a scale for the readiness score (stormy under 40, clear from 80): it carries
// no more information than the number, it just says it in a way that is read at a glance.

const VITARA = moduleApi(5100);
const get = <T,>(url: string): Promise<T> =>
  fetch(url, { headers: vitaraHeaders() }).then(r => {
    if (!r.ok) throw new Error(String(r.status));
    return r.json() as Promise<T>;
  });

const WEATHER = [
  { word: 'Stormy', range: '< 40', from: 0 },
  { word: 'Overcast', range: '40–59', from: 40 },
  { word: 'Fair', range: '60–79', from: 60 },
  { word: 'Clear', range: '80+', from: 80 },
];
const weatherIndex = (score: number) => (score >= 80 ? 3 : score >= 60 ? 2 : score >= 40 ? 1 : 0);
const SUN = [0, 0.18, 0.38, 0.6];

export function MobileRecovery({ tick, header, detail }: {
  tick: number;
  header: (title: string, eyebrow: string) => ReactNode;
  detail: () => void;
}) {
  const person = useProfileId();
  const t = useReveal(`${person}:${tick}`);
  const k = ease(clamp(t / 0.85));

  const dash = useQuery<Dashboard>({ queryKey: ['dashboard'], queryFn: () => get(`${VITARA}/api/dashboard`), refetchInterval: 60_000 });
  const sleepQ = useQuery<Sleep[]>({ queryKey: ['sleep', 30], queryFn: () => get(`${VITARA}/api/sleep?days=30`) });
  const readyQ = useQuery<Readiness[]>({ queryKey: ['readiness', 30], queryFn: () => get(`${VITARA}/api/readiness?days=30`) });
  const actQ = useQuery<Activity[]>({ queryKey: ['activity', 14], queryFn: () => get(`${VITARA}/api/activity?days=14`) });

  const eyebrow = formatDay(dash.data?.date, { weekday: 'long', month: 'long', day: 'numeric' });
  if (dash.isPending) return <>{header('Recovery', '')}<div className="vm-skel" aria-busy="true" /></>;
  if (dash.isError && !dash.data) return <>{header('Recovery', '')}<OutOfReach onRetry={() => { void dash.refetch(); }} /></>;

  const score = dash.data?.readiness?.score ?? null;
  const ready = [...(readyQ.data ?? [])].sort((a, b) => a.day.localeCompare(b.day));
  const nights = oneNightPerDay(sleepQ.data ?? []);

  // Nothing overnight to read: say what is missing and how to get it, rather than a zero.
  if (score == null && ready.every(r => r.restingHeartRate == null) && nights.every(n => n.avgHrv == null)) {
    return (
      <div className="vm-screen">
        {header('Recovery', eyebrow)}
        <div className="vm-missing">
          <svg width="168" height="168" viewBox="0 0 168 168" aria-hidden="true">
            <circle cx="84" cy="84" r="72" fill="none" strokeWidth="3" strokeLinecap="round" style={{ stroke: 'var(--line2)', strokeDasharray: '2 9' }} />
          </svg>
          <b>No sensor linked</b>
          <p>Recovery needs overnight heart data from a ring, band or watch.</p>
          <a className="vm-cta" href={`${VITARA}/api/oura/auth${profileQuery()}`} target="_blank" rel="noreferrer">Link a sensor</a>
        </div>
      </div>
    );
  }

  const w = score == null ? null : weatherIndex(score);
  const level = dash.data?.readiness?.level;

  const hrv = nights.map(n => n.avgHrv ?? null);
  const rhr = ready.map(r => r.restingHeartRate ?? null);
  const charts = [
    chartSpec('Heart rate variability', 'ms', hrv, nights[0]?.day, dash.data?.weeklyAvg?.hrv, ' above your usual', ' below your usual'),
    chartSpec('Resting heart rate', 'bpm', rhr, ready[0]?.day, dash.data?.weeklyAvg?.rhr, ' higher than your usual', ' calmer than your usual'),
  ];

  // The last seven days, activity score beside readiness. Both are the ring's own scores.
  const act = [...(actQ.data ?? [])].sort((a, b) => a.day.localeCompare(b.day));
  const byDay = new Map(ready.map(r => [r.day, r.score ?? null]));
  const week = act.slice(-7).map(a => ({ day: a.day, load: a.score ?? null, rec: byDay.get(a.day) ?? null }));

  return (
    <div className="vm-screen">
      {header('Recovery', eyebrow)}

      {w != null && (
        <section className="vm-weather">
          <i className="vm-sun" style={{ opacity: SUN[w] * k }} aria-hidden="true" />
          <div className="vm-weather-main">
            <span className="vm-label-s">Body weather</span>
            <b>{WEATHER[w].word}</b>
            <p>Readiness {score}{level ? ` · ${level}` : ''}.</p>
          </div>
          <div className="vm-weather-scale">
            {WEATHER.map((x, i) => (
              <div key={x.word}>
                <i style={{ background: i === w ? 'var(--gold)' : 'var(--line2)' }} />
                <span style={{ color: i === w ? 'var(--t1)' : 'var(--t2)', fontWeight: i === w ? 700 : 500 }}>{x.word}</span>
                <small>{x.range}</small>
              </div>
            ))}
          </div>
        </section>
      )}

      {charts.map(c => (
        <section key={c.title} className="vm-tile">
          <p className="vm-label"><span>{c.title}</span><em>last 30 days</em></p>
          <div className="vm-chart-head">
            <b>{c.value == null ? '—' : Math.round(c.value * k)}</b><small>{c.unit}</small>
            {c.vs && <span className="vm-chart-vs">{c.vs}</span>}
          </div>
          <BandChart data={c.data} band={c.band} color="var(--teal)" t={t} />
          <div className="vm-axis">
            <span>{c.first}</span>
            {c.band && <span className="vm-key"><i style={{ background: 'var(--teal)' }} />your usual {Math.round(c.band[0])}–{Math.round(c.band[1])}</span>}
            <span>Today</span>
          </div>
        </section>
      ))}

      {week.length >= 3 && (
        <section className="vm-tile">
          <p className="vm-label"><span>Activity and readiness</span><em>last {week.length} days</em></p>
          <div className="vm-pairs">
            {week.map((d, i) => (
              <div key={d.day}>
                <div className="vm-pair-bars">
                  <i style={{ height: `${(clamp((d.load ?? 0) / 100) * 64 * k).toFixed(1)}px`, background: 'var(--gold)', opacity: d.load == null ? 0.15 : 1 }} />
                  <i style={{ height: `${(clamp((d.rec ?? 0) / 100) * 64 * k).toFixed(1)}px`, background: 'var(--teal)', opacity: d.rec == null ? 0.15 : 1 }} />
                </div>
                <span style={{ color: i === week.length - 1 ? 'var(--t1)' : 'var(--t2)' }}>{'SMTWTFS'[weekdayOf(d.day)]}</span>
              </div>
            ))}
          </div>
          <div className="vm-legend-line">
            <span><i style={{ background: 'var(--gold)' }} />Activity score (left bar)</span>
            <span><i style={{ background: 'var(--teal)' }} />Readiness (right bar)</span>
          </div>
        </section>
      )}

      <button type="button" className="vm-row-link" onClick={detail}>
        <span>Full recovery detail</span><em aria-hidden="true">›</em>
      </button>
    </div>
  );
}

// One measure over the last month: today's value, how it sits against the middle half of the
// previous days, and the line itself.
function chartSpec(title: string, unit: string, data: (number | null)[], firstDay: string | undefined, serverUsual: number | undefined, upWords: string, downWords: string) {
  const present = data.filter((v): v is number => v != null);
  const value = present.length ? present[present.length - 1] : null;
  const prior = present.slice(0, -1);
  const lo = quantile(prior, 0.25), hi = quantile(prior, 0.75);
  // The same "usual" the home screen uses when the server has one, so the two screens agree.
  const mean = serverUsual ?? avg(prior);
  let vs: string | null = null;
  if (value != null && mean != null) {
    const diff = Math.round(value - mean);
    vs = diff === 0 ? '≈ about your usual' : `${diff > 0 ? '↑' : '↓'} ${Math.abs(diff)}${diff > 0 ? upWords : downWords}`;
  }
  return {
    title, unit, data, value, vs,
    band: lo != null && hi != null && prior.length >= 5 ? ([lo, hi] as [number, number]) : null,
    days: data.length,
    // Direction is stated in words, never coloured as good or bad: below your usual is information.
    first: firstDay ? formatDay(firstDay, { day: 'numeric', month: 'short' }) : '',
  };
}
