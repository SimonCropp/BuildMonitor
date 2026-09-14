import AppKit
import CBm

/// The window's content: the builds page and footer drawn by the renderer, with the form view
/// laid over the body while a form page is up.
final class MonitorView: NSView {
    let renderer: BuildsRenderer
    let form: FormView
    var model: Frame?
    private weak var runtime: Runtime?

    /// Holds the form, so a page with more fields than the body fits scrolls instead of hiding its
    /// last controls behind the footer where they cannot be reached.
    private let formScroll = NSScrollView()

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

        guard let model, !model.isForm else {
            return
        }

        if let hit = renderer.chips.first(where: { $0.rect.contains(point) }) {
            if hit.link != Int32(BM_LINK_NONE.rawValue) {
                runtime.input.clickedLinkRow = Int32(hit.row)
                runtime.input.clickedLink = hit.link
            } else {
                runtime.input.clickedActionRow = Int32(hit.row)
                runtime.input.clickedAction = hit.action
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
