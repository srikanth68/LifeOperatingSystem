import { useEffect, useState, useCallback } from 'react';
import { authHeaders } from '../services/auth';
import { moduleApi } from '../services/apiHost';
import { ApiUnreachable } from '../components/ApiUnreachable';
import KnowledgeGraph from '../components/KnowledgeGraph';
import '../styles/northstar.css';
import '../styles/modules.css';

const API = moduleApi(5500);
const af = (url: string, init?: RequestInit) => fetch(url, { ...init, headers: { ...authHeaders(), ...init?.headers } });
const post = (url: string, body: unknown) => af(url, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) });
const patch = (url: string, body: unknown) => af(url, { method: 'PATCH', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) });
const put = (url: string, body: unknown) => af(url, { method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) });
const del = (url: string) => af(url, { method: 'DELETE' });

type Page = 'brain' | 'actions' | 'knowledge' | 'facts' | 'insights';

const TABS: { id: Page; label: string; icon: string }[] = [
  { id: 'brain',     label: 'Brain',     icon: '🧠' },
  { id: 'actions',   label: 'Actions',   icon: '⚡' },
  { id: 'knowledge', label: 'Knowledge', icon: '📚' },
  { id: 'facts',     label: 'Profile',   icon: '👤' },
  { id: 'insights',  label: 'Insights',  icon: '💡' },
];

const MC = 'var(--northstar)';
const style = { '--mc': MC } as React.CSSProperties;

const SOURCE_COLORS: Record<string, string> = {
  vault: '#1fc87a', vitara: '#06c8a0', aasthi: '#f0a030',
  san: '#a855f7', sutra: '#4f9ef8', manual: '#94a3b8',
};
const MODULE_LABELS: Record<string, string> = {
  vault: 'Vault', vitara: 'Vitara', aasthi: 'Aasthi',
  san: 'San', sutra: 'Sutra',
};

function timeAgo(dateStr: string) {
  const diff = Date.now() - new Date(dateStr).getTime();
  const mins = Math.floor(diff / 60000);
  if (mins < 1) return 'just now';
  if (mins < 60) return `${mins}m ago`;
  const hrs = Math.floor(mins / 60);
  if (hrs < 24) return `${hrs}h ago`;
  return `${Math.floor(hrs / 24)}d ago`;
}

// ── BRAIN (central context view) ──

interface ModuleStatus { name: string; lastSync: string; healthy: boolean; error: string | null; snapshot: unknown }
interface ContextData {
  generatedAt: string;
  user: Record<string, string>;
  modules: ModuleStatus[];
  pendingActions: { id: string; source: string; category: string; title: string; priority: number; dueDate: string | null }[];
  activeInsights: { id: string; title: string; body: string; generatedBy: string }[];
  recentKnowledge: { source: string; topic: string; summary: string; day: string | null; createdAt: string }[];
}

