import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { vitaraHeaders } from '../services/profile';
import { todayInTz } from '../services/timezone';
import { API, VITARA, get, useNamer, WhyLink, f1, lc } from './insightKit';
import type { Why, Correlation } from './insightKit';

// Insight › Patterns on the phone (canvas board: Patterns). Three questions, each a picture:
// which measures are moving as a group, what goes with what in your own days, and whether
// something you started actually did anything.

// ── Types ───────────────────────────────────────────────────────────────────

interface PatternComponent { metric: string; label: string; movement: 'unmeasured' | 'steady' | 'favourable' | 'unfavourable'; detail: string; counts: boolean }
interface PatternRow {
  key: string; title: string; statement: string; interpretation: string; fires: boolean; heldBecause: string | null;
  moving: number; counted: number; unmeasured: number; components: PatternComponent[];
  wouldHelp: { metric: string; label: string; detail: string }[];
}
interface PatternScan { patterns: PatternRow[]; evidence: { grade: string; label: string; claim: string; caveat: string } | null }

interface Range { median: number; n: number; p25?: number; p75?: number }
interface Evaluated {
  id: string; name: string; kind: string; startedOn: string; running: boolean; daysIn: number;
  targetMetric: string | null; targetLabel: string | null; verdict: string; statement: string; confidence: string;
  caveats: string[]; before: Range | null; after: Range | null; change: number | null;
  rebound: { comparableWindows: number; median: number; p75: number } | null;
}
interface InterventionScan { runInDays: number; windowDays: number; interventions: Evaluated[]; evidence: { label: string; claim: string; caveat: string } | null }
interface Target { key: string; label: string; unit: string; group: string; better: string }

// ── Moving together ─────────────────────────────────────────────────────────

const BEAD: Record<PatternComponent['movement'], string> = {
  unfavourable: 'b-bad', favourable: 'b-good', steady: 'b-steady', unmeasured: 'b-none',
};

function Beads({ p }: { p: PatternRow }) {
  const n = p.components.length;
  return (
    <svg width="120" height="120" viewBox="0 0 120 120" role="img"
         aria-label={`${p.title}: ${p.moving} of ${p.counted} measured moving, ${p.unmeasured} not measured`}>
      <circle cx="60" cy="60" r="42" className="vmi-ring-thin" />
      {p.components.map((c, i) => {
        const a = -Math.PI / 2 + (i / Math.max(1, n)) * 2 * Math.PI;
        return <circle key={c.metric} cx={f1(60 + 42 * Math.cos(a))} cy={f1(60 + 42 * Math.sin(a))} r="9" className={BEAD[c.movement]} />;
      })}
      <text x="60" y="60" textAnchor="middle" className="vmi-bead-num">{p.moving}/{p.counted}</text>
      <text x="60" y="76" textAnchor="middle" className="vmi-t">moving</text>
    </svg>
  );
}

function Together({ why }: { why: (w: Why) => void }) {
  const q = useQuery({ queryKey: ['insight-patterns'], queryFn: () => get<PatternScan>(`${API}/api/health/patterns`) });
  if (!q.data) return <section className="vm-tile vmi-skel" aria-busy="true" />;
  const firing = q.data.patterns.filter(p => p.fires);
  const quiet = q.data.patterns.filter(p => !p.fires);
  const ev = q.data.evidence;

  return (
    <>
      {firing.map(p => (
        <section key={p.key} className="vm-tile vmi-together" aria-label="Moving together">
          <Beads p={p} />
          <div className="vmi-together-body">
            <p className="vmi-eyebrow vmi-gold">Moving together</p>
            <b className="vmi-together-title">{p.title}</b>
            <p className="vmi-muted vmi-small">{p.components.filter(c => c.movement === 'unfavourable').map(c => c.label).join(', ')}</p>
            <WhyLink onClick={() => why({
              title: p.title,
              body: <>
                <p>{p.statement}</p>
                <p className="vmi-muted">{p.interpretation}</p>
                <ul className="vmi-comp">
                  {p.components.map(c => <li key={c.metric}><i className={BEAD[c.movement]} /><span><b>{c.label}</b> {c.detail}</span></li>)}
                </ul>
                {ev && <p className="vmi-grade"><b>{ev.grade}</b><span>{ev.label}. {ev.caveat}</span></p>}
              </>,
            })} />
          </div>
        </section>
      ))}
      {quiet.map(p => (
        <button key={p.key} type="button" className="vmi-quiet" onClick={() => why({
          title: p.title,
          body: <>
            <p>{p.heldBecause ?? p.statement}</p>
            {p.wouldHelp.length > 0 && <>
              <p className="vmi-eyebrow">What would help</p>
              <ul className="vmi-comp">{p.wouldHelp.map(w => <li key={w.metric}><em>+</em><span><b>{w.label}</b> {w.detail}</span></li>)}</ul>
            </>}
          </>,
        })}>
          <i className="b-steady" />
          <span>{p.title}: nothing moving as a group{p.unmeasured ? ` · ${p.unmeasured} not measured` : ''}</span>
          <em>›</em>
        </button>
      ))}
    </>
  );
}

