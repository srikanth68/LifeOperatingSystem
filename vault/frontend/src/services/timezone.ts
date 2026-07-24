import { useQuery } from '@tanstack/react-query';
import { fromZonedTime, toZonedTime, formatInTimeZone } from 'date-fns-tz';
import { moduleApi } from './apiHost';
import { authHeaders } from './auth';

// System-wide default — matches the Docker containers' TZ (see root Dockerfile,
// which sets ENV TZ so backend schedulers like Karma's habit reminders agree
// with this). Overridable via the "timezone" fact stored in NorthStar.
export const DEFAULT_TIMEZONE = 'America/New_York';

const northstar = () => moduleApi(5500);

// Synchronous fallback cache — utility functions below run outside React (event
// handlers, mutation callbacks) and can't always await the fetch. useTimezone()
// keeps this populated; until the first fetch resolves, DEFAULT_TIMEZONE is used.
let cachedTz: string = DEFAULT_TIMEZONE;

export async function fetchTimezone(): Promise<string> {
  try {
    const res = await fetch(`${northstar()}/api/facts/timezone`, { headers: authHeaders() });
    if (res.ok) {
      const data = await res.json();
      if (data?.value) { cachedTz = data.value; return cachedTz; }
    }
  } catch { /* NorthStar unreachable — keep the current default */ }
  return cachedTz;
}

export async function setTimezone(tz: string): Promise<void> {
  await fetch(`${northstar()}/api/facts/timezone`, {
    method: 'PUT',
    headers: { ...authHeaders(), 'Content-Type': 'application/json' },
    body: JSON.stringify({ value: tz, source: 'settings' }),
  });
  cachedTz = tz;
}

// React Query hook — call once near the app root so cachedTz is populated
// early; components can also read `.data` directly for the current value.
export function useTimezone() {
  return useQuery({
    queryKey: ['system-timezone'],
    queryFn: fetchTimezone,
    staleTime: 60 * 60_000, // rarely changes
    initialData: cachedTz,
  });
}

// Convert a <input type="datetime-local"> value (no offset — a "wall clock"
// reading in the configured timezone) into a correct UTC ISO string to send
// to the backend. Fixes reminders/alerts meaning a different real-world time
// depending on which device's browser clock happened to create them.
export function localInputToUtcIso(localValue: string, tz: string = cachedTz): string {
  return fromZonedTime(localValue, tz).toISOString();
}

// Convert a UTC ISO string from the backend into a <input type="datetime-local">
// value showing the correct wall-clock time in the configured timezone (for
// pre-filling an edit form).
export function utcIsoToLocalInput(iso: string, tz: string = cachedTz): string {
  const zoned = toZonedTime(iso, tz);
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${zoned.getFullYear()}-${pad(zoned.getMonth() + 1)}-${pad(zoned.getDate())}T${pad(zoned.getHours())}:${pad(zoned.getMinutes())}`;
}

// Display formatting — always renders in the configured timezone, regardless
// of the viewing device's own clock/TZ setting, so the dashboard looks
// consistent whether you're on the Everest Mac, this laptop, or a phone.
export function formatInTz(iso: string | null | undefined, tz: string = cachedTz, formatStr = 'MMM d, h:mm a'): string {
  if (!iso) return '—';
  return formatInTimeZone(new Date(iso), tz, formatStr);
}
