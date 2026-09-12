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
    targets: [
        // Exists only to import the ABI. Swift does not guarantee struct layout, so the structs
        // have to come from the C header rather than being redeclared here.
        .target(name: "CBm"),
        .target(
            name: "Bm",
            dependencies: ["CBm"],
            linkerSettings: [
                .linkedFramework("AppKit"),
                .linkedFramework("CoreGraphics"),
                .linkedFramework("ImageIO")
            ])
    ])
