import AppKit
import CBm
import Foundation

/// Process wide, because the ABI is: one window, addressed by free functions.
///
/// Everything here runs on the thread that calls in, which is the managed side's main thread and
/// therefore the process main thread. AppKit requires that, and it is also why the loop stays in
/// C# rather than being inverted into `NSApplication.run`.
final class Runtime {
    static let shared = Runtime()

    private var delegate: WindowDelegate?
    private var size = CGSize(width: 1000, height: 640)
    /// Where the window was left, which the window is made at in place of centred.
    private var placement: BmPlacement?
    private var title = "BuildMonitor"
    private var scroller: NSScroller?
    private var scrollerWidth: CGFloat = 0
    private var menuShown = false
    private var edits: [(Int32, String)] = []
    private var changedValue: [UInt8] = []
    private var wheelAccumulator: CGFloat = 0
    private lazy var target = ControlTarget(runtime: self)

    var window: NSWindow?
    var view: MonitorView?
    var renderer: BuildsRenderer?
    var tray: Tray?
    var input = BmInput()
    var initialised = false

    /// The generation of the screen last presented, so one presented again is not decoded or drawn
    /// again. Nil before the first, and after a show, which may have made the window it goes in.
    var presentedGeneration: Int32?

    private init() {
        resetInput()
    }

    func open(width: Int32, height: Int32, title: String, font: Data?, fontSize: CGFloat, placement: BmPlacement?, hidden: Bool) -> Bool {
        if initialised {
            return true
        }

        renderer = BuildsRenderer(fontData: font, size: fontSize)
        size = CGSize(width: CGFloat(width), height: CGFloat(height))
        self.title = title
        if let placement, placement.known != 0 {
            self.placement = placement
        }

        initialised = true

        // A hidden start builds no window: NSWindow may only be made on the main thread, and a
        // test host captures from whatever thread it likes. Capture draws into a bitmap of its
        // own, so a headless runtime is still useful. A tray app starts hidden too, and the first
        // show() makes the window then.
        if !hidden {
            show()
        }

        measure()
        return true
    }

    private func makeWindow() {
        guard window == nil, let renderer else {
            return
        }

        let application = NSApplication.shared
        application.setActivationPolicy(.regular)
        application.appearance = NSAppearance(named: Palette.light ? .aqua : .darkAqua)
        application.mainMenu = MainMenu.build(target)
        application.finishLaunching()

        let bounds = NSRect(origin: .zero, size: size)
        let view = MonitorView(renderer: renderer, runtime: self, frame: bounds)
        let window = NSWindow(
            contentRect: bounds,
            styleMask: [.titled, .closable, .miniaturizable, .resizable],
            backing: .buffered,
            defer: false)
        let delegate = WindowDelegate(runtime: self)
        window.title = title
        window.contentView = view
        window.delegate = delegate
        window.isReleasedWhenClosed = false
        window.minSize = NSSize(width: 700, height: 400)
        window.center()
        if let placement, let primary = NSScreen.screens.first {
            let frame = WindowPlacement.frame(of: placement, primaryHeight: primary.frame.height)
            if WindowPlacement.reachable(frame, screens: NSScreen.screens.map(\.visibleFrame)) {
                window.setFrame(frame, display: false)
            }
        }

        self.view = view
        self.window = window
        self.delegate = delegate
        makeScroller(in: view, renderer)
    }

    private func makeScroller(in view: MonitorView, _ renderer: BuildsRenderer) {
        let width = NSScroller.scrollerWidth(for: .regular, scrollerStyle: .legacy)
        let scroller = NSScroller(frame: NSRect(x: 0, y: 0, width: width, height: view.bounds.height))
        scroller.scrollerStyle = .legacy
        scroller.knobStyle = Palette.light ? .dark : .light
        scroller.target = target
        scroller.action = #selector(ControlTarget.scrolled(_:))
        view.addSubview(scroller)
        self.scroller = scroller
        scrollerWidth = width
        renderer.rightInset = width
    }

    func present(_ frame: Frame) {
        if Palette.use(frame.theme) {
            retheme()
        }

        tray?.apply(frame)
        guard let view else {
            // Hidden with no window yet: still pump, so the tray menu and the app menu work.
            pump()
            return
        }

        view.model = frame
        view.needsDisplay = true
        view.displayIfNeeded()
        position(frame)
        pump()
        popMenu(frame)
        measure()
    }

