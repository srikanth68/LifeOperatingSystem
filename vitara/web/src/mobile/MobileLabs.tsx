import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { moduleApi } from '../services/apiHost';
import { vitaraHeaders } from '../services/profile';
import { formatDay } from '../services/timezone';
import { clamp } from './motion';
import { OutOfReach } from './Parts';

// Labs, the phone screen: the latest blood draw, each result laid on its reference range.
//
// Labs are the one place Vitara compares you with a published range rather than with your own
// history (two draws a year are far too few to learn a personal normal from), and this screen
// says so. A result is in range, outside it, or has no range recorded; there is no invented
// "watch" tier, because nothing in the data says where one would begin. Coral is used for
// outside-range results and nothing else in the app.

const VITARA = moduleApi(5100);
const get = <T,>(url: string): Promise<T> =>
  fetch(url, { headers: vitaraHeaders() }).then(r => {
    if (!r.ok) throw new Error(String(r.status));
    return r.json() as Promise<T>;
  });

type Standing = 'below' | 'within' | 'above' | 'unknown';
interface Range { low: number | null; high: number | null; band: string; notes: string | null }
interface Result { metric: string; label: string; value: number; unit: string; standing: Standing; standingText: string; range: Range | null; previous: number | null; change: number | null }
interface Derived { metric: string; label: string; value: number; unit: string; standing: Standing; standingText: string; gradeLabel: string }
interface Gap { metric: string; label: string; reason: string; missing: { key: string; label: string }[] }
interface Panel { id: string; drawnOn: string; daysAgo: number; labName: string | null; fasting: boolean | null; results: Result[]; derived: Derived[]; gaps: Gap[] }

const STATUS: Record<'in' | 'out' | 'none', { word: string; color: string; tint: string; shape: string }> = {
  in:   { word: 'In range',     color: 'var(--teal)',  tint: 'var(--tint-t)', shape: 'circle' },
  out:  { word: 'Outside range', color: 'var(--coral)', tint: 'var(--tint-c)', shape: 'square' },
  none: { word: 'No range',     color: 'var(--t2)',    tint: 'var(--s3)',     shape: 'ring' },
};
const kind = (s: Standing) => (s === 'within' ? 'in' : s === 'unknown' ? 'none' : 'out');
const fmtNum = (n: number) => String(Math.round(n * 100) / 100);

export function MobileLabs({ enter }: { enter: () => void }) {
  const q = useQuery<Panel[]>({ queryKey: ['labs'], queryFn: () => get(`${VITARA}/api/labs`) });
  const [pick, setPick] = useState(0);

  const panels = [...(q.data ?? [])].sort((a, b) => b.drawnOn.localeCompare(a.drawnOn));

  if (q.isPending) return <div className="vm-skel" aria-busy="true" />;
  if (q.isError && !q.data) return <OutOfReach onRetry={() => { void q.refetch(); }} />;

  if (panels.length === 0) {
    return (
      <div className="vm-screen">
        <div className="vm-tile vm-dashed">
          <b className="vm-card-title">No blood work yet</b>
          <p className="vm-fine">
            Labs are the one place this compares you with a published range rather than with your own history, because
            two draws a year are far too few to learn a personal normal from.
          </p>
        </div>
        <button type="button" className="vm-cta" onClick={enter}>Enter a draw</button>
      </div>
    );
  }

  const p = panels[Math.min(pick, panels.length - 1)];
  const counts = { in: 0, out: 0, none: 0 };
  p.results.forEach(r => { counts[kind(r.standing)] += 1; });

  return (
    <div className="vm-screen">
      <div>
        <p className="vm-label-s">
          Drawn {formatDay(p.drawnOn, { day: 'numeric', month: 'short', year: 'numeric' })} · {p.results.length} marker{p.results.length === 1 ? '' : 's'}
        </p>
        <p className="vm-fine" style={{ marginTop: 4 }}>
          {[p.labName, p.fasting === true ? 'fasting' : p.fasting === false ? 'not fasting' : null, p.daysAgo === 0 ? 'today' : `${p.daysAgo} days ago`].filter(Boolean).join(' · ')}
        </p>
      </div>

      {panels.length > 1 && (
        <div className="vm-chips" role="tablist" aria-label="Blood draws">
          {panels.map((x, i) => (
            <button key={x.id} role="tab" aria-selected={i === pick} className={i === pick ? 'is-on' : ''} onClick={() => setPick(i)}>
              {formatDay(x.drawnOn, { day: 'numeric', month: 'short' })}
            </button>
          ))}
        </div>
      )}

      <div className="vm-statuses">
        {(['in', 'out', 'none'] as const).filter(k => counts[k] > 0).map(k => (
          <span key={k} style={{ background: STATUS[k].tint, color: STATUS[k].color }}>
            <i className={`mark-${STATUS[k].shape}`} style={{ background: STATUS[k].shape === 'ring' ? 'transparent' : STATUS[k].color, borderColor: STATUS[k].color }} aria-hidden="true" />
            {STATUS[k].word} · {counts[k]}
          </span>
        ))}
      </div>

      <div className="vm-lab-list">
        {p.results.map(r => <LabCard key={r.metric} r={r} />)}
      </div>

      <div className="vm-key-line">
        <span><i className="mark-dot" aria-hidden="true" />this draw</span>
        <span><i className="mark-hollow" aria-hidden="true" />previous draw</span>
      </div>

      {p.derived.length > 0 && (
        <section>
          <p className="vm-label"><span>Worked out from this draw</span></p>
          <div className="vm-list">
            {p.derived.map(d => {
              const s = STATUS[kind(d.standing)];
              return (
                <div key={d.metric} className="vm-list-row">
                  <div>
                    <b>{d.label}</b>
                    <small>{d.standingText} · {d.gradeLabel}</small>
                  </div>
                  <span style={{ color: 'var(--t1)', fontWeight: 600 }}>
                    {fmtNum(d.value)} <small style={{ display: 'inline', color: 'var(--t2)', fontWeight: 400 }}>{d.unit}</small>
                    <small style={{ color: s.color }}>{s.word}</small>
                  </span>
                </div>
              );
            })}
          </div>
        </section>
      )}

      {p.gaps.length > 0 && (
        <details className="vm-more-box">
          <summary>What this draw could not answer</summary>
          {p.gaps.map(g => (
            <p key={g.metric}><b>{g.label}.</b> {g.reason}{g.missing.length > 0 ? ` Needs ${g.missing.map(m => m.label).join(', ')}.` : ''}</p>
          ))}
        </details>
      )}

      <button type="button" className="vm-row-link" onClick={enter}>
        <span>Enter a draw</span><em aria-hidden="true">›</em>
      </button>
    </div>
  );
}

