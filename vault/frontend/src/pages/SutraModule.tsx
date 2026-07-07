import { useState, useRef } from 'react';
import { QueryClientProvider, useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { makeModuleQueryClient } from '../services/moduleQuery';
import { authHeaders } from '../services/auth';
import '../styles/sutra.css';

const API = 'http://localhost:5400';

const qc = makeModuleQueryClient(30_000);

type Page = 'all' | 'categories' | 'expiry' | 'search';

interface Doc {
  id: string; fileName: string; contentType: string; sizeBytes: number;
  category: string; tags: string | null; sourceModule: string | null;
  sourceRefId: string | null; expiresAt: string | null; notes: string | null; uploadedAt: string;
}
interface Stats { totalCount: number; totalSize: string; byCategory: Record<string, number>; expiringSoon: number; }

const TABS: { id: Page; label: string }[] = [
  { id: 'all', label: 'All Documents' },
  { id: 'categories', label: 'Categories' },
  { id: 'expiry', label: 'Expiry Tracker' },
  { id: 'search', label: 'Search' },
];

const CATEGORIES = [
  { name: 'identity',  icon: '🪪', label: 'Identity',  examples: 'Passport, driving license, national ID, PAN card', color: '#4f9ef8' },
  { name: 'finance',   icon: '💳', label: 'Finance',   examples: 'Bank statements, tax returns, investment records', color: '#1fc87a' },
  { name: 'property',  icon: '🏠', label: 'Property',  examples: 'Deeds, sale agreements, property tax, utility bills', color: '#f0a030' },
  { name: 'insurance', icon: '🛡️', label: 'Insurance', examples: 'Health, life, vehicle, home insurance policies', color: '#06c8a0' },
  { name: 'medical',   icon: '🏥', label: 'Medical',   examples: 'Health records, prescriptions, test reports', color: '#f472b6' },
  { name: 'contracts', icon: '📝', label: 'Contracts', examples: 'Employment, lease, service agreements', color: '#a855f7' },
  { name: 'vehicles',  icon: '🚗', label: 'Vehicles',  examples: 'RC book, insurance, service records, loans', color: '#d4a843' },
  { name: 'education', icon: '🎓', label: 'Education', examples: 'Degrees, certificates, marksheets, transcripts', color: '#94a3b8' },
];

const CAT_COLOR: Record<string, string> = Object.fromEntries(CATEGORIES.map(c => [c.name, c.color]));
const CAT_ICON: Record<string, string> = Object.fromEntries(CATEGORIES.map(c => [c.name, c.icon]));

function fmtSize(b: number) { return b < 1024 ? `${b} B` : b < 1048576 ? `${(b/1024).toFixed(1)} KB` : `${(b/1048576).toFixed(1)} MB`; }
function fmtDate(s: string) { return new Date(s).toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' }); }
function fileIcon(ct: string) { if (ct?.includes('pdf')) return '📄'; if (ct?.includes('image')) return '🖼️'; if (ct?.includes('spreadsheet') || ct?.includes('excel')) return '📊'; return '📎'; }

const get = (url: string) => fetch(url, { headers: authHeaders() }).then(r => { if (!r.ok) throw new Error(r.status.toString()); return r.json(); });
const send = (url: string, method: string) => fetch(url, { method, headers: authHeaders() }).then(r => { if (!r.ok) throw new Error(r.status.toString()); });

// Turn a raw fetch/HTTP error into something a human can act on.
function friendlyError(e: unknown): string {
  const msg = e instanceof Error ? e.message : String(e);
  if (msg === 'Failed to fetch' || /network|load failed/i.test(msg))
    return "Can't reach the document service. Make sure Sutra is running on port 5400 (start the full stack with maaya-start.ps1).";
  if (msg === '401') return 'Your session expired. Please sign out and sign in again.';
  if (msg === '413') return 'That file is too large (200 MB max).';
  if (msg === '404') return 'Document not found — it may have already been removed.';
  if (/^5\d\d$/.test(msg)) return `The document service hit an error (${msg}). Try again in a moment.`;
  return `Something went wrong (${msg}).`;
}

function Banner({ kind, text, onClose }: { kind: 'error' | 'ok'; text: string; onClose?: () => void }) {
  return (
    <div className={`sutra-banner ${kind}`} role={kind === 'error' ? 'alert' : 'status'}>
      <span className="sutra-banner-icon">{kind === 'error' ? '⚠' : '✓'}</span>
      <span className="sutra-banner-text">{text}</span>
      {onClose && <button className="sutra-banner-close" onClick={onClose} aria-label="Dismiss">×</button>}
    </div>
  );
}

function AllDocuments() {
  const qc = useQueryClient();
  const docsQ = useQuery<Doc[]>({ queryKey: ['sutra-docs'], queryFn: () => get(`${API}/api/documents`) });
  const { data: stats } = useQuery<Stats>({ queryKey: ['sutra-stats'], queryFn: () => get(`${API}/api/documents/stats`) });
  const docs = docsQ.data;
  const fileRef = useRef<HTMLInputElement>(null);
  const [cat, setCat] = useState('other');
  const [tags, setTags] = useState('');
  const [expiry, setExpiry] = useState('');
  const [notice, setNotice] = useState<{ kind: 'error' | 'ok'; text: string } | null>(null);
  const invalidate = () => { qc.invalidateQueries({ queryKey: ['sutra-docs'] }); qc.invalidateQueries({ queryKey: ['sutra-stats'] }); qc.invalidateQueries({ queryKey: ['sutra-expiring'] }); };

  const upload = useMutation({
    mutationFn: () => {
      const fd = new FormData();
      const files = fileRef.current?.files!;
      Array.from(files).forEach(f => fd.append('files', f));
      fd.append('category', cat);
      if (tags) fd.append('tags', tags);
      if (expiry) fd.append('expiresAt', expiry);
      return fetch(`${API}/api/documents`, { method: 'POST', headers: authHeaders(), body: fd }).then(r => { if (!r.ok) throw new Error(r.status.toString()); return r.json(); });
    },
    onSuccess: (results) => {
      if (fileRef.current) fileRef.current.value = '';
      setTags(''); setExpiry('');
      const n = Array.isArray(results) ? results.length : 1;
      setNotice({ kind: 'ok', text: `Uploaded ${n} document${n === 1 ? '' : 's'}.` });
      invalidate();
    },
    onError: (e) => setNotice({ kind: 'error', text: friendlyError(e) }),
  });

  const del = useMutation({
    mutationFn: (id: string) => send(`${API}/api/documents/${id}`, 'DELETE'),
    onSuccess: invalidate,
    onError: (e) => setNotice({ kind: 'error', text: friendlyError(e) }),
  });

  return (
    <div>
      {docsQ.isError && (
        <Banner kind="error" text={friendlyError(docsQ.error)} onClose={() => docsQ.refetch()} />
      )}
      {notice && <Banner kind={notice.kind} text={notice.text} onClose={() => setNotice(null)} />}

      {stats && (
        <div className="sutra-stats">
          <div className="sutra-stat"><div className="sutra-stat-label">Total Documents</div><div className="sutra-stat-value">{stats.totalCount}</div></div>
          <div className="sutra-stat"><div className="sutra-stat-label">Total Size</div><div className="sutra-stat-value">{stats.totalSize}</div></div>
          <div className="sutra-stat"><div className="sutra-stat-label">Categories</div><div className="sutra-stat-value">{Object.keys(stats.byCategory).length}</div></div>
          <div className="sutra-stat"><div className="sutra-stat-label">Expiring Soon</div><div className="sutra-stat-value">{stats.expiringSoon}</div></div>
        </div>
      )}

      <div className="sutra-upload">
        <input type="file" ref={fileRef} multiple />
        <select value={cat} onChange={e => setCat(e.target.value)}>
          {CATEGORIES.map(c => <option key={c.name} value={c.name}>{c.label}</option>)}
          <option value="other">Other</option>
        </select>
        <input type="text" placeholder="Tags (comma-sep)" value={tags} onChange={e => setTags(e.target.value)} />
        <input type="date" value={expiry} onChange={e => setExpiry(e.target.value)} title="Expiry date (optional)" />
        <button
          className="sutra-upload-btn"
          onClick={() => {
            if (!fileRef.current?.files?.length) { setNotice({ kind: 'error', text: 'Choose at least one file to upload.' }); return; }
            upload.mutate();
          }}
          disabled={upload.isPending}
        >
          {upload.isPending ? 'Uploading...' : 'Upload'}
        </button>
      </div>

      {docs?.length === 0 && (
        <div className="sutra-empty">
          <h3>No documents yet</h3>
          <p>Upload your first document above to get started.</p>
        </div>
      )}

      {docs?.map(d => (
        <div key={d.id} className="sutra-doc-row">
          <span className="sutra-doc-icon">{fileIcon(d.contentType)}</span>
          <div className="sutra-doc-info">
            <a className="sutra-doc-name" href={`${API}/api/documents/${d.id}/download`} target="_blank" rel="noreferrer">{d.fileName}</a>
            <div className="sutra-doc-meta">
              <span>{fmtSize(d.sizeBytes)}</span>
              <span>{fmtDate(d.uploadedAt)}</span>
              {d.sourceModule && <span>via {d.sourceModule}</span>}
              {d.tags && <span>{d.tags}</span>}
              {d.expiresAt && <span>expires {fmtDate(d.expiresAt)}</span>}
            </div>
          </div>
          <span className="sutra-doc-cat" style={{ background: (CAT_COLOR[d.category] || '#7a96c0') + '20', color: CAT_COLOR[d.category] || '#7a96c0', border: `1px solid ${(CAT_COLOR[d.category] || '#7a96c0')}40` }}>
            {d.category}
          </span>
          <button className="sutra-doc-delete" onClick={() => del.mutate(d.id)} title="Delete">×</button>
        </div>
      ))}
    </div>
  );
}

function CategoriesPage() {
  const { data: stats } = useQuery<Stats>({ queryKey: ['sutra-stats'], queryFn: () => get(`${API}/api/documents/stats`) });
  return (
    <div className="sutra-cat-grid">
      {CATEGORIES.map(c => {
        const count = stats?.byCategory[c.name] ?? 0;
        return (
          <div key={c.name} className="sutra-cat-card">
            <div className="sutra-cat-head">
              <span className="sutra-cat-icon">{c.icon}</span>
              <span className="sutra-cat-name">{c.label}</span>
              <span className="sutra-cat-count">{count} docs</span>
            </div>
            <p className="sutra-cat-examples">{c.examples}</p>
          </div>
        );
      })}
    </div>
  );
}

function ExpiryTracker() {
  const expQ = useQuery<Doc[]>({ queryKey: ['sutra-expiring'], queryFn: () => get(`${API}/api/documents/expiring?days=90`) });
  const docs = expQ.data;

  const badge = (d: Doc) => {
    if (!d.expiresAt) return null;
    const days = Math.ceil((new Date(d.expiresAt).getTime() - Date.now()) / 86400000);
    if (days <= 7) return <span className="sutra-expiry-badge urgent">Expires in {days}d</span>;
    if (days <= 30) return <span className="sutra-expiry-badge soon">Expires in {days}d</span>;
    return <span className="sutra-expiry-badge ok">Expires in {days}d</span>;
  };

  return (
    <div>
      <h3 style={{ margin: '0 0 1rem' }}>Documents Expiring Within 90 Days</h3>
      {expQ.isError && <Banner kind="error" text={friendlyError(expQ.error)} onClose={() => expQ.refetch()} />}
      {docs?.length === 0 && <div className="sutra-empty"><h3>No expiring documents</h3><p>Documents with expiry dates will appear here as they approach their renewal date.</p></div>}
      {docs?.map(d => (
        <div key={d.id} className="sutra-expiry-row">
          <span className="sutra-doc-icon">{CAT_ICON[d.category] || '📎'}</span>
          <div className="sutra-doc-info">
            <a className="sutra-doc-name" href={`${API}/api/documents/${d.id}/download`} target="_blank" rel="noreferrer">{d.fileName}</a>
            <div className="sutra-doc-meta"><span>{d.category}</span>{d.expiresAt && <span>{fmtDate(d.expiresAt)}</span>}</div>
          </div>
          {badge(d)}
        </div>
      ))}
    </div>
  );
}

function SearchPage() {
  const [q, setQ] = useState('');
  const [query, setQuery] = useState('');
  const searchQ = useQuery<Doc[]>({
    queryKey: ['sutra-search', query],
    queryFn: () => get(`${API}/api/documents?q=${encodeURIComponent(query)}`),
    enabled: query.length > 0,
  });
  const docs = searchQ.data;

  return (
    <div>
      <div className="sutra-search">
        <input placeholder="Search by filename, tags, or notes..." value={q} onChange={e => setQ(e.target.value)} onKeyDown={e => e.key === 'Enter' && setQuery(q)} />
        <button className="sutra-search-btn" onClick={() => setQuery(q)}>Search</button>
      </div>
      {searchQ.isError && <Banner kind="error" text={friendlyError(searchQ.error)} onClose={() => searchQ.refetch()} />}
      {query && !searchQ.isError && docs?.length === 0 && <div className="sutra-empty"><h3>No results</h3><p>No documents matched "{query}"</p></div>}
      {docs?.map(d => (
        <div key={d.id} className="sutra-doc-row">
          <span className="sutra-doc-icon">{fileIcon(d.contentType)}</span>
          <div className="sutra-doc-info">
            <a className="sutra-doc-name" href={`${API}/api/documents/${d.id}/download`} target="_blank" rel="noreferrer">{d.fileName}</a>
            <div className="sutra-doc-meta">
              <span>{fmtSize(d.sizeBytes)}</span>
              <span>{fmtDate(d.uploadedAt)}</span>
              {d.tags && <span>{d.tags}</span>}
            </div>
          </div>
          <span className="sutra-doc-cat" style={{ background: (CAT_COLOR[d.category] || '#7a96c0') + '20', color: CAT_COLOR[d.category] || '#7a96c0', border: `1px solid ${(CAT_COLOR[d.category] || '#7a96c0')}40` }}>
            {d.category}
          </span>
        </div>
      ))}
    </div>
  );
}

function SutraInner() {
  const [page, setPage] = useState<Page>('all');
  const style = { '--mc': 'var(--sutra)' } as React.CSSProperties;
  return (
    <div style={style}>
      <nav className="module-subnav">
        {TABS.map(t => (
          <button key={t.id} className={`module-tab ${page === t.id ? 'active' : ''}`} onClick={() => setPage(t.id)}>
            {t.label}
          </button>
        ))}
      </nav>
      {page === 'all' && <AllDocuments />}
      {page === 'categories' && <CategoriesPage />}
      {page === 'expiry' && <ExpiryTracker />}
      {page === 'search' && <SearchPage />}
    </div>
  );
}

export default function SutraModule() {
  return <QueryClientProvider client={qc}><SutraInner /></QueryClientProvider>;
}
