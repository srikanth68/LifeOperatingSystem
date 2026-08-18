# MaayaCompanion — iOS Companion for Maaya OS

A private companion app for Maaya OS. It does three things:

1. **Background telemetry** — pushes device context (location, calendar, health snapshot) to San so your assistant has real-time awareness (the app's original purpose, unchanged).
2. **San, in full** — chat (with photo attachments), voice calls, and a Now tab that lists what is actually outstanding across San's reminders, San's alerts and NorthStar's action queue, each completable with one tap. The app is deliberately action-oriented: the per-module dashboards were removed, because reading net worth and sleep scores is what the website is for.
3. **Apple Health → Vitara** — uploads richer HealthKit data (steps, heart rate, calories, sleep, weight, recent workouts, and the last week of daily activity) straight into Vitara.

All traffic goes directly from the phone to Everest over the private NordVPN Meshnet / Tailscale mesh — no cloud services in the path. The mesh VPN app (NordVPN Meshnet or Tailscale) must be installed and connected on the iPhone; this app just talks plain HTTP to the mesh IP.

## Requirements

- macOS with Xcode 15+ installed
- iPhone running iOS 17.0+
- Apple ID (free or paid Developer account)

## Setup — Creating the Xcode Project

Since an `.xcodeproj` cannot be reliably generated outside Xcode, follow these steps to create the project:

### 1. Create the Project

1. Open Xcode
2. **File → New → Project**
3. Select **iOS → App**, click Next
4. Fill in:
   - **Product Name:** `MaayaCompanion`
   - **Team:** Your Apple ID
   - **Organization Identifier:** `com.maaya`
   - **Bundle Identifier:** should auto-fill to `com.maaya.companion`
   - **Interface:** SwiftUI
   - **Language:** Swift
   - **Storage:** None
5. Click Next, save inside `ios/MaayaCompanion/` (replace the existing `MaayaCompanion/` folder or save alongside it)

### 2. Add Source Files

1. In Xcode's Project Navigator, delete the auto-generated `ContentView.swift` and `MaayaCompanionApp.swift` (move to trash)
2. Right-click the `MaayaCompanion` group → **Add Files to "MaayaCompanion"**
3. Navigate to the `MaayaCompanion/` source folder and select all `.swift` files and subfolders:
   - `MaayaCompanionApp.swift`
   - `ContentView.swift`
   - `Theme.swift`
   - `Models/ContextPush.swift`
   - `Models/AuthModels.swift`
   - `Models/DashboardModels.swift`
   - `Models/VoiceModels.swift`
   - `Models/NotificationModels.swift`
   - `Models/ActionModels.swift`
   - `Managers/LocationManager.swift`
   - `Managers/CalendarManager.swift`
   - `Managers/HealthManager.swift`
   - `Managers/SyncManager.swift`
   - `Services/APIClient.swift`
   - `Services/AppConfig.swift`
   - `Services/KeychainStore.swift`
   - `Services/AuthService.swift`
   - `Services/MaayaClient.swift`
   - `Services/SpeechPlayer.swift`
   - `Services/SpeechChunks.swift`
   - `Services/VoiceConversationManager.swift`
   - `Services/NotificationManager.swift`
   - `Views/SettingsView.swift`
   - `Views/LoginView.swift`
   - `Views/ChatView.swift`
   - `Views/CallView.swift`
   - `Views/NowView.swift`
4. Make sure **"Copy items if needed"** is checked and **"Create groups"** is selected

### 3. Add Info.plist

1. In the Project Navigator, right-click the `MaayaCompanion` group → **Add Files**
2. Select the `Info.plist` file from the source folder
3. In Xcode, click on the project (top-level blue icon) → select the `MaayaCompanion` target
4. Go to the **Build Settings** tab, search for "Info.plist File"
5. Set the value to `MaayaCompanion/Info.plist`

### 4. Add Capabilities

1. Click on the project → select the `MaayaCompanion` target → **Signing & Capabilities** tab
2. Click **+ Capability** and add:
   - **HealthKit** — check "Clinical Health Records" is unchecked (you only need basic health data)
   - **Background Modes** — check these boxes:
     - Location updates
     - Background fetch
     - Background processing

**No extra entitlements are needed for auth, voice, or notifications.** Tokens are stored in the app's own Keychain via a generic-password item — that does **not** require the "Keychain Sharing" capability (that's only for sharing a keychain across apps). Voice uses AVFoundation (mic capture + audio playback), which needs **no capability** — only the `NSMicrophoneUsageDescription` string already in `Info.plist`. **Local notifications** (see below) also need **no capability** — that's the key advantage over remote push: the "Push Notifications" capability + a paid Apple Developer account are only required for APNs/remote push, which a free-Apple-ID sideload can't use anyway. The `Info.plist` also includes an **App Transport Security** exception (`NSAllowsArbitraryLoads`) because the app talks plain HTTP to the mesh IP; iOS blocks raw-IP HTTP otherwise. This is intentional for a private, non-App-Store build.

