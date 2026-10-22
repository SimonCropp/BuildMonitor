import AppKit
import CBm
import PlatformHeadsMac

/// The window's content: the builds page and footer drawn by the renderer, with the form view
/// laid over the body while a form page is up, and the filter box over the header while it is not.
final class MonitorView: NSView {
    let renderer: BuildsRenderer
    let form: FormView
    var model: Frame?
    private weak var runtime: Runtime?

    /// Holds the form, so a page with more fields than the body fits scrolls instead of hiding its
    /// last controls behind the footer where they cannot be reached.
    private let formScroll = NSScrollView()

    private let toolTips = ToolTipRegions()

    /// The filter box: a real search field, for the platform's editing. It is pushed the session's
    /// text only while it is not being edited, so a frame never fights the typist.
    private let search = NSSearchField()

    init(renderer: BuildsRenderer, runtime: Runtime, frame: NSRect) {
        self.renderer = renderer
        self.runtime = runtime
        form = FormView(runtime: runtime)
        super.init(frame: frame)
        wantsLayer = true
        formScroll.drawsBackground = false
        formScroll.hasVerticalScroller = true
        formScroll.autohidesScrollers = true
        formScroll.documentView = form
        formScroll.isHidden = true
        addSubview(formScroll)
        search.placeholderString = "Filter"
        search.font = renderer.font
        search.delegate = self
        // The cancel button inside the field empties it without a text change notification.
        search.target = self
        search.action = #selector(searched(_:))
        addSubview(search)
    }

    required init?(coder: NSCoder) {
        nil
    }

    override var isFlipped: Bool {
        true
    }

    override var acceptsFirstResponder: Bool {
        true
    }

    override func draw(_ dirtyRect: NSRect) {
        guard let context = NSGraphicsContext.current?.cgContext, let model else {
            return
        }

        renderer.draw(model, in: context, size: bounds.size)
        formScroll.isHidden = !model.isForm
        if model.isForm {
            formScroll.frame = renderer.bodyRect
            form.apply(model.fields)
            let visible = formScroll.contentSize
            form.frame.size = NSSize(width: visible.width, height: max(form.contentHeight, visible.height))
        }

        // A form page has no filter box, and a hidden box must not keep the keyboard.
        if model.isForm && search.currentEditor() != nil {
            window?.makeFirstResponder(self)
        }

        search.isHidden = model.isForm
        let searchRect = renderer.searchRect(size: bounds.size)
        if search.frame != searchRect {
            search.frame = searchRect
        }

        if search.currentEditor() == nil && search.stringValue != model.search {
            search.stringValue = model.search
        }

        search.toolTip = model.searchTooltip.isEmpty ? nil : model.searchTooltip
    }

    /// The hover texts of the frame last drawn, as tool tip regions, asked for after every present
    /// because a poll moves the rows under them, and a region left behind would describe the row
    /// that used to be there. Registered again only when they changed: rebuilt on every draw, a
    /// spinner's turns restarted the delay under a resting cursor. See `ToolTipRegions`.
    func refreshToolTips() {
        toolTips.apply(renderer.tips, to: self)
    }

    @objc private func searched(_ sender: NSSearchField) {
        queueSearch()
    }

    private func queueSearch() {
        runtime?.queueEdit(field: Int(BM_SEARCH_FIELD.rawValue), value: search.stringValue)
    }

    override func mouseDown(with event: NSEvent) {
        guard let runtime else {
            return
        }

        let point = convert(event.locationInWindow, from: nil)
        if let index = renderer.buttonRects.firstIndex(where: { $0.contains(point) }) {
            runtime.input.clickedButton = Int32(index)
            return
        }

        if renderer.statusRect.contains(point) {
            runtime.input.key = Int32(BM_KEY_COPY_STATUS.rawValue)
            return
        }

        guard let model, !model.isForm else {
            return
        }

        if let hit = renderer.chips.first(where: { $0.rect.contains(point) }) {
            if hit.overflow {
                runtime.input.clickedOverflowRow = Int32(hit.row)
                runtime.input.overflowFrom = hit.chip
            } else {
                runtime.input.clickedChipRow = Int32(hit.row)
                runtime.input.clickedChip = hit.chip
            }

            return
        }

        // A click on a group toggles it, so the second press of a double click is dropped, or it
        // would close what the first opened.
        if let index = renderer.rowRects.firstIndex(where: { $0.contains(point) }),
           !(event.clickCount > 1 && index < model.rows.count && model.rows[index].isGroup) {
            runtime.input.clickedRow = Int32(index)
        }
    }

