import { useState } from 'react';
import type { ReactNode } from 'react';

// The pictures for the Patterns tab.
//
// These are the less ordinary ones: a web of relationships, a ring of measures, a track for a
// trial, a before-and-after strip. They are plain SVG and CSS, no library, for the same reason
// as the rest of the kit. Each draws only what it was given and refuses to invent the rest, and
// every one of them is also readable without colour or without the picture, because the words
// stay beside it.

// ── The web: what moves with what ───────────────────────────────────────────

export interface WebLink {
  driver: string;
  outcome: string;
  rho: number;
  lagDays: number;
  n: number;
}

const ROW = 52;

const signed = (v: number, digits = 2) => `${v > 0 ? '+' : v < 0 ? '−' : ''}${Math.abs(v).toFixed(digits)}`;

// Drivers on the left, what they move on the right, a ribbon between each pair that has
// earned one. The ribbon's thickness is how strongly the two track each other; a solid
// ribbon moves together, a dashed one moves apart. Point at a name and everything not
// connected to it steps back, which is the only way to read a tangle of seven lines.
export function RelationshipWeb({ links, name }: { links: WebLink[]; name: (metric: string) => string }) {
  const [focus, setFocus] = useState<{ side: 'driver' | 'outcome'; key: string } | null>(null);

  if (links.length === 0) return null;

  const strongest = [...links].sort((a, b) => Math.abs(b.rho) - Math.abs(a.rho));
  const drivers = [...new Set(strongest.map(l => l.driver))];
  const outcomes = [...new Set(strongest.map(l => l.outcome))];

  const rows = Math.max(drivers.length, outcomes.length);
  const H = rows * ROW;
  const yAt = (i: number, count: number) => (i + 0.5) * (H / count);

  const lit = (l: WebLink) =>
    focus === null || (focus.side === 'driver' ? l.driver === focus.key : l.outcome === focus.key);

  // Weakest first, so the strong ones are drawn on top of the faint ones.
  const drawn = [...strongest].reverse().map(l => {
    const y1 = yAt(drivers.indexOf(l.driver), drivers.length);
    const y2 = yAt(outcomes.indexOf(l.outcome), outcomes.length);
    return { l, y1, y2, mid: (y1 + y2) / 2 };
  });

  const nodeButton = (side: 'driver' | 'outcome', key: string, index: number, count: number) => (
    <button
      key={key}
      type="button"
      className={`ix-web-node ${side} ${focus?.side === side && focus.key === key ? 'is-on' : ''}`}
      style={{ top: yAt(index, count) - 15 }}
      onMouseEnter={() => setFocus({ side, key })}
      onMouseLeave={() => setFocus(null)}
      onFocus={() => setFocus({ side, key })}
      onBlur={() => setFocus(null)}
    >
      {name(key)}
    </button>
  );

  return (
    <div className="ix-web" style={{ height: H }} role="group"
         aria-label={`${links.length} relationships between ${drivers.length} things you do and ${outcomes.length} things that follow`}>
      <div className="ix-web-col">{drivers.map((d, i) => nodeButton('driver', d, i, drivers.length))}</div>

      <div className="ix-web-mid">
        <svg viewBox={`0 0 100 ${H}`} preserveAspectRatio="none" width="100%" height={H} aria-hidden="true">
          {drawn.map(({ l, y1, y2 }) => (
            <path
              key={`${l.driver}-${l.outcome}-${l.lagDays}`}
              d={`M 0 ${y1} C 50 ${y1}, 50 ${y2}, 100 ${y2}`}
              fill="none"
              className={`ix-web-ribbon ${l.rho >= 0 ? 'pos' : 'neg'} ${lit(l) ? '' : 'is-dim'}`}
              strokeWidth={3 + Math.abs(l.rho) * 15}
              strokeDasharray={l.rho >= 0 ? undefined : '9 6'}
              vectorEffect="non-scaling-stroke"
              onMouseEnter={() => setFocus({ side: 'driver', key: l.driver })}
              onMouseLeave={() => setFocus(null)}
            >
              <title>{`${name(l.driver)} → ${name(l.outcome)}: ${signed(l.rho)} over ${l.n} days`}</title>
            </path>
          ))}
        </svg>

        {drawn.map(({ l, mid }) => (
          <span
            key={`chip-${l.driver}-${l.outcome}-${l.lagDays}`}
            className={`ix-web-chip ${l.rho >= 0 ? 'pos' : 'neg'} ${lit(l) ? '' : 'is-dim'}`}
            style={{ top: mid - 11 }}
          >
            {signed(l.rho)}
            <small>{l.lagDays === 0 ? 'same day' : 'next day'}</small>
          </span>
        ))}
      </div>

      <div className="ix-web-col right">{outcomes.map((o, i) => nodeButton('outcome', o, i, outcomes.length))}</div>
    </div>
  );
}

