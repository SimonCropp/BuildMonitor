import AppKit

/// The status bar item. The managed side sends the icon kind and the menu with every frame; this
/// keeps the images it was handed at start and rebuilds the menu when the items change.
final class Tray: NSObject, NSMenuDelegate {
    private var item: NSStatusItem?
    private var icons: [Int32: NSImage] = [:]
    private var menuIcons: [String: NSImage] = [:]
    private var lastIcon: Int32 = -1
    private var lastItems: [Frame.TrayItem] = []
    private var lastTooltip = ""
    private weak var runtime: Runtime?

    init(runtime: Runtime) {
        self.runtime = runtime
    }

    func start() -> Bool {
        let item = NSStatusBar.system.statusItem(withLength: NSStatusItem.squareLength)
        guard let button = item.button else {
            return false
        }

        button.target = self
        button.action = #selector(clicked(_:))
        button.sendAction(on: [.leftMouseUp, .rightMouseUp])
        self.item = item
        return true
    }

    func setIcon(kind: Int32, png: Data) {
        if let image = NSImage(data: png) {
            image.size = NSSize(width: 18, height: 18)
            icons[kind] = image
        }
    }

    func setMenuIcon(name: String, png: Data) {
        if let image = NSImage(data: png) {
            image.size = NSSize(width: 16, height: 16)
            menuIcons[name] = image
        }
    }

    func apply(_ frame: Frame) {
        guard let item else {
            return
        }

        if frame.trayIcon != lastIcon {
            lastIcon = frame.trayIcon
            item.button?.image = icons[frame.trayIcon]
        }

        if frame.trayTooltip != lastTooltip {
            lastTooltip = frame.trayTooltip
            item.button?.toolTip = frame.trayTooltip
        }

        if !same(frame.trayItems, lastItems) {
            lastItems = frame.trayItems
        }
    }

    private func same(_ left: [Frame.TrayItem], _ right: [Frame.TrayItem]) -> Bool {
        left.count == right.count && zip(left, right).allSatisfy {
            $0.id == $1.id && $0.label == $1.label && $0.enabled == $1.enabled
        }
    }

    @objc private func clicked(_ sender: Any?) {
        guard let item else {
            return
        }

        let menu = build()
        item.menu = menu
        item.button?.performClick(nil)
        item.menu = nil
    }

    private func build() -> NSMenu {
        let menu = NSMenu()
        menu.autoenablesItems = false
        for (index, entry) in lastItems.enumerated() {
            if entry.separator {
                menu.addItem(.separator())
                continue
            }

            let menuItem = NSMenuItem(title: entry.label, action: #selector(chose(_:)), keyEquivalent: "")
            menuItem.target = self
            menuItem.tag = index
            menuItem.isEnabled = entry.enabled
            menuItem.image = menuIcons[entry.icon]
            menu.addItem(menuItem)
        }

        return menu
    }

    @objc private func chose(_ sender: NSMenuItem) {
        runtime?.input.clickedTrayItem = Int32(sender.tag)
    }
}
