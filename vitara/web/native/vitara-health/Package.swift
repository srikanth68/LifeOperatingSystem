// swift-tools-version: 5.9

import PackageDescription

// Vitara's Apple Health reader, as a local Capacitor plugin. `npx cap sync` finds it through
// the "vitara-health" entry in web/package.json and adds it to the app like any other plugin.
let package = Package(
    name: "VitaraHealth",
    platforms: [.iOS(.v15)],
    products: [
        .library(name: "VitaraHealth", targets: ["VitaraHealthPlugin"])
    ],
    dependencies: [
        .package(url: "https://github.com/ionic-team/capacitor-swift-pm.git", from: "8.0.0")
    ],
    targets: [
        .target(
            name: "VitaraHealthPlugin",
            dependencies: [
                .product(name: "Capacitor", package: "capacitor-swift-pm"),
                .product(name: "Cordova", package: "capacitor-swift-pm")
            ],
            path: "ios/Sources/VitaraHealthPlugin",
            linkerSettings: [.linkedFramework("HealthKit")]
        )
    ]
)
