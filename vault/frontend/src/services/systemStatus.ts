import { useState, useEffect } from 'react';
import { authHeaders } from './auth';
import { moduleApi } from './apiHost';

// Cheap reachability probe per module. Any HTTP response (even 401/503) means the
// module process is up; only a network/timeout error counts as offline.
const MODULES: { id: string; label: string; url: string }[] = [
  { id: 'vault',     label: 'Vault',     url: `${moduleApi(5000)}/api/summary` },
  { id: 'vitara',    label: 'Vitara',    url: `${moduleApi(5100)}/api/oura/status` },
  { id: 'aasthi',    label: 'Aasthi',    url: `${moduleApi(5200)}/api/properties` },
  { id: 'san',       label: 'San',       url: `${moduleApi(5300)}/api/chat/messages` },
  { id: 'sutra',     label: 'Sutra',     url: `${moduleApi(5400)}/api/documents` },
  { id: 'northstar', label: 'NorthStar', url: `${moduleApi(5500)}/api/context` },
  { id: 'karma',     label: 'Karma',     url: `${moduleApi(5600)}/api/habits` },
  { id: 'nexus',     label: 'Nexus',     url: `${moduleApi(5700)}/api/nexus/sentinel/status` },
];

export interface SystemStatus {
  online: number;
  total: number;
  offline: string[]; // labels of unreachable modules
  reachable: Record<string, boolean>; // keyed by module id
  loading: boolean;
}

export function useSystemStatus(pollMs = 30_000): SystemStatus {
  const [status, setStatus] = useState<SystemStatus>({ online: 0, total: MODULES.length, offline: [], reachable: {}, loading: true });

  useEffect(() => {
    let cancelled = false;

    const check = async () => {
      const results = await Promise.all(MODULES.map(async m => {
        try {
          const ctrl = new AbortController();
          const t = setTimeout(() => ctrl.abort(), 4000);
          await fetch(m.url, { headers: authHeaders(), signal: ctrl.signal });
          clearTimeout(t);
          return { id: m.id, label: m.label, ok: true };
        } catch {
          return { id: m.id, label: m.label, ok: false };
        }
      }));
      if (cancelled) return;
      const offline = results.filter(r => !r.ok).map(r => r.label);
      const reachable = Object.fromEntries(results.map(r => [r.id, r.ok]));
      setStatus({ online: results.length - offline.length, total: results.length, offline, reachable, loading: false });
    };

    check();
    const iv = setInterval(check, pollMs);
    return () => { cancelled = true; clearInterval(iv); };
  }, [pollMs]);

  return status;
}