    /// A frame with the generation last presented: nothing to decode, lay out or draw again, only
    /// events to pump. The spinner is the exception: it is drawn from the clock, so it turns only
    /// while the view is drawn.
    func pumpUnchanged() {
        if let view, let model = view.model, !model.isForm, model.rows.isEmpty, model.loading {
            view.needsDisplay = true
            view.displayIfNeeded()
        }

        pump()
    }

    /// The drawn parts read the palette every frame; this is for what AppKit holds on to: the
    /// app's appearance, which the controls and menus follow, and the form's built controls.
    private func retheme() {
        guard window != nil else {
            return
        }

        NSApplication.shared.appearance = NSAppearance(named: Palette.light ? .aqua : .darkAqua)
        scroller?.knobStyle = Palette.light ? .dark : .light
        view?.form.retheme()
    }

    private func position(_ frame: Frame) {
        guard let view, let scroller, let renderer else {
            return
        }

        let body = renderer.bodyRect
        let hidden = frame.isForm || frame.totalRows <= frame.rows.count
        scroller.isHidden = hidden
        scroller.frame = NSRect(x: view.bounds.maxX - scrollerWidth, y: body.minY, width: scrollerWidth, height: max(1, body.height))
        let visible = max(1, renderer.bodyRows)
        let total = max(Int(frame.totalRows), visible)
        let maximum = total - visible
        scroller.knobProportion = CGFloat(visible) / CGFloat(total)
        scroller.doubleValue = maximum <= 0 ? 0 : Double(frame.scrollTop) / Double(maximum)
    }

    func scrolled(_ scroller: NSScroller) {
        guard let frame = view?.model, let renderer else {
            return
        }

        let visible = max(1, renderer.bodyRows)
        let maximum = max(0, Int(frame.totalRows) - visible)
        switch scroller.hitPart {
        case .decrementPage: input.scrollTo = Int32(max(0, Int(frame.scrollTop) - visible))
        case .incrementPage: input.scrollTo = Int32(min(maximum, Int(frame.scrollTop) + visible))
        case .decrementLine: input.scrollTo = Int32(max(0, Int(frame.scrollTop) - 1))
        case .incrementLine: input.scrollTo = Int32(min(maximum, Int(frame.scrollTop) + 1))
        default: input.scrollTo = Int32((scroller.doubleValue * Double(maximum)).rounded())
        }
    }

    func wheel(_ delta: CGFloat) {
        wheelAccumulator += delta
        let rows = Int32(wheelAccumulator.rounded(.towardZero))
        if rows != 0 {
            wheelAccumulator -= CGFloat(rows)
            input.scrollDelta -= rows
        }
    }

    func queueEdit(field: Int, value: String) {
        edits.append((Int32(field), value))
    }

    /// Hands over the oldest queued edit, pointing the ABI at a buffer this side keeps until the
    /// next poll.
    func drainEdit() {
        guard !edits.isEmpty else {
            return
        }

        let (field, value) = edits.removeFirst()
        changedValue = Array(value.utf8)
        input.changedField = field
        input.changedValueLength = Int32(changedValue.count)
        input.changedValue = changedValue.withUnsafeBufferPointer { $0.baseAddress }
    }

