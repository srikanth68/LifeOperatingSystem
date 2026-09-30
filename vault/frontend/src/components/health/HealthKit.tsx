import { useLayoutEffect, useRef, useState } from 'react';
import type { ReactNode } from 'react';
import '../../styles/health-app.css';

// The shared pieces of the health app: the shell, the tiles, and the empty states.
//
// Vitara and Insight are heading for a product of their own, so they stop borrowing
// the dashboard's dark chrome and get one small set of primitives instead. Everything
// here is deliberately plain HTML and CSS — no chart library, no animation library —
// so the same components can move to a phone app without carrying a stack with them.

export const HX_SERIES = ['var(--hx-1)', 'var(--hx-2)', 'var(--hx-3)', 'var(--hx-4)', 'var(--hx-5)', 'var(--hx-6)'];

// `accent` is for the second tab: Insight reads the same data as Vitara and belongs in
// the same skin, so it takes the same shell and repaints only its mark.
export function Shell({ title, subtitle, icon, right, tabs, accent, accentWash, children }: {
  title: string;
  subtitle: string;
  icon: ReactNode;
  right?: ReactNode;
  tabs?: ReactNode;
  accent?: string;
  accentWash?: string;
  children: ReactNode;
}) {
  const tone = accent
    ? { ['--mark' as string]: accent, ['--mark-wash' as string]: accentWash ?? 'rgba(63,91,217,0.09)' }
    : undefined;
  return (
    <div className="hx" style={tone}>
      <header className="hx-top">
        <div className="hx-mark">{icon}</div>
        <div className="hx-titles">
          <h1>{title}</h1>
          <p>{subtitle}</p>
        </div>
        {right && <div className="hx-top-right">{right}</div>}
      </header>
      {tabs}
      {children}
    </div>
  );
}

export function Tabs<T extends string>({ tabs, active, onPick }: {
  tabs: { id: T; label: string }[];
  active: T;
  onPick: (id: T) => void;
}) {
  return (
    <nav className="hx-tabs" role="tablist">
      {tabs.map(t => (
        <button
          key={t.id}
          role="tab"
          aria-selected={active === t.id}
          className={`hx-tab ${active === t.id ? 'active' : ''}`}
          onClick={() => onPick(t.id)}
        >
          {t.label}
        </button>
      ))}
    </nav>
  );
}

// The explanation, on request.
//
// Every tile used to carry a sentence about what the number means. Read once, that is
// useful; read every morning it is clutter, and it crowded out the number itself. The
// words stay -- one tap away, never gone.
export function Info({ children, label = 'What is this?' }: { children: ReactNode; label?: string }) {
  const [open, setOpen] = useState(false);
  const [flip, setFlip] = useState(false);
  const pop = useRef<HTMLSpanElement>(null);

  // A tile in the last column would push its note off the right edge, so the note
  // measures itself once and hangs from its right edge instead.
  useLayoutEffect(() => {
    if (!open || !pop.current) return;
    const r = pop.current.getBoundingClientRect();
    setFlip(r.right > window.innerWidth - 8);
  }, [open]);

  return (
    <span className="hx-info-wrap">
      <button
        type="button"
        className={`hx-info ${open ? 'open' : ''}`}
        aria-expanded={open}
        aria-label={label}
        onClick={() => { setFlip(false); setOpen(o => !o); }}
      >
        i
      </button>
      {open && (
        <span className={`hx-pop ${flip ? 'flip' : ''}`} role="note" ref={pop}>
          {children}
          <button type="button" className="hx-pop-close" onClick={() => setOpen(false)} aria-label="Close">×</button>
        </span>
      )}
    </span>
  );
}

export function Card({ children, className = '' }: { children: ReactNode; className?: string }) {
  return <div className={`hx-card ${className}`}>{children}</div>;
}

export function SectionHead({ title, note, info }: { title: string; note?: string; info?: ReactNode }) {
  return (
    <div className="hx-section">
      <h2>{title}</h2>
      {note && <p>{note}</p>}
      {info && <Info>{info}</Info>}
    </div>
  );
}

export function Chip({ tone = 'neutral', children }: { tone?: 'neutral' | 'good' | 'warn' | 'bad'; children: ReactNode }) {
  return <span className={`hx-chip ${tone === 'neutral' ? '' : tone}`}>{children}</span>;
}

