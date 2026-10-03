import { useState } from 'react';
import Dashboard from './Dashboard';
import Transactions from './Transactions';
import CategoryBudget from './CategoryBudget';
import Recurring from './Recurring';
import Settings from './Settings';
import '../styles/modules.css';

type VaultPage = 'dashboard' | 'transactions' | 'recurring' | 'category-budget' | 'settings';

const TABS: { id: VaultPage; label: string }[] = [
  { id: 'dashboard',       label: 'Dashboard' },
  { id: 'transactions',    label: 'Transactions' },
  { id: 'recurring',       label: 'Subscriptions' },
  { id: 'category-budget', label: 'Category Budget' },
  { id: 'settings',        label: 'Settings' },
];

export default function VaultModule() {
  const [page, setPage] = useState<VaultPage>('dashboard');

  return (
    <div style={{ '--mc': 'var(--vault)' } as React.CSSProperties}>
      <nav className="module-subnav">
        {TABS.map(t => (
          <button
            key={t.id}
            className={`module-tab ${page === t.id ? 'active' : ''}`}
            onClick={() => setPage(t.id)}
          >
            {t.label}
          </button>
        ))}
      </nav>
      {page === 'dashboard'       && <Dashboard />}
      {page === 'transactions'    && <Transactions />}
      {page === 'recurring'       && <Recurring />}
      {page === 'category-budget' && <CategoryBudget />}
      {page === 'settings'        && <Settings />}
    </div>
  );
}
