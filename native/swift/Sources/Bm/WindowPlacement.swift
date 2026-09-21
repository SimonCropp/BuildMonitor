import AppKit
import CBm

/// A BmPlacement from an AppKit frame and back. AppKit measures from the bottom left of the
/// primary screen with y up, and the ABI from its top left with y down, as the other heads do.
/// The primary screen's height is a parameter rather than looked up, so a test can convert without
/// a screen to look it up on.
enum WindowPlacement {
    static func frame(of placement: BmPlacement, primaryHeight: CGFloat) -> NSRect {
        NSRect(
            x: CGFloat(placement.x),
            y: primaryHeight - CGFloat(placement.y) - CGFloat(placement.height),
            width: CGFloat(placement.width),
            height: CGFloat(placement.height))
    }

    /// Never maximized: AppKit's zoom is a size rather than a mode the window stays in, so a zoomed
    /// window is kept as the frame it zoomed to.
    static func placement(of frame: NSRect, primaryHeight: CGFloat) -> BmPlacement {
        BmPlacement(
            x: Int32(frame.minX.rounded()),
            y: Int32((primaryHeight - frame.maxY).rounded()),
            width: Int32(frame.width.rounded()),
            height: Int32(frame.height.rounded()),
            maximized: 0,
            known: 1)
    }

    /// Whether enough of the title bar is on one of the screens to drag the window by. A display
    /// unplugged since the window was left there, or a laptop taken off its dock, would otherwise
    /// open it where it could be neither seen nor dragged back.
    static func reachable(_ frame: NSRect, screens: [NSRect]) -> Bool {
        let titleBar = NSRect(x: frame.minX, y: frame.maxY - 28, width: frame.width, height: 28)
        return screens.contains {
            let visible = $0.intersection(titleBar)
            return visible.width >= 100 && visible.height > 0
        }
    }
}
