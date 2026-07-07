import { useState, useEffect, useRef, useCallback } from 'react';
import { transactionsApi } from '@/services/api';
import type { Transaction } from '@/types';
import '../styles/transactions.css';

// ── Category definitions ──────────────────────────────────────
const CATALOG_GROUPS = [
  { group: 'ESSENTIALS', cats: ['Housing', 'Groceries', 'Utilities', 'Transport', 'Health & Fitness'] },
  { group: 'LIFESTYLE',  cats: ['Dining', 'Shopping', 'Entertainment', 'Travel', 'Subscriptions'] },
  { group: 'FINANCIAL',  cats: ['Fees & Interest', 'Transfers', 'Other'] },
];
const ALL_CATS = CATALOG_GROUPS.flatMap(g => g.cats);

const CAT_COLORS: Record<string, string> = {
  'Housing': '#1fc87a', 'Groceries': '#22c55e', 'Utilities': '#06b6d4',
  'Transport': '#3b82f6', 'Health & Fitness': '#10b981',
  'Dining': '#f97316', 'Shopping': '#ef4444', 'Entertainment': '#8b5cf6',
  'Travel': '#a855f7', 'Subscriptions': '#d946ef',
  'Fees & Interest': '#60a5fa', 'Transfers': '#94a3b8', 'Other': '#6b7280',
};

const AVATAR_COLORS = ['#1fc87a','#06c8a0','#f0a030','#a855f7','#f472b6','#3b82f6','#ef4444','#f59e0b','#10b981','#8b5cf6'];
const ACCT_COLORS  = ['#3b82f6','#f0a030','#1fc87a','#a855f7','#f472b6','#06c8a0','#ef4444','#60a5fa'];

const fmt = (n: number) =>
  '$' + Math.abs(n).toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 });

const catColor = (cat?: string | null) => cat ? (CAT_COLORS[cat] ?? '#6b7280') : '#6b7280';
const avatarColor = (name: string) => AVATAR_COLORS[name.charCodeAt(0) % AVATAR_COLORS.length];
const acctColor   = (name: string) => ACCT_COLORS[name.charCodeAt(0) % ACCT_COLORS.length];

// ── Merchant Avatar ───────────────────────────────────────────
function MerchantAvatar({ name }: { name: string }) {
  return (
    <div className="txn-avatar" style={{ background: avatarColor(name) }}>
      {name.charAt(0).toUpperCase()}
    </div>
  );
}

// ── Category Badge + Dropdown ─────────────────────────────────
function CategoryBadge({
  txId, category, onChange,
}: { txId: string; category?: string | null; onChange: (cat: string | null) => void }) {
  const [open, setOpen] = useState(false);
  const [saving, setSaving] = useState(false);
  const wrapRef = useRef<HTMLDivElement>(null);
  const color = catColor(category);

  useEffect(() => {
    if (!open) return;
    const handler = (e: MouseEvent) => {
      if (!wrapRef.current?.contains(e.target as Node)) setOpen(false);
    };
    document.addEventListener('mousedown', handler);
    return () => document.removeEventListener('mousedown', handler);
  }, [open]);

  const select = async (cat: string | null) => {
    setOpen(false);
    setSaving(true);
    try {
      await transactionsApi.updateCategory(txId, cat);
      onChange(cat);
    } catch { /* ignore */ }
    finally { setSaving(false); }
  };

  return (
    <div className="cat-badge-wrap" ref={wrapRef}>
      {category ? (
        <div className="cat-badge" style={{ '--cat': color } as React.CSSProperties}>
          <button className="cat-badge-label" onClick={() => setOpen(v => !v)} disabled={saving}>
            <span className="cat-badge-dot" />
            {category}
            <span className="cat-badge-chevron">▾</span>
          </button>
          <button className="cat-badge-clear" onClick={() => select(null)} disabled={saving} title="Remove category">
            ×
          </button>
        </div>
      ) : (
        <button className="cat-badge-empty" onClick={() => setOpen(v => !v)} disabled={saving}>
          + categorize
        </button>
      )}

      {open && (
        <div className="cat-dropdown">
          {CATALOG_GROUPS.map(g => (
            <div key={g.group}>
              <div className="cat-dropdown-group">{g.group}</div>
              {g.cats.map(c => (
                <button
                  key={c}
                  className={`cat-dropdown-item ${c === category ? 'active' : ''}`}
                  onClick={() => select(c)}
                >
                  <span className="cat-dd-dot" style={{ background: CAT_COLORS[c] }} />
                  {c}
                </button>
              ))}
            </div>
          ))}
          {category && (
            <>
              <div className="cat-dropdown-divider" />
              <button className="cat-dropdown-item cat-dropdown-clear" onClick={() => select(null)}>
                Remove category
              </button>
            </>
          )}
        </div>
      )}
    </div>
  );
}

