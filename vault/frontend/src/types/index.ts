export interface Institution {
  id: string;
  name: string;
  logo?: string;
  website?: string;
}

export interface Account {
  id: string;
  institutionId: string;
  institutionName: string;
  name: string;
  type: string;
  subType: string;
  balance: number;
  availableBalance?: number;
  currency: string;
  isActive: boolean;
}

export interface Transaction {
  id: string;
  accountId: string;
  accountName: string;
  institutionName: string;
  amount: number;
  currency: string;
  transactionDate: string;
  description: string;
  merchantName?: string;
  category?: string;
  isPending: boolean;
}

export interface AccountBalance {
  name: string;
  subType: string;
  balance: number;
}

export interface InstitutionBalance {
  institutionName: string;
  totalBalance: number;
  accounts: AccountBalance[];
}

export interface CategorySpending {
  category: string;
  totalAmount: number;
  transactionCount: number;
}

export interface UpcomingBill {
  merchantName: string;
  lastAmount: number;
  lastDate: string;
  estimatedNextDate: string;
}

export interface DashboardSummary {
  netWorth: number;
  totalCash: number;
  totalDebt: number;
  cashByInstitution: InstitutionBalance[];
  debtByInstitution: InstitutionBalance[];
  spendingByCategory: CategorySpending[];
  recentTransactions: Transaction[];
  upcomingBills: UpcomingBill[];
}

export interface SyncStatus {
  id: string;
  status: string;
  lastSyncTime: string;
  transactionsAdded?: number;
  errorMessage?: string;
}

export interface CategoryGroupItem {
  id: string;
  keyword: string;
  isIncome: boolean;
  label?: string;
}

export interface CategoryGroup {
  id: string;
  name: string;
  budget: number;
  color: string;
  notes?: string;
  items: CategoryGroupItem[];
}

export interface CategoryGroupSummary {
  id: string;
  name: string;
  budget: number;
  color: string;
  totalIncome: number;
  totalExpenses: number;
  net: number;
  transactions: Transaction[];
}
