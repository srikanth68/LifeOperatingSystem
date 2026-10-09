# Vitara on a phone

Vitara is one codebase. Below 720 px wide it renders as a phone app; above that, the desktop app.
`?app=1` forces the phone shell on a laptop (handy for trying it), `?desktop=1` forces the full layout
on a phone.

## What the phone app is

| | |
|---|---|
| **Shell** | A bottom bar with Today, Sleep, Recovery, Activity, Insight and **More**; the More sheet springs up with Labs, Body, Food, Protocols, Record a reading, Import, Everything we track, an **Appearance** switch (dark or light, remembered on the phone) and Sign out. Respects the notch and home indicator. |
| **Today** | Three gradient rings (sleep, recovery, activity) with readiness in the middle, the day's sentence, swipeable cards (HRV, resting HR, time asleep, steps) each with a dashed "usual" line and a trend that draws in, last night, tomorrow's forecast with its likely range, and the top finding. Pull down to refresh. |
| **Sleep** | Score ring and duration against your usual, each stage against its own usual, the last 14 nights over a band of your usual, and bedtime consistency. |
| **Recovery** | "Body weather" (a scale for the readiness score), HRV and resting heart rate over 30 days against the middle half of your own values, and activity beside readiness for the week. |
| **Activity** | Rings that close on *your usual* (there is no universal target), steps over 14 days, active minutes, recent workouts. |
| **Labs** (More) | The latest draw, each result laid on its reference range with the previous draw marked; coral is used for outside-range results and nothing else. Entering a draw is one tap away. |
| **Insight, and the deep pages** | Insight is the same page the desktop app uses, in the phone skin. Sleep, Recovery and Activity each end with a "Full ... detail" row that opens the desktop page for it. |
| **Installable** | A web app manifest, generated icons (`scripts/make-icons.py`) and a service worker. |

The look comes from the Claude Design project "Vitara Health App Design": Instrument Sans for the interface, Newsreader for
the one human sentence per screen, deep navy surfaces with teal (recovery), lavender (sleep), gold (activity) and blue
(insight). The app's own tokens (`--surface`, `--text`, `--border`) are re-pointed at the phone tokens inside `.hx.vm`, so any
desktop page shown in the phone shell follows the theme.

**Honesty rules the design set, kept in code.** An empty ring is a dashed track, never a zero. Stale data says it is stale and
since when. A missing sensor is named, with a way to fix it. Nothing is red for being below your usual; coral is reserved for
out-of-range lab results. Things the data does not contain are not drawn (there is no sleep timeline, hourly steps or "load"
score, so the screens do not pretend to).

**Class names.** Everything phone-specific is `vm-*`. The desktop metrics page already uses `.vm-card`, `.vm-head`,
`.vm-foot` and `.vm-note`, so the phone versions are `vm-tile`, `vm-hd`, `vm-end` and `vm-msg`; a colliding name restyles the
other app's page.

Code: `src/mobile/` (shell, the six screens, `Rings.tsx`, `Parts.tsx`, `motion.ts`, `stats.ts`), `src/styles/mobile.css`, `public/sw.js`, `public/manifest.webmanifest`.

## Installing it

- **iPhone:** open the app in Safari, tap **Share**, then **Add to Home Screen**. It opens full screen with no browser bar.
- **Android (Chrome):** open the menu, **Install app** (or use the install button in the More sheet).

It has to be served from an address the phone can reach. On the mesh that is `http://<server>:3100`. Note that
browsers only offer "install" and run service workers on **https** (or localhost); over plain http the app still
works and can be added to the home screen as a shortcut on iOS, but without offline start and, on Android, without
the install prompt. Putting TLS in front of `vitara-web` (as Maaya's frontend does on `:3443`) turns both on.

## What the service worker does, and refuses to do

It caches **the app itself** (page, scripts, styles, icons) so it opens instantly and survives a bad connection.
It **never** stores anything under `/svc/`, which is every request to the health servers and to sign-in: readings,
labs and tokens always go to the network and are never written to a cache. Offline, the app opens and says its data
cannot be reached rather than showing numbers that may be days old.

## Going native (Capacitor), when you want HealthKit

A native wrapper is the right next step if you want to read Apple Health directly, send notifications, or ship to the
App Store / Play Store. It needs a Mac with Xcode (iOS) and Android Studio (Android), so it cannot be built on a Windows
laptop. `capacitor.config.json` is already here. On the Mac:

```bash
cd vitara/web
npm i @capacitor/core @capacitor/cli @capacitor/ios @capacitor/android
VITE_API_BASE=https://<your-server>:<port> npm run build
npx cap add ios && npx cap sync && npx cap open ios
```

Things that are different inside a native webview, and need doing first:

1. **The API address.** On the web the page and the API share an origin (`/svc/...`). A native app serves its page
   from its own origin, so it must be told where the server is: `VITE_API_BASE` (already supported in
   `src/services/apiHost.ts`; empty on the web, so nothing changes there).
2. **CORS.** Because the origins now differ, `vitara-web`'s nginx must answer preflight requests and allow the
   `Authorization` and `X-Profile-Id` headers for `capacitor://localhost` (iOS) and `https://localhost` (Android).
3. **HTTPS.** iOS blocks plain http to anything but local-network exceptions, and a self-signed certificate is not
   trusted by the native webview unless installed on the device. Real TLS on the server is the clean answer.
4. **HealthKit.** The existing Swift companion in `ios/` already pushes readings to the server. A Capacitor HealthKit
   plugin could replace it so the app reads Apple Health itself; that is a separate piece of work.

## Not built yet

Push notifications, a home-screen widget, offline reading of the last known data (the app shows what it last loaded while open, but does not store readings on the phone, by design), and per-person device
keys (see `docs/VITARA-ACCOUNTS.md`). None of them is blocked by anything here.

## How it is meant to differ from Oura, Whoop and Apple Health

It will not out-sense them: it has no sensor of its own. What it does that they do not is judge everything against
**your own history only**, say plainly when it cannot judge (a measure with no baseline yet, a forecast that did not beat
"same as today"), refuse to invent a single health score, and keep the data on your own server.
