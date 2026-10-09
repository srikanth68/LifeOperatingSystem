import { todayInTz } from '../services/timezone';
import { clamp, ease } from './motion';

// Small pieces the phone screens share.

const f1 = (n: number) => n.toFixed(1);

// A 14-day line with the person's own usual drawn through it as a dashed rule, and a dot on
// today. The usual never moves; only today does. With fewer than two readings there is no shape
// to show, so it says so instead of drawing a line through nothing.
export function UsualSpark({ data, usual, color, t = 1, width = 224, height = 46 }: {
  data: (number | null)[];
  usual?: number | null;
  color: string;
  t?: number;
  width?: number;
  height?: number;
}) {
  const pts = data.map((v, i) => ({ v, i })).filter((p): p is { v: number; i: number } => p.v != null);
  if (pts.length < 2) return <div className="vm-spark-none" style={{ height }}>Not enough days yet</div>;

  const vals = pts.map(p => p.v).concat(usual != null ? [usual] : []);
  let lo = Math.min(...vals), hi = Math.max(...vals);
  if (hi - lo < 1e-9) { lo -= 1; hi += 1; }
  const pad = (hi - lo) * 0.08;
  lo -= pad; hi += pad;

  const n = data.length;
  const X = (i: number) => 3 + (i / (n - 1)) * (width - 6);
  const Y = (v: number) => height - 3 - ((v - lo) / (hi - lo)) * (height - 6);
  const d = pts.map((p, k) => `${k ? 'L' : 'M'}${f1(X(p.i))} ${f1(Y(p.v))}`).join(' ');
  const last = pts[pts.length - 1];
  const draw = 1 - ease(clamp((t - 0.15) / 0.75));

  return (
    <svg className="vm-spark" viewBox={`0 0 ${width} ${height}`} width="100%" height={height} style={{ overflow: 'visible' }} aria-hidden="true">
      {usual != null && (
        <line x1="0" x2={width} y1={f1(Y(usual))} y2={f1(Y(usual))} strokeWidth="1" style={{ stroke: 'var(--line2)', strokeDasharray: '3 4' }} />
      )}
      <path d={d} fill="none" strokeWidth="1.75" strokeLinecap="round" strokeLinejoin="round" pathLength={1}
            style={{ stroke: color, strokeDasharray: 1, strokeDashoffset: f1(draw) }} />
      <circle cx={f1(X(last.i))} cy={f1(Y(last.v))} r="3.5" style={{ fill: color, opacity: t > 0.5 ? 1 : 0, transition: 'opacity .3s' }} />
    </svg>
  );
}

// "↑ 6 ms above your usual": a glyph, plain words, and the unit. Direction is never carried by
// colour alone, and nothing here is red: being below your usual is information, not a verdict.
export function vsUsual(value: number | null | undefined, usual: number | null | undefined, unit: string,
                        words: { up: string; down: string }, tolerance = 0.02): { arrow: string; text: string } | null {
  if (value == null || usual == null) return null;
  const diff = value - usual;
  if (Math.abs(diff) <= Math.abs(usual) * tolerance) return { arrow: '≈', text: 'about your usual' };
  const n = Math.abs(diff) >= 100 ? Math.round(Math.abs(diff)).toLocaleString('en-US') : String(Math.round(Math.abs(diff)));
  return diff > 0
    ? { arrow: '↑', text: `${n}${unit} ${words.up}` }
    : { arrow: '↓', text: `${n}${unit} ${words.down}` };
}

// Nothing loaded and the server cannot be reached: said plainly, with the way out. Never a
// blank screen and never a zero.
export function OutOfReach({ onRetry }: { onRetry: () => void }) {
  return (
    <div className="vm-missing" role="alert">
      <svg width="168" height="168" viewBox="0 0 168 168" aria-hidden="true">
        <circle cx="84" cy="84" r="72" fill="none" strokeWidth="3" strokeLinecap="round" style={{ stroke: 'var(--line2)', strokeDasharray: '2 9' }} />
      </svg>
      <b>Data out of reach</b>
      <p>Vitara could not reach your server. Check you are on the same network as it, then try again.</p>
      <button type="button" className="vm-cta" style={{ width: 'auto' }} onClick={onRetry}>Try again</button>
    </div>
  );
}

// "Thursday, October 8", in the configured zone rather than the phone's.
export const dateLine = () =>
  new Date(todayInTz() + 'T12:00:00').toLocaleDateString('en-US', { weekday: 'long', month: 'long', day: 'numeric' });

// A longer line with the person's own usual as a soft band behind it and a dot on today. The
// band is the middle half of their recent values, so it says "this is where you normally are"
// without being stretched by one odd day.
export function BandChart({ data, band, color, t = 1, height = 110 }: {
  data: (number | null)[];
  band: [number, number] | null;
  color: string;
  t?: number;
  height?: number;
}) {
  const width = 295;
  const pts = data.map((v, i) => ({ v, i })).filter((p): p is { v: number; i: number } => p.v != null);
  if (pts.length < 3) return <div className="vm-spark-none" style={{ height }}>Not enough days yet</div>;

  const vals = pts.map(p => p.v).concat(band ?? []);
  let lo = Math.min(...vals), hi = Math.max(...vals);
  if (hi - lo < 1e-9) { lo -= 1; hi += 1; }
  const pad = (hi - lo) * 0.12;
  lo -= pad; hi += pad;
  const n = data.length;
  const X = (i: number) => 3 + (i / (n - 1)) * (width - 6);
  const Y = (v: number) => height - 6 - ((v - lo) / (hi - lo)) * (height - 12);
  const d = pts.map((p, k) => `${k ? 'L' : 'M'}${f1(X(p.i))} ${f1(Y(p.v))}`).join(' ');
  const last = pts[pts.length - 1];
  const draw = 1 - ease(clamp((t - 0.15) / 0.75));

  return (
    <svg viewBox={`0 0 ${width} ${height}`} width="100%" height={height} style={{ overflow: 'visible' }} aria-hidden="true">
      {band && (
        <rect x="0" y={f1(Y(band[1]))} width={width} height={f1(Math.max(4, Y(band[0]) - Y(band[1])))} rx="4" style={{ fill: 'var(--teal)', opacity: 0.1 }} />
      )}
      <path d={d} fill="none" strokeWidth="1.75" strokeLinecap="round" strokeLinejoin="round" pathLength={1}
            style={{ stroke: color, strokeDasharray: 1, strokeDashoffset: f1(draw) }} />
      <circle cx={f1(X(last.i))} cy={f1(Y(last.v))} r="4" style={{ fill: color, stroke: 'var(--s1)', strokeWidth: 2, opacity: t > 0.5 ? 1 : 0, transition: 'opacity .3s' }} />
    </svg>
  );
}
