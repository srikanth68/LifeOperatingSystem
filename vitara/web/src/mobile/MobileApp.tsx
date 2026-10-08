import { useEffect, useRef, useState } from 'react';
import type { ReactNode } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import {
  PersonProvider, PanelBoundary, BackendDown, relTime,
  SleepPage, ReadinessPage, ActivityPage, BodyPage, NutritionPage, ProtocolsPage,
  MeasurePanel, XmlImportPanel, ImportPanel,
} from '../pages/VitaraModule';
import type { OuraStatus } from '../pages/VitaraModule';
import { InsightContent } from '../pages/InsightModule';
import { VitaraLabs } from '../components/VitaraLabs';
import { VitaraMetricsCatalogue } from '../components/VitaraMetricsCatalogue';
import { ProfileMenu } from '../components/health/ProfileMenu';
import { moduleApi } from '../services/apiHost';
import { vitaraHeaders } from '../services/profile';
import { todayInTz } from '../services/timezone';
import { MobileToday } from './MobileToday';
import { useInstall } from './useMobile';
import '../styles/mobile.css';

// Vitara as a phone app.
//
// Five places you go every day sit on the bottom bar (the thumb's reach), everything else is one
// tap away in the More sheet, and the screens themselves are the same ones the desktop app uses:
// the phone gets a shell and a new home screen, not a second copy of the product.

type Tab = 'today' | 'sleep' | 'recovery' | 'move' | 'insight';
type MoreKey = 'body' | 'food' | 'labs' | 'protocols' | 'record' | 'import' | 'all';

const TABS: { id: Tab; label: string; title: string; icon: ReactNode }[] = [
  {
    id: 'today', label: 'Today', title: 'Today',
    icon: <svg viewBox="0 0 24 24"><circle cx="12" cy="12" r="8.5" /><circle cx="12" cy="12" r="4.5" /></svg>,
  },
  {
    id: 'sleep', label: 'Sleep', title: 'Sleep',
    icon: <svg viewBox="0 0 24 24"><path d="M20 14.5A8.5 8.5 0 1 1 9.5 4a6.8 6.8 0 0 0 10.5 10.5z" /></svg>,
  },
  {
    id: 'recovery', label: 'Recovery', title: 'Recovery',
    icon: <svg viewBox="0 0 24 24"><path d="M12 21c-5 0-8-3.2-8-7.5C4 8 9 4 12 3c3 1 8 5 8 10.500C20 17.800 17 21 12 21z" /><path d="M12 8v9M8.500 13.500 12 17l3.500-3.500" /></svg>,
  },
  {
    id: 'move', label: 'Activity', title: 'Activity',
    icon: <svg viewBox="0 0 24 24"><path d="M3 13h3l3 7 4-16 3 9h5" /></svg>,
  },
  {
    id: 'insight', label: 'Insight', title: 'Insight',
    icon: <svg viewBox="0 0 24 24"><circle cx="5.500" cy="17.500" r="2.500" /><circle cx="18.500" cy="6.500" r="2.500" /><circle cx="18" cy="18" r="2" /><path d="M7.700 16.200 16.300 8M8 17.700l8 .2" /></svg>,
  },
];

const MORE: { id: MoreKey; label: string; note: string }[] = [
  { id: 'labs', label: 'Labs', note: 'Blood work against reference ranges' },
  { id: 'body', label: 'Body', note: 'Weight, composition, biological age' },
  { id: 'food', label: 'Food', note: 'Meals and macros' },
  { id: 'protocols', label: 'Protocols', note: 'What you are working on' },
  { id: 'record', label: 'Record a reading', note: 'Blood pressure, glucose, waist and more' },
  { id: 'import', label: 'Import', note: 'Apple Health and CSV files' },
  { id: 'all', label: 'Everything we track', note: 'Every measure, and how it is read' },
];

const dateLine = () => {
  const d = new Date(todayInTz() + 'T12:00:00');
  return d.toLocaleDateString('en-US', { weekday: 'long', month: 'long', day: 'numeric' });
};

