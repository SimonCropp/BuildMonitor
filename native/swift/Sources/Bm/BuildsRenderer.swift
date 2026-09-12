import AppKit
import CBm
import CoreGraphics
import Foundation

/// Draws the builds page and the footer into any CGContext: the window's, or the offscreen bitmap
/// a capture makes. Also remembers where it put each clickable thing, so the view can hit test
/// the last frame drawn. Coordinates are top-left origin; the view flips.
final class BuildsRenderer {
    struct Hit {
        var row: Int
        var link: Int32
        var action: Int32
        var rect: CGRect
    }

    let rowHeight: CGFloat = 30
    let headerHeight: CGFloat = 28
    let footerHeight: CGFloat = 44
    let padding: CGFloat = 12
    var rightInset: CGFloat = 0

    let font: NSFont
    let smallFont: NSFont

    private(set) var rowRects: [CGRect] = []
    private(set) var chips: [Hit] = []
    private(set) var buttonRects: [CGRect] = []
    private(set) var bodyRect = CGRect.zero

    init(fontData: Data?, size: CGFloat) {
        var loaded: NSFont?
        if let fontData,
           let provider = CGDataProvider(data: fontData as CFData),
           let graphicsFont = CGFont(provider) {
            loaded = CTFontCreateWithGraphicsFont(graphicsFont, size, nil, nil) as NSFont
        }

        font = loaded ?? NSFont.systemFont(ofSize: size)
        smallFont = NSFont(descriptor: font.fontDescriptor, size: size - 2) ?? font
    }

    var bodyRows: Int {
        max(1, Int(bodyRect.height / rowHeight))
    }

    func layout(size: CGSize) {
        bodyRect = CGRect(
            x: 0,
            y: headerHeight,
            width: size.width - rightInset,
            height: max(0, size.height - headerHeight - footerHeight))
    }

    /// `staticForm` draws a form page as text and boxes. The window lays real controls over the
    /// body instead, so it passes false; a capture has no window and passes true, which is what
    /// makes a form snapshot show something.
    func draw(_ frame: Frame, in context: CGContext, size: CGSize, staticForm: Bool = false) {
        layout(size: size)
        rowRects.removeAll()
        chips.removeAll()
        buttonRects.removeAll()

        context.setFillColor(Palette.background.cgColor)
        context.fill(CGRect(origin: .zero, size: size))

        NSGraphicsContext.saveGraphicsState()
        let graphics = NSGraphicsContext(cgContext: context, flipped: true)
        NSGraphicsContext.current = graphics

        drawText(frame.isForm ? frame.formTitle : frame.header, at: CGPoint(x: padding, y: 6), font: font, colour: Palette.dim)
        Palette.border.setFill()
        CGRect(x: 0, y: headerHeight - 1, width: size.width, height: 1).fill()

        if !frame.isForm {
            drawRows(frame)
        } else if staticForm {
            drawForm(frame)
        }

        drawFooter(frame, size: size)
        NSGraphicsContext.restoreGraphicsState()
    }