### Reminder & alert notifications

The app mirrors your San reminders and alerts into **on-device local notifications**, so the phone buzzes on time — natively, not only through Telegram:

- On every sync (foreground, on becoming active, and the ~15-min background refresh) it reads your reminders/alerts from San and schedules a local notification for each future one. iOS delivers these **even when the app is closed** (a `UNCalendarNotificationTrigger` — no server or APNs involved).
- Edited/completed/deleted items are reconciled off the schedule on the next sync.
- Server-side alerts that fire (e.g. a spending threshold) surface once, immediately, on the sync that discovers them.
- First launch prompts for notification permission. If you decline, everything else still works; grant it later in **Settings › MaayaCompanion › Notifications**.

> **Why not "real" push?** True remote push (APNs) needs a paid Apple Developer account, the Push Notifications capability, and a server component to send them — none of which a free-Apple-ID sideload supports. Local notifications deliver the same on-time buzz for time-based reminders with zero cloud and zero extra setup. If you later move to a paid account and want server-triggered pushes, that can be layered on.

### Voice conversation with San

The **San** tab shows two buttons in the nav bar **only when San's voice proxy is configured** (`WHISPER_SERVICE_URL` + `PIPER_SERVICE_URL` set on the server — see `maaya/VOICE.md`):

- **🔊 speaker toggle** — read San's text-chat replies aloud (local Piper TTS).
- **📞 phone** — enter **call mode**: a hands-free, continuous back-and-forth. It listens, detects when you stop talking, transcribes on your local Whisper, sends to San, speaks the reply on local Piper, and listens again — no push-to-talk. Tap the orb while San is talking to cut in; Mute pauses the mic; the red button hangs up. All audio stays on your mesh (Whisper + Piper + Gemma all run on Everest); nothing goes to a cloud speech service.

The buttons stay hidden until the voice services are up, so the app works normally without them.

### 5. Set Deployment Target

1. In the project settings, under **General** → **Minimum Deployments**
2. Set **iOS** to `17.0`

## Installing on iPhone

### Without App Store (Sideloading)

1. Connect your iPhone to your Mac via USB cable
2. In Xcode, select your iPhone from the device dropdown (top toolbar)
3. In **Signing & Capabilities**, select your Apple ID team
4. Press **Cmd+R** to build and run

**First-time trust on iPhone:**
1. The build may fail with "Untrusted Developer"
2. On iPhone: **Settings → General → VPN & Device Management**
3. Tap your developer email → **Trust**
4. Run again from Xcode

**Signing duration:**
- Free Apple ID: app expires after **7 days** — re-deploy from Xcode to renew
- Paid Apple Developer ($99/year): **1-year** signing

## Configuration

1. Make sure NordVPN Meshnet (or Tailscale) is connected on the iPhone so Everest is reachable.
2. Open the app. On first launch you'll see the **sign-in screen**:
   - On a trusted mesh network the server reports PIN auth → enter your Maaya PIN.
   - Otherwise → enter your Maaya username/password. (Tap "Use password instead" to switch off PIN.)
   - Tokens are saved in the Keychain; the app silently refreshes them and only shows the sign-in screen again if the session truly expires.
3. Go to the **Settings** tab and set:
   - **Scheme** — `http` (or `https` if you use the nginx proxy path — not required).
   - **Host / mesh IP** — e.g. `100.x.y.z` (defaults to `localhost`). Each module is reached on its own port off this host (5000–5700).
   - **Device Key** — the `DEVICE_API_KEY` shared by San and Vitara (used only for the telemetry/HealthKit uploads, not for sign-in).
4. The **Dashboard** and **San** tabs load automatically once signed in. On the **Status** tab, tap **Sync Now** to test the telemetry + Vitara HealthKit push.

