import { useEffect, useState } from 'react';
import type { ReactNode } from 'react';

// Three concentric rings: sleep outermost, recovery in the middle, activity inside.
//
// Each ring is a score out of 100 as the ring reports it, drawn as an arc. A missing score is an
// empty dashed track, never a ring of zero: a night nobody wore the ring is not a night of
// score nothing. The arcs draw in once on mount (and re-draw when a score changes), unless the
// person has asked their device for reduced motion.

export interface RingSpec {
  key: string;
  label: string;
  score: number | null | undefined;
  color: string;
  word?: string;
}

const clamp = (n: number) => Math.max(0, Math.min(100, n));

export function TripleRing({ rings, size = 244, center, onPick }: {
  rings: RingSpec[];
  size?: number;
  center?: ReactNode;
  onPick?: (key: string) => void;
}) {
  // Start empty and fill on the next frame, so the transition has somewhere to start from.
  const [drawn, setDrawn] = useState(false);
  useEffect(() => {
    const id = requestAnimationFrame(() => setDrawn(true));
    return () => cancelAnimationFrame(id);
  }, []);

  const stroke = Math.round(size * 0.075);
  const gap = Math.round(size * 0.028);
  const c = size / 2;

  return (
    <div className="vm-rings" style={{ width: size, height: size }}>
      <svg width={size} height={size} role="img"
           aria-label={rings.map(r => `${r.label} ${r.score == null ? 'not measured' : Math.round(r.score)}`).join(', ')}>
        {rings.map((r, i) => {
          const rad = c - stroke / 2 - 2 - i * (stroke + gap);
          const circ = 2 * Math.PI * rad;
          const has = r.score != null;
          const len = has ? (clamp(r.score as number) / 100) * circ : 0;
          return (
            <g key={r.key} className="vm-ring" onClick={() => onPick?.(r.key)} style={{ cursor: onPick ? 'pointer' : undefined }}>
              <circle cx={c} cy={c} r={rad} fill="none" strokeWidth={stroke}
                      className={has ? 'vm-ring-track' : 'vm-ring-track is-empty'} />
              {has && (
                <circle
                  cx={c} cy={c} r={rad} fill="none" strokeWidth={stroke} strokeLinecap="round"
                  stroke={r.color}
                  strokeDasharray={`${drawn ? len.toFixed(1) : 0} ${circ.toFixed(1)}`}
                  transform={`rotate(-90 ${c} ${c})`}
                  className="vm-ring-arc"
                />
              )}
            </g>
          );
        })}
      </svg>
      <div className="vm-rings-mid">{center}</div>
    </div>
  );
}
