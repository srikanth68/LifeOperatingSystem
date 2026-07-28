import { useState, useEffect } from 'react';
import MaayaDashboard  from './pages/MaayaDashboard';
import VaultModule     from './pages/VaultModule';
import VitaraModule    from './pages/VitaraModule';
import NexusModule     from './pages/NexusModule';
import AasthiModule    from './pages/AasthiModule';
import SanModule       from './pages/SanModule';
import NorthStarModule from './pages/NorthStarModule';
import KarmaModule     from './pages/KarmaModule';
import SutraModule     from './pages/SutraModule';
import SettingsHub     from './pages/SettingsHub';
import Login           from './pages/Login';
import PinPad          from './pages/PinPad';
import CommandPalette  from './components/CommandPalette';
import ArcReactor      from './components/ArcReactor';
import { CallFab, CallOverlay } from './components/VoiceCallUI';
import { VoiceCallProvider } from './services/voiceCallContext';
import { auth, installSessionExpiryInterceptor, onSessionExpired } from './services/auth';
import type { ProbeResult } from './services/auth';
import { useSystemStatus } from './services/systemStatus';
import './styles/index.css';

export type ModuleId = 'home' | 'vault' | 'vitara' | 'nexus' | 'aasthi' | 'san' | 'northstar' | 'karma' | 'sutra' | 'settings';

/* ── SVG icon library ── */
function Icon({ name }: { name: string }) {
  const p = {
    viewBox: '0 0 24 24', fill: 'none', stroke: 'currentColor',
    strokeWidth: '1.75', strokeLinecap: 'round' as const, strokeLinejoin: 'round' as const,
  };
  switch (name) {
    case 'home':
      return <svg {...p}><path d="M3 9l9-7 9 7v11a2 2 0 01-2 2H5a2 2 0 01-2-2z"/><polyline points="9 22 9 12 15 12 15 22"/></svg>;
    case 'vault':
      return (
        <svg viewBox="0 0 24 24" fill="none" strokeLinecap="round" strokeLinejoin="round">
          <path d="M12 3L22 21H2L12 3z" stroke="currentColor" strokeWidth="1.75"/>
          <circle cx="12" cy="14" r="1.2" fill="currentColor"/>
          <circle cx="9.2" cy="18" r="1" fill="currentColor"/>
          <circle cx="14.8" cy="18" r="1" fill="currentColor"/>
        </svg>
      );
    case 'vitara':
      return <svg {...p}><path d="M20.84 4.61a5.5 5.5 0 00-7.78 0L12 5.67l-1.06-1.06a5.5 5.5 0 00-7.78 7.78l1.06 1.06L12 21.23l7.78-7.78 1.06-1.06a5.5 5.5 0 000-7.78z"/></svg>;
    case 'nexus':
      return (
        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.75" strokeLinecap="round">
          <line x1="7" y1="3" x2="7" y2="7"/><rect x="4" y="7" width="6" height="8" rx="0.5"/>
          <line x1="7" y1="15" x2="7" y2="21"/>
          <line x1="17" y1="5" x2="17" y2="9"/><rect x="14" y="9" width="6" height="6" rx="0.5"/>
          <line x1="17" y1="15" x2="17" y2="19"/>
        </svg>
      );
    case 'aasthi':
      return (
        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.75" strokeLinecap="round" strokeLinejoin="round">
          <rect x="2" y="7" width="9" height="15" rx="0.5"/>
          <rect x="13" y="11" width="9" height="11" rx="0.5"/>
          <path d="M2 7l4.5-4L11 7"/><line x1="1" y1="22" x2="23" y2="22"/>
        </svg>
      );
    case 'san':
      return (
        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.75" strokeLinecap="round">
          <path d="M12 2v3M12 19v3M2 12h3M19 12h3"/>
          <path d="M4.93 4.93l2.12 2.12M16.95 16.95l2.12 2.12M4.93 19.07l2.12-2.12M16.95 7.05l2.12-2.12"/>
          <circle cx="12" cy="12" r="3" fill="currentColor" stroke="none"/>
        </svg>
      );
    case 'northstar':
      return (
        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.75" strokeLinecap="round" strokeLinejoin="round">
          <circle cx="12" cy="12" r="10"/>
          <polygon points="12,4 14,12 12,20 10,12" fill="currentColor" stroke="none"/>
          <polygon points="4,12 12,10 20,12 12,14" fill="currentColor" stroke="none" opacity="0.4"/>
        </svg>
      );
    case 'karma':
      return <svg {...p}><path d="M23 4v6h-6"/><path d="M1 20v-6h6"/><path d="M3.51 9a9 9 0 0114.85-3.36L23 10M1 14l4.64 4.36A9 9 0 0020.49 15"/></svg>;
    case 'sutra':
      return <svg {...p}><path d="M4 19.5A2.5 2.5 0 016.5 17H20"/><path d="M6.5 2H20v20H6.5A2.5 2.5 0 014 19.5v-15A2.5 2.5 0 016.5 2z"/><line x1="9" y1="8" x2="16" y2="8"/><line x1="9" y1="12" x2="16" y2="12"/><line x1="9" y1="16" x2="13" y2="16"/></svg>;
    case 'settings':
      return <svg {...p}><circle cx="12" cy="12" r="3"/><path d="M19.4 15a1.65 1.65 0 00.33 1.82l.06.06a2 2 0 010 2.83 2 2 0 01-2.83 0l-.06-.06a1.65 1.65 0 00-1.82-.33 1.65 1.65 0 00-1 1.51V21a2 2 0 01-4 0v-.09A1.65 1.65 0 009 19.4a1.65 1.65 0 00-1.82.33l-.06.06a2 2 0 01-2.83-2.83l.06-.06A1.65 1.65 0 004.68 15a1.65 1.65 0 00-1.51-1H3a2 2 0 010-4h.09A1.65 1.65 0 004.6 9a1.65 1.65 0 00-.33-1.82l-.06-.06a2 2 0 012.83-2.83l.06.06A1.65 1.65 0 009 4.68a1.65 1.65 0 001-1.51V3a2 2 0 014 0v.09a1.65 1.65 0 001 1.51 1.65 1.65 0 001.82-.33l.06-.06a2 2 0 012.83 2.83l-.06.06A1.65 1.65 0 0019.4 9a1.65 1.65 0 001.51 1H21a2 2 0 010 4h-.09a1.65 1.65 0 00-1.51 1z"/></svg>;
    default: return <svg {...p}><circle cx="12" cy="12" r="10"/></svg>;
  }
}

