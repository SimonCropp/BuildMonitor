import CBm
import Foundation

/// One frame, copied out of the C structs into Swift values so nothing here outlives the call
/// that handed it over.
struct Frame {
    struct Chip {
        let kind: Int32
        let label: String
    }

    struct Row {
        let status: Int32
        let flags: Int32
        let name: String
        let detail: String
        let provider: String
        let timing: String
        let chips: [Chip]
        let progress: Float

        var isGroup: Bool { flags & Int32(BM_ROW_GROUP.rawValue) != 0 }
        var isSelected: Bool { flags & Int32(BM_ROW_SELECTED.rawValue) != 0 }
        var isExpanded: Bool { flags & Int32(BM_ROW_EXPANDED.rawValue) != 0 }
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
    let header: String
    let rows: [Row]
    let scrollTop: Int32
    let totalRows: Int32
    let selectedRow: Int32
    let loading: Bool
    let names: [String]
    let groupNames: [String]
    let details: [String]
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
            Chip(kind: $0.kind, label: text($0.label))
        }

        let rows = UnsafeBufferPointer(start: screen.rows, count: Int(screen.rowCount)).map { row -> Row in
            let start = Int(row.chipOffset)
            let end = min(chips.count, start + Int(row.chipCount))
            return Row(
                status: row.status,
                flags: row.flags,
                name: text(row.name),
                detail: text(row.detail),
                provider: text(row.provider),
                timing: text(row.timing),
                chips: start >= 0 && start < end ? Array(chips[start..<end]) : [],
                progress: row.progress)
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
            Button(label: text($0.label), enabled: $0.flags & Int32(BM_BUTTON_ENABLED.rawValue) != 0)
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

        return Frame(
            page: screen.page,
            title: text(screen.title),
            status: text(screen.status),
            header: text(screen.header),
            rows: rows,
            scrollTop: screen.scrollTop,
            totalRows: screen.totalRows,
            selectedRow: screen.selectedRow,
            loading: screen.loading != 0,
            names: Array(allNames.prefix(Int(screen.nameCount))),
            groupNames: Array(allNames.dropFirst(Int(screen.nameCount))),
            details: details,
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
