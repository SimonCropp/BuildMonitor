import CBm
import Foundation

/// One frame, copied out of the C structs into Swift values so nothing here outlives the call
/// that handed it over.
struct Frame {
    struct Chip {
        let kind: Int32
        let label: String
        let tooltip: String
        let icon: String
        let text: String
    }

    /// What one part of a row says on hover.
    struct Tooltip {
        let part: Int32
        let text: String
    }

    /// One run of a row's detail: plain text, or a link a click reports as a chip's kind.
    struct Span {
        let text: String
        let link: Int32

        var isLink: Bool { link != Int32(BM_CHIP_NONE.rawValue) }
    }

    struct Row {
        let status: Int32
        let flags: Int32
        let name: String
        let nameLink: Int32
        let statusLink: Int32
        let detail: String
        let spans: [Span]
        let provider: String
        let timing: String
        let chips: [Chip]
        let progress: Float
        let author: String
        let tooltips: [Tooltip]

        /// What `part` says on hover, falling back to what the row itself says, so a caller can ask
        /// for any part without first checking whether this row has one.
        func tooltip(_ part: BmRowPart) -> String {
            let wanted = Int32(part.rawValue)
            if let found = tooltips.first(where: { $0.part == wanted }) {
                return found.text
            }

            if wanted == Int32(BM_PART_ROW.rawValue) {
                return ""
            }

            return tooltips.first { $0.part == Int32(BM_PART_ROW.rawValue) }?.text ?? ""
        }

        var isGroup: Bool { flags & Int32(BM_ROW_GROUP.rawValue) != 0 }
        var isSelected: Bool { flags & Int32(BM_ROW_SELECTED.rawValue) != 0 }
        var isExpanded: Bool { flags & Int32(BM_ROW_EXPANDED.rawValue) != 0 }
        var isNameLink: Bool { nameLink != Int32(BM_CHIP_NONE.rawValue) }
    }

    struct Field {
        let kind: Int32
        let enabled: Bool
        let id: String
        let label: String
        let value: String
        let hint: String
        let options: [String]
    }

    struct Button {
        let label: String
        let tooltip: String
        let enabled: Bool
    }

    struct TrayItem {
        let id: String
        let label: String
        let icon: String
        let enabled: Bool
        let separator: Bool
    }

    let page: Int32
    let title: String
    let status: String
    let statusTooltip: String
    let header: String
    let rows: [Row]
    let scrollTop: Int32
    let totalRows: Int32
    let selectedRow: Int32
    let loading: Bool
    let names: [String]
    let groupNames: [String]
    let details: [String]
    let authors: [String]
    let search: String
    let searchTooltip: String
    let empty: String
    let formTitle: String
    let fields: [Field]
    let buttons: [Button]
    let menu: [String]
    let menuRow: Int32
    let menuOverflow: Bool
    let trayIcon: Int32
    let trayTooltip: String
    let trayItems: [TrayItem]
    let theme: Int32

    var isForm: Bool { page == Int32(BM_PAGE_FORM.rawValue) }

