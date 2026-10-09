import { useCallback, useEffect, useRef, useState } from 'react';

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

export type VmTheme = 'dark' | 'light';
const THEME_KEY = 'vitara.mobile.theme';
const CHROME: Record<VmTheme, string> = { dark: '#060e1c', light: '#f4f7fc' };

const readTheme = (): VmTheme => {
  try {
    const saved = localStorage.getItem(THEME_KEY);
    if (saved === 'dark' || saved === 'light') return saved;
  } catch { /* private mode: fall through to the default */ }
  return 'dark';
};

// Dark is the default; the choice is remembered on this phone. The browser's own chrome (the
// status bar, the overscroll area behind the page) is painted to match, otherwise a light page
// shows a dark strip at the top.
export function useVmTheme(): [VmTheme, (t: VmTheme) => void] {
  const [theme, setThemeState] = useState<VmTheme>(readTheme);

  useEffect(() => {
    const meta = document.querySelector('meta[name="theme-color"]');
    const html = document.documentElement;
    const before = { meta: meta?.getAttribute('content') ?? null, bg: html.style.background };
    meta?.setAttribute('content', CHROME[theme]);
    html.style.background = CHROME[theme];
    return () => {
      if (before.meta != null) meta?.setAttribute('content', before.meta);
      html.style.background = before.bg;
    };
  }, [theme]);

  const setTheme = useCallback((t: VmTheme) => {
    setThemeState(t);
    try { localStorage.setItem(THEME_KEY, t); } catch { /* it just will not be remembered */ }
  }, []);

  return [theme, setTheme];
}