/* ── Maaya sigil (gold stallion). Falls back to the dharma-wheel mark until
   /sigil.png is present in public/. ── */
function MaayaLogo({ size = 24 }: { size?: number }) {
  const [failed, setFailed] = useState(false);
  if (!failed) {
    return (
      <img
        src="/sigil.png"
        width={size}
        height={size}
        alt="Maaya"
        className="maaya-sigil"
        onError={() => setFailed(true)}
      />
    );
  }
  return (
    <svg width={size} height={size} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeLinecap="round">
      <circle cx="12" cy="12" r="9.5" strokeWidth="1.75"/>
      <line x1="12" y1="2.5" x2="12" y2="21.5" strokeWidth="1.2"/>
      <line x1="16.75" y1="3.77" x2="7.25" y2="20.23" strokeWidth="1.2"/>
      <line x1="7.25" y1="3.77" x2="16.75" y2="20.23" strokeWidth="1.2"/>
      <circle cx="12" cy="12" r="2.5" fill="currentColor" stroke="none"/>
    </svg>
  );
}

const CORE_MODULES: { id: ModuleId; label: string; color: string }[] = [
  { id: 'vault',     label: 'Vault',     color: 'var(--vault)'     },
  { id: 'vitara',    label: 'Vitara',    color: 'var(--vitara)'    },
  { id: 'nexus',     label: 'Nexus',     color: 'var(--nexus)'     },
  { id: 'aasthi',    label: 'Aasthi',    color: 'var(--aasthi)'    },
  { id: 'san',       label: 'San',       color: 'var(--san)'       },
  { id: 'northstar', label: 'NorthStar', color: 'var(--northstar)' },
];

