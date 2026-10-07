import type { ReactNode } from 'react';

// The pictures on the Insight page.
//
// Plain SVG, no chart library, same as the rest of the health kit, so these can move to a
// phone app unchanged. Every one of them states what it shows in words as well as colour:
// a ring you cannot read without telling blue from amber is decoration, not information.
//
// None of them invents a number. A part with nothing in it is drawn as nothing, and a
// ring with no parts is an empty track -- never a full circle of some default colour.

export interface DonutPart {
  key: string;
  label: string;
  value: number;
  color: string;
}

// A ring split into parts, with the legend beside it carrying the counts.
export function DonutSplit({ parts, size = 148, thickness = 15, children, ariaLabel }: {
  parts: DonutPart[];
  size?: number;
  thickness?: number;
  children?: ReactNode;
  ariaLabel: string;
}) {
  const total = parts.reduce((s, p) => s + Math.max(0, p.value), 0);
  const r = (size - thickness) / 2;
  const c = 2 * Math.PI * r;
  const gap = total > 0 && parts.filter(p => p.value > 0).length > 1 ? 3 : 0;

  let offset = 0;
  const arcs = parts.filter(p => p.value > 0).map(p => {
    const len = (p.value / total) * c;
    const arc = { ...p, dash: Math.max(0, len - gap), at: offset };
    offset += len;
    return arc;
  });

  return (
    <div className="ix-donut" style={{ width: size, height: size }}>
      <svg width={size} height={size} role="img" aria-label={ariaLabel}>
        <circle className="ix-donut-track" cx={size / 2} cy={size / 2} r={r} fill="none" strokeWidth={thickness} />
        {arcs.map(a => (
          <circle
            key={a.key}
            cx={size / 2} cy={size / 2} r={r} fill="none"
            stroke={a.color} strokeWidth={thickness} strokeLinecap="butt"
            strokeDasharray={`${a.dash.toFixed(2)} ${(c - a.dash).toFixed(2)}`}
            strokeDashoffset={(-a.at).toFixed(2)}
            transform={`rotate(-90 ${size / 2} ${size / 2})`}
          >
            <title>{`${a.label}: ${a.value}`}</title>
          </circle>
        ))}
      </svg>
      <div className="ix-donut-mid">{children}</div>
    </div>
  );
}

export function Legend({ parts }: { parts: DonutPart[] }) {
  return (
    <ul className="ix-legend">
      {parts.map(p => (
        <li key={p.key} className={p.value === 0 ? 'is-zero' : ''}>
          <i style={{ background: p.color }} aria-hidden="true" />
          <span>{p.label}</span>
          <b>{p.value}</b>
        </li>
      ))}
    </ul>
  );
}

// ── Sleep clock ──────────────────────────────────────────────────────────────

const minutesOf = (hhmm: string): number | null => {
  const m = /^(\d{1,2}):(\d{2})$/.exec(hhmm);
  return m ? (Number(m[1]) * 60 + Number(m[2])) % 1440 : null;
};

const polar = (cx: number, cy: number, r: number, minutes: number) => {
  // Midnight at the top, clockwise, so a night reads as an arc across the bottom-left
  // of the dial the way it does on a wall clock.
  const a = (minutes / 1440) * 2 * Math.PI - Math.PI / 2;
  return { x: cx + r * Math.cos(a), y: cy + r * Math.sin(a) };
};

const arcPath = (cx: number, cy: number, r: number, from: number, to: number) => {
  const span = (to - from + 1440) % 1440;
  const a = polar(cx, cy, r, from);
  const b = polar(cx, cy, r, from + span);
  return `M ${a.x.toFixed(2)} ${a.y.toFixed(2)} A ${r} ${r} 0 ${span > 720 ? 1 : 0} 1 ${b.x.toFixed(2)} ${b.y.toFixed(2)}`;
};

// A 24-hour dial with the night drawn across it. The pale arc around bedtime is how much
// that moment wanders -- the honest part of "you go to bed at 11:10".
export function SleepDial({ bedtime, wake, wanderMinutes, size = 168 }: {
  bedtime: string;
  wake: string;
  wanderMinutes?: number;
  size?: number;
}) {
  const bed = minutesOf(bedtime);
  const up = minutesOf(wake);
  if (bed == null || up == null) return null;

  const cx = size / 2, cy = size / 2;
  const r = size / 2 - 22;
  const span = (up - bed + 1440) % 1440;
  const hours = Math.floor(span / 60), mins = span % 60;
  const wander = Math.max(0, Math.min(180, wanderMinutes ?? 0));

  const bedDot = polar(cx, cy, r, bed);
  const wakeDot = polar(cx, cy, r, up);

  return (
    <div className="ix-dial" style={{ width: size, height: size }}>
      <svg width={size} height={size} role="img"
           aria-label={`Asleep from ${bedtime} to ${wake}, about ${hours} hours ${mins} minutes`}>
        <circle className="ix-dial-face" cx={cx} cy={cy} r={r} fill="none" strokeWidth={14} />
        {[0, 360, 720, 1080].map(m => {
          const a = polar(cx, cy, r + 15, m);
          return (
            <text key={m} className="ix-dial-tick" x={a.x} y={a.y} textAnchor="middle" dominantBaseline="central">
              {m === 0 ? '12a' : m === 720 ? '12p' : m === 360 ? '6a' : '6p'}
            </text>
          );
        })}
        {wander > 0 && (
          <path className="ix-dial-wander" d={arcPath(cx, cy, r, (bed - wander + 1440) % 1440, (bed + wander) % 1440)}
                fill="none" strokeWidth={14} strokeLinecap="round" />
        )}
        <path className="ix-dial-night" d={arcPath(cx, cy, r, bed, up)} fill="none" strokeWidth={14} strokeLinecap="round" />
        <circle className="ix-dial-pin" cx={bedDot.x} cy={bedDot.y} r={4.5} />
        <circle className="ix-dial-pin wake" cx={wakeDot.x} cy={wakeDot.y} r={4.5} />
      </svg>
      <div className="ix-dial-mid">
        <b>{hours}h {String(mins).padStart(2, '0')}m</b>
        <span>in bed</span>
      </div>
    </div>
  );
}

