import { useState, useEffect } from 'react';
import { categoryGroupApi } from '@/services/api';
import type { CategoryGroup, CategoryGroupSummary } from '@/types';
import '../styles/categorybudget.css';

const fmt = (n: number) =>
  '$' + Math.abs(n).toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 });

const COLORS = ['#c9a227', '#22d98a', '#f05454', '#6366f1', '#ec4899', '#14b8a6', '#f97316'];

export default function CategoryBudget() {
  const [groups, setGroups] = useState<CategoryGroup[]>([]);
  const [summaries, setSummaries] = useState<Record<string, CategoryGroupSummary>>({});
  const [loading, setLoading] = useState(true);
  const [expanded, setExpanded] = useState<string | null>(null);
  const [creating, setCreating] = useState(false);
  const [editingId, setEditingId] = useState<string | null>(null);
  const [newKeyword, setNewKeyword] = useState<Record<string, { keyword: string; isIncome: boolean; label: string }>>({});

  const [form, setForm] = useState({ name: '', budget: '', color: COLORS[0], notes: '' });

  useEffect(() => { loadGroups(); }, []);

  const loadGroups = async () => {
    try {
      setLoading(true);
      const res = await categoryGroupApi.getAll();
      setGroups(res.data);
    } finally { setLoading(false); }
  };

  const loadSummary = async (id: string) => {
    if (summaries[id]) return;
    const res = await categoryGroupApi.getSummary(id);
    setSummaries(prev => ({ ...prev, [id]: res.data }));
  };

  const handleExpand = (id: string) => {
    if (expanded === id) { setExpanded(null); return; }
    setExpanded(id);
    loadSummary(id);
  };

  const handleCreate = async () => {
    if (!form.name) return;
    await categoryGroupApi.create({ name: form.name, budget: parseFloat(form.budget) || 0, color: form.color, notes: form.notes || undefined });
    setCreating(false);
    setForm({ name: '', budget: '', color: COLORS[0], notes: '' });
    loadGroups();
  };

  const handleDelete = async (id: string, name: string) => {
    if (!window.confirm(`Delete "${name}"?`)) return;
    await categoryGroupApi.delete(id);
    setGroups(g => g.filter(x => x.id !== id));
    setSummaries(prev => { const n = { ...prev }; delete n[id]; return n; });
  };

  const handleAddKeyword = async (groupId: string) => {
    const kw = newKeyword[groupId];
    if (!kw?.keyword) return;
    await categoryGroupApi.addItem(groupId, { keyword: kw.keyword, isIncome: kw.isIncome, label: kw.label || undefined });
    setNewKeyword(prev => { const n = { ...prev }; delete n[groupId]; return n; });
    setSummaries(prev => { const n = { ...prev }; delete n[groupId]; return n; });
    loadGroups();
    loadSummary(groupId);
  };

  const handleRemoveKeyword = async (groupId: string, itemId: string) => {
    await categoryGroupApi.removeItem(groupId, itemId);
    setSummaries(prev => { const n = { ...prev }; delete n[groupId]; return n; });
    loadGroups();
    if (expanded === groupId) loadSummary(groupId);
  };

  if (loading) return <div className="loading">Loading…</div>;

  return (
    <div className="catbudget">
      <div className="catbudget-header">
        <div>
          <h1>Category Budget</h1>
          <p className="text-muted" style={{ marginTop: '0.25rem' }}>
            Group recurring transactions across institutions — track net income vs expenses per category.
          </p>
        </div>
        <button className="btn-primary" onClick={() => setCreating(true)}>+ New Category</button>
      </div>

      {/* ── Create form ── */}
      {creating && (
        <div className="card catbudget-form">
          <h3>New Category</h3>
          <div className="form-grid">
            <div className="form-group">
              <label>Name</label>
              <input placeholder="e.g. Apartment" value={form.name} onChange={e => setForm(f => ({ ...f, name: e.target.value }))} />
            </div>
            <div className="form-group">
              <label>Monthly Budget</label>
              <input type="number" placeholder="2500" value={form.budget} onChange={e => setForm(f => ({ ...f, budget: e.target.value }))} />
            </div>
          </div>
          <div className="form-group" style={{ marginTop: '0.875rem' }}>
            <label>Notes (optional)</label>
            <input placeholder="e.g. Rent + utilities + expenses for main apartment" value={form.notes} onChange={e => setForm(f => ({ ...f, notes: e.target.value }))} />
          </div>
          <div className="color-row">
            <label>Color</label>
            <div className="color-swatches">
              {COLORS.map(c => (
                <button key={c} className={`swatch ${form.color === c ? 'active' : ''}`} style={{ background: c }} onClick={() => setForm(f => ({ ...f, color: c }))} />
              ))}
            </div>
          </div>
          <div className="form-actions">
            <button className="btn-primary" onClick={handleCreate}>Create</button>
            <button className="btn-ghost" onClick={() => setCreating(false)}>Cancel</button>
          </div>
        </div>
      )}

      {/* ── Empty state ── */}
      {groups.length === 0 && !creating && (
        <div className="catbudget-empty card">
          <div className="empty-icon">📁</div>
          <h2>No categories yet</h2>
          <p className="text-muted">Create a category like "Apartment" and add keywords to match transactions like rent, electric, internet.</p>
          <button className="btn-primary" onClick={() => setCreating(true)}>Create First Category</button>
        </div>
      )}

      {/* ── Group cards ── */}
      <div className="catbudget-groups">
        {groups.map(g => {
          const s = summaries[g.id];
          const isOpen = expanded === g.id;
          const budgetUsed = s ? (s.totalExpenses / (g.budget || 1)) * 100 : 0;
          const isOver = s && g.budget > 0 && s.totalExpenses > g.budget;

          return (
            <div key={g.id} className="group-card card">
              {/* Group header strip */}
              <div className="group-color-bar" style={{ background: g.color }} />

              <div className="group-top">
                <div className="group-info">
                  <div className="group-name">{g.name}</div>
                  {g.notes && <div className="group-notes text-muted">{g.notes}</div>}
                </div>

                {s && (
                  <div className="group-stats">
                    <div className="stat">
                      <span className="stat-label">Income</span>
                      <span className="stat-value text-cash">{fmt(s.totalIncome)}</span>
                    </div>
                    <div className="stat">
                      <span className="stat-label">Expenses</span>
                      <span className={`stat-value ${isOver ? 'text-debt' : ''}`}>{fmt(s.totalExpenses)}</span>
                    </div>
                    <div className="stat">
                      <span className="stat-label">Net</span>
                      <span className={`stat-value ${s.net >= 0 ? 'text-cash' : 'text-debt'}`}>
                        {s.net >= 0 ? '+' : '-'}{fmt(s.net)}
                      </span>
                    </div>
                    <div className="stat">
                      <span className="stat-label">Budget</span>
                      <span className="stat-value">{fmt(g.budget)}</span>
                    </div>
                  </div>
                )}

                {!s && (
                  <div className="group-budget-preview">
                    <span className="text-muted">Budget: </span>
                    <span style={{ color: g.color, fontWeight: 700 }}>{fmt(g.budget)}/mo</span>
                  </div>
                )}

                <div className="group-actions">
                  <button className="btn-ghost" style={{ fontSize: '0.78rem' }} onClick={() => handleExpand(g.id)}>
                    {isOpen ? '▲ Close' : '▼ Details'}
                  </button>
                  <button className="btn-danger-ghost" onClick={() => handleDelete(g.id, g.name)}>Delete</button>
                </div>
              </div>

              {/* Budget progress */}
              {s && g.budget > 0 && (
                <div className="group-progress">
                  <div className="progress-track">
                    <div className="progress-fill" style={{ width: `${Math.min(budgetUsed, 100)}%`, background: isOver ? 'var(--debt)' : g.color }} />
                  </div>
                  <span className="text-dim">{fmt(s.totalExpenses)} / {fmt(g.budget)}</span>
                  {isOver && <span className="text-danger"> · Over by {fmt(s.totalExpenses - g.budget)}</span>}
                </div>
              )}

              {/* Expanded detail */}
              {isOpen && (
                <div className="group-detail">
                  {/* Keywords */}
                  <div className="kw-section">
                    <div className="section-label">Keyword Rules</div>
                    <div className="kw-list">
                      {g.items.length === 0 && <p className="text-dim">No keywords yet — add some to match transactions.</p>}
                      {g.items.map(item => (
                        <div key={item.id} className="kw-row">
                          <span className={`badge ${item.isIncome ? 'badge-cash' : 'badge-debt'}`}>
                            {item.isIncome ? 'income' : 'expense'}
                          </span>
                          <span className="kw-text">{item.keyword}</span>
                          {item.label && <span className="text-dim">({item.label})</span>}
                          <button className="btn-danger-ghost kw-del" onClick={() => handleRemoveKeyword(g.id, item.id)}>×</button>
                        </div>
                      ))}
                    </div>

                    {/* Add keyword form */}
                    <div className="kw-add-form">
                      <input
                        placeholder="Keyword (e.g. amazon, electric)"
                        value={newKeyword[g.id]?.keyword || ''}
                        onChange={e => setNewKeyword(prev => ({ ...prev, [g.id]: { ...prev[g.id], keyword: e.target.value, isIncome: prev[g.id]?.isIncome ?? false, label: prev[g.id]?.label ?? '' } }))}
                      />
                      <input
                        placeholder="Label (optional)"
                        value={newKeyword[g.id]?.label || ''}
                        onChange={e => setNewKeyword(prev => ({ ...prev, [g.id]: { ...prev[g.id], label: e.target.value, keyword: prev[g.id]?.keyword ?? '', isIncome: prev[g.id]?.isIncome ?? false } }))}
                      />
                      <select
                        value={newKeyword[g.id]?.isIncome ? 'income' : 'expense'}
                        onChange={e => setNewKeyword(prev => ({ ...prev, [g.id]: { ...prev[g.id], isIncome: e.target.value === 'income', keyword: prev[g.id]?.keyword ?? '', label: prev[g.id]?.label ?? '' } }))}
                      >
                        <option value="expense">Expense</option>
                        <option value="income">Income</option>
                      </select>
                      <button className="btn-primary" onClick={() => handleAddKeyword(g.id)}>Add</button>
                    </div>
                  </div>

                  {/* Matched transactions */}
                  {s && (
                    <div className="txn-section">
                      <div className="section-label">Matched Transactions This Month</div>
                      {s.transactions.length === 0 ? (
                        <p className="text-dim">No transactions matched this month</p>
                      ) : (
                        <div className="recent-txns">
                          {s.transactions.slice(0, 20).map(t => (
                            <div key={t.id} className="txn-row">
                              <div className="txn-left">
                                <div className="txn-merchant">{t.merchantName || t.description}</div>
                                <div className="txn-meta">{t.accountName} · {t.institutionName}</div>
                              </div>
                              <div className="txn-right">
                                <div className={`txn-amount ${t.amount < 0 ? 'text-cash' : 'text-debt'}`}>
                                  {t.amount < 0 ? '+' : '-'}{fmt(t.amount)}
                                </div>
                                <div className="txn-date">
                                  {new Date(t.transactionDate).toLocaleDateString('en-US', { month: 'short', day: 'numeric' })}
                                </div>
                              </div>
                            </div>
                          ))}
                        </div>
                      )}
                    </div>
                  )}
                </div>
              )}
            </div>
          );
        })}
      </div>
    </div>
  );
}