    private func popMenu(_ frame: Frame) {
        guard !frame.menu.isEmpty else {
            menuShown = false
            return
        }

        guard !menuShown, let view, let renderer, frame.menuRow >= 0, Int(frame.menuRow) < renderer.rowRects.count else {
            return
        }

        menuShown = true
        let menu = NSMenu()
        for (index, entry) in frame.menu.enumerated() {
            // Before the item and with no tag of its own, so a click still reports the item's index.
            if entry.separatorAbove {
                menu.addItem(.separator())
            }

            let item = NSMenuItem(title: entry.label, action: #selector(ControlTarget.contextItem(_:)), keyEquivalent: "")
            item.target = target
            item.tag = index
            menu.addItem(item)
        }

        // A drop down hangs under the overflow chip that opened it, a context menu under its row.
        let row = renderer.rowRects[Int(frame.menuRow)]
        var anchor = CGPoint(x: row.minX + 24, y: row.maxY)
        if frame.menuOverflow, let chip = renderer.chips.last(where: { $0.overflow && $0.row == Int(frame.menuRow) }) {
            anchor = CGPoint(x: chip.rect.minX, y: chip.rect.maxY)
        }

        if !menu.popUp(positioning: nil, at: anchor, in: view) {
            input.menuClosed = 1
        }
    }

    private func pump() {
        let deadline = Date(timeIntervalSinceNow: 1.0 / 60.0)
        while let event = NSApp.nextEvent(matching: .any, until: deadline, inMode: .default, dequeue: true) {
            NSApp.sendEvent(event)
        }
    }

    func measure() {
        guard let renderer else {
            return
        }

        renderer.layout(size: view?.bounds.size ?? size)
        input.rows = Int32(renderer.bodyRows)
    }

    /// Where the window is, into the input every poll. Left as it was while the window is hidden,
    /// minimized or full screen, none of which is a place to open at.
    func samplePlacement() {
        guard let window, window.isVisible, !window.isMiniaturized,
              !window.styleMask.contains(.fullScreen),
              let primary = NSScreen.screens.first else {
            return
        }

        input.placement = WindowPlacement.placement(of: window.frame, primaryHeight: primary.frame.height)
    }

    /// What the pointer is on, into the input every poll. Sampled rather than tracked through
    /// mouseMoved, because it is a state and not an event: the managed side holds the rows still
    /// while a pointer is on its way to a chip, and a pointer resting on one sends nothing to say
    /// it is still there. The hits are those of the last frame drawn, as a click's are.
    func sampleHover() {
        input.hoveredChipRow = -1
        input.hoveredChip = Int32(BM_CHIP_NONE.rawValue)
        guard let window, window.isVisible, !window.isMiniaturized, let view, let renderer else {
            return
        }

        let point = view.convert(window.mouseLocationOutsideOfEventStream, from: nil)
        guard view.bounds.contains(point), let hit = renderer.chips.first(where: { $0.rect.contains(point) }) else {
            return
        }

        input.hoveredChipRow = Int32(hit.row)
        input.hoveredChip = hit.chip
    }

    func show() {
        makeWindow()
        presentedGeneration = nil
        window?.makeKeyAndOrderFront(nil)
        NSApp.activate(ignoringOtherApps: true)
    }

    func hide() {
        window?.orderOut(nil)
    }

    /// The folder chooser, as a panel of this app rather than as a program of its own. A chooser
    /// run as a program puts its window up from another process, which leaves nothing pumping the
    /// loop this head's window is drawn from: it beachballs within a second, and the beachball then
    /// sits in front of the panel for as long as the user takes to answer it, which reads as a hang
    /// rather than as a dialog waiting. `runModal` pumps AppKit itself, so the window behind the
    /// panel stays drawn, the way the WinForms head's `ShowDialog` does.
    ///
    /// `run` is what puts the panel up and reads what it was answered with. It is a parameter
    /// because a panel cannot be answered from code: a test stands in for it rather than leaving
    /// one open on a machine nobody is sitting at.
    func pickDirectory(start: String?, run: (NSOpenPanel) -> URL? = Runtime.runPanel) -> String? {
        let panel = directoryPanel(start: start)
        // An accessory app is not frontmost just because one of its windows is, and a panel put up
        // by an app that is not frontmost opens behind whatever is.
        NSApp.activate(ignoringOtherApps: true)
        guard let url = run(panel) else {
            return nil
        }

        return url.path
    }

    /// The panel as it is asked for, made but not run, so how it was configured can be looked at
    /// without one opening.
    ///
    /// A start that is no longer there is left off rather than set: a code directory that has since
    /// been moved would otherwise open the panel at a path that does not exist, instead of wherever
    /// the user last was.
    func directoryPanel(start: String?) -> NSOpenPanel {
        let panel = NSOpenPanel()
        panel.canChooseDirectories = true
        panel.canChooseFiles = false
        panel.allowsMultipleSelection = false
        panel.canCreateDirectories = false
        panel.message = "Choose your code directory"
        panel.prompt = "Choose"
        if let start, FileManager.default.fileExists(atPath: start) {
            panel.directoryURL = URL(fileURLWithPath: start)
        }

        return panel
    }

    /// The one line a test cannot stand in for: the panel on screen, and what the user did with it.
    static func runPanel(_ panel: NSOpenPanel) -> URL? {
        panel.runModal() == .OK ? panel.url : nil
    }

    func shutdown() {
        window?.delegate = nil
        window?.orderOut(nil)
        window?.close()
        window = nil
        view = nil
        renderer = nil
        delegate = nil
        scroller = nil
        tray = nil
        menuShown = false
        presentedGeneration = nil
        initialised = false
    }

    func resetInput() {
        input.key = Int32(BM_KEY_NONE.rawValue)
        input.clickedButton = -1
        input.clickedRow = -1
        input.clickedChipRow = -1
        input.clickedChip = Int32(BM_CHIP_NONE.rawValue)
        input.clickedOverflowRow = -1
        input.overflowFrom = Int32(BM_CHIP_NONE.rawValue)
        // Filled again at every poll by sampleHover, like the placement, rather than left as
        // whatever the frame that read it happened to see.
        input.hoveredChipRow = -1
        input.hoveredChip = Int32(BM_CHIP_NONE.rawValue)
        input.rightClickedRow = -1
        input.clickedMenuItem = -1
        input.menuClosed = 0
        input.changedField = -1
        input.changedValue = nil
        input.changedValueLength = 0
        input.clickedField = -1
        input.clickedTrayItem = -1
        input.trayIconClicked = 0
        input.scrollDelta = 0
        input.scrollTo = -1
        input.closeRequested = 0
    }
}

/// What every AppKit control here sends to. Held for the process, because a menu item's target
/// is a weak reference.
final class ControlTarget: NSObject {
    private unowned let runtime: Runtime

