import CBm
import Foundation

/// One frame, copied out of the C structs into Swift values so nothing here outlives the call
/// that handed it over.
struct Frame {
    struct Row {
        let status: Int32
        let flags: Int32
        let pipeline: String
        let repoBranch: String
        let runNumber: String
        let statusText: String
        let timing: String
        let tooltip: String
        let buildLabel: String
        let branchLabel: String
        let pullRequestLabel: String
        let progress: Float

        var isHeader: Bool { flags & Int32(BM_ROW_HEADER.rawValue) != 0 }
        var isSelected: Bool { flags & Int32(BM_ROW_SELECTED.rawValue) != 0 }
        var isFolded: Bool { flags & Int32(BM_ROW_FOLDED.rawValue) != 0 }
        var canRetry: Bool { flags & Int32(BM_ROW_CAN_RETRY.rawValue) != 0 }
        var canCancel: Bool { flags & Int32(BM_ROW_CAN_CANCEL.rawValue) != 0 }
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
        let depth: Int
    }

    let page: Int32
    let title: String
    let status: String
    let header: String
    let rows: [Row]
    let scrollTop: Int32
    let totalRows: Int32
    let selectedRow: Int32
    let formTitle: String
    let fields: [Field]
    let buttons: [Button]
    let menu: [String]
    let menuRow: Int32
    let trayIcon: Int32
    let trayTooltip: String
    let trayItems: [TrayItem]

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

        let rows = UnsafeBufferPointer(start: screen.rows, count: Int(screen.rowCount)).map {
            Row(
                status: $0.status,
                flags: $0.flags,
                pipeline: text($0.pipeline),
                repoBranch: text($0.repoBranch),
                runNumber: text($0.runNumber),
                statusText: text($0.statusText),
                timing: text($0.timing),
                tooltip: text($0.tooltip),
                buildLabel: text($0.buildLabel),
                branchLabel: text($0.branchLabel),
                pullRequestLabel: text($0.pullRequestLabel),
                progress: $0.progress)
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
                separator: $0.flags & Int32(BM_TRAY_SEPARATOR.rawValue) != 0,
                depth: Int($0.depth))
        }

        return Frame(
            page: screen.page,
            title: text(screen.title),
            status: text(screen.status),
            header: text(screen.header),
            rows: rows,
            scrollTop: screen.scrollTop,
            totalRows: screen.totalRows,
            selectedRow: screen.selectedRow,
            formTitle: text(screen.formTitle),
            fields: fields,
            buttons: buttons,
            menu: menu,
            menuRow: screen.menuRow,
            trayIcon: screen.trayIcon,
            trayTooltip: text(screen.trayTooltip),
            trayItems: trayItems)
    }
}
