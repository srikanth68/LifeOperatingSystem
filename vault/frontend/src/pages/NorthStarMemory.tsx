import { useCallback, useEffect, useState } from 'react';
import { authHeaders } from '../services/auth';
import { moduleApi } from '../services/apiHost';
import { formatInstant } from '../services/timezone';
import { ApiUnreachable } from '../components/ApiUnreachable';

// NorthStar's long-term memory, as the owner sees it.
//
// San saves what it concludes as PROPOSED: recalled, labelled unconfirmed, and listed
// here until confirmed or rejected. Nothing is overwritten: an edit is a correction that
// supersedes the old memory, which stays visible in its history. Entities are the
// people, places and things memories are about; an alias ("my car") is what lets San
// find a memory that never uses those words.

const API = moduleApi(5500);
const MC = 'var(--northstar)';
const af = (url: string, init?: RequestInit) => fetch(url, { ...init, headers: { ...authHeaders(), ...init?.headers } });
const send = (method: string, url: string, body: unknown) =>
  af(url, { method, headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) });

type Status = 'active' | 'proposed' | 'superseded' | 'retracted';
interface Memory {
  id: string; content: string; kind: string; source: string; importance: number; createdAt: string;
  status: Status; statusNote: string | null; statusChangedAt: string | null;
  supersedes: string | null; supersededBy: string | null;
  sourceRef: string | null; evidence: string | null;
  replaces?: string | null;
}
interface MemoryDetail { memory: Memory; history: Memory[]; entities: { id: string; name: string; type: string }[] }
interface Entity { id: string; type: string; name: string; aliases: string; note: string | null }
interface LinkedRecord { linkId: string; relation: string; direction: string; basis: string; otherType: string; otherId: string; otherSummary: string | null }

const KINDS = ['preference', 'fact', 'decision', 'event', 'observation', 'skill'];
const ENTITY_TYPES = ['person', 'org', 'place', 'project', 'property', 'vehicle', 'account', 'topic', 'other'];
const day = (s: string | null) => formatInstant(s, { month: 'short', day: 'numeric', year: 'numeric' });

type View = 'review' | 'memories' | 'entities';

export default function MemoryPage() {
  const [view, setView] = useState<View>('review');
  const [reviewCount, setReviewCount] = useState<number | null>(null);

  const countReview = useCallback(() => {
    af(`${API}/api/memory/stats`).then(r => r.ok ? r.json() : null)
      .then(d => setReviewCount(d?.byStatus?.proposed ?? 0)).catch(() => setReviewCount(null));
  }, []);
  useEffect(countReview, [countReview]);

  return (
    <div>
      <div className="ns-filter-bar">
        <button className={`ns-filter-btn ${view === 'review' ? 'active' : ''}`} onClick={() => setView('review')}>
          To review {reviewCount ? <span className="ns-badge">{reviewCount}</span> : null}
        </button>
        <button className={`ns-filter-btn ${view === 'memories' ? 'active' : ''}`} onClick={() => setView('memories')}>Memories</button>
        <button className={`ns-filter-btn ${view === 'entities' ? 'active' : ''}`} onClick={() => setView('entities')}>Entities</button>
      </div>
      {view === 'review' && <ReviewList onChange={countReview} />}
      {view === 'memories' && <MemoryList onChange={countReview} />}
      {view === 'entities' && <EntityList />}
    </div>
  );
}

// ── Review: what San concluded and nobody has confirmed ──

function ReviewList({ onChange }: { onChange: () => void }) {
  const [items, setItems] = useState<Memory[] | null>(null);
  const [err, setErr] = useState(false);

  const load = useCallback(() => {
    af(`${API}/api/memory/review?limit=200`)
      .then(r => { if (!r.ok) throw new Error(); return r.json(); })
      .then(d => { setItems(d.memories ?? []); setErr(false); })
      .catch(() => setErr(true));
  }, []);
  useEffect(load, [load]);

  const done = () => { load(); onChange(); };

  if (err) return <ApiUnreachable name="NorthStar" port={5500} mc={MC} onRetry={load} />;
  if (!items) return <div className="ns-loading">Loading…</div>;

  return (
    <div>
      <p className="ns-section-desc">
        Things San concluded from your conversations and emails. San already uses them, marked as unconfirmed.
        Confirm the ones that are right, fix the ones that are nearly right, reject the rest.
      </p>
      <div className="ns-mem-list">
        {items.map(m => <MemoryCard key={m.id} m={m} onDone={done} reviewing />)}
        {items.length === 0 && <div className="ns-empty-mini">Nothing waiting for review.</div>}
      </div>
    </div>
  );
}

