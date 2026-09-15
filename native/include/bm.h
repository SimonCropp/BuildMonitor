/*
 * BuildMonitor native head.
 *
 * This is a renderer for a screen model, not a toolkit binding. All application logic, state and
 * layout live in C#; the managed side marshals one flat, blittable description of the frame and
 * this library turns it into pixels. Every string is a byte offset and length into
 * BmScreen.strings, a single UTF-8 blob, so a frame is one allocation on the managed side and no
 * per-string marshalling.
 *
 * Two implementations share this header: native/src/bm.cpp (raylib and Dear ImGui, Linux) and
 * native/swift (AppKit, macOS). The managed side does not know which one it loaded.
 */
#ifndef BM_H
#define BM_H

#include <stdint.h>

#if defined(_WIN32)
#define BM_API __declspec(dllexport)
#else
#define BM_API __attribute__((visibility("default")))
#endif

#ifdef __cplusplus
extern "C" {
#endif

/* A run of UTF-8 in BmScreen.strings. length 0 means absent. */
typedef struct BmString {
    int32_t offset;
    int32_t length;
} BmString;

enum BmPage {
    BM_PAGE_BUILDS = 0,
    BM_PAGE_FORM = 1
};

/* Keep in sync with BuildStatus.cs */
enum BmStatus {
    BM_STATUS_QUEUED = 0,
    BM_STATUS_RUNNING = 1,
    BM_STATUS_SUCCEEDED = 2,
    BM_STATUS_FAILED = 3,
    BM_STATUS_CANCELLED = 4,
    BM_STATUS_UNKNOWN = 5
};

enum BmRowFlags {
    BM_ROW_SELECTED = 1 << 0,
    /* A project's group: name is the project and detail its count. No chips. */
    BM_ROW_GROUP = 1 << 1,
    /* A group whose members follow it. */
    BM_ROW_EXPANDED = 1 << 2,
    /* A build under its open group. name is empty. */
    BM_ROW_MEMBER = 1 << 3
};

/*
 * Keep in sync with ChipKind.cs. A row's chips come in the order of these values, links then
 * actions, and a click reports the kind rather than a position.
 */
enum BmChipKind {
    BM_CHIP_NONE = 0,
    /* The run: a link in a row's text, the pipeline's name in its detail or, where the pipeline is
       named after the project, its name. Never among a row's chips. */
    BM_CHIP_BUILD = 1,
    /* The branch: a link in a row's detail. Never among a row's chips. */
    BM_CHIP_BRANCH = 2,
    BM_CHIP_PULL_REQUEST = 3,
    BM_CHIP_RETRY = 4,
    BM_CHIP_CANCEL = 5,
    BM_CHIP_COPY_LOG = 6,
    /* The provider icon: the repository or project page. Clicked like a chip, never among a row's chips. */
    BM_CHIP_PROJECT = 7
};

/* One button on a row. */
typedef struct BmChip {
    BmString label;
    /* A BmChipKind. */
    int32_t kind;
} BmChip;

/* One run of a row's detail: plain text, drawn dimmed, or a link, drawn in the link colour. */
typedef struct BmSpan {
    BmString text;
    /* A BmChipKind a click on the run reports, or BM_CHIP_NONE for plain text. */
    int32_t link;
} BmSpan;

typedef struct BmRow {
    int32_t status;
    int32_t flags;
    /* The first cell, drawn bright, and the second, drawn dimmed. Which string leads is decided
       by the managed side, so no renderer composes its own. detail is the text of the row's spans
       joined, for measuring; the cell is drawn from the spans. */
    BmString name;
    BmString detail;
    /* A name given to bm_set_row_icon, drawn at the start of the detail cell, or empty for none. */
    BmString provider;
    BmString timing;
    /*
     * The row's chips: a range into BmScreen.chips. A row without room for all of them draws those
     * that fit, from the left, then an overflow chip in place of the rest, and reports a click on
     * it through BmInput.clickedOverflowRow; the managed side opens the drop down.
     */
    int32_t chipOffset;
    int32_t chipCount;
    /* 0 to 1 while a bar should be drawn, -1 for none. */
    float progress;
    /* A BmChipKind a click on the name reports, drawn in the link colour, or BM_CHIP_NONE. */
    int32_t nameLink;
    /*
     * The detail in runs, drawn one after another: a range into BmScreen.spans. A click on a link run
     * reports its kind through BmInput.clickedChipRow and clickedChip, as a chip's does.
     */
    int32_t spanOffset;
    int32_t spanCount;
    /* Who broke a failed build, drawn after the timing, or empty for any other row. */
    BmString author;
} BmRow;

/* Keep in sync with FieldKind.cs */
enum BmFieldKind {
    BM_FIELD_LABEL = 0,
    BM_FIELD_CHECKBOX = 1,
    BM_FIELD_TEXT = 2,
    BM_FIELD_PASSWORD = 3,
    BM_FIELD_NUMBER = 4,
    BM_FIELD_SELECT = 5,
    BM_FIELD_BUTTON = 6,
    BM_FIELD_LINK = 7,
    BM_FIELD_LIST_ROW = 8
};

enum BmFieldFlags {
    BM_FIELD_ENABLED = 1 << 0
};

typedef struct BmField {
    int32_t kind;
    int32_t flags;
    BmString id;
    BmString label;
    /* A checkbox carries "true"/"false", a select the chosen option. */
    BmString value;
    BmString hint;
    /* The options of a select: a range into BmScreen.options. */
    int32_t optionOffset;
    int32_t optionCount;
} BmField;

enum BmButtonFlags {
    BM_BUTTON_ENABLED = 1 << 0
};

typedef struct BmButton {
    BmString label;
    int32_t flags;
} BmButton;

/* One item of the open context menu. */
typedef struct BmMenuItem {
    BmString label;
} BmMenuItem;

/* Keep in sync with TrayIconKind.cs */
enum BmTrayIcon {
    BM_TRAY_IDLE = 0,
    BM_TRAY_RUNNING = 1,
    BM_TRAY_SUCCESS = 2,
    BM_TRAY_FAILED = 3,
    BM_TRAY_ATTENTION = 4
};

enum BmTrayFlags {
    BM_TRAY_ENABLED = 1 << 0,
    BM_TRAY_SEPARATOR = 1 << 1
};

/*
 * One tray menu entry, in menu order. icon names one of the glyphs handed over through
 * bm_tray_set_menu_icon, or is empty.
 */
typedef struct BmTrayItem {
    BmString id;
    BmString label;
    BmString icon;
    int32_t flags;
} BmTrayItem;

/* Keep in sync with Theme.cs. System is resolved by the implementation, which can ask the platform. */
enum BmTheme {
    BM_THEME_SYSTEM = 0,
    BM_THEME_DARK = 1,
    BM_THEME_LIGHT = 2
};

typedef struct BmScreen {
    const uint8_t* strings;
    int32_t stringsLength;

    int32_t page;
    BmString title;
    BmString status;

    /* The builds page. rows is the visible slice; totalRows and scrollTop size a scrollbar. */
    BmString header;
    const BmRow* rows;
    int32_t rowCount;
    int32_t scrollTop;
    int32_t totalRows;
    /* Index into rows of the selected one, or -1 when it is scrolled out of view. */
    int32_t selectedRow;
    /* 1 when there are no rows yet because a connection has not finished its first poll: draw a
       spinner rather than an empty page. */
    int32_t loading;
    /* Every distinct first column name across all rows, not only the visible slice, to size the
       column from: nameCount build names, then groupNameCount group names, drawn behind an arrow. */
    const BmString* names;
    int32_t nameCount;
    int32_t groupNameCount;
    /* Every distinct detail across all rows, to size that column from. The chips give way to the
       width these want, up to a readable maximum, before the details are cut short. */
    const BmString* details;
    int32_t detailCount;
    /* Every distinct author across all failed builds, to size the author column from. With none the
       column is not drawn. */
    const BmString* authors;
    int32_t authorCount;
    /* Every visible row's chips, which BmRow.chipOffset and chipCount index. */
    const BmChip* chips;
    int32_t chipCount;
    /* Every visible row's detail runs, which BmRow.spanOffset and spanCount index. */
    const BmSpan* spans;
    int32_t spanCount;
    /* The filter box at the right of the builds page's header: its text, shown unless the box is being
       typed in. An edit is reported through BmInput.changedField as BM_SEARCH_FIELD. */
    BmString search;
    /* What the body says when rowCount is 0, beside the spinner while loading. */
    BmString empty;

    /* The form page. */
    BmString formTitle;
    const BmField* fields;
    int32_t fieldCount;
    const BmString* options;
    int32_t optionCount;

    const BmButton* buttons;
    int32_t buttonCount;

    /* The open context menu, under visible row menuRow. menuCount is 0 when none is open. */
    const BmMenuItem* menu;
    int32_t menuCount;
    int32_t menuRow;
    /* 1 when the menu is the drop down of menuRow's overflow chip: it hangs under that chip. */
    int32_t menuOverflow;

    /* The tray. Ignored by an implementation whose bm_tray_available returns 0. */
    int32_t trayIcon;
    BmString trayTooltip;
    const BmTrayItem* trayItems;
    int32_t trayItemCount;

    /* A BmTheme. */
    int32_t theme;

    /* Bumped by the managed side each time it builds a new screen, and unchanged while it presents
       the same one again. With an unchanged generation, no input and nothing animating, an
       implementation only pumps events: drawing the same screen sixty times a second cost a whole
       frame of layout and drawing each time. */
    int32_t generation;
} BmScreen;

/* Keep in sync with NativeMonitorWindow.Key */
enum BmKey {
    BM_KEY_NONE = 0,
    BM_KEY_SCROLL_UP = 1,
    BM_KEY_SCROLL_DOWN = 2,
    BM_KEY_PAGE_UP = 3,
    BM_KEY_PAGE_DOWN = 4,
    BM_KEY_HOME = 5,
    BM_KEY_END = 6,
    BM_KEY_NEXT_ROW = 7,
    BM_KEY_PREVIOUS_ROW = 8,
    /* Enter on the builds page. */
    BM_KEY_OPEN_BUILD = 9,
    BM_KEY_RETRY = 10,
    BM_KEY_CANCEL_BUILD = 11,
    BM_KEY_REFRESH = 12,
    /* Ctrl+C, and Cmd+C on macOS. */
    BM_KEY_COPY = 13,
    /* Escape on a form page. */
    BM_KEY_BACK = 14,
    /* Escape on the builds page. */
    BM_KEY_HIDE = 15,
    BM_KEY_QUIT = 16,
    /* A click on the footer status, which copies it. */
    BM_KEY_COPY_STATUS = 17
};

/* BmInput.changedField for an edit of the filter box, which is not one of BmScreen.fields. */
enum BmSearchField {
    BM_SEARCH_FIELD = -2
};

typedef struct BmInput {
    int32_t key;
    /* Index into BmScreen.buttons, or -1. */
    int32_t clickedButton;
    /* Index into BmScreen.rows, or -1. */
    int32_t clickedRow;
    /* A chip, a link in the text, or the provider icon, of a visible row: the row and a BmChipKind. */
    int32_t clickedChipRow;
    int32_t clickedChip;
    /* The overflow chip of a visible row, and the BmChipKind of the first chip it stands in for. */
    int32_t clickedOverflowRow;
    int32_t overflowFrom;
    int32_t rightClickedRow;
    /* Index into BmScreen.menu, or -1. */
    int32_t clickedMenuItem;
    /* The open menu was dismissed without choosing anything. */
    int32_t menuClosed;
    /*
     * One edit per poll: the field (index into BmScreen.fields, BM_SEARCH_FIELD for the filter box,
     * or -1) and its new UTF-8 value. The buffer belongs to the library and is valid until the next
     * bm_poll_input. A head queues edits and hands over the oldest; a frame is 16 ms, so a burst
     * drains in a few.
     */
    int32_t changedField;
    const uint8_t* changedValue;
    int32_t changedValueLength;
    /* A button, link or list row field that was clicked: index into BmScreen.fields, or -1. */
    int32_t clickedField;
    /* Index into BmScreen.trayItems, or -1. */
    int32_t clickedTrayItem;
    int32_t trayIconClicked;
    int32_t scrollDelta;
    /* An absolute first visible row, from a scrollbar, or -1. */
    int32_t scrollTo;
    int32_t closeRequested;
    /* How many build rows fit in the body, measured from the font that was actually loaded. */
    int32_t rows;
} BmInput;

/*
 * Bumped whenever the structs above change, or what a field means changes, so a stale native
 * library is detected rather than crashed.
 */
#define BM_VERSION 7

/*
 * The Swift implementation imports this header for the struct layouts, because Swift does not
 * guarantee its own, and then defines the entry points itself with @_cdecl.
 */
#ifndef BM_TYPES_ONLY

/* Returns 1 on success. fontTtf may be NULL for a built in font. hidden starts without a visible window. */
BM_API int32_t bm_init(
    int32_t width,
    int32_t height,
    const char* title,
    const uint8_t* fontTtf,
    int32_t fontLength,
    float fontSize,
    int32_t hidden);

/*
 * Draws one frame, or pumps events for one frame while hidden, or while the generation, the input
 * and anything animating are all as they were for the last frame drawn. Returns 0 once the window is
 * gone for good.
 */
BM_API int32_t bm_present(const BmScreen* screen);

BM_API void bm_poll_input(BmInput* input);

/* Renders one frame offscreen and writes it to pngPath. Returns 1 on success. */
BM_API int32_t bm_capture(
    const BmScreen* screen,
    int32_t width,
    int32_t height,
    const char* pngPath);

BM_API void bm_set_hidden(int32_t hidden);

BM_API void bm_focus(void);

BM_API void bm_set_clipboard(const char* text);

/* 1 when this implementation puts an icon in the tray itself. Linux answers 0: its tray is managed code over D-Bus. */
BM_API int32_t bm_tray_available(void);

/* Creates the tray item. Returns 1 on success. Call bm_tray_set_icon for every BmTrayIcon before the first bm_present. */
BM_API int32_t bm_tray_init(void);

BM_API void bm_tray_set_icon(int32_t kind, const uint8_t* png, int32_t length);

BM_API void bm_tray_set_menu_icon(const char* name, const uint8_t* png, int32_t length);
/* A PNG a row can name in BmRow.provider. Call after bm_init; a second call for a name replaces it. */
BM_API void bm_set_row_icon(const char* name, const uint8_t* png, int32_t length);

BM_API void bm_shutdown(void);

BM_API int32_t bm_version(void);

#endif /* BM_TYPES_ONLY */

#ifdef __cplusplus
}
#endif

#endif
