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

    private init() {
        resetInput()
    }

    func open(width: Int32, height: Int32, title: String, font: Data?, fontSize: CGFloat, hidden: Bool) -> Bool {
        if initialised {
            return true
        }

        renderer = BuildsRenderer(fontData: font, size: fontSize)
        size = CGSize(width: CGFloat(width), height: CGFloat(height))
        self.title = title
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
        application.appearance = NSAppearance(named: .darkAqua)
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

        self.view = view
        self.window = window
        self.delegate = delegate
        makeScroller(in: view, renderer)
    }

    private func makeScroller(in view: MonitorView, _ renderer: BuildsRenderer) {
        let width = NSScroller.scrollerWidth(for: .regular, scrollerStyle: .legacy)
        let scroller = NSScroller(frame: NSRect(x: 0, y: 0, width: width, height: view.bounds.height))
        scroller.scrollerStyle = .legacy
        scroller.knobStyle = .light
        scroller.target = target
        scroller.action = #selector(ControlTarget.scrolled(_:))
        view.addSubview(scroller)
        self.scroller = scroller
        scrollerWidth = width
        renderer.rightInset = width
    }

    func present(_ frame: Frame) {
        tray?.apply(frame)
        guard let view else {
            // Hidden with no window yet: still pump, so the tray menu and the app menu work.
            pump()
            return
        }

        view.model = frame
        view.needsDisplay = true
        view.displayIfNeeded()
        view.refreshToolTips()
        position(frame)
        pump()
        popMenu(frame)
        measure()
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
        for (index, label) in frame.menu.enumerated() {
            let item = NSMenuItem(title: label, action: #selector(ControlTarget.contextItem(_:)), keyEquivalent: "")
            item.target = target
            item.tag = index
            menu.addItem(item)
        }

        let anchor = renderer.rowRects[Int(frame.menuRow)]
        if !menu.popUp(positioning: nil, at: CGPoint(x: anchor.minX + 24, y: anchor.maxY), in: view) {
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

    func show() {
        makeWindow()
        window?.makeKeyAndOrderFront(nil)
        NSApp.activate(ignoringOtherApps: true)
    }

    func hide() {
        window?.orderOut(nil)
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
        initialised = false
    }

    func resetInput() {
        input.key = Int32(BM_KEY_NONE.rawValue)
        input.clickedButton = -1
        input.clickedRow = -1
        input.clickedLinkRow = -1
        input.clickedLink = Int32(BM_LINK_NONE.rawValue)
        input.clickedActionRow = -1
        input.clickedAction = Int32(BM_ACTION_NONE.rawValue)
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