// One result on its range. Zones are drawn only from the range the data carries: a range with
// only an upper limit gets two zones, never an invented lower one.
function LabCard({ r }: { r: Result }) {
  const k = kind(r.standing);
  const s = STATUS[k];
  const lo0 = r.range?.low ?? null, hi0 = r.range?.high ?? null;
  const hasRange = lo0 != null || hi0 != null;

  let axis: { min: number; max: number } | null = null;
  if (hasRange) {
    const span = lo0 != null && hi0 != null ? hi0 - lo0 : (hi0 ?? lo0 ?? 1);
    const values = [r.value, r.previous].filter((v): v is number => v != null);
    const min = Math.min(lo0 != null ? lo0 - span * 0.6 : 0, ...values) ;
    const max = Math.max(hi0 != null ? hi0 + span * 0.6 : (lo0 as number) + span, ...values);
    axis = { min: Math.max(0, min - span * 0.05), max: max + span * 0.05 };
  }
  const pos = (v: number) => (axis ? clamp((v - axis.min) / (axis.max - axis.min)) * 100 : 0);

  const zones = axis
    ? [
        ...(lo0 != null ? [{ w: pos(lo0), bad: true }] : []),
        { w: pos(hi0 ?? axis.max) - (lo0 != null ? pos(lo0) : 0), bad: false },
        ...(hi0 != null ? [{ w: 100 - pos(hi0), bad: true }] : []),
      ]
    : [];
  const note = [r.standingText, r.change != null ? `${r.change > 0 ? '+' : ''}${fmtNum(r.change)} since the previous draw` : null].filter(Boolean).join(' · ');

  return (
    <article className="vm-lab">
      <div className="vm-lab-head">
        <div>
          <b>{r.label}</b>
          <div className="vm-lab-value"><span>{fmtNum(r.value)}</span><small>{r.unit}</small></div>
        </div>
        <span className="vm-status" style={{ background: s.tint, color: s.color }}>
          <i className={`mark-${s.shape}`} style={{ background: s.shape === 'ring' ? 'transparent' : s.color, borderColor: s.color }} aria-hidden="true" />
          {s.word}
        </span>
      </div>

      {axis ? (
        <div className="vm-zone" aria-hidden="true">
          <div className="vm-zone-bar">
            {zones.map((z, i) => <i key={i} style={{ flex: `${Math.max(z.w, 0.5)} 0 0`, background: z.bad ? 'var(--coral)' : 'var(--teal)', opacity: z.bad === (k === 'out') ? 0.6 : 0.22 }} />)}
          </div>
          {r.previous != null && <i className="vm-zone-prev" style={{ left: `${pos(r.previous)}%` }} />}
          <i className="vm-zone-now" style={{ left: `${pos(r.value)}%` }} />
          {lo0 != null && <span style={{ left: `${pos(lo0)}%` }}>{fmtNum(lo0)}</span>}
          {hi0 != null && <span style={{ left: `${pos(hi0)}%` }}>{fmtNum(hi0)}</span>}
        </div>
      ) : (
        <p className="vm-fine" style={{ margin: 0 }}>No reference range is recorded for this one, so it is shown as a number and nothing more.</p>
      )}
      {note && <p className="vm-fine" style={{ margin: 0 }}>{note}</p>}
    </article>
  );
}
