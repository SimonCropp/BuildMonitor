import AppKit

/// The images rows and chips name in BmRow.nameIcon, BmRow.detailIcon and BmChip.icon, handed
/// over once through bm_set_row_icon. Kept apart from the tray's menu icons: a window with no
/// tray still draws them.
enum RowIcons {
    static var images: [String: NSImage] = [:]
}
