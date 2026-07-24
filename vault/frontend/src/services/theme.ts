// Theme management: dark (default) | light | system.
// The chosen theme is persisted and applied as a data-theme attribute on <html>,
// which CSS variable overrides in index.css key off of.

export type ThemePref = 'dark' | 'light' | 'system';
const KEY = 'maaya_theme';

export function getThemePref(): ThemePref {
  const v = localStorage.getItem(KEY);
  return v === 'light' || v === 'system' ? v : 'dark';
}

function systemPrefersLight(): boolean {
  return window.matchMedia('(prefers-color-scheme: light)').matches;
}

// The concrete theme (dark|light) after resolving "system".
export function resolveTheme(pref: ThemePref): 'dark' | 'light' {
  if (pref === 'system') return systemPrefersLight() ? 'light' : 'dark';
  return pref;
}

export function applyTheme(pref: ThemePref) {
  document.documentElement.setAttribute('data-theme', resolveTheme(pref));
}

export function setThemePref(pref: ThemePref) {
  localStorage.setItem(KEY, pref);
  applyTheme(pref);
}

// Call once at startup. Also keeps "system" in sync with OS changes.
export function initTheme() {
  applyTheme(getThemePref());
  window.matchMedia('(prefers-color-scheme: light)').addEventListener('change', () => {
    if (getThemePref() === 'system') applyTheme('system');
  });
}
