import { registerPlugin } from '@capacitor/core';
import { moduleApi } from '../services/apiHost';
import { vitaraHeaders } from '../services/profile';
import { isNative } from './native';

// Apple Health, synced by Vitara's own iOS app (native/vitara-health), replacing the Maaya
// companion's sync. The plugin reads HealthKit and hands back the companion's bundle; this
// posts it to /api/healthkit/sync with the signed-in user's token, so the server files it
// exactly as it filed the companion's (steps and workouts under the phone's source, which
// wins over the ring: Vitara ADR 0025).
//
// It never asks for access on its own. The first sync is the person tapping Connect; after
// that the app syncs when it opens or comes back to the front, at most every half hour.

interface VitaraHealthPlugin {
  isAvailable(): Promise<{ available: boolean }>;
  requestAuthorization(): Promise<{ asked: boolean }>;
  readBundle(opts: { days: number }): Promise<Record<string, unknown>>;
}

const VitaraHealth = registerPlugin<VitaraHealthPlugin>('VitaraHealth');

const KEY = 'vitara.appleHealth';
const EVERY_MS = 30 * 60_000;

export interface AppleHealthState { connected: boolean; lastSyncedAt: string | null; lastApplied: string[] }

export function readState(): AppleHealthState {
  try {
    const v = JSON.parse(localStorage.getItem(KEY) ?? 'null');
    if (v && typeof v.connected === 'boolean') return v;
  } catch { /* fall through */ }
  return { connected: false, lastSyncedAt: null, lastApplied: [] };
}

function writeState(s: AppleHealthState) {
  try { localStorage.setItem(KEY, JSON.stringify(s)); } catch { /* the next open syncs again; harmless */ }
}

export async function appleHealthAvailable(): Promise<boolean> {
  if (!isNative()) return false;
  try { return (await VitaraHealth.isAvailable()).available; } catch { return false; }   // an older build without the plugin
}

// One sync. The first one reads thirty days, so the history fills in; later ones a week,
// which still covers a phone left unopened for a few days.
export async function syncAppleHealth(): Promise<AppleHealthState> {
  const before = readState();
  if (!before.connected) await VitaraHealth.requestAuthorization();

  const bundle = await VitaraHealth.readBundle({ days: before.lastSyncedAt ? 7 : 30 });
  const res = await fetch(`${moduleApi(5100)}/api/healthkit/sync`, {
    method: 'POST',
    headers: { ...vitaraHeaders(), 'Content-Type': 'application/json' },
    body: JSON.stringify(bundle),
  });
  if (!res.ok) throw new Error(res.status === 401 ? 'Signed out: sign in again, then sync.' : `The server said ${res.status}.`);
  const body = await res.json() as { applied?: string[] };

  const after: AppleHealthState = { connected: true, lastSyncedAt: new Date().toISOString(), lastApplied: body.applied ?? [] };
  writeState(after);
  return after;
}

export function disconnectAppleHealth() {
  writeState({ connected: false, lastSyncedAt: null, lastApplied: [] });
}

// On open and on return to the front, if connected and not synced in the last half hour.
// Failures stay quiet here; the Apple Health screen shows the last good sync.
export function startAutoSync(onSynced: () => void): () => void {
  if (!isNative()) return () => {};
  let running = false;
  const maybe = async () => {
    const s = readState();
    if (running || !s.connected || document.visibilityState !== 'visible') return;
    if (s.lastSyncedAt && Date.now() - Date.parse(s.lastSyncedAt) < EVERY_MS) return;
    running = true;
    try { await syncAppleHealth(); onSynced(); } catch { /* next time */ } finally { running = false; }
  };
  void maybe();
  document.addEventListener('visibilitychange', maybe);
  return () => document.removeEventListener('visibilitychange', maybe);
}