const GROWTH_MODULES: { id: ModuleId; label: string; color: string }[] = [
  { id: 'karma', label: 'Karma', color: 'var(--karma)' },
  { id: 'sutra', label: 'Sutra', color: 'var(--sutra)' },
];

function SystemStatusBadge() {
  const s = useSystemStatus();
  const state = s.loading ? 'loading' : s.offline.length === 0 ? 'ok' : s.online === 0 ? 'down' : 'degraded';
  const value =
    state === 'loading'  ? 'Checking modules…'
    : state === 'ok'     ? 'All Systems Operational'
    : state === 'down'   ? 'All Modules Offline'
    :                      `${s.online}/${s.total} Modules Online`;
  const title = s.offline.length ? `Offline: ${s.offline.join(', ')}` : 'All modules reachable';
  return (
    <div className={`sidebar-status sidebar-status--${state}`} title={title}>
      <span className="sidebar-status-dot"><span className="ping" /><span className="core" /></span>
      <span className="sidebar-status-text">
        <span className="sidebar-status-label">System Status</span>
        <span className="sidebar-status-value">{value}</span>
      </span>
    </div>
  );
}

function SidebarItem({ id, label, color, active, onClick }: {
  id: string; label: string; color: string; active: boolean; onClick: () => void;
}) {
  return (
    <button
      className={`sidebar-item ${active ? 'active' : ''}`}
      style={{ '--mc': color } as React.CSSProperties}
      onClick={onClick}
      title={label}
    >
      <span className="sidebar-icon"><Icon name={id} /></span>
      <span>{label}</span>
    </button>
  );
}

function BootScreen({ message, ready }: { message: string; ready?: boolean }) {
  return (
    <div className="boot-screen">
      <div className={`boot-reactor ${ready ? 'boot-ready' : ''}`}>
        <ArcReactor size={140} />
      </div>
      <p className="boot-text">{message}</p>
      {!ready && <div className="boot-scanline" />}
    </div>
  );
}

function Sidebar({ active, onSelect, onOpenPalette, onLogout }: {
  active: ModuleId;
  onSelect: (m: ModuleId) => void;
  onOpenPalette: () => void;
  onLogout: () => void;
}) {
  return (
    <aside className="sidebar">
      <button className="sidebar-logo" onClick={() => onSelect('home')}>
        <MaayaLogo size={54} />
        <span className="sidebar-logo-text">MAAYA</span>
      </button>

      <div className="sidebar-section">
        <SidebarItem id="home" label="Home" color="var(--gold)" active={active === 'home'} onClick={() => onSelect('home')} />
        <button className="sidebar-item sidebar-search-btn" onClick={onOpenPalette} title="Search (Ctrl+K)">
          <span className="sidebar-icon">
            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.75" strokeLinecap="round">
              <circle cx="11" cy="11" r="8"/><line x1="21" y1="21" x2="16.65" y2="16.65"/>
            </svg>
          </span>
          <span>Search</span>
          <span className="sidebar-search-kbd">⌘K</span>
        </button>
      </div>

      <div className="sidebar-divider"/>

      <div className="sidebar-section">
        <div className="sidebar-section-label">Modules</div>
        {CORE_MODULES.map(m => (
          <SidebarItem key={m.id} id={m.id} label={m.label} color={m.color} active={active === m.id} onClick={() => onSelect(m.id)} />
        ))}
      </div>

      <div className="sidebar-divider"/>

      <div className="sidebar-section">
        <div className="sidebar-section-label">Growth</div>
        {GROWTH_MODULES.map(m => (
          <SidebarItem key={m.id} id={m.id} label={m.label} color={m.color} active={active === m.id} onClick={() => onSelect(m.id)} />
        ))}
      </div>

      <div className="sidebar-bottom">
        <SystemStatusBadge />
        <SidebarItem id="settings" label="Settings" color="var(--text2)" active={active === 'settings'} onClick={() => onSelect('settings')} />
        <button className="sidebar-item sidebar-logout" onClick={onLogout} title="Sign out">
          <span className="sidebar-icon">
            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.75" strokeLinecap="round" strokeLinejoin="round">
              <path d="M9 21H5a2 2 0 01-2-2V5a2 2 0 012-2h4"/><polyline points="16 17 21 12 16 7"/><line x1="21" y1="12" x2="9" y2="12"/>
            </svg>
          </span>
          <span>Sign Out</span>
        </button>
      </div>
    </aside>
  );
}

