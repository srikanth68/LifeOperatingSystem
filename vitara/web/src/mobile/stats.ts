import { zoned } from '../services/timezone';
import type { Sleep } from '../pages/VitaraModule';

// Small number helpers the phone screens share. Nothing here touches the network or the DOM.

export const avg = (xs: number[]): number | null =>
  xs.length ? xs.reduce((a, b) => a + b, 0) / xs.length : null;

// The q-th quantile (0..1) by linear interpolation, for the "your usual" band: the middle half of
// your own recent values, so one odd night does not stretch it.
export function quantile(xs: number[], q: number): number | null {
  if (!xs.length) return null;
  const s = [...xs].sort((a, b) => a - b);
  const pos = (s.length - 1) * q;
  const lo = Math.floor(pos), hi = Math.ceil(pos);
  return s[lo] + (s[hi] - s[lo]) * (pos - lo);
}

export const fmtMin = (m: number) => {
  const h = Math.floor(m / 60), min = Math.round(m % 60);
  return h > 0 ? `${h}h ${String(min).padStart(2, '0')}m` : `${min}m`;
};

// A nap is not a night: keep the longest session per day, oldest first.
export function oneNightPerDay(sessions: Sleep[]): Sleep[] {
  const best = new Map<string, Sleep>();
  for (const s of sessions) {
    const cur = best.get(s.day);
    if (!cur || s.totalSleepMinutes > cur.totalSleepMinutes) best.set(s.day, s);
  }
  return [...best.values()].sort((a, b) => a.day.localeCompare(b.day));
}

// Minutes after 18:00 on the evening a night began, in the configured zone, so 23:15 is 315 and
// 00:30 is 390 rather than 30. A bedtime either side of midnight then lands on one continuous
// scale instead of wrapping.
export function bedtimeScale(iso: string): number | null {
  const z = zoned(iso);
  if (Number.isNaN(z.getTime())) return null;
  const m = z.getHours() * 60 + z.getMinutes();
  return m >= 18 * 60 ? m - 18 * 60 : m + 6 * 60;
}

export const clockFromScale = (scale: number): string => {
  const m = (Math.round(scale) + 18 * 60) % (24 * 60);
  const h = Math.floor(m / 60), min = m % 60;
  const h12 = h % 12 === 0 ? 12 : h % 12;
  return `${h12}:${String(min).padStart(2, '0')} ${h < 12 ? 'AM' : 'PM'}`;
};