// ── What moves you: signed bars, strongest first ────────────────────────────

function Relations({ why }: { why: (w: Why) => void }) {
  const q = useQuery({ queryKey: ['insight-correlations'], queryFn: () => get<{ correlations: Correlation[]; caveat: string }>(`${API}/api/health/correlations`) });
  const { name, fmt } = useNamer();
  if (!q.data) return null;
  const list = [...q.data.correlations].sort((a, b) => Math.abs(b.rho) - Math.abs(a.rho)).slice(0, 8);

  return (
    <section className="vm-tile" aria-label="What moves you">
      <h2 className="vmi-h2"><span>What moves you</span><em>90 days, tested</em></h2>
      {list.length === 0 && <p className="vmi-muted vmi-small">Nothing has passed the test yet. A pair needs 30 days together and has to survive a correction for testing many pairs at once.</p>}
      <div className="vmi-rel">
        {list.map(c => {
          const w = Math.min(1, Math.abs(c.rho) / 0.8) * 50;
          const s = c.split;
          return (
            <button key={`${c.driver}-${c.outcome}-${c.lagDays}`} type="button" className="vmi-rel-row" onClick={() => why({
              title: `${name(c.driver)} → ${name(c.outcome)}`,
              body: <>
                {s
                  ? <div className="vmi-pair">
                      <div><small>{lc(name(c.driver))} ≤ {fmt(c.driver, s.cut)}</small><b>{fmt(c.outcome, s.low)}</b><small>{s.lowN} days</small></div>
                      <div><small>{lc(name(c.driver))} &gt; {fmt(c.driver, s.cut)}</small><b className="vmi-violet">{fmt(c.outcome, s.high)}</b><small>{s.highN} days</small></div>
                    </div>
                  : null}
                <p>{name(c.outcome)}{c.lagDays === 1 ? ' the next day' : ''} {c.rho > 0 ? 'rises' : 'falls'} with {lc(name(c.driver))}, over {c.n} paired days (ρ {c.rho > 0 ? '+' : '−'}{Math.abs(c.rho).toFixed(2)}).</p>
                <p className="vmi-muted">{q.data!.caveat}</p>
              </>,
            })}>
              <span className="vmi-rel-name">{name(c.driver)} <small>→ {lc(name(c.outcome))}{c.lagDays === 1 ? ' next day' : ''}</small></span>
              <span className="vmi-rel-bar" aria-hidden="true">
                <i className="vmi-rel-mid" />
                <i className={`vmi-rel-fill ${c.rho > 0 ? 'is-pos' : 'is-neg'}`} style={c.rho > 0 ? { left: '50%', width: `${w}%` } : { right: '50%', width: `${w}%` }} />
              </span>
              <b>{c.rho > 0 ? '+' : '−'}{Math.abs(c.rho).toFixed(2)}</b>
            </button>
          );
        })}
      </div>
      {list.length > 0 && <div className="vmi-row vmi-muted vmi-small"><span>← falls with it</span><span>rises with it →</span></div>}
      <WhyLink onClick={() => why({
        title: 'Together is not cause',
        body: <>
          <p>Each bar is a rank correlation over your last 90 days: how reliably two things moved together. Longer is more reliable, not bigger.</p>
          <p className="vmi-muted">{q.data!.caveat}</p>
          <p className="vmi-muted">To find out whether one causes the other, change it on purpose and record it under Did it work.</p>
        </>,
      })}>Together is not cause ›</WhyLink>
    </section>
  );
}