export default function App() {
  const [active, setActive]         = useState<ModuleId>('home');
  const [palette, setPalette]       = useState(false);
  const [authState, setAuthState]   = useState<'probing' | 'pin' | 'login' | 'greeting' | 'ready'>(
    auth.isAuthenticated() ? 'ready' : 'probing'
  );
  const [probe, setProbe] = useState<ProbeResult | null>(null);

  useEffect(() => {
    if (authState !== 'probing') return;
    auth.probe().then(p => {
      setProbe(p);
      setAuthState(p.trusted && p.method === 'pin' ? 'pin' : 'login');
    });
  }, [authState]);

  // Any module's API call hitting a 401 (session expired/invalidated — e.g.
  // came back after the backend restarted while away) drops straight back to
  // the login/PIN screen instead of every module silently looking "offline."
  useEffect(() => {
    installSessionExpiryInterceptor();
    onSessionExpired(() => {
      setProbe(null);
      setActive('home');
      setAuthState('probing');
    });
  }, []);

  useEffect(() => {
    const handler = (e: KeyboardEvent) => {
      if ((e.metaKey || e.ctrlKey) && e.key === 'k') {
        e.preventDefault();
        setPalette(p => !p);
      }
    };
    window.addEventListener('keydown', handler);
    return () => window.removeEventListener('keydown', handler);
  }, []);

  const greet = () => {
    setAuthState('greeting');
    setTimeout(() => setAuthState('ready'), 2000);
  };

  const handleLogout = async () => {
    await auth.logout();
    setProbe(null);
    setAuthState('probing');
    setActive('home');
  };

  if (authState === 'probing') {
    return <BootScreen message="Initializing..." />;
  }

  if (authState === 'greeting') {
    const hour = new Date().getHours();
    const greeting = hour < 12 ? 'Good morning' : hour < 17 ? 'Good afternoon' : 'Good evening';
    return <BootScreen message={`${greeting}, ${auth.getUsername() ?? 'Sir'}.`} ready />;
  }

  if (authState === 'pin') {
    return <PinPad length={probe?.pinLength ?? 4} onSuccess={greet} />;
  }

  if (authState === 'login') {
    return <Login onLogin={greet} />;
  }

  const navigate = (m: ModuleId | 'home') => setActive(m as ModuleId);

  const renderModule = () => {
    switch (active) {
      case 'home':      return <MaayaDashboard onNavigate={navigate} />;
      case 'vault':     return <VaultModule />;
      case 'vitara':    return <VitaraModule />;
      case 'nexus':     return <NexusModule />;
      case 'aasthi':    return <AasthiModule />;
      case 'san':       return <SanModule />;
      case 'northstar': return <NorthStarModule />;
      case 'karma':     return <KarmaModule />;
      case 'sutra':     return <SutraModule />;
      case 'settings':  return <SettingsHub />;
    }
  };

  return (
    <VoiceCallProvider>
      <div className="app">
        <Sidebar active={active} onSelect={setActive} onOpenPalette={() => setPalette(true)} onLogout={handleLogout} />
        <main className="main">
          <div className="container">{renderModule()}</div>
        </main>
        <CommandPalette open={palette} onClose={() => setPalette(false)} onNavigate={navigate} />
        {/* Outside the per-module switch: reachable from every tab, survives
            switching tabs mid-call. */}
        <CallFab />
        <CallOverlay />
      </div>
    </VoiceCallProvider>
  );
}
