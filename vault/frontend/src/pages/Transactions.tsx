import { useState, useEffect } from 'react';
import { transactionsApi } from '@/services/api';
import type { Transaction } from '@/types';
import '../styles/transactions.css';

const fmt = (n: number) =>
  '$' + Math.abs(n).toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 });

export default function Transactions() {
  const [transactions, setTransactions] = useState<Transaction[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const [filters, setFilters] = useState({
    startDate: new Date(Date.now() - 30 * 86400000).toISOString().split('T')[0],
    endDate: new Date().toISOString().split('T')[0],
    category: '',
  });

  useEffect(() => { loadTransactions(); }, [filters]);

  const loadTransactions = async () => {
    try {
      setLoading(true);
      const res = await transactionsApi.getAll({
        startDate: filters.startDate ? new Date(filters.startDate) : undefined,
        endDate: filters.endDate ? new Date(filters.endDate) : undefined,
        category: filters.category || undefined,
      });
      setTransactions(res.data);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to load');
    } finally {
      setLoading(false);
    }
  };

  const set = (key: string, val: string) => setFilters(f => ({ ...f, [key]: val }));

  if (error) return <div className="error">{error}</div>;

  return (
    <div className="transactions">
      <h1>Transactions</h1>

      <div className="filters card">
        <div className="filter-row">
          <div className="filter-group">
            <label>From</label>
            <input type="date" value={filters.startDate} onChange={e => set('startDate', e.target.value)} />
          </div>
          <div className="filter-group">
            <label>To</label>
            <input type="date" value={filters.endDate} onChange={e => set('endDate', e.target.value)} />
          </div>
          <div className="filter-group">
            <label>Category</label>
            <input placeholder="Filter category…" value={filters.category} onChange={e => set('category', e.target.value)} />
          </div>
        </div>
      </div>

      {!loading && (
        <p className="txn-count">{transactions.length} transaction{transactions.length !== 1 ? 's' : ''}</p>
      )}

      <div className="card transactions-table">
        {loading ? (
          <div className="loading">Loading…</div>
        ) : transactions.length === 0 ? (
          <div className="no-data">No transactions found</div>
        ) : (
          <table>
            <thead>
              <tr>
                <th>Date</th>
                <th>Merchant</th>
                <th>Category</th>
                <th>Account</th>
                <th>Institution</th>
                <th style={{ textAlign: 'right' }}>Amount</th>
                <th>Status</th>
              </tr>
            </thead>
            <tbody>
              {transactions.map(t => (
                <tr key={t.id} className={t.isPending ? 'pending' : ''}>
                  <td style={{ whiteSpace: 'nowrap', color: 'var(--text3)' }}>
                    {new Date(t.transactionDate).toLocaleDateString('en-US', { month: 'short', day: 'numeric' })}
                  </td>
                  <td style={{ maxWidth: '200px', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                    {t.merchantName || t.description}
                  </td>
                  <td>
                    {t.category ? <span className="badge" style={{ background: 'var(--surface3)', color: 'var(--text2)', fontSize: '0.7rem' }}>{t.category}</span> : <span className="text-dim">—</span>}
                  </td>
                  <td style={{ color: 'var(--text2)' }}>{t.accountName || '—'}</td>
                  <td style={{ color: 'var(--text2)' }}>{t.institutionName || '—'}</td>
                  <td style={{ textAlign: 'right' }}>
                    <span className={t.amount > 0 ? 'text-debt' : 'text-cash'} style={{ fontWeight: 700, fontVariantNumeric: 'tabular-nums' }}>
                      {t.amount > 0 ? '-' : '+'}{fmt(t.amount)}
                    </span>
                  </td>
                  <td>
                    {t.isPending
                      ? <span className="badge badge-pending">Pending</span>
                      : <span className="text-dim">Posted</span>}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>
    </div>
  );
}
