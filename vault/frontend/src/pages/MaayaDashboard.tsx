import { useEffect, useState } from 'react';
import { summaryApi } from '@/services/api';
import '../styles/maaya.css';
import '../styles/modules.css';

import type { ModuleId } from '../App';

interface Props {
  onNavigate: (m: ModuleId) => void;
}

function greeting() {
  const h = new Date().getHours();
  if (h < 12) return 'Good morning';
  if (h < 17) return 'Good afternoon';
  return 'Good evening';
}

function fmtDate() {
  return new Date().toLocaleDateString('en-US', { weekday: 'long', month: 'long', day: 'numeric', year: 'numeric' });
}

function VaultIcon() {
  return (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.75" strokeLinecap="round" strokeLinejoin="round">
      <rect x="2" y="3" width="20" height="18" rx="2"/>
      <circle cx="12" cy="12" r="3"/>
      <path d="M12 9V7M12 17v-2M9.5 12H7M17 12h-2.5"/>
    </svg>
  );
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

const MODULES = [
  { id: 'vault'     as ModuleId, name: 'Vault',     sub: 'Personal Finance',    color: 'var(--vault)',     active: true  },
  { id: 'vitara'    as ModuleId, name: 'Vitara',    sub: 'Health & Wellness',   color: 'var(--vitara)',    active: false },
  { id: 'nexus'     as ModuleId, name: 'Nexus',     sub: 'Trading & Markets',   color: 'var(--nexus)',     active: false },
  { id: 'aasthi'    as ModuleId, name: 'Aasthi',    sub: 'Real Estate',         color: 'var(--aasthi)',    active: false },
  { id: 'san'       as ModuleId, name: 'San',        sub: 'Assistant & Alerts',  color: 'var(--san)',       active: false },
  { id: 'northstar' as ModuleId, name: 'NorthStar', sub: 'Knowledge Graph',     color: 'var(--northstar)', active: false },
  { id: 'karma'     as ModuleId, name: 'Karma',     sub: 'Goals & Habits',      color: 'var(--karma)',     active: false },
  { id: 'sutra'     as ModuleId, name: 'Sutra',     sub: 'Document Vault',      color: 'var(--sutra)',     active: false },
];

export default function MaayaDashboard({ onNavigate }: Props) {
  const [vaultStat, setVaultStat] = useState<{ value: string; label: string } | null>(null);

  useEffect(() => {
    summaryApi.getDashboard().then(r => {
      const d = r.data;
      const nw = d.netWorth;
      const sign = nw < 0 ? '-' : '';
      setVaultStat({
        value: sign + '$' + Math.abs(nw).toLocaleString('en-US', { minimumFractionDigits: 0, maximumFractionDigits: 0 }),
        label: 'Net Worth',
      });
    }).catch(() => {});
  }, []);

  return (
    <div className="maaya-home">
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

      {/* ── Greeting ── */}
      <div className="maaya-greeting">
        <div className="maaya-greeting-time">{fmtDate()}</div>
        <h1>{greeting()}</h1>
        <p className="maaya-tagline">Your life, organized. <span>Powered by Maaya.</span></p>
      </div>

      {/* ── Status row ── */}
      <div className="maaya-status-row">
        <span className="status-chip active"><span className="chip-dot"/>&nbsp;Vault — Live</span>
        {['Vitara','Nexus','Aasthi','San','NorthStar','Karma','Sutra'].map(m => (
          <span key={m} className="status-chip setup"><span className="chip-dot"/>&nbsp;{m} — Set up</span>
        ))}
      </div>

      {/* ── Module grid ── */}
      <div className="maaya-section-label">Modules</div>
      <div className="maaya-grid">
        {MODULES.map(mod => (
          <div
            key={mod.id}
            className="mod-card"
            style={{ '--mc': mod.color } as React.CSSProperties}
            onClick={() => onNavigate(mod.id)}
          >
            <div className="mod-card-header">
              <div className="mod-card-icon">{getModuleIcon(mod.id)}</div>
              <div className="mod-card-title-wrap">
                <div className="mod-card-name">{mod.name}</div>
                <div className="mod-card-sub">{mod.sub}</div>
              </div>
            </div>
            {mod.id === 'vault' && vaultStat ? (
              <>
                <div className="mod-stat-value">{vaultStat.value}</div>
                <div className="mod-stat-label">{vaultStat.label}</div>
              </>
            ) : (
              <div className="mod-card-setup">
                <span>Open module</span>
                <span className="mod-card-setup-arrow">→</span>
              </div>
            )}
          </div>
        ))}
      </div>

      {/* ── Recent activity ── */}
      <div className="maaya-section-label">Recent Activity</div>
      <div className="card maaya-activity">
        <div className="activity-list">
          <div className="activity-row">
            <span className="activity-dot" style={{ background: 'var(--vault)' }}/>
            <span className="activity-module" style={{ color: 'var(--vault)' }}>Vault</span>
            <span className="activity-text">Linked Tartan Bank + First Gingham Credit Union via Plaid</span>
            <span className="activity-time">Today</span>
          </div>
          <div className="activity-row">
            <span className="activity-dot" style={{ background: 'var(--vault)' }}/>
            <span className="activity-module" style={{ color: 'var(--vault)' }}>Vault</span>
            <span className="activity-text">Synced 16 transactions across 2 institutions</span>
            <span className="activity-time">Today</span>
          </div>
          {['Vitara','Nexus','Aasthi','San','NorthStar','Karma','Sutra'].map((m, i) => {
            const colors: Record<string,string> = { Vitara:'var(--vitara)', Nexus:'var(--nexus)', Aasthi:'var(--aasthi)', San:'var(--san)', NorthStar:'var(--northstar)', Karma:'var(--karma)', Sutra:'var(--sutra)' };
            return (
              <div key={m} className="activity-row">
                <span className="activity-dot" style={{ background: colors[m] }}/>
                <span className="activity-module" style={{ color: colors[m] }}>{m}</span>
                <span className="activity-text" style={{ color: 'var(--text3)' }}>Not configured yet — click to set up</span>
                <span className="activity-time">—</span>
              </div>
            );
          })}
        </div>
      </div>
    </div>
  );
}