function BrainPage() {
  const [ctx, setCtx] = useState<ContextData | null>(null);
  const [err, setErr] = useState('');
  const [syncing, setSyncing] = useState(false);

  const load = useCallback(() => {
    af(`${API}/api/context`).then(r => { if (!r.ok) throw new Error(); return r.json(); }).then(d => { setCtx(d); setErr(''); }).catch(() => setErr('unreachable'));
  }, []);
  useEffect(load, [load]);

  const sync = async () => {
    setSyncing(true);
    try { await post(`${API}/api/context/sync`, {}); load(); }
    catch { /* */ }
    setSyncing(false);
  };

  if (err) return <ApiUnreachable name="NorthStar" port={5500} mc="var(--northstar)" onRetry={() => { setErr(''); load(); }} />;
  if (!ctx) return <div className="ns-loading">Loading brain...</div>;

  const mods = ctx.modules ?? [];
  const healthy = mods.filter(m => m.healthy).length;

  return (
    <div>
      {/* Header */}
      <div className="ns-brain-header">
        <div>
          <div className="ns-brain-title">NorthStar Brain</div>
          <div className="ns-brain-sub">Context snapshot · {new Date(ctx.generatedAt).toLocaleTimeString()}</div>
        </div>
        <button className="ns-sync-btn" onClick={sync} disabled={syncing}>
          {syncing ? 'Syncing...' : '🔄 Sync All Modules'}
        </button>
      </div>

      {/* Knowledge graph visualization */}
      <KnowledgeGraph
        modules={mods}
        knowledge={ctx.recentKnowledge}
        actions={ctx.pendingActions}
        facts={ctx.user}
      />

      {/* Module health grid */}
      <div className="ns-section-label">Module Health</div>
      <div className="ns-module-grid">
        {['vault','vitara','aasthi','san','sutra'].map(mod => {
          const m = mods.find(x => x.name === mod);
          return (
            <div key={mod} className={`ns-module-card ${m?.healthy ? 'ns-healthy' : ''}`}>
              <div className="ns-module-dot" style={{ background: SOURCE_COLORS[mod] }}/>
              <div className="ns-module-info">
                <div className="ns-module-name">{MODULE_LABELS[mod]}</div>
                <div className="ns-module-status">
                  {m ? (m.healthy ? `Synced ${timeAgo(m.lastSync)}` : `Error: ${m.error}`) : 'No data'}
                </div>
              </div>
            </div>
          );
        })}
        <div className="ns-module-card ns-module-summary">
          <div className="ns-module-big">{healthy}/{mods.length}</div>
          <div className="ns-module-status">modules online</div>
        </div>
      </div>

      {/* Pending actions preview */}
      {ctx.pendingActions.length > 0 && (
        <>
          <div className="ns-section-label">Pending Actions <span className="ns-badge">{ctx.pendingActions.length}</span></div>
          <div className="ns-action-list">
            {ctx.pendingActions.slice(0, 5).map(a => (
              <div key={a.id} className="ns-action-row">
                <span className="ns-action-priority" data-p={a.priority}>P{a.priority}</span>
                <span className="ns-action-source" style={{ color: SOURCE_COLORS[a.source] }}>{a.source}</span>
                <span className="ns-action-title">{a.title}</span>
                {a.dueDate && <span className="ns-action-due">due {a.dueDate}</span>}
              </div>
            ))}
          </div>
        </>
      )}

      {/* User profile facts */}
      {Object.keys(ctx.user).length > 0 && (
        <>
          <div className="ns-section-label">User Profile</div>
          <div className="ns-facts-grid">
            {Object.entries(ctx.user).slice(0, 8).map(([k, v]) => (
              <div key={k} className="ns-fact-chip">
                <span className="ns-fact-key">{k}</span>
                <span className="ns-fact-val">{v}</span>
              </div>
            ))}
          </div>
        </>
      )}

      {/* Recent knowledge feed */}
      <div className="ns-section-label">Recent Knowledge <span className="ns-badge">{ctx.recentKnowledge.length}</span></div>
      {ctx.recentKnowledge.length > 0 ? (
        <div className="ns-knowledge-feed">
          {ctx.recentKnowledge.slice(0, 10).map((k, i) => (
            <div key={i} className="ns-feed-item">
              <span className="ns-feed-dot" style={{ background: SOURCE_COLORS[k.source] }}/>
              <div className="ns-feed-body">
                <div className="ns-feed-header">
                  <span className="ns-feed-source">{k.source}</span>
                  <span className="ns-feed-topic">{k.topic}</span>
                  <span className="ns-feed-time">{timeAgo(k.createdAt)}</span>
                </div>
                <div className="ns-feed-summary">{k.summary}</div>
              </div>
            </div>
          ))}
        </div>
      ) : (
        <div className="ns-empty-mini">No knowledge entries yet. Click "Sync All Modules" to pull data.</div>
      )}
    </div>
  );
}

// ── ACTIONS ──

interface ActionItem {
  id: string; source: string; category: string; title: string; description: string | null;
  priority: number; dueDate: string | null; status: string; resolvedBy: string | null;
  createdAt: string; completedAt: string | null;
}

