import type { ReactNode } from 'react';
import { useQuery } from '@tanstack/react-query';
import { moduleApi } from '../services/apiHost';
import { vitaraHeaders, useProfileId } from '../services/profile';
import { formatClock, formatDay } from '../services/timezone';
import type { Sleep } from '../pages/VitaraModule';
import { Rings } from './Rings';
import { OutOfReach } from './Parts';
import { clamp, ease, useReveal } from './motion';
import { avg, bedtimeScale, clockFromScale, fmtMin, oneNightPerDay, quantile } from './stats';

// Sleep, the phone screen: how last night went, what it was made of, and whether it was unusual
// for YOU. Everything is compared with the person's own earlier nights; there is no "ideal".

const VITARA = moduleApi(5100);
const get = <T,>(url: string): Promise<T> =>
  fetch(url, { headers: vitaraHeaders() }).then(r => {
    if (!r.ok) throw new Error(String(r.status));
    return r.json() as Promise<T>;
  });

export function MobileSleep({ tick, header, detail }: {
  tick: number;
  header: (title: string, eyebrow: string) => ReactNode;
  detail: () => void;
}) {
  const person = useProfileId();
  const t = useReveal(`${person}:${tick}`);
  const k = ease(clamp(t / 0.85));

  const q = useQuery<Sleep[]>({ queryKey: ['sleep', 14], queryFn: () => get(`${VITARA}/api/sleep?days=14`) });
  const nights = oneNightPerDay(q.data ?? []);
  const last = nights[nights.length - 1];
  const before = nights.slice(0, -1);

  if (q.isPending) return <>{header('Sleep', '')}<div className="vm-skel" aria-busy="true" /></>;
  if (q.isError && !q.data) return <>{header('Sleep', '')}<OutOfReach onRetry={() => { void q.refetch(); }} /></>;

  const eyebrow = last ? `Last night · ${formatClock(last.bedtimeStart)} – ${formatClock(last.bedtimeEnd)}` : 'No nights yet';
  if (!last) {
    return (
      <div className="vm-screen">
        {header('Sleep', eyebrow)}
        <div className="vm-hero-row">
          <Rings items={[{ key: 's', label: 'Sleep', value: null, hue: 'sleep' }]} size={148} stroke={14} gap={0}
                 center={<><span className="vm-eyebrow-s">Score</span><b className="vm-ring-num">—</b></>} />
          <div className="vm-hero-text"><b>No nights yet</b><p>Your first night appears here tomorrow morning.</p></div>
        </div>
        <div className="vm-tile vm-dashed">
          <b className="vm-card-title">Stages, trends and consistency</b>
          <p className="vm-fine">Wear your sensor tonight. Stages appear after one night; your 14-night trend and bedtime consistency fill in as the week goes on.</p>
        </div>
      </div>
    );
  }

  const usualMin = avg(before.map(n => n.totalSleepMinutes));
  const diff = usualMin != null ? Math.round(last.totalSleepMinutes - usualMin) : null;
  const score = last.score ?? null;

  return (
    <div className="vm-screen">
      {header('Sleep', eyebrow)}

      <div className="vm-hero-row">
        <Rings items={[{ key: 's', label: 'Sleep score', value: score == null ? null : score / 100, hue: 'sleep' }]}
               size={148} stroke={14} gap={0} t={t}
               center={<><span className="vm-eyebrow-s">Score</span><b className="vm-ring-num">{score == null ? '—' : Math.round(score * k)}</b></>} />
        <div className="vm-hero-text">
          <b className="vm-big">{fmtMin(last.totalSleepMinutes * k)}</b>
          <p>asleep{usualMin != null ? ` · usual ${fmtMin(usualMin)}` : ''}</p>
          {diff != null && (
            <p className="vm-sleep-diff">
              {Math.abs(diff) <= 5 ? '≈ about your usual' : diff > 0 ? `↑ ${diff} min more than usual` : `↓ ${-diff} min less than usual`}
            </p>
          )}
          <p>{Math.round(last.efficiency * 100)}% efficiency</p>
        </div>
      </div>

      <Stages last={last} before={before} t={t} />
      <Trend nights={nights} t={t} />
      <Consistency nights={nights} />

      <button type="button" className="vm-row-link" onClick={detail}>
        <span>Full sleep detail</span><em aria-hidden="true">›</em>
      </button>
    </div>
  );
}

// ── Stages ────────────────────────────────────────────────────────────────────

