import { useState, useEffect, useRef, useCallback } from 'react';
import type { ModuleId } from '../App';
import '../styles/command-palette.css';

interface Command {
  id: string;
  label: string;
  group: 'Navigate' | 'Actions';
  module: ModuleId | 'home';
  moduleLabel: string;
  color: string;
  keywords: string[];
}

const COMMANDS: Command[] = [
  { id: 'go-home',      label: 'Go to Home',               group: 'Navigate', module: 'home',      moduleLabel: 'Maaya',     color: 'var(--gold)',      keywords: ['home','dashboard','maaya'] },
  { id: 'go-vault',     label: 'Open Vault',               group: 'Navigate', module: 'vault',     moduleLabel: 'Vault',     color: 'var(--vault)',     keywords: ['vault','finance','bank','money','accounts'] },
  { id: 'go-txns',      label: 'View Transactions',        group: 'Navigate', module: 'vault',     moduleLabel: 'Vault',     color: 'var(--vault)',     keywords: ['transactions','spending','history','purchases'] },
  { id: 'go-catbudget', label: 'Category Budget',          group: 'Navigate', module: 'vault',     moduleLabel: 'Vault',     color: 'var(--vault)',     keywords: ['budget','category','categories','recurring'] },
  { id: 'go-vitara',    label: 'Open Vitara',              group: 'Navigate', module: 'vitara',    moduleLabel: 'Vitara',    color: 'var(--vitara)',    keywords: ['vitara','health','wellness','body','sleep','steps'] },
  { id: 'go-nexus',     label: 'Open Nexus',               group: 'Navigate', module: 'nexus',     moduleLabel: 'Nexus',     color: 'var(--nexus)',     keywords: ['nexus','trading','stocks','portfolio','markets'] },
  { id: 'go-aasthi',    label: 'Open Aasthi',              group: 'Navigate', module: 'aasthi',    moduleLabel: 'Aasthi',    color: 'var(--aasthi)',    keywords: ['aasthi','real estate','property','house','rent'] },
  { id: 'go-san',       label: 'Open San',                 group: 'Navigate', module: 'san',       moduleLabel: 'San',       color: 'var(--san)',       keywords: ['san','assistant','ai','reminders','alerts','chat'] },
  { id: 'go-northstar', label: 'Open NorthStar',           group: 'Navigate', module: 'northstar', moduleLabel: 'NorthStar', color: 'var(--northstar)', keywords: ['northstar','knowledge','graph','notes','ideas','learning'] },
  { id: 'go-karma',     label: 'Open Karma',               group: 'Navigate', module: 'karma',     moduleLabel: 'Karma',     color: 'var(--karma)',     keywords: ['karma','goals','habits','journal','progress','growth'] },
  { id: 'go-sutra',     label: 'Open Sutra',               group: 'Navigate', module: 'sutra',     moduleLabel: 'Sutra',     color: 'var(--sutra)',     keywords: ['sutra','documents','files','passport','insurance','contracts'] },
  { id: 'go-settings',  label: 'Open Settings',            group: 'Navigate', module: 'settings',  moduleLabel: 'Settings',  color: 'var(--text3)',     keywords: ['settings','integrations','plaid','notifications','preferences'] },
  { id: 'act-sync',     label: 'Sync Bank Data',           group: 'Actions',  module: 'vault',     moduleLabel: 'Vault',     color: 'var(--vault)',     keywords: ['sync','refresh','plaid','bank','update'] },
  { id: 'act-addtxn',   label: 'Add Transaction',          group: 'Actions',  module: 'vault',     moduleLabel: 'Vault',     color: 'var(--vault)',     keywords: ['add','transaction','expense','income','manual'] },
  { id: 'act-loghealth',label: 'Log Health Entry',         group: 'Actions',  module: 'vitara',    moduleLabel: 'Vitara',    color: 'var(--vitara)',    keywords: ['log','health','sleep','steps','weight','mood'] },
  { id: 'act-addgoal',  label: 'Add a Goal',               group: 'Actions',  module: 'karma',     moduleLabel: 'Karma',     color: 'var(--karma)',     keywords: ['add','goal','target','objective'] },
  { id: 'act-addprop',  label: 'Add Property',             group: 'Actions',  module: 'aasthi',    moduleLabel: 'Aasthi',    color: 'var(--aasthi)',    keywords: ['add','property','house','apartment','real estate'] },
  { id: 'act-uploaddoc',label: 'Upload Document',          group: 'Actions',  module: 'sutra',     moduleLabel: 'Sutra',     color: 'var(--sutra)',     keywords: ['upload','document','file','passport','insurance'] },
];

