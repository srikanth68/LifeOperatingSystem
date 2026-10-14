import { useCallback, useEffect, useRef, useState } from 'react';
import { setNativeChrome } from './native';

// Motion and theme for the phone app.
//
// One clock drives everything that draws in on a screen (rings, numbers counting up, lines
// being drawn), so they finish together instead of each running on its own. The clock goes
// from 0 to 1 over about a second; every visual takes the slice of it that suits it.

export const clamp = (v: number, lo = 0, hi = 1) => Math.max(lo, Math.min(hi, v));
export const ease = (t: number) => 1 - Math.pow(1 - t, 3);          // ease-out cubic

const reducedMotion = () =>
  typeof window !== 'undefined' && window.matchMedia?.('(prefers-reduced-motion: reduce)').matches === true;

// 0 → 1 once on mount, and again whenever `replayKey` changes. A person who asked their device
// for reduced motion gets the finished state straight away: no count-up, no drawing.
export function useReveal(replayKey: unknown = 0, ms = 1150): number {
  const [t, setT] = useState(() => (reducedMotion() ? 1 : 0));
  const raf = useRef(0);

  useEffect(() => {
    if (reducedMotion()) { setT(1); return; }
    const start = performance.now();
    setT(0);
    const step = (now: number) => {
      const next = Math.min(1, (now - start) / ms);
      setT(next);
      if (next < 1) raf.current = requestAnimationFrame(step);
    };
    raf.current = requestAnimationFrame(step);
    return () => cancelAnimationFrame(raf.current);
  }, [replayKey, ms]);

  return t;
}

// ── Appearance ────────────────────────────────────────────────────────────────

// Two looks (Dusk for dark, Mist for light) and three choices: follow the phone, or pin one.
export type VmTheme = 'dark' | 'light';
export type VmThemePref = 'system' | VmTheme;
const THEME_KEY = 'vitara.mobile.theme';
const CHROME: Record<VmTheme, string> = { dark: '#121a36', light: '#f4f1ea' };   // top of each background

const readPref = (): VmThemePref => {
  try {
    const saved = localStorage.getItem(THEME_KEY);
    if (saved === 'system' || saved === 'dark' || saved === 'light') return saved;
  } catch { /* private mode: fall through to the default */ }
  return 'system';
};

const darkQuery = () =>
  typeof window !== 'undefined' ? window.matchMedia?.('(prefers-color-scheme: dark)') : undefined;

// What the phone itself is set to, kept current: iOS can switch at sunset on its own.
function useSystemTheme(): VmTheme {
  const [dark, setDark] = useState(() => darkQuery()?.matches ?? true);
  useEffect(() => {
    const q = darkQuery();
    if (!q) return;
    const on = (e: MediaQueryListEvent) => setDark(e.matches);
    q.addEventListener('change', on);
    return () => q.removeEventListener('change', on);
  }, []);
  return dark ? 'dark' : 'light';
}

// Following the phone is the default; a pinned choice is remembered on this phone. The browser's
// own chrome (the status bar, the overscroll area behind the page) is painted to match, otherwise
// a light page shows a dark strip at the top. Returns the look in use, the choice, and its setter.
export function useVmTheme(): [VmTheme, VmThemePref, (p: VmThemePref) => void] {
  const [pref, setPrefState] = useState<VmThemePref>(readPref);
  const system = useSystemTheme();
  const theme: VmTheme = pref === 'system' ? system : pref;

  useEffect(() => {
    const meta = document.querySelector('meta[name="theme-color"]');
    const html = document.documentElement;
    const before = { meta: meta?.getAttribute('content') ?? null, bg: html.style.background };
    meta?.setAttribute('content', CHROME[theme]);
    html.style.background = CHROME[theme];
    void setNativeChrome(theme);
    return () => {
      if (before.meta != null) meta?.setAttribute('content', before.meta);
      html.style.background = before.bg;
    };
  }, [theme]);

  const setPref = useCallback((p: VmThemePref) => {
    setPrefState(p);
    try { localStorage.setItem(THEME_KEY, p); } catch { /* it just will not be remembered */ }
  }, []);

  return [theme, pref, setPref];
}
