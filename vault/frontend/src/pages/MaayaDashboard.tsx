import { useEffect, useState } from 'react';
import { summaryApi } from '@/services/api';
import { authHeaders } from '../services/auth';
import { moduleApi } from '../services/apiHost';
import { useSystemStatus } from '../services/systemStatus';
import '../styles/maaya.css';
import '../styles/modules.css';

import type { ModuleId } from '../App';
import type { DashboardSummary, Transaction } from '@/types';

interface Props {
  onNavigate: (m: ModuleId) => void;
}

function greeting() {
  const h = new Date().getHours();
  if (h < 12) return 'Good morning';
  if (h < 17) return 'Good afternoon';
  return 'Good evening';
}

function fmtMoney(n: number) {
  const sign = n < 0 ? '-' : '';
  return sign + '$' + Math.abs(n).toLocaleString('en-US', { minimumFractionDigits: 0, maximumFractionDigits: 0 });
}

function relTime(iso: string) {
  const diffMs = Date.now() - new Date(iso).getTime();
  const mins = Math.round(diffMs / 60000);
  if (mins < 1) return 'just now';
  if (mins < 60) return `${mins}m ago`;
  const hrs = Math.round(mins / 60);
  if (hrs < 24) return `${hrs}h ago`;
  return `${Math.round(hrs / 24)}d ago`;
}

function getModuleIcon(id: string) {
  const p: Record<string, React.ReactNode> = {
    vault:     <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.75" strokeLinecap="round" strokeLinejoin="round"><rect x="2" y="3" width="20" height="18" rx="2"/><circle cx="12" cy="12" r="3"/><path d="M12 9V7M12 17v-2M9.5 12H7M17 12h-2.5"/></svg>,
    vitara:    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.75" strokeLinecap="round" strokeLinejoin="round"><polyline points="3 12 6 12 8 6 10 18 12 12 14 15 16 12 21 12"/></svg>,
    nexus:     <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.75" strokeLinecap="round" strokeLinejoin="round"><polyline points="23 6 13.5 15.5 8.5 10.5 1 18"/><polyline points="17 6 23 6 23 12"/></svg>,
    aasthi:    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.75" strokeLinecap="round" strokeLinejoin="round"><path d="M3 9l9-7 9 7v11a2 2 0 01-2 2H5a2 2 0 01-2-2z"/><rect x="9" y="14" width="6" height="7"/></svg>,
    san:       <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.75" strokeLinecap="round" strokeLinejoin="round"><path d="M12 2l3.09 6.26L22 9.27l-5 4.87 1.18 6.88L12 17.77l-6.18 3.25L7 14.14 2 9.27l6.91-1.01L12 2z"/></svg>,
    northstar: <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.75" strokeLinecap="round" strokeLinejoin="round"><circle cx="12" cy="12" r="10"/><polygon points="16.24 7.76 14.12 14.12 7.76 16.24 9.88 9.88 16.24 7.76"/></svg>,
    karma:     <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.75" strokeLinecap="round" strokeLinejoin="round"><path d="M22 11.08V12a10 10 0 11-5.93-9.14"/><polyline points="22 4 12 14.01 9 11.01"/></svg>,
    sutra:     <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.75" strokeLinecap="round" strokeLinejoin="round"><path d="M14 2H6a2 2 0 00-2 2v16a2 2 0 002 2h12a2 2 0 002-2V8z"/><polyline points="14 2 14 8 20 8"/><line x1="16" y1="13" x2="8" y2="13"/><line x1="16" y1="17" x2="8" y2="17"/></svg>,
  };
  return p[id] ?? null;
}

const MODULES: { id: ModuleId; name: string; sub: string; desc: string; color: string; live: boolean }[] = [
  { id: 'vault',     name: 'Vault',     sub: 'Personal Finance',   desc: 'Accounts, budgets, net worth',                color: 'var(--vault)',     live: true  },
  { id: 'vitara',    name: 'Vitara',    sub: 'Health & Wellness',  desc: 'Vitals, sleep, activity, nutrition',          color: 'var(--vitara)',    live: false },
  { id: 'nexus',     name: 'Nexus',     sub: 'Trading & Markets',  desc: 'Sentinel watchlist, verdicts, portfolio',     color: 'var(--nexus)',     live: false },
  { id: 'aasthi',    name: 'Aasthi',    sub: 'Real Estate',        desc: 'Properties, contacts, documents',             color: 'var(--aasthi)',    live: false },
  { id: 'san',       name: 'San',       sub: 'Assistant & Alerts', desc: 'Chat, reminders, cross-module activity',      color: 'var(--san)',       live: false },
  { id: 'northstar', name: 'NorthStar', sub: 'Knowledge Graph',    desc: 'Context, insights, cross-module search',      color: 'var(--northstar)', live: false },
  { id: 'karma',     name: 'Karma',     sub: 'Goals & Habits',     desc: 'Routines, streaks, focus',                    color: 'var(--karma)',     live: false },
  { id: 'sutra',     name: 'Sutra',     sub: 'Document Vault',     desc: 'Identity, contracts, expiry tracking',        color: 'var(--sutra)',     live: false },
];