// ── Category Catalog sidebar ──────────────────────────────────
function CategoryCatalog({ transactions }: { transactions: Transaction[] }) {
  const stats: Record<string, { count: number; total: number }> = {};
  for (const t of transactions) {
    if (!t.category) continue;
    if (!stats[t.category]) stats[t.category] = { count: 0, total: 0 };
    stats[t.category].count++;
    if (t.amount > 0) stats[t.category].total += t.amount;
  }

  const categorizedTotal = transactions
    .filter(t => t.category && t.amount > 0)
    .reduce((s, t) => s + t.amount, 0);

  const totalCats = ALL_CATS.filter(c => stats[c]?.count).length;

  return (
    <div className="cat-catalog">
      <div className="cat-catalog-header">
        <span>Category catalog{totalCats > 0 && <span className="cat-catalog-count"> {totalCats}</span>}</span>
      </div>

      {CATALOG_GROUPS.map(g => (
        <div key={g.group} className="cat-catalog-group">
          <div className="cat-catalog-group-label">{g.group}</div>
          {g.cats.map(c => {
            const s = stats[c];
            return (
              <div key={c} className={`cat-catalog-row ${s ? 'has-data' : ''}`}>
                <div className="cat-catalog-left">
                  <span className="cat-catalog-dot" style={{ background: CAT_COLORS[c] }} />
                  <span className="cat-catalog-name">{c}</span>
                </div>
                <div className="cat-catalog-right">
                  <span className="cat-catalog-cnt">{s?.count ?? 0}</span>
                  <span className="cat-catalog-amount">{s ? fmt(s.total) : '$0'}</span>
                </div>
              </div>
            );
          })}
        </div>
      ))}

      <div className="cat-catalog-footer">
        <span>Categorized spend</span>
        <span className="cat-catalog-total">{fmt(categorizedTotal)}</span>
      </div>
    </div>
  );
}