function Stages({ last, before, t }: { last: Sleep; before: Sleep[]; t: number }) {
  const rows = [
    { name: 'Deep', c: 'var(--deep)', min: last.deepMinutes, usual: avg(before.map(n => n.deepMinutes)) },
    { name: 'REM', c: 'var(--rem)', min: last.remMinutes, usual: avg(before.map(n => n.remMinutes)) },
    { name: 'Light', c: 'var(--lt)', min: last.lightMinutes, usual: avg(before.map(n => n.lightMinutes)) },
    { name: 'Awake', c: 'var(--awake)', min: last.awakeMinutes, usual: avg(before.map(n => n.awakeMinutes)) },
  ];
  const scale = Math.max(...rows.map(r => Math.max(r.min, r.usual ?? 0))) * 1.08 || 1;
  const grow = ease(clamp((t - 0.1) / 0.8));
  return (
    <section className="vm-tile">
      <p className="vm-label"><span>Stages</span><em>vs your usual</em></p>
      <div className="vm-strip" aria-hidden="true" style={{ clipPath: `inset(0 ${((1 - grow) * 100).toFixed(1)}% 0 0)` }}>
        {rows.map(r => <span key={r.name} style={{ flex: `${Math.max(r.min, 1)} 0 0`, background: r.c }} />)}
      </div>
      <div className="vm-stage-rows">
        {rows.map(r => (
          <div key={r.name} className="vm-stage-row">
            <span className="vm-stage-name"><i style={{ background: r.c }} aria-hidden="true" />{r.name}</span>
            <div className="vm-stage-bar">
              <div className="vm-meter">
                <i style={{ width: `${(r.min / scale * 100 * grow).toFixed(1)}%`, background: r.c }} />
                {r.usual != null && <u style={{ left: `${(r.usual / scale * 100).toFixed(1)}%` }} />}
              </div>
              <small>{r.usual != null ? `usual ${fmtMin(r.usual)}` : 'no usual yet'}</small>
            </div>
            <b>{fmtMin(r.min)}</b>
          </div>
        ))}
      </div>
    </section>
  );
}

// ── Fourteen nights ───────────────────────────────────────────────────────────

function Trend({ nights, t }: { nights: Sleep[]; t: number }) {
  const hours = nights.map(n => n.totalSleepMinutes / 60);
  const prior = hours.slice(0, -1);
  const lo = Math.max(0, Math.min(...hours) - 1), hi = Math.max(...hours) + 0.4;
  const H = 130;
  const y = (h: number) => ((h - lo) / (hi - lo)) * H;
  const q1 = quantile(prior, 0.25), q3 = quantile(prior, 0.75);
  const rise = ease(clamp((t - 0.1) / 0.8));
  return (
    <section className="vm-tile">
      <p className="vm-label"><span>{nights.length} nights</span><em>time asleep</em></p>
      <div className="vm-nights-chart" style={{ height: H }}>
        {q1 != null && q3 != null && prior.length >= 3 && (
          <>
            <div className="vm-band" style={{ bottom: y(q1), height: Math.max(4, y(q3) - y(q1)), background: 'var(--sleep)' }} />
          </>
        )}
        <div className="vm-bars">
          {hours.map((h, i) => {
            const isLast = i === hours.length - 1;
            return (
              <div key={nights[i].day} className={`vm-bar ${isLast ? 'is-last' : ''}`} style={{ height: Math.max(3, y(h) * rise) }}>
                {isLast && <span>{fmtMin(h * 60)}</span>}
              </div>
            );
          })}
        </div>
      </div>
      <div className="vm-axis">
        <span>{formatDay(nights[0].day, { day: 'numeric', month: 'short' })}</span>
        {q1 != null && prior.length >= 3 && <span className="vm-key"><i style={{ background: 'var(--sleep)' }} />your usual</span>}
        <span>Last night</span>
      </div>
    </section>
  );
}

// ── Bedtime consistency ───────────────────────────────────────────────────────

function Consistency({ nights }: { nights: Sleep[] }) {
  const pts = nights.map(n => ({ day: n.day, v: bedtimeScale(n.bedtimeStart) })).filter((p): p is { day: string; v: number } => p.v != null);
  if (pts.length < 4) return null;
  const typical = quantile(pts.map(p => p.v), 0.5) as number;
  const within = pts.filter(p => Math.abs(p.v - typical) <= 30).length;
  const steady = within / pts.length >= 0.7;
  const lo = Math.min(...pts.map(p => p.v), typical - 60) - 10, hi = Math.max(...pts.map(p => p.v), typical + 60) + 10;
  const H = 110;
  const y = (v: number) => ((v - lo) / (hi - lo)) * H;
  const lastPt = pts[pts.length - 1];
  const snap = (v: number) => Math.round(v / 5) * 5;
  const ticks = [snap(typical - 60), typical, snap(typical + 60)];

  return (
    <section className="vm-tile">
      <p className="vm-label">
        <span>Bedtime consistency</span>
        <span className="vm-state" style={{ color: steady ? 'var(--teal)' : 'var(--gold)' }}>{steady ? '● Steady' : '◆ Varied'}</span>
      </p>
      <div className="vm-dots-chart" style={{ height: H }}>
        <div className="vm-dots-axis">{ticks.map(v => <span key={v} style={{ top: y(v) - 8 }}>{clockFromScale(v).replace(':00 ', ' ')}</span>)}</div>
        <div className="vm-dots-plot">
          <div className="vm-band" style={{ top: y(typical - 30), height: y(typical + 30) - y(typical - 30), background: 'var(--teal)' }} />
          {pts.map((p, i) => {
            const isLast = i === pts.length - 1;
            const inside = Math.abs(p.v - typical) <= 30;
            return (
              <i key={p.day} className={`vm-dot ${isLast ? 'is-last' : inside ? 'in' : 'out'}`}
                 style={{ left: `${2 + (i / (pts.length - 1)) * 94}%`, top: y(p.v) }} />
            );
          })}
        </div>
      </div>
      <p className="vm-fine">
        {within} of {pts.length} nights within 30 min of your usual {clockFromScale(typical)}. Last night: {clockFromScale(lastPt.v)}.
      </p>
    </section>
  );
}