    private func drawRows(_ frame: Frame) {
        if frame.rows.isEmpty {
            drawText("Nothing to show yet.", at: CGPoint(x: padding, y: bodyRect.minY + 8), font: font, colour: Palette.dim)
            return
        }

        let width = bodyRect.width
        let actionsWidth: CGFloat = 130
        let linksWidth: CGFloat = 210
        let timingWidth: CGFloat = 100
        let barWidth: CGFloat = 110
        let statusWidth: CGFloat = 90
        let runWidth: CGFloat = 60
        let textWidth = max(120, width - padding * 2 - actionsWidth - linksWidth - timingWidth - barWidth - statusWidth - runWidth - 24)
        let pipelineWidth = textWidth * 0.45
        let repoWidth = textWidth - pipelineWidth

        for (index, row) in frame.rows.enumerated() {
            let rect = CGRect(x: 0, y: bodyRect.minY + CGFloat(index) * rowHeight, width: width, height: rowHeight)
            rowRects.append(rect)
            if row.isSelected {
                Palette.selectedRow.setFill()
                rect.fill()
            } else if row.isHeader {
                Palette.headerRow.setFill()
                rect.fill()
            }

            let textY = rect.minY + (rowHeight - font.pointSize * 1.3) / 2
            if row.isHeader {
                let label = (row.isFolded ? "▸ " : "▾ ") + row.pipeline
                drawText(label, at: CGPoint(x: padding, y: textY), font: font, colour: Palette.dim)
                continue
            }

            let colour = Palette.status(row.status)
            colour.setFill()
            NSBezierPath(ovalIn: CGRect(x: padding, y: rect.midY - 5, width: 10, height: 10)).fill()

            var x = padding + 20
            drawText(row.pipeline, at: CGPoint(x: x, y: textY), font: font, colour: Palette.text, width: pipelineWidth - 8)
            x += pipelineWidth
            drawText(row.repoBranch, at: CGPoint(x: x, y: textY), font: font, colour: Palette.dim, width: repoWidth - 8)
            x += repoWidth
            drawText(row.runNumber, at: CGPoint(x: x, y: textY), font: font, colour: Palette.dim, width: runWidth)
            x += runWidth
            drawText(row.statusText, at: CGPoint(x: x, y: textY), font: font, colour: colour, width: statusWidth)
            x += statusWidth
            if row.progress >= 0 {
                let track = CGRect(x: x, y: rect.midY - 4, width: barWidth - 10, height: 8)
                Palette.barTrack.setFill()
                NSBezierPath(roundedRect: track, xRadius: 4, yRadius: 4).fill()
                let filled = CGRect(x: track.minX, y: track.minY, width: track.width * CGFloat(min(1, max(0, row.progress))), height: track.height)
                Palette.status(Int32(BM_STATUS_RUNNING.rawValue)).setFill()
                NSBezierPath(roundedRect: filled, xRadius: 4, yRadius: 4).fill()
            }

            x += barWidth
            drawText(row.timing, at: CGPoint(x: x, y: textY), font: font, colour: Palette.dim, width: timingWidth)
            x += timingWidth

            var chipX = x
            for (label, link) in [(row.buildLabel, BM_LINK_BUILD), (row.branchLabel, BM_LINK_BRANCH), (row.pullRequestLabel, BM_LINK_PULL_REQUEST)] where !label.isEmpty {
                let chipRect = drawChip(label, x: chipX, rowRect: rect, fill: Palette.chip, textColour: Palette.chipText)
                chips.append(Hit(row: index, link: Int32(link.rawValue), action: Int32(BM_ACTION_NONE.rawValue), rect: chipRect))
                chipX += chipRect.width + 6
            }

            x += linksWidth
            chipX = x
            if row.canRetry {
                let chipRect = drawChip("Retry", x: chipX, rowRect: rect, fill: Palette.retryChip, textColour: Palette.text)
                chips.append(Hit(row: index, link: Int32(BM_LINK_NONE.rawValue), action: Int32(BM_ACTION_RETRY.rawValue), rect: chipRect))
                chipX += chipRect.width + 6
            }

            if row.canCancel {
                let chipRect = drawChip("Cancel", x: chipX, rowRect: rect, fill: Palette.cancelChip, textColour: Palette.text)
                chips.append(Hit(row: index, link: Int32(BM_LINK_NONE.rawValue), action: Int32(BM_ACTION_CANCEL.rawValue), rect: chipRect))
            }
        }
    }

