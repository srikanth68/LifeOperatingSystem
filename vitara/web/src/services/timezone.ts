import { useQuery } from '@tanstack/react-query';
import { fromZonedTime, toZonedTime, formatInTimeZone } from 'date-fns-tz';
import { moduleApi } from './apiHost';
import { authHeaders } from './auth';

// Falls back to the same zone the servers fall back to (MaayaClock: configured override, then
// the container TZ, then America/New_York), used only until the real answer arrives.
export const DEFAULT_TIMEZONE = 'America/New_York';

// Synchronous fallback cache. The helpers below run outside React (event handlers, mutation
// callbacks) and cannot await a fetch, so useTimezone() keeps this filled in and they read it.
let cachedTz: string = DEFAULT_TIMEZONE;

// The zone comes from the Vitara SERVER, not from a setting in this app. The server's clock is
// what decides which day a night of sleep belongs to; if the browser guessed a different zone
// the two would disagree about what "today" is. Asking the server removes the possibility.
export async function fetchTimezone(): Promise<string> {
  try {
    const res = await fetch(`${moduleApi(5100)}/api/clock`, { headers: authHeaders() });
    if (res.ok) {
      const data = await res.json();
      if (typeof data?.timezone === 'string' && data.timezone) { cachedTz = data.timezone; return cachedTz; }
    }
  } catch { /* server unreachable: keep what we have */ }
  return cachedTz;
}

