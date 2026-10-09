import type { ReactNode } from 'react';
import { clamp, ease } from './motion';

// Concentric rings, the signature shape of the phone app.
//
// Each ring is a score drawn as an arc with a gradient stroke and a soft glow. Three states, and
// they look different on purpose:
//   live  - gradient arc, glow, a solid track
//   stale - the same arc at half strength with no glow ("this is the last thing we had")
//   empty - a dashed track and no arc at all. A missing score is NEVER a ring of zero: a night
//           nobody wore the sensor is not a night of score nothing.
// The arcs fill from the outside in, a beat apart, from the shared reveal clock `t` (0 → 1).

export type RingMode = 'live' | 'stale' | 'empty';
export type RingHue = 'sleep' | 'recovery' | 'activity' | 'blue';

export interface RingItem {
  key: string;
  label: string;
  value: number | null | undefined;      // 0..1, or missing
  hue: RingHue;
  mode?: RingMode;                       // default: live when there is a value, otherwise empty
}

const GRADIENT: Record<RingHue, { stroke: string; glow: string }> = {
  sleep:    { stroke: 'url(#vg-s)', glow: 'var(--glow-s)' },
  recovery: { stroke: 'url(#vg-r)', glow: 'var(--glow-r)' },
  activity: { stroke: 'url(#vg-a)', glow: 'var(--glow-a)' },
  blue:     { stroke: 'url(#vg-b)', glow: 'var(--glow-r)' },
};

// The gradients every ring points at, defined once for the whole page.
export function RingDefs() {
  const stop = (offset: number, v: string) => <stop offset={offset} style={{ stopColor: `var(${v})` }} />;
  return (
    <svg width="0" height="0" style={{ position: 'absolute' }} aria-hidden="true" focusable="false">
      <defs>
        <linearGradient id="vg-s" x1="0" y1="0" x2="1" y2="1">{stop(0, '--sleep2')}{stop(1, '--sleep')}</linearGradient>
        <linearGradient id="vg-r" x1="0" y1="0" x2="1" y2="1">{stop(0, '--teal2')}{stop(1, '--teal')}</linearGradient>
        <linearGradient id="vg-a" x1="0" y1="0" x2="1" y2="1">{stop(0, '--gold2')}{stop(1, '--gold')}</linearGradient>
        <linearGradient id="vg-b" x1="0" y1="0" x2="1" y2="1">{stop(0, '--lt')}{stop(1, '--blue')}</linearGradient>
      </defs>
    </svg>
  );
}

export function Rings({ items, size, stroke, gap, t = 1, center, onPick, label }: {
  items: RingItem[];
  size: number;
  stroke: number;
  gap: number;
  t?: number;                 // the shared reveal clock
  center?: ReactNode;
  onPick?: (key: string) => void;
  label?: string;
}) {
  const aria = label ?? items.map(r => `${r.label} ${r.value == null ? 'not measured' : Math.round(r.value * 100)}`).join(', ');

  return (
    <div className="vm-rings" style={{ width: size, height: size }}>
      <svg width={size} height={size} viewBox={`0 0 ${size} ${size}`} role="img" aria-label={aria}
           style={{ transform: 'rotate(-90deg)', overflow: 'visible' }}>
        {items.map((it, i) => {
          const r = size / 2 - stroke / 2 - i * (stroke + gap);
          const c = 2 * Math.PI * r;
          const mode: RingMode = it.mode ?? (it.value == null ? 'empty' : 'live');
          const live = mode !== 'empty';
          const progress = live ? ease(clamp((t - 0.06 - i * 0.08) / 0.72)) : 0;
          const v = clamp(it.value ?? 0) * progress;
          const g = GRADIENT[it.hue];
          const stale = mode === 'stale';
          return (
            <g key={it.key} onClick={() => onPick?.(it.key)} style={{ cursor: onPick ? 'pointer' : undefined }}>
              <circle cx={size / 2} cy={size / 2} r={r} fill="none"
                      strokeWidth={live ? stroke : 2} strokeLinecap={live ? 'round' : 'butt'}
                      style={{ stroke: live ? 'var(--track)' : 'var(--line2)', strokeDasharray: live ? 'none' : '2 6' }} />
              <circle cx={size / 2} cy={size / 2} r={r} fill="none" strokeWidth={stroke} strokeLinecap="round"
                      style={{
                        stroke: g.stroke,
                        strokeDasharray: c.toFixed(1),
                        strokeDashoffset: (c * (1 - v)).toFixed(1),
                        opacity: live && v > 0.003 ? (stale ? 0.5 : 1) : 0,
                        filter: live && !stale ? `drop-shadow(0 0 ${Math.round(stroke / 3)}px ${g.glow})` : 'none',
                      }} />
            </g>
          );
        })}
      </svg>
      {center != null && <div className="vm-rings-mid">{center}</div>}
    </div>
  );
}
