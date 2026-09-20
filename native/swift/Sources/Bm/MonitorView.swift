import AppKit
import CBm

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
        applyTooltips()
    }

    /// The hover texts of the frame just drawn, as tool tip rects. Rebuilt on every draw because a
    /// poll moves the rows under them, and a rect left behind would describe the row that used to
    /// be there. AppKit owns the delay, which is the system's and not ours to set.
    private func applyTooltips() {
        removeAllToolTips()
        for tip in renderer.tips {
            addToolTipRect(tip.rect, owner: tip.text as NSString, userData: nil)
        }
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

        let delta = event.hasPreciseScrollingDeltas ? event.scrollingDeltaY / renderer.rowHeight : event.scrollingDeltaY
        runtime.wheel(delta)
    }

    override func keyDown(with event: NSEvent) {
        guard let runtime, let model else {
            return
        }

        let command = event.modifierFlags.contains(.command)
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
        default:
            let characters = event.charactersIgnoringModifiers ?? ""
            if command && characters == "f" && !model.isForm {
                window?.makeFirstResponder(search)
                return
            }

            if command && characters == "q" { key = BM_KEY_QUIT }
            else if command && characters == "c" && !model.isForm { key = BM_KEY_COPY }
            else if command && characters == "r" { key = BM_KEY_REFRESH }
            else if !command && characters == "r" && !model.isForm { key = BM_KEY_RETRY }
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
