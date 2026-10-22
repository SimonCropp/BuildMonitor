import AppKit
import CBm
import Foundation
import PlatformHeadsMac

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
    private var scroller: KnobScroller?
    private var scrollerWidth: CGFloat = 0
    private var menuShown = false
    private var edits: [(Int32, String)] = []
    private var changedValue: [UInt8] = []
    private var wheelNotches = WheelNotches()
    private lazy var target = ControlTarget(runtime: self)

    /// A quit from the Dock or a logout, reported as the app menu's Quit is, so the managed side
    /// leaves its loop and saves on its way out rather than AppKit ending the process from inside
    /// the pump. Held here because `NSApplication.delegate` does not keep it alive.
    private lazy var terminateGuard = TerminateGuard { [unowned self] in
        self.input.key = Int32(BM_KEY_QUIT.rawValue)
    }

    /// A tray app spends most of its life with no window on screen, which is when App Nap would
    /// coalesce the timers its polls and its socket answer on.
    private let napGuard = NapGuard()

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
        napGuard.begin(reason: "Watching builds")

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

        NSApplication.shared.appearance = NSAppearance(named: Palette.light ? .aqua : .darkAqua)
        HeadWindow.launch(menu: MainMenu.build(target), delegate: terminateGuard)

        let view = MonitorView(renderer: renderer, runtime: self, frame: NSRect(origin: .zero, size: size))
        let delegate = WindowDelegate(runtime: self)
        let window = HeadWindow.make(view: view, size: size, title: title, delegate: delegate)
        window.minSize = NSSize(width: 700, height: 400)
        if let placement {
            WindowPlacement.restore(
                window,
                to: WindowBounds(x: placement.x, y: placement.y, width: placement.width, height: placement.height))
        }

        self.view = view
        self.window = window
        self.delegate = delegate
        makeScroller(in: view, renderer)
    }

    /// Before a tray with no window has been shown, a quit is still one to answer.
    func guardTermination() {
        NSApplication.shared.delegate = terminateGuard
    }

    /// The rows' scrollbar, a strip of its own beside them, whose knob is followed as it is
    /// dragged: see `KnobScroller`.
    private func makeScroller(in view: MonitorView, _ renderer: BuildsRenderer) {
        let scroller = KnobScroller.legacy(in: view, target: target, action: #selector(ControlTarget.scrolled(_:)))
        scroller.knobStyle = Palette.light ? .dark : .light
        scroller.knobDragged = { [unowned self] in
            self.knobDragged(to: $0)
        }

        self.scroller = scroller
        scrollerWidth = scroller.frame.width
        renderer.rightInset = scrollerWidth
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
        // After drawing, which is what places the rows the tips are over.
        view.refreshToolTips()
        position(frame)
        pump()
        popMenu(frame)
        measure()
    }

    /// A frame with the generation last presented: nothing to decode, lay out or draw again, only
    /// events to pump. The spinner is the exception: it is drawn from the clock, so it turns only
    /// while the view is drawn. The tips are asked again all the same, since a resize draws inside
    /// the pump and moves the rows under them with no new screen.
    func pumpUnchanged() {
        if let view, let model = view.model, !model.isForm, model.rows.isEmpty, model.loading {
            view.needsDisplay = true
            view.displayIfNeeded()
        }

        view?.refreshToolTips()
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
        // Assigned during a drag of the knob as at any other time: `KnobScroller` reports where a
        // drag has got to without moving the knob, so this is what moves it.
        travel(of: frame, renderer).apply(to: scroller)
    }

    /// The rows as the scroller counts them.
    private func travel(of frame: Frame, _ renderer: BuildsRenderer) -> ScrollerTravel {
        ScrollerTravel(top: Int(frame.scrollTop), visible: renderer.bodyRows, total: Int(frame.totalRows))
    }

    /// Translates wherever the scroller was grabbed into a first visible row. What reaches here is
    /// what AppKit tracked itself, which is a press in the slot.
    func scrolled(_ scroller: NSScroller) {
        guard let frame = view?.model, let renderer else {
            return
        }

        input.scrollTo = travel(of: frame, renderer).row(for: scroller.hitPart, value: scroller.doubleValue)
    }

    /// The knob dragged to `value` of the way along its travel. AppKit's own tracking loop left
    /// the rows still until the knob was let go.
    private func knobDragged(to value: Double) {
        guard let frame = view?.model, let renderer else {
            return
        }

        input.scrollTo = travel(of: frame, renderer).row(at: value)
    }

    /// Down the rows positive, as the ABI counts a scroll, where AppKit counts up the screen. One
    /// slow click of a wheel is a row, which used to take ten.
    func wheel(_ event: NSEvent, rowHeight: CGFloat) {
        input.scrollDelta -= wheelNotches.take(event, pointsPerNotch: Double(rowHeight))
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

    /// Drains what is queued and waits out the frame, returning as soon as an event leaves input
    /// for the next poll, and waiting longer while the window is behind the tray: see `EventPump`.
    /// It waited out the whole frame whatever arrived, so a key was answered a frame late, and
    /// turned sixty times a second behind the tray for nobody. A tray whose window has never been
    /// shown has none to see, and its menu's clicks are events, which end the wait at once.
    private func pump() {
        EventPump.pump(unseen: window == nil || EventPump.isUnseen(window)) { hasInput }
    }

    /// Something is waiting for the next poll to hand over, by the values `resetInput` clears
    /// each to.
    private var hasInput: Bool {
        !edits.isEmpty ||
            input.key != Int32(BM_KEY_NONE.rawValue) ||
            input.clickedButton >= 0 ||
            input.clickedRow >= 0 ||
            input.clickedChipRow >= 0 ||
            input.clickedOverflowRow >= 0 ||
            input.rightClickedRow >= 0 ||
            input.clickedMenuItem >= 0 ||
            input.menuClosed != 0 ||
            input.clickedField >= 0 ||
            input.clickedTrayItem >= 0 ||
            input.trayIconClicked != 0 ||
            input.scrollDelta != 0 ||
            input.scrollTo >= 0 ||
            input.closeRequested != 0
    }

    func measure() {
        guard let renderer else {
            return
        }

        renderer.layout(size: view?.bounds.size ?? size)
        input.rows = Int32(renderer.bodyRows)
    }

    /// Whether the window is minimized, into the input every poll, as a state rather than an event.
    func sampleMinimized() {
        input.minimized = (window?.isMiniaturized ?? false) ? 1 : 0
    }

    /// Where the window is, into the input every poll. Left as it was while the window is hidden,
    /// minimized or full screen, none of which is a place to open at.
    func samplePlacement() {
        guard let window, window.isVisible, !window.isMiniaturized, let bounds = WindowPlacement.current(of: window) else {
            return
        }

        input.placement = BmPlacement(x: bounds.x, y: bounds.y, width: bounds.width, height: bounds.height, maximized: 0, known: 1)
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
        HeadWindow.show(window)
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
        napGuard.end()
        HeadWindow.close(window)
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