// ── Did it work ─────────────────────────────────────────────────────────────

const VERDICT: Record<string, { word: string; tone: string }> = {
  improved: { word: 'it helped', tone: 'good' },
  worsened: { word: 'it made it worse', tone: 'warn' },
  no_change: { word: 'no change', tone: 'steady' },
  would_have_anyway: { word: 'would have anyway', tone: 'steady' },
  too_early: { word: 'too early', tone: 'steady' },
  not_enough_data: { word: 'not enough data', tone: 'steady' },
  cannot_separate: { word: 'can’t separate', tone: 'steady' },
  no_direction: { word: 'no better direction', tone: 'steady' },
  no_target: { word: 'no target chosen', tone: 'steady' },
};

function Trial({ e, runIn, window: win, onStop, why }: {
  e: Evaluated; runIn: number; window: number; onStop: () => void; why: (w: Why) => void;
}) {
  const { fmt } = useNamer();
  const v = VERDICT[e.verdict] ?? { word: e.verdict.replace(/_/g, ' '), tone: 'steady' };
  const m = e.targetMetric ?? '';
  const f = (x: number) => (m ? fmt(m, x, false) : x.toFixed(0));

  // The time track: before | settling | after, with today's place on it.
  const W = 350, total = win + runIn + win;
  const X = (d: number) => (d / total) * W;
  const now = Math.min(total, win + e.daysIn);

  // The value strip, only once there is a before and an after.
  const vals = [e.before?.p25, e.before?.p75, e.before?.median, e.after?.p25, e.after?.p75, e.after?.median, e.rebound?.median, e.rebound?.p75]
    .filter((x): x is number => x != null);
  const lo = Math.min(...vals), hi = Math.max(...vals), pad = (hi - lo || 1) * 0.15;
  const V = (x: number) => 8 + ((x - (lo - pad)) / (hi - lo + 2 * pad)) * (W - 16);

  return (
    <div className="vmi-trial">
      <div className="vmi-row">
        <b className="vmi-finding-name">{e.name}</b>
        <span className={`vmi-tag t-${v.tone}`}>{v.word}</span>
      </div>
      <p className="vmi-muted vmi-small">{e.kind.replace('_', ' ')} · day {e.daysIn}{e.targetLabel ? ` · for ${lc(e.targetLabel)}` : ''}</p>
      <svg viewBox={`0 0 ${W} 44`} width="100%" height="44" role="img"
           aria-label={`Day ${e.daysIn}: ${win} days before, ${runIn} days settling, then ${win} days measured`}>
        <rect x="0" y="16" width={f1(X(win) - 4)} height="10" rx="5" className="vmi-seg-before" />
        <rect x={f1(X(win))} y="16" width={f1(X(runIn) - 4)} height="10" rx="5" className="vmi-seg-settle" />
        <rect x={f1(X(win + runIn))} y="16" width={f1(X(win))} height="10" rx="5" className="vmi-seg-after" />
        <rect x={f1(X(win + runIn))} y="16" width={f1(Math.max(0, X(now) - X(win + runIn)))} height="10" rx="5" className="vmi-seg-done" />
        {e.running && <line x1={f1(X(now))} x2={f1(X(now))} y1="10" y2="32" className="vmi-now" />}
        <text x={f1(X(win / 2))} y="40" textAnchor="middle" className="vmi-t">before {win} d</text>
        <text x={f1(X(win + runIn / 2))} y="40" textAnchor="middle" className="vmi-t vmi-t-dim">settling {runIn} d</text>
        <text x={f1(X(win + runIn + win / 2))} y="40" textAnchor="middle" className="vmi-t vmi-t-violet">after {win} d</text>
        {e.running && <text x={f1(Math.min(W - 18, Math.max(18, X(now))))} y="8" textAnchor="middle" className="vmi-t vmi-t-b">today</text>}
      </svg>
      {e.before && e.after && (
        <svg viewBox={`0 0 ${W} 64`} width="100%" height="64" role="img"
             aria-label={`${e.targetLabel ?? 'Target'}: before ${f(e.before.median)}, after ${f(e.after.median)}${e.rebound ? `, a usual bounce-back reaches ${f(e.rebound.median)} to ${f(e.rebound.p75)}` : ''}`}>
          <line x1="8" x2={W - 8} y1="26" y2="26" className="vmi-axis" />
          {e.rebound && <rect x={f1(V(e.rebound.median))} y="36" width={f1(Math.max(4, V(e.rebound.p75) - V(e.rebound.median)))} height="8" rx="4" className="vmi-rebound" />}
          <circle cx={f1(V(e.before.median))} cy="26" r="7" className="vmi-dot-before" />
          <circle cx={f1(V(e.after.median))} cy="26" r="7" className="vmi-dot-after" />
          <text x={f1(V(e.before.median))} y="12" textAnchor="middle" className="vmi-t">before {f(e.before.median)}</text>
          <text x={f1(V(e.after.median))} y="12" textAnchor="middle" className="vmi-t vmi-t-violet">after {f(e.after.median)}</text>
          {e.rebound && <text x={f1((V(e.rebound.median) + V(e.rebound.p75)) / 2)} y="60" textAnchor="middle" className="vmi-t vmi-t-b">usual bounce-back {f(e.rebound.median)}–{f(e.rebound.p75)}</text>}
        </svg>
      )}
      <div className="vmi-row">
        <WhyLink onClick={() => why({
          title: v.word === 'would have anyway' ? 'What “would have anyway” means' : e.name,
          body: <>
            <p>{e.statement}</p>
            {e.rebound && <p className="vmi-muted">Measured against {e.rebound.comparableWindows} earlier stretches of yours that were just as far off and came back on their own, to about {f(e.rebound.median)}. Only a change beyond that counts.</p>}
            <p className="vmi-muted">The first {runIn} days are not counted: anything new takes time to settle. Then {win} days are compared with the {win} before you started.</p>
            {e.caveats.length > 0 && <ul className="vmi-comp">{e.caveats.map(c => <li key={c}><em>·</em><span>{c}</span></li>)}</ul>}
          </>,
        })} />
        {e.running && <button type="button" className="vmi-link vmi-link-dim" onClick={onStop}>Mark stopped</button>}
      </div>
    </div>
  );
}

