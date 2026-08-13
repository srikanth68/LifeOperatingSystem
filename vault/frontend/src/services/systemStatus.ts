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

// ONE poller for the whole app, shared by every consumer.
//
// This used to be a plain hook, which meant each component calling it got its own
// useState and its own interval — two consumers, two independent probe loops on
// separate timers hitting all eight modules. They disagreed constantly: the sidebar
// badge said "All Systems Operational" while the dashboard listed modules as
// offline, because the two had polled at different moments and one had timed out.
// It also doubled the request rate, which made those timeouts more likely.
//
// Now a single loop runs while at least one component is mounted, and every consumer
// renders the same answer.

let shared: SystemStatus = { online: 0, total: MODULES.length, offline: [], reachable: {}, loading: true };
const subscribers = new Set<(s: SystemStatus) => void>();
let timer: ReturnType<typeof setInterval> | null = null;
let inFlight = false;

async function probe() {
  // A slow round can outlast the interval; overlapping runs would double the load
  // for no extra freshness.
  if (inFlight) return;
  inFlight = true;
  try {
    const results = await Promise.all(MODULES.map(async m => {
      try {
        const ctrl = new AbortController();
        const t = setTimeout(() => ctrl.abort(), 8000);
        await fetch(m.url, { headers: authHeaders(), signal: ctrl.signal });
        clearTimeout(t);
        return { id: m.id, label: m.label, ok: true };
      } catch {
        return { id: m.id, label: m.label, ok: false };
      }
    }));
    const offline = results.filter(r => !r.ok).map(r => r.label);
    shared = {
      online: results.length - offline.length,
      total: results.length,
      offline,
      reachable: Object.fromEntries(results.map(r => [r.id, r.ok])),
      loading: false,
    };
    subscribers.forEach(fn => fn(shared));
  } finally {
    inFlight = false;
  }
}

export function useSystemStatus(pollMs = 30_000): SystemStatus {
  const [status, setStatus] = useState<SystemStatus>(shared);

  useEffect(() => {
    subscribers.add(setStatus);
    // Late mounters get the last known answer immediately rather than flashing
    // "checking" or, worse, "offline" until the next tick.
    setStatus(shared);

    if (timer === null) {
      probe();
      timer = setInterval(probe, pollMs);
    }

    return () => {
      subscribers.delete(setStatus);
      if (subscribers.size === 0 && timer !== null) {
        clearInterval(timer);
        timer = null;
      }
    };
  }, [pollMs]);

  return status;
}