// A thin arc. Missing is drawn as an empty track with a dash in the middle, never as a
// full ring of some default colour — a score nobody has is not a score of zero.
export function Ring({ score, size = 104, label, tone = 'var(--vitara)' }: {
  score?: number | null;
  size?: number;
  label?: string;
  tone?: string;
}) {
  const stroke = 8;
  const r = (size - stroke) / 2;
  const c = 2 * Math.PI * r;
  const pct = score == null ? 0 : Math.max(0, Math.min(100, score)) / 100;

  return (
    <div className="hx-ring" style={{ width: size, height: size }}>
      <svg width={size} height={size} aria-hidden="true">
        <circle className="hx-ring-track" cx={size / 2} cy={size / 2} r={r} fill="none" strokeWidth={stroke} />
        {score != null && (
          <circle
            className="hx-ring-fill"
            cx={size / 2} cy={size / 2} r={r} fill="none"
            stroke={tone} strokeWidth={stroke}
            strokeDasharray={`${(c * pct).toFixed(1)} ${c.toFixed(1)}`}
          />
        )}
      </svg>
      <div className="hx-ring-mid">
        <span className="hx-ring-num" style={score == null ? { color: 'var(--text3)' } : undefined}>
          {score == null ? '—' : Math.round(score)}
        </span>
        {label && <span className="hx-ring-cap">{label}</span>}
      </div>
    </div>
  );
}

// One number, its name, and what it is measured against.
//
// `empty` is the whole point: a tile with nothing in it says so, and says what would
// fill it. `info` holds the explanation, which sits behind the i rather than under every
// number. `accent` tints the value, so a wall of tiles reads as a set of measurements
// rather than a spreadsheet -- it carries no meaning of its own, which is why it is the
// series palette and never the status colours.
export function Stat({ label, value, unit, sub, chip, empty, info, accent }: {
  label: string;
  value?: string | number | null;
  unit?: string;
  sub?: ReactNode;
  chip?: ReactNode;
  empty?: string;
  info?: ReactNode;
  accent?: string;
}) {
  const missing = value == null || value === '';
  return (
    <div className={`hx-stat ${missing ? 'is-empty' : ''}`} style={accent ? { ['--tile' as string]: accent } : undefined}>
      <div className="hx-stat-head">
        <span className="hx-stat-label">
          {label}
          {info && <Info>{info}</Info>}
        </span>
        {chip}
      </div>
      <div className="hx-stat-value">
        <span className="hx-stat-num" style={!missing && accent ? { color: accent } : undefined}>{missing ? '—' : value}</span>
        {!missing && unit && <span className="hx-stat-unit">{unit}</span>}
      </div>
      <span className="hx-stat-sub">{missing ? (empty ?? 'Nothing recorded yet') : sub}</span>
    </div>
  );
}

// Signed change against a reference, coloured by whether that direction is good for
// this metric rather than by its sign.
export function Delta({ value, reference, goodWhen, unit = '', digits = 0 }: {
  value?: number | null;
  reference?: number | null;
  goodWhen: 'higher' | 'lower';
  unit?: string;
  digits?: number;
}) {
  if (value == null || reference == null) return <span className="hx-delta flat">no comparison yet</span>;
  const diff = value - reference;
  if (Math.abs(diff) < Math.pow(10, -digits) / 2) return <span className="hx-delta flat">same as usual</span>;
  const good = goodWhen === 'higher' ? diff > 0 : diff < 0;
  return (
    <span className={`hx-delta ${good ? 'good' : 'bad'}`}>
      {diff > 0 ? '+' : '−'}{Math.abs(diff).toFixed(digits)}{unit} vs usual
    </span>
  );
}

// ── The dashboard pieces ─────────────────────────────────────────────────────
//
// A tile answers "what is this number". A panel answers "how is this part of me
// doing", which needs a heading, a headline figure and its own small chart. The
// difference is why the Today tab could not be built out of Stat alone.

export function Panel({ title, icon, note, right, info, className = '', children }: {
  title: string;
  icon?: ReactNode;
  note?: ReactNode;
  right?: ReactNode;
  info?: ReactNode;
  className?: string;
  children: ReactNode;
}) {
  return (
    <section className={`hx-panel ${className}`}>
      <header className="hx-panel-head">
        {icon && <span className="hx-panel-icon" aria-hidden="true">{icon}</span>}
        <h3>{title}</h3>
        {info && <Info>{info}</Info>}
        {note && <span className="hx-panel-note">{note}</span>}
        {right && <span className="hx-panel-right">{right}</span>}
      </header>
      {children}
    </section>
  );
}