> **One host, many ports.** The app derives every module URL from the single Host + Scheme (Vault 5000, Vitara 5100, Aasthi 5200, San 5300, Sutra 5400, NorthStar 5500, Karma 5600, Nexus 5700). If you later deploy the same-origin nginx proxy (port 3443), point Scheme=`https` and Host at that — but the per-port default needs no extra setup.

## How It Works

### Data Collected
- **Location:** Significant location changes (battery-efficient, ~500m movement threshold)
- **Calendar:** Events from all calendars for the next 7 days
- **Health:** Today's step count, latest heart rate, active calories burned, last night's sleep duration

### Sync Behavior
- **Manual:** Tap "Sync Now" on the Status tab
- **Background:** Approximately every 15 minutes when auto-sync is enabled (iOS controls exact timing)
- All data is sent to `POST /api/context/push` on your Maaya server

### API Request Format
```json
{
  "location": {
    "latitude": 37.7749,
    "longitude": -122.4194,
    "address": "San Francisco, CA"
  },
  "calendarEvents": [
    {
      "title": "Team Standup",
      "startTime": "2024-01-15T09:00:00Z",
      "endTime": "2024-01-15T09:30:00Z",
      "location": "Zoom",
      "allDay": false
    }
  ],
  "health": {
    "steps": 4230,
    "heartRate": 72,
    "activeCalories": 185,
    "sleepHours": 7.3
  },
  "timestamp": "2024-01-15T14:30:00Z"
}
```

The server should respond with:
```json
{
  "received": true,
  "message": "Context updated"
}
```

## Troubleshooting

| Problem | Solution |
|---------|----------|
| "Untrusted Developer" on iPhone | Settings → General → VPN & Device Management → Trust |
| Location not updating | Check Settings → Privacy → Location Services → MaayaCompanion → Always |
| Health data shows "--" | Check Settings → Privacy → Health → MaayaCompanion → enable all categories |
| Sync fails with network error | Ensure iPhone and server are on the same Wi-Fi network |
| Calendar shows no events | Check Settings → Privacy → Calendars → MaayaCompanion → Full Access |
| App disappeared after 7 days | Re-deploy from Xcode (free Apple ID limitation) |

## Architecture

```
MaayaCompanionApp
├── AuthService       — probe → PIN/credentials login, Keychain tokens, silent refresh (Maaya.Auth on Vault:5000)
├── MaayaClient       — authenticated (Bearer) read-only client for dashboards + San chat; refresh-on-401
├── AppConfig         — single host+scheme → per-module URLs; lenient JSON date decoding
├── LocationManager   — CLLocationManager wrapper, background significant changes
├── CalendarManager   — EventKit wrapper, fetches next 7 days
├── HealthManager     — HealthKit wrapper: today's stats + weight, workouts, multi-day history
├── SyncManager       — Coordinates telemetry (San /context/push) + Vitara /healthkit/ingest
├── APIClient         — URLSession POST for the device-key telemetry/HealthKit endpoints
├── KeychainStore     — generic-password wrapper for the auth tokens
└── Views
    ├── LoginView          — PIN pad (trusted network) or username/password
    ├── DashboardView      — read-only per-module summary cards + Sentinel board drill-in
    ├── ChatView           — San chat (history + send)
    ├── StatusView         — Sync status, map, sync button
    ├── HealthSummaryView  — Steps, heart rate, calories, sleep cards
    └── SettingsView       — Host/scheme, device key, account (sign out), sync toggles
```

### Auth & networking notes
- Every module enforces `RequireAuthenticatedUser` (Maaya.Auth `FallbackPolicy`) except the two device-key endpoints, so all dashboard/chat reads send `Authorization: Bearer <token>`. A 401 triggers one silent refresh, then falls back to the login screen.
- The telemetry (`/api/context/push`) and HealthKit (`/api/healthkit/ingest`) endpoints stay on the `X-Device-Key` header — unchanged.

### Server-side change
`vitara/Vitara.API/Controllers/HealthKitController.cs` — `HealthKitPayload` gained optional `WeightKg`, `Workouts[]`, and `DailyActivity[]` fields (plus `HealthKitWorkout` / `HealthKitDailyActivity` records). They upsert into Vitara's WeighIn / Workout / DailyActivity tables tagged `source: "apple_health"`. Fully backward compatible — the older snapshot-only body still works.
