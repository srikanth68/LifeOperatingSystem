import { useState } from 'react';
import { QueryClient, QueryClientProvider, useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import '../styles/modules.css';
import '../styles/aasthi.css';

const API = 'http://localhost:5200';
const MC = 'var(--aasthi)';
const style = { '--mc': MC } as React.CSSProperties;

const qc = new QueryClient({ defaultOptions: { queries: { retry: 1, staleTime: 60_000, refetchOnWindowFocus: false } } });

const get = (url: string) => fetch(url).then(r => { if (!r.ok) throw new Error(r.status.toString()); return r.json(); });
const send = (url: string, method: string, body?: unknown) =>
  fetch(url, {
    method,
    headers: body ? { 'Content-Type': 'application/json' } : undefined,
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
      return fetch(`${API}/api/properties/${propertyId}/documents`, { method: 'POST', body: fd })
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

function EmptyPage({ title, desc }: { title: string; desc: string }) {
  return (
    <div style={style}>
      <div className="module-empty">
        <h2>{title}</h2>
        <p>{desc}</p>
        <button className="btn-primary" disabled>Coming Soon</button>
      </div>
    </div>
  );
}

type Page = 'properties' | 'financials' | 'maintenance';
const TABS: { id: Page; label: string }[] = [
  { id: 'properties',  label: 'Properties' },
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
      {page === 'financials'  && <EmptyPage title="Property Financials" desc="Income, expenses, mortgage payments, and ROI breakdown per property and across the portfolio." />}
      {page === 'maintenance' && <EmptyPage title="Maintenance Log" desc="Track repairs, improvements, vendor contacts, and maintenance costs for each property." />}
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