function match(cmd: Command, q: string): boolean {
  if (!q) return true;
  const lower = q.toLowerCase();
  return (
    cmd.label.toLowerCase().includes(lower) ||
    cmd.moduleLabel.toLowerCase().includes(lower) ||
    cmd.keywords.some(k => k.includes(lower))
  );
}

const MODULE_ICONS: Record<string, React.ReactNode> = {
  home: <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.75" strokeLinecap="round" strokeLinejoin="round"><path d="M3 9l9-7 9 7v11a2 2 0 01-2 2H5a2 2 0 01-2-2z"/><polyline points="9 22 9 12 15 12 15 22"/></svg>,
  vault: <svg viewBox="0 0 24 24" fill="currentColor" stroke="none"><path d="M12 3L22 21H2L12 3z" fill="none" stroke="currentColor" strokeWidth="1.75" strokeLinecap="round" strokeLinejoin="round"/><circle cx="12" cy="14" r="1.2"/><circle cx="9.2" cy="18" r="1"/><circle cx="14.8" cy="18" r="1"/></svg>,
  vitara: <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.75" strokeLinecap="round" strokeLinejoin="round"><path d="M20.84 4.61a5.5 5.5 0 00-7.78 0L12 5.67l-1.06-1.06a5.5 5.5 0 00-7.78 7.78l1.06 1.06L12 21.23l7.78-7.78 1.06-1.06a5.5 5.5 0 000-7.78z"/></svg>,
  nexus: <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.75" strokeLinecap="round"><line x1="7" y1="3" x2="7" y2="7"/><rect x="4" y="7" width="6" height="8" rx="0.5"/><line x1="7" y1="15" x2="7" y2="21"/><line x1="17" y1="5" x2="17" y2="9"/><rect x="14" y="9" width="6" height="6" rx="0.5"/><line x1="17" y1="15" x2="17" y2="19"/></svg>,
  aasthi: <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.75" strokeLinecap="round" strokeLinejoin="round"><rect x="2" y="7" width="9" height="15" rx="0.5"/><rect x="13" y="11" width="9" height="11" rx="0.5"/><path d="M2 7l4.5-4L11 7"/><line x1="1" y1="22" x2="23" y2="22"/></svg>,
  san: <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.75" strokeLinecap="round"><path d="M12 2v3M12 19v3M2 12h3M19 12h3M4.93 4.93l2.12 2.12M16.95 16.95l2.12 2.12M4.93 19.07l2.12-2.12M16.95 7.05l2.12-2.12"/><circle cx="12" cy="12" r="3" fill="currentColor" stroke="none"/></svg>,
  northstar: <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.75" strokeLinecap="round" strokeLinejoin="round"><circle cx="12" cy="12" r="10"/><polygon points="12,4 14,12 12,20 10,12" fill="currentColor" stroke="none"/></svg>,
  karma: <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.75" strokeLinecap="round" strokeLinejoin="round"><path d="M23 4v6h-6"/><path d="M1 20v-6h6"/><path d="M3.51 9a9 9 0 0114.85-3.36L23 10M1 14l4.64 4.36A9 9 0 0020.49 15"/></svg>,
  sutra: <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.75" strokeLinecap="round" strokeLinejoin="round"><path d="M4 19.5A2.5 2.5 0 016.5 17H20"/><path d="M6.5 2H20v20H6.5A2.5 2.5 0 014 19.5v-15A2.5 2.5 0 016.5 2z"/><line x1="9" y1="8" x2="16" y2="8"/><line x1="9" y1="12" x2="16" y2="12"/><line x1="9" y1="16" x2="13" y2="16"/></svg>,
  settings: <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.75" strokeLinecap="round" strokeLinejoin="round"><circle cx="12" cy="12" r="3"/><path d="M19.4 15a1.65 1.65 0 00.33 1.82l.06.06a2 2 0 010 2.83 2 2 0 01-2.83 0l-.06-.06a1.65 1.65 0 00-1.82-.33 1.65 1.65 0 00-1 1.51V21a2 2 0 01-4 0v-.09A1.65 1.65 0 009 19.4a1.65 1.65 0 00-1.82.33l-.06.06a2 2 0 01-2.83-2.83l.06-.06A1.65 1.65 0 004.68 15a1.65 1.65 0 00-1.51-1H3a2 2 0 010-4h.09A1.65 1.65 0 004.6 9a1.65 1.65 0 00-.33-1.82l-.06-.06a2 2 0 012.83-2.83l.06.06A1.65 1.65 0 009 4.68a1.65 1.65 0 001-1.51V3a2 2 0 014 0v.09a1.65 1.65 0 001 1.51 1.65 1.65 0 001.82-.33l.06-.06a2 2 0 012.83 2.83l-.06.06A1.65 1.65 0 0019.4 9a1.65 1.65 0 001.51 1H21a2 2 0 010 4h-.09a1.65 1.65 0 00-1.51 1z"/></svg>,
};

