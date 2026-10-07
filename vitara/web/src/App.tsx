import { useEffect, useMemo, useState } from 'react';
import { QueryClientProvider } from '@tanstack/react-query';
import { auth, installSessionExpiryInterceptor, onSessionExpired } from './services/auth';
import type { ProbeResult } from './services/auth';
import { makeModuleQueryClient } from './services/moduleQuery';
import { useTimezone } from './services/timezone';
import VitaraModule from './pages/VitaraModule';
import InsightModule from './pages/InsightModule';
import SignIn from './pages/SignIn';

// Vitara and Insight as ONE app.
//
// Two backends, one place to be: Vitara records what happened (readings, labs, profiles) and
// Insight says what it means. They share one person, one sign-in and one header, which is why
// this is a single page with a switch rather than two apps.
//
// Signing in is Maaya's, unchanged and for now: the same server, the same PIN-on-a-trusted-
// network or username-and-password, the same tokens. Only the screen is new. Replacing that
// with accounts of Vitara's own is a separate decision (docs/VITARA-ACCOUNTS.md); keeping it
// behind this one component is what leaves that decision open.

type Section = 'vitara' | 'insight';
type AuthState = 'probing' | 'pin' | 'login' | 'ready';

const SECTION_KEY = 'vitara.section';

// A link can name the section (#insight is what Maaya's launcher sends); otherwise it is
// whichever one you were last in.
const readSection = (): Section => {
  if (window.location.hash === '#insight') return 'insight';
  if (window.location.hash === '#vitara') return 'vitara';
  try { return localStorage.getItem(SECTION_KEY) === 'insight' ? 'insight' : 'vitara'; } catch { return 'vitara'; }
};

const HEART = (
  <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.9" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
    <path d="M3 13h3l3 7 4-16 3 9h5" />
  </svg>
);

function ProductBar({ section, onPick, onSignOut }: {
  section: Section;
  onPick: (s: Section) => void;
  onSignOut: () => void;
}) {
  return (
    <header className="vx-bar">
      <div className="vx-brand">
        <span className="vx-brand-mark">{HEART}</span>
        <b>Vitara</b>
      </div>

      <nav className="vx-switch" role="tablist" aria-label="Section">
        <button role="tab" aria-selected={section === 'vitara'} onClick={() => onPick('vitara')}>Today</button>
        <button role="tab" aria-selected={section === 'insight'} onClick={() => onPick('insight')}>Insight</button>
      </nav>

      <button className="vx-out" onClick={onSignOut} title="Sign out">
        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.75" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
          <path d="M9 21H5a2 2 0 01-2-2V5a2 2 0 012-2h4" /><polyline points="16 17 21 12 16 7" /><line x1="21" y1="12" x2="9" y2="12" />
        </svg>
        <span>Sign out</span>
      </button>
    </header>
  );
}

// Keeps the browser's idea of "today" in step with the server's. Renders nothing.
function Clock() {
  useTimezone();
  return null;
}

export default function App() {
  const [authState, setAuthState] = useState<AuthState>(auth.isAuthenticated() ? 'ready' : 'probing');
  const [probe, setProbe] = useState<ProbeResult | null>(null);
  const [section, setSection] = useState<Section>(readSection);

  // One client for things that belong to the app rather than to a person. Each section builds
  // its own per-person client inside, so switching people still discards everything.
  const client = useMemo(() => makeModuleQueryClient(60_000), []);

  useEffect(() => {
    if (authState !== 'probing') return;
    auth.probe().then(p => {
      setProbe(p);
      setAuthState(p.trusted && p.method === 'pin' ? 'pin' : 'login');
    });
  }, [authState]);

  // A 401 anywhere (the session expired, or the server restarted while you were away) goes
  // straight back to sign-in rather than leaving every panel looking quietly empty.
  useEffect(() => {
    installSessionExpiryInterceptor();
    onSessionExpired(() => { setProbe(null); setAuthState('probing'); });
  }, []);

  const pick = (s: Section) => {
    setSection(s);
    try { localStorage.setItem(SECTION_KEY, s); } catch { /* it just will not be remembered */ }
    window.scrollTo?.({ top: 0 });
  };

  const signOut = async () => {
    await auth.logout();
    setProbe(null);
    setAuthState('probing');
  };

  if (authState === 'probing') return <div className="vx-boot" aria-busy="true">Vitara</div>;
  if (authState === 'pin' || authState === 'login') {
    return (
      <SignIn
        mode={authState === 'pin' ? 'pin' : 'password'}
        pinLength={probe?.pinLength ?? 4}
        onSignedIn={() => setAuthState('ready')}
      />
    );
  }

  return (
    <QueryClientProvider client={client}>
      <Clock />
      <ProductBar section={section} onPick={pick} onSignOut={signOut} />
      {section === 'vitara' ? <VitaraModule /> : <InsightModule />}
    </QueryClientProvider>
  );
}
