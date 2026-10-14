import { useQuery } from '@tanstack/react-query';
import { API, get, useNamer, WhyLink } from './insightKit';
import type { Why } from './insightKit';

// Where to aim (Vitara ADR 0028): each measure with a healthy range from longevity research,
// the latest reading as a dot on it, and the research behind it one tap away. Next to your
// own normal, never instead of it: the normal says whether today is unusual for you; this
// says where people who live longer tend to sit.

export interface StriveRange {
  metric: string; label: string; unit: string | null;
  low: number | null; high: number | null; riskBelow: number | null; riskAbove: number | null;
  grade: string; gradeLabel: string; source: string; basis: string;
  latest: { day: string; value: number } | null;
  position: 'inside' | 'below' | 'above' | null; atRisk: boolean;
}
interface StriveSet {
  note: string; ranges: StriveRange[];
  missing: { metric: string; label: string; reason: string }[];
  personalOnlyNote: string;
}

export const useStrive = () =>
  useQuery({ queryKey: ['healthy-ranges'], queryFn: () => get<StriveSet>(`${API}/api/health/healthy-ranges`), staleTime: 10 * 60_000 });

export function striveText(r: StriveRange, fmt: (m: string, v: number | null | undefined, withUnit?: boolean) => string): string {
  if (r.low != null && r.high != null) return `${fmt(r.metric, r.low, false)}–${fmt(r.metric, r.high)}`;
  if (r.low != null) return `${fmt(r.metric, r.low)} or more`;
  return `under ${fmt(r.metric, r.high)}`;
}

export function StriveWhy({ r, fmt }: { r: StriveRange; fmt: (m: string, v: number | null | undefined, withUnit?: boolean) => string }) {
  return (
    <>
      <Gauge r={r} fmt={fmt} big />
      <p>{r.basis}</p>
      {r.latest && (
        <p className="vmi-muted">
          Your latest, {fmt(r.metric, r.latest.value)} on {r.latest.day},{' '}
          {r.position === 'inside' ? 'is inside it.' : r.position === 'below' ? 'is under it.' : 'is over it.'}
          {r.atRisk ? ' It is past the line where the research saw clearly higher risk: worth a conversation with your doctor.' : ''}
        </p>
      )}
      <p className="vmi-grade"><b>{r.grade}</b><span>{r.gradeLabel[0].toUpperCase() + r.gradeLabel.slice(1)}. {r.source}.</span></p>
      <p className="vmi-muted vmi-small">A direction to aim, not a diagnosis. Your own normal is still what every finding is measured against.</p>
    </>
  );
}

// The range as a band, the risk lines beyond it, the reading as a dot. Open bounds run to the edge.
function Gauge({ r, fmt, big = false }: { r: StriveRange; fmt: (m: string, v: number | null | undefined, withUnit?: boolean) => string; big?: boolean }) {
  const v = r.latest?.value;
  const pts = [r.low, r.high, r.riskBelow, r.riskAbove, v].filter((x): x is number => x != null);
  let lo = Math.min(...pts), hi = Math.max(...pts);
  const span = hi - lo || Math.abs(hi) || 1;
  lo -= span * 0.25; hi += span * 0.25;
  if (Math.min(...pts) >= 0) lo = Math.max(0, lo);
  const P = (x: number) => Math.max(0, Math.min(100, ((x - lo) / (hi - lo)) * 100));
  const bandL = r.low != null ? P(r.low) : 0;
  const bandR = r.high != null ? P(r.high) : 100;
  const off = r.position !== null && r.position !== 'inside';

  return (
    <div className={`vmi-gauge ${big ? 'is-big' : ''}`} role="img"
         aria-label={`Strive for ${striveText(r, fmt)}${v != null ? `; latest ${fmt(r.metric, v)}` : ''}`}>
      <i className="vmi-gauge-track" />
      {r.riskBelow != null && <i className="vmi-gauge-risk" style={{ left: 0, width: `${P(r.riskBelow)}%` }} />}
      {r.riskAbove != null && <i className="vmi-gauge-risk" style={{ left: `${P(r.riskAbove)}%`, right: 0 }} />}
      <i className="vmi-gauge-band" style={{ left: `${bandL}%`, width: `${Math.max(2, bandR - bandL)}%` }} />
      {v != null && <b className={`vmi-gauge-dot ${r.atRisk ? 'is-risk' : off ? 'is-off' : ''}`} style={{ left: `calc(${P(v)}% - 6px)` }} />}
      {big && <>
        {r.low != null && <span className="vmi-gauge-tick" style={{ left: `${bandL}%` }}>{fmt(r.metric, r.low, false)}</span>}
        {r.high != null && <span className="vmi-gauge-tick" style={{ left: `${bandR}%` }}>{fmt(r.metric, r.high, false)}</span>}
      </>}
    </div>
  );
}

export function StriveCard({ why }: { why: (w: Why) => void }) {
  const q = useStrive();
  const { fmt } = useNamer();
  if (!q.data) return null;

  // Measured ones first, those past a risk line leading; unmeasured ones listed after, as
  // what you could add.
  const measured = q.data.ranges.filter(r => r.latest)
    .sort((a, b) => Number(b.atRisk) - Number(a.atRisk) || Number(b.position !== 'inside') - Number(a.position !== 'inside'));
  const unmeasured = q.data.ranges.filter(r => !r.latest);
  const inside = measured.filter(r => r.position === 'inside').length;

  return (
    <section className="vm-tile" aria-label="Where to aim">
      <h2 className="vmi-h2"><span>Where to aim</span><em>{inside} of {measured.length} in range</em></h2>
      <div className="vmi-strive">
        {measured.map(r => (
          <button key={r.metric} type="button" className="vmi-strive-row" onClick={() => why({ title: r.label, body: <StriveWhy r={r} fmt={fmt} /> })}>
            <span className="vmi-strive-name">{r.label}<small>strive for {striveText(r, fmt)}</small></span>
            <Gauge r={r} fmt={fmt} />
            <span className={`vmi-strive-val ${r.atRisk ? 'is-risk' : r.position !== 'inside' ? 'is-off' : ''}`}>
              {fmt(r.metric, r.latest!.value, false)}<small>{r.grade}</small>
            </span>
          </button>
        ))}
      </div>
      {unmeasured.length > 0 && (
        <p className="vmi-muted vmi-small vmi-strive-more">
          Not measured yet: {unmeasured.map(r => r.label).join(', ')}.
        </p>
      )}
      {q.data.missing.length > 0 && (
        <p className="vmi-muted vmi-small">Add your age and sex to the profile to see {q.data.missing.map(m => m.label).join(', ')}.</p>
      )}
      <div className="vmi-keys vmi-keys-wrap">
        <span><i className="k-band" />strive for</span><span><i className="k-risk" />higher risk</span><span><b className="k-grade">A–C</b>evidence</span>
      </div>
      <WhyLink onClick={() => why({
        title: 'Where to aim',
        body: <>
          <p>{q.data!.note}</p>
          <p className="vmi-muted">Each range comes from the study named on it. The letter is how strong that evidence is: A when trials or large meta-analyses agree, B for consistent large observational studies, C when it is suggestive only.</p>
          <p className="vmi-muted">{q.data!.personalOnlyNote} That is why HRV, skin temperature and breathing rate are not here.</p>
        </>,
      })} />
    </section>
  );
}
