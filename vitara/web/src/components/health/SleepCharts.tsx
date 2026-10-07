import { useState } from 'react';

// Pictures for the Sleep and Recovery tabs.
//
// Plain SVG and CSS like the rest of the kit. They draw what the ring reports per night and
// nothing finer: there is no minute-by-minute stage data here, so nothing pretends there is
// (no hypnogram drawn from totals). Every picture carries its numbers in words too.

const DAY = 1440;

const polar = (c: number, r: number, minute: number) => {
  // Midnight at the top, clockwise, like a wall clock with the whole day on it.
  const a = (minute / DAY) * 2 * Math.PI - Math.PI / 2;
  return { x: c + r * Math.cos(a), y: c + r * Math.sin(a) };
};

const arc = (c: number, r: number, from: number, to: number) => {
  const span = (((to - from) % DAY) + DAY) % DAY || 1;
  const p = polar(c, r, from), q = polar(c, r, from + span);
  return `M ${p.x.toFixed(2)} ${p.y.toFixed(2)} A ${r} ${r} 0 ${span > DAY / 2 ? 1 : 0} 1 ${q.x.toFixed(2)} ${q.y.toFixed(2)}`;
};

const clock = (m: number) => {
  const v = ((Math.round(m) % DAY) + DAY) % DAY;
  return `${String(Math.floor(v / 60)).padStart(2, '0')}:${String(v % 60).padStart(2, '0')}`;
};

const hm = (min: number) => `${Math.floor(min / 60)}h ${String(Math.round(min % 60)).padStart(2, '0')}m`;

// ── Sleep rings ──────────────────────────────────────────────────────────────

export interface RingNight {
  key: string;
  label: string;       // "Wed 3 Oct"
  bed: number;         // minutes after midnight, wall clock in the configured zone
  wake: number;
  asleep: number;      // minutes actually asleep
}

// Bedtimes that cross midnight are compared on one line: 23:30 and 00:30 are an hour apart,
// not twenty-three. Anything before noon is treated as the small hours of the same night.
const unwrap = (m: number) => (m < 12 * 60 ? m + DAY : m);

const median = (xs: number[]) => {
  const s = [...xs].sort((a, b) => a - b);
  return s.length % 2 ? s[(s.length - 1) / 2] : (s[s.length / 2 - 1] + s[s.length / 2]) / 2;
};

const spread = (xs: number[]) => {
  if (xs.length < 2) return null;
  const m = xs.reduce((a, b) => a + b, 0) / xs.length;
  return Math.sqrt(xs.reduce((a, b) => a + (b - m) ** 2, 0) / (xs.length - 1));
};

// One ring per night on a 24-hour dial, the newest outermost. Nights that line up are a steady
// schedule; arcs that wander are not, and that is visible before a single number is read. The
// centre says how much bedtime moves; point at a ring and it says that night instead.
export function SleepRings({ nights, size = 300 }: { nights: RingNight[]; size?: number }) {
  const [hover, setHover] = useState<string | null>(null);
  if (nights.length === 0) return null;

  // Oldest innermost, newest outermost.
  const ordered = [...nights];
  const c = size / 2;
  const rOut = size / 2 - 30, rIn = Math.max(54, size * 0.2);
  const step = ordered.length > 1 ? (rOut - rIn) / (ordered.length - 1) : 0;
  const width = Math.max(3, Math.min(9, step * 0.72 || 9));

  const beds = ordered.map(n => unwrap(n.bed));
  const typicalBed = median(beds) % DAY;
  const wobble = spread(beds);
  const shown = ordered.find(n => n.key === hover) ?? null;
  const last = ordered[ordered.length - 1];

  const tickHours = [0, 3, 6, 9, 12, 15, 18, 21];
  const tickLabel = (h: number) => (h === 0 ? '12a' : h === 12 ? '12p' : h < 12 ? `${h}a` : `${h - 12}p`);
  const typicalA = polar(c, rIn - 12, typicalBed), typicalB = polar(c, rOut + 10, typicalBed);

  return (
    <div className="vx-rings">
      <svg width={size} height={size} viewBox={`0 0 ${size} ${size}`} role="img"
           aria-label={`${nights.length} nights on a 24-hour clock. Typical bedtime ${clock(typicalBed)}${wobble != null ? `, varying by about ${Math.round(wobble)} minutes` : ''}.`}>
        {tickHours.map(h => {
          const p = polar(c, rOut + 19, h * 60);
          const a = polar(c, rIn - 6, h * 60), b = polar(c, rOut + 6, h * 60);
          return (
            <g key={h}>
              <line className="vx-rings-spoke" x1={a.x} y1={a.y} x2={b.x} y2={b.y} />
              <text className="vx-rings-tick" x={p.x} y={p.y} textAnchor="middle" dominantBaseline="central">{tickLabel(h)}</text>
            </g>
          );
        })}

        {/* Where bedtime usually falls, as a line through every ring. */}
        <line className="vx-rings-typical" x1={typicalA.x} y1={typicalA.y} x2={typicalB.x} y2={typicalB.y} />

        {ordered.map((n, i) => {
          const r = rIn + i * step;
          const isLast = n.key === last.key;
          const age = ordered.length > 1 ? i / (ordered.length - 1) : 1;
          return (
            <g key={n.key}>
              <circle className="vx-rings-guide" cx={c} cy={c} r={r} fill="none" />
              <path
                d={arc(c, r, n.bed, n.wake)}
                fill="none"
                strokeWidth={width}
                strokeLinecap="round"
                className={`vx-rings-night ${isLast ? 'is-last' : ''} ${hover === n.key ? 'is-on' : ''} ${hover && hover !== n.key ? 'is-dim' : ''}`}
                style={isLast ? undefined : { opacity: hover ? undefined : 0.3 + 0.55 * age }}
                tabIndex={0}
                onMouseEnter={() => setHover(n.key)}
                onMouseLeave={() => setHover(null)}
                onFocus={() => setHover(n.key)}
                onBlur={() => setHover(null)}
              >
                <title>{`${n.label}: ${clock(n.bed)} to ${clock(n.wake)}, ${hm(n.asleep)} asleep`}</title>
              </path>
            </g>
          );
        })}
      </svg>

      <div className="vx-rings-mid" aria-live="polite">
        {shown ? (
          <>
            <span>{shown.label}</span>
            <b>{clock(shown.bed)}–{clock(shown.wake)}</b>
            <span>{hm(shown.asleep)} asleep</span>
          </>
        ) : (
          <>
            <span>bedtime moves</span>
            <b>{wobble == null ? '—' : `±${Math.round(wobble)}m`}</b>
            <span>usually {clock(typicalBed)}</span>
          </>
        )}
      </div>
    </div>
  );
}

