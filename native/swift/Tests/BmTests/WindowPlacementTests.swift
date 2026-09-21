import AppKit
import XCTest
@testable import Bm

/// Where the window was left, across the ABI and back. No screen is looked up, so these run
/// wherever the package builds.
final class WindowPlacementTests: XCTestCase {
    func testAFrameComesBackAsTheFrameItWas() {
        let frame = NSRect(x: 120, y: 200, width: 1000, height: 640)
        let placement = WindowPlacement.placement(of: frame, primaryHeight: 1080)
        XCTAssertEqual(WindowPlacement.frame(of: placement, primaryHeight: 1080), frame)
    }

    /// From the top, as the other heads measure: 1080 less the frame's top at 200 + 640.
    func testYIsMeasuredFromTheTopOfThePrimaryScreen() {
        let placement = WindowPlacement.placement(of: NSRect(x: 120, y: 200, width: 1000, height: 640), primaryHeight: 1080)
        XCTAssertEqual(placement.y, 240)
        XCTAssertEqual(placement.known, 1)
        XCTAssertEqual(placement.maximized, 0)
    }

    /// A title bar on no screen, as after the display it was left on is unplugged, opens centred
    /// rather than out of reach.
    func testATitleBarOnNoScreenIsOutOfReach() {
        let screens = [NSRect(x: 0, y: 0, width: 1920, height: 1080)]
        XCTAssertTrue(WindowPlacement.reachable(NSRect(x: 100, y: 100, width: 1000, height: 640), screens: screens))
        XCTAssertFalse(WindowPlacement.reachable(NSRect(x: 5000, y: 100, width: 1000, height: 640), screens: screens))
        // Its bottom still on the screen, its title bar above it.
        XCTAssertFalse(WindowPlacement.reachable(NSRect(x: 100, y: 1000, width: 1000, height: 640), screens: screens))
    }
}
