import AppKit
import CBm

/// Transcribed from the WinForms head's Palette so every head agrees.
enum Palette {
    static let background = NSColor(srgbRed: 24 / 255, green: 24 / 255, blue: 24 / 255, alpha: 1)
    static let surface = NSColor(srgbRed: 32 / 255, green: 32 / 255, blue: 32 / 255, alpha: 1)
    static let headerRow = NSColor(srgbRed: 38 / 255, green: 38 / 255, blue: 38 / 255, alpha: 1)
    static let selectedRow = NSColor(srgbRed: 44 / 255, green: 50 / 255, blue: 66 / 255, alpha: 1)
    static let text = NSColor(srgbRed: 212 / 255, green: 212 / 255, blue: 212 / 255, alpha: 1)
    static let dim = NSColor(srgbRed: 140 / 255, green: 140 / 255, blue: 140 / 255, alpha: 1)
    static let border = NSColor(srgbRed: 56 / 255, green: 56 / 255, blue: 56 / 255, alpha: 1)
    static let barTrack = NSColor(srgbRed: 58 / 255, green: 58 / 255, blue: 58 / 255, alpha: 1)
    static let chip = NSColor(srgbRed: 52 / 255, green: 52 / 255, blue: 56 / 255, alpha: 1)
    static let chipText = NSColor(srgbRed: 180 / 255, green: 200 / 255, blue: 255 / 255, alpha: 1)
    static let retryChip = NSColor(srgbRed: 48 / 255, green: 82 / 255, blue: 52 / 255, alpha: 1)
    static let cancelChip = NSColor(srgbRed: 96 / 255, green: 52 / 255, blue: 52 / 255, alpha: 1)
    static let error = NSColor(srgbRed: 233 / 255, green: 129 / 255, blue: 129 / 255, alpha: 1)

    static func status(_ status: Int32) -> NSColor {
        switch UInt32(status) {
        case BM_STATUS_QUEUED.rawValue: return NSColor(srgbRed: 150 / 255, green: 150 / 255, blue: 150 / 255, alpha: 1)
        case BM_STATUS_RUNNING.rawValue: return NSColor(srgbRed: 86 / 255, green: 156 / 255, blue: 214 / 255, alpha: 1)
        case BM_STATUS_SUCCEEDED.rawValue: return NSColor(srgbRed: 126 / 255, green: 214 / 255, blue: 139 / 255, alpha: 1)
        case BM_STATUS_FAILED.rawValue: return NSColor(srgbRed: 233 / 255, green: 129 / 255, blue: 129 / 255, alpha: 1)
        case BM_STATUS_CANCELLED.rawValue: return NSColor(srgbRed: 160 / 255, green: 160 / 255, blue: 160 / 255, alpha: 1)
        default: return NSColor(srgbRed: 120 / 255, green: 120 / 255, blue: 120 / 255, alpha: 1)
        }
    }
}
