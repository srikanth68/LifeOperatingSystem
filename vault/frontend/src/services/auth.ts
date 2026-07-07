const AUTH_API = 'http://localhost:5000/api/auth';
const TOKEN_KEY = 'maaya_access_token';
const REFRESH_KEY = 'maaya_refresh_token';
const USER_KEY = 'maaya_username';

export interface AuthTokens {
  accessToken: string;
  refreshToken: string;
  expiresIn: number;
  username: string;
}

export interface ProbeResult {
  trusted: boolean;
  method: 'pin' | 'credentials';
  pinLength: number;
}

export const auth = {
  getToken: () => localStorage.getItem(TOKEN_KEY),
  getRefreshToken: () => localStorage.getItem(REFRESH_KEY),
  getUsername: () => localStorage.getItem(USER_KEY),
  isAuthenticated: () => !!localStorage.getItem(TOKEN_KEY),

  save(tokens: AuthTokens) {
    localStorage.setItem(TOKEN_KEY, tokens.accessToken);
    localStorage.setItem(REFRESH_KEY, tokens.refreshToken);
    localStorage.setItem(USER_KEY, tokens.username);
  },

  clear() {
    localStorage.removeItem(TOKEN_KEY);
    localStorage.removeItem(REFRESH_KEY);
    localStorage.removeItem(USER_KEY);
  },

  async login(username: string, password: string): Promise<AuthTokens> {
    const res = await fetch(`${AUTH_API}/login`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ username, password }),
    });
    if (!res.ok) {
      const err = await res.json().catch(() => ({ error: 'Login failed' }));
      throw new Error(err.error || 'Login failed');
    }
    const tokens: AuthTokens = await res.json();
    auth.save(tokens);
    return tokens;
  },

  async refresh(): Promise<AuthTokens | null> {
    const refreshToken = auth.getRefreshToken();
    if (!refreshToken) return null;

    try {
      const res = await fetch(`${AUTH_API}/refresh`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ refreshToken }),
      });
      if (!res.ok) {
        auth.clear();
        return null;
      }
      const tokens: AuthTokens = await res.json();
      auth.save(tokens);
      return tokens;
    } catch {
      auth.clear();
      return null;
    }
  },

  async probe(): Promise<ProbeResult> {
    try {
      const res = await fetch(`${AUTH_API}/probe`);
      if (!res.ok) return { trusted: false, method: 'credentials', pinLength: 0 };
      return await res.json();
    } catch {
      return { trusted: false, method: 'credentials', pinLength: 0 };
    }
  },

  async pinLogin(pin: string): Promise<AuthTokens> {
    const res = await fetch(`${AUTH_API}/pin`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ pin }),
    });
    if (!res.ok) {
      const err = await res.json().catch(() => ({ error: 'PIN rejected' }));
      throw new Error(err.error || 'PIN rejected');
    }
    const tokens: AuthTokens = await res.json();
    auth.save(tokens);
    return tokens;
  },

  async autoLogin(): Promise<AuthTokens | null> {
    try {
      const res = await fetch(`${AUTH_API}/auto`, { method: 'POST' });
      if (!res.ok) return null;
      const tokens: AuthTokens = await res.json();
      auth.save(tokens);
      return tokens;
    } catch {
      return null;
    }
  },

  async logout() {
    const token = auth.getToken();
    const refreshToken = auth.getRefreshToken();
    try {
      await fetch(`${AUTH_API}/logout`, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          ...(token ? { Authorization: `Bearer ${token}` } : {}),
        },
        body: JSON.stringify({ refreshToken }),
      });
    } catch { /* best-effort */ }
    auth.clear();
  },
};

export function authHeaders(): Record<string, string> {
  const token = auth.getToken();
  return token ? { Authorization: `Bearer ${token}` } : {};
}

export function authFetch(input: RequestInfo | URL, init?: RequestInit): Promise<Response> {
  const token = auth.getToken();
  const headers = new Headers(init?.headers);
  if (token) headers.set('Authorization', `Bearer ${token}`);
  return fetch(input, { ...init, headers });
}

let refreshPromise: Promise<AuthTokens | null> | null = null;

export async function authFetchWithRefresh(input: RequestInfo | URL, init?: RequestInit): Promise<Response> {
  let res = await authFetch(input, init);

  if (res.status === 401) {
    if (!refreshPromise) {
      refreshPromise = auth.refresh().finally(() => { refreshPromise = null; });
    }
    const tokens = await refreshPromise;
    if (tokens) {
      res = await authFetch(input, init);
    }
  }

  return res;
}