function ActionsPage() {
  const [actions, setActions] = useState<ActionItem[]>([]);
  const [filter, setFilter] = useState('pending');
  const [title, setTitle] = useState('');
  const [desc, setDesc] = useState('');
  const [priority, setPriority] = useState('3');
  const [category, setCategory] = useState('task');
  const [due, setDue] = useState('');
  const [err, setErr] = useState('');

  const load = useCallback(() => {
    af(`${API}/api/actions?status=${filter}&limit=50`)
      .then(r => { if (!r.ok) throw new Error(); return r.json(); })
      .then(d => { setActions(d); setErr(''); })
      .catch(() => setErr('unreachable'));
  }, [filter]);
  useEffect(load, [load]);

  if (err) return <ApiUnreachable name="NorthStar" port={5500} mc={MC} onRetry={load} />;

  const create = async () => {
    if (!title.trim()) return;
    await post(`${API}/api/actions`, { title, description: desc || null, priority: parseInt(priority), category, dueDate: due || null, source: 'manual' });
    setTitle(''); setDesc(''); setDue('');
    load();
  };

  const updateStatus = async (id: string, status: string) => {
    await patch(`${API}/api/actions/${id}`, { status });
    load();
  };

  return (
    <div>
      <div className="ns-section-label">Add Action</div>
      <div className="ns-action-form">
        <input className="ns-input" placeholder="What needs to be done?" value={title} onChange={e => setTitle(e.target.value)} onKeyDown={e => e.key === 'Enter' && create()} />
        <div className="ns-action-form-row">
          <input className="ns-input ns-input-sm" placeholder="Details (optional)" value={desc} onChange={e => setDesc(e.target.value)} />
          <select className="ns-select" value={priority} onChange={e => setPriority(e.target.value)}>
            <option value="1">P1 Critical</option>
            <option value="2">P2 High</option>
            <option value="3">P3 Medium</option>
            <option value="4">P4 Low</option>
          </select>
          <select className="ns-select" value={category} onChange={e => setCategory(e.target.value)}>
            {['task','reminder','bill','health','document','follow-up'].map(c => <option key={c} value={c}>{c}</option>)}
          </select>
          <input className="ns-input ns-input-date" type="date" value={due} onChange={e => setDue(e.target.value)} />
          <button className="ns-btn" onClick={create} disabled={!title.trim()}>Add</button>
        </div>
      </div>

      <div className="ns-filter-bar">
        {['pending','completed','dismissed','all'].map(f => (
          <button key={f} className={`ns-filter-btn ${filter === f ? 'active' : ''}`} onClick={() => setFilter(f)}>{f}</button>
        ))}
      </div>

      <div className="ns-action-list">
        {actions.map(a => (
          <div key={a.id} className={`ns-action-row ns-action-${a.status}`}>
            <span className="ns-action-priority" data-p={a.priority}>P{a.priority}</span>
            <span className="ns-action-source" style={{ color: SOURCE_COLORS[a.source] || '#94a3b8' }}>{a.source}</span>
            <div className="ns-action-body">
              <div className="ns-action-title">{a.title}</div>
              {a.description && <div className="ns-action-desc">{a.description}</div>}
            </div>
            {a.dueDate && <span className="ns-action-due">due {a.dueDate}</span>}
            {a.status === 'pending' && (
              <div className="ns-action-btns">
                <button className="ns-btn-sm ns-btn-done" onClick={() => updateStatus(a.id, 'completed')}>✓</button>
                <button className="ns-btn-sm ns-btn-dismiss" onClick={() => updateStatus(a.id, 'dismissed')}>×</button>
              </div>
            )}
            {a.status !== 'pending' && <span className="ns-action-status">{a.status}</span>}
          </div>
        ))}
        {actions.length === 0 && <div className="ns-empty-mini">No {filter} actions.</div>}
      </div>
    </div>
  );
}

// ── KNOWLEDGE (search + browse) ──

interface KnowledgeEntry { id: string; source: string; topic: string; summary: string; day: string | null; createdAt: string }

