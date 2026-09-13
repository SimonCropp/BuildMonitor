import AppKit
import CBm

/// The form pages, out of real controls so they get the platform's editing, focus ring and
/// VoiceOver. Rebuilt only when the sequence of field ids changes; otherwise values are pushed
/// into the existing controls, never into the one being edited.
final class FormView: NSView {
    private let runtime: Runtime
    private var shape: [String] = []
    private var controls: [NSView] = []
    private var editing: String?

    init(runtime: Runtime) {
        self.runtime = runtime
        super.init(frame: .zero)
        wantsLayer = true
        layer?.backgroundColor = Palette.background.cgColor
    }

    required init?(coder: NSCoder) {
        nil
    }

    override var isFlipped: Bool {
        true
    }

    /// Labels and links hold their colours, so the next apply rebuilds every control.
    func retheme() {
        layer?.backgroundColor = Palette.background.cgColor
        shape = []
    }

    func apply(_ fields: [Frame.Field]) {
        let newShape = fields.map { "\($0.kind):\($0.id)" }
        if newShape != shape {
            shape = newShape
            rebuild(fields)
        }

        for (index, field) in fields.enumerated() {
            update(controls[index], field: field, index: index)
        }
    }

    private func rebuild(_ fields: [Frame.Field]) {
        subviews.forEach { $0.removeFromSuperview() }
        controls.removeAll()
        var y: CGFloat = 12
        for (index, field) in fields.enumerated() {
            let control = make(field, index: index)
            let height: CGFloat = field.kind == Int32(BM_FIELD_LABEL.rawValue) ? 22 : 28
            if [BM_FIELD_TEXT, BM_FIELD_PASSWORD, BM_FIELD_NUMBER, BM_FIELD_SELECT].map({ Int32($0.rawValue) }).contains(field.kind) {
                let label = NSTextField(labelWithString: field.label)
                label.textColor = Palette.dim
                label.frame = NSRect(x: 12, y: y + 4, width: 240, height: 20)
                label.lineBreakMode = .byTruncatingTail
                addSubview(label)
                control.frame = NSRect(x: 260, y: y, width: field.kind == Int32(BM_FIELD_NUMBER.rawValue) ? 100 : 420, height: 24)
            } else {
                control.frame = NSRect(x: 12, y: y, width: 620, height: 24)
            }

            addSubview(control)
            controls.append(control)
            y += height + 6
        }
    }

    private func make(_ field: Frame.Field, index: Int) -> NSView {
        switch UInt32(field.kind) {
        case BM_FIELD_CHECKBOX.rawValue:
            let box = NSButton(checkboxWithTitle: field.label, target: self, action: #selector(toggled(_:)))
            box.tag = index
            return box
        case BM_FIELD_TEXT.rawValue, BM_FIELD_NUMBER.rawValue:
            let text = NSTextField()
            text.tag = index
            text.delegate = self
            text.placeholderString = field.hint
            return text
        case BM_FIELD_PASSWORD.rawValue:
            let text = NSSecureTextField()
            text.tag = index
            text.delegate = self
            text.placeholderString = field.hint
            return text
        case BM_FIELD_SELECT.rawValue:
            let popup = NSPopUpButton(frame: .zero, pullsDown: false)
            popup.tag = index
            popup.target = self
            popup.action = #selector(selected(_:))
            return popup
        case BM_FIELD_BUTTON.rawValue:
            let button = NSButton(title: field.label, target: self, action: #selector(clicked(_:)))
            button.tag = index
            button.bezelStyle = .rounded
            return button
        case BM_FIELD_LINK.rawValue:
            let button = NSButton(title: field.label, target: self, action: #selector(clicked(_:)))
            button.tag = index
            button.isBordered = false
            button.contentTintColor = Palette.chipText
            return button
        case BM_FIELD_LIST_ROW.rawValue:
            let button = NSButton(title: "✕  " + (field.label.isEmpty ? field.value : "\(field.label): \(field.value)"), target: self, action: #selector(clicked(_:)))
            button.tag = index
            button.isBordered = false
            button.alignment = .left
            button.contentTintColor = Palette.text
            return button
        default:
            let label = NSTextField(wrappingLabelWithString: "")
            label.tag = index
            return label
        }
    }

    private func update(_ control: NSView, field: Frame.Field, index: Int) {
        if let control = control as? NSControl {
            control.isEnabled = field.enabled || field.kind == Int32(BM_FIELD_LABEL.rawValue) || field.kind == Int32(BM_FIELD_LINK.rawValue)
        }

        switch UInt32(field.kind) {
        case BM_FIELD_CHECKBOX.rawValue:
            (control as? NSButton)?.state = field.value == "true" ? .on : .off
        case BM_FIELD_TEXT.rawValue, BM_FIELD_NUMBER.rawValue, BM_FIELD_PASSWORD.rawValue:
            if let text = control as? NSTextField, editing != field.id, text.stringValue != field.value {
                text.stringValue = field.value
            }
        case BM_FIELD_SELECT.rawValue:
            if let popup = control as? NSPopUpButton {
                if popup.itemTitles != field.options {
                    popup.removeAllItems()
                    popup.addItems(withTitles: field.options)
                }

                popup.selectItem(withTitle: field.value)
            }
        case BM_FIELD_LABEL.rawValue:
            if let label = control as? NSTextField {
                label.stringValue = field.label.isEmpty ? field.value : (field.value.isEmpty ? field.label : "\(field.label): \(field.value)")
                label.textColor = field.id == "error" ? Palette.error : Palette.text
            }
        default:
            break
        }
    }

    @objc private func toggled(_ sender: NSButton) {
        runtime.queueEdit(field: sender.tag, value: sender.state == .on ? "true" : "false")
    }

    @objc private func selected(_ sender: NSPopUpButton) {
        runtime.queueEdit(field: sender.tag, value: sender.titleOfSelectedItem ?? "")
    }

    @objc private func clicked(_ sender: NSButton) {
        runtime.input.clickedField = Int32(sender.tag)
    }
}

extension FormView: NSTextFieldDelegate {
    func controlTextDidBeginEditing(_ notification: Notification) {
        guard let text = notification.object as? NSTextField, text.tag < shape.count else {
            return
        }

        editing = String(shape[text.tag].split(separator: ":", maxSplits: 1).last ?? "")
    }

    func controlTextDidChange(_ notification: Notification) {
        guard let text = notification.object as? NSTextField else {
            return
        }

        runtime.queueEdit(field: text.tag, value: text.stringValue)
    }

    func controlTextDidEndEditing(_ notification: Notification) {
        editing = nil
    }
}
