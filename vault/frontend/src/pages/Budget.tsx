import { useState, useEffect } from 'react';
import { summaryApi } from '@/services/api';
import type { CategorySpending, MonthlySpendings } from '@/types';
import '../styles/budget.css';

export default function Budget() {
  const [categoryBreakdown, setCategoryBreakdown] = useState<CategorySpending[]>([]);
  const [monthlySpendings, setMonthlySpendings] = useState<MonthlySpendings[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [budgets, setBudgets] = useState<Record<string, number>>({});
  const [newBudget, setNewBudget] = useState({ category: '', amount: '' });

  useEffect(() => {
    loadData();
    loadBudgetsFromStorage();
  }, []);

  const loadData = async () => {
    try {
      setLoading(true);
      const [categoryRes, monthlyRes] = await Promise.all([
        summaryApi.getCategoryBreakdown(30),
        summaryApi.getMonthlySpendings(12),
      ]);
      setCategoryBreakdown(categoryRes.data);
      setMonthlySpendings(monthlyRes.data);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load data');
    } finally {
      setLoading(false);
    }
  };

  const loadBudgetsFromStorage = () => {
    const stored = localStorage.getItem('vault-budgets');
    if (stored) {
      setBudgets(JSON.parse(stored));
    }
  };

  const saveBudget = (category: string, amount: number) => {
    const updated = { ...budgets, [category]: amount };
    setBudgets(updated);
    localStorage.setItem('vault-budgets', JSON.stringify(updated));
  };

  const handleAddBudget = () => {
    if (newBudget.category && newBudget.amount) {
      saveBudget(newBudget.category, parseFloat(newBudget.amount));
      setNewBudget({ category: '', amount: '' });
    }
  };

  const getProgress = (spent: number, budget: number) => {
    return Math.min((spent / budget) * 100, 100);
  };

  const isOverBudget = (spent: number, budget: number) => {
    return spent > budget;
  };

  if (loading) return <div className="loading">Loading...</div>;
  if (error) return <div className="error">Error: {error}</div>;

  return (
    <div className="budget">
      <h1>Budget</h1>

      <div className="budget-grid">
        <div className="card">
          <h3>Set Budget</h3>
          <div className="budget-form">
            <input
              type="text"
              placeholder="Category"
              value={newBudget.category}
              onChange={(e) => setNewBudget({ ...newBudget, category: e.target.value })}
            />
            <input
              type="number"
              placeholder="Budget Amount"
              value={newBudget.amount}
              onChange={(e) => setNewBudget({ ...newBudget, amount: e.target.value })}
            />
            <button onClick={handleAddBudget} className="btn-primary">
              Add Budget
            </button>
          </div>
        </div>
      </div>

      <div className="budget-items">
        <h3>Category Budgets</h3>
        {categoryBreakdown.map((category) => {
          const budget = budgets[category.category] || null;
          const isOver = budget && isOverBudget(category.totalAmount, budget);
          const progress = budget ? getProgress(category.totalAmount, budget) : 0;

          return (
            <div key={category.category} className="card budget-item">
              <div className="budget-header">
                <div>
                  <p className="category-name">{category.category}</p>
                  <small className="text-muted">
                    {category.transactionCount} transactions
                  </small>
                </div>
                <p className={`amount ${isOver ? 'over-budget' : ''}`}>
                  ${category.totalAmount.toFixed(2)}
                </p>
              </div>

              {budget && (
                <div className="budget-progress">
                  <div className="progress-bar">
                    <div
                      className={`progress-fill ${isOver ? 'over' : ''}`}
                      style={{ width: `${progress}%` }}
                    ></div>
                  </div>
                  <small className="text-muted">
                    ${category.totalAmount.toFixed(2)} of ${budget.toFixed(2)}
                  </small>
                  {isOver && (
                    <small className="text-danger">
                      Over budget by ${(category.totalAmount - budget).toFixed(2)}
                    </small>
                  )}
                </div>
              )}

              {!budget && (
                <button
                  onClick={() => {
                    setNewBudget({ category: category.category, amount: '' });
                  }}
                  className="btn-small"
                >
                  Set Budget
                </button>
              )}
            </div>
          );
        })}
      </div>

      <div className="card">
        <h3>Monthly Spending Trend</h3>
        <div className="monthly-trend">
          {monthlySpendings.map((month) => (
            <div key={month.month} className="month-item">
              <small>{new Date(month.month).toLocaleDateString('en-US', { month: 'short' })}</small>
              <div className="trend-bar">
                <div className="trend-value">${month.totalSpending.toFixed(0)}</div>
              </div>
            </div>
          ))}
        </div>
      </div>
    </div>
  );
}