// ── Browse, add, correct ──

function MemoryList({ onChange }: { onChange: () => void }) {
  const [status, setStatus] = useState<'live' | Status | 'all'>('live');
  const [items, setItems] = useState<Memory[] | null>(null);
  const [filter, setFilter] = useState('');
  const [err, setErr] = useState(false);
  const [content, setContent] = useState('');
  const [kind, setKind] = useState('preference');

  const load = useCallback(() => {
    const q = status === 'live' ? '' : `&status=${status}`;
    af(`${API}/api/memory/recent?limit=500${q}`)
      .then(r => { if (!r.ok) throw new Error(); return r.json(); })
      .then(d => { setItems(d); setErr(false); })
      .catch(() => setErr(true));
  }, [status]);
  useEffect(load, [load]);

  const add = async () => {
    if (!content.trim()) return;
    await send('POST', `${API}/api/memory`, { content: content.trim(), kind, source: 'manual', importance: 3 });
    setContent('');
    load();
  };

  if (err) return <ApiUnreachable name="NorthStar" port={5500} mc={MC} onRetry={load} />;

  const f = filter.trim().toLowerCase();
  const shown = (items ?? []).filter(m => !f || m.content.toLowerCase().includes(f) || m.kind.includes(f));

  return (
    <div>
      <div className="ns-section-label">Tell NorthStar something</div>
      <div className="ns-fact-form">
        <input className="ns-input" placeholder="Something San should know about you" value={content}
          onChange={e => setContent(e.target.value)} onKeyDown={e => e.key === 'Enter' && add()} />
        <select className="ns-select" value={kind} onChange={e => setKind(e.target.value)}>
          {KINDS.map(k => <option key={k} value={k}>{k}</option>)}
        </select>
        <button className="ns-btn" onClick={add} disabled={!content.trim()}>Remember</button>
      </div>

      <div className="ns-filter-bar">
        {(['live', 'active', 'proposed', 'superseded', 'retracted', 'all'] as const).map(s => (
          <button key={s} className={`ns-filter-btn ${status === s ? 'active' : ''}`} onClick={() => setStatus(s)}>{s}</button>
        ))}
      </div>
      <div className="ns-search-bar">
        <input className="ns-search-input" placeholder="Filter…" value={filter} onChange={e => setFilter(e.target.value)} />
      </div>

      {!items ? <div className="ns-loading">Loading…</div> : (
        <div className="ns-mem-list">
          {shown.map(m => <MemoryCard key={m.id} m={m} onDone={() => { load(); onChange(); }} />)}
          {shown.length === 0 && <div className="ns-empty-mini">No memories here.</div>}
        </div>
      )}
    </div>
  );
}

