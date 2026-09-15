import AppKit
import CBm
import CoreGraphics
import Foundation

/// Draws the builds page and the footer into any CGContext: the window's, or the offscreen bitmap
/// a capture makes. Also remembers where it put each clickable thing, so the view can hit test
/// the last frame drawn. Coordinates are top-left origin; the view flips.
///
/// Heights come from the font's line height and cell widths from measured text, the way the
/// WinForms canvas sizes itself, rather than from fixed points. The size is whatever the managed
/// side hands bm_init, and cells fixed for one size crowd or clip the text at a larger one.
final class BuildsRenderer {
    /// A chip, the provider icon, or an overflow chip, which carries the first of the chips it
    /// stands in for.
    struct Hit {
        var row: Int
        var chip: Int32
        var overflow: Bool
        var rect: CGRect
    }

    let padding: CGFloat = 12
    /// Between the cells of a row.
    let gap: CGFloat = 10
    let chipPadding: CGFloat = 8
    let chipGap: CGFloat = 6
    let iconSize: CGFloat = 16
    /// Stands in for the chips a row has no room for, and opens the drop down that holds them.
    let overflowLabel = "…"
    var rightInset: CGFloat = 0

    let font: NSFont
    let smallFont: NSFont
    let lineHeight: CGFloat
    let rowHeight: CGFloat
    let headerHeight: CGFloat
    let footerHeight: CGFloat
    let chipHeight: CGFloat
    let buttonHeight: CGFloat

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
        // Whole points, so the rows and their status squares land on pixel edges.
        lineHeight = (font.ascender - font.descender + font.leading).rounded(.up)
        rowHeight = lineHeight + 14
        headerHeight = lineHeight + 14
        footerHeight = lineHeight + 28
        chipHeight = lineHeight + 2
        buttonHeight = lineHeight + 10
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

        drawText(frame.isForm ? frame.formTitle : frame.header, at: CGPoint(x: padding, y: (headerHeight - lineHeight) / 2), font: font, colour: Palette.dim)
        Palette.border.setFill()
        CGRect(x: 0, y: headerHeight - 1, width: size.width, height: 1).fill()

        if !frame.isForm {
            drawRows(frame)
        } else if staticForm {
            // Clipped to the body, so fields past the bottom are cut off the way the window's
            // scroll view cuts them off, rather than drawn over the footer.
            context.saveGState()
            context.clip(to: bodyRect)
            drawForm(frame)
            context.restoreGState()
        }