// ── A forecast as a range ────────────────────────────────────────────────────

// The usual landing zone as a bar, the best guess as a mark in it. The scale is the
// range padded on each side, never a fixed 0-100, so a tight forecast looks tight.
export function RangeBar({ low, high, value, bounds, unit }: {
  low: number;
  high: number;
  value: number;
  bounds?: [number, number];
  unit?: string;
}) {
  const width = Math.max(1e-6, high - low);
  let lo = low - width * 0.9;
  let hi = high + width * 0.9;
  if (bounds) { lo = Math.max(bounds[0], lo); hi = Math.min(bounds[1], hi); }
  const pos = (v: number) => `${Math.max(0, Math.min(100, ((v - lo) / (hi - lo)) * 100))}%`;

  return (
    <div className="ix-range" role="img"
         aria-label={`Best guess ${Math.round(value)}${unit ?? ''}, usually between ${Math.round(low)} and ${Math.round(high)}`}>
      <span className="ix-range-track" />
      <span className="ix-range-span" style={{ left: pos(low), width: `calc(${pos(high)} - ${pos(low)})` }} />
      <span className="ix-range-mark" style={{ left: pos(value) }} />
    </div>
  );
}

// ── A split bar ──────────────────────────────────────────────────────────────

export function StackBar({ parts, ariaLabel }: { parts: DonutPart[]; ariaLabel: string }) {
  const total = parts.reduce((s, p) => s + Math.max(0, p.value), 0);
  return (
    <div className="ix-stack" role="img" aria-label={ariaLabel}>
      {total === 0
        ? <span className="ix-stack-empty" />
        : parts.filter(p => p.value > 0).map(p => (
            <span key={p.key} style={{ flexGrow: p.value, background: p.color }} title={`${p.label}: ${p.value}`} />
          ))}
    </div>
  );
}

// ── A lab value on its range ─────────────────────────────────────────────────

export interface LabReading {
  value: number;
  previous?: number | null;
  low?: number | null;
  high?: number | null;
  unit?: string | null;
  drawnOn?: string | null;
  standing?: string | null;
}

const trim = (n: number) => String(Math.round(n * 100) / 100);

// The reference range as a zone on a line, the result as a mark on it, and the last draw as
// a hollow mark when there is one. A range with no floor (LDL has a ceiling and nothing
// else) leaves the zone open on that side: the lab never said where it starts, so the
// picture does not either.
export function RangeGauge({ reading }: { reading: LabReading }) {
  const { value, previous, low, high, unit } = reading;
  const marks = [value, previous, low, high].filter((v): v is number => v != null);
  const mn = Math.min(...marks);
  const mx = Math.max(...marks);
  const pad = (mx - mn) * 0.35 || Math.abs(mx) * 0.2 || 1;
  const d0 = mn >= 0 ? Math.max(0, mn - pad) : mn - pad;
  const d1 = mx + pad;
  const at = (v: number) => `${Math.max(0, Math.min(100, ((v - d0) / (d1 - d0)) * 100))}%`;

  const zoneFrom = low != null ? at(low) : '0%';
  const zoneTo = high != null ? at(high) : '100%';
  const inside = (low == null || value >= low) && (high == null || value <= high);
  const u = unit ? ` ${unit}` : '';

  const range =
    low != null && high != null ? `${trim(low)}–${trim(high)}`
    : high != null ? `up to ${trim(high)}`
    : `from ${trim(low as number)}`;

  return (
    <div className={`ix-gauge ${inside ? 'is-in' : 'is-out'}`} role="img"
         aria-label={`${trim(value)}${u}, ${inside ? 'inside' : reading.standing ?? 'outside'} the usual range of ${range}${u}`}>
      <div className="ix-gauge-line">
        <span className="ix-gauge-track" />
        <span className="ix-gauge-zone" style={{ left: zoneFrom, width: `calc(${zoneTo} - ${zoneFrom})` }} />
        {previous != null && <span className="ix-gauge-prev" style={{ left: at(previous) }} title={`Last draw: ${trim(previous)}${u}`} />}
        <span className="ix-gauge-mark" style={{ left: at(value) }}><b>{trim(value)}</b></span>
      </div>
      <div className="ix-gauge-ends">
        <span>usual {range}{u}</span>
        {previous != null && <span>last draw {trim(previous)}</span>}
      </div>
    </div>
  );
}