function MemoryCard({ m, onDone, reviewing = false }: { m: Memory; onDone: () => void; reviewing?: boolean }) {
  const [editing, setEditing] = useState(false);
  const [text, setText] = useState(m.content);
  const [open, setOpen] = useState(false);
  const [detail, setDetail] = useState<MemoryDetail | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const live = m.status === 'active' || m.status === 'proposed';

  const act = async (fn: () => Promise<Response>) => {
    setBusy(true); setError('');
    try {
      const r = await fn();
      if (!r.ok) { setError((await r.text()) || `HTTP ${r.status}`); return; }
      setEditing(false);
      onDone();
    } catch { setError('NorthStar is unreachable'); }
    finally { setBusy(false); }
  };

  const confirm = () => act(() => send('POST', `${API}/api/memory/${m.id}/confirm`, { note: 'confirmed in review' }));
  const retract = () => act(() => send('POST', `${API}/api/memory/${m.id}/retract`,
    { note: reviewing ? 'rejected in review' : 'retracted by owner' }));
  // An edit is a correction by the owner: active at once, and the original kept as history.
  const correct = () => act(() => send('POST', `${API}/api/memory`,
    { content: text.trim(), kind: m.kind, importance: m.importance, source: 'manual', supersedes: m.id }));

  const toggle = async () => {
    if (!open && !detail) {
      const r = await af(`${API}/api/memory/${m.id}`);
      if (r.ok) setDetail(await r.json());
    }
    setOpen(!open);
  };

  return (
    <div className={`ns-mem-card ns-mem-${m.status}`}>
      <div className="ns-mem-head">
        <span className="ns-mem-kind">{m.kind}</span>
        <span className={`ns-mem-status ns-mem-status-${m.status}`}>{m.status === 'proposed' ? 'unconfirmed' : m.status}</span>
        <span className="ns-mem-meta">{m.source} · {day(m.createdAt)}</span>
        <span className="ns-mem-spacer" />
        {live && !editing && (
          <div className="ns-action-btns">
            {m.status === 'proposed' && <button className="ns-btn-sm ns-btn-done" title="Confirm" aria-label="Confirm" disabled={busy} onClick={confirm}>✓</button>}
            <button className="ns-btn-sm" title="Correct" aria-label="Correct" disabled={busy} onClick={() => { setText(m.content); setEditing(true); }}>✎</button>
            <button className="ns-btn-sm ns-btn-dismiss" title={reviewing ? 'Reject' : 'Retract'} aria-label={reviewing ? 'Reject' : 'Retract'} disabled={busy} onClick={retract}>×</button>
          </div>
        )}
      </div>

      {editing ? (
        <div className="ns-mem-edit">
          <textarea className="ns-textarea" rows={2} value={text} onChange={e => setText(e.target.value)} />
          <div className="ns-mem-edit-btns">
            <button className="ns-btn" disabled={busy || !text.trim() || text.trim() === m.content} onClick={correct}>Save correction</button>
            <button className="ns-btn-sm" onClick={() => setEditing(false)}>Cancel</button>
          </div>
        </div>
      ) : (
        <div className="ns-mem-content">{m.content}</div>
      )}

      {m.replaces && <div className="ns-mem-line"><span className="ns-mem-label">would replace</span> {m.replaces}</div>}
      {m.evidence && <div className="ns-mem-evidence">“{m.evidence}”</div>}
      {error && <div className="ns-mem-error">{error}</div>}

      <button className="ns-mem-more" onClick={toggle}>{open ? 'Hide' : 'Where this came from'}</button>
      {open && detail && (
        <div className="ns-mem-detail">
          <div className="ns-mem-line"><span className="ns-mem-label">source</span> {detail.memory.source}{detail.memory.sourceRef ? ` · ${detail.memory.sourceRef}` : ''}</div>
          {detail.memory.statusNote && <div className="ns-mem-line"><span className="ns-mem-label">note</span> {detail.memory.statusNote}</div>}
          {detail.entities.length > 0 && (
            <div className="ns-mem-line"><span className="ns-mem-label">about</span>
              {detail.entities.map(e => <span key={e.id} className="ns-mem-chip">{e.name}</span>)}
            </div>
          )}
          {detail.history.length > 1 && (
            <ol className="ns-mem-history">
              {detail.history.map(h => (
                <li key={h.id} className={h.id === m.id ? 'ns-mem-here' : ''}>
                  <span className={`ns-mem-status ns-mem-status-${h.status}`}>{h.status === 'proposed' ? 'unconfirmed' : h.status}</span>
                  <span className="ns-mem-meta">{day(h.createdAt)}</span>
                  <div>{h.content}</div>
                </li>
              ))}
            </ol>
          )}
        </div>
      )}
    </div>
  );
}

// ── Entities ──

