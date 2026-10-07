import { RangeGauge } from './InsightCharts';
import { Chip } from './HealthKit';

// Recent results as a shelf of small cards, each result sitting on its own reference range.
//
// A list of "9.4 µIU/mL" only says what the number is. Laid on its range, it says whether the
// number is near the edge, well inside, or well past it, which is the thing a person actually
// wants from a result. A result with no range is shown as a number and says so, rather than
// being given a made-up scale.

export interface ShelfReading {
  id: string;
  metric: string;
  label: string;
  value: number;
  unit: string;
  at: string;
  signature?: string | null;
}

export interface ShelfAnalyte {
  key: string;
  label: string;
  unit: string;
  range: { low: number | null; high: number | null } | null;
}

const round = (n: number) => String(Math.round(n * 100) / 100);

export function LabShelf({ readings, analytes }: { readings: ShelfReading[]; analytes: ShelfAnalyte[] }) {
  const byKey = new Map(analytes.map(a => [a.key, a]));

  return (
    <div className="vx-shelf">
      {readings.map(r => {
        const a = byKey.get(r.metric);
        const range = a?.range && (a.range.low != null || a.range.high != null) ? a.range : null;
        const outside = range && ((range.low != null && r.value < range.low) || (range.high != null && r.value > range.high));
        const unit = r.unit || a?.unit || '';

        return (
          <div key={r.id} className={`vx-shelf-card ${outside ? 'is-out' : ''}`}>
            <div className="vx-shelf-head">
              {/* The catalogue's own label first: a stored key like fasting_insulin is a column name. */}
              <b>{a?.label ?? r.label}</b>
              {range && (outside ? <Chip tone="warn">{r.value < (range.low ?? -Infinity) ? 'below range' : 'above range'}</Chip> : <Chip tone="good">in range</Chip>)}
            </div>
            <div className="vx-shelf-value">
              <span>{round(r.value)}</span>
              {unit && <small>{unit}</small>}
            </div>
            {range
              ? <RangeGauge reading={{ value: r.value, low: range.low, high: range.high, unit }} />
              : <p className="vx-shelf-none">No reference range is recorded for this one.</p>}
            <p className="vx-shelf-when">{r.at}{r.signature ? ` · ${r.signature}` : ''}</p>
          </div>
        );
      })}
    </div>
  );
}
