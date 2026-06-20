import { useState, useEffect } from 'react';
import { summaryApi, syncApi } from '@/services/api';
import PlaidLinkButton from '@/components/PlaidLink';
import '../styles/modules.css';

type Page = 'integrations' | 'notifications' | 'data' | 'appearance' | 'about';

const TABS: { id: Page; label: string }[] = [
  { id: 'integrations',  label: 'Integrations' },
  { id: 'notifications', label: 'Notifications' },
  { id: 'data',          label: 'Data & Sync' },
  { id: 'appearance',    label: 'Appearance' },
  { id: 'about',         label: 'About' },
];

const MC = 'var(--gold)';
const style = { '--mc': MC } as React.CSSProperties;

function SectionCard({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <div className="card" style={{ marginBottom: '1rem' }}>
      <h3 style={{ marginBottom: '1.25rem' }}>{title}</h3>
      {children}
    </div>
  );
}

function Row({ label, desc, children }: { label: string; desc?: string; children: React.ReactNode }) {
  return (
    <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', padding: '0.75rem 0', borderBottom: '1px solid rgba(26,47,82,0.4)' }}>
      <div>
        <div style={{ fontSize: '0.875rem', color: 'var(--text)', fontWeight: 500 }}>{label}</div>
        {desc && <div style={{ fontSize: '0.72rem', color: 'var(--text3)', marginTop: '0.15rem' }}>{desc}</div>}
      </div>
      <div style={{ flexShrink: 0, marginLeft: '1rem' }}>{children}</div>
    </div>
  );
}

function Toggle({ enabled, onToggle }: { enabled: boolean; onToggle: () => void }) {
  return (
    <button
      onClick={onToggle}
      style={{
        width: 40, height: 22, borderRadius: 11,
        background: enabled ? 'var(--gold)' : 'rgba(255,255,255,0.1)',
        border: 'none', cursor: 'pointer', position: 'relative',
        transition: 'background 0.2s',
      }}
    >
      <span style={{
        position: 'absolute',
        top: 2, left: enabled ? 20 : 2,
        width: 18, height: 18, borderRadius: '50%',
        background: '#fff',
        transition: 'left 0.2s',
        boxShadow: '0 1px 3px rgba(0,0,0,0.3)',
      }}/>
    </button>
  );
}

const ENV_STYLE: Record<string, { bg: string; border: string; color: string }> = {
  production:  { bg: 'rgba(31,200,122,0.1)',  border: 'rgba(31,200,122,0.25)',  color: 'var(--cash)'     },
  development: { bg: 'rgba(79,158,248,0.1)',  border: 'rgba(79,158,248,0.25)',  color: 'var(--nexus)'    },
  sandbox:     { bg: 'rgba(212,168,67,0.1)',  border: 'rgba(212,168,67,0.25)', color: 'var(--gold)'     },
};

function EnvBadge({ env }: { env: string }) {
  const s = ENV_STYLE[env] ?? ENV_STYLE.sandbox;
  return (
    <span style={{ fontSize: '0.78rem', background: s.bg, color: s.color, border: `1px solid ${s.border}`, padding: '0.2rem 0.6rem', borderRadius: '99px', textTransform: 'capitalize' }}>
      {env}
    </span>
  );
}

