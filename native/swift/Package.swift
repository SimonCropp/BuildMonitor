// swift-tools-version: 5.9
import PackageDescription

// The product name decides the file name: SwiftPM emits lib<product>.dylib, which is what
// NativeResolver probes for. Nothing else here may rename it.
let package = Package(
    name: "buildmonitor_ui",
    platforms: [.macOS(.v12)],
    products: [
        .library(name: "buildmonitor_ui", type: .dynamic, targets: ["Bm"])
    ],
    dependencies: [
        // What every AppKit head does underneath its drawing, shared with DiffEngine's viewer. The
        // sixth pin of PlatformHeads, moved with the other five.
        .package(url: "https://github.com/SimonCropp/PlatformHeads", branch: "native-runtime")
    ],
    targets: [
        // Exists only to import the ABI. Swift does not guarantee struct layout, so the structs
        // have to come from the C header rather than being redeclared here.
        .target(name: "CBm"),
        .target(
            name: "Bm",
            dependencies: [
                "CBm",
                .product(name: "PlatformHeadsMac", package: "PlatformHeads")
            ],
            linkerSettings: [
                .linkedFramework("AppKit"),
                .linkedFramework("CoreGraphics"),
                .linkedFramework("ImageIO")
            ]),
        // The only head with tests of its own. Everything above a renderer is managed code and is
        // tested there; the folder panel is one of the few things this head decides by itself,
        // because it is AppKit rather than a program the managed side could run and watch.
        // Turning AppKit's frames into placements is PlatformHeads', and tested there.
        .testTarget(name: "BmTests", dependencies: ["Bm"])
    ])