function EntityList() {
  const [items, setItems] = useState<Entity[] | null>(null);
  const [err, setErr] = useState(false);
  const [filter, setFilter] = useState('');

  const load = useCallback(() => {
    af(`${API}/api/entities?limit=500`)
      .then(r => { if (!r.ok) throw new Error(); return r.json(); })
      .then(d => { setItems(d); setErr(false); })
      .catch(() => setErr(true));
  }, []);
  useEffect(load, [load]);

  if (err) return <ApiUnreachable name="NorthStar" port={5500} mc={MC} onRetry={load} />;
  if (!items) return <div className="ns-loading">Loading…</div>;

  const f = filter.trim().toLowerCase();
  const shown = items.filter(e => !f || e.name.toLowerCase().includes(f) || e.aliases.toLowerCase().includes(f));

  return (
    <div>
      <p className="ns-section-desc">
        The people, places and things your memories are about. Add the words you actually use as aliases:
        with “my car” on Honda Civic, asking San about your car finds every memory about the Honda.
      </p>
      <div className="ns-search-bar">
        <input className="ns-search-input" placeholder="Filter…" value={filter} onChange={e => setFilter(e.target.value)} />
      </div>
      <div className="ns-mem-list">
        {shown.map(e => <EntityRow key={e.id} e={e} onDone={load} />)}
        {shown.length === 0 && <div className="ns-empty-mini">No entities yet. San adds them as it saves memories.</div>}
      </div>
    </div>
  );
}

function EntityRow({ e, onDone }: { e: Entity; onDone: () => void }) {
  const [type, setType] = useState(e.type);
  const [aliases, setAliases] = useState(e.aliases);
  const [links, setLinks] = useState<LinkedRecord[] | null>(null);
  const [open, setOpen] = useState(false);
  const [error, setError] = useState('');
  const dirty = type !== e.type || aliases !== e.aliases;

  const save = async () => {
    setError('');
    const r = await send('PATCH', `${API}/api/entities/${e.id}`, { type, aliases });
    if (!r.ok) { setError(await r.text()); return; }
    onDone();
  };
  const remove = async () => {
    if (!window.confirm(`Delete "${e.name}"? Its memories stay; only the entity and its links go.`)) return;
    await af(`${API}/api/entities/${e.id}`, { method: 'DELETE' });
    onDone();
  };
  const toggle = async () => {
    if (!open && !links) {
      const r = await af(`${API}/api/entities/${e.id}`);
      if (r.ok) setLinks((await r.json()).links ?? []);
    }
    setOpen(!open);
  };

  return (
    <div className="ns-mem-card">
      <div className="ns-ent-row">
        <span className="ns-ent-name">{e.name}</span>
        <select className="ns-select" value={type} onChange={ev => setType(ev.target.value)} aria-label="Type">
          {ENTITY_TYPES.map(t => <option key={t} value={t}>{t}</option>)}
        </select>
        <input className="ns-input ns-ent-aliases" placeholder="Aliases, comma-separated" value={aliases}
          onChange={ev => setAliases(ev.target.value)} onKeyDown={ev => ev.key === 'Enter' && dirty && save()} aria-label="Aliases" />
        {dirty && <button className="ns-btn" onClick={save}>Save</button>}
        <button className="ns-btn-sm ns-btn-dismiss" title="Delete entity" aria-label="Delete entity" onClick={remove}>×</button>
      </div>
      {error && <div className="ns-mem-error">{error}</div>}
      <button className="ns-mem-more" onClick={toggle}>{open ? 'Hide' : 'Linked records'}</button>
      {open && links && (
        <ul className="ns-ent-links">
          {links.map(l => (
            <li key={l.linkId}>
              <span className="ns-mem-kind">{l.otherType}</span>
              {l.basis === 'inferred' && <span className="ns-mem-status ns-mem-status-proposed">inferred</span>}
              <span>{l.otherSummary ?? <em>(no longer exists)</em>}</span>
            </li>
          ))}
          {links.length === 0 && <li className="ns-mem-meta">Nothing linked.</li>}
        </ul>
      )}
    </div>
  );
}
