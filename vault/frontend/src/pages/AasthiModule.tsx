import { useState } from 'react';
import { QueryClientProvider, useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { makeModuleQueryClient } from '../services/moduleQuery';
import { authHeaders } from '../services/auth';
import { moduleApi } from '../services/apiHost';
import '../styles/modules.css';
import '../styles/aasthi.css';

const API = moduleApi(5200);
const MC = 'var(--aasthi)';
const style = { '--mc': MC } as React.CSSProperties;

const qc = makeModuleQueryClient(60_000);

const get = (url: string) => fetch(url, { headers: authHeaders() }).then(r => { if (!r.ok) throw new Error(r.status.toString()); return r.json(); });
const send = (url: string, method: string, body?: unknown) =>
  fetch(url, {
    method,
    headers: { ...authHeaders(), ...(body ? { 'Content-Type': 'application/json' } : {}) },
    body: body ? JSON.stringify(body) : undefined,
  }).then(r => { if (!r.ok) throw new Error(r.status.toString()); return r.status === 204 ? null : r.json(); });

/* ── Types ── */
interface Property {
  id: string;
  address: string; city: string; state: string; zip: string; country: string;
  latitude: number | null; longitude: number | null;
  purchasePrice: number; purchaseDate: string | null;
  currentValue: number; currentValueAsOf: string | null;
  notes: string; createdAt: string;
  profitAmount: number; profitPct: number | null;
  contactCount: number; documentCount: number;
}
interface Contact {
  id: string; propertyId: string;
  name: string; role: string; phone: string; email: string; notes: string;
}
interface Document {
  id: string; propertyId: string;
  fileName: string; contentType: string; sizeBytes: number; category: string; uploadedAt: string;
}
interface PropertyDetail extends Omit<Property, 'contactCount' | 'documentCount'> {
  contacts: Contact[];
  documents: Document[];
}
interface PortfolioSummary {
  propertyCount: number; totalPurchasePrice: number; totalCurrentValue: number;
  totalProfit: number; totalProfitPct: number | null;
}
interface TaskItem {
  id: string; propertyId: string; title: string; description: string;
  dueDate: string | null; status: string; priority: string; source: string;
  createdAt: string; completedAt: string | null;
}

const fmtMoney = (n: number) => '$' + n.toLocaleString('en-US', { maximumFractionDigits: 0 });
const fmtPct = (n: number | null) => n == null ? '—' : `${n >= 0 ? '+' : ''}${n.toFixed(1)}%`;
const fmtBytes = (n: number) => n < 1024 ? `${n} B` : n < 1024 * 1024 ? `${(n / 1024).toFixed(0)} KB` : `${(n / 1024 / 1024).toFixed(1)} MB`;
const fmtDate = (d: string | null) => d ? new Date(d).toLocaleDateString('en-US', { year: 'numeric', month: 'short', day: 'numeric' }) : '—';

const DOC_CATEGORIES = ['deed', 'insurance', 'lease', 'tax', 'inspection', 'other'];
const DOC_CATEGORY_COLOR: Record<string, string> = {
  deed: '#4f9ef8', insurance: '#1fc87a', lease: '#a855f7', tax: '#e84444', inspection: '#d4a843', other: '#7a96c0',
};

type PropertyFormState = {
  address: string; city: string; state: string; zip: string; country: string;
  latitude: string; longitude: string;
  purchasePrice: string; purchaseDate: string;
  currentValue: string; currentValueAsOf: string;
  notes: string;
};
const emptyForm: PropertyFormState = {
  address: '', city: '', state: '', zip: '', country: 'USA',
  latitude: '', longitude: '', purchasePrice: '', purchaseDate: '',
  currentValue: '', currentValueAsOf: '', notes: '',
};

function propertyToForm(p: PropertyDetail): PropertyFormState {
  return {
    address: p.address, city: p.city, state: p.state, zip: p.zip, country: p.country,
    latitude: p.latitude?.toString() ?? '', longitude: p.longitude?.toString() ?? '',
    purchasePrice: p.purchasePrice.toString(), purchaseDate: p.purchaseDate ?? '',
    currentValue: p.currentValue.toString(), currentValueAsOf: p.currentValueAsOf ?? '',
    notes: p.notes,
  };
}

function PropertyForm({ initial, onSubmit, onCancel, submitting }: {
  initial: PropertyFormState;
  onSubmit: (f: PropertyFormState) => void;
  onCancel: () => void;
  submitting: boolean;
}) {
  const [f, setF] = useState(initial);
  const set = (k: keyof PropertyFormState) => (e: React.ChangeEvent<HTMLInputElement>) => setF(prev => ({ ...prev, [k]: e.target.value }));

  return (
    <div className="card aasthi-form">
      <div className="aasthi-form-grid">
        <div className="aasthi-form-group aasthi-form-span2">
          <label>Address</label>
          <input placeholder="123 Maple St" value={f.address} onChange={set('address')} />
        </div>
        <div className="aasthi-form-group">
          <label>City</label>
          <input value={f.city} onChange={set('city')} />
        </div>
        <div className="aasthi-form-group">
          <label>State</label>
          <input value={f.state} onChange={set('state')} />
        </div>
        <div className="aasthi-form-group">
          <label>Zip</label>
          <input value={f.zip} onChange={set('zip')} />
        </div>
        <div className="aasthi-form-group">
          <label>Country</label>
          <input value={f.country} onChange={set('country')} />
        </div>
        <div className="aasthi-form-group">
          <label>Latitude</label>
          <input type="number" step="any" placeholder="30.2672" value={f.latitude} onChange={set('latitude')} />
        </div>
        <div className="aasthi-form-group">
          <label>Longitude</label>
          <input type="number" step="any" placeholder="-97.7431" value={f.longitude} onChange={set('longitude')} />
        </div>
        <div className="aasthi-form-group">
          <label>Purchase Price</label>
          <input type="number" step="any" value={f.purchasePrice} onChange={set('purchasePrice')} />
        </div>
        <div className="aasthi-form-group">
          <label>Purchase Date</label>
          <input type="date" value={f.purchaseDate} onChange={set('purchaseDate')} />
        </div>
        <div className="aasthi-form-group">
          <label>Current Value</label>
          <input type="number" step="any" value={f.currentValue} onChange={set('currentValue')} />
        </div>
        <div className="aasthi-form-group">
          <label>Current Value As Of</label>
          <input type="date" value={f.currentValueAsOf} onChange={set('currentValueAsOf')} />
        </div>
        <div className="aasthi-form-group aasthi-form-span2">
          <label>Notes</label>
          <input value={f.notes} onChange={set('notes')} />
        </div>
      </div>
      <div className="aasthi-form-actions">
        <button className="btn-primary" disabled={!f.address || submitting} onClick={() => onSubmit(f)}>
          {submitting ? 'Saving…' : 'Save'}
        </button>
        <button className="btn-ghost" onClick={onCancel}>Cancel</button>
      </div>
    </div>
  );
}

function ContactsSection({ propertyId, contacts }: { propertyId: string; contacts: Contact[] }) {
  const qClient = useQueryClient();
  const [adding, setAdding] = useState(false);
  const [form, setForm] = useState({ name: '', role: '', phone: '', email: '', notes: '' });

  const invalidate = () => qClient.invalidateQueries({ queryKey: ['property', propertyId] });

  const addContact = useMutation({
    mutationFn: () => send(`${API}/api/properties/${propertyId}/contacts`, 'POST', form),
    onSuccess: () => { setAdding(false); setForm({ name: '', role: '', phone: '', email: '', notes: '' }); invalidate(); },
  });
  const deleteContact = useMutation({
    mutationFn: (contactId: string) => send(`${API}/api/properties/${propertyId}/contacts/${contactId}`, 'DELETE'),
    onSuccess: invalidate,
  });

  return (
    <div className="aasthi-subsection">
      <div className="aasthi-section-label">Contacts</div>
      {contacts.length === 0 && !adding && <p className="text-dim">No contacts yet.</p>}
      <div className="aasthi-contact-list">
        {contacts.map(c => (
          <div key={c.id} className="aasthi-contact-row">
            <div className="aasthi-contact-info">
              <span className="aasthi-contact-name">{c.name}</span>
              {c.role && <span className="aasthi-contact-role">{c.role}</span>}
              <span className="text-dim">{[c.phone, c.email].filter(Boolean).join(' · ')}</span>
            </div>
            <button className="btn-danger-ghost" onClick={() => deleteContact.mutate(c.id)}>×</button>
          </div>
        ))}
      </div>

      {adding ? (
        <div className="aasthi-inline-form">
          <input placeholder="Name" value={form.name} onChange={e => setForm(f => ({ ...f, name: e.target.value }))} />
          <input placeholder="Role (agent, contractor…)" value={form.role} onChange={e => setForm(f => ({ ...f, role: e.target.value }))} />
          <input placeholder="Phone" value={form.phone} onChange={e => setForm(f => ({ ...f, phone: e.target.value }))} />
          <input placeholder="Email" value={form.email} onChange={e => setForm(f => ({ ...f, email: e.target.value }))} />
          <button className="btn-primary" disabled={!form.name || addContact.isPending} onClick={() => addContact.mutate()}>Add</button>
          <button className="btn-ghost" onClick={() => setAdding(false)}>Cancel</button>
        </div>
      ) : (
        <button className="btn-ghost" style={{ fontSize: '0.78rem', marginTop: '0.5rem' }} onClick={() => setAdding(true)}>+ Add Contact</button>
      )}
    </div>
  );
}

function DocumentsSection({ propertyId, documents }: { propertyId: string; documents: Document[] }) {
  const qClient = useQueryClient();
  const [category, setCategory] = useState('other');
  const [files, setFiles] = useState<FileList | null>(null);

  const invalidate = () => qClient.invalidateQueries({ queryKey: ['property', propertyId] });

  const upload = useMutation({
    mutationFn: () => {
      const fd = new FormData();
      Array.from(files!).forEach(f => fd.append('files', f));
      fd.append('category', category);
      return fetch(`${API}/api/properties/${propertyId}/documents`, { method: 'POST', headers: authHeaders(), body: fd })
        .then(r => { if (!r.ok) throw new Error(r.status.toString()); return r.json(); });
    },
    onSuccess: () => { setFiles(null); invalidate(); },
  });
  const deleteDoc = useMutation({
    mutationFn: (docId: string) => send(`${API}/api/properties/${propertyId}/documents/${docId}`, 'DELETE'),
    onSuccess: invalidate,
  });

  return (
    <div className="aasthi-subsection">
      <div className="aasthi-section-label">Documents</div>
      {documents.length === 0 && <p className="text-dim">No documents yet.</p>}
      <div>
        {documents.map(d => (
          <div key={d.id} className="doc-item">
            <span className="doc-type-badge" style={{ background: `${DOC_CATEGORY_COLOR[d.category] ?? '#7a96c0'}22`, color: DOC_CATEGORY_COLOR[d.category] ?? '#7a96c0' }}>
              {d.category}
            </span>
            <a className="doc-name" href={`${API}/api/properties/${propertyId}/documents/${d.id}/download`} target="_blank" rel="noreferrer">
              {d.fileName}
            </a>
            <span className="doc-meta">{fmtBytes(d.sizeBytes)} · {fmtDate(d.uploadedAt)}</span>
            <button className="btn-danger-ghost" onClick={() => deleteDoc.mutate(d.id)}>×</button>
          </div>
        ))}
      </div>

      <div className="aasthi-upload-row">
        <select value={category} onChange={e => setCategory(e.target.value)}>
          {DOC_CATEGORIES.map(c => <option key={c} value={c}>{c}</option>)}
        </select>
        <input type="file" multiple onChange={e => setFiles(e.target.files)} />
        <button className="btn-primary" disabled={!files || files.length === 0 || upload.isPending} onClick={() => upload.mutate()}>
          {upload.isPending ? 'Uploading…' : 'Upload'}
        </button>
      </div>
    </div>
  );
}

function PropertyCard({ summary }: { summary: Property }) {
  const qClient = useQueryClient();
  const [open, setOpen] = useState(false);
  const [editing, setEditing] = useState(false);

  const { data: detail } = useQuery<PropertyDetail>({
    queryKey: ['property', summary.id],
    queryFn: () => get(`${API}/api/properties/${summary.id}`),
    enabled: open,
  });

  const update = useMutation({
    mutationFn: (f: PropertyFormState) => send(`${API}/api/properties/${summary.id}`, 'PUT', toUpsertBody(f)),
    onSuccess: () => {
      setEditing(false);
      qClient.invalidateQueries({ queryKey: ['properties'] });
      qClient.invalidateQueries({ queryKey: ['property', summary.id] });
      qClient.invalidateQueries({ queryKey: ['portfolio-summary'] });
    },
  });
  const remove = useMutation({
    mutationFn: () => send(`${API}/api/properties/${summary.id}`, 'DELETE'),
    onSuccess: () => {
      qClient.invalidateQueries({ queryKey: ['properties'] });
      qClient.invalidateQueries({ queryKey: ['portfolio-summary'] });
    },
  });

  const profitPositive = summary.profitAmount >= 0;
  const mapsHref = summary.latitude != null && summary.longitude != null
    ? `https://www.google.com/maps?q=${summary.latitude},${summary.longitude}`
    : null;

  return (
    <div className="card aasthi-property-card">
      <div className="aasthi-property-top">
        <div className="aasthi-property-info">
          <div className="aasthi-property-address">{summary.address}</div>
          <div className="text-dim">{[summary.city, summary.state, summary.zip].filter(Boolean).join(', ')}</div>
          {mapsHref && <a className="aasthi-map-link" href={mapsHref} target="_blank" rel="noreferrer">View on map ↗</a>}
        </div>

        <div className="aasthi-property-stats">
          <div className="aasthi-stat">
            <span className="aasthi-stat-label">Purchase</span>
            <span className="aasthi-stat-value">{fmtMoney(summary.purchasePrice)}</span>
          </div>
          <div className="aasthi-stat">
            <span className="aasthi-stat-label">Current</span>
            <span className="aasthi-stat-value">{fmtMoney(summary.currentValue)}</span>
          </div>
          <div className="aasthi-stat">
            <span className="aasthi-stat-label">Profit</span>
            <span className={`aasthi-stat-value ${profitPositive ? 'text-cash' : 'text-debt'}`}>
              {profitPositive ? '+' : ''}{fmtMoney(summary.profitAmount)} ({fmtPct(summary.profitPct)})
            </span>
          </div>
        </div>

        <div className="aasthi-property-actions">
          <button className="btn-ghost" onClick={() => setOpen(o => !o)}>{open ? '▲ Close' : '▼ Details'}</button>
          <button className="btn-danger-ghost" onClick={() => { if (window.confirm(`Delete "${summary.address}"? This removes its contacts and documents too.`)) remove.mutate(); }}>
            Delete
          </button>
        </div>
      </div>

      {open && detail && !editing && (
        <div className="aasthi-property-detail">
          {detail.notes && <p className="text-dim" style={{ marginBottom: '0.875rem' }}>{detail.notes}</p>}
          <div className="aasthi-detail-actions">
            <button className="btn-ghost" onClick={() => setEditing(true)}>Edit Property</button>
          </div>
          <ContactsSection propertyId={summary.id} contacts={detail.contacts} />
          <DocumentsSection propertyId={summary.id} documents={detail.documents} />
        </div>
      )}

      {open && editing && detail && (
        <PropertyForm
          initial={propertyToForm(detail)}
          submitting={update.isPending}
          onSubmit={f => update.mutate(f)}
          onCancel={() => setEditing(false)}
        />
      )}
    </div>
  );
}

function toUpsertBody(f: PropertyFormState) {
  return {
    address: f.address,
    city: f.city,
    state: f.state,
    zip: f.zip,
    country: f.country || 'USA',
    latitude: f.latitude ? parseFloat(f.latitude) : null,
    longitude: f.longitude ? parseFloat(f.longitude) : null,
    purchasePrice: parseFloat(f.purchasePrice) || 0,
    purchaseDate: f.purchaseDate || null,
    currentValue: parseFloat(f.currentValue) || 0,
    currentValueAsOf: f.currentValueAsOf || null,
    notes: f.notes,
  };
}

function Properties() {
  const qClient = useQueryClient();
  const [creating, setCreating] = useState(false);

  const { data: properties, isPending, isError } = useQuery<Property[]>({
    queryKey: ['properties'],
    queryFn: () => get(`${API}/api/properties`),
  });
  const { data: summary } = useQuery<PortfolioSummary>({
    queryKey: ['portfolio-summary'],
    queryFn: () => get(`${API}/api/properties/summary`),
  });

  const create = useMutation({
    mutationFn: (f: PropertyFormState) => send(`${API}/api/properties`, 'POST', toUpsertBody(f)),
    onSuccess: () => {
      setCreating(false);
      qClient.invalidateQueries({ queryKey: ['properties'] });
      qClient.invalidateQueries({ queryKey: ['portfolio-summary'] });
    },
  });

  if (isError) {
    return (
      <div className="module-empty" style={style}>
        <h2>Can't reach Aasthi API</h2>
        <p>Make sure the Aasthi backend is running on port 5200 (<code>start.ps1</code> in the aasthi folder).</p>
      </div>
    );
  }

  return (
    <div style={style}>
      <div className="placeholder-grid">
        <div className="placeholder-metric">
          <div className="pm-label">Total Portfolio Value</div>
          <div className="pm-value">{summary ? fmtMoney(summary.totalCurrentValue) : '—'}</div>
          <div className="pm-sub">{summary ? `${summary.propertyCount} ${summary.propertyCount === 1 ? 'property' : 'properties'}` : ''}</div>
        </div>
        <div className="placeholder-metric">
          <div className="pm-label">Total Purchase Price</div>
          <div className="pm-value">{summary ? fmtMoney(summary.totalPurchasePrice) : '—'}</div>
          <div className="pm-sub">Across all properties</div>
        </div>
        <div className="placeholder-metric">
          <div className="pm-label">Total Profit</div>
          <div className="pm-value" style={{ color: summary && summary.totalProfit < 0 ? 'var(--debt)' : 'var(--cash)' }}>
            {summary ? `${summary.totalProfit >= 0 ? '+' : ''}${fmtMoney(summary.totalProfit)}` : '—'}
          </div>
          <div className="pm-sub">{summary ? fmtPct(summary.totalProfitPct) : ''}</div>
        </div>
      </div>

      <div className="aasthi-toolbar">
        <button className="btn-primary" onClick={() => setCreating(c => !c)}>{creating ? 'Cancel' : '+ Add Property'}</button>
      </div>

      {creating && (
        <PropertyForm
          initial={emptyForm}
          submitting={create.isPending}
          onSubmit={f => create.mutate(f)}
          onCancel={() => setCreating(false)}
        />
      )}

      {isPending && <p className="text-dim">Loading…</p>}

      {properties && properties.length === 0 && !creating && (
        <div className="module-empty">
          <h2>No properties yet</h2>
          <p>Add your first property to start tracking purchase price, current value, contacts, and documents.</p>
        </div>
      )}

      <div className="aasthi-property-list">
        {properties?.map(p => <PropertyCard key={p.id} summary={p} />)}
      </div>
    </div>
  );
}

const PRIORITY_COLOR: Record<string, string> = {
  urgent: '#e84444', high: '#ff8c32', medium: '#d4a843', low: '#7a96c0',
};
const STATUS_LABELS: Record<string, string> = {
  pending: 'To Do', in_progress: 'In Progress', completed: 'Done', cancelled: 'Cancelled',
};

function TasksPage() {
  const qClient = useQueryClient();
  const [filter, setFilter] = useState<string>('');
  const [adding, setAdding] = useState(false);
  const [form, setForm] = useState({ title: '', description: '', dueDate: '', priority: 'medium', propertyId: '' });

  const { data: tasks, isPending, isError } = useQuery<TaskItem[]>({
    queryKey: ['tasks', filter],
    queryFn: () => get(`${API}/api/tasks${filter ? `?status=${filter}` : ''}`),
  });
  const { data: properties } = useQuery<Property[]>({ queryKey: ['properties'], queryFn: () => get(`${API}/api/properties`) });

  const invalidate = () => qClient.invalidateQueries({ queryKey: ['tasks'] });

  const createTask = useMutation({
    mutationFn: () => send(`${API}/api/tasks?propertyId=${form.propertyId}`, 'POST', {
      title: form.title, description: form.description || null,
      dueDate: form.dueDate || null, priority: form.priority,
    }),
    onSuccess: () => { setAdding(false); setForm({ title: '', description: '', dueDate: '', priority: 'medium', propertyId: '' }); invalidate(); },
  });
  const updateStatus = useMutation({
    mutationFn: ({ id, status }: { id: string; status: string }) => send(`${API}/api/tasks/${id}/status`, 'PATCH', { status }),
    onSuccess: invalidate,
  });
  const deleteTask = useMutation({
    mutationFn: (id: string) => send(`${API}/api/tasks/${id}`, 'DELETE'),
    onSuccess: invalidate,
  });

  const propMap = new Map(properties?.map(p => [p.id, p.address]) ?? []);
  const overdue = (t: TaskItem) => t.dueDate && t.status !== 'completed' && t.status !== 'cancelled' && new Date(t.dueDate) < new Date();

  if (isError) {
    return (
      <div className="module-empty" style={style}>
        <h2>Can't reach Aasthi API</h2>
        <p>Make sure the Aasthi backend is running on port 5200 (<code>start.ps1</code> in the aasthi folder).</p>
      </div>
    );
  }

  return (
    <div style={style}>
      <div className="aasthi-task-filters">
        {['', 'pending', 'in_progress', 'completed'].map(s => (
          <button key={s} className={`aasthi-filter-btn ${filter === s ? 'active' : ''}`} onClick={() => setFilter(s)}>
            {s ? (STATUS_LABELS[s] ?? s) : 'All'}
          </button>
        ))}
        <button className="btn-primary" style={{ marginLeft: 'auto' }} onClick={() => setAdding(a => !a)}>
          {adding ? 'Cancel' : '+ New Task'}
        </button>
      </div>

      {adding && (
        <div className="card aasthi-task-form">
          <div className="aasthi-form-grid">
            <div className="aasthi-form-group aasthi-form-span2">
              <label>Title</label>
              <input placeholder="e.g. Renew tenant lease" value={form.title} onChange={e => setForm(f => ({ ...f, title: e.target.value }))} />
            </div>
            <div className="aasthi-form-group aasthi-form-span2">
              <label>Description</label>
              <input placeholder="Details (optional)" value={form.description} onChange={e => setForm(f => ({ ...f, description: e.target.value }))} />
            </div>
            <div className="aasthi-form-group">
              <label>Property</label>
              <select value={form.propertyId} onChange={e => setForm(f => ({ ...f, propertyId: e.target.value }))}>
                <option value="">Select property...</option>
                {properties?.map(p => <option key={p.id} value={p.id}>{p.address}</option>)}
              </select>
            </div>
            <div className="aasthi-form-group">
              <label>Due Date</label>
              <input type="date" value={form.dueDate} onChange={e => setForm(f => ({ ...f, dueDate: e.target.value }))} />
            </div>
            <div className="aasthi-form-group">
              <label>Priority</label>
              <select value={form.priority} onChange={e => setForm(f => ({ ...f, priority: e.target.value }))}>
                <option value="low">Low</option>
                <option value="medium">Medium</option>
                <option value="high">High</option>
                <option value="urgent">Urgent</option>
              </select>
            </div>
          </div>
          <div className="aasthi-form-actions">
            <button className="btn-primary" disabled={!form.title || !form.propertyId || createTask.isPending}
              onClick={() => createTask.mutate()}>
              {createTask.isPending ? 'Creating...' : 'Create Task'}
            </button>
          </div>
        </div>
      )}

      {isPending && <p className="text-dim">Loading tasks...</p>}

      {tasks && tasks.length === 0 && !adding && (
        <div className="module-empty">
          <h2>No tasks yet</h2>
          <p>Create tasks for your properties — track lease renewals, inspections, repairs, and more. SAN will eventually create tasks here automatically from your emails and events.</p>
        </div>
      )}

      <div className="aasthi-task-list">
        {tasks?.map(t => (
          <div key={t.id} className={`aasthi-task-card ${overdue(t) ? 'overdue' : ''} ${t.status === 'completed' ? 'done' : ''}`}>
            <div className="aasthi-task-left">
              <button className={`aasthi-task-check ${t.status === 'completed' ? 'checked' : ''}`}
                onClick={() => updateStatus.mutate({ id: t.id, status: t.status === 'completed' ? 'pending' : 'completed' })}>
                {t.status === 'completed' ? '✓' : ''}
              </button>
            </div>
            <div className="aasthi-task-body">
              <div className="aasthi-task-title-row">
                <span className={`aasthi-task-title ${t.status === 'completed' ? 'struck' : ''}`}>{t.title}</span>
                <span className="aasthi-task-priority" style={{ color: PRIORITY_COLOR[t.priority] }}>{t.priority}</span>
                {t.source !== 'manual' && <span className="aasthi-task-source">{t.source}</span>}
              </div>
              {t.description && <div className="aasthi-task-desc">{t.description}</div>}
              <div className="aasthi-task-meta">
                <span>{propMap.get(t.propertyId) ?? 'Unknown property'}</span>
                {t.dueDate && (
                  <span className={overdue(t) ? 'text-debt' : ''}>
                    Due {fmtDate(t.dueDate)}
                  </span>
                )}
                {t.status !== 'completed' && t.status !== 'pending' && (
                  <span className="aasthi-task-status-badge">{STATUS_LABELS[t.status] ?? t.status}</span>
                )}
              </div>
            </div>
            <button className="btn-danger-ghost" onClick={() => deleteTask.mutate(t.id)}>x</button>
          </div>
        ))}
      </div>
    </div>
  );
}

/* ── Financials & Maintenance tabs (real data — replaced the old "Coming Soon" placeholder) ── */
interface FinancialEntry {
  id: string; propertyId: string; type: string; category: string;
  amount: number; date: string; notes: string | null; createdAt: string;
}
interface PropertyCashFlow {
  propertyId: string; address: string; income: number; expenses: number; mortgage: number;
  netCashFlow: number; appreciation: number; appreciationPct: number | null; cashOnCashPct: number | null;
}
interface FinancialsSummaryData {
  totalIncome: number; totalExpenses: number; totalMortgage: number;
  netCashFlow: number; byProperty: PropertyCashFlow[];
}

const FIN_TYPES = ['income', 'expense', 'mortgage'] as const;
const FIN_CATEGORIES: Record<string, string[]> = {
  income:   ['rent', 'other'],
  expense:  ['tax', 'insurance', 'repair', 'hoa', 'utility', 'other'],
  mortgage: ['mortgage_payment'],
};
const todayISO = () => new Date().toISOString().slice(0, 10);

function FinancialsPage() {
  const qClient = useQueryClient();
  const [adding, setAdding] = useState(false);
  const [form, setForm] = useState({ propertyId: '', type: 'expense', category: 'repair', amount: '', date: todayISO(), notes: '' });

  const { data: properties } = useQuery<Property[]>({ queryKey: ['properties'], queryFn: () => get(`${API}/api/properties`) });
  const summaryQ = useQuery<FinancialsSummaryData>({ queryKey: ['financials-summary'], queryFn: () => get(`${API}/api/financials/summary`) });
  const entriesQ = useQuery<FinancialEntry[]>({
    queryKey: ['financials', form.propertyId || 'all'],
    queryFn: () => {
      const pid = form.propertyId || properties?.[0]?.id;
      return pid ? get(`${API}/api/properties/${pid}/financials`) : Promise.resolve([]);
    },
    enabled: !!(form.propertyId || properties?.length),
  });

  const invalidate = () => {
    qClient.invalidateQueries({ queryKey: ['financials'] });
    qClient.invalidateQueries({ queryKey: ['financials-summary'] });
  };

  const create = useMutation({
    mutationFn: () => send(`${API}/api/properties/${form.propertyId}/financials`, 'POST', {
      type: form.type, category: form.category, amount: parseFloat(form.amount),
      date: form.date, notes: form.notes || null,
    }),
    onSuccess: () => { setForm(f => ({ ...f, amount: '', notes: '' })); setAdding(false); invalidate(); },
  });
  const remove = useMutation({
    mutationFn: (id: string) => send(`${API}/api/financials/${id}`, 'DELETE'),
    onSuccess: invalidate,
  });

  const summary = summaryQ.data;
  const entries = entriesQ.data ?? [];

  if (summaryQ.isError) {
    return (
      <div className="module-empty" style={style}>
        <h2>Can't reach Aasthi API</h2>
        <p>Make sure the Aasthi backend is running on port 5200 (<code>start.ps1</code> in the aasthi folder).</p>
      </div>
    );
  }

  return (
    <div style={style}>
      {summary && (
        <div className="placeholder-grid" style={{ marginBottom: '1.25rem' }}>
          <div className="placeholder-metric"><div className="pm-label">Total Income</div><div className="pm-value" style={{ color: 'var(--cash)' }}>{fmtMoney(summary.totalIncome)}</div></div>
          <div className="placeholder-metric"><div className="pm-label">Expenses + Mortgage</div><div className="pm-value" style={{ color: 'var(--debt)' }}>{fmtMoney(summary.totalExpenses + summary.totalMortgage)}</div></div>
          <div className="placeholder-metric"><div className="pm-label">Net Cash Flow</div><div className="pm-value" style={{ color: summary.netCashFlow >= 0 ? 'var(--cash)' : 'var(--debt)' }}>{fmtMoney(summary.netCashFlow)}</div></div>
        </div>
      )}

      {summary && summary.byProperty.length > 0 && (
        <>
          <div className="maaya-section-label">Per-Property Cash Flow</div>
          <div className="aasthi-cashflow-list">
            {summary.byProperty.map(p => (
              <div key={p.propertyId} className="aasthi-cashflow-row">
                <span className="aasthi-cf-addr">{p.address}</span>
                <span className="aasthi-cf-stat"><span className="cf-k">Net</span><b style={{ color: p.netCashFlow >= 0 ? 'var(--cash)' : 'var(--debt)' }}>{fmtMoney(p.netCashFlow)}</b></span>
                <span className="aasthi-cf-stat"><span className="cf-k">Appreciation</span><b style={{ color: p.appreciation >= 0 ? 'var(--cash)' : 'var(--debt)' }}>{fmtMoney(p.appreciation)}{p.appreciationPct != null ? ` (${p.appreciationPct.toFixed(1)}%)` : ''}</b></span>
                {p.cashOnCashPct != null && <span className="aasthi-cf-stat"><span className="cf-k">Cash-on-cash</span><b>{p.cashOnCashPct.toFixed(1)}%/yr</b></span>}
              </div>
            ))}
          </div>
        </>
      )}

      <div className="aasthi-task-filters" style={{ marginTop: '1rem' }}>
        <div className="maaya-section-label" style={{ margin: 0, flex: 1 }}>Entries</div>
        <button className="btn-primary" onClick={() => setAdding(a => !a)}>{adding ? 'Cancel' : '+ Add Entry'}</button>
      </div>

      {adding && (
        <div className="card aasthi-task-form">
          <div className="aasthi-form-grid">
            <div className="aasthi-form-group">
              <label>Property</label>
              <select value={form.propertyId} onChange={e => setForm(f => ({ ...f, propertyId: e.target.value }))}>
                <option value="">Select property...</option>
                {properties?.map(p => <option key={p.id} value={p.id}>{p.address}</option>)}
              </select>
            </div>
            <div className="aasthi-form-group">
              <label>Type</label>
              <select value={form.type} onChange={e => setForm(f => ({ ...f, type: e.target.value, category: FIN_CATEGORIES[e.target.value][0] }))}>
                {FIN_TYPES.map(t => <option key={t} value={t}>{t}</option>)}
              </select>
            </div>
            <div className="aasthi-form-group">
              <label>Category</label>
              <select value={form.category} onChange={e => setForm(f => ({ ...f, category: e.target.value }))}>
                {FIN_CATEGORIES[form.type].map(c => <option key={c} value={c}>{c.replace('_', ' ')}</option>)}
              </select>
            </div>
            <div className="aasthi-form-group">
              <label>Amount</label>
              <input type="number" step="any" placeholder="1200" value={form.amount} onChange={e => setForm(f => ({ ...f, amount: e.target.value }))} />
            </div>
            <div className="aasthi-form-group">
              <label>Date</label>
              <input type="date" value={form.date} onChange={e => setForm(f => ({ ...f, date: e.target.value }))} />
            </div>
            <div className="aasthi-form-group aasthi-form-span2">
              <label>Notes</label>
              <input placeholder="Optional" value={form.notes} onChange={e => setForm(f => ({ ...f, notes: e.target.value }))} />
            </div>
          </div>
          <div className="aasthi-form-actions">
            <button className="btn-primary" disabled={!form.propertyId || !form.amount || create.isPending} onClick={() => create.mutate()}>
              {create.isPending ? 'Adding...' : 'Add Entry'}
            </button>
          </div>
        </div>
      )}

      {properties && properties.length > 0 && (
        <div className="aasthi-task-filters" style={{ marginTop: '0.5rem' }}>
          <select value={form.propertyId} onChange={e => setForm(f => ({ ...f, propertyId: e.target.value }))} className="aasthi-inline-select">
            <option value="">{properties[0]?.address} (default)</option>
            {properties.map(p => <option key={p.id} value={p.id}>{p.address}</option>)}
          </select>
        </div>
      )}

      <div className="aasthi-task-list">
        {entries.length === 0 && <p className="text-dim">No entries for this property yet.</p>}
        {entries.map(e => (
          <div key={e.id} className="aasthi-fin-row">
            <span className={`aasthi-fin-badge ${e.type}`}>{e.type}</span>
            <div className="aasthi-fin-body">
              <span className="aasthi-fin-cat">{e.category.replace('_', ' ')}</span>
              {e.notes && <span className="aasthi-fin-notes">{e.notes}</span>}
            </div>
            <span className="aasthi-fin-date">{fmtDate(e.date)}</span>
            <span className="aasthi-fin-amount" style={{ color: e.type === 'income' ? 'var(--cash)' : 'var(--debt)' }}>
              {e.type === 'income' ? '+' : '−'}{fmtMoney(e.amount)}
            </span>
            <button className="btn-danger-ghost" onClick={() => remove.mutate(e.id)}>x</button>
          </div>
        ))}
      </div>
    </div>
  );
}

/* ── Maintenance ── */
interface MaintenanceItem {
  id: string; propertyId: string; title: string; description: string | null;
  vendorName: string | null; vendorContact: string | null; cost: number | null;
  category: string; completedDate: string | null; createdAt: string;
}
interface MaintenanceSummaryData {
  totalSpend: number; byCategory: Record<string, number>; byProperty: Record<string, number>; logCount: number;
}
const MAINT_CATEGORIES = ['repair', 'improvement', 'inspection', 'other'] as const;

function MaintenancePage() {
  const qClient = useQueryClient();
  const [adding, setAdding] = useState(false);
  const [selectedProp, setSelectedProp] = useState('');
  const [form, setForm] = useState({ title: '', description: '', vendorName: '', vendorContact: '', cost: '', category: 'repair', completedDate: '' });

  const { data: properties } = useQuery<Property[]>({ queryKey: ['properties'], queryFn: () => get(`${API}/api/properties`) });
  const summaryQ = useQuery<MaintenanceSummaryData>({ queryKey: ['maintenance-summary'], queryFn: () => get(`${API}/api/maintenance/summary`) });
  const activeProp = selectedProp || properties?.[0]?.id || '';
  const logsQ = useQuery<MaintenanceItem[]>({
    queryKey: ['maintenance', activeProp],
    queryFn: () => activeProp ? get(`${API}/api/properties/${activeProp}/maintenance`) : Promise.resolve([]),
    enabled: !!activeProp,
  });

  const invalidate = () => {
    qClient.invalidateQueries({ queryKey: ['maintenance'] });
    qClient.invalidateQueries({ queryKey: ['maintenance-summary'] });
  };

  const create = useMutation({
    mutationFn: () => send(`${API}/api/properties/${activeProp}/maintenance`, 'POST', {
      title: form.title, description: form.description || null,
      vendorName: form.vendorName || null, vendorContact: form.vendorContact || null,
      cost: form.cost ? parseFloat(form.cost) : null, category: form.category,
      completedDate: form.completedDate || null,
    }),
    onSuccess: () => { setForm({ title: '', description: '', vendorName: '', vendorContact: '', cost: '', category: 'repair', completedDate: '' }); setAdding(false); invalidate(); },
  });
  const remove = useMutation({
    mutationFn: (id: string) => send(`${API}/api/maintenance/${id}`, 'DELETE'),
    onSuccess: invalidate,
  });

  const summary = summaryQ.data;
  const logs = logsQ.data ?? [];

  if (summaryQ.isError) {
    return (
      <div className="module-empty" style={style}>
        <h2>Can't reach Aasthi API</h2>
        <p>Make sure the Aasthi backend is running on port 5200 (<code>start.ps1</code> in the aasthi folder).</p>
      </div>
    );
  }

  return (
    <div style={style}>
      {summary && (
        <div className="placeholder-grid" style={{ marginBottom: '1.25rem' }}>
          <div className="placeholder-metric"><div className="pm-label">Total Spend</div><div className="pm-value">{fmtMoney(summary.totalSpend)}</div></div>
          <div className="placeholder-metric"><div className="pm-label">Log Entries</div><div className="pm-value">{summary.logCount}</div></div>
          <div className="placeholder-metric"><div className="pm-label">Categories</div><div className="pm-value">{Object.keys(summary.byCategory).length}</div></div>
        </div>
      )}

      <div className="aasthi-task-filters">
        {properties && properties.length > 0 && (
          <select value={activeProp} onChange={e => setSelectedProp(e.target.value)} className="aasthi-inline-select">
            {properties.map(p => <option key={p.id} value={p.id}>{p.address}</option>)}
          </select>
        )}
        <button className="btn-primary" style={{ marginLeft: 'auto' }} onClick={() => setAdding(a => !a)}>{adding ? 'Cancel' : '+ Log Work'}</button>
      </div>

      {adding && (
        <div className="card aasthi-task-form">
          <div className="aasthi-form-grid">
            <div className="aasthi-form-group aasthi-form-span2">
              <label>Title</label>
              <input placeholder="e.g. Replaced water heater" value={form.title} onChange={e => setForm(f => ({ ...f, title: e.target.value }))} />
            </div>
            <div className="aasthi-form-group aasthi-form-span2">
              <label>Description</label>
              <input placeholder="Details (optional)" value={form.description} onChange={e => setForm(f => ({ ...f, description: e.target.value }))} />
            </div>
            <div className="aasthi-form-group">
              <label>Category</label>
              <select value={form.category} onChange={e => setForm(f => ({ ...f, category: e.target.value }))}>
                {MAINT_CATEGORIES.map(c => <option key={c} value={c}>{c}</option>)}
              </select>
            </div>
            <div className="aasthi-form-group">
              <label>Cost</label>
              <input type="number" step="any" placeholder="450" value={form.cost} onChange={e => setForm(f => ({ ...f, cost: e.target.value }))} />
            </div>
            <div className="aasthi-form-group">
              <label>Vendor</label>
              <input placeholder="ABC Plumbing" value={form.vendorName} onChange={e => setForm(f => ({ ...f, vendorName: e.target.value }))} />
            </div>
            <div className="aasthi-form-group">
              <label>Vendor Contact</label>
              <input placeholder="Phone / email" value={form.vendorContact} onChange={e => setForm(f => ({ ...f, vendorContact: e.target.value }))} />
            </div>
            <div className="aasthi-form-group">
              <label>Completed Date</label>
              <input type="date" value={form.completedDate} onChange={e => setForm(f => ({ ...f, completedDate: e.target.value }))} />
            </div>
          </div>
          <div className="aasthi-form-actions">
            <button className="btn-primary" disabled={!form.title || !activeProp || create.isPending} onClick={() => create.mutate()}>
              {create.isPending ? 'Saving...' : 'Save Log'}
            </button>
          </div>
        </div>
      )}

      <div className="aasthi-task-list">
        {logs.length === 0 && <p className="text-dim">No maintenance logged for this property yet.</p>}
        {logs.map(m => (
          <div key={m.id} className="aasthi-maint-row">
            <span className={`aasthi-maint-badge ${m.category}`}>{m.category}</span>
            <div className="aasthi-maint-body">
              <span className="aasthi-maint-title">{m.title}</span>
              {m.description && <span className="aasthi-maint-desc">{m.description}</span>}
              <div className="aasthi-maint-meta">
                {m.vendorName && <span>{m.vendorName}{m.vendorContact ? ` · ${m.vendorContact}` : ''}</span>}
                {m.completedDate && <span>{fmtDate(m.completedDate)}</span>}
              </div>
            </div>
            {m.cost != null && <span className="aasthi-maint-cost">{fmtMoney(m.cost)}</span>}
            <button className="btn-danger-ghost" onClick={() => remove.mutate(m.id)}>x</button>
          </div>
        ))}
      </div>
    </div>
  );
}

type Page = 'properties' | 'tasks' | 'financials' | 'maintenance';
const TABS: { id: Page; label: string }[] = [
  { id: 'properties',  label: 'Properties' },
  { id: 'tasks',       label: 'Tasks' },
  { id: 'financials',  label: 'Financials' },
  { id: 'maintenance', label: 'Maintenance' },
];

function AasthiInner() {
  const [page, setPage] = useState<Page>('properties');
  return (
    <div>
      <nav className="module-subnav" style={style}>
        {TABS.map(t => (
          <button key={t.id} className={`module-tab ${page === t.id ? 'active' : ''}`} onClick={() => setPage(t.id)}>
            {t.label}
          </button>
        ))}
      </nav>
      {page === 'properties'  && <Properties />}
      {page === 'tasks'       && <TasksPage />}
      {page === 'financials'  && <FinancialsPage />}
      {page === 'maintenance' && <MaintenancePage />}
    </div>
  );
}

export default function AasthiModule() {
  return (
    <QueryClientProvider client={qc}>
      <AasthiInner />
    </QueryClientProvider>
  );
}