// ── Main Transactions Component ───────────────────────────────
export default function Transactions() {
  const [transactions, setTransactions] = useState<Transaction[]>([]);
  const [loading, setLoading] = useState(true);
  const [search, setSearch] = useState('');
  const [dateRange, setDateRange] = useState({
    start: new Date(Date.now() - 30 * 86400000).toISOString().split('T')[0],
    end:   new Date().toISOString().split('T')[0],
  });

  const load = useCallback(async () => {
    try {
      setLoading(true);
      const res = await transactionsApi.getAll({
        startDate: new Date(dateRange.start),
        endDate:   new Date(dateRange.end),
      });
      setTransactions(res.data);
    } catch { /* offline */ }
    finally { setLoading(false); }
  }, [dateRange]);

  useEffect(() => { load(); }, [load]);

  const updateCat = (id: string, cat: string | null) => {
    setTransactions(prev =>
      prev.map(t => t.id === id ? { ...t, category: cat ?? undefined } : t)
    );
  };

  const filtered = transactions.filter(t => {
    if (!search) return true;
    const q = search.toLowerCase();
    return (
      (t.merchantName || t.description).toLowerCase().includes(q) ||
      t.accountName.toLowerCase().includes(q) ||
      (t.category || '').toLowerCase().includes(q)
    );
  });

  return (
    <div className="txn-page">
      {/* ── Left panel ─────────────────────────────────────── */}
      <div className="txn-main">
        {/* Top bar */}
        <div className="txn-topbar">
          <h2 className="txn-heading">Transactions</h2>
          <div className="txn-search-wrap">
            <svg className="txn-search-icon" viewBox="0 0 20 20" fill="none">
              <circle cx="9" cy="9" r="6" stroke="currentColor" strokeWidth="1.5" />
              <path d="M13.5 13.5L17 17" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" />
            </svg>
            <input
              className="txn-search"
              placeholder="Search merchant…"
              value={search}
              onChange={e => setSearch(e.target.value)}
            />
          </div>
          <div className="txn-date-filters">
            <input type="date" className="txn-date-input" value={dateRange.start}
              onChange={e => setDateRange(r => ({ ...r, start: e.target.value }))} />
            <span className="txn-date-sep">→</span>
            <input type="date" className="txn-date-input" value={dateRange.end}
              onChange={e => setDateRange(r => ({ ...r, end: e.target.value }))} />
          </div>
        </div>

        {/* Column headers */}
        <div className="txn-col-headers">
          <span className="txn-col-merchant">MERCHANT</span>
          <span className="txn-col-account">ACCOUNT</span>
          <span className="txn-col-date">DATE</span>
          <span className="txn-col-cat">CATEGORY</span>
          <span className="txn-col-amount">AMOUNT</span>
        </div>

        {/* Rows */}
        <div className="txn-rows">
          {loading ? (
            <div className="txn-empty">Loading…</div>
          ) : filtered.length === 0 ? (
            <div className="txn-empty">No transactions found</div>
          ) : filtered.map(t => {
            const merchant = t.merchantName || t.description;
            const dateFmt = new Date(t.transactionDate).toLocaleDateString('en-US', { month: 'short', day: 'numeric' });
            return (
              <div key={t.id} className="txn-row-new">
                {/* Merchant */}
                <div className="txn-merchant-cell txn-col-merchant">
                  <MerchantAvatar name={merchant} />
                  <div className="txn-merchant-info">
                    <div className="txn-merchant-name">{merchant}</div>
                    {t.description !== merchant && (
                      <div className="txn-merchant-sub">{t.description.toUpperCase().slice(0, 24)}</div>
                    )}
                  </div>
                </div>
                {/* Account */}
                <div className="txn-acct-cell txn-col-account">
                  <span className="txn-acct-dot" style={{ background: acctColor(t.accountName) }} />
                  <span className="txn-acct-name">{t.accountName}</span>
                </div>
                {/* Date */}
                <div className="txn-date-cell txn-col-date">{dateFmt}</div>
                {/* Category */}
                <div className="txn-col-cat">
                  <CategoryBadge txId={t.id} category={t.category} onChange={cat => updateCat(t.id, cat)} />
                </div>
                {/* Amount */}
                <div className="txn-amount-cell txn-col-amount">
                  <span className={t.amount > 0 ? 'txn-debit' : 'txn-credit'}>
                    {t.amount > 0 ? '-' : '+'}{fmt(t.amount)}
                  </span>
                  {t.isPending && <span className="txn-pending-tag">Pending</span>}
                </div>
              </div>
            );
          })}
        </div>

        {!loading && (
          <div className="txn-footer-count">
            {filtered.length} transaction{filtered.length !== 1 ? 's' : ''}
            {search && ` matching "${search}"`}
          </div>
        )}
      </div>

      {/* ── Right: Category catalog ─────────────────────────── */}
      <CategoryCatalog transactions={filtered} />
    </div>
  );
}
