import { useState, useEffect } from 'react';
import { plaidApi } from '@/services/api';
import PlaidLinkButton from '@/components/PlaidLink';
import '../styles/settings.css';

interface LinkedItem {
  id: string;
  institutionName: string;
  plaidInstitutionId: string;
  createdAt: string;
}

export default function Settings() {
  const [items, setItems] = useState<LinkedItem[]>([]);
  const [loadingItems, setLoadingItems] = useState(true);
  const [saved, setSaved] = useState(false);
  const [refreshKey, setRefreshKey] = useState(0);

  useEffect(() => {
    plaidApi.getItems()
      .then(res => setItems(res.data))
      .catch(() => {})
      .finally(() => setLoadingItems(false));
  }, [refreshKey]);

  const handleUnlink = async (id: string, name: string) => {
    if (!window.confirm(`Unlink ${name}? All associated data will be removed.`)) return;
    await plaidApi.unlinkItem(id);
    setItems(prev => prev.filter(i => i.id !== id));
  };

  return (
    <div className="settings">
      <h1>Settings</h1>

      {/* ── Linked Accounts ── */}
      <div className="card settings-card">
        <h2>Linked Accounts</h2>
        <p className="text-muted" style={{ marginBottom: '1.25rem' }}>
          Manage your connected financial institutions. Data syncs nightly at 2 AM.
        </p>

        {loadingItems ? (
          <p className="text-muted">Loading…</p>
        ) : items.length === 0 ? (
          <p className="text-muted" style={{ marginBottom: '1rem' }}>No accounts linked yet.</p>
        ) : (
          <div className="linked-accounts" style={{ marginBottom: '1.25rem' }}>
            {items.map(item => (
              <div key={item.id} className="linked-item">
                <div>
                  <p className="linked-name">{item.institutionName}</p>
                  <p className="text-muted">Linked {new Date(item.createdAt).toLocaleDateString()}</p>
                </div>
                <button className="btn-danger-ghost" onClick={() => handleUnlink(item.id, item.institutionName)}>
                  Unlink
                </button>
              </div>
            ))}
          </div>
        )}

        <PlaidLinkButton onSuccess={() => setRefreshKey(k => k + 1)} />
      </div>

      {/* ── Sync Schedule ── */}
      <div className="card settings-card">
        <h2>Sync Schedule</h2>
        <div className="settings-row">
          <div>
            <p className="settings-label">Nightly Sync</p>
            <p className="text-muted">Runs automatically every night at 2:00 AM</p>
          </div>
          <span className="badge badge-cash">Active</span>
        </div>
        <div className="settings-row" style={{ marginTop: '0.875rem' }}>
          <div>
            <p className="settings-label">Transaction History</p>
            <p className="text-muted">Pulls last 30 days of transactions per sync</p>
          </div>
        </div>
      </div>

      {/* ── About ── */}
      <div className="card settings-card">
        <h2>About Vault</h2>
        <div className="about-grid">
          <div className="about-row"><span className="text-muted">Version</span><span>0.1.0</span></div>
          <div className="about-row"><span className="text-muted">Environment</span><span>{import.meta.env.MODE}</span></div>
          <div className="about-row"><span className="text-muted">Database</span><span>SQLite · local</span></div>
          <div className="about-row"><span className="text-muted">Backend</span><span>.NET 8 Worker Service</span></div>
        </div>
      </div>

      {/* ── Danger zone ── */}
      <div className="card settings-card settings-card--danger">
        <h2>Danger Zone</h2>
        <p className="text-muted" style={{ marginBottom: '1rem' }}>
          Your financial data is stored locally and never shared with third parties beyond the Plaid API.
        </p>
        <button
          className="btn-danger-ghost"
          style={{ fontSize: '0.85rem', padding: '0.5rem 1rem' }}
          onClick={() => { if (window.confirm('Clear all local preferences?')) { localStorage.clear(); window.location.reload(); } }}
        >
          Clear Local Preferences
        </button>
      </div>
    </div>
  );
}
