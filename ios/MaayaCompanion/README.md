# MaayaCompanion — iOS Companion for Maaya OS

Pushes device context (location, calendar, health) to the Maaya server so your assistant has real-time awareness of where you are, what's on your schedule, and how you're doing.

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
   - `Models/ContextPush.swift`
   - `Managers/LocationManager.swift`
   - `Managers/CalendarManager.swift`
   - `Managers/HealthManager.swift`
   - `Managers/SyncManager.swift`
   - `Services/APIClient.swift`
   - `Views/StatusView.swift`
   - `Views/HealthSummaryView.swift`
   - `Views/SettingsView.swift`
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

1. Open the app on your iPhone
2. Go to the **Settings** tab
3. Enter your **Server URL** — use your Mac's local IP for same-network access:
   - Example: `http://192.168.1.42:5300`
   - Find your Mac's IP: System Settings → Wi-Fi → Details → IP Address
4. Enter your **API Key** — this is the `DEVICE_API_KEY` from your `san/.env` file
5. Go back to the **Status** tab and tap **Sync Now** to test the connection

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
├── LocationManager   — CLLocationManager wrapper, background significant changes
├── CalendarManager   — EventKit wrapper, fetches next 7 days
├── HealthManager     — HealthKit wrapper, today's stats
├── SyncManager       — Coordinates all managers, background refresh task
├── APIClient         — URLSession POST to /api/context/push
└── Views
    ├── StatusView         — Sync status, map, sync button
    ├── HealthSummaryView  — Steps, heart rate, calories, sleep cards
    └── SettingsView       — Server URL, API key, auto-sync toggle
```