function ClockDial() {
  const [now, setNow] = useState(new Date());
  useEffect(() => {
    const t = setInterval(() => setNow(new Date()), 30_000);
    return () => clearInterval(t);
  }, []);
  const time = now.toLocaleTimeString('en-US', { hour: '2-digit', minute: '2-digit', hour12: false });
  const day = now.toLocaleDateString('en-US', { weekday: 'long' });
  const date = now.toLocaleDateString('en-US', { day: '2-digit', month: 'short', year: 'numeric' }).toUpperCase();

  return (
    <div className="clock-dial">
      <div className="clock-ring-dashed" />
      <div className="clock-ring-dotted" />
      <div className="clock-face">
        <span className="day">{day}</span>
        <span className="time">{time}</span>
        <span className="date">{date}</span>
      </div>
    </div>
  );
}

function onCardMove(e: React.MouseEvent<HTMLDivElement>) {
  const rect = e.currentTarget.getBoundingClientRect();
  e.currentTarget.style.setProperty('--mouse-x', `${e.clientX - rect.left}px`);
  e.currentTarget.style.setProperty('--mouse-y', `${e.clientY - rect.top}px`);
}

export default function MaayaDashboard({ onNavigate }: Props) {
  const [summary, setSummary] = useState<DashboardSummary | null>(null);
  const [marketOpen, setMarketOpen] = useState<boolean | null>(null);

  useEffect(() => {
    summaryApi.getDashboard().then(r => setSummary(r.data)).catch(() => {});
    fetch(`${moduleApi(5700)}/api/nexus/sentinel/status`, { headers: authHeaders() })
      .then(r => { if (!r.ok) throw new Error(); return r.json(); })
      .then(d => setMarketOpen(d.marketOpen))
      .catch(() => setMarketOpen(null));
  }, []);

  const sys = useSystemStatus();
  const liveCount = sys.loading ? MODULES.length : sys.online;
  const offlineModules = MODULES.filter(m => !sys.loading && sys.reachable[m.id] === false);
  const transactions: Transaction[] = summary?.recentTransactions?.slice(0, 4) ?? [];

  return (
    <div className="maaya-home">
      <div className="ambient-glow" />

      {/* ── Quick Actions ── */}
      <div className="quick-actions">
        <button className="qa-btn" style={{ '--qa': 'var(--vault)' } as React.CSSProperties} onClick={() => onNavigate('vault')}>
          <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.75" strokeLinecap="round" strokeLinejoin="round"><line x1="12" y1="5" x2="12" y2="19"/><line x1="5" y1="12" x2="19" y2="12"/></svg>
          Add Transaction
        </button>
        <button className="qa-btn" style={{ '--qa': 'var(--vitara)' } as React.CSSProperties} onClick={() => onNavigate('vitara')}>
          <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.75" strokeLinecap="round" strokeLinejoin="round"><path d="M20.84 4.61a5.5 5.5 0 00-7.78 0L12 5.67l-1.06-1.06a5.5 5.5 0 00-7.78 7.78l7.78 7.78 7.78-7.78 1.06-1.06a5.5 5.5 0 000-7.78z"/></svg>
          Log Health
        </button>
        <button className="qa-btn" style={{ '--qa': 'var(--nexus)' } as React.CSSProperties} onClick={() => onNavigate('nexus')}>
          <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.75" strokeLinecap="round" strokeLinejoin="round"><polyline points="23 6 13.5 15.5 8.5 10.5 1 18"/><polyline points="17 6 23 6 23 12"/></svg>
          New Trade
        </button>
        <button className="qa-btn" style={{ '--qa': 'var(--aasthi)' } as React.CSSProperties} onClick={() => onNavigate('aasthi')}>
          <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.75" strokeLinecap="round" strokeLinejoin="round"><path d="M3 9l9-7 9 7v11a2 2 0 01-2 2H5a2 2 0 01-2-2z"/><rect x="9" y="14" width="6" height="7"/></svg>
          Add Property
        </button>
      </div>

      {/* ── Header: greeting + clock ── */}
      <header className="maaya-header animate-in">
        <div>
          <div className="maaya-greeting-time">{greeting()},</div>
          <h1 className="maaya-wordmark">Maaya.</h1>
          <p className="maaya-tagline">
            <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><path d="M22 11.08V12a10 10 0 11-5.93-9.14"/><polyline points="22 4 12 14.01 9 11.01"/></svg>
            {offlineModules.length === 0 ? <>Your life, unified. <span>You&apos;re in command.</span></> : <>{offlineModules.length} module{offlineModules.length > 1 ? 's' : ''} offline. <span>{offlineModules.map(m => m.name).join(', ')}</span></>}
          </p>
        </div>
        <div className="maaya-header-right">
          <div className="maaya-topbar">
            <span>MAAYA OS</span>
            <span className="private-mode"><span className="dot" />PRIVATE MODE</span>
          </div>
          <ClockDial />
        </div>
      </header>

      {/* ── Key stats ── */}
      <section className="glass-panel maaya-stats-bar animate-in delay-100">
        <div className="stat-tile">
          <span className="stat-label">Net Worth</span>
          <div className="stat-value accent">{summary ? fmtMoney(summary.netWorth) : '—'}</div>
          <div className="stat-sub">{summary ? 'Across all linked accounts' : 'Not connected'}</div>
        </div>
        <div className="stat-divider" />
        <div className="stat-tile">
          <span className="stat-label">Cash</span>
          <div className="stat-value">{summary ? fmtMoney(summary.totalCash) : '—'}</div>
          <div className="stat-sub">Liquid across accounts</div>
        </div>
        <div className="stat-divider" />
        <div className="stat-tile">
          <span className="stat-label">Modules Live</span>
          <div className="stat-value">{liveCount}/{MODULES.length}</div>
          <div className="stat-sub">{liveCount === MODULES.length ? 'All modules online' : `${MODULES.length - liveCount} offline`}</div>
        </div>
        <div className="stat-divider" />
        <div className="stat-tile">
          <span className="stat-label">Market</span>
          <div className="stat-value">
            {marketOpen === null ? '—' : marketOpen ? 'OPEN' : 'CLOSED'}
          </div>
          <div className="stat-sub stat-dot">
            <span className={`led ${marketOpen ? 'open' : 'closed'}`} />
            {marketOpen === null ? 'Not connected' : marketOpen ? 'US Markets' : 'US Markets'}
          </div>
        </div>
      </section>

      {/* ── Module grid ── */}
      <div className="maaya-section-label">Modules</div>
      <div className="maaya-grid animate-in delay-200">
        {MODULES.map(mod => (
          <div
            key={mod.id}
            className="glass-card mod-card"
            style={{ '--mc': mod.color, '--card-glow-color': `color-mix(in srgb, ${mod.color} 20%, transparent)` } as React.CSSProperties}
            onMouseMove={onCardMove}
            onClick={() => onNavigate(mod.id)}
          >
            <div className="mod-card-header">
              <div className="mod-card-icon">{getModuleIcon(mod.id)}</div>
            </div>
            <div className="mod-card-title-wrap">
              <div className="mod-card-name">{mod.name}</div>
              <div className="mod-card-sub">{mod.sub}</div>
              {mod.id === 'vault' && summary ? (
                <div className="mod-stat-value">{fmtMoney(summary.netWorth)}</div>
              ) : (
                <div className="mod-card-desc">{mod.desc}</div>
              )}
              <div className="mod-card-footer">
                <span className="mod-card-cta">Enter Module</span>
                <span className="mod-card-arrow">→</span>
              </div>
            </div>
          </div>
        ))}
      </div>

      {/* ── Recent activity ── */}
      <div className="maaya-section-label">Recent Activity</div>
      <div className="glass-panel maaya-activity" style={{ padding: '1.25rem 1.5rem' }}>
        <div className="activity-list">
          {transactions.map(t => (
            <div key={t.id} className="activity-row" style={{ '--am': 'var(--vault)' } as React.CSSProperties}>
              <span className="activity-avatar">{getModuleIcon('vault')}</span>
              <div className="activity-body">
                <span className="activity-name">{t.merchantName || t.description}</span>
                <span className="activity-sub">{t.institutionName}</span>
              </div>
              <div className="activity-amount-wrap">
                <span className={`activity-amount ${t.amount > 0 ? 'neg' : 'pos'}`}>
                  {t.amount > 0 ? '-' : '+'}{fmtMoney(Math.abs(t.amount))}
                </span>
                <span className="activity-time">{relTime(t.transactionDate)}</span>
              </div>
            </div>
          ))}
          {offlineModules.map(m => (
            <div key={m.id} className="activity-row" style={{ '--am': m.color } as React.CSSProperties}>
              <span className="activity-avatar">{getModuleIcon(m.id)}</span>
              <div className="activity-body">
                <span className="activity-name" style={{ color: 'var(--text3)' }}>{m.name} — offline</span>
                <span className="activity-sub">Backend not reachable — start it to see live data</span>
              </div>
            </div>
          ))}
        </div>
      </div>
    </div>
  );
}
