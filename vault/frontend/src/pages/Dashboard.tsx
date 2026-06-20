import { useState, useEffect, useCallback } from 'react';
import { summaryApi, syncApi } from '@/services/api';
import type { DashboardSummary, SyncStatus } from '@/types';
import PlaidLinkButton from '@/components/PlaidLink';
import '../styles/dashboard.css';

const fmt = (n: number) =>
  '$' + Math.abs(n).toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 });

const fmtDate = (d: string) =>
  new Date(d).toLocaleDateString('en-US', { month: 'short', day: 'numeric' });

const daysUntil = (d: string) => {
  const diff = new Date(d).getTime() - Date.now();
  return Math.ceil(diff / 86400000);
};

export default function Dashboard() {
  const [data, setData] = useState<DashboardSummary | null>(null);
  const [syncStatus, setSyncStatus] = useState<SyncStatus | null>(null);
  const [loading, setLoading] = useState(true);
  const [syncing, setSyncing] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [refreshKey, setRefreshKey] = useState(0);

  useEffect(() => { load(); }, [refreshKey]);

  const load = async () => {
    try {
      setLoading(true);
      const res = await summaryApi.getDashboard();
      setData(res.data);
      try { setSyncStatus((await syncApi.getLatestStatus()).data); } catch { }
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to load');
    } finally {
      setLoading(false);
    }
  };

  const handleSync = async () => {
    try {
      setSyncing(true);
      const res = await syncApi.trigger();
      setSyncStatus(res.data);
      setRefreshKey(k => k + 1);
    } catch { } finally { setSyncing(false); }
  };

  const onLinkSuccess = useCallback(() => setRefreshKey(k => k + 1), []);

  if (loading) return <div className="loading">Loading dashboard…</div>;
  if (error)   return <div className="error">{error}</div>;

  const hasAccounts = data && (data.cashByInstitution.length > 0 || data.debtByInstitution.length > 0);
  const maxSpend = data ? Math.max(...data.spendingByCategory.map(c => c.totalAmount), 1) : 1;

  return (
    <div className="dashboard">
      {/* ── Header ── */}
      <div className="dash-header">
        <h1>Dashboard</h1>
        <div className="dash-header-right">
          <PlaidLinkButton onSuccess={onLinkSuccess} />
          <button className="btn-ghost" onClick={handleSync} disabled={syncing}>
            {syncing ? 'Syncing…' : '↻ Sync'}
          </button>
        </div>
      </div>

      {syncStatus && (
        <div className={`sync-bar ${syncStatus.status}`}>
          Last sync {fmtDate(syncStatus.lastSyncTime)}
          {syncStatus.transactionsAdded ? ` · ${syncStatus.transactionsAdded} added` : ''}
          {syncStatus.errorMessage ? ` · ${syncStatus.errorMessage}` : ''}
        </div>
      )}

      {!hasAccounts ? (
        <div className="empty-state">
          <div className="empty-icon">🏦</div>
          <h2>No accounts linked</h2>
          <p>Connect a bank or credit card account to see your balances, transactions and debt overview.</p>
          <PlaidLinkButton onSuccess={onLinkSuccess} />
        </div>
      ) : (
        <>
          {/* ── Hero strip ── */}
          <div className="hero-strip">
            <div className="hero-cell hero-cell--worth">
              <div className="hero-label"><span className="dot" /> Net Worth</div>
              <div className="hero-value" style={{ color: data!.netWorth >= 0 ? 'var(--cash)' : 'var(--debt)' }}>
                {data!.netWorth < 0 ? '-' : ''}{fmt(data!.netWorth)}
              </div>
              <div className="hero-sub">cash minus debt</div>
            </div>
            <div className="hero-cell hero-cell--cash">
              <div className="hero-label"><span className="dot" /> Total Cash</div>
              <div className="hero-value">{fmt(data!.totalCash)}</div>
              <div className="hero-sub">{data!.cashByInstitution.length} institution{data!.cashByInstitution.length !== 1 ? 's' : ''}</div>
            </div>
            <div className="hero-cell hero-cell--debt">
              <div className="hero-label"><span className="dot" /> Total Debt</div>
              <div className="hero-value">{fmt(data!.totalDebt)}</div>
              <div className="hero-sub">{data!.debtByInstitution.length} institution{data!.debtByInstitution.length !== 1 ? 's' : ''}</div>
            </div>
          </div>

          {/* ── Cash vs Debt by institution ── */}
          <div className="two-col">
            {/* Cash */}
            <div className="card card-cash">
              <h3>Cash by Institution</h3>
              {data!.cashByInstitution.length === 0 ? (
                <p className="text-dim">No depository accounts</p>
              ) : data!.cashByInstitution.map((inst, i) => (
                <div key={inst.institutionName}>
                  <div className="inst-header-row" style={i === 0 ? {} : { marginTop: '0.875rem' }}>
                    <span className="inst-bank-name">{inst.institutionName}</span>
                    <span className="inst-bank-total amount-cash">{fmt(inst.totalBalance)}</span>
                  </div>
                  <div className="inst-accounts">
                    {inst.accounts.map(a => (
                      <div key={a.name} className="acct-row">
                        <div className="acct-left">
                          <span className="acct-dot" style={{ background: 'var(--cash)' }} />
                          <span className="acct-name">{a.name}</span>
                          <span className="acct-subtype">{a.subType}</span>
                        </div>
                        <span className="acct-bal amount-cash">{fmt(a.balance)}</span>
                      </div>
                    ))}
                  </div>
                </div>
              ))}
            </div>

            <div className="card card-debt">
              <h3>Debt by Institution</h3>
              {data!.debtByInstitution.length === 0 ? (
                <p className="text-dim">No credit/loan accounts</p>
              ) : data!.debtByInstitution.map((inst, i) => (
                <div key={inst.institutionName}>
                  <div className="inst-header-row" style={i === 0 ? {} : { marginTop: '0.875rem' }}>
                    <span className="inst-bank-name">{inst.institutionName}</span>
                    <span className="inst-bank-total amount-debt">{fmt(inst.totalBalance)}</span>
                  </div>
                  <div className="inst-accounts">
                    {inst.accounts.map(a => (
                      <div key={a.name} className="acct-row">
                        <div className="acct-left">
                          <span className="acct-dot" style={{ background: 'var(--debt)' }} />
                          <span className="acct-name">{a.name}</span>
                          <span className="acct-subtype">{a.subType}</span>
                        </div>
                        <span className="acct-bal amount-debt">{fmt(a.balance)}</span>
                      </div>
                    ))}
                  </div>
                </div>
              ))}
            </div>
          </div>

          {/* ── Upcoming bills + Category spending ── */}
          <div className="two-col">
            {/* Upcoming Bills */}
            <div className="card">
              <h3>Upcoming Bills</h3>
              {data!.upcomingBills.length === 0 ? (
                <p className="text-dim">No recurring bills detected yet</p>
              ) : (
                <div className="bills-list">
                  {data!.upcomingBills.map(b => {
                    const days = daysUntil(b.estimatedNextDate);
                    return (
                      <div key={b.merchantName} className="bill-row">
                        <div>
                          <div className="bill-name">{b.merchantName}</div>
                          <div className="bill-date">
                            Due {fmtDate(b.estimatedNextDate)}
                            <span style={{ marginLeft: '0.4rem', color: days <= 7 ? 'var(--debt)' : 'var(--text3)' }}>
                              · {days}d
                            </span>
                          </div>
                        </div>
                        <div className="bill-amount">{fmt(b.lastAmount)}</div>
                      </div>
                    );
                  })}
                </div>
              )}
            </div>

            {/* Spending by Category */}
            <div className="card">
              <h3>Spending by Category <span style={{ color: 'var(--text3)', fontWeight: 400 }}>· 30 days</span></h3>
              {data!.spendingByCategory.length === 0 ? (
                <p className="text-dim">No transactions yet</p>
              ) : (
                <div className="cat-list">
                  {data!.spendingByCategory.slice(0, 7).map(c => (
                    <div key={c.category} className="cat-row">
                      <div className="cat-header">
                        <span className="cat-name">{c.category}</span>
                        <span className="cat-amount">{fmt(c.totalAmount)}</span>
                      </div>
                      <div className="cat-bar-wrap">
                        <div className="cat-bar" style={{ width: `${(c.totalAmount / maxSpend) * 100}%` }} />
                      </div>
                      <span className="cat-count">{c.transactionCount} transaction{c.transactionCount !== 1 ? 's' : ''}</span>
                    </div>
                  ))}
                </div>
              )}
            </div>
          </div>

          {/* ── Recent transactions ── */}
          <div className="card" style={{ marginTop: '0' }}>
            <h3>Recent Transactions</h3>
            {data!.recentTransactions.length === 0 ? (
              <p className="text-dim">No transactions found</p>
            ) : (
              <div className="recent-txns">
                {data!.recentTransactions.map(t => (
                  <div key={t.id} className="txn-row">
                    <div className="txn-left">
                      <div className="txn-merchant">{t.merchantName || t.description}</div>
                      <div className="txn-meta">
                        {t.category && <span>{t.category} · </span>}
                        <span>{t.accountName}</span>
                        {t.isPending && <span className="badge badge-pending" style={{ marginLeft: '0.4rem' }}>Pending</span>}
                      </div>
                    </div>
                    <div className="txn-right">
                      <div className={`txn-amount ${t.amount > 0 ? 'text-debt' : 'text-cash'}`}>
                        {t.amount > 0 ? '-' : '+'}{fmt(t.amount)}
                      </div>
                      <div className="txn-date">{fmtDate(t.transactionDate)}</div>
                    </div>
                  </div>
                ))}
              </div>
            )}
          </div>
        </>
      )}
    </div>
  );
}