function Inner({ onSignOut }: { onSignOut: () => void }) {
  const qc = useQueryClient();
  const [tab, setTab] = useState<Tab>('today');
  const [more, setMore] = useState<MoreKey | null>(null);
  const [sheet, setSheet] = useState(false);
  const install = useInstall();

  const { data: status, isPending, isError } = useQuery<OuraStatus>({
    queryKey: ['oura-status'],
    queryFn: () => fetch(`${moduleApi(5100)}/api/oura/status`, { headers: vitaraHeaders() }).then(r => {
      if (!r.ok) throw new Error(String(r.status));
      return r.json();
    }),
  });

  const goTab = (t: Tab) => {
    setMore(null); setTab(t); setSheet(false);
    window.scrollTo?.({ top: 0 });
    navigator.vibrate?.(6);                       // a light tick where the device has one
  };
  const goMore = (m: MoreKey) => { setMore(m); setSheet(false); window.scrollTo?.({ top: 0 }); };

  const { pull, busy } = usePullToRefresh(() => qc.invalidateQueries());

  const moreLabel = MORE.find(m => m.id === more)?.label;
  const title = more ? moreLabel : TABS.find(t => t.id === tab)?.title;

  return (
    <div className="hx vm">
      <div className="vm-ptr" style={{ height: busy ? 44 : pull }} aria-hidden="true">
        <span className={`vm-spin ${busy ? 'is-on' : ''}`} style={{ opacity: busy ? 1 : Math.min(1, pull / 56) }} />
      </div>

      <header className="vm-top">
        {more ? (
          <button className="vm-back" onClick={() => setMore(null)} aria-label="Back">‹</button>
        ) : null}
        <div className="vm-title">
          <h1>{title}</h1>
          <p>{more ? 'Vitara' : dateLine()}</p>
        </div>
        <div className="vm-top-right">
          {status?.linked && !status.expired && (
            <span className="vm-sync" title="Last sync"><span className="hx-dot" />{relTime(status.lastSyncedAt)}</span>
          )}
          <ProfileMenu />
          <button className="vm-more" onClick={() => setSheet(true)} aria-label="More">
            <svg viewBox="0 0 24 24"><circle cx="5" cy="12" r="1.600" /><circle cx="12" cy="12" r="1.600" /><circle cx="19" cy="12" r="1.600" /></svg>
          </button>
        </div>
      </header>

      <main className="vm-main">
        {isPending && <div className="vm-skel" aria-busy="true" />}
        {!isPending && isError && <BackendDown />}
        {!isPending && !isError && (
          <PanelBoundary key={more ?? tab} name={String(title)}>
            {more === null && tab === 'today' && <MobileToday status={status} go={t => goTab(t)} />}
            {more === null && tab === 'sleep' && <SleepPage />}
            {more === null && tab === 'recovery' && <ReadinessPage />}
            {more === null && tab === 'move' && <ActivityPage />}
            {more === null && tab === 'insight' && <InsightContent />}
            {more === 'labs' && <VitaraLabs />}
            {more === 'body' && <BodyPage />}
            {more === 'food' && <NutritionPage />}
            {more === 'protocols' && <ProtocolsPage />}
            {more === 'record' && <MeasurePanel />}
            {more === 'import' && <><XmlImportPanel /><ImportPanel /></>}
            {more === 'all' && <VitaraMetricsCatalogue />}
          </PanelBoundary>
        )}
      </main>

      <nav className="vm-nav" aria-label="Sections">
        {TABS.map(t => (
          <button key={t.id} className={more === null && tab === t.id ? 'is-on' : ''} onClick={() => goTab(t.id)}
                  aria-current={more === null && tab === t.id ? 'page' : undefined}>
            {t.icon}
            <span>{t.label}</span>
          </button>
        ))}
      </nav>

      {sheet && (
        <div className="vm-sheet-wrap" onClick={() => setSheet(false)}>
          <div className="vm-sheet" role="dialog" aria-label="More" onClick={e => e.stopPropagation()}>
            <span className="vm-grab" aria-hidden="true" />
            <ul>
              {MORE.map(m => (
                <li key={m.id}>
                  <button onClick={() => goMore(m.id)}>
                    <b>{m.label}</b><span>{m.note}</span><i aria-hidden="true">›</i>
                  </button>
                </li>
              ))}
            </ul>
            {!install.standalone && (install.canPrompt || install.ios) && (
              <div className="vm-install">
                {install.canPrompt
                  ? <button className="hx-btn" onClick={() => install.prompt()}>Install Vitara on this phone</button>
                  : <p>To install: tap <b>Share</b>, then <b>Add to Home Screen</b>.</p>}
              </div>
            )}
            <button className="vm-signout" onClick={onSignOut}>Sign out</button>
          </div>
        </div>
      )}
    </div>
  );
}

// Pull down from the top to refresh everything. Touch only, and only when already at the top, so
// it never fights ordinary scrolling.
function usePullToRefresh(onRefresh: () => Promise<unknown>) {
  const [pull, setPull] = useState(0);
  const [busy, setBusy] = useState(false);
  const pullRef = useRef(0);
  const busyRef = useRef(false);
  // The latest callback without re-binding the touch listeners on every render.
  const refresh = useRef(onRefresh);
  refresh.current = onRefresh;

  useEffect(() => {
    let startY = 0;
    let active = false;

    const start = (e: TouchEvent) => {
      if (window.scrollY <= 0 && !busyRef.current) { startY = e.touches[0].clientY; active = true; }
    };
    const move = (e: TouchEvent) => {
      if (!active) return;
      const dy = e.touches[0].clientY - startY;
      if (dy > 0 && window.scrollY <= 0) { pullRef.current = Math.min(88, dy * 0.5); setPull(pullRef.current); }
      else { pullRef.current = 0; setPull(0); }
    };
    const end = async () => {
      if (!active) return;
      active = false;
      const go = pullRef.current >= 56;
      pullRef.current = 0; setPull(0);
      if (go) {
        busyRef.current = true; setBusy(true);
        try { await refresh.current(); } finally { busyRef.current = false; setBusy(false); }
      }
    };

    window.addEventListener('touchstart', start, { passive: true });
    window.addEventListener('touchmove', move, { passive: true });
    window.addEventListener('touchend', end);
    return () => {
      window.removeEventListener('touchstart', start);
      window.removeEventListener('touchmove', move);
      window.removeEventListener('touchend', end);
    };
  }, []);

  return { pull, busy };
}

export default function MobileApp({ onSignOut }: { onSignOut: () => void }) {
  return <PersonProvider><Inner onSignOut={onSignOut} /></PersonProvider>;
}
