# Vitara on a phone

Vitara is one codebase. Below 720 px wide it renders as a phone app; above that, the desktop app.
`?app=1` forces the phone shell on a laptop (handy for trying it), `?desktop=1` forces the full layout
on a phone.

## What the phone app is

| | |
|---|---|
| **Shell** | A header that stays put, a bottom bar with the five places you go daily (Today, Sleep, Recovery, Activity, Insight), and a **More** sheet for the rest (Labs, Body, Food, Protocols, Record a reading, Import, Everything we track, Sign out). Respects the notch and home indicator. |
| **Today** | Three concentric rings (sleep, recovery, activity) with readiness in the middle, one computed sentence about the day, swipeable cards (HRV, resting HR, time asleep, steps) each against *your own* usual with a trend line, tomorrow's forecast, the top finding from Insight, and last night's stages. Pull down to refresh. |
| **Everything else** | The same Sleep, Recovery, Activity, Insight, Labs... screens the desktop app uses, not a second copy. Insight's own tab bar moves under the header so it doesn't collide with the bottom bar. |
| **Installable** | A web app manifest, generated icons (`scripts/make-icons.py`) and a service worker. |

Code: `src/mobile/` (shell, Today, rings), `src/styles/mobile.css`, `public/sw.js`, `public/manifest.webmanifest`.

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

Push notifications, a home-screen widget, a dark theme, offline reading of the last known data, and per-person device
keys (see `docs/VITARA-ACCOUNTS.md`). None of them is blocked by anything here.

## How it is meant to differ from Oura, Whoop and Apple Health

It will not out-sense them: it has no sensor of its own. What it does that they do not is judge everything against
**your own history only**, say plainly when it cannot judge (a measure with no baseline yet, a forecast that did not beat
"same as today"), refuse to invent a single health score, and keep the data on your own server.
