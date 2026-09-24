import { useMemo, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { authHeaders } from '../services/auth';
import { moduleApi } from '../services/apiHost';
import { Info } from './health/HealthKit';

// Everything Vitara can know about you, including the parts it doesn't know yet.
//
// The rest of this module can only show a metric that happens to have data, so an empty
// panel reads as "not supported" rather than "you haven't recorded any". This page is the
// opposite promise: one card per metric, always, each saying plainly whether it is
// current, stale, still learning your normal, or empty — and what would fill it.
//
// Written for someone who has just arrived: every card explains in one sentence what the
// number is and why it is worth having, with no jargon left unexplained.
const INSIGHT = moduleApi(5110);

interface MetricRow {
  key: string;
  label: string;
  unit: string;
  group: string;
  tier: string;
  source: 'ring' | 'phone' | 'manual' | 'lab' | 'computed';
  what: string;
  decimals: number;
  polarity: number;
  state: 'current' | 'stale' | 'no_data';
  latest: { value: number; day: string; daysAgo: number } | null;
  readings: number;
  staleAfterDays: number;
  baseline: {
    state: 'ready' | 'learning' | 'none';
    signature: string;
    median: number;
    p25: number;
    p75: number;
    n: number;
    needs: number;
    windowDays: number;
    regimeStart: string | null;
    exclusions: string | null;
  } | null;
  fillWith: string;
}

interface CatalogueResult {
  today: string;
  computedThrough: string | null;
  minReadingsForBaseline: number;
  groups: string[];
  metrics: MetricRow[];
}

const SOURCE_LABEL: Record<MetricRow['source'], string> = {
  ring: 'Ring',
  phone: 'Phone',
  manual: 'You',
  lab: 'Lab',
  computed: 'Worked out',
};

// A colour per group, so a long scroll reads as sections rather than as one list. It is
// decoration: state is still said in words, and these are the series palette rather than
// the status one so a colour can never be mistaken for a verdict.
const GROUP_ACCENT: Record<string, string> = {
  Sleep: 'var(--hx-2)',
  Recovery: 'var(--hx-5)',
  Activity: 'var(--hx-1)',
  Body: 'var(--hx-4)',
  Vitals: 'var(--hx-3)',
  Labs: 'var(--hx-6)',
};

const GROUP_BLURB: Record<string, string> = {
  Sleep: 'How long and how well you slept, from the ring on your finger.',
  Recovery: 'What your body did overnight — the earliest signs of illness, stress or a hard day.',
  Activity: 'Movement and training, and whether you ramped up faster than you adapted.',
  Body: 'The slow-moving numbers: weight, shape, and fitness age.',
  Vitals: 'Readings you take yourself, with the context that changes them.',
  Labs: 'Blood work. Rare, exact, and the anchor everything else is checked against.',
};

// Minutes and seconds are stored raw and read badly raw: "392 min" is arithmetic, not an
// answer.
function display(row: MetricRow, value: number): string {
  if (row.unit === 'min') {
    const h = Math.floor(Math.abs(value) / 60);
    const m = Math.round(Math.abs(value) % 60);
    const sign = value < 0 ? '−' : '';
    return h ? `${sign}${h}h ${m}m` : `${sign}${m}m`;
  }
  if (row.unit === 's') return `${Math.round(value / 60)}m`;
  if (row.unit === 'steps' || row.unit === 'kcal') return Math.round(value).toLocaleString();
  const n = value.toFixed(row.decimals);
  return row.decimals === 0 ? Number(n).toLocaleString() : n;
}

function unitSuffix(row: MetricRow): string {
  if (['min', 's', 'steps', 'kcal', ''].includes(row.unit)) return '';
  if (row.unit === '/100' || row.unit === '%') return row.unit;
  return ` ${row.unit}`;
}

function agoText(daysAgo: number): string {
  if (daysAgo <= 0) return 'today';
  if (daysAgo === 1) return 'yesterday';
  if (daysAgo < 14) return `${daysAgo} days ago`;
  if (daysAgo < 60) return `${Math.round(daysAgo / 7)} weeks ago`;
  return `${Math.round(daysAgo / 30)} months ago`;
}

function MetricCard({ row, accent }: { row: MetricRow; accent: string }) {
  const b = row.baseline;

  return (
    <li className={`vm-card vm-${row.state}`} style={{ ['--tile' as string]: accent }}>
      <div className="vm-card-top">
        <span className="vm-card-label">
          {row.label}
          <Info label={`What ${row.label} is`}>{row.what}</Info>
        </span>
        <span className={`vm-source vm-source-${row.source}`}>{SOURCE_LABEL[row.source]}</span>
      </div>

      {row.latest ? (
        <div className="vm-value">
          <span className="vm-number" style={{ color: accent }}>{display(row, row.latest.value)}</span>
          <span className="vm-unit">{unitSuffix(row)}</span>
          <span className={`vm-when ${row.state === 'stale' ? 'is-stale' : ''}`}>
            {row.state === 'stale' ? `last seen ${agoText(row.latest.daysAgo)}` : agoText(row.latest.daysAgo)}
          </span>
        </div>
      ) : (
        // Never a blank space. An empty metric says it is empty and says what fills it.
        <div className="vm-value vm-empty">
          <span className="vm-number">—</span>
          <span className="vm-when">Nothing recorded yet</span>
        </div>
      )}

      <div className="vm-normal">
        {b?.state === 'ready' ? (
          <>Your usual: <b>{display(row, b.p25)} – {display(row, b.p75)}</b><span> (typically {display(row, b.median)})</span></>
        ) : b?.state === 'learning' ? (
          <>
            <span className="vm-learning-bar" aria-hidden="true">
              <span style={{ width: `${Math.min(100, (b.n / (b.n + b.needs)) * 100)}%`, background: accent }} />
            </span>
            Learning your usual range — {b.n} of {b.n + b.needs} readings
          </>
        ) : row.latest ? (
          <>No usual range yet — that needs steady readings over about two months</>
        ) : (
          <>{row.fillWith}</>
        )}
      </div>

      {b?.regimeStart && (
        <p className="vm-note">Your normal was rebuilt from {b.regimeStart}, after a lasting change.</p>
      )}
      {b?.exclusions?.includes('unadopted_regime_shift') && (
        <p className="vm-note vm-note-warn">
          This moved to a new level and nothing recorded explains it, so your earlier normal is being kept.
        </p>
      )}
    </li>
  );
}

type Filter = 'all' | 'current' | 'learning' | 'empty';

export function VitaraMetricsCatalogue() {
  const [filter, setFilter] = useState<Filter>('all');

  const { data, isPending, isError, error, refetch } = useQuery<CatalogueResult>({
    queryKey: ['vitara-metric-catalogue'],
    queryFn: async () => {
      const res = await fetch(`${INSIGHT}/api/health/metrics`, { headers: authHeaders() });
      if (!res.ok) throw new Error(`${res.status}`);
      return res.json();
    },
    refetchInterval: 5 * 60_000,
  });

  const counts = useMemo(() => {
    const m = data?.metrics ?? [];
    return {
      all: m.length,
      current: m.filter(r => r.state === 'current').length,
      learning: m.filter(r => r.baseline?.state === 'learning' || (r.latest && r.baseline?.state !== 'ready')).length,
      empty: m.filter(r => r.state === 'no_data').length,
      stale: m.filter(r => r.state === 'stale').length,
    };
  }, [data]);

  const shown = useMemo(() => {
    const m = data?.metrics ?? [];
    if (filter === 'current') return m.filter(r => r.state === 'current');
    if (filter === 'learning') return m.filter(r => r.baseline?.state === 'learning' || (r.latest && r.baseline?.state !== 'ready'));
    if (filter === 'empty') return m.filter(r => r.state === 'no_data');
    return m;
  }, [data, filter]);

  if (isPending) return <div className="vm-page"><p className="vm-lede">Checking what has arrived…</p></div>;

  if (isError || !data) {
    return (
      <div className="vm-page">
        <div className="vm-error">
          <p><b>Can't reach the analysis service.</b> Nothing has been checked, which is not the same as nothing being wrong.</p>
          <p className="vm-error-detail">{String(error ?? 'no response')}</p>
          <button className="btn-ghost" onClick={() => refetch()}>Try again</button>
        </div>
      </div>
    );
  }

  const filters: { id: Filter; label: string; count: number }[] = [
    { id: 'all', label: 'Everything', count: counts.all },
    { id: 'current', label: 'Up to date', count: counts.current },
    { id: 'learning', label: 'Still learning', count: counts.learning },
    { id: 'empty', label: 'Nothing yet', count: counts.empty },
  ];

  return (
    <div className="vm-page">
      <header className="vm-head">
        <div>
          <h2 className="vm-title">Everything we track</h2>
          <p className="vm-lede">
            One card per measurement, whether or not you have any yet. {counts.current} up to date,
            {' '}{counts.stale} gone quiet, {counts.empty} not started.
            {data.computedThrough
              ? ` Your usual ranges were worked out through ${data.computedThrough}.`
              : ' Your usual ranges have not been worked out yet — that starts once readings arrive.'}
          </p>
        </div>
      </header>

      {/* One filter row above everything it scopes, rather than a control per section. */}
      <div className="vm-filters" role="tablist" aria-label="Filter metrics">
        {filters.map(f => (
          <button
            key={f.id}
            role="tab"
            aria-selected={filter === f.id}
            className={`vm-filter ${filter === f.id ? 'active' : ''}`}
            onClick={() => setFilter(f.id)}
          >
            {f.label} <span className="vm-filter-count">{f.count}</span>
          </button>
        ))}
      </div>

      {data.groups.map(group => {
        const rows = shown.filter(r => r.group === group);
        if (rows.length === 0) return null;

        return (
          <section key={group} className="vm-group">
            <div className="vm-group-head">
              <h3 style={{ color: GROUP_ACCENT[group] ?? 'var(--vm-accent)' }}>{group}</h3>
              <p>{GROUP_BLURB[group] ?? ''}</p>
            </div>
            <ul className="vm-grid">
              {rows.map(r => (
                <MetricCard key={`${r.key}:${r.baseline?.signature ?? ''}`} row={r} accent={GROUP_ACCENT[group] ?? 'var(--hx-1)'} />
              ))}
            </ul>
          </section>
        );
      })}

      {shown.length === 0 && (
        <p className="vm-lede">Nothing in this group — try another filter.</p>
      )}

      <p className="vm-foot">
        A range is your own, not a population average: it comes from your last {data.metrics[0]?.baseline?.windowDays ?? 60} days,
        with illness, travel and device changes left out, and needs {data.minReadingsForBaseline} readings before it means anything.
      </p>
    </div>
  );
}