    init(runtime: Runtime) {
        self.runtime = runtime
    }

    @objc func scrolled(_ sender: NSScroller) {
        runtime.scrolled(sender)
    }

    @objc func contextItem(_ sender: NSMenuItem) {
        runtime.input.clickedMenuItem = Int32(sender.tag)
    }

    @objc func hideWindow(_ sender: Any?) {
        runtime.input.key = Int32(BM_KEY_HIDE.rawValue)
    }

    @objc func quit(_ sender: Any?) {
        runtime.input.key = Int32(BM_KEY_QUIT.rawValue)
    }

    @objc func refresh(_ sender: Any?) {
        runtime.input.key = Int32(BM_KEY_REFRESH.rawValue)
    }
}

final class WindowDelegate: NSObject, NSWindowDelegate {
    private unowned let runtime: Runtime

    init(runtime: Runtime) {
        self.runtime = runtime
    }

    /// Closing hides. The managed side decides what a close means, and for a tray app it is hide.
    func windowShouldClose(_ sender: NSWindow) -> Bool {
        runtime.input.closeRequested = 1
        return false
    }

    func windowDidResize(_ notification: Notification) {
        runtime.measure()
        runtime.view?.needsDisplay = true
    }
}

enum MainMenu {
    static func build(_ target: ControlTarget) -> NSMenu {
        let bar = NSMenu()
        let application = NSMenuItem()
        let applicationMenu = NSMenu()
        applicationMenu.addItem(withTitle: "Hide BuildMonitor", action: #selector(ControlTarget.hideWindow(_:)), keyEquivalent: "h").target = target
        applicationMenu.addItem(.separator())
        applicationMenu.addItem(withTitle: "Quit BuildMonitor", action: #selector(ControlTarget.quit(_:)), keyEquivalent: "q").target = target
        application.submenu = applicationMenu
        bar.addItem(application)

        let edit = NSMenuItem()
        let editMenu = NSMenu(title: "Edit")
        editMenu.addItem(withTitle: "Cut", action: #selector(NSText.cut(_:)), keyEquivalent: "x")
        editMenu.addItem(withTitle: "Copy", action: #selector(NSText.copy(_:)), keyEquivalent: "c")
        editMenu.addItem(withTitle: "Paste", action: #selector(NSText.paste(_:)), keyEquivalent: "v")
        editMenu.addItem(withTitle: "Select All", action: #selector(NSText.selectAll(_:)), keyEquivalent: "a")
        edit.submenu = editMenu
        bar.addItem(edit)

        let viewItem = NSMenuItem()
        let viewMenu = NSMenu(title: "View")
        viewMenu.addItem(withTitle: "Refresh", action: #selector(ControlTarget.refresh(_:)), keyEquivalent: "r").target = target
        viewItem.submenu = viewMenu
        bar.addItem(viewItem)
        return bar
    }
}