// ── The ring: several measures read together ────────────────────────────────

export type Movement = 'unmeasured' | 'steady' | 'favourable' | 'unfavourable';

export interface RingBead {
  label: string;
  movement: Movement;
  counts: boolean;
}

const polar = (cx: number, cy: number, r: number, angle: number) => ({
  x: cx + r * Math.cos(angle),
  y: cy + r * Math.sin(angle),
});

// One bead per measure, evenly round a ring. A bead that is moving the wrong way is amber, one
// moving the right way blue, a steady one plain, and one nobody is measuring is hollow and dashed:
// absence is drawn as absence, never as "fine". Where two neighbours move the same way the ring
// between them is lit, which is the picture of "moving together". A small bead is a measure that
// is shown but not counted because it is derived from another one on the ring.
export function PatternRing({ beads, moving, counted, size = 176 }: {
  beads: RingBead[];
  moving: number;
  counted: number;
  size?: number;
}) {
  const n = beads.length;
  if (n === 0) return null;

  const c = size / 2;
  const r = size / 2 - 22;
  const at = (i: number) => polar(c, c, r, -Math.PI / 2 + (i / n) * 2 * Math.PI);

  const lit = (a: Movement, b: Movement) => a === b && (a === 'unfavourable' || a === 'favourable');

  const arcs = beads.map((bead, i) => {
    const j = (i + 1) % n;
    if (n < 3 || !lit(bead.movement, beads[j].movement)) return null;
    const p = at(i), q = at(j);
    return { key: i, d: `M ${p.x.toFixed(1)} ${p.y.toFixed(1)} A ${r} ${r} 0 0 1 ${q.x.toFixed(1)} ${q.y.toFixed(1)}`, tone: bead.movement };
  });

  return (
    <div className="ix-ring" style={{ width: size, height: size }}>
      <svg width={size} height={size} role="img"
           aria-label={`${moving} of ${counted} counted measures are moving the wrong way together`}>
        <circle className="ix-ring-track" cx={c} cy={c} r={r} fill="none" />
        {arcs.map(a => a && <path key={a.key} d={a.d} fill="none" className={`ix-ring-arc ${a.tone}`} />)}
        {beads.map((b, i) => {
          const p = at(i);
          const rad = b.counts ? 13 : 9;
          return (
            <g key={i} className={`ix-bead ${b.movement}`} transform={`translate(${p.x.toFixed(1)} ${p.y.toFixed(1)})`}>
              <circle r={rad} />
              <text textAnchor="middle" dominantBaseline="central">{i + 1}</text>
              <title>{`${i + 1}. ${b.label}`}</title>
            </g>
          );
        })}
      </svg>
      <div className="ix-ring-mid">
        <b>{moving}<small>/{counted}</small></b>
        <span>moving the<br />wrong way</span>
      </div>
    </div>
  );
}

// The number that goes with a bead, for the list beside the ring.
export function Bead({ n, movement }: { n: number; movement: Movement }) {
  return <span className={`ix-bead-badge ${movement}`} aria-hidden="true">{n}</span>;
}

// ── The track: how far a trial has got ──────────────────────────────────────

