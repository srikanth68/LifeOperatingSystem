import { moduleApi } from './apiHost';

const AUTH_API = `${moduleApi(5000)}/api/auth`;
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
  // Why the server chose that method, or how the probe itself failed. The credentials
  // form is the fallback for every error path, so without this a probe that never
  // arrived is indistinguishable from one that deliberately said "use a password".
  reason?: 'ok' | 'untrusted_network' | 'pin_not_configured' | 'probe_failed' | 'probe_error';
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
      if (!res.ok) {
        console.warn(`[auth] probe returned HTTP ${res.status} — falling back to the password form.`);
        return { trusted: false, method: 'credentials', pinLength: 0, reason: 'probe_failed' };
      }
      const result: ProbeResult = await res.json();
      if (result.method !== 'pin')
        console.warn(`[auth] server chose the password form: ${result.reason ?? 'no reason given'}.`);
      return result;
    } catch (e) {
      // Vault unreachable, nginx not routing /svc/vault, or the page blocked the
      // request. Worth a console line: on screen this is indistinguishable from a
      // deliberate "use your password".
      console.warn('[auth] probe could not reach Vault — falling back to the password form.', e);
      return { trusted: false, method: 'credentials', pinLength: 0, reason: 'probe_error' };
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

  // autoLogin() removed along with the server's POST /api/auth/auto — it logged you in
  // on network trust alone, which behind the nginx proxy meant everyone. Nothing called
  // it: App.tsx probes, then shows the PIN pad or the credentials form.

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

// Auto-logoff on session expiry. Most module pages (San, Vitara, Karma, etc.)
// use their own plain `fetch()` helpers, not authFetchWithRefresh — so a 401
// from an expired/wiped session (e.g. after a backend restart while you were
// away) used to just look like every module going "offline" with no
// explanation, requiring a manual logout+login to fix. Patching window.fetch
// once, globally, means every existing call site gets this for free with no
// per-module changes: any 401 seen while we believe we're logged in clears
// the (now-invalid) session and immediately shows the login/PIN screen again.
let sessionExpiredHandler: (() => void) | null = null;
export function onSessionExpired(handler: () => void) { sessionExpiredHandler = handler; }

let interceptorInstalled = false;
export function installSessionExpiryInterceptor() {
  if (interceptorInstalled) return;
  interceptorInstalled = true;
  const originalFetch = window.fetch.bind(window);
  window.fetch = async (...args: Parameters<typeof fetch>) => {
    const res = await originalFetch(...args);
    if (res.status === 401 && auth.isAuthenticated()) {
      auth.clear();
      sessionExpiredHandler?.();
    }
    return res;
  };
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
