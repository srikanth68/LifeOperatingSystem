import { useState } from 'react';
import '../styles/modules.css';

type Page = 'all' | 'categories' | 'expiry' | 'search';

const TABS: { id: Page; label: string }[] = [
  { id: 'all',        label: 'All Documents' },
  { id: 'categories', label: 'Categories' },
  { id: 'expiry',     label: 'Expiry Tracker' },
  { id: 'search',     label: 'Search' },
];

const MC = 'var(--sutra)';
const style = { '--mc': MC } as React.CSSProperties;

function Icon() {
  return (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.75" strokeLinecap="round" strokeLinejoin="round">
      <path d="M14 2H6a2 2 0 00-2 2v16a2 2 0 002 2h12a2 2 0 002-2V8z"/>
      <polyline points="14 2 14 8 20 8"/>
      <line x1="16" y1="13" x2="8" y2="13"/>
      <line x1="16" y1="17" x2="8" y2="17"/>
    </svg>
  );
}

const CATEGORIES = [
  { name: 'Identity',    icon: '🪪', examples: 'Passport, driving license, national ID, PAN card', count: 0, color: '#4f9ef8' },
  { name: 'Finance',     icon: '💳', examples: 'Bank statements, tax returns, investment statements', count: 0, color: '#1fc87a' },
  { name: 'Property',    icon: '🏠', examples: 'Deeds, sale agreements, property tax, utility bills', count: 0, color: '#f0a030' },
  { name: 'Insurance',   icon: '🛡️', examples: 'Health, life, vehicle, home insurance policies', count: 0, color: '#06c8a0' },
  { name: 'Medical',     icon: '🏥', examples: 'Health records, prescriptions, test reports', count: 0, color: '#f472b6' },
  { name: 'Contracts',   icon: '📝', examples: 'Employment, lease, service agreements', count: 0, color: '#a855f7' },
  { name: 'Vehicles',    icon: '🚗', examples: 'RC book, insurance, service records, loans', count: 0, color: '#d4a843' },
  { name: 'Education',   icon: '🎓', examples: 'Degrees, certificates, marksheets, transcripts', count: 0, color: '#94a3b8' },
];

const EXPIRY_EXAMPLES = [
  { name: 'Passport',           category: 'Identity',  expiry: 'Not entered', status: 'unknown', color: '#4f9ef8' },
  { name: 'Health Insurance',   category: 'Insurance', expiry: 'Not entered', status: 'unknown', color: '#06c8a0' },
  { name: 'Vehicle Insurance',  category: 'Vehicles',  expiry: 'Not entered', status: 'unknown', color: '#d4a843' },
  { name: 'Driving License',    category: 'Identity',  expiry: 'Not entered', status: 'unknown', color: '#4f9ef8' },
];

function AllDocuments() {
  return (
    <div style={style}>
      <div className="module-empty">
        <div className="module-empty-icon"><Icon /></div>
        <h2>Sutra</h2>
        <div className="tagline">Document Vault</div>
        <p>Store, organize, and never lose an important document again. Sutra tracks expiry dates, sends renewal reminders, and keeps your files searchable and secure.</p>
        <div className="module-features">
          {[
            { icon: '🔒', text: 'Encrypted storage for sensitive documents' },
            { icon: '⏰', text: 'Expiry tracking for passports, insurance, licenses' },
            { icon: '🔔', text: 'Renewal reminders sent via San 30 days before expiry' },
            { icon: '🔍', text: 'Full-text search across all uploaded documents' },
            { icon: '📂', text: 'Organized by category: identity, finance, property, medical' },
            { icon: '🔗', text: 'Link documents to Vault accounts, Aasthi properties, Nexus holdings' },
          ].map(f => (
            <div key={f.text} className="module-feature-item">
              <span className="feat-icon">{f.icon}</span>
              <span>{f.text}</span>
            </div>
          ))}
        </div>
        <button className="btn-primary" disabled>Coming Soon</button>
      </div>
    </div>
  );
}

function Categories() {
  return (
    <div style={style}>
      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(2,1fr)', gap: '0.875rem' }}>
        {CATEGORIES.map(cat => (
          <div
            key={cat.name}
            className="card"
            style={{ cursor: 'pointer', '--mc': cat.color, borderColor: 'rgba(255,255,255,0.06)' } as React.CSSProperties}
          >
            <div style={{ display: 'flex', alignItems: 'center', gap: '0.625rem', marginBottom: '0.5rem' }}>
              <span style={{ fontSize: '1.2rem' }}>{cat.icon}</span>
              <span style={{ fontWeight: 700, fontSize: '0.875rem', color: 'var(--text)' }}>{cat.name}</span>
              <span style={{
                marginLeft: 'auto',
                background: 'rgba(255,255,255,0.05)',
                color: 'var(--text3)',
                fontSize: '0.68rem',
                padding: '0.1rem 0.4rem',
                borderRadius: '99px',
              }}>0 docs</span>
            </div>
            <p style={{ fontSize: '0.72rem', color: 'var(--text3)', lineHeight: 1.5 }}>{cat.examples}</p>
          </div>
        ))}
      </div>
    </div>
  );
}

function ExpiryTracker() {
  return (
    <div style={style}>
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '1rem' }}>
        <h3 style={{ margin: 0 }}>Documents with Expiry Dates</h3>
        <button className="btn-primary" disabled style={{ opacity: 0.5, fontSize: '0.78rem' }}>+ Add Document</button>
      </div>
      {EXPIRY_EXAMPLES.map((d, i) => (
        <div key={i} className="doc-item">
          <span
            className="doc-type-badge"
            style={{ background: d.color + '20', color: d.color, border: `1px solid ${d.color}40` }}
          >
            {d.category}
          </span>
          <span className="doc-name">{d.name}</span>
          <span className="doc-meta">Expires: {d.expiry}</span>
        </div>
      ))}
      <p className="text-dim" style={{ marginTop: '1rem', textAlign: 'center' }}>Add documents to track their expiry dates and receive timely reminders</p>
    </div>
  );
}

function EmptyPage({ title, desc }: { title: string; desc: string }) {
  return (
    <div style={style}>
      <div className="module-empty">
        <div className="module-empty-icon"><Icon /></div>
        <h2>{title}</h2>
        <p>{desc}</p>
        <button className="btn-primary" disabled>Coming Soon</button>
      </div>
    </div>
  );
}

export default function SutraModule() {
  const [page, setPage] = useState<Page>('all');
  return (
    <div>
      <nav className="module-subnav" style={style}>
        {TABS.map(t => (
          <button key={t.id} className={`module-tab ${page === t.id ? 'active' : ''}`} onClick={() => setPage(t.id)}>
            {t.label}
          </button>
        ))}
      </nav>
      {page === 'all'        && <AllDocuments />}
      {page === 'categories' && <Categories />}
      {page === 'expiry'     && <ExpiryTracker />}
      {page === 'search'     && <EmptyPage title="Search Documents" desc="Find any document by name, category, tag, or content with full-text search." />}
    </div>
  );
}
