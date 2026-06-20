import { useState } from 'react';
import Dashboard from './Dashboard';
import Transactions from './Transactions';
import CategoryBudget from './CategoryBudget';
import Settings from './Settings';
import '../styles/modules.css';

type VaultPage = 'dashboard' | 'transactions' | 'category-budget' | 'settings';

const TABS: { id: VaultPage; label: string }[] = [
  { id: 'dashboard',       label: 'Dashboard' },
  { id: 'transactions',    label: 'Transactions' },
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
      {page === 'category-budget' && <CategoryBudget />}
      {page === 'settings'        && <Settings />}
    </div>
  );
}