    private func drawForm(_ frame: Frame) {
        var y = bodyRect.minY + 12
        for field in frame.fields {
            let kind = UInt32(field.kind)
            let colour = field.enabled ? Palette.text : Palette.dim
            switch kind {
            case BM_FIELD_TEXT.rawValue, BM_FIELD_PASSWORD.rawValue, BM_FIELD_NUMBER.rawValue, BM_FIELD_SELECT.rawValue:
                drawText(field.label, at: CGPoint(x: padding, y: y + 4), font: font, colour: Palette.dim, width: 240)
                let width: CGFloat = kind == BM_FIELD_NUMBER.rawValue ? 100 : 420
                let box = CGRect(x: 260, y: y, width: width, height: 24)
                Palette.surface.setFill()
                NSBezierPath(roundedRect: box, xRadius: 4, yRadius: 4).fill()
                var shown = field.value
                if kind == BM_FIELD_PASSWORD.rawValue {
                    shown = String(repeating: "•", count: field.value.count)
                }

                if shown.isEmpty {
                    drawText(field.hint, at: CGPoint(x: box.minX + 8, y: y + 4), font: font, colour: Palette.dim, width: width - 16)
                } else {
                    drawText(shown, at: CGPoint(x: box.minX + 8, y: y + 4), font: font, colour: colour, width: width - 16)
                }

                if kind == BM_FIELD_SELECT.rawValue {
                    drawText("▾", at: CGPoint(x: box.maxX - 20, y: y + 4), font: font, colour: Palette.dim)
                }

                y += 34
            case BM_FIELD_CHECKBOX.rawValue:
                let box = CGRect(x: padding, y: y + 5, width: 14, height: 14)
                Palette.surface.setFill()
                NSBezierPath(roundedRect: box, xRadius: 3, yRadius: 3).fill()
                if field.value == "true" {
                    drawText("✓", at: CGPoint(x: box.minX + 1, y: y + 2), font: smallFont, colour: Palette.chipText)
                }

                drawText(field.label, at: CGPoint(x: padding + 22, y: y + 4), font: font, colour: colour)
                y += 34
            case BM_FIELD_BUTTON.rawValue:
                let attributes: [NSAttributedString.Key: Any] = [.font: font, .foregroundColor: colour]
                let textSize = (field.label as NSString).size(withAttributes: attributes)
                let box = CGRect(x: padding, y: y, width: textSize.width + 24, height: 24)
                Palette.chip.setFill()
                NSBezierPath(roundedRect: box, xRadius: 4, yRadius: 4).fill()
                (field.label as NSString).draw(at: CGPoint(x: box.minX + 12, y: box.midY - textSize.height / 2), withAttributes: attributes)
                y += 34
            case BM_FIELD_LINK.rawValue:
                drawText(field.label, at: CGPoint(x: padding, y: y + 4), font: font, colour: Palette.chipText)
                y += 28
            case BM_FIELD_LIST_ROW.rawValue:
                let line = field.label.isEmpty ? field.value : "\(field.label): \(field.value)"
                drawText("✕  " + line, at: CGPoint(x: padding, y: y + 4), font: font, colour: Palette.text)
                y += 28
            default:
                let line = field.label.isEmpty ? field.value : (field.value.isEmpty ? field.label : "\(field.label): \(field.value)")
                drawText(line, at: CGPoint(x: padding, y: y + 4), font: font, colour: field.id == "error" ? Palette.error : Palette.text, width: bodyRect.width - padding * 2)
                y += 28
            }
        }
    }

    private func drawChip(_ label: String, x: CGFloat, rowRect: CGRect, fill: NSColor, textColour: NSColor) -> CGRect {
        let attributes: [NSAttributedString.Key: Any] = [.font: smallFont, .foregroundColor: textColour]
        let textSize = (label as NSString).size(withAttributes: attributes)
        let rect = CGRect(x: x, y: rowRect.midY - 10, width: textSize.width + 14, height: 20)
        fill.setFill()
        NSBezierPath(roundedRect: rect, xRadius: 4, yRadius: 4).fill()
        (label as NSString).draw(at: CGPoint(x: rect.minX + 7, y: rect.minY + (20 - textSize.height) / 2), withAttributes: attributes)
        return rect
    }

    private func drawFooter(_ frame: Frame, size: CGSize) {
        let top = size.height - footerHeight
        Palette.border.setFill()
        CGRect(x: 0, y: top, width: size.width, height: 1).fill()

        var x = padding
        for button in frame.buttons {
            let rect = CGRect(x: x, y: top + 10, width: 90, height: 24)
            buttonRects.append(rect)
            (button.enabled ? Palette.chip : Palette.surface).setFill()
            NSBezierPath(roundedRect: rect, xRadius: 4, yRadius: 4).fill()
            let attributes: [NSAttributedString.Key: Any] = [.font: font, .foregroundColor: button.enabled ? Palette.text : Palette.dim]
            let textSize = (button.label as NSString).size(withAttributes: attributes)
            (button.label as NSString).draw(
                at: CGPoint(x: rect.midX - textSize.width / 2, y: rect.midY - textSize.height / 2),
                withAttributes: attributes)
            x += 98
        }

        let attributes: [NSAttributedString.Key: Any] = [.font: smallFont, .foregroundColor: Palette.dim]
        let statusSize = (frame.status as NSString).size(withAttributes: attributes)
        (frame.status as NSString).draw(
            at: CGPoint(x: size.width - statusSize.width - padding, y: top + 22 - statusSize.height / 2),
            withAttributes: attributes)
    }

    private func drawText(_ text: String, at point: CGPoint, font: NSFont, colour: NSColor, width: CGFloat = .greatestFiniteMagnitude) {
        let paragraph = NSMutableParagraphStyle()
        paragraph.lineBreakMode = .byTruncatingTail
        let attributes: [NSAttributedString.Key: Any] = [.font: font, .foregroundColor: colour, .paragraphStyle: paragraph]
        let rect = CGRect(x: point.x, y: point.y, width: width, height: font.pointSize * 1.5)
        (text as NSString).draw(with: rect, options: [.usesLineFragmentOrigin], attributes: attributes)
    }
}
