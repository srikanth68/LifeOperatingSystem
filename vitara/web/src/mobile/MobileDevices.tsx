import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { moduleApi } from '../services/apiHost';
import { vitaraHeaders, profileQuery } from '../services/profile';
import { relTime } from '../pages/VitaraModule';
import { MobileAppleHealth } from './MobileAppleHealth';
import { isNative } from './native';

// Where the data comes from: the wearable (Oura or WHOOP) and, in the iPhone app, Apple Health.
// One wearable per person is the expected case (ADR 0029): both write the same nights, so two
// linked at once would count each night twice, and the screen says so.

const VITARA = moduleApi(5100);

interface LinkStatus {
  linked: boolean; healthy?: boolean; expired?: boolean; stale?: boolean;
  linkedAt?: string; lastSyncedAt?: string | null; lastSyncError?: string | null; reason?: string | null;
}

type Wearable = 'oura' | 'whoop';
const NAME: Record<Wearable, string> = { oura: 'Oura Ring', whoop: 'WHOOP' };
const WHAT: Record<Wearable, string> = {
  oura: 'Sleep, readiness, HRV, temperature, activity and workouts.',
  whoop: 'Sleep, recovery, HRV, temperature and workouts. Steps come from Apple Health.',
};

async function json<T>(r: Response): Promise<T> {
  const body = await r.json().catch(() => ({}));
  if (!r.ok) throw new Error((body as { error?: string }).error ?? `The server said ${r.status}.`);
  return body as T;
}

function WearableCard({ w, other }: { w: Wearable; other: LinkStatus | undefined }) {
  const qc = useQueryClient();
  const q = useQuery<LinkStatus>({ queryKey: [`${w}-status`], queryFn: () => fetch(`${VITARA}/api/${w}/status`, { headers: vitaraHeaders() }).then(r => json<LinkStatus>(r)) });
  const [confirm, setConfirm] = useState(false);

  const sync = useMutation({
    mutationFn: () => fetch(`${VITARA}/api/${w}/sync`, { method: 'POST', headers: vitaraHeaders() }).then(r => json(r)),
    onSettled: () => qc.invalidateQueries(),
  });
  const unlink = useMutation({
    mutationFn: () => fetch(`${VITARA}/api/${w}/unlink`, { method: 'DELETE', headers: vitaraHeaders() }),
    onSuccess: () => { setConfirm(false); void qc.invalidateQueries({ queryKey: [`${w}-status`] }); },
  });

  const s = q.data;
  // Linking is a full-page visit to the device maker, which cannot carry a header: the person
  // rides in the URL, and comes back in `state`.
  const linkUrl = `${VITARA}/api/${w}/auth${profileQuery()}`;
  const tone = !s?.linked ? 'none' : s.healthy ? 'ok' : 'warn';
  const otherLinked = other?.linked && s?.linked;

  return (
    <section className="vm-tile vmi-device">
      <div className="vmi-row">
        <b className="vmi-device-name"><i className={`vmi-dev-dot d-${tone}`} />{NAME[w]}</b>
        <span className="vmi-muted vmi-small">
          {!s ? '…' : !s.linked ? 'not linked' : s.lastSyncedAt ? `synced ${relTime(s.lastSyncedAt)}` : 'linked, not synced yet'}
        </span>
      </div>
      <p className="vmi-muted vmi-small">{WHAT[w]}</p>
      {s?.linked && !s.healthy && s.reason && <p className="vmi-small vmi-gold">{s.reason}</p>}
      {otherLinked && w === 'whoop' && (
        <p className="vmi-small vmi-gold">Oura and WHOOP are both linked. Each writes its own night, so sleep would be counted twice: unlink the one you no longer wear.</p>
      )}

      {!s?.linked || s.expired ? (
        <a className="vm-cta" href={linkUrl} target="_blank" rel="noreferrer">{s?.expired ? `Reconnect ${NAME[w]}` : `Link ${NAME[w]}`}</a>
      ) : (
        <div className="vmi-buttons">
          <button type="button" className="vmi-btn is-primary" disabled={sync.isPending} onClick={() => sync.mutate()}>
            {sync.isPending ? 'Syncing…' : 'Sync now'}
          </button>
          {confirm
            ? <button type="button" className="vmi-btn vmi-btn-danger" disabled={unlink.isPending} onClick={() => unlink.mutate()}>Yes, unlink</button>
            : <button type="button" className="vmi-btn" onClick={() => setConfirm(true)}>Unlink</button>}
        </div>
      )}
      {sync.error && <p className="vmi-small vmi-coral">{(sync.error as Error).message}</p>}
      {confirm && <p className="vmi-muted vmi-small">Stops syncing. What was already read stays in Vitara.</p>}
      {!s?.linked && (
        <p className="vmi-muted vmi-small">Opens {NAME[w]} in the browser to approve read-only access, then comes back here.</p>
      )}
    </section>
  );
}

export function MobileDevices() {
  const oura = useQuery<LinkStatus>({ queryKey: ['oura-status-dev'], queryFn: () => fetch(`${VITARA}/api/oura/status`, { headers: vitaraHeaders() }).then(r => json<LinkStatus>(r)) });
  const whoop = useQuery<LinkStatus>({ queryKey: ['whoop-status-dev'], queryFn: () => fetch(`${VITARA}/api/whoop/status`, { headers: vitaraHeaders() }).then(r => json<LinkStatus>(r)) });

  return (
    <div className="vmi-panel">
      <p className="vmi-eyebrow">Wearable</p>
      <WearableCard w="oura" other={whoop.data} />
      <WearableCard w="whoop" other={oura.data} />
      <p className="vmi-muted vmi-small">One wearable per person. Steps and workouts from the phone win over the wearable's when both have them.</p>
      {isNative() && <>
        <p className="vmi-eyebrow" style={{ marginTop: 8 }}>On this iPhone</p>
        <MobileAppleHealth />
      </>}
    </div>
  );
}