    override func rightMouseDown(with event: NSEvent) {
        guard let runtime, let model, !model.isForm else {
            return
        }

        let point = convert(event.locationInWindow, from: nil)
        if let index = renderer.rowRects.firstIndex(where: { $0.contains(point) }) {
            runtime.input.rightClickedRow = Int32(index)
        }
    }

    override func scrollWheel(with event: NSEvent) {
        guard let runtime, let model, !model.isForm else {
            super.scrollWheel(with: event)
            return
        }

        runtime.wheel(event, rowHeight: renderer.rowHeight)
    }

    override func keyDown(with event: NSEvent) {
        guard let runtime, let model else {
            return
        }

        let command = event.modifierFlags.contains(.command)
        let shift = event.modifierFlags.contains(.shift)
        let key: BmKey
        switch event.keyCode {
        case 126: key = BM_KEY_PREVIOUS_ROW
        case 125: key = BM_KEY_NEXT_ROW
        case 116: key = BM_KEY_PAGE_UP
        case 121: key = BM_KEY_PAGE_DOWN
        case 115: key = BM_KEY_HOME
        case 119: key = BM_KEY_END
        case 53: key = model.isForm ? BM_KEY_BACK : BM_KEY_HIDE
        case 36, 76: key = model.isForm ? BM_KEY_NONE : BM_KEY_OPEN_BUILD
        case 96: key = BM_KEY_REFRESH
        // Shift+F10, the keyboard's way to a context menu, as on the other heads.
        case 109 where shift: key = model.isForm ? BM_KEY_NONE : BM_KEY_OPEN_MENU
        default:
            // The characters keep what Shift does, so Shift+Cmd+R reads as "R".
            let characters = event.charactersIgnoringModifiers ?? ""
            if command && characters == "f" && !model.isForm {
                window?.makeFirstResponder(search)
                return
            }

            if command && characters == "q" { key = BM_KEY_QUIT }
            else if command && characters == "c" && !model.isForm { key = BM_KEY_COPY }
            // Shift+Cmd+R, since Cmd+R refreshes. Never R alone: typed into the rows by someone who
            // took the filter box to have the keyboard, it reran the build.
            else if command && shift && characters.lowercased() == "r" && !model.isForm { key = BM_KEY_RETRY }
            else if command && characters == "r" { key = BM_KEY_REFRESH }
            else if command && characters == "." && !model.isForm { key = BM_KEY_CANCEL_BUILD }
            else if command && characters == "l" && !model.isForm { key = BM_KEY_COPY_LOG }
            else if command && characters == "t" && !model.isForm { key = BM_KEY_TRIAGE }
            else {
                super.keyDown(with: event)
                return
            }
        }

        runtime.input.key = Int32(key.rawValue)
    }
}

extension MonitorView: NSSearchFieldDelegate {
    func controlTextDidChange(_ notification: Notification) {
        queueSearch()
    }

    /// The keys that move through the rows still reach them from the box, so a filter can be typed
    /// and its match opened without leaving it. Escape empties a box with text in it, and only once
    /// it is empty hides the window, as it does from the rows.
    func control(_ control: NSControl, textView: NSTextView, doCommandBy commandSelector: Selector) -> Bool {
        guard let runtime else {
            return false
        }

        switch commandSelector {
        case #selector(NSResponder.moveUp(_:)):
            runtime.input.key = Int32(BM_KEY_PREVIOUS_ROW.rawValue)
        case #selector(NSResponder.moveDown(_:)):
            runtime.input.key = Int32(BM_KEY_NEXT_ROW.rawValue)
        case #selector(NSResponder.insertNewline(_:)):
            runtime.input.key = Int32(BM_KEY_OPEN_BUILD.rawValue)
        case #selector(NSResponder.cancelOperation(_:)):
            if search.stringValue.isEmpty {
                runtime.input.key = Int32(BM_KEY_HIDE.rawValue)
            } else {
                search.stringValue = ""
                queueSearch()
            }
        default:
            return false
        }

        return true
    }
}