const KINDS = [
  { id: 'protocol', label: 'A habit' }, { id: 'supplement', label: 'A supplement' },
  { id: 'medication', label: 'A medication' }, { id: 'dose_change', label: 'A dose change' },
];

function StartSheet({ open, close }: { open: boolean; close: () => void }) {
  const qc = useQueryClient();
  const tq = useQuery({ queryKey: ['targets'], queryFn: () => get<Target[]>(`${VITARA}/api/interventions/targets`), enabled: open });
  const [name, setName] = useState('');
  const [kind, setKind] = useState('protocol');
  const [target, setTarget] = useState('');
  const [startedOn, setStartedOn] = useState(todayInTz());

  const add = useMutation({
    mutationFn: async () => {
      const r = await fetch(`${VITARA}/api/interventions`, {
        method: 'POST',
        headers: { ...vitaraHeaders(), 'Content-Type': 'application/json' },
        body: JSON.stringify({ name: name.trim(), kind, targetMetric: target || null, startedOn }),
      });
      const json = await r.json();
      if (!r.ok) throw new Error(json?.error ?? `${r.status}`);
      return json;
    },
    onSuccess: () => {
      setName(''); setTarget('');
      void qc.invalidateQueries({ queryKey: ['interventions'] });
      close();
    },
  });

  return (
    <>
      <div className={`vm-scrim ${open ? 'is-on' : ''}`} onClick={close} aria-hidden="true" />
      <div className={`vm-sheet vmi-why ${open ? 'is-on' : ''}`} role="dialog" aria-label="Start one" aria-hidden={!open}>
        <span className="vm-grab" aria-hidden="true" />
        <div className="vmi-why-head">
          <h2>Start one</h2>
          <button className="vmi-close" onClick={close} aria-label="Close" tabIndex={open ? 0 : -1}>×</button>
        </div>
        <div className="vmi-form">
          <label><span>What are you starting?</span>
            <input value={name} onChange={e => setName(e.target.value)} placeholder="Magnesium at night" tabIndex={open ? 0 : -1} />
          </label>
          <div className="vmi-chips" role="radiogroup" aria-label="What kind">
            {KINDS.map(k => (
              <button key={k.id} type="button" role="radio" aria-checked={kind === k.id} className={kind === k.id ? 'is-on' : ''}
                      onClick={() => setKind(k.id)} tabIndex={open ? 0 : -1}>{k.label}</button>
            ))}
          </div>
          <label><span>What should it change?</span>
            <select value={target} onChange={e => setTarget(e.target.value)} tabIndex={open ? 0 : -1}>
              <option value="">Choose one: no verdict without it</option>
              {(tq.data ?? []).map(t => <option key={t.key} value={t.key}>{t.label} ({t.better} is better)</option>)}
            </select>
          </label>
          <label><span>Started on</span>
            <input type="date" value={startedOn} max={todayInTz()} onChange={e => setStartedOn(e.target.value)} tabIndex={open ? 0 : -1} />
          </label>
          {kind === 'medication' && <p className="vmi-muted vmi-small">Start, stop or change a medication only with your doctor. Vitara records the date and shows what your data did; it never predicts the effect.</p>}
          <p className="vmi-muted vmi-small">Pick the target now. Choosing afterwards finds whichever measure happened to move.</p>
          <button type="button" className="vm-cta" disabled={!name.trim() || add.isPending} onClick={() => add.mutate()} tabIndex={open ? 0 : -1}>
            {add.isPending ? 'Saving…' : 'Start tracking'}
          </button>
          {add.error && <p className="vmi-small vmi-coral">{(add.error as Error).message}</p>}
        </div>
      </div>
    </>
  );
}

