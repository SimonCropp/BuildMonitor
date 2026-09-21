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
    /// A chip, a link in a row's text, the provider icon, or an overflow chip, which carries the
    /// first of the chips it stands in for.
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
    /// Between a chip's icon and the text after it, where it has both.
    let chipIconGap: CGFloat = 4
    /// The button beside a directory field's box. Matches FormView, which draws the same field
    /// with real controls for the window this one only captures.
    let browseLabel = "Browse"
    let browseWidth: CGFloat = 82
    let browseGap: CGFloat = 8
    /// The icon inside a chip, which is a pill the height of a line of text.
    let iconSize: CGFloat = 16
    /// The filter box at the right of the header.
    let searchWidth: CGFloat = 240
    /// Stands in for the chips a row has no room for, and opens the drop down that holds them.
    let overflowLabel = "…"
    var rightInset: CGFloat = 0

    let font: NSFont
    let smallFont: NSFont
    let lineHeight: CGFloat
    let rowHeight: CGFloat
    /// The square a row's two marks are drawn in: the host's before the name, and the provider's
    /// before the pipeline. Short of the row's height rather than the sixteen a chip's icon is,
    /// since the marks carry detail, a Jenkins butler's face and the play badge on the Actions
    /// mark, that a sixteen pixel square turned to a smudge.
    let logoSize: CGFloat
    let headerHeight: CGFloat
    let footerHeight: CGFloat
    let chipHeight: CGFloat
    let buttonHeight: CGFloat
    let searchHeight: CGFloat

    private(set) var rowRects: [CGRect] = []
    private(set) var chips: [Hit] = []
    private(set) var buttonRects: [CGRect] = []
    /// Where each hover text belongs, in the order drawn. The view registers these as tool tip
    /// rects; a later one wins where they overlap, so a cell's own text beats the row's.
    private(set) var tips: [(rect: CGRect, text: String)] = []
    // Where the footer status was drawn, which a click copies.
    private(set) var statusRect = CGRect.zero
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
        logoSize = rowHeight - 6
        headerHeight = lineHeight + 14
        footerHeight = lineHeight + 28
        chipHeight = lineHeight + 2
        buttonHeight = lineHeight + 10
        searchHeight = lineHeight + 6
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

    /// Where the filter box goes: the right of the header, centred in it. One rect for the search
    /// field the window lays there and for the box a capture draws in its place.
    func searchRect(size: CGSize) -> CGRect {
        CGRect(
            x: max(padding, size.width - padding - searchWidth),
            y: (headerHeight - searchHeight) / 2,
            width: searchWidth,
            height: searchHeight)
    }

    /// `staticControls` draws the filter box and a form page as text and boxes. The window lays real
    /// controls over them instead, so it passes false; a capture has no window and passes true, which
    /// is what makes a snapshot show them.
    func draw(_ frame: Frame, in context: CGContext, size: CGSize, staticControls: Bool = false) {
        layout(size: size)
        rowRects.removeAll()
        chips.removeAll()
        buttonRects.removeAll()
        tips.removeAll()

        context.setFillColor(Palette.background.cgColor)
        context.fill(CGRect(origin: .zero, size: size))

        NSGraphicsContext.saveGraphicsState()
        let graphics = NSGraphicsContext(cgContext: context, flipped: true)
        NSGraphicsContext.current = graphics

        // Short of the filter box on the builds page, so a long header runs out rather than under it.
        let search = searchRect(size: size)
        let headerWidth = frame.isForm ? size.width - 2 * padding : search.minX - gap - padding
        drawText(frame.isForm ? frame.formTitle : frame.header, at: CGPoint(x: padding, y: (headerHeight - lineHeight) / 2), font: font, colour: Palette.dim, width: max(0, headerWidth))
        if !frame.isForm && staticControls {
            drawSearch(frame.search, in: search)
        }

        Palette.border.setFill()
        CGRect(x: 0, y: headerHeight - 1, width: size.width, height: 1).fill()

        if !frame.isForm {
            drawRows(frame)
        } else if staticControls {
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

    /// The filter box as a capture shows it: its text, or the placeholder the search field shows.
    private func drawSearch(_ search: String, in rect: CGRect) {
        Palette.surface.setFill()
        NSBezierPath(roundedRect: rect, xRadius: 4, yRadius: 4).fill()
        let shown = search.isEmpty ? "Filter" : search
        drawText(shown, at: CGPoint(x: rect.minX + 8, y: rect.midY - lineHeight / 2), font: font, colour: search.isEmpty ? Palette.dim : Palette.text, width: rect.width - 16)
    }

    private func drawRows(_ frame: Frame) {
        if frame.rows.isEmpty {
            // Where the first row would be.
            let line = CGRect(x: 0, y: bodyRect.minY + gap, width: bodyRect.width, height: rowHeight)
            var x = gap
            if frame.loading {
                // An arc turning once a second, from the clock: while it shows, the view is drawn every frame.
                let size: CGFloat = 18
                let angle = CGFloat(Date().timeIntervalSinceReferenceDate.truncatingRemainder(dividingBy: 1)) * 360
                let path = NSBezierPath()
                path.appendArc(withCenter: CGPoint(x: x + size / 2, y: line.midY), radius: size / 2, startAngle: angle, endAngle: angle + 270)
                path.lineWidth = 2.5
                Palette.dim.setStroke()
                path.stroke()
                x += size + gap
            }

            drawText(frame.empty, at: CGPoint(x: x, y: line.midY - lineHeight / 2), font: font, colour: Palette.dim)
            return
        }

        let width = bodyRect.width
        let barWidth: CGFloat = 110
        // Measured rather than fixed, so each cell holds its widest text at whatever size the font
        // is: a countdown past an hour, and the widest set of chips a row carries.
        let timingWidth = measure("0:00:00 left")
        // Every chip the widest row can carry, with a gap between each, so the columns before
        // them do not move as builds gain and lose chips: a failed pull request build with a
        // checkout carries every one of them. Cancel is not among them; it never shares a row
        // with Retry, and a row that has it has nothing else.
        let widestChips = [("pull-request", "9999"), ("retry", ""), ("log", ""), ("folder", ""), ("triage", "")]
            .map { chipWidth($0.0, $0.1) }
            .reduce(0, +) + 4 * chipGap
        let overflowWidth = chipWidth("", overflowLabel)
        // Reserved on every row once any row has one, so a group's row, which has no provider
        // logo, keeps its name in line with the rows under it, and the names line up where a
        // provider gave no repository URL to read a host mark from.
        let iconWidth: CGFloat = frame.rows.contains { !$0.detailIcon.isEmpty } ? logoSize + gap : 0
        let markWidth: CGFloat = frame.rows.contains { !$0.nameIcon.isEmpty } ? logoSize + gap : 0
        // The author of a failed build, as wide as the widest name up to twenty characters, and gone
        // with its gap when no failed build names anyone.
        let authorWidth = min(frame.authors.map { measure($0).rounded(.up) }.max() ?? 0, measure(String(repeating: "0", count: 20)))
        let textX = rowHeight + gap
        // What the name, the detail, the bar and the chips share: the row less the square, the timing,
        // the author when shown, and a gap after each cell.
        let shared = width - textX - timingWidth - 4 * gap - (authorWidth > 0 ? authorWidth + gap : 0)
        // As wide as the widest name across every row, not only those on screen, so it does not shift
        // while scrolling; the detail likewise, up to forty characters, past which a long pipeline or
        // branch is cut short rather than pushing every row's chips into the drop down.
        let nameWanted = markWidth + ((frame.names + frame.groupNames.map { "▼ " + $0 })
            .map { measure($0).rounded(.up) }
            .max() ?? 0)
        let detailWanted = iconWidth + min(
            frame.details.map { measure($0).rounded(.up) }.max() ?? 0,
            measure(String(repeating: "0", count: 40)))
        // The bar gives way before anything else, since the timing beside it says the same: it shows
        // only while the names, the detail and every chip still fit.
        let showBar = shared - barWidth - gap - nameWanted - detailWanted >= widestChips
        let available = showBar ? shared - barWidth - gap : shared
        // Then the chips: a row without room for all of them puts the last behind an
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
            // First, so every cell drawn after it covers it where it has something of its own.
            tip(rect, row.tooltip(BM_PART_ROW))
            if row.isSelected {
                Palette.selectedRow.setFill()
                rect.fill()
            }

            let textY = rect.midY - lineHeight / 2
            let colour = Palette.status(row.status)
            colour.setFill()
            // The full height of the row and flush with its neighbours, so a run of rows in one status
            // reads as one block rather than a column of dots.
            let square = CGRect(x: rect.minX, y: rect.minY, width: rowHeight, height: rowHeight)
            square.fill()
            // The square opens the run: an AppVeyor row, whose pipeline is left out of the detail,
            // has no other part that does. A group's square reports nothing: it stands for several.
            if row.statusLink != Int32(BM_CHIP_NONE.rawValue) {
                chips.append(Hit(row: index, chip: row.statusLink, overflow: false, rect: square))
                tip(square, row.tooltip(BM_PART_STATUS))
            }

            var x = textX
            // The arrow is drawn before the name but is no part of it: it is what opens and closes
            // the group, so it stays in the ordinary colour and outside the link, and a click on it
            // reaches the row. Inside the link it left a closed group with no way to expand.
            var nameX = x
            var nameRoom = nameWidth
            let arrow = groupArrow(row)
            if !arrow.isEmpty {
                drawText(arrow, at: CGPoint(x: nameX, y: textY), font: font, colour: Palette.text, width: nameRoom)
                let arrowWidth = min(measure(arrow).rounded(.up), nameRoom)
                nameX += arrowWidth
                nameRoom -= arrowWidth
            }

            // The host's mark leads the name, in a width reserved on every row once any row has
            // one, so the names still line up where a provider gave no repository URL. Not on a
            // group with no mark of its own: its arrow already stands where the mark would, and a
            // prefix group, which names no one repository, read as an indented heading.
            let markX = nameX
            if markWidth > 0,
               arrow.isEmpty || !row.nameIcon.isEmpty {
                if let mark = RowIcons.images[row.nameIcon] {
                    let markRect = CGRect(x: markX, y: rect.midY - logoSize / 2, width: logoSize, height: logoSize)
                    mark.draw(in: markRect, from: .zero, operation: .sourceOver, fraction: 1, respectFlipped: true, hints: nil)
                }

                nameX += markWidth
                nameRoom -= markWidth
            }

            if row.isNameLink {
                // A name that opens the repository, in the link colour. Only its text is the link,
                // so a click beside it still selects the row. The mark is part of it: it stands for
                // the same page, so a click on it is not a click on nothing.
                drawText(row.name, at: CGPoint(x: nameX, y: textY), font: font, colour: Palette.chipText, width: nameRoom)
                let linkWidth = min(measure(row.name).rounded(.up), nameRoom)
                var nameRect = CGRect(x: nameX, y: textY, width: linkWidth, height: lineHeight)
                if !row.nameIcon.isEmpty {
                    // As tall as the mark, which stands above and below a line of text.
                    nameRect = CGRect(x: markX, y: rect.midY - logoSize / 2, width: nameX - markX + linkWidth, height: logoSize)
                }

                chips.append(Hit(row: index, chip: row.nameLink, overflow: false, rect: nameRect))
                tip(nameRect, row.tooltip(BM_PART_NAME))
            } else {
                drawText(row.name, at: CGPoint(x: nameX, y: textY), font: font, colour: Palette.text, width: nameRoom)
            }

            x += nameWidth + gap
            // The second of the row's two marks leads the detail cell, beside the pipeline, so a
            // group's members, whose first cell is empty, still show which service each came from.
            if let icon = RowIcons.images[row.detailIcon] {
                let iconRect = CGRect(x: x, y: rect.midY - logoSize / 2, width: logoSize, height: logoSize)
                icon.draw(in: iconRect, from: .zero, operation: .sourceOver, fraction: 1, respectFlipped: true, hints: nil)
                chips.append(Hit(row: index, chip: row.detailIconLink, overflow: false, rect: iconRect))
                tip(iconRect, row.tooltip(BM_PART_DETAIL_ICON))
            }

            drawDetail(row, index: index, from: x + iconWidth, width: detailWidth - iconWidth, textY: textY)
            x += detailWidth + gap
            if showBar {
                if row.progress >= 0 {
                    let track = CGRect(x: x, y: rect.midY - 4, width: barWidth, height: 8)
                    Palette.barTrack.setFill()
                    NSBezierPath(roundedRect: track, xRadius: 4, yRadius: 4).fill()
                    let filled = CGRect(x: track.minX, y: track.minY, width: track.width * CGFloat(min(1, max(0, row.progress))), height: track.height)
                    Palette.status(Int32(BM_STATUS_RUNNING.rawValue)).setFill()
                    NSBezierPath(roundedRect: filled, xRadius: 4, yRadius: 4).fill()
                }

                x += barWidth + gap
            }

            drawText(row.timing, at: CGPoint(x: x, y: textY), font: font, colour: Palette.dim, width: timingWidth)
            tip(CGRect(x: x, y: rect.minY, width: timingWidth, height: rowHeight), row.tooltip(BM_PART_TIMING))
            x += timingWidth + gap
            if authorWidth > 0 {
                drawText(row.author, at: CGPoint(x: x, y: textY), font: font, colour: Palette.text, width: authorWidth)
                x += authorWidth + gap
            }

            drawChips(row, index: index, from: x, to: x + chipsWidth, rowRect: rect, overflowWidth: overflowWidth)
        }
    }

    /// The detail cell run by run: plain text dimmed and links in the link colour, a run too long for
    /// what is left of the cell cut short with an ellipsis. Each link records only what showed of it,
    /// so a click resolves against the text on screen.
    private func drawDetail(_ row: Frame.Row, index: Int, from start: CGFloat, width: CGFloat, textY: CGFloat) {
        let right = start + width
        var x = start
        for span in row.spans {
            guard x < right else {
                return
            }

            let spanWidth = measure(span.text)
            drawText(span.text, at: CGPoint(x: x, y: textY), font: font, colour: span.isLink ? Palette.chipText : Palette.dim, width: right - x)
            if span.isLink {
                let spanRect = CGRect(x: x, y: textY, width: min(spanWidth, right - x), height: lineHeight)
                chips.append(Hit(row: index, chip: span.link, overflow: false, rect: spanRect))
                tip(spanRect, row.tooltip(span.link == Int32(BM_CHIP_BRANCH.rawValue) ? BM_PART_BRANCH : BM_PART_PIPELINE))
            }

            x += spanWidth
        }
    }

    /// The chips that fit, from the left, then an overflow chip in place of the rest. A chip is drawn
    /// only with room after it for the overflow chip, unless it is the last, so the overflow chip
    /// always fits where the first chip that did not would have gone.
    private func drawChips(_ row: Frame.Row, index: Int, from start: CGFloat, to right: CGFloat, rowRect: CGRect, overflowWidth: CGFloat) {
        var x = start
        for (position, chip) in row.chips.enumerated() {
            let reserve = position == row.chips.count - 1 ? 0 : chipGap + overflowWidth
            if x + width(of: chip) + reserve > right {
                let chipRect = drawChip("", overflowLabel, x: x, rowRect: rowRect, fill: Palette.chip, textColour: Palette.text)
                chips.append(Hit(row: index, chip: chip.kind, overflow: true, rect: chipRect))
                tip(chipRect, "More actions")
                return
            }

            let (fill, textColour) = colours(chip.kind)
            let chipRect = drawChip(chip.icon, chip.text, x: x, rowRect: rowRect, fill: fill, textColour: textColour)
            chips.append(Hit(row: index, chip: chip.kind, overflow: false, rect: chipRect))
            tip(chipRect, chip.tooltip)
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
        case BM_CHIP_TRIAGE.rawValue: return (Palette.chip, Palette.text)
        default: return (Palette.chip, Palette.chipText)
        }
    }

    private func chipWidth(_ icon: String, _ label: String) -> CGFloat {
        var width = 2 * chipPadding
        if !icon.isEmpty {
            width += iconSize
        }

        if !label.isEmpty {
            width += measure(label)
        }

        if !icon.isEmpty && !label.isEmpty {
            width += chipIconGap
        }

        return width
    }

    private func width(of chip: Frame.Chip) -> CGFloat {
        chipWidth(chip.icon, chip.text)
    }

    /// The open or closed arrow a group's row is drawn behind, and nothing for any other row.
    private func groupArrow(_ row: Frame.Row) -> String {
        row.isGroup ? (row.isExpanded ? "▼ " : "▶ ") : ""
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
            case BM_FIELD_TEXT.rawValue, BM_FIELD_PASSWORD.rawValue, BM_FIELD_NUMBER.rawValue, BM_FIELD_SELECT.rawValue, BM_FIELD_DIRECTORY.rawValue:
                drawText(field.label, at: CGPoint(x: padding, y: y + 4), font: font, colour: Palette.dim, width: 240)
                let width: CGFloat
                if kind == BM_FIELD_NUMBER.rawValue {
                    width = 100
                } else if kind == BM_FIELD_DIRECTORY.rawValue {
                    width = 420 - browseWidth - browseGap
                } else {
                    width = 420
                }

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

                // The button beside the box rather than under it, on the width the box gave up for
                // it, so the field ends where every other one does.
                if kind == BM_FIELD_DIRECTORY.rawValue {
                    let browse = CGRect(x: box.maxX + browseGap, y: y, width: browseWidth, height: fieldHeight)
                    Palette.chip.setFill()
                    NSBezierPath(roundedRect: browse, xRadius: 4, yRadius: 4).fill()
                    drawText(browseLabel, at: CGPoint(x: browse.minX + 12, y: y + 4), font: font, colour: Palette.text)
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
            case BM_FIELD_EDIT_ROW.rawValue:
                let line = field.label.isEmpty ? field.value : "\(field.label): \(field.value)"
                drawText(line, at: CGPoint(x: padding, y: y + 4), font: font, colour: Palette.chipText)
                y += lineHeight + 8
            default:
                let line = field.label.isEmpty ? field.value : (field.value.isEmpty ? field.label : "\(field.label): \(field.value)")
                drawText(line, at: CGPoint(x: padding, y: y + 4), font: font, colour: field.id == "error" ? Palette.error : Palette.text, width: bodyRect.width - padding * 2)
                y += lineHeight + 8
            }
        }
    }

    /// A chip whose picture is its label, for an action a folder says better than a word. Hit
    /// tested like any other chip, so the click path does not know the difference. A missing image
    /// still leaves a clickable pill: a chip that vanished because the icons were never built
    /// would be worse than an empty one.
    /// In the row's font, as tall as a line of it, so a chip reads at the size of the row it sits in.
    /// One pill: its icon, then its text, either of which may be empty. The icon stands where the
    /// text would start, so a row of chips keeps one rhythm whichever of the two each one carries.
    private func drawChip(_ icon: String, _ label: String, x: CGFloat, rowRect: CGRect, fill: NSColor, textColour: NSColor) -> CGRect {
        let attributes: [NSAttributedString.Key: Any] = [.font: font, .foregroundColor: textColour]
        let rect = CGRect(x: x, y: rowRect.midY - chipHeight / 2, width: chipWidth(icon, label), height: chipHeight)
        fill.setFill()
        NSBezierPath(roundedRect: rect, xRadius: 4, yRadius: 4).fill()
        var left = rect.minX + chipPadding
        if !icon.isEmpty {
            // A chip whose glyph was never registered is still drawn and still clickable: an empty
            // pill is odd, but one that vanished because IconBuilder never ran would be worse.
            if let image = RowIcons.images[icon] {
                let square = CGRect(x: left, y: rect.midY - iconSize / 2, width: iconSize, height: iconSize)
                image.draw(in: square, from: .zero, operation: .sourceOver, fraction: 1, respectFlipped: true, hints: nil)
            }

            left += iconSize
            if !label.isEmpty {
                left += chipIconGap
            }
        }

        if !label.isEmpty {
            let textSize = (label as NSString).size(withAttributes: attributes)
            (label as NSString).draw(at: CGPoint(x: left, y: rect.midY - textSize.height / 2), withAttributes: attributes)
        }

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
            tip(rect, button.tooltip)
            (button.enabled ? Palette.chip : Palette.surface).setFill()
            NSBezierPath(roundedRect: rect, xRadius: 4, yRadius: 4).fill()
            (button.label as NSString).draw(
                at: CGPoint(x: rect.midX - textSize.width / 2, y: rect.midY - textSize.height / 2),
                withAttributes: attributes)
            x = rect.maxX + 8
        }

        let attributes: [NSAttributedString.Key: Any] = [.font: smallFont, .foregroundColor: Palette.dim]
        let statusSize = (frame.status as NSString).size(withAttributes: attributes)
        let statusOrigin = CGPoint(x: size.width - statusSize.width - padding, y: top + footerHeight / 2 - statusSize.height / 2)
        statusRect = CGRect(origin: statusOrigin, size: statusSize)
        tip(statusRect, frame.statusTooltip)
        (frame.status as NSString).draw(at: statusOrigin, withAttributes: attributes)
    }

    /// Records a hover text, dropping the empty ones so a part with nothing to say leaves whatever
    /// was registered under it showing rather than blanking it.
    private func tip(_ rect: CGRect, _ text: String) {
        if text.isEmpty {
            return
        }

        tips.append((rect, text))
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
