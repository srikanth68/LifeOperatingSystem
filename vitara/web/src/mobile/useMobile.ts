import { useEffect, useState } from 'react';

// Is this a phone-shaped screen?
//
// Decided by width, not by guessing the device: a narrow desktop window gets the phone layout and
// a tablet gets the desktop one, which is what each of them can actually fit. Two escape hatches
// for the person who disagrees: ?app=1 forces the phone shell (handy for trying it on a laptop)
// and ?desktop=1 forces the full layout on a phone.
const QUERY = '(max-width: 720px)';

const forced = (): boolean | null => {
  const q = new URLSearchParams(window.location.search);
  if (q.has('desktop')) return false;
  if (q.has('app')) return true;
  return null;
};

export function useIsMobile(): boolean {
  const [mobile, setMobile] = useState(() => forced() ?? window.matchMedia(QUERY).matches);

  useEffect(() => {
    const f = forced();
    if (f !== null) { setMobile(f); return; }
    const mq = window.matchMedia(QUERY);
    const on = () => setMobile(mq.matches);
    on();
    mq.addEventListener('change', on);
    return () => mq.removeEventListener('change', on);
  }, []);

  return mobile;
}

// Installed to the home screen? Then there is no browser chrome and the app is on its own.
export function useInstall() {
  const [evt, setEvt] = useState<(Event & { prompt: () => Promise<void> }) | null>(null);

  useEffect(() => {
    const on = (e: Event) => { e.preventDefault(); setEvt(e as Event & { prompt: () => Promise<void> }); };
    window.addEventListener('beforeinstallprompt', on);
    return () => window.removeEventListener('beforeinstallprompt', on);
  }, []);

  const standalone =
    window.matchMedia('(display-mode: standalone)').matches ||
    (navigator as Navigator & { standalone?: boolean }).standalone === true;
  const ios = /iphone|ipad|ipod/i.test(navigator.userAgent);

  return { standalone, ios, canPrompt: evt !== null, prompt: () => evt?.prompt() };
}
