import { useEffect, useState } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { relTime } from '../pages/VitaraModule';
import { appleHealthAvailable, disconnectAppleHealth, readState, syncAppleHealth } from './appleHealth';
import type { AppleHealthState } from './appleHealth';

// Connect and sync Apple Health from Vitara itself. One button; what was read, in words.

const WHAT: Record<string, string> = {
  activity: 'today’s steps', dailyActivity: 'daily steps and calories', heartRate: 'heart rate', sleep: 'last night’s sleep',
  weight: 'weight', workouts: 'workouts', body_fat_pct: 'body fat', lean_mass_kg: 'lean mass',
};

export function MobileAppleHealth() {
  const qc = useQueryClient();
  const [state, setState] = useState<AppleHealthState>(readState);
  const [available, setAvailable] = useState<boolean | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => { void appleHealthAvailable().then(setAvailable); }, []);

  const sync = async () => {
    setBusy(true); setError(null);
    try { setState(await syncAppleHealth()); await qc.invalidateQueries(); }
    catch (e) { setError((e as Error).message); }
    finally { setBusy(false); }
  };

  if (available === false) {
    return (
      <section className="vm-tile vm-msg">
        <b>Only in the iPhone app</b>
        <p>Apple Health can only be read by Vitara’s iPhone app. In a browser, export from the Health app and use Import instead.</p>
      </section>
    );
  }

  const read = state.lastApplied.map(k => WHAT[k] ?? k.replace(/_/g, ' '));

  return (
    <div className="vmi-panel">
      <section className="vm-tile vmi-ah">
        <p className="vmi-eyebrow"><span>Apple Health</span><em>{state.connected ? 'connected' : 'not connected'}</em></p>
        <b className="vmi-ah-big">{state.lastSyncedAt ? `Synced ${relTime(state.lastSyncedAt)}` : 'Not synced yet'}</b>
        {read.length > 0 && <p className="vmi-muted vmi-small">Last sync read {read.join(', ')}.</p>}
        <button type="button" className="vm-cta" disabled={busy} onClick={() => { void sync(); }}>
          {busy ? 'Syncing…' : state.connected ? 'Sync now' : 'Connect Apple Health'}
        </button>
        {error && <p className="vmi-small vmi-coral">{error}</p>}
        <p className="vmi-muted vmi-small">
          {state.connected
            ? 'Syncs by itself when you open Vitara, at most every half hour.'
            : 'Reads steps, workouts, sleep, heart rate and weight. Read-only: Vitara never writes to Health.'}
        </p>
      </section>
      <section className="vm-tile vm-msg">
        <b>Where the phone’s numbers go</b>
        <p>Steps and workouts from Apple Health count over the ring’s when both have the same day. The ring keeps its own scores.</p>
        <p>If you still run the Maaya companion’s health sync, you can turn it off: this replaces it.</p>
      </section>
      {state.connected && (
        <button type="button" className="vm-signout" onClick={() => { disconnectAppleHealth(); setState(readState()); }}>
          Stop syncing
        </button>
      )}
      {state.connected && <p className="vmi-muted vmi-small">To remove Vitara’s access entirely: Settings › Health › Data Access &amp; Devices › Vitara.</p>}
    </div>
  );
}