function Integrations() {
  const [plaidEnv, setPlaidEnv] = useState('...');
  const [syncing, setSyncing] = useState(false);
  const [syncMsg, setSyncMsg] = useState('');
  const [clearing, setClearing] = useState(false);

  useEffect(() => {
    fetch('/api/config/plaid-env')
      .then(r => r.json())
      .then(d => setPlaidEnv(d.environment ?? 'sandbox'))
      .catch(() => setPlaidEnv('sandbox'));
  }, []);

  const handleSync = async () => {
    setSyncing(true);
    setSyncMsg('');
    try {
      const r = await syncApi.trigger();
      setSyncMsg(`Sync complete — ${r.data.transactionsAdded ?? 0} new transactions`);
    } catch {
      setSyncMsg('Sync failed');
    } finally {
      setSyncing(false);
    }
  };

  const handleClearAccounts = async () => {
    if (!window.confirm('This will unlink ALL bank accounts. You will need to re-connect your real bank accounts. Continue?')) return;
    setClearing(true);
    setSyncMsg('');
    try {
      const items: { id: string }[] = await fetch('/api/plaid/items').then(r => r.json());
      await Promise.all(items.map(i => fetch(`/api/plaid/items/${i.id}`, { method: 'DELETE' })));
      setSyncMsg(`Unlinked ${items.length} account(s). Connect your real bank below.`);
    } catch {
      setSyncMsg('Failed to unlink accounts.');
    } finally {
      setClearing(false);
    }
  };

  return (
    <div>
      <SectionCard title="Banking · Plaid">
        <Row label="Connect Bank Account" desc="Link your real checking, savings, and credit card accounts via Plaid">
          <PlaidLinkButton onSuccess={() => setSyncMsg('Account linked!')} />
        </Row>
        <Row label="Sync Transactions" desc="Manually pull fresh data from all linked accounts">
          <div style={{ display: 'flex', alignItems: 'center', gap: '0.75rem' }}>
            {syncMsg && <span style={{ fontSize: '0.75rem', color: 'var(--cash)' }}>{syncMsg}</span>}
            <button className="btn-ghost" onClick={handleSync} disabled={syncing}>
              {syncing ? 'Syncing…' : '↻ Sync Now'}
            </button>
          </div>
        </Row>
        <Row label="Active Environment" desc="Set via PLAID_ENV in your .env file — restart the API to change">
          <EnvBadge env={plaidEnv} />
        </Row>
        {plaidEnv === 'sandbox' && (
          <Row label="Clear Sandbox Accounts" desc="Remove test accounts before switching to development/production">
            <button className="btn-danger-ghost" onClick={handleClearAccounts} disabled={clearing}>
              {clearing ? 'Clearing…' : 'Unlink All'}
            </button>
          </Row>
        )}
      </SectionCard>

      <SectionCard title="Trading · Coming Soon">
        {[
          { name: 'Robinhood',  desc: 'US stock & options broker',   icon: '🟢', status: 'Soon' },
          { name: 'Zerodha',    desc: 'Indian equities & derivatives', icon: '🔵', status: 'Soon' },
          { name: 'Coinbase',   desc: 'Cryptocurrency exchange',       icon: '🔷', status: 'Soon' },
          { name: 'Alpaca',     desc: 'Algorithmic trading API',       icon: '⚡', status: 'Soon' },
        ].map(i => (
          <Row key={i.name} label={`${i.icon} ${i.name}`} desc={i.desc}>
            <span style={{ fontSize: '0.72rem', color: 'var(--text3)', background: 'rgba(255,255,255,0.04)', border: '1px solid var(--border)', padding: '0.2rem 0.6rem', borderRadius: '99px' }}>{i.status}</span>
          </Row>
        ))}
      </SectionCard>

      <SectionCard title="Real Estate · Coming Soon">
        <Row label="🏠 Zillow / Zestimate" desc="Property value estimates">
          <span style={{ fontSize: '0.72rem', color: 'var(--text3)', background: 'rgba(255,255,255,0.04)', border: '1px solid var(--border)', padding: '0.2rem 0.6rem', borderRadius: '99px' }}>Soon</span>
        </Row>
      </SectionCard>
    </div>
  );
}

function Notifications() {
  const [prefs, setPrefs] = useState({
    vault_unusual:   true,
    vault_sync:      false,
    vitara_goals:    true,
    nexus_alerts:    true,
    aasthi_maintain: true,
    sutra_expiry:    true,
    karma_habits:    false,
  });

  const toggle = (key: keyof typeof prefs) => setPrefs(p => ({ ...p, [key]: !p[key] }));

  return (
    <SectionCard title="Notification Preferences">
      <Row label="Unusual spending detected" desc="Vault · Alert when a transaction is significantly above average">
        <Toggle enabled={prefs.vault_unusual} onToggle={() => toggle('vault_unusual')} />
      </Row>
      <Row label="Sync completed" desc="Vault · Notify when bank data syncs successfully">
        <Toggle enabled={prefs.vault_sync} onToggle={() => toggle('vault_sync')} />
      </Row>
      <Row label="Health goal not met" desc="Vitara · Daily reminder if steps/sleep goal missed">
        <Toggle enabled={prefs.vitara_goals} onToggle={() => toggle('vitara_goals')} />
      </Row>
      <Row label="Price alert triggered" desc="Nexus · When a watchlist stock hits your target price">
        <Toggle enabled={prefs.nexus_alerts} onToggle={() => toggle('nexus_alerts')} />
      </Row>
      <Row label="Property maintenance due" desc="Aasthi · Upcoming maintenance reminders">
        <Toggle enabled={prefs.aasthi_maintain} onToggle={() => toggle('aasthi_maintain')} />
      </Row>
      <Row label="Document expiry" desc="Sutra · 30 days before passport, insurance, or license expires">
        <Toggle enabled={prefs.sutra_expiry} onToggle={() => toggle('sutra_expiry')} />
      </Row>
      <Row label="Habit check-in" desc="Karma · Daily reminder to log your habits">
        <Toggle enabled={prefs.karma_habits} onToggle={() => toggle('karma_habits')} />
      </Row>
    </SectionCard>
  );
}