// A chart small enough to read at a glance and too small to read precisely, which is
// the point: it shows shape, and the number beside it carries the value. No axes, no
// grid, no tooltip -- anything that needs those belongs in a real chart on its own tab.
export function Spark({ data, color = 'var(--hx-1)', kind = 'line', height = 34 }: {
  data: (number | null)[];
  color?: string;
  kind?: 'line' | 'bar';
  height?: number;
}) {
  const points = data.filter((v): v is number => v != null);
  if (points.length < 2) return <span className="hx-spark-empty" style={{ height }}>not enough yet</span>;

  const min = Math.min(...points);
  const max = Math.max(...points);
  const span = max - min || 1;
  const w = 100;
  const step = w / (data.length - 1);

  if (kind === 'bar') {
    const barW = Math.max(1.5, step * 0.55);
    return (
      <svg className="hx-spark" viewBox={`0 0 ${w} ${height}`} height={height} preserveAspectRatio="none" aria-hidden="true">
        {data.map((v, i) => {
          if (v == null) return null;
          const h = Math.max(1.5, ((v - min) / span) * (height - 3) + 1.5);
          return <rect key={i} x={i * step - barW / 2} y={height - h} width={barW} height={h} rx={0.8} fill={color} opacity={0.85}/>;
        })}
      </svg>
    );
  }

  const d = data
    .map((v, i) => (v == null ? null : `${i * step},${height - 2 - ((v - min) / span) * (height - 4)}`))
    .filter(Boolean)
    .join(' L ');

  return (
    <svg className="hx-spark" viewBox={`0 0 ${w} ${height}`} height={height} preserveAspectRatio="none" aria-hidden="true">
      <path d={`M ${d}`} fill="none" stroke={color} strokeWidth={1.6} strokeLinecap="round" strokeLinejoin="round"
            vectorEffect="non-scaling-stroke"/>
    </svg>
  );
}

// The right-hand rail: one measurement, its trend, and a word for how it is going.
// The word is required rather than optional -- a green line means nothing on its own,
// and a rail of coloured lines is decoration pretending to be information.
export function RailCard({ label, icon, value, unit, chip, sub, spark, empty, info }: {
  label: string;
  icon?: ReactNode;
  value?: string | number | null;
  unit?: string;
  chip?: ReactNode;
  sub?: ReactNode;
  spark?: ReactNode;
  empty?: string;
  info?: ReactNode;
}) {
  const missing = value == null || value === '';
  return (
    <div className={`hx-rail-card ${missing ? 'is-empty' : ''}`}>
      <div className="hx-rail-head">
        {icon && <span className="hx-rail-icon" aria-hidden="true">{icon}</span>}
        <span className="hx-rail-label">{label}{info && <Info>{info}</Info>}</span>
        {chip}
      </div>
      <div className="hx-rail-body">
        <div>
          <div className="hx-rail-value">
            <b>{missing ? '\u2014' : value}</b>
            {!missing && unit && <span>{unit}</span>}
          </div>
          <span className="hx-rail-sub">{missing ? (empty ?? 'Nothing yet') : sub}</span>
        </div>
        {!missing && spark && <div className="hx-rail-spark">{spark}</div>}
      </div>
    </div>
  );
}

// A row in a list of short statements -- an insight, a lab, a workout. Deliberately not
// a table: three of these read better as sentences than as cells.
export function Row({ icon, title, note, right, tone }: {
  icon?: ReactNode;
  title: ReactNode;
  note?: ReactNode;
  right?: ReactNode;
  tone?: 'good' | 'warn' | 'bad';
}) {
  return (
    <div className={`hx-row-item ${tone ? `tone-${tone}` : ''}`}>
      {icon && <span className="hx-row-icon" aria-hidden="true">{icon}</span>}
      <span className="hx-row-body">
        <b>{title}</b>
        {note && <span>{note}</span>}
      </span>
      {right && <span className="hx-row-right">{right}</span>}
    </div>
  );
}

export function Empty({ title, children }: { title: string; children?: ReactNode }) {
  return (
    <div className="hx-empty">
      <b>{title}</b>
      {children && <> {children}</>}
    </div>
  );
}