// The first stretch of a trial is not counted and the next is measured; nothing can be said
// before the end of it. Drawn as that, with a pin for today, "cannot say yet" is not a verdict
// somebody has to take on trust: you can see how much road is left.
export function TrialTrack({ daysIn, runInDays, windowDays, running }: {
  daysIn: number;
  runInDays: number;
  windowDays: number;
  running: boolean;
}) {
  const total = runInDays + windowDays;
  const scale = Math.max(total, daysIn + 2);
  const pct = (d: number) => `${(Math.max(0, Math.min(scale, d)) / scale) * 100}%`;
  const measured = Math.max(0, Math.min(windowDays, daysIn - runInDays));
  const left = Math.max(0, total - daysIn);

  return (
    <div className="ix-trial"
         role="img"
         aria-label={left > 0
           ? `Day ${daysIn}. ${Math.max(0, runInDays - daysIn)} days not yet counted, ${windowDays - measured} days of measuring still to go.`
           : `Day ${daysIn}. The measuring window is complete.`}>
      <div className="ix-trial-bar">
        <span className="runin" style={{ width: pct(runInDays) }} />
        <span className="window" style={{ left: pct(runInDays), width: `calc(${pct(total)} - ${pct(runInDays)})` }}>
          <i style={{ width: `${(measured / windowDays) * 100}%` }} />
        </span>
        {running && <span className="pin" style={{ left: pct(daysIn) }}><b>day {daysIn}</b></span>}
      </div>
      <div className="ix-trial-legend">
        <span style={{ width: pct(runInDays) }}>not counted</span>
        <span style={{ width: `calc(${pct(total)} - ${pct(runInDays)})` }}>measured</span>
      </div>
    </div>
  );
}

// ── The strip: before and after ─────────────────────────────────────────────

export interface Window {
  median: number;
  p25?: number;
  p75?: number;
  n: number;
}

// Two stretches of the same measure on one scale: the usual spread as a bar, the typical value
// as a dot. If the bars overlap heavily the picture says
// what the verdict says, that the difference is small against ordinary day-to-day wobble.
export function ShiftStrip({ before, after, format, tone }: {
  before: Window;
  after: Window;
  format: (v: number) => string;
  tone?: 'good' | 'warn';
}) {
  const lows = [before.p25 ?? before.median, after.p25 ?? after.median];
  const highs = [before.p75 ?? before.median, after.p75 ?? after.median];
  const mn = Math.min(...lows), mx = Math.max(...highs);
  const pad = (mx - mn) * 0.25 || Math.abs(mx) * 0.1 || 1;
  const lo = mn - pad, hi = mx + pad;
  const x = (v: number) => ((v - lo) / (hi - lo)) * 100;

  const row = (title: string, w: Window) => {
    const a = x(w.p25 ?? w.median), b = x(w.p75 ?? w.median);
    return (
      <div className="ix-shift-row">
        <span className="ix-shift-title">{title}<small>{w.n} days</small></span>
        <span className="ix-shift-lane">
          <span className="ix-shift-iqr" style={{ left: `${a}%`, width: `${Math.max(1.5, b - a)}%` }} />
          <span className="ix-shift-dot" style={{ left: `${x(w.median)}%` }} />
        </span>
        <span className="ix-shift-val">{format(w.median)}</span>
      </div>
    );
  };

  return (
    <div className={`ix-shift ${tone ?? ''}`} role="img"
         aria-label={`Typical value ${format(before.median)} before and ${format(after.median)} after`}>
      {row('Before', before)}
      {row('After', after)}
    </div>
  );
}

// ── The tug: how far one change would move tomorrow ─────────────────────────

// A bar out from a centre line. The line is "no change"; the length is how far, on a scale
// shared by every question asked, so a small answer looks small beside a big one.
export function Tug({ change, max, children }: { change: number; max: number; children?: ReactNode }) {
  const share = max === 0 ? 0 : Math.min(1, Math.abs(change) / max) * 50;
  const up = change > 0;
  return (
    <div className="ix-tug" role="img" aria-label={`${up ? 'Up' : change < 0 ? 'Down' : 'No change'} ${Math.abs(change)}`}>
      <span className="ix-tug-axis" />
      <span
        className={`ix-tug-bar ${up ? 'up' : 'down'}`}
        style={up ? { left: '50%', width: `${share}%` } : { right: '50%', width: `${share}%` }}
      />
      <span className="ix-tug-knob" style={{ left: `${50 + (up ? share : -share)}%` }} />
      {children}
    </div>
  );
}
