import { useSyncExternalStore } from 'react';
import { authHeaders } from './auth';
import { moduleApi } from './apiHost';

// Whose health this screen is showing.
//
// Vitara and Insight are one product with two backends, so there is ONE choice of person and
// both read it. It lives here, outside any component, in localStorage, so switching in one
// tab or one module is seen by the other.
//
// "default" is the original profile -- the data that existed before there were profiles. It
// is deliberately NOT sent as a header: a request that names no profile reaches the original
// database, which is exactly what every other client (the phone, the assistant's tools) has
// always done, and means that nothing changes for anyone who never makes a second profile.

export const DEFAULT_PROFILE = 'default';
const KEY = 'vitara.profile';
const VITARA = () => moduleApi(5100);

let current = read();
let version = 0;
const listeners = new Set<() => void>();

function read(): string {
  try { return localStorage.getItem(KEY) || DEFAULT_PROFILE; } catch { return DEFAULT_PROFILE; }
}

function emit() { listeners.forEach(l => l()); }

function subscribe(listener: () => void) {
  listeners.add(listener);
  return () => { listeners.delete(listener); };
}

// Another tab changed the person: follow it, or two open tabs would show different people
// while believing they were the same.
if (typeof window !== 'undefined') {
  window.addEventListener('storage', e => {
    if (e.key === KEY) { current = read(); version++; emit(); }
  });
}

export const getProfileId = () => current;

export function setProfileId(id: string) {
  current = id || DEFAULT_PROFILE;
  try { localStorage.setItem(KEY, current); } catch { /* private mode: still works for this tab */ }
  version++;
  emit();
}

// The same person, but their data changed underneath the screen (a profile was edited).
export function refreshProfile() { version++; emit(); }

export const useProfileId = () => useSyncExternalStore(subscribe, getProfileId, getProfileId);

// Changes whenever the person OR their profile changes. Modules key their whole tree on this,
// so a switch discards every cached query and every piece of local state: a screen must never
// show one person's numbers under another person's name for the length of a refetch.
export const useProfileKey = () =>
  useSyncExternalStore(subscribe, () => `${current}:${version}`, () => `${current}:${version}`);

export function profileHeaders(id: string = current): Record<string, string> {
  return id === DEFAULT_PROFILE ? {} : { 'X-Profile-Id': id };
}

// Everything the Vitara and Insight backends need to know about who is asking and for whom.
export const vitaraHeaders = (): Record<string, string> => ({ ...authHeaders(), ...profileHeaders() });

// For the one request a browser makes by NAVIGATING -- linking an Oura ring opens a new tab,
// and a navigation cannot carry a header -- so the person rides in the URL instead.
export const profileQuery = (): string =>
  current === DEFAULT_PROFILE ? '' : `?profile=${encodeURIComponent(current)}`;

// ── The people ───────────────────────────────────────────────────────────────

export interface Person { id: string; name: string | null; isDefault: boolean }

export interface MyProfile {
  synced: boolean;
  id: string;
  name?: string | null;
  age?: number | null;
  dateOfBirth?: string | null;
  heightCm?: number | null;
  biologicalSex?: string | null;
  sources?: { height: 'you' | 'sync' | null; sex: 'you' | 'sync' | null };
}

export interface ProfileInput {
  name: string | null;
  biologicalSex: string | null;
  dateOfBirth: string | null;
  heightCm: number | null;
}

export const personLabel = (p: Pick<Person, 'name' | 'isDefault'>) =>
  p.name?.trim() || (p.isDefault ? 'Me' : 'Unnamed');

async function call<T>(path: string, init?: RequestInit, as?: string): Promise<T> {
  const res = await fetch(`${VITARA()}${path}`, {
    ...init,
    headers: {
      ...authHeaders(),
      ...(as === undefined ? profileHeaders() : profileHeaders(as)),
      ...(init?.body ? { 'Content-Type': 'application/json' } : {}),
    },
  });

  if (res.status === 204) return undefined as T;

  const body = await res.json().catch(() => null);
  // The server's own sentence is the useful one: "Height should be between 100 and 250 cm"
  // beats "400".
  if (!res.ok) throw new Error(body?.error ?? `${res.status}`);

  // A 200 that is not JSON is not success. A proxy that does not recognise the route answers
  // with the app's own index.html and a cheerful 200, and trusting that is how a misrouted
  // request turned into a crash on the first render instead of an error somebody could read.
  if (body === null) throw new Error('The server answered, but not with data. Is the Vitara service reachable?');

  return body as T;
}

export const listPeople = () => call<Person[]>('/api/profiles');

export const getProfile = (id: string) => call<MyProfile>('/api/profile', undefined, id);

export const saveProfile = (id: string, input: ProfileInput) =>
  call<MyProfile>('/api/profile', { method: 'PUT', body: JSON.stringify(input) }, id);

export const createPerson = (input: ProfileInput) =>
  call<Person>('/api/profiles', { method: 'POST', body: JSON.stringify(input) });

// Irreversible, and the server insists the id is repeated, so it cannot happen by accident.
export const deletePerson = (id: string) =>
  call<void>(`/api/profiles/${encodeURIComponent(id)}?confirm=${encodeURIComponent(id)}`, { method: 'DELETE' });

// ── Height, in the unit a person actually thinks in ──────────────────────────

export const ftInToCm = (ft: number, inch: number) => Math.round(((ft * 12 + inch) * 2.54) * 10) / 10;

export function cmToFtIn(cm: number): { ft: number; inch: number } {
  const total = cm / 2.54;
  let ft = Math.floor(total / 12);
  let inch = Math.round((total - ft * 12) * 10) / 10;
  if (inch >= 12) { ft += 1; inch = 0; }          // 5'11.96" is 6'0", not 5'12"
  return { ft, inch };
}
