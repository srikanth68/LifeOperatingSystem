import { useEffect, useRef, useState } from 'react';
import type { ReactNode } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import {
  PersonProvider, PanelBoundary, relTime,
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
import { MobileToday } from './MobileToday';
import { MobileSleep } from './MobileSleep';
import { MobileRecovery } from './MobileRecovery';
import { MobileActivity } from './MobileActivity';
import { MobileLabs } from './MobileLabs';
import { RingDefs } from './Rings';
import { dateLine } from './Parts';
import { useVmTheme } from './motion';
import { useInstall } from './useMobile';
import '../styles/mobile.css';

// Vitara as a phone app.
//
// Five places you go every day sit on the bottom bar (the thumb's reach), everything else is one
// tap away in the More sheet, and the screens themselves are the same ones the desktop app uses:
// the phone gets a shell and a new home screen, not a second copy of the product.

type Tab = 'today' | 'sleep' | 'recovery' | 'move' | 'insight';
type MoreKey = 'body' | 'food' | 'labs' | 'protocols' | 'record' | 'import' | 'all';
// The deep pages behind the three designed screens: reached from a row at the bottom of each.
type DetailKey = 'sleep-detail' | 'recovery-detail' | 'activity-detail' | 'labs-entry';

const TABS: { id: Tab; label: string; title: string; icon: ReactNode }[] = [
  {
    id: 'today', label: 'Today', title: 'Today',
    icon: <svg viewBox="0 0 24 24"><circle cx="12" cy="12" r="8" fill="none" strokeWidth="2" /><circle cx="12" cy="12" r="3" /></svg>,
  },
  {
    id: 'sleep', label: 'Sleep', title: 'Sleep',
    icon: <svg viewBox="0 0 24 24"><path d="M20.500 14.500A8.500 8.500 0 1 1 9.500 3.500a7 7 0 0 0 11 11z" /></svg>,
  },
  {
    id: 'recovery', label: 'Recovery', title: 'Recovery',
    icon: <svg viewBox="0 0 24 24"><rect x="6.500" y="6.500" width="11" height="11" rx="2.500" transform="rotate(45 12 12)" fill="none" strokeWidth="2" /></svg>,
  },
  {
    id: 'move', label: 'Activity', title: 'Activity',
    icon: <svg viewBox="0 0 24 24"><rect x="4" y="12" width="4" height="8" rx="2" /><rect x="10" y="4" width="4" height="16" rx="2" /><rect x="16" y="9" width="4" height="11" rx="2" /></svg>,
  },
  {
    id: 'insight', label: 'Insight', title: 'Insight',
    icon: <svg viewBox="0 0 24 24"><circle cx="12" cy="5.500" r="3" /><circle cx="5.500" cy="18" r="2.800" fill="none" strokeWidth="1.800" /><circle cx="18.500" cy="18" r="2.800" fill="none" strokeWidth="1.800" /></svg>,
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

function Inner({ onSignOut }: { onSignOut: () => void }) {
  const qc = useQueryClient();
  const [tab, setTab] = useState<Tab>('today');
  const [more, setMore] = useState<MoreKey | DetailKey | null>(null);
  const [sheet, setSheet] = useState(false);
  const [tick, setTick] = useState(0);                 // bumped on refresh: the home screen redraws
  const [theme, setTheme] = useVmTheme();
  const install = useInstall();

  const { data: status, isPending } = useQuery<OuraStatus>({
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
  const goMore = (m: MoreKey | DetailKey) => { setMore(m); setSheet(false); window.scrollTo?.({ top: 0 }); };

  const { pull, busy } = usePullToRefresh(async () => {
    await qc.invalidateQueries();
    setTick(n => n + 1);
  });

  const DETAIL_TITLE: Record<DetailKey, string> = { 'sleep-detail': 'Sleep detail', 'recovery-detail': 'Recovery detail', 'activity-detail': 'Activity detail', 'labs-entry': 'Enter a draw' };
  const moreLabel = more ? (MORE.find(m => m.id === more)?.label ?? DETAIL_TITLE[more as DetailKey]) : undefined;
  const title = more ? moreLabel : TABS.find(t => t.id === tab)?.title;
  const onMore = more !== null || sheet;

  // The sync pill: what the sensor last said, in words, with a dot that is hollow when there is
  // nothing to be in sync with.
  const sync = !status ? null
    : !status.linked ? { dot: 'none', label: 'Not linked' }
    : status.expired ? { dot: 'warn', label: 'Reconnect' }
    : { dot: 'ok', label: `Synced ${relTime(status.lastSyncedAt)}` };

  const header = (heading: string, eyebrow: string, big = false) => (
    <header className={`vm-hd ${big ? 'is-big' : ''}`}>
      {more ? <button className="vm-back" onClick={() => setMore(more === 'labs-entry' ? 'labs' : null)} aria-label="Back">‹</button> : null}
      <div className="vm-head-text">
        <p>{eyebrow}</p>
        <h1>{heading}</h1>
      </div>
      <div className="vm-head-right">
        {sync && !more && <span className="vm-pill" title="Last sync"><i className={`dot-${sync.dot}`} />{sync.label}</span>}
        <ProfileMenu />
      </div>
    </header>
  );

  return (
    <div className="hx vm" data-vm={theme}>
      <div className="vm-ptr" style={{ height: busy ? 48 : pull }} aria-hidden="true">
        <svg viewBox="0 0 30 30" className={busy ? 'is-on' : ''} style={{ opacity: busy ? 1 : Math.min(1, pull / 40) }}>
          <circle cx="15" cy="15" r="11" fill="none" strokeWidth="3" className="vm-ptr-track" />
          <circle cx="15" cy="15" r="11" fill="none" strokeWidth="3" strokeLinecap="round" pathLength={1}
                  className="vm-ptr-arc" style={{ strokeDasharray: `${busy ? 0.75 : Math.min(1, pull / 56)} 1` }} />
        </svg>
      </div>

      <main className="vm-main">
        {isPending && <div className="vm-skel" aria-busy="true" />}
                {!isPending && (
          <PanelBoundary key={more ?? tab} name={String(title)}>
            {more === null && tab === 'today' && (
              <MobileToday status={status} tick={tick} go={t => goTab(t)} header={(h, e) => header(h, e, true)} />
            )}
            {more === null && tab === 'sleep' && <MobileSleep tick={tick} header={(h, e) => header(h, e)} detail={() => goMore('sleep-detail')} />}
            {more === null && tab === 'recovery' && <MobileRecovery tick={tick} header={(h, e) => header(h, e)} detail={() => goMore('recovery-detail')} />}
            {more === null && tab === 'move' && <MobileActivity tick={tick} header={(h, e) => header(h, e)} detail={() => goMore('activity-detail')} />}
            {(more !== null || tab === 'insight') && header(String(title), more ? 'Vitara' : dateLine())}
            {more === 'sleep-detail' && <SleepPage />}
            {more === 'recovery-detail' && <ReadinessPage />}
            {more === 'activity-detail' && <ActivityPage />}
            {more === null && tab === 'insight' && <InsightContent />}
            {more === 'labs' && <MobileLabs enter={() => goMore('labs-entry')} />}
            {more === 'labs-entry' && <VitaraLabs />}
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
        {TABS.map(t => {
          const on = more === null && !sheet && tab === t.id;
          return (
            <button key={t.id} className={on ? 'is-on' : ''} onClick={() => goTab(t.id)} aria-current={on ? 'page' : undefined}>
              <i className="vm-tick" aria-hidden="true" />
              {t.icon}
              <span>{t.label}</span>
            </button>
          );
        })}
        <button className={onMore ? 'is-on' : ''} onClick={() => setSheet(true)} aria-haspopup="dialog" aria-expanded={sheet}>
          <i className="vm-tick" aria-hidden="true" />
          <svg viewBox="0 0 24 24"><circle cx="5" cy="12" r="2" /><circle cx="12" cy="12" r="2" /><circle cx="19" cy="12" r="2" /></svg>
          <span>More</span>
        </button>
      </nav>

      <div className={`vm-scrim ${sheet ? 'is-on' : ''}`} onClick={() => setSheet(false)} aria-hidden="true" />
      <div className={`vm-sheet ${sheet ? 'is-on' : ''}`} role="dialog" aria-label="More" aria-hidden={!sheet}>
        <span className="vm-grab" aria-hidden="true" />
        <h2>More</h2>
        <ul>
          {MORE.map(m => (
            <li key={m.id}>
              <button onClick={() => goMore(m.id)} tabIndex={sheet ? 0 : -1}>
                <i className={`vm-shape s-${m.id}`} aria-hidden="true" />
                <span className="vm-sheet-text"><b>{m.label}</b><small>{m.note}</small></span>
                <em aria-hidden="true">›</em>
              </button>
            </li>
          ))}
          <li className="vm-appearance">
            <span>Appearance</span>
            <div role="group" aria-label="Appearance">
              <button className={theme === 'dark' ? 'is-on' : ''} onClick={() => setTheme('dark')} tabIndex={sheet ? 0 : -1}>Dark</button>
              <button className={theme === 'light' ? 'is-on' : ''} onClick={() => setTheme('light')} tabIndex={sheet ? 0 : -1}>Light</button>
            </div>
          </li>
        </ul>
        {!install.standalone && (install.canPrompt || install.ios) && (
          <div className="vm-install">
            {install.canPrompt
              ? <button className="vm-cta" onClick={() => install.prompt()}>Install Vitara on this phone</button>
              : <p>To install: tap <b>Share</b>, then <b>Add to Home Screen</b>.</p>}
          </div>
        )}
        <button className="vm-signout" onClick={onSignOut} tabIndex={sheet ? 0 : -1}>Sign out</button>
      </div>
      <RingDefs />
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
