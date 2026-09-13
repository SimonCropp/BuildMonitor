import AppKit
import CBm
import Foundation

/// Transcribed from the WinForms head's Palette so every head agrees.
enum Palette {
    private(set) static var light = false

    /// Returns true when the palette changed, so a caller rethemes only then. System reads the
    /// global default rather than NSApp's appearance, because a capture may run off the main
    /// thread and the app's appearance is the thing this decides.
    static func use(_ theme: Int32) -> Bool {
        let next: Bool
        switch UInt32(theme) {
        case BM_THEME_LIGHT.rawValue: next = true
        case BM_THEME_DARK.rawValue: next = false
        default: next = UserDefaults.standard.string(forKey: "AppleInterfaceStyle") != "Dark"
        }

        guard next != light else {
            return false
        }

        light = next
        return true
    }

    static var background: NSColor { pick(rgb(24, 24, 24), rgb(250, 250, 250)) }
    static var surface: NSColor { pick(rgb(32, 32, 32), rgb(240, 240, 240)) }
    static var headerRow: NSColor { pick(rgb(38, 38, 38), rgb(232, 232, 232)) }
    static var selectedRow: NSColor { pick(rgb(44, 50, 66), rgb(204, 222, 245)) }
    static var text: NSColor { pick(rgb(212, 212, 212), rgb(32, 32, 32)) }
    static var dim: NSColor { pick(rgb(140, 140, 140), rgb(110, 110, 110)) }
    static var border: NSColor { pick(rgb(56, 56, 56), rgb(204, 204, 204)) }
    static var barTrack: NSColor { pick(rgb(58, 58, 58), rgb(220, 220, 220)) }
    static var chip: NSColor { pick(rgb(52, 52, 56), rgb(226, 226, 232)) }
    static var chipText: NSColor { pick(rgb(180, 200, 255), rgb(0, 90, 180)) }
    static var retryChip: NSColor { pick(rgb(48, 82, 52), rgb(200, 230, 204)) }
    static var cancelChip: NSColor { pick(rgb(96, 52, 52), rgb(244, 208, 208)) }
    static var error: NSColor { pick(rgb(233, 129, 129), rgb(196, 43, 28)) }

    static func status(_ status: Int32) -> NSColor {
        switch UInt32(status) {
        case BM_STATUS_QUEUED.rawValue: return pick(rgb(150, 150, 150), rgb(120, 120, 120))
        case BM_STATUS_RUNNING.rawValue: return pick(rgb(86, 156, 214), rgb(0, 120, 212))
        case BM_STATUS_SUCCEEDED.rawValue: return pick(rgb(126, 214, 139), rgb(16, 124, 16))
        case BM_STATUS_FAILED.rawValue: return pick(rgb(233, 129, 129), rgb(196, 43, 28))
        case BM_STATUS_CANCELLED.rawValue: return pick(rgb(160, 160, 160), rgb(120, 120, 120))
        default: return pick(rgb(120, 120, 120), rgb(140, 140, 140))
        }
    }

    private static func pick(_ dark: NSColor, _ lightColour: NSColor) -> NSColor {
        light ? lightColour : dark
    }

    private static func rgb(_ red: CGFloat, _ green: CGFloat, _ blue: CGFloat) -> NSColor {
        NSColor(srgbRed: red / 255, green: green / 255, blue: blue / 255, alpha: 1)
    }
}