function KnowledgePage() {
  const [query, setQuery] = useState('');
  const [results, setResults] = useState<KnowledgeEntry[]>([]);
  const [entries, setEntries] = useState<KnowledgeEntry[]>([]);
  const [days, setDays] = useState(14);
  const [searched, setSearched] = useState(false);
  const [err, setErr] = useState('');

  const loadTimeline = useCallback(() => {
    af(`${API}/api/knowledge/timeline?days=${days}&limit=100`)
      .then(r => { if (!r.ok) throw new Error(); return r.json(); })
      .then(d => { setEntries(d.entries ?? []); setErr(''); })
      .catch(() => setErr('unreachable'));
  }, [days]);
  useEffect(loadTimeline, [loadTimeline]);

  const search = async () => {
    if (!query.trim()) return;
    try {
      const resp = await af(`${API}/api/knowledge/search?q=${encodeURIComponent(query)}`);
      if (!resp.ok) throw new Error();
      const data = await resp.json();
      setResults(data.entries ?? []);
      setSearched(true);
      setErr('');
    } catch { setErr('unreachable'); }
  };

  if (err) return <ApiUnreachable name="NorthStar" port={5500} mc={MC} onRetry={loadTimeline} />;

  const display = searched ? results : entries;

  return (
    <div>
      <div className="ns-search-bar">
        <input className="ns-search-input" placeholder="Search knowledge base..." value={query}
          onChange={e => { setQuery(e.target.value); if (!e.target.value) setSearched(false); }}
          onKeyDown={e => e.key === 'Enter' && search()} />
        <button className="ns-btn" onClick={search}>Search</button>
      </div>

      {!searched && (
        <div className="ns-filter-bar">
          {[7,14,30,90].map(d => (
            <button key={d} className={`ns-filter-btn ${days === d ? 'active' : ''}`} onClick={() => setDays(d)}>{d}d</button>
          ))}
        </div>
      )}

      {searched && <div className="ns-section-label">Results for "{query}" <span className="ns-badge">{results.length}</span></div>}

      <div className="ns-knowledge-feed">
        {display.map((k, i) => (
          <div key={k.id || i} className="ns-feed-item">
            <span className="ns-feed-dot" style={{ background: SOURCE_COLORS[k.source] }}/>
            <div className="ns-feed-body">
              <div className="ns-feed-header">
                <span className="ns-feed-source">{k.source}</span>
                <span className="ns-feed-topic">{k.topic}</span>
                {k.day && <span className="ns-feed-day">{k.day}</span>}
                <span className="ns-feed-time">{timeAgo(k.createdAt)}</span>
              </div>
              <div className="ns-feed-summary">{k.summary}</div>
            </div>
          </div>
        ))}
        {display.length === 0 && <div className="ns-empty-mini">{searched ? `No results for "${query}"` : 'No entries yet.'}</div>}
      </div>
    </div>
  );
}

// ── FACTS (user profile) ──

interface UserFact { key: string; value: string; source: string; updatedAt: string }

function FactsPage() {
  const [facts, setFacts] = useState<UserFact[]>([]);
  const [newKey, setNewKey] = useState('');
  const [newVal, setNewVal] = useState('');
  const [err, setErr] = useState('');

  const load = useCallback(() => {
    af(`${API}/api/facts`)
      .then(r => { if (!r.ok) throw new Error(); return r.json(); })
      .then(d => { setFacts(d); setErr(''); })
      .catch(() => setErr('unreachable'));
  }, []);
  useEffect(load, [load]);

  if (err) return <ApiUnreachable name="NorthStar" port={5500} mc={MC} onRetry={load} />;

  const save = async () => {
    if (!newKey.trim() || !newVal.trim()) return;
    await put(`${API}/api/facts/${encodeURIComponent(newKey.trim())}`, { value: newVal.trim(), source: 'manual' });
    setNewKey(''); setNewVal('');
    load();
  };

  const remove = async (key: string) => {
    await del(`${API}/api/facts/${encodeURIComponent(key)}`);
    load();
  };

  return (
    <div>
      <div className="ns-section-label">User Profile Facts</div>
      <p className="ns-section-desc">Persistent facts about you that San's AI uses for reasoning. Things like your name, preferences, routines, goals.</p>

      <div className="ns-fact-form">
        <input className="ns-input" placeholder="Key (e.g. name, diet, wake_time)" value={newKey} onChange={e => setNewKey(e.target.value)} />
        <input className="ns-input" placeholder="Value" value={newVal} onChange={e => setNewVal(e.target.value)} onKeyDown={e => e.key === 'Enter' && save()} />
        <button className="ns-btn" onClick={save} disabled={!newKey.trim() || !newVal.trim()}>Save</button>
      </div>

      <div className="ns-facts-list">
        {facts.map(f => (
          <div key={f.key} className="ns-fact-row">
            <span className="ns-fact-key">{f.key}</span>
            <span className="ns-fact-val">{f.value}</span>
            <span className="ns-fact-source">{f.source}</span>
            <button className="ns-btn-sm ns-btn-dismiss" onClick={() => remove(f.key)}>×</button>
          </div>
        ))}
        {facts.length === 0 && <div className="ns-empty-mini">No facts stored. Add your name, preferences, and routines above.</div>}
      </div>
    </div>
  );
}