        drawFooter(frame, size: size)
        NSGraphicsContext.restoreGraphicsState()
    }

    private func drawRows(_ frame: Frame) {
        if frame.rows.isEmpty {
            // Where the first row would be.
            let line = CGRect(x: 0, y: bodyRect.minY + gap, width: bodyRect.width, height: rowHeight)
            var x = gap
            if frame.loading {
                // An arc turning once a second, from the clock: the window is drawn every frame anyway.
                let size: CGFloat = 18
                let angle = CGFloat(Date().timeIntervalSinceReferenceDate.truncatingRemainder(dividingBy: 1)) * 360
                let path = NSBezierPath()
                path.appendArc(withCenter: CGPoint(x: x + size / 2, y: line.midY), radius: size / 2, startAngle: angle, endAngle: angle + 270)
                path.lineWidth = 2.5
                Palette.dim.setStroke()
                path.stroke()
                x += size + gap
            }

            drawText(frame.loading ? "Loading builds" : "Nothing to show yet.", at: CGPoint(x: x, y: line.midY - lineHeight / 2), font: font, colour: Palette.dim)
            return
        }

        let width = bodyRect.width
        let barWidth: CGFloat = 110
        // Measured rather than fixed, so each cell holds its widest text at whatever size the font
        // is: a countdown past an hour, and the widest set of chips a row carries.
        let timingWidth = measure("0:00:00 left")
        let widestChips = ["Build", "Branch", "PR 9999", "Retry", "Copy log"].map(chipWidth).reduce(0, +) + 4 * chipGap
        let overflowWidth = chipWidth(overflowLabel)
        // Reserved on every row once any row has an icon, so a group's row, which has none, keeps its
        // name in line with the rows under it.
        let iconWidth: CGFloat = frame.rows.contains { !$0.provider.isEmpty } ? iconSize + gap : 0
        let textX = rowHeight + gap
        // What the name, the detail and the chips share: the row less the square, the bar, the
        // timing and a gap after each of the five cells.
        let available = width - textX - barWidth - timingWidth - 5 * gap
        // As wide as the widest name across every row, not only those on screen, so it does not shift
        // while scrolling; the detail likewise, up to forty characters, past which a long pipeline or
        // branch is cut short rather than pushing every row's chips into the drop down.
        let nameWanted = (frame.names + frame.groupNames.map { "▾ " + $0 })
            .map { measure($0).rounded(.up) }
            .max() ?? 0
        let detailWanted = iconWidth + min(
            frame.details.map { measure($0).rounded(.up) }.max() ?? 0,
            measure(String(repeating: "0", count: 40)))
        // The chips give way first: a row without room for all of them puts the last behind an
        // overflow chip, rather than the names being cut short. Only once no chip but that one fits
        // do the names shrink.
        let spare = available - nameWanted - detailWanted
        let chipsWidth = spare >= widestChips ? widestChips : max(overflowWidth, spare)
        let names = max(120, available - chipsWidth)
        let nameWidth = min(max(nameWanted, 40), max(40, names - min(120, detailWanted)))
        let detailWidth = names - nameWidth

        for (index, row) in frame.rows.enumerated() {
            let rect = CGRect(x: 0, y: bodyRect.minY + CGFloat(index) * rowHeight, width: width, height: rowHeight)
            rowRects.append(rect)
            if row.isSelected {
                Palette.selectedRow.setFill()
                rect.fill()
            }

            let textY = rect.midY - lineHeight / 2
            let colour = Palette.status(row.status)
            colour.setFill()
            // The full height of the row and flush with its neighbours, so a run of rows in one status
            // reads as one block rather than a column of dots.
            CGRect(x: rect.minX, y: rect.minY, width: rowHeight, height: rowHeight).fill()

            var x = textX
            drawText(displayName(row), at: CGPoint(x: x, y: textY), font: font, colour: Palette.text, width: nameWidth)
            x += nameWidth + gap
            // The logo leads the detail cell, beside the pipeline it ran, so a group's members, whose
            // first cell is empty, still show which service each came from.
            if let icon = RowIcons.images[row.provider] {
                let iconRect = CGRect(x: x, y: rect.midY - iconSize / 2, width: iconSize, height: iconSize)
                icon.draw(in: iconRect, from: .zero, operation: .sourceOver, fraction: 1, respectFlipped: true, hints: nil)
                chips.append(Hit(row: index, chip: Int32(BM_CHIP_PROJECT.rawValue), overflow: false, rect: iconRect))
            }

            drawText(row.detail, at: CGPoint(x: x + iconWidth, y: textY), font: font, colour: Palette.dim, width: detailWidth - iconWidth)
            x += detailWidth + gap
            if row.progress >= 0 {
                let track = CGRect(x: x, y: rect.midY - 4, width: barWidth, height: 8)
                Palette.barTrack.setFill()
                NSBezierPath(roundedRect: track, xRadius: 4, yRadius: 4).fill()
                let filled = CGRect(x: track.minX, y: track.minY, width: track.width * CGFloat(min(1, max(0, row.progress))), height: track.height)
                Palette.status(Int32(BM_STATUS_RUNNING.rawValue)).setFill()
                NSBezierPath(roundedRect: filled, xRadius: 4, yRadius: 4).fill()
            }

            x += barWidth + gap
            drawText(row.timing, at: CGPoint(x: x, y: textY), font: font, colour: Palette.dim, width: timingWidth)
            x += timingWidth + gap
            drawChips(row, index: index, from: x, to: x + chipsWidth, rowRect: rect, overflowWidth: overflowWidth)
        }
    }

    /// The chips that fit, from the left, then an overflow chip in place of the rest. A chip is drawn
    /// only with room after it for the overflow chip, unless it is the last, so the overflow chip
    /// always fits where the first chip that did not would have gone.
    private func drawChips(_ row: Frame.Row, index: Int, from start: CGFloat, to right: CGFloat, rowRect: CGRect, overflowWidth: CGFloat) {
        var x = start
        for (position, chip) in row.chips.enumerated() {
            let reserve = position == row.chips.count - 1 ? 0 : chipGap + overflowWidth
            if x + chipWidth(chip.label) + reserve > right {
                let chipRect = drawChip(overflowLabel, x: x, rowRect: rowRect, fill: Palette.chip, textColour: Palette.text)
                chips.append(Hit(row: index, chip: chip.kind, overflow: true, rect: chipRect))
                return
            }

            let (fill, textColour) = colours(chip.kind)
            let chipRect = drawChip(chip.label, x: x, rowRect: rowRect, fill: fill, textColour: textColour)
            chips.append(Hit(row: index, chip: chip.kind, overflow: false, rect: chipRect))
            x = chipRect.maxX + chipGap
        }
    }

    /// Retry and Cancel on colours of their own, links in the link colour, as the WinForms canvas
    /// draws them.
    private func colours(_ kind: Int32) -> (NSColor, NSColor) {
        switch UInt32(kind) {
        case BM_CHIP_RETRY.rawValue: return (Palette.retryChip, Palette.text)
        case BM_CHIP_CANCEL.rawValue: return (Palette.cancelChip, Palette.text)
        case BM_CHIP_COPY_LOG.rawValue: return (Palette.chip, Palette.text)
        default: return (Palette.chip, Palette.chipText)
        }
    }

    private func chipWidth(_ label: String) -> CGFloat {
        measure(label) + 2 * chipPadding
    }

    private func displayName(_ row: Frame.Row) -> String {
        row.isGroup ? (row.isExpanded ? "▾ " : "▸ ") + row.name : row.name
    }

    private func drawForm(_ frame: Frame) {
        // A box is a line of text with room above and below it. A field that is only text is
        // spaced tighter than one with a box.
        let fieldHeight = lineHeight + 4
        var y = bodyRect.minY + 12
        for field in frame.fields {
            let kind = UInt32(field.kind)
            let colour = field.enabled ? Palette.text : Palette.dim
            switch kind {
            case BM_FIELD_TEXT.rawValue, BM_FIELD_PASSWORD.rawValue, BM_FIELD_NUMBER.rawValue, BM_FIELD_SELECT.rawValue:
                drawText(field.label, at: CGPoint(x: padding, y: y + 4), font: font, colour: Palette.dim, width: 240)
                let width: CGFloat = kind == BM_FIELD_NUMBER.rawValue ? 100 : 420
                let box = CGRect(x: 260, y: y, width: width, height: fieldHeight)
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

                y += fieldHeight + 10
            case BM_FIELD_CHECKBOX.rawValue:
                let box = CGRect(x: padding, y: y + (fieldHeight - 14) / 2, width: 14, height: 14)
                Palette.surface.setFill()
                NSBezierPath(roundedRect: box, xRadius: 3, yRadius: 3).fill()
                if field.value == "true" {
                    drawText("✓", at: CGPoint(x: box.minX + 1, y: box.minY - 3), font: smallFont, colour: Palette.chipText)
                }

                drawText(field.label, at: CGPoint(x: padding + 22, y: y + 4), font: font, colour: colour)
                y += fieldHeight + 10
            case BM_FIELD_BUTTON.rawValue:
                let attributes: [NSAttributedString.Key: Any] = [.font: font, .foregroundColor: colour]
                let textSize = (field.label as NSString).size(withAttributes: attributes)
                let box = CGRect(x: padding, y: y, width: textSize.width + 24, height: fieldHeight)
                Palette.chip.setFill()
                NSBezierPath(roundedRect: box, xRadius: 4, yRadius: 4).fill()
                (field.label as NSString).draw(at: CGPoint(x: box.minX + 12, y: box.midY - textSize.height / 2), withAttributes: attributes)
                y += fieldHeight + 10
            case BM_FIELD_LINK.rawValue:
                drawText(field.label, at: CGPoint(x: padding, y: y + 4), font: font, colour: Palette.chipText)
                y += lineHeight + 8
            case BM_FIELD_LIST_ROW.rawValue:
                let line = field.label.isEmpty ? field.value : "\(field.label): \(field.value)"
                drawText("✕  " + line, at: CGPoint(x: padding, y: y + 4), font: font, colour: Palette.text)
                y += lineHeight + 8
            default:
                let line = field.label.isEmpty ? field.value : (field.value.isEmpty ? field.label : "\(field.label): \(field.value)")
                drawText(line, at: CGPoint(x: padding, y: y + 4), font: font, colour: field.id == "error" ? Palette.error : Palette.text, width: bodyRect.width - padding * 2)
                y += lineHeight + 8
            }
        }
    }

    /// In the row's font, as tall as a line of it, so a chip reads at the size of the row it sits in.
    private func drawChip(_ label: String, x: CGFloat, rowRect: CGRect, fill: NSColor, textColour: NSColor) -> CGRect {
        let attributes: [NSAttributedString.Key: Any] = [.font: font, .foregroundColor: textColour]
        let textSize = (label as NSString).size(withAttributes: attributes)
        let rect = CGRect(x: x, y: rowRect.midY - chipHeight / 2, width: textSize.width + 2 * chipPadding, height: chipHeight)
        fill.setFill()
        NSBezierPath(roundedRect: rect, xRadius: 4, yRadius: 4).fill()
        (label as NSString).draw(at: CGPoint(x: rect.minX + chipPadding, y: rect.midY - textSize.height / 2), withAttributes: attributes)
        return rect
    }

    private func drawFooter(_ frame: Frame, size: CGSize) {
        let top = size.height - footerHeight
        Palette.border.setFill()
        CGRect(x: 0, y: top, width: size.width, height: 1).fill()

        var x = padding
        for button in frame.buttons {
            let attributes: [NSAttributedString.Key: Any] = [.font: font, .foregroundColor: button.enabled ? Palette.text : Palette.dim]
            let textSize = (button.label as NSString).size(withAttributes: attributes)
            // One width for every short label, so a row of buttons lines up, and wider only for a
            // label that would not fit it.
            let rect = CGRect(x: x, y: top + (footerHeight - buttonHeight) / 2, width: max(90, textSize.width + 2 * chipPadding), height: buttonHeight)
            buttonRects.append(rect)
            (button.enabled ? Palette.chip : Palette.surface).setFill()
            NSBezierPath(roundedRect: rect, xRadius: 4, yRadius: 4).fill()
            (button.label as NSString).draw(
                at: CGPoint(x: rect.midX - textSize.width / 2, y: rect.midY - textSize.height / 2),
                withAttributes: attributes)
            x = rect.maxX + 8
        }

        let attributes: [NSAttributedString.Key: Any] = [.font: smallFont, .foregroundColor: Palette.dim]
        let statusSize = (frame.status as NSString).size(withAttributes: attributes)
        (frame.status as NSString).draw(
            at: CGPoint(x: size.width - statusSize.width - padding, y: top + footerHeight / 2 - statusSize.height / 2),
            withAttributes: attributes)
    }

    private func measure(_ text: String) -> CGFloat {
        (text as NSString).size(withAttributes: [.font: font]).width
    }

    private func drawText(_ text: String, at point: CGPoint, font: NSFont, colour: NSColor, width: CGFloat = .greatestFiniteMagnitude) {
        let paragraph = NSMutableParagraphStyle()
        paragraph.lineBreakMode = .byTruncatingTail
        let attributes: [NSAttributedString.Key: Any] = [.font: font, .foregroundColor: colour, .paragraphStyle: paragraph]
        let rect = CGRect(x: point.x, y: point.y, width: width, height: font.pointSize * 1.5)
        (text as NSString).draw(with: rect, options: [.usesLineFragmentOrigin], attributes: attributes)
    }
}
