import axios from 'axios';
import type {
  DashboardSummary,
  Transaction,
  SyncStatus,
  CategoryGroup,
  CategoryGroupSummary,
} from '@/types';

const api = axios.create({
  baseURL: import.meta.env.VITE_API_URL || '/api',
  headers: { 'Content-Type': 'application/json' },
});

export const summaryApi = {
  getDashboard: () => api.get<DashboardSummary>('/summary'),
  getMonthlySpendings: (months = 12) =>
    api.get('/summary/monthly-spending', { params: { months } }),
};

export const transactionsApi = {
  getAll: (filters?: { accountId?: string; startDate?: Date; endDate?: Date; category?: string }) =>
    api.get<Transaction[]>('/transactions', {
      params: {
        accountId: filters?.accountId,
        startDate: filters?.startDate?.toISOString().split('T')[0],
        endDate: filters?.endDate?.toISOString().split('T')[0],
        category: filters?.category,
      },
    }),
};

export const syncApi = {
  trigger: () => api.post<SyncStatus>('/sync/trigger'),
  getLatestStatus: () => api.get<SyncStatus>('/sync/status/latest'),
};

export const plaidApi = {
  getItems: () => api.get('/plaid/items'),
  unlinkItem: (id: string) => api.delete(`/plaid/items/${id}`),
};

export const categoryGroupApi = {
  getAll: () => api.get<CategoryGroup[]>('/category-groups'),
  create: (data: { name: string; budget: number; color: string; notes?: string }) =>
    api.post<CategoryGroup>('/category-groups', data),
  update: (id: string, data: { name: string; budget: number; color: string; notes?: string }) =>
    api.put<CategoryGroup>(`/category-groups/${id}`, data),
  delete: (id: string) => api.delete(`/category-groups/${id}`),
  addItem: (id: string, data: { keyword: string; isIncome: boolean; label?: string }) =>
    api.post<CategoryGroup>(`/category-groups/${id}/items`, data),
  removeItem: (id: string, itemId: string) =>
    api.delete(`/category-groups/${id}/items/${itemId}`),
  getSummary: (id: string, month?: number, year?: number) =>
    api.get<CategoryGroupSummary>(`/category-groups/${id}/summary`, { params: { month, year } }),
};

export default api;
