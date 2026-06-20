import { useState, useRef, useEffect } from 'react';
import { QueryClient, QueryClientProvider, useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import '../styles/modules.css';
import '../styles/san.css';

const API = 'http://localhost:5300';
const MC = 'var(--san)';
const style = { '--mc': MC } as React.CSSProperties;

const qc = new QueryClient({ defaultOptions: { queries: { retry: 1, staleTime: 30_000, refetchOnWindowFocus: false } } });

const get = (url: string) => fetch(url).then(r => { if (!r.ok) throw new Error(r.status.toString()); return r.json(); });
const send = (url: string, method: string, body?: unknown) =>
  fetch(url, {
    method,
    headers: body !== undefined ? { 'Content-Type': 'application/json' } : undefined,
    body: body !== undefined ? JSON.stringify(body) : undefined,
  }).then(r => { if (!r.ok) throw new Error(r.status.toString()); return r.status === 204 ? null : r.json(); });

/* ── Types ── */
interface ChatMsg { id: string; role: 'user' | 'assistant'; content: string; createdAt: string }
interface Reminder { id: string; text: string; dueAt: string; done: boolean; notifyTelegram: boolean; notifiedAt: string | null; createdAt: string }
interface AlertItem {
  id: string; type: string; title: string; description: string;
  thresholdValue: number | null; triggerAt: string | null; active: boolean;
  notifyTelegram: boolean; triggeredAt: string | null; createdAt: string;
}
interface FeedEntry { module: string; title: string; description: string; occurredAt: string }
interface ModuleStatus { module: string; reachable: boolean; error: string | null }
interface FeedResult { entries: FeedEntry[]; modules: ModuleStatus[] }

const fmtDateTime = (d: string | null) => d ? new Date(d).toLocaleString('en-US', { month: 'short', day: 'numeric', hour: 'numeric', minute: '2-digit' }) : '—';
const toLocalInputValue = (iso: string) => {
  const d = new Date(iso);
  const pad = (n: number) => n.toString().padStart(2, '0');
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`;
};

const ALERT_TYPES = [
  { id: 'spending_threshold', label: 'Spending Threshold' },
  { id: 'goal_deadline', label: 'Goal Deadline' },
  { id: 'document_expiry', label: 'Document Expiry' },
  { id: 'custom', label: 'Custom' },
];
const ALERT_TYPE_COLOR: Record<string, string> = {
  spending_threshold: '#e84444', goal_deadline: '#1fc87a', document_expiry: '#d4a843', custom: '#7a96c0',
};
const MODULE_COLOR: Record<string, string> = { Vault: '#4f9ef8', Vitara: '#e8527c', Aasthi: '#d4a843' };

type Page = 'assistant' | 'reminders' | 'alerts' | 'feed';
const TABS: { id: Page; label: string }[] = [
  { id: 'assistant', label: 'Assistant' },
  { id: 'reminders', label: 'Reminders' },
  { id: 'alerts',    label: 'Alerts' },
  { id: 'feed',      label: 'Activity Feed' },
];

function Icon() {
  return (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.75" strokeLinecap="round" strokeLinejoin="round">
      <path d="M12 2l3.09 6.26L22 9.27l-5 4.87 1.18 6.88L12 17.77l-6.18 3.25L7 14.14 2 9.27l6.91-1.01L12 2z"/>
    </svg>
  );
}
function SendIcon() {
  return (
    <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
      <line x1="22" y1="2" x2="11" y2="13"/><polygon points="22 2 15 22 11 13 2 9 22 2"/>
    </svg>
  );
}

function ApiError({ port }: { port: number }) {
  return (
    <div className="module-empty" style={style}>
      <div className="module-empty-icon"><Icon /></div>
      <h2>Can't reach San API</h2>
      <p>Make sure the San backend is running on port {port} (<code>san/start.ps1</code>).</p>
    </div>
  );
}

/* ── Assistant ── */
function Assistant() {
  const queryClient = useQueryClient();
  const [draft, setDraft] = useState('');
  const scrollRef = useRef<HTMLDivElement>(null);

  const messagesQ = useQuery<ChatMsg[]>({ queryKey: ['san-messages'], queryFn: () => get(`${API}/api/chat/messages`) });
  const sendMut = useMutation({
    mutationFn: (content: string) => send(`${API}/api/chat/messages`, 'POST', { content }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['san-messages'] }),
  });

  useEffect(() => {
    scrollRef.current?.scrollTo({ top: scrollRef.current.scrollHeight });
  }, [messagesQ.data, sendMut.isPending]);

  if (messagesQ.isError) return <ApiError port={5300} />;

  const messages = messagesQ.data ?? [];
  const submit = () => {
    const text = draft.trim();
    if (!text || sendMut.isPending) return;
    setDraft('');
    sendMut.mutate(text);
  };

  return (
    <div style={style}>
      <div className="chat-shell" style={{ marginBottom: '1.5rem' }}>
        <div className="chat-messages" ref={scrollRef}>
          {messages.length === 0 && !messagesQ.isLoading && (
            <div className="chat-bubble">
              <div className="chat-avatar">S</div>
              <div className="chat-text">
                Hello! I'm San. Ask me anything about your finances, health, or properties — I can see live data
                across Vault, Vitara, and Aasthi.
              </div>
            </div>
          )}
          {messages.map(m => (
            <div key={m.id} className="chat-bubble">
              <div className="chat-avatar">{m.role === 'user' ? 'U' : 'S'}</div>
              <div className="chat-text">{m.content}</div>
            </div>
          ))}
          {sendMut.isPending && (
            <div className="chat-bubble">
              <div className="chat-avatar">S</div>
              <div className="chat-text" style={{ color: 'var(--text3)', fontStyle: 'italic' }}>San is thinking…</div>
            </div>
          )}
        </div>
        <div className="chat-bar">
          <input
            placeholder="Ask San anything about your life data…"
            value={draft}
            onChange={e => setDraft(e.target.value)}
            onKeyDown={e => { if (e.key === 'Enter') submit(); }}
            disabled={sendMut.isPending}
          />
          <button className="chat-send" onClick={submit} disabled={sendMut.isPending || !draft.trim()}><SendIcon /></button>
        </div>
      </div>
      <div className="card">
        <h3>What San can do</h3>
        <div style={{ display: 'flex', flexDirection: 'column', gap: '0.45rem', marginTop: '0.5rem' }}>
          {[
            { icon: '💬', text: 'Answer natural language questions about your finances, health, and properties' },
            { icon: '🔗', text: 'Cross-module insights pulled live from Vault, Vitara, and Aasthi' },
            { icon: '📅', text: 'Reminders and alerts, delivered to Telegram' },
            { icon: '📊', text: 'A unified activity feed across all connected modules' },
          ].map(f => (
            <div key={f.text} className="module-feature-item" style={{ '--mc': MC } as React.CSSProperties}>
              <span className="feat-icon">{f.icon}</span>
              <span>{f.text}</span>
            </div>
          ))}
        </div>
      </div>
    </div>
  );
}

/* ── Reminders ── */
type ReminderFormState = { text: string; dueAt: string; notifyTelegram: boolean };
const emptyReminderForm: ReminderFormState = { text: '', dueAt: '', notifyTelegram: true };

function ReminderForm({ initial, onSubmit, onCancel, submitting }: {
  initial: ReminderFormState; onSubmit: (f: ReminderFormState) => void; onCancel: () => void; submitting: boolean;
}) {
  const [f, setF] = useState(initial);
  return (
    <div className="san-inline-form">
      <input placeholder="Reminder text…" value={f.text} onChange={e => setF({ ...f, text: e.target.value })} style={{ flex: 2 }} />
      <input type="datetime-local" value={f.dueAt} onChange={e => setF({ ...f, dueAt: e.target.value })} />
      <label className="san-checkbox-label">
        <input type="checkbox" checked={f.notifyTelegram} onChange={e => setF({ ...f, notifyTelegram: e.target.checked })} />
        Telegram
      </label>
      <button className="btn-primary" disabled={submitting || !f.text.trim() || !f.dueAt} onClick={() => onSubmit(f)}>Save</button>
      <button className="btn-ghost" onClick={onCancel}>Cancel</button>
    </div>
  );
}

function Reminders() {
  const queryClient = useQueryClient();
  const [adding, setAdding] = useState(false);
  const [editingId, setEditingId] = useState<string | null>(null);

  const remindersQ = useQuery<Reminder[]>({ queryKey: ['san-reminders'], queryFn: () => get(`${API}/api/reminders`) });

  const invalidate = () => queryClient.invalidateQueries({ queryKey: ['san-reminders'] });
  const createMut = useMutation({
    mutationFn: (f: ReminderFormState) => send(`${API}/api/reminders`, 'POST', { text: f.text, dueAt: new Date(f.dueAt).toISOString(), notifyTelegram: f.notifyTelegram }),
    onSuccess: () => { invalidate(); setAdding(false); },
  });
  const updateMut = useMutation({
    mutationFn: ({ id, f }: { id: string; f: ReminderFormState }) =>
      send(`${API}/api/reminders/${id}`, 'PUT', { text: f.text, dueAt: new Date(f.dueAt).toISOString(), notifyTelegram: f.notifyTelegram }),
    onSuccess: () => { invalidate(); setEditingId(null); },
  });
  const toggleDoneMut = useMutation({
    mutationFn: ({ id, done }: { id: string; done: boolean }) => send(`${API}/api/reminders/${id}/done`, 'PATCH', done),
    onSuccess: invalidate,
  });
  const deleteMut = useMutation({
    mutationFn: (id: string) => send(`${API}/api/reminders/${id}`, 'DELETE'),
    onSuccess: invalidate,
  });

  if (remindersQ.isError) return <ApiError port={5300} />;
  const reminders = remindersQ.data ?? [];

  return (
    <div style={style}>
      <div className="san-toolbar">
        <h3 style={{ margin: 0 }}>Reminders</h3>
        <button className="btn-primary" onClick={() => setAdding(a => !a)}>{adding ? 'Close' : '+ Add Reminder'}</button>
      </div>
      {adding && (
        <ReminderForm initial={emptyReminderForm} submitting={createMut.isPending} onCancel={() => setAdding(false)} onSubmit={f => createMut.mutate(f)} />
      )}
      {reminders.length === 0 && !remindersQ.isLoading && <p className="text-dim" style={{ textAlign: 'center', marginTop: '1.5rem' }}>No reminders yet.</p>}
      {reminders.map(r => (
        <div key={r.id}>
          {editingId === r.id ? (
            <ReminderForm
              initial={{ text: r.text, dueAt: toLocalInputValue(r.dueAt), notifyTelegram: r.notifyTelegram }}
              submitting={updateMut.isPending}
              onCancel={() => setEditingId(null)}
              onSubmit={f => updateMut.mutate({ id: r.id, f })}
            />
          ) : (
            <div className="reminder-item">
              <div
                className="reminder-check"
                style={r.done ? { background: 'var(--san)', borderColor: 'var(--san)' } : {}}
                onClick={() => toggleDoneMut.mutate({ id: r.id, done: !r.done })}
              />
              <span className="reminder-text" style={r.done ? { textDecoration: 'line-through', color: 'var(--text3)' } : {}}>{r.text}</span>
              {r.notifyTelegram && <span className="san-tg-badge" title={r.notifiedAt ? `Notified ${fmtDateTime(r.notifiedAt)}` : 'Will notify via Telegram'}>✈ {r.notifiedAt ? 'sent' : 'armed'}</span>}
              <span className="reminder-due">{fmtDateTime(r.dueAt)}</span>
              <button className="btn-ghost" style={{ fontSize: '0.72rem' }} onClick={() => setEditingId(r.id)}>Edit</button>
              <button className="btn-danger-ghost" style={{ fontSize: '0.72rem' }} onClick={() => deleteMut.mutate(r.id)}>Delete</button>
            </div>
          )}
        </div>
      ))}
    </div>
  );
}

/* ── Alerts ── */
type AlertFormState = {
  type: string; title: string; description: string;
  thresholdValue: string; triggerAt: string; active: boolean; notifyTelegram: boolean;
};
const emptyAlertForm: AlertFormState = { type: 'spending_threshold', title: '', description: '', thresholdValue: '', triggerAt: '', active: true, notifyTelegram: true };

function AlertForm({ initial, onSubmit, onCancel, submitting }: {
  initial: AlertFormState; onSubmit: (f: AlertFormState) => void; onCancel: () => void; submitting: boolean;
}) {
  const [f, setF] = useState(initial);
  const isThreshold = f.type === 'spending_threshold';
  return (
    <div className="san-form-grid">
      <div className="san-form-group">
        <label>Type</label>
        <select value={f.type} onChange={e => setF({ ...f, type: e.target.value })}>
          {ALERT_TYPES.map(t => <option key={t.id} value={t.id}>{t.label}</option>)}
        </select>
      </div>
      <div className="san-form-group san-form-span2">
        <label>Title</label>
        <input value={f.title} onChange={e => setF({ ...f, title: e.target.value })} />
      </div>
      <div className="san-form-group san-form-span2">
        <label>Description</label>
        <input value={f.description} onChange={e => setF({ ...f, description: e.target.value })} />
      </div>
      {isThreshold ? (
        <div className="san-form-group">
          <label>Threshold ($, 30-day spend)</label>
          <input type="number" value={f.thresholdValue} onChange={e => setF({ ...f, thresholdValue: e.target.value })} />
        </div>
      ) : (
        <div className="san-form-group">
          <label>Trigger At</label>
          <input type="datetime-local" value={f.triggerAt} onChange={e => setF({ ...f, triggerAt: e.target.value })} />
        </div>
      )}
      <div className="san-form-group" style={{ flexDirection: 'row', alignItems: 'center', gap: '1.25rem' }}>
        <label className="san-checkbox-label">
          <input type="checkbox" checked={f.active} onChange={e => setF({ ...f, active: e.target.checked })} /> Active
        </label>
        <label className="san-checkbox-label">
          <input type="checkbox" checked={f.notifyTelegram} onChange={e => setF({ ...f, notifyTelegram: e.target.checked })} /> Notify via Telegram
        </label>
      </div>
      <div className="san-form-actions san-form-span2">
        <button className="btn-primary" disabled={submitting || !f.title.trim()} onClick={() => onSubmit(f)}>Save</button>
        <button className="btn-ghost" onClick={onCancel}>Cancel</button>
      </div>
    </div>
  );
}

function Alerts() {
  const queryClient = useQueryClient();
  const [adding, setAdding] = useState(false);
  const [editingId, setEditingId] = useState<string | null>(null);

  const alertsQ = useQuery<AlertItem[]>({ queryKey: ['san-alerts'], queryFn: () => get(`${API}/api/alerts`) });
  const invalidate = () => queryClient.invalidateQueries({ queryKey: ['san-alerts'] });

  const toBody = (f: AlertFormState) => ({
    type: f.type, title: f.title, description: f.description,
    thresholdValue: f.type === 'spending_threshold' && f.thresholdValue ? Number(f.thresholdValue) : null,
    triggerAt: f.type !== 'spending_threshold' && f.triggerAt ? new Date(f.triggerAt).toISOString() : null,
    active: f.active, notifyTelegram: f.notifyTelegram,
  });

  const createMut = useMutation({ mutationFn: (f: AlertFormState) => send(`${API}/api/alerts`, 'POST', toBody(f)), onSuccess: () => { invalidate(); setAdding(false); } });
  const updateMut = useMutation({ mutationFn: ({ id, f }: { id: string; f: AlertFormState }) => send(`${API}/api/alerts/${id}`, 'PUT', toBody(f)), onSuccess: () => { invalidate(); setEditingId(null); } });
  const deleteMut = useMutation({ mutationFn: (id: string) => send(`${API}/api/alerts/${id}`, 'DELETE'), onSuccess: invalidate });

  if (alertsQ.isError) return <ApiError port={5300} />;
  const alerts = alertsQ.data ?? [];

  return (
    <div style={style}>
      <div className="san-toolbar">
        <h3 style={{ margin: 0 }}>Custom Alerts</h3>
        <button className="btn-primary" onClick={() => setAdding(a => !a)}>{adding ? 'Close' : '+ Add Alert'}</button>
      </div>
      {adding && <AlertForm initial={emptyAlertForm} submitting={createMut.isPending} onCancel={() => setAdding(false)} onSubmit={f => createMut.mutate(f)} />}
      {alerts.length === 0 && !alertsQ.isLoading && <p className="text-dim" style={{ textAlign: 'center', marginTop: '1.5rem' }}>No alerts configured.</p>}
      <div className="san-alert-list">
        {alerts.map(a => (
          <div key={a.id} className="card san-alert-card">
            {editingId === a.id ? (
              <AlertForm
                initial={{
                  type: a.type, title: a.title, description: a.description,
                  thresholdValue: a.thresholdValue?.toString() ?? '', triggerAt: a.triggerAt ? toLocalInputValue(a.triggerAt) : '',
                  active: a.active, notifyTelegram: a.notifyTelegram,
                }}
                submitting={updateMut.isPending}
                onCancel={() => setEditingId(null)}
                onSubmit={f => updateMut.mutate({ id: a.id, f })}
              />
            ) : (
              <>
                <div className="san-alert-top">
                  <div>
                    <span className="san-type-badge" style={{ background: `color-mix(in srgb, ${ALERT_TYPE_COLOR[a.type]} 18%, transparent)`, color: ALERT_TYPE_COLOR[a.type] }}>
                      {ALERT_TYPES.find(t => t.id === a.type)?.label ?? a.type}
                    </span>
                    <div className="san-alert-title">{a.title}</div>
                    {a.description && <div className="text-dim" style={{ fontSize: '0.78rem' }}>{a.description}</div>}
                  </div>
                  <div className="san-alert-actions">
                    {!a.active && <span className="text-dim" style={{ fontSize: '0.7rem' }}>inactive</span>}
                    <button className="btn-ghost" style={{ fontSize: '0.72rem' }} onClick={() => setEditingId(a.id)}>Edit</button>
                    <button className="btn-danger-ghost" style={{ fontSize: '0.72rem' }} onClick={() => deleteMut.mutate(a.id)}>Delete</button>
                  </div>
                </div>
                <div className="san-alert-meta">
                  {a.type === 'spending_threshold'
                    ? <span>Threshold: ${a.thresholdValue?.toLocaleString()}</span>
                    : <span>Triggers: {fmtDateTime(a.triggerAt)}</span>}
                  {a.triggeredAt && <span className="text-debt">• Triggered {fmtDateTime(a.triggeredAt)}</span>}
                  {a.notifyTelegram && <span className="san-tg-badge">✈ Telegram</span>}
                </div>
              </>
            )}
          </div>
        ))}
      </div>
    </div>
  );
}

/* ── Activity Feed ── */
function Feed() {
  const feedQ = useQuery<FeedResult>({ queryKey: ['san-feed'], queryFn: () => get(`${API}/api/feed`) });
  if (feedQ.isError) return <ApiError port={5300} />;
  const data = feedQ.data;

  return (
    <div style={style}>
      <div className="san-module-status">
        {(data?.modules ?? []).map(m => (
          <span key={m.module} className={`san-module-pill ${m.reachable ? '' : 'down'}`} style={{ '--mc': MODULE_COLOR[m.module] ?? MC } as React.CSSProperties}>
            ● {m.module} {m.reachable ? 'online' : 'unreachable'}
          </span>
        ))}
      </div>
      {(!data || data.entries.length === 0) && !feedQ.isLoading && (
        <p className="text-dim" style={{ textAlign: 'center', marginTop: '1.5rem' }}>No recent activity from connected modules.</p>
      )}
      <div className="san-feed-list">
        {(data?.entries ?? []).map((e, i) => (
          <div key={i} className="san-feed-item">
            <span className="san-feed-module" style={{ color: MODULE_COLOR[e.module] ?? MC }}>{e.module}</span>
            <div className="san-feed-body">
              <div className="san-feed-title">{e.title}</div>
              {e.description && <div className="text-dim" style={{ fontSize: '0.78rem' }}>{e.description}</div>}
            </div>
            <span className="san-feed-time">{fmtDateTime(e.occurredAt)}</span>
          </div>
        ))}
      </div>
    </div>
  );
}

function SanModuleInner() {
  const [page, setPage] = useState<Page>('assistant');
  return (
    <div>
      <nav className="module-subnav" style={style}>
        {TABS.map(t => (
          <button key={t.id} className={`module-tab ${page === t.id ? 'active' : ''}`} onClick={() => setPage(t.id)}>
            {t.label}
          </button>
        ))}
      </nav>
      {page === 'assistant' && <Assistant />}
      {page === 'reminders' && <Reminders />}
      {page === 'alerts'    && <Alerts />}
      {page === 'feed'      && <Feed />}
    </div>
  );
}

export default function SanModule() {
  return (
    <QueryClientProvider client={qc}>
      <SanModuleInner />
    </QueryClientProvider>
  );
}