    static func decode(_ pointer: UnsafePointer<BmScreen>) -> Frame {
        let screen = pointer.pointee
        let strings = UnsafeBufferPointer(start: screen.strings, count: Int(screen.stringsLength))

        func text(_ value: BmString) -> String {
            guard value.length > 0, value.offset >= 0, Int(value.offset + value.length) <= strings.count else {
                return ""
            }

            let slice = UnsafeBufferPointer(start: strings.baseAddress! + Int(value.offset), count: Int(value.length))
            return String(decoding: slice, as: UTF8.self)
        }

        let chips = UnsafeBufferPointer(start: screen.chips, count: Int(screen.chipCount)).map {
            Chip(kind: $0.kind, label: text($0.label), tooltip: text($0.tooltip), icon: text($0.icon), text: text($0.text))
        }

        let tooltips = UnsafeBufferPointer(start: screen.tooltips, count: Int(screen.tooltipCount)).map {
            Tooltip(part: $0.part, text: text($0.text))
        }

        let spans = UnsafeBufferPointer(start: screen.spans, count: Int(screen.spanCount)).map {
            Span(text: text($0.text), link: $0.link)
        }

        let rows = UnsafeBufferPointer(start: screen.rows, count: Int(screen.rowCount)).map { row -> Row in
            let start = Int(row.chipOffset)
            let end = min(chips.count, start + Int(row.chipCount))
            let spanStart = Int(row.spanOffset)
            let spanEnd = min(spans.count, spanStart + Int(row.spanCount))
            let tipStart = Int(row.tooltipOffset)
            let tipEnd = min(tooltips.count, tipStart + Int(row.tooltipCount))
            return Row(
                status: row.status,
                flags: row.flags,
                name: text(row.name),
                nameLink: row.nameLink,
                statusLink: row.statusLink,
                detail: text(row.detail),
                spans: spanStart >= 0 && spanStart < spanEnd ? Array(spans[spanStart..<spanEnd]) : [],
                provider: text(row.provider),
                timing: text(row.timing),
                chips: start >= 0 && start < end ? Array(chips[start..<end]) : [],
                progress: row.progress,
                author: text(row.author),
                tooltips: tipStart >= 0 && tipStart < tipEnd ? Array(tooltips[tipStart..<tipEnd]) : [])
        }

        let options = UnsafeBufferPointer(start: screen.options, count: Int(screen.optionCount)).map(text)
        let fields = UnsafeBufferPointer(start: screen.fields, count: Int(screen.fieldCount)).map { field -> Field in
            let start = Int(field.optionOffset)
            let end = min(options.count, start + Int(field.optionCount))
            let slice = start >= 0 && start < end ? Array(options[start..<end]) : []
            return Field(
                kind: field.kind,
                enabled: field.flags & Int32(BM_FIELD_ENABLED.rawValue) != 0,
                id: text(field.id),
                label: text(field.label),
                value: text(field.value),
                hint: text(field.hint),
                options: slice)
        }

        let buttons = UnsafeBufferPointer(start: screen.buttons, count: Int(screen.buttonCount)).map {
            Button(label: text($0.label), tooltip: text($0.tooltip), enabled: $0.flags & Int32(BM_BUTTON_ENABLED.rawValue) != 0)
        }

        let menu = UnsafeBufferPointer(start: screen.menu, count: Int(screen.menuCount)).map { text($0.label) }
        let trayItems = UnsafeBufferPointer(start: screen.trayItems, count: Int(screen.trayItemCount)).map {
            TrayItem(
                id: text($0.id),
                label: text($0.label),
                icon: text($0.icon),
                enabled: $0.flags & Int32(BM_TRAY_ENABLED.rawValue) != 0,
                separator: $0.flags & Int32(BM_TRAY_SEPARATOR.rawValue) != 0)
        }

        let allNames = UnsafeBufferPointer(start: screen.names, count: Int(screen.nameCount + screen.groupNameCount)).map(text)
        let details = UnsafeBufferPointer(start: screen.details, count: Int(screen.detailCount)).map(text)
        let authors = UnsafeBufferPointer(start: screen.authors, count: Int(screen.authorCount)).map(text)

        return Frame(
            page: screen.page,
            title: text(screen.title),
            status: text(screen.status),
            statusTooltip: text(screen.statusTooltip),
            header: text(screen.header),
            rows: rows,
            scrollTop: screen.scrollTop,
            totalRows: screen.totalRows,
            selectedRow: screen.selectedRow,
            loading: screen.loading != 0,
            names: Array(allNames.prefix(Int(screen.nameCount))),
            groupNames: Array(allNames.dropFirst(Int(screen.nameCount))),
            details: details,
            authors: authors,
            search: text(screen.search),
            searchTooltip: text(screen.searchTooltip),
            empty: text(screen.empty),
            formTitle: text(screen.formTitle),
            fields: fields,
            buttons: buttons,
            menu: menu,
            menuRow: screen.menuRow,
            menuOverflow: screen.menuOverflow != 0,
            trayIcon: screen.trayIcon,
            trayTooltip: text(screen.trayTooltip),
            trayItems: trayItems,
            theme: screen.theme)
    }
}