// ── INSIGHTS ──

interface Insight { id: string; title: string; body: string; generatedBy: string; dismissed: boolean; createdAt: string }

function InsightsPage() {
  const [insights, setInsights] = useState<Insight[]>([]);
  const [title, setTitle] = useState('');
  const [body, setBody] = useState('');
  const [err, setErr] = useState('');

  const load = useCallback(() => {
    af(`${API}/api/insights`)
      .then(r => { if (!r.ok) throw new Error(); return r.json(); })
      .then(d => { setInsights(d); setErr(''); })
      .catch(() => setErr('unreachable'));
  }, []);
  useEffect(load, [load]);

  if (err) return <ApiUnreachable name="NorthStar" port={5500} mc={MC} onRetry={load} />;

  const create = async () => {
    if (!title.trim() || !body.trim()) return;
    await post(`${API}/api/insights`, { title, body });
    setTitle(''); setBody('');
    load();
  };

  const dismiss = async (id: string) => {
    await patch(`${API}/api/insights/${id}/dismiss`, {});
    load();
  };

  return (
    <div>
      <div className="ns-section-label">Add Insight</div>
      <div className="ns-insight-form">
        <input className="ns-input" placeholder="Insight title" value={title} onChange={e => setTitle(e.target.value)} />
        <textarea className="ns-textarea" placeholder="What pattern did you notice?" rows={3} value={body} onChange={e => setBody(e.target.value)} />
        <button className="ns-btn" onClick={create} disabled={!title.trim() || !body.trim()}>Add Insight</button>
      </div>

      <div className="ns-section-label">Active Insights</div>
      <div className="ns-insights-list">
        {insights.map(i => (
          <div key={i.id} className="ns-insight-card">
            <div className="ns-insight-header">
              <span className="ns-insight-title">{i.title}</span>
              <span className="ns-insight-badge">{i.generatedBy}</span>
              <button className="ns-btn-sm ns-btn-dismiss" onClick={() => dismiss(i.id)}>×</button>
            </div>
            <div className="ns-insight-body">{i.body}</div>
            <div className="ns-insight-time">{timeAgo(i.createdAt)}</div>
          </div>
        ))}
        {insights.length === 0 && <div className="ns-empty-mini">No insights yet. Add one manually or wait for AI-generated ones.</div>}
      </div>
    </div>
  );
}

// ── ROOT ──

export default function NorthStarModule() {
  const [page, setPage] = useState<Page>('brain');

  return (
    <div style={style}>
      {/* Same .module-header structure Vitara and the others use — NorthStar was the
          only module on its own .m-* classes, and those had no CSS at all, which is
          why the title and tabs rendered as bare text. */}
      <div className="module-header">
        <div className="module-header-icon">
          <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.6" strokeLinecap="round" strokeLinejoin="round">
            <circle cx="12" cy="12" r="3" />
            <path d="M12 2v3M12 19v3M2 12h3M19 12h3M4.9 4.9l2.1 2.1M17 17l2.1 2.1M19.1 4.9L17 7M7 17l-2.1 2.1" />
          </svg>
        </div>
        <div>
          <h1 className="module-title">NorthStar</h1>
          <div className="module-subtitle">Knowledge Center &amp; Brain</div>
        </div>
      </div>

      <nav className="ns-tabs" role="tablist">
        {TABS.map(t => (
          <button
            key={t.id}
            role="tab"
            aria-selected={page === t.id}
            className={`ns-tab ${page === t.id ? 'active' : ''}`}
            onClick={() => setPage(t.id)}
          >
            <span className="ns-tab-icon" aria-hidden="true">{t.icon}</span>
            <span>{t.label}</span>
          </button>
        ))}
      </nav>

      <div className="m-content">
        {page === 'brain'     && <BrainPage />}
        {page === 'actions'   && <ActionsPage />}
        {page === 'knowledge' && <KnowledgePage />}
        {page === 'facts'     && <FactsPage />}
        {page === 'insights'  && <InsightsPage />}
      </div>
    </div>
  );
}
