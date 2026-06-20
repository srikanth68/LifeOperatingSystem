import { useState } from 'react';
import '../styles/modules.css';

type Page = 'portfolio' | 'watchlist' | 'analysis' | 'orders';

const TABS: { id: Page; label: string }[] = [
  { id: 'portfolio',  label: 'Portfolio' },
  { id: 'watchlist',  label: 'Watchlist' },
  { id: 'analysis',   label: 'Analysis' },
  { id: 'orders',     label: 'Orders' },
];

const MC = 'var(--nexus)';
const style = { '--mc': MC } as React.CSSProperties;

function Icon() {
  return (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.75" strokeLinecap="round" strokeLinejoin="round">
      <polyline points="23 6 13.5 15.5 8.5 10.5 1 18"/>
      <polyline points="17 6 23 6 23 12"/>
    </svg>
  );
}

const FEATURES = [
  { icon: '📊', text: 'Live portfolio tracking with real-time P&L and allocation' },
  { icon: '👀', text: 'Watchlist with price alerts and technical signals' },
  { icon: '📝', text: 'Per-ticker trade thesis and notes' },
  { icon: '📈', text: 'Historical performance analysis and attribution' },
  { icon: '🔔', text: 'Custom price and volume alerts' },
  { icon: '🧾', text: 'Order history across all brokers' },
];

function Portfolio() {
  return (
    <div style={style}>
      <div className="placeholder-grid">
        <div className="placeholder-metric">
          <div className="pm-label">Total Value</div>
          <div className="pm-value">—</div>
          <div className="pm-sub">Not connected</div>
        </div>
        <div className="placeholder-metric">
          <div className="pm-label">Day P&L</div>
          <div className="pm-value">—</div>
          <div className="pm-sub">Today vs. yesterday</div>
        </div>
        <div className="placeholder-metric">
          <div className="pm-label">Total P&L</div>
          <div className="pm-value">—</div>
          <div className="pm-sub">All-time return</div>
        </div>
      </div>
      <div className="module-empty" style={{ paddingTop: '1rem' }}>
        <div className="module-empty-icon"><Icon /></div>
        <h2>Nexus</h2>
        <div className="tagline">Trading & Markets</div>
        <p>Connect your brokerage accounts to see your portfolio performance, open positions, and trade history in one unified view.</p>
        <div className="module-features">
          {FEATURES.map(f => (
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

export default function NexusModule() {
  const [page, setPage] = useState<Page>('portfolio');
  return (
    <div>
      <nav className="module-subnav" style={style}>
        {TABS.map(t => (
          <button key={t.id} className={`module-tab ${page === t.id ? 'active' : ''}`} onClick={() => setPage(t.id)}>
            {t.label}
          </button>
        ))}
      </nav>
      {page === 'portfolio' && <Portfolio />}
      {page === 'watchlist' && <EmptyPage title="Watchlist" desc="Track your go-to tickers with price levels, technical indicators, and custom notes." />}
      {page === 'analysis'  && <EmptyPage title="Analysis" desc="Write and store your trade thesis, sector views, and market analysis notes per ticker." />}
      {page === 'orders'    && <EmptyPage title="Order History" desc="Review your historical trades, performance by ticker, and win/loss statistics." />}
    </div>
  );
}