function DidItWork({ why }: { why: (w: Why) => void }) {
  const qc = useQueryClient();
  const q = useQuery({ queryKey: ['interventions'], queryFn: () => get<InterventionScan>(`${API}/api/health/interventions`) });
  const [start, setStart] = useState(false);
  const stop = useMutation({
    mutationFn: (id: string) => fetch(`${VITARA}/api/interventions/${id}/stop`, { method: 'POST', headers: vitaraHeaders() }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['interventions'] }),
  });
  if (!q.data) return null;
  const d = q.data;

  return (
    <section className="vm-tile" aria-label="Did it work">
      <h2 className="vmi-h2"><span>Did it work</span>
        <button type="button" className="vmi-btn vmi-btn-s" onClick={() => setStart(true)}>+ Start one</button>
      </h2>
      {d.interventions.length === 0 && (
        <p className="vmi-muted vmi-small">Nothing recorded yet. Start something, pick what it should change, and after {d.runInDays + d.windowDays} days you get an answer, including “it would have happened anyway”.</p>
      )}
      <div className="vmi-trials">
        {d.interventions.map(e => (
          <Trial key={e.id} e={e} runIn={d.runInDays} window={d.windowDays} onStop={() => stop.mutate(e.id)} why={why} />
        ))}
      </div>
      <StartSheet open={start} close={() => setStart(false)} />
    </section>
  );
}

export function PatternsView({ why }: { why: (w: Why) => void }) {
  return (
    <>
      <Together why={why} />
      <Relations why={why} />
      <DidItWork why={why} />
    </>
  );
}
