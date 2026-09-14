import AppKit

/// The images rows name in BmRow.provider, handed over once through bm_set_row_icon. Kept apart
/// from the tray's menu icons: a window with no tray still draws them.
enum RowIcons {
    static var images: [String: NSImage] = [:]
}
