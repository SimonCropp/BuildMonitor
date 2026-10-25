import AppKit
import PlatformHeadsMac

/// The status bar item, which is PlatformHeads' `StatusItem`. The managed side sends the icon kind
/// and the menu with every frame; this hands them over, and reports a click as the index of the
/// item in the frame's list, which is what `BmInput.clickedTrayItem` carries.
final class Tray {
    private let item = StatusItem()
    private var ids: [String] = []
    private weak var runtime: Runtime?

    init(runtime: Runtime) {
        self.runtime = runtime
        item.iconClicked = { [weak self] in
            self?.runtime?.input.trayIconClicked = 1
        }
        item.chose = { [weak self] chosen in
            guard let self, let index = self.ids.firstIndex(of: chosen.id) else {
                return
            }

            self.runtime?.input.clickedTrayItem = Int32(index)
        }
    }

    func start() -> Bool {
        item.start()
    }

    func setIcon(kind: Int32, png: Data) {
        item.setIcon(kind, png: png)
    }

    func setMenuIcon(name: String, png: Data) {
        item.setMenuIcon(name, png: png)
    }

    func apply(_ frame: Frame) {
        ids = frame.trayItems.map { $0.id }
        item.apply(
            icon: frame.trayIcon,
            tooltip: frame.trayTooltip,
            items: frame.trayItems.map {
                StatusMenuItem(id: $0.id, label: $0.label, icon: $0.icon, enabled: $0.enabled, separator: $0.separator)
            })
    }
}