interface Props {
  open: boolean;
  onClose: () => void;
  onNavigate: (m: ModuleId | 'home') => void;
}

export default function CommandPalette({ open, onClose, onNavigate }: Props) {
  const [query, setQuery] = useState('');
  const [selected, setSelected] = useState(0);
  const inputRef = useRef<HTMLInputElement>(null);

  const filtered = COMMANDS.filter(c => match(c, query));
  const groups = ['Navigate', 'Actions'] as const;

  useEffect(() => {
    if (open) {
      setQuery('');
      setSelected(0);
      setTimeout(() => inputRef.current?.focus(), 50);
    }
  }, [open]);

  const execute = useCallback((cmd: Command) => {
    onNavigate(cmd.module);
    onClose();
  }, [onNavigate, onClose]);

  useEffect(() => {
    if (!open) return;
    const handler = (e: KeyboardEvent) => {
      if (e.key === 'Escape') { onClose(); return; }
      if (e.key === 'ArrowDown') { e.preventDefault(); setSelected(s => Math.min(s + 1, filtered.length - 1)); }
      if (e.key === 'ArrowUp')   { e.preventDefault(); setSelected(s => Math.max(s - 1, 0)); }
      if (e.key === 'Enter' && filtered[selected]) { execute(filtered[selected]); }
    };
    window.addEventListener('keydown', handler);
    return () => window.removeEventListener('keydown', handler);
  }, [open, filtered, selected, execute, onClose]);

  if (!open) return null;

  let globalIdx = -1;

  return (
    <div className="cp-overlay" onClick={onClose}>
      <div className="cp-modal" onClick={e => e.stopPropagation()}>
        <div className="cp-input-row">
          <svg className="cp-search-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round">
            <circle cx="11" cy="11" r="8"/><line x1="21" y1="21" x2="16.65" y2="16.65"/>
          </svg>
          <input
            ref={inputRef}
            className="cp-input"
            placeholder="Search modules, pages, actions…"
            value={query}
            onChange={e => { setQuery(e.target.value); setSelected(0); }}
          />
          <span className="cp-hint">ESC</span>
        </div>

        <div className="cp-results">
          {groups.map(group => {
            const items = filtered.filter(c => c.group === group);
            if (!items.length) return null;
            return (
              <div key={group}>
                <div className="cp-group-label">{group}</div>
                {items.map(cmd => {
                  globalIdx++;
                  const idx = globalIdx;
                  return (
                    <button
                      key={cmd.id}
                      className={`cp-item ${selected === idx ? 'selected' : ''}`}
                      style={{ '--item-color': cmd.color } as React.CSSProperties}
                      onClick={() => execute(cmd)}
                      onMouseEnter={() => setSelected(idx)}
                    >
                      <div className="cp-item-icon">{MODULE_ICONS[cmd.module] ?? MODULE_ICONS.settings}</div>
                      <span className="cp-item-label">{cmd.label}</span>
                      <span className="cp-item-module">{cmd.moduleLabel}</span>
                    </button>
                  );
                })}
              </div>
            );
          })}
        </div>

        <div className="cp-footer">
          <div className="cp-footer-hint"><kbd>↑↓</kbd> navigate</div>
          <div className="cp-footer-hint"><kbd>↵</kbd> open</div>
          <div className="cp-footer-hint"><kbd>ESC</kbd> close</div>
          <div className="cp-footer-hint" style={{ marginLeft: 'auto' }}>
            <kbd>⌘K</kbd> / <kbd>Ctrl K</kbd>
          </div>
        </div>
      </div>
    </div>
  );
}