// React Query hook — call once near the app root so cachedTz is populated
// early; components can also read `.data` directly for the current value.
export function useTimezone() {
  return useQuery({
    queryKey: ['system-timezone'],
    queryFn: fetchTimezone,
    staleTime: 60 * 60_000, // rarely changes
    initialData: cachedTz,
    // initialData counts as fresh for staleTime, so without this the hour-long staleTime
    // meant the configured zone was never fetched at all and everyone got the default.
    // Epoch 0 says the placeholder is ancient, so the real value is fetched on mount.
    initialDataUpdatedAt: 0,
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

// ─────────────────────────────────────────────────────────────────────────────
// Dates and instants are different things, and mixing them up is the bug.
//
//   A CALENDAR DATE   "2026-10-03"                a day, with no time and no zone.
//                                                 A transaction date, a habit day, a draw date.
//   AN INSTANT        "2026-10-04T03:00:00Z"      one moment, which is a different wall-clock
//                                                 time and sometimes a different DAY depending
//                                                 on where you stand. A bedtime, a reminder.
//
// The browser's own formatting treats everything as an instant and renders it in the
// VIEWING DEVICE's zone. That is wrong in both directions:
//
//   - A calendar date must never move. `new Date("2026-10-03")` is midnight UTC, which
//     is 8pm on the 2nd in New York, so toLocaleDateString() prints Oct 2. The same code
//     printed the right day on a laptop in Hyderabad, which is why it survived.
//   - An instant should be shown in the CONFIGURED zone, not whatever the phone, the
//     Everest Mac or this laptop happens to be set to -- so the dashboard reads the same
//     everywhere.
//
// Vault is a worse case: its database layer re-tags every DateTime as UTC, including the
// ones that are really calendar dates, so a transaction date arrives as
// "2026-10-03T00:00:00Z". Those are dates. Use formatDay, not formatInstant.

const DAY_ONLY = /^(\d{4})-(\d{2})-(\d{2})/;
const HAS_ZONE = /(Z|[+-]\d{2}:?\d{2})$/i;

export const getTimezone = (): string => cachedTz;

// The calendar date inside a wire value, ignoring any time or zone attached to it.
export function dayPart(value: string | null | undefined): string | null {
  const m = value ? DAY_ONLY.exec(value) : null;
  return m ? `${m[1]}-${m[2]}-${m[3]}` : null;
}

// A calendar date as the same date everywhere. Built at noon UTC and formatted in UTC, so
// no viewer's zone can push it across midnight in either direction.
export function formatDay(
  value: string | null | undefined,
  options: Intl.DateTimeFormatOptions = { month: 'short', day: 'numeric', year: 'numeric' },
  locale = 'en-US',
): string {
  const d = dayPart(value);
  if (!d) return '—';
  const [y, m, day] = d.split('-').map(Number);
  return new Intl.DateTimeFormat(locale, { ...options, timeZone: 'UTC' })
    .format(new Date(Date.UTC(y, m - 1, day, 12)));
}

// A stored instant as a Date. A value with no zone designator is read as UTC rather than
// as the viewer's local time, which is what `new Date(...)` would do and is wrong for
// anything the backend stored as UTC and lost the Kind on.
export function parseInstant(value: string | Date | null | undefined): Date | null {
  if (!value) return null;
  if (value instanceof Date) return value;
  const iso = /^\d{4}-\d{2}-\d{2}T/.test(value) && !HAS_ZONE.test(value) ? `${value}Z` : value;
  const d = new Date(iso);
  return Number.isNaN(d.getTime()) ? null : d;
}

// An instant, shown in the configured zone.
export function formatInstant(
  value: string | Date | null | undefined,
  options: Intl.DateTimeFormatOptions = { month: 'short', day: 'numeric', hour: 'numeric', minute: '2-digit' },
  tz: string = cachedTz,
  locale = 'en-US',
): string {
  const d = parseInstant(value);
  return d ? new Intl.DateTimeFormat(locale, { ...options, timeZone: tz }).format(d) : '—';
}

export const formatClock = (value: string | Date | null | undefined, tz: string = cachedTz) =>
  formatInstant(value, { hour: 'numeric', minute: '2-digit' }, tz);

// Today's date in the configured zone, as "yyyy-MM-dd".
//
// The replacement for `new Date().toISOString().slice(0, 10)`, which is today in
// LONDON: from 8pm in New York it returns tomorrow, so a form's "today" default and a
// "last 30 days" window both end a day in the future every evening.
export function todayInTz(tz: string = cachedTz): string {
  return dayInTz(new Date(), tz);
}

// The calendar day an instant falls on in the configured zone.
export function dayInTz(instant: Date | string, tz: string = cachedTz): string {
  const d = typeof instant === 'string' ? parseInstant(instant) : instant;
  if (!d) return todayInTz(tz);
  // en-CA formats as yyyy-MM-dd, which is the one locale-stable way to get it without
  // assembling the parts by hand.
  return new Intl.DateTimeFormat('en-CA', { timeZone: tz, year: 'numeric', month: '2-digit', day: '2-digit' }).format(d);
}

// Date arithmetic on a calendar date, with no clock involved: "30 days before 2026-10-03".
// Done in UTC on a noon anchor so a DST change cannot shift the answer by a day.
export function addDays(day: string, n: number): string {
  const [y, m, d] = day.split('-').map(Number);
  const t = new Date(Date.UTC(y, m - 1, d + n, 12));
  return `${t.getUTCFullYear()}-${String(t.getUTCMonth() + 1).padStart(2, '0')}-${String(t.getUTCDate()).padStart(2, '0')}`;
}

// How long ago an instant was, in words. Not zone-dependent, so it is correct anywhere.
export function ago(value: string | Date | null | undefined, now: number = Date.now()): string {
  const d = parseInstant(value);
  if (!d) return '—';
  const mins = Math.round((now - d.getTime()) / 60_000);
  if (mins < 1) return 'just now';
  if (mins < 60) return `${mins}m ago`;
  const hours = Math.round(mins / 60);
  if (hours < 48) return `${hours}h ago`;
  return `${Math.round(hours / 24)}d ago`;
}

// A Date whose LOCAL fields (getHours, getDate, getDay ...) read the wall clock in the
// configured zone. For existing code that asks "what hour is it" or "what day of the
// month" of an instant by calling those getters, which would otherwise answer in the
// viewing device's zone.
//
// It is a reading, not an instant: its getTime() is meaningless. Use it to read fields or
// to format with no timeZone option, never to compare against another Date or send to a
// server.
export function zoned(value: string | Date | null | undefined = new Date(), tz: string = cachedTz): Date {
  return toZonedTime(parseInstant(value) ?? new Date(), tz);
}

export const zonedNow = (tz: string = cachedTz): Date => zoned(new Date(), tz);

// Day of the week for a calendar date, 0 = Sunday. No clock and no zone involved.
export function weekdayOf(day: string): number {
  const [y, m, d] = day.split('-').map(Number);
  return new Date(Date.UTC(y, m - 1, d, 12)).getUTCDay();
}

// Whole calendar days from one date to another. Positive when `to` is later.
//
// Not a count of 24-hour spans between two instants, which is off by one for half of every
// day: "days until the 15th" asked at 9pm is a different number than at 9am if the
// subtraction involves a clock.
export function daysBetween(from: string, to: string): number {
  const at = (d: string) => { const [y, m, day] = d.split('-').map(Number); return Date.UTC(y, m - 1, day, 12); };
  return Math.round((at(to) - at(from)) / 86_400_000);
}

// Calendar days from today, in the configured zone, to a date. 0 is today, negative is past.
export function daysFromToday(value: string | null | undefined, tz: string = cachedTz): number {
  const d = dayPart(value);
  return d ? daysBetween(todayInTz(tz), d) : 0;
}

// The first and last instants of a calendar month in the configured zone, as ISO strings
// for a query. `month` is 0-based, as in Date.
//
// Built from the configured zone rather than `new Date(year, month, 1).toISOString()`,
// which takes the month boundary in the viewing device's zone and so fetches a window that
// is several hours off for anyone looking from somewhere else.
export function monthWindowUtc(year: number, month: number, tz: string = cachedTz): { from: string; to: string } {
  const pad = (n: number) => String(n).padStart(2, '0');
  const last = new Date(Date.UTC(year, month + 1, 0)).getUTCDate();
  return {
    from: fromZonedTime(`${year}-${pad(month + 1)}-01T00:00:00`, tz).toISOString(),
    to: fromZonedTime(`${year}-${pad(month + 1)}-${pad(last)}T23:59:59`, tz).toISOString(),
  };
}
