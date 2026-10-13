import { useMemo } from 'react';
import type { ReactNode } from 'react';
import { useQuery } from '@tanstack/react-query';
import { moduleApi } from '../services/apiHost';
import { vitaraHeaders } from '../services/profile';
import { label as metricLabel, fmtValue } from '../pages/InsightModule';

// What the phone's Insight screens share: fetching, numbers in the catalogue's own units,
// and the "Why?" sheet every card opens.

export const API = moduleApi(5110);
export const VITARA = moduleApi(5100);

export async function get<T>(url: string): Promise<T> {
  const res = await fetch(url, { headers: vitaraHeaders() });
  if (!res.ok) throw new Error(`${res.status} ${res.statusText}`);
  return res.json() as Promise<T>;
}


export interface Split { cut: number | null; low: number; lowN: number | null; high: number; highN: number | null }
export interface Correlation { driver: string; outcome: string; lagDays: number; rho: number; n: number; split?: Split | null }
export interface CatalogueRow { key: string; label: string; unit: string; decimals: number }

// ── Numbers ─────────────────────────────────────────────────────────────────

export function useCatalogue() {
  const q = useQuery({
    queryKey: ['insight-metrics'],
    queryFn: () => get<{ metrics: CatalogueRow[] }>(`${API}/api/health/metrics`),
    staleTime: 10 * 60_000,
  });
  return useMemo(() => new Map((q.data?.metrics ?? []).map(m => [m.key, m])), [q.data]);
}

// "1 h 32" for minutes, "6.5 h" for hours, otherwise the number at the catalogue's precision.
export function num(v: number | null | undefined, unit?: string | null, decimals = 0): string {
  if (v == null || !Number.isFinite(v)) return '—';
  if (unit === 'min') {
    const m = Math.round(Math.abs(v));
    const s = v < 0 ? '−' : '';
    return m >= 60 ? `${s}${Math.floor(m / 60)} h ${String(m % 60).padStart(2, '0')}` : `${s}${m} min`;
  }
  const n = Math.abs(v) >= 1000 ? Math.round(v).toLocaleString('en-US') : v.toFixed(decimals);
  return unit && unit !== 'score' && unit !== 'steps' ? `${n} ${unit}` : n;
}

export function useNamer() {
  const cat = useCatalogue();
  return {
    name: (m: string) => cat.get(m)?.label ?? metricLabel(m),
    fmt: (m: string, v: number | null | undefined, withUnit = true) => {
      const c = cat.get(m);
      if (!c) return v == null ? '—' : fmtValue(m, v);
      return num(v, withUnit ? c.unit : (c.unit === 'min' ? 'min' : null), c.decimals);
    },
  };
}

// ── The Why sheet ───────────────────────────────────────────────────────────

export type Why = { title: string; body: ReactNode };

export function WhySheet({ why, close }: { why: Why | null; close: () => void }) {
  return (
    <>
      <div className={`vm-scrim ${why ? 'is-on' : ''}`} onClick={close} aria-hidden="true" />
      <div className={`vm-sheet vmi-why ${why ? 'is-on' : ''}`} role="dialog" aria-label={why?.title ?? 'Why'} aria-hidden={!why}>
        <span className="vm-grab" aria-hidden="true" />
        <div className="vmi-why-head">
          <h2>{why?.title}</h2>
          <button className="vmi-close" onClick={close} aria-label="Close" tabIndex={why ? 0 : -1}>×</button>
        </div>
        <div className="vmi-why-body">{why?.body}</div>
      </div>
    </>
  );
}

export const WhyLink = ({ onClick, children = 'Why? ›' }: { onClick: () => void; children?: ReactNode }) => (
  <button type="button" className="vmi-link" onClick={onClick}>{children}</button>
);

// A label in the middle of a sentence: "Deep sleep" → "deep sleep", but "HRV" stays "HRV".
export const lc = (s: string) => (s.length > 1 && s[1] === s[1].toLowerCase() ? s[0].toLowerCase() + s.slice(1) : s);

export const f1 = (n: number) => n.toFixed(1);