function DataSync() {
  return (
    <div>
      <SectionCard title="Sync Schedule">
        <Row label="Auto-sync frequency" desc="How often Maaya pulls fresh data from linked accounts">
          <select style={{ width: 'auto', padding: '0.35rem 0.75rem' }}>
            <option>Every 6 hours</option>
            <option>Every 12 hours</option>
            <option>Daily at midnight</option>
            <option>Manual only</option>
          </select>
        </Row>
        <Row label="Transaction history depth" desc="How far back to sync on first link">
          <select style={{ width: 'auto', padding: '0.35rem 0.75rem' }}>
            <option>90 days</option>
            <option>6 months</option>
            <option>1 year</option>
            <option>All available</option>
          </select>
        </Row>
      </SectionCard>
      <SectionCard title="Data Export">
        <Row label="Export all data" desc="Download a complete JSON backup of your Maaya data">
          <button className="btn-ghost" disabled>Export JSON</button>
        </Row>
        <Row label="Export transactions" desc="Download transaction history as CSV">
          <button className="btn-ghost" disabled>Export CSV</button>
        </Row>
      </SectionCard>
      <SectionCard title="Danger Zone">
        <Row label="Delete all transaction data" desc="Remove all synced transactions (accounts remain linked)">
          <button className="btn-danger-ghost" disabled>Delete Transactions</button>
        </Row>
        <Row label="Unlink all accounts" desc="Disconnect all Plaid-linked bank accounts">
          <button className="btn-danger-ghost" disabled>Unlink All</button>
        </Row>
      </SectionCard>
    </div>
  );
}

function Appearance() {
  return (
    <SectionCard title="Theme">
      <Row label="Color theme" desc="Dark mode (default) — light mode coming soon">
        <div style={{ display: 'flex', gap: '0.5rem' }}>
          {[
            { name: 'Dark', active: true },
            { name: 'Light', active: false },
            { name: 'System', active: false },
          ].map(t => (
            <button
              key={t.name}
              className={t.active ? 'btn-primary' : 'btn-ghost'}
              style={{ fontSize: '0.78rem', padding: '0.35rem 0.875rem', opacity: t.active ? 1 : 0.5 }}
              disabled={!t.active}
            >
              {t.name}
            </button>
          ))}
        </div>
      </Row>
      <Row label="Sidebar" desc="Full labels or icon-only compact mode">
        <div style={{ display: 'flex', gap: '0.5rem' }}>
          <button className="btn-primary" style={{ fontSize: '0.78rem', padding: '0.35rem 0.875rem' }}>Labels</button>
          <button className="btn-ghost" style={{ fontSize: '0.78rem', padding: '0.35rem 0.875rem', opacity: 0.5 }} disabled>Icons only</button>
        </div>
      </Row>
    </SectionCard>
  );
}

function About() {
  return (
    <SectionCard title="About Maaya">
      {[
        { label: 'Version',      value: '0.1.0 (alpha)' },
        { label: 'Build',        value: 'Local Development' },
        { label: 'Backend',      value: '.NET 8 + SQLite' },
        { label: 'Frontend',     value: 'React 18 + Vite + TypeScript' },
        { label: 'Auth',         value: 'Plaid Sandbox' },
        { label: 'API URL',      value: 'http://localhost:5000' },
      ].map(r => (
        <Row key={r.label} label={r.label}>
          <span style={{ fontSize: '0.82rem', color: 'var(--text2)', fontFamily: 'monospace' }}>{r.value}</span>
        </Row>
      ))}
      <div style={{ marginTop: '1.5rem', padding: '1rem', background: 'rgba(212,168,67,0.05)', border: '1px solid rgba(212,168,67,0.15)', borderRadius: 'var(--r-sm)' }}>
        <p style={{ fontSize: '0.82rem', color: 'var(--text3)', lineHeight: 1.7 }}>
          Maaya is your personal Life Manager OS — a self-hosted platform for managing finance, health, trading, real estate, knowledge, and goals. Built for privacy-first, local-first operation.
          <br/><br/>
          <strong style={{ color: 'var(--gold)' }}>Phase 2:</strong> Plaid production data, Raspberry Pi 4 deployment, mobile PWA.
        </p>
      </div>
    </SectionCard>
  );
}

export default function SettingsHub() {
  const [page, setPage] = useState<Page>('integrations');
  return (
    <div>
      <div style={{ marginBottom: '1.5rem' }}>
        <h1 style={{ marginBottom: '0.25rem' }}>Settings</h1>
        <p style={{ color: 'var(--text3)', fontSize: '0.875rem' }}>Manage integrations, preferences, and your Maaya account</p>
      </div>
      <nav className="module-subnav" style={style}>
        {TABS.map(t => (
          <button key={t.id} className={`module-tab ${page === t.id ? 'active' : ''}`} onClick={() => setPage(t.id)}>
            {t.label}
          </button>
        ))}
      </nav>
      {page === 'integrations'  && <Integrations />}
      {page === 'notifications' && <Notifications />}
      {page === 'data'          && <DataSync />}
      {page === 'appearance'    && <Appearance />}
      {page === 'about'         && <About />}
    </div>
  );
}