// ── Stage strata ─────────────────────────────────────────────────────────────

export interface StrataNight {
  key: string;
  short: string;   // "W"
  label: string;   // "Wed 3 Oct"
  deep: number;
  rem: number;
  light: number;
  awake: number;
}

const STAGES = [
  { key: 'deep', label: 'Deep', color: 'var(--hx-2)' },
  { key: 'rem', label: 'REM', color: 'var(--hx-4)' },
  { key: 'light', label: 'Light', color: 'var(--hx-6)' },
  { key: 'awake', label: 'Awake', color: 'var(--border2)' },
] as const;

// Each night as a column of layers, deep at the bottom and time awake on top, all to one scale,
// with a line at the usual time asleep. A short night is a short column; a night that kept its
// length but lost its deep sleep is a column with a thin floor, which a total alone would hide.
export function StageStrata({ nights, usualAsleep }: { nights: StrataNight[]; usualAsleep: number | null }) {
  if (nights.length === 0) return null;
  const tallest = Math.max(...nights.map(n => n.deep + n.rem + n.light + n.awake), 1);
  const line = usualAsleep != null ? (usualAsleep / tallest) * 100 : null;

  return (
    <div className="vx-strata">
      <div className="vx-strata-plot">
        {line != null && (
          <span className="vx-strata-usual" style={{ bottom: `${line}%` }}>
            <em>usual {hm(usualAsleep as number)}</em>
          </span>
        )}
        {nights.map(n => {
          const total = n.deep + n.rem + n.light + n.awake;
          return (
            <div key={n.key} className="vx-strata-col"
                 title={`${n.label}: ${hm(n.deep + n.rem + n.light)} asleep · deep ${hm(n.deep)} · REM ${hm(n.rem)} · light ${hm(n.light)} · awake ${hm(n.awake)}`}>
              <div className="vx-strata-stack" style={{ height: `${(total / tallest) * 100}%` }}>
                {STAGES.map(s => {
                  const v = n[s.key];
                  return v > 0 ? <i key={s.key} style={{ flexGrow: v, background: s.color }} /> : null;
                })}
              </div>
              <span className="vx-strata-day">{n.short}</span>
            </div>
          );
        })}
      </div>
      <div className="vx-strata-key">
        {STAGES.map(s => <span key={s.key}><i style={{ background: s.color }} />{s.label}</span>)}
      </div>
    </div>
  );
}

// ── Recovery flower ──────────────────────────────────────────────────────────

export interface Petal {
  key: string;
  label: string;
  value: number | null;   // 0-100, as the ring scores it
  usual: number | null;   // the same, averaged over the fortnight
  color: string;
}

// The parts of recovery as petals round the score: each petal's length is how that part scored
// last night, and the dashed outline behind it is where it usually reaches. A short petal inside
// its own outline is the part that held recovery back. A part with no reading has no petal,
// only its outline, rather than a petal of length zero that would read as "scored zero".
export function RecoveryFlower({ petals, score, size = 240 }: { petals: Petal[]; score: number | null; size?: number }) {
  const n = petals.length;
  if (n === 0) return null;

  const c = size / 2;
  const r0 = size * 0.17, r1 = size / 2 - 12;
  const gap = 0.07;
  const sector = (2 * Math.PI) / n;
  const len = (v: number) => r0 + (Math.max(0, Math.min(100, v)) / 100) * (r1 - r0);

  const at = (r: number, a: number) => ({ x: c + r * Math.cos(a), y: c + r * Math.sin(a) });
  const wedge = (outer: number, i: number) => {
    const a0 = -Math.PI / 2 + i * sector + gap, a1 = -Math.PI / 2 + (i + 1) * sector - gap;
    const p1 = at(r0, a0), p2 = at(outer, a0), p3 = at(outer, a1), p4 = at(r0, a1);
    return `M ${p1.x.toFixed(2)} ${p1.y.toFixed(2)} L ${p2.x.toFixed(2)} ${p2.y.toFixed(2)} ` +
           `A ${outer} ${outer} 0 0 1 ${p3.x.toFixed(2)} ${p3.y.toFixed(2)} L ${p4.x.toFixed(2)} ${p4.y.toFixed(2)} ` +
           `A ${r0} ${r0} 0 0 0 ${p1.x.toFixed(2)} ${p1.y.toFixed(2)} Z`;
  };

  return (
    <div className="vx-flower" style={{ width: size, height: size }}>
      <svg width={size} height={size} role="img"
           aria-label={petals.map(p => `${p.label} ${p.value ?? 'not measured'}${p.usual != null ? `, usually ${Math.round(p.usual)}` : ''}`).join('; ')}>
        <circle className="vx-flower-guide" cx={c} cy={c} r={len(50)} fill="none" />
        <circle className="vx-flower-guide" cx={c} cy={c} r={r1} fill="none" />
        {petals.map((p, i) => (
          <g key={p.key}>
            {p.usual != null && <path d={wedge(len(p.usual), i)} className="vx-flower-usual" />}
            {p.value != null && (
              <path d={wedge(len(p.value), i)} className="vx-flower-petal" style={{ fill: p.color }}>
                <title>{`${p.label}: ${p.value}${p.usual != null ? ` (usually ${Math.round(p.usual)})` : ''}`}</title>
              </path>
            )}
          </g>
        ))}
      </svg>
      <div className="vx-flower-mid">
        <b>{score ?? '—'}</b>
        <span>readiness</span>
      </div>
    </div>
  );
}

// ── Readiness weather ────────────────────────────────────────────────────────

export interface WeatherDay {
  key: string;
  short: string;     // "Wed"
  date: string;      // "3"
  label: string;     // "Wed 3 Oct"
  score: number | null;
  level: string | null;
}

const Sun = () => (
  <svg viewBox="0 0 32 32" aria-hidden="true"><circle cx="16" cy="16" r="6.5" className="sun" />
    {[0, 45, 90, 135, 180, 225, 270, 315].map(d => (
      <line key={d} x1="16" y1="3.5" x2="16" y2="7" className="ray" transform={`rotate(${d} 16 16)`} />
    ))}
  </svg>
);
const SunCloud = () => (
  <svg viewBox="0 0 32 32" aria-hidden="true"><circle cx="12" cy="12" r="5.5" className="sun" />
    <path d="M10 25h13a4.5 4.5 0 0 0 0-9 6 6 0 0 0-11.4 1.6A3.8 3.8 0 0 0 10 25z" className="cloud" />
  </svg>
);
const Cloud = () => (
  <svg viewBox="0 0 32 32" aria-hidden="true">
    <path d="M8 24h16a5 5 0 0 0 0-10 7 7 0 0 0-13.3 1.8A4.2 4.2 0 0 0 8 24z" className="cloud dark" />
  </svg>
);
const Blank = () => (
  <svg viewBox="0 0 32 32" aria-hidden="true"><circle cx="16" cy="16" r="8" className="blank" /></svg>
);

const WEATHER: Record<string, { icon: () => JSX.Element; word: string; tone: string }> = {
  optimal: { icon: Sun, word: 'optimal', tone: 'good' },
  good: { icon: SunCloud, word: 'good', tone: 'fine' },
  pay_attention: { icon: Cloud, word: 'pay attention', tone: 'warn' },
};

// The fortnight as a forecast strip: sun for an optimal day, sun behind cloud for a good one,
// cloud for one that asked for attention. A metaphor people already read at a glance, and never
// alone: the level is written under every icon, and a day with an unfamiliar level shows its
// own word with a plain mark rather than being forced into a picture that does not fit.
export function ReadinessWeather({ days }: { days: WeatherDay[] }) {
  return (
    <div className="vx-weather" role="list">
      {days.map(d => {
        const w = d.level ? WEATHER[d.level] : undefined;
        const Icon = w?.icon ?? Blank;
        const word = w?.word ?? (d.level ? d.level.replace(/_/g, ' ') : 'no score');
        return (
          <div key={d.key} role="listitem" className={`vx-weather-day ${w?.tone ?? 'none'}`}
               title={`${d.label}: ${d.score ?? 'no score'}${d.level ? `, ${word}` : ''}`}>
            <span className="vx-weather-name">{d.short}<small>{d.date}</small></span>
            <Icon />
            <b>{d.score ?? '—'}</b>
            <span className="vx-weather-word">{word}</span>
          </div>
        );
      })}
    </div>
  );
}
