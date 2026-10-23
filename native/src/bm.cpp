/*
 * The Linux head: raylib for the window, GL context and input; Dear ImGui for the widgets,
 * rendered through rlgl. A renderer for the screen model in bm.h, not a toolkit binding: the
 * managed side owns every decision about what is on screen, this file only draws it and reports
 * what was clicked.
 */
#include "bm.h"
#include "platformheads/runtime.h"

#include "raylib.h"
#include "rlgl.h"
#include "imgui.h"

#include <algorithm>
#include <cmath>
#include <cstdint>
#include <cstring>
#include <deque>
#include <string>
#include <unordered_map>
#include <vector>

namespace {

struct Edit {
    int32_t field;
    std::string value;
};

struct State {
    bool initialised = false;
    bool hidden = false;
    bool windowGone = false;
    int width = 1000;
    int height = 640;
    float fontPixels = 20.0f;
    std::vector<unsigned char> font;
    std::vector<unsigned char> emoji;
    ImGuiContext* context = nullptr;
    /* The embedded font alone, which a capture draws with, so a capture is the same picture on a
       machine with every font installed and on one with none. The window's has the machine's
       fonts merged in for the characters it lacks. */
    ImFont* captureFont = nullptr;
    std::unordered_map<std::string, Texture2D> rowIcons;
    BmInput input{};
    std::deque<Edit> edits;
    std::string changedValue;
    std::unordered_map<std::string, std::string> buffers;
    std::string activeField;
    bool menuOpen = false;
    int32_t menuRow = -1;
    bool menuOverflow = false;
    /* Where each visible row's overflow chip was drawn this frame, bottom left, or x -1 for none. */
    std::vector<ImVec2> overflowAnchors;
    /* What the pointer was on in the last frame drawn, as BmInput.hoveredChipRow and hoveredChip.
       Kept out of input, which is cleared after every poll: a hover lasts until the pointer moves,
       and a still pointer draws no frames to report it again. */
    int32_t hoveredChipRow = -1;
    int32_t hoveredChip = BM_CHIP_NONE;
    int bodyRows = 1;
    /* The filter box's text. The screen's replaces it on every frame the box is not being typed in. */
    std::string search;
    bool searchActive = false;
    /* Ctrl+F was pressed: the box takes the keyboard on the next frame it is drawn. */
    bool focusSearch = false;
    /* The generation of the screen last drawn. The managed side hands over no such value, so the
       first screen is always drawn. */
    int64_t drawnGeneration = INT64_MIN;
    /* A frame has been built for the window, which is what bodyRows is measured from. */
    bool built = false;
    /* The last frame drawn left an item active, a box being typed in or a slider being dragged,
       which changes with no input at all: the caret blinks. */
    bool itemActive = false;
    /* What the last frame built for the window left, which is what a key read between frames
       is read against: whether a text field had the keyboard, and whether it was a form page. */
    bool wantTextInput = false;
    bool formPage = false;
    /* What the wheel left over, in rows. A notch is three, and a touchpad sends a fraction of
       one: truncating each frame's on its own threw all of them away. */
    float wheel = 0.0f;
};

State g;

// ImGui 1.92 keeps IM_PI in imgui_internal.h, so imgui.h alone no longer declares it.
const float pi = 3.14159265f;

ImVec4 Rgb(int red, int green, int blue) {
    return ImVec4(red / 255.0f, green / 255.0f, blue / 255.0f, 1.0f);
}

// Colours, transcribed from the WinForms head's Palette so every head agrees. Set by UsePalette.
bool lightPalette = false;
bool paletteSet = false;
ImVec4 background;
ImVec4 surface;
ImVec4 headerRow;
ImVec4 selectedRow;
ImVec4 hoverRow;
ImVec4 text;
ImVec4 dim;
ImVec4 border;
ImVec4 barTrack;
ImVec4 chip;
ImVec4 chipText;
ImVec4 retryChip;
ImVec4 cancelChip;
ImVec4 errorText;

void UsePalette(bool light) {
    lightPalette = light;
    background = light ? Rgb(250, 250, 250) : Rgb(24, 24, 24);
    surface = light ? Rgb(240, 240, 240) : Rgb(32, 32, 32);
    headerRow = light ? Rgb(232, 232, 232) : Rgb(38, 38, 38);
    selectedRow = light ? Rgb(204, 222, 245) : Rgb(44, 50, 66);
    hoverRow = light ? Rgb(236, 238, 244) : Rgb(36, 36, 40);
    text = light ? Rgb(32, 32, 32) : Rgb(212, 212, 212);
    dim = light ? Rgb(110, 110, 110) : Rgb(140, 140, 140);
    border = light ? Rgb(204, 204, 204) : Rgb(56, 56, 56);
    barTrack = light ? Rgb(220, 220, 220) : Rgb(58, 58, 58);
    chip = light ? Rgb(226, 226, 232) : Rgb(52, 52, 56);
    chipText = light ? Rgb(0, 90, 180) : Rgb(180, 200, 255);
    retryChip = light ? Rgb(200, 230, 204) : Rgb(48, 82, 52);
    cancelChip = light ? Rgb(244, 208, 208) : Rgb(96, 52, 52);
    errorText = light ? Rgb(196, 43, 28) : Rgb(233, 129, 129);
}

ImVec4 StatusColour(int32_t status) {
    switch (status) {
        case BM_STATUS_QUEUED: return lightPalette ? Rgb(120, 120, 120) : Rgb(150, 150, 150);
        case BM_STATUS_RUNNING: return lightPalette ? Rgb(0, 120, 212) : Rgb(86, 156, 214);
        case BM_STATUS_SUCCEEDED: return lightPalette ? Rgb(16, 124, 16) : Rgb(126, 214, 139);
        case BM_STATUS_FAILED: return lightPalette ? Rgb(196, 43, 28) : Rgb(233, 129, 129);
        case BM_STATUS_CANCELLED: return lightPalette ? Rgb(120, 120, 120) : Rgb(160, 160, 160);
        default: return lightPalette ? Rgb(140, 140, 140) : Rgb(120, 120, 120);
    }
}

Color ClearColour() {
    return Color{
        static_cast<unsigned char>(background.x * 255.0f),
        static_cast<unsigned char>(background.y * 255.0f),
        static_cast<unsigned char>(background.z * 255.0f),
        255};
}

// Every string is an offset into the screen's one UTF-8 blob, read by the runtime and bounds
// checked: a bad offset was a read past the end of the blob.
const char none[] = "";

const char* Begin(const BmScreen& screen, const BmString& value) {
    const char* begin;
    const char* end;
    return ph::Slice(screen.strings, screen.stringsLength, value.offset, value.length, &begin, &end) ? begin : none;
}

const char* End(const BmScreen& screen, const BmString& value) {
    const char* begin;
    const char* end;
    return ph::Slice(screen.strings, screen.stringsLength, value.offset, value.length, &begin, &end) ? end : none;
}

std::string Str(const BmScreen& screen, const BmString& value) {
    return ph::Copy(screen.strings, screen.stringsLength, value.offset, value.length);
}

/* Where the window goes back to, into BmInput.placement, as the runtime tracked it: GLFW gives no
   bounds back for a maximized window other than the ones it fills, so those are kept from the
   last present it was not maximized. Whether it is minimized goes in on every poll. */
void Sample() {
    g.input.minimized = IsWindowMinimized() ? 1 : 0;
    ph::Placement placement;
    if (ph::GetPlacement(placement)) {
        g.input.placement.x = placement.x;
        g.input.placement.y = placement.y;
        g.input.placement.width = placement.width;
        g.input.placement.height = placement.height;
        g.input.placement.maximized = placement.maximized ? 1 : 0;
        g.input.placement.known = 1;
    }
}

void ResetInput() {
    g.input.key = BM_KEY_NONE;
    g.input.clickedButton = -1;
    g.input.clickedRow = -1;
    g.input.clickedChipRow = -1;
    g.input.clickedChip = BM_CHIP_NONE;
    g.input.clickedOverflowRow = -1;
    g.input.overflowFrom = BM_CHIP_NONE;
    // Filled at every poll from the last frame drawn, like placement and rows, rather than left as
    // whatever the frame that read it happened to see.
    g.input.hoveredChipRow = -1;
    g.input.hoveredChip = BM_CHIP_NONE;
    g.input.rightClickedRow = -1;
    g.input.clickedMenuItem = -1;
    g.input.menuClosed = 0;
    g.input.changedField = -1;
    g.input.changedValue = nullptr;
    g.input.changedValueLength = 0;
    g.input.clickedField = -1;
    g.input.clickedTrayItem = -1;
    g.input.trayIconClicked = 0;
    g.input.scrollDelta = 0;
    g.input.scrollTo = -1;
    g.input.closeRequested = 0;
}

// Keys, read from the runtime's queue at every poll rather than off raylib's state while a frame
// is built: a frame is built only when something could make it look different, so a key read
// that way between two frames was lost, and so was any press and release that came between two
// of raylib's readings. Letters are read as what the key types on the layout in use rather than
// by where it is on a US keyboard, so Ctrl+Q on AZERTY is the key labelled Q. The filter box
// keeps the keys editing needs, but Up, Down and the page keys still move through the rows it
// leaves, so a match can be picked without leaving the box.
bool Navigation(int key) {
    return key == KEY_UP || key == KEY_DOWN || key == KEY_PAGE_UP || key == KEY_PAGE_DOWN;
}

int32_t ShortcutOf(const ph::KeyPress& press) {
    // A held key repeats moving through the rows and nothing else: a held Ctrl+R was a retry for
    // every repeat the window system sent.
    if (press.repeated && !Navigation(press.key)) {
        return BM_KEY_NONE;
    }

    if (g.wantTextInput) {
        if (!g.searchActive) {
            return BM_KEY_NONE;
        }

        switch (press.key) {
            case KEY_UP: return BM_KEY_PREVIOUS_ROW;
            case KEY_DOWN: return BM_KEY_NEXT_ROW;
            case KEY_PAGE_UP: return BM_KEY_PAGE_UP;
            case KEY_PAGE_DOWN: return BM_KEY_PAGE_DOWN;
            default: return BM_KEY_NONE;
        }
    }

    const bool control = (press.mods & ph::ModControl) != 0;
    const bool shift = (press.mods & ph::ModShift) != 0;
    const bool formPage = g.formPage;
    const unsigned int letter = ph::LetterOf(press);
    if (!formPage && control && letter == 'f') {
        g.focusSearch = true;
        return BM_KEY_NONE;
    }

    switch (press.key) {
        case KEY_UP: return BM_KEY_PREVIOUS_ROW;
        case KEY_DOWN: return BM_KEY_NEXT_ROW;
        case KEY_PAGE_UP: return BM_KEY_PAGE_UP;
        case KEY_PAGE_DOWN: return BM_KEY_PAGE_DOWN;
        case KEY_HOME: return BM_KEY_HOME;
        case KEY_END: return BM_KEY_END;
        case KEY_F5: return BM_KEY_REFRESH;
        case KEY_ESCAPE: return formPage ? BM_KEY_BACK : BM_KEY_HIDE;
        default: break;
    }

    if (control && letter == 'q') return BM_KEY_QUIT;
    if (formPage) return BM_KEY_NONE;
    if (control && letter == 'c') return BM_KEY_COPY;
    if (press.key == KEY_ENTER) return BM_KEY_OPEN_BUILD;
    // With Control, as every key that changes a service's builds is: R alone, typed into the rows
    // by someone who took the filter box to have the keyboard, reran the build.
    if (control && letter == 'r') return BM_KEY_RETRY;
    if (control && (letter == '.' || press.key == KEY_PERIOD)) return BM_KEY_CANCEL_BUILD;
    if (control && letter == 'l') return BM_KEY_COPY_LOG;
    if (control && letter == 't') return BM_KEY_TRIAGE;
    if ((shift && press.key == KEY_F10) || press.key == KEY_KB_MENU) return BM_KEY_OPEN_MENU;
    return BM_KEY_NONE;
}

// The next key waiting that asks for anything. One a poll: the rest wait for the polls after it,
// which is what keeps two keys pressed between two polls both acted on.
int32_t ReadKey() {
    ph::KeyPress press;
    while (ph::PopKey(press)) {
        const int32_t key = ShortcutOf(press);
        if (key != BM_KEY_NONE) {
            return key;
        }
    }

    return BM_KEY_NONE;
}

// Widgets

int TextResize(ImGuiInputTextCallbackData* data) {
    if (data->EventFlag == ImGuiInputTextFlags_CallbackResize) {
        std::string* value = static_cast<std::string*>(data->UserData);
        value->resize(static_cast<size_t>(data->BufTextLen));
        data->Buf = value->data();
    }

    return 0;
}

// Stands in for the chips a row has no room for.
const char* const overflowLabel = "…";

// Retry and Cancel on colours of their own, links in the link colour, as the WinForms canvas draws them.
ImVec4 ChipColour(int32_t kind) {
    switch (kind) {
        case BM_CHIP_RETRY: return retryChip;
        case BM_CHIP_CANCEL: return cancelChip;
        default: return chip;
    }
}

ImVec4 ChipTextColour(int32_t kind) {
    switch (kind) {
        case BM_CHIP_RETRY:
        case BM_CHIP_CANCEL:
        case BM_CHIP_COPY_LOG:
        case BM_CHIP_TRIAGE:
            return text;
        default:
            return chipText;
    }
}

float ChipHeight() {
    return ImGui::GetTextLineHeight() + 2.0f;
}

// The side of a chip's picture, matching the provider logo that leads the detail cell.
const float chipIconSize = 16.0f;
// Between a chip's icon and the text after it, where it has both.
const float chipIconGap = 4.0f;

bool Empty(const char* value) {
    return value == nullptr || value[0] == '\0';
}

float ChipWidth(const char* icon, const char* label) {
    float width = 2.0f * ImGui::GetStyle().FramePadding.x;
    if (!Empty(icon)) {
        width += chipIconSize;
    }

    if (!Empty(label)) {
        width += ImGui::CalcTextSize(label).x;
    }

    if (!Empty(icon) && !Empty(label)) {
        width += chipIconGap;
    }

    return width;
}

// One pill: its icon, then its text, either of which may be empty. An invisible button under a
// hand drawn pill, rather than ImGui::Button, because a button's label is text and a chip's may be
// a texture. The icon stands where the text would start, so a row of chips keeps one rhythm
// whichever of the two each one carries.
bool Chip(const char* icon, const char* label, const ImVec4& colour, const ImVec4& foreground) {
    const ImVec2 size(ChipWidth(icon, label), ChipHeight());
    const ImVec2 at = ImGui::GetCursorScreenPos();
    const bool clicked = ImGui::InvisibleButton("##chip", size);
    const bool hovered = ImGui::IsItemHovered();
    const ImVec4 fill = hovered
        ? ImVec4(colour.x + 0.08f, colour.y + 0.08f, colour.z + 0.08f, 1.0f)
        : colour;
    ImDrawList* draw = ImGui::GetWindowDrawList();
    draw->AddRectFilled(at, ImVec2(at.x + size.x, at.y + size.y), ImGui::GetColorU32(fill), ImGui::GetStyle().FrameRounding);
    float left = at.x + ImGui::GetStyle().FramePadding.x;
    if (!Empty(icon)) {
        // A chip whose glyph was never registered is still drawn and still clickable: an empty pill
        // is odd, but one that vanished because IconBuilder never ran would be worse.
        auto found = g.rowIcons.find(icon);
        if (found != g.rowIcons.end()) {
            const float top = at.y + (size.y - chipIconSize) / 2.0f;
            draw->AddImage(static_cast<ImTextureID>(found->second.id), ImVec2(left, top), ImVec2(left + chipIconSize, top + chipIconSize));
        }

        left += chipIconSize;
        if (!Empty(label)) {
            left += chipIconGap;
        }
    }

    if (!Empty(label)) {
        const float top = at.y + (size.y - ImGui::GetTextLineHeight()) / 2.0f;
        draw->AddText(ImVec2(left, top), ImGui::GetColorU32(foreground), label);
    }

    if (hovered) {
        ImGui::SetMouseCursor(ImGuiMouseCursor_Hand);
    }

    return clicked;
}

// The item just submitted is a row's button, and the pointer is on it. Reported for as long as it
// stays there, unlike a click: the managed side holds the rows still while a pointer is on its way
// to a chip, so a poll cannot re-sort them out from under it.
void Hovering(int32_t row, int32_t kind) {
    if (kind != BM_CHIP_NONE && ImGui::IsItemHovered()) {
        g.hoveredChipRow = row;
        g.hoveredChip = kind;
    }
}

// A hover text on the item just submitted. Delayed: a row's cells touch, and without a wait a
// pointer crossing one row pops four tooltips on its way past.
void Tip(const std::string& text) {
    if (!text.empty() && ImGui::IsItemHovered(ImGuiHoveredFlags_DelayNormal | ImGuiHoveredFlags_NoSharedDelay)) {
        ImGui::SetTooltip("%s", text.c_str());
    }
}

// What one part of a row says on hover, falling back to what the row itself says, so a caller can
// ask for any part without first checking whether this row has one.
std::string TipOf(const BmScreen& screen, const BmRow& row, int32_t part) {
    const BmTooltip* fallback = nullptr;
    for (int32_t i = 0; i < row.tooltipCount; i++) {
        const BmTooltip& tooltip = screen.tooltips[row.tooltipOffset + i];
        if (tooltip.part == part) {
            return Str(screen, tooltip.text);
        }

        if (tooltip.part == BM_PART_ROW) {
            fallback = &tooltip;
        }
    }

    return fallback == nullptr ? std::string() : Str(screen, fallback->text);
}

// Before a detail run's icon, on top of the space the run already follows: after a space alone the
// branch's mark sat closer to the pipeline than to the branch it leads.
const float spanIconLead = 4.0f;

// A detail run's icon and the gap a chip leaves between its icon and its text: the part of the run
// before its text, inside its link.
float SpanIconInside() {
    return chipIconSize + chipIconGap;
}

// How far a detail run's icon pushes its text along: the room before it, then the icon and its gap.
float SpanIconWidth() {
    return spanIconLead + SpanIconInside();
}

// A detail run's icon, centred on the line of text that starts at at. A name that was never
// registered draws nothing, and the text still leaves its room, so the columns stay in line.
void DrawSpanIcon(const std::string& icon, ImVec2 at) {
    auto found = g.rowIcons.find(icon);
    if (found == g.rowIcons.end()) {
        return;
    }

    const float top = at.y + (ImGui::GetTextLineHeight() - chipIconSize) / 2.0f;
    ImGui::GetWindowDrawList()->AddImage(static_cast<ImTextureID>(found->second.id), ImVec2(at.x, top), ImVec2(at.x + chipIconSize, top + chipIconSize));
}

// Text that opens something: an invisible button the size of the text, which takes the click from the
// row's selectable beneath it, with the text drawn over it in the link colour and underlined while the
// pointer is over it. An icon, where the run has one, leads the text inside the same button, so a
// click on it is a click on the link, and only the text is underlined. The button is the last item,
// so the cursor is never left moved with nothing submitted after it, which ImGui reports as an error.
void LinkText(const char* begin, const char* end, int32_t row, int32_t kind, const std::string& tip = std::string(), const std::string& icon = std::string()) {
    ImVec2 at = ImGui::GetCursorScreenPos();
    const float lead = icon.empty() ? 0.0f : SpanIconInside();
    ImVec2 size = ImGui::CalcTextSize(begin, end);
    if (ImGui::InvisibleButton("##link", ImVec2(lead + size.x, size.y))) {
        g.input.clickedChipRow = row;
        g.input.clickedChip = kind;
    }

    Hovering(row, kind);

    Tip(tip);
    ImDrawList* draw = ImGui::GetWindowDrawList();
    if (!icon.empty()) {
        DrawSpanIcon(icon, at);
    }

    const ImVec2 textAt(at.x + lead, at.y);
    draw->AddText(textAt, ImGui::GetColorU32(chipText), begin, end);
    if (ImGui::IsItemHovered()) {
        ImGui::SetMouseCursor(ImGuiMouseCursor_Hand);
        draw->AddLine(ImVec2(textAt.x, textAt.y + size.y - 1.0f), ImVec2(textAt.x + size.x, textAt.y + size.y - 1.0f), ImGui::GetColorU32(chipText));
    }
}

// The detail cell run by run: plain text dimmed, links through LinkText, a run's icon before its text.
void DrawDetail(const BmScreen& screen, const BmRow& row, int32_t index) {
    if (row.spanCount <= 0) {
        // An item all the same, since the cursor was moved down to the line of text.
        ImGui::TextUnformatted("");
        return;
    }

    for (int32_t s = 0; s < row.spanCount; s++) {
        const BmSpan& span = screen.spans[row.spanOffset + s];
        if (s > 0) {
            ImGui::SameLine(0.0f, 0.0f);
        }

        const std::string icon = Str(screen, span.icon);
        if (!icon.empty()) {
            // Outside the link, so a click in the room before the mark is not a click on it.
            ImGui::Dummy(ImVec2(spanIconLead, ImGui::GetTextLineHeight()));
            ImGui::SameLine(0.0f, 0.0f);
        }

        if (span.link == BM_CHIP_NONE) {
            if (!icon.empty()) {
                // A branch with no page to open still leads with its mark: the mark is what says
                // where the pipeline's name ends. Room made with a Dummy, since the text after it
                // is an item of its own.
                DrawSpanIcon(icon, ImGui::GetCursorScreenPos());
                ImGui::Dummy(ImVec2(SpanIconInside(), ImGui::GetTextLineHeight()));
                ImGui::SameLine(0.0f, 0.0f);
            }

            ImGui::PushStyleColor(ImGuiCol_Text, dim);
            ImGui::TextUnformatted(Begin(screen, span.text), End(screen, span.text));
            ImGui::PopStyleColor();
            continue;
        }

        ImGui::PushID(s);
        LinkText(
            Begin(screen, span.text),
            End(screen, span.text),
            index,
            span.link,
            TipOf(screen, row, span.link == BM_CHIP_BRANCH ? BM_PART_BRANCH : BM_PART_PIPELINE),
            icon);
        ImGui::PopID();
    }
}

// The counts at the left and the filter box at the right, where the WinForms head puts it. The screen's
// text replaces the box's on every frame it is not typed in, so a filter cleared elsewhere clears here
// too. Escape empties a box with text in it before it lets go of the keyboard, and Enter opens the
// selected build.
void DrawHeader(const BmScreen& screen) {
    const float searchWidth = 260.0f;
    ImVec2 at = ImGui::GetCursorScreenPos();
    float searchX = at.x + std::max(0.0f, ImGui::GetContentRegionAvail().x - searchWidth);
    ImGui::AlignTextToFramePadding();
    // Clipped short of the box, so a long header in a narrow window runs out rather than under it.
    ImGui::PushClipRect(at, ImVec2(searchX - ImGui::GetStyle().ItemSpacing.x, at.y + ImGui::GetFrameHeight()), true);
    ImGui::TextColored(dim, "%s", Str(screen, screen.header).c_str());
    ImGui::PopClipRect();
    ImGui::SameLine();
    ImGui::SetCursorScreenPos(ImVec2(searchX, at.y));
    if (!g.searchActive) {
        g.search = Str(screen, screen.search);
    }

    if (g.focusSearch) {
        ImGui::SetKeyboardFocusHere();
        g.focusSearch = false;
    }

    // The cross sits inside the box's frame rather than beside it, so it reads as part of the box
    // the way the other heads draw it. The box keeps its full width and the cross is allowed to
    // take the clicks on the corner it covers.
    const float clearWidth = ImGui::GetFrameHeight();
    ImGui::SetNextItemWidth(searchWidth);
    ImGui::SetNextItemAllowOverlap();
    ImGuiInputTextFlags flags = ImGuiInputTextFlags_CallbackResize | ImGuiInputTextFlags_EscapeClearsAll | ImGuiInputTextFlags_EnterReturnsTrue;
    if (ImGui::InputTextWithHint("##search", "Filter", g.search.data(), g.search.capacity() + 1, flags, TextResize, &g.search)) {
        g.input.key = BM_KEY_OPEN_BUILD;
    }

    Tip(Str(screen, screen.searchTooltip));
    g.searchActive = ImGui::IsItemActive();
    if (ImGui::IsItemEdited()) {
        g.edits.push_back({BM_SEARCH_FIELD, std::string(g.search.c_str())});
    }

    // The cross that empties the box, in the cell held back for it and only while there is
    // something to empty. Emptying is reported as the edit it is, so the session hears it the way
    // it hears typing. The multiplication sign rather than a heavier cross: the text font is loaded
    // with Latin and a handful of named marks, and this one is inside that.
    if (!g.search.empty()) {
        ImGui::SetCursorScreenPos(ImVec2(searchX + searchWidth - clearWidth, at.y));
        ImGui::PushStyleColor(ImGuiCol_Button, ImVec4(0.0f, 0.0f, 0.0f, 0.0f));
        ImGui::PushStyleColor(ImGuiCol_Text, dim);
        if (ImGui::Button("\xc3\x97##clearsearch", ImVec2(clearWidth, clearWidth))) {
            g.search.clear();
            g.searchActive = false;
            g.edits.push_back({BM_SEARCH_FIELD, std::string()});
        }

        ImGui::PopStyleColor(2);
        if (ImGui::IsItemHovered()) {
            ImGui::SetMouseCursor(ImGuiMouseCursor_Hand);
        }
    }
}

void DrawBuilds(const BmScreen& screen, float bodyHeight) {
    const float headerTop = ImGui::GetCursorPosY();
    DrawHeader(screen);
    ImGui::Separator();
    // What the header took, measured rather than assumed, so the body ends where the footer begins
    // whatever the height of the box in it.
    const float body = bodyHeight - (ImGui::GetCursorPosY() - headerTop);

    float rowHeight = ImGui::GetFrameHeightWithSpacing();
    g.bodyRows = rowHeight > 0 ? static_cast<int>(body / rowHeight) : 1;
    if (g.bodyRows < 1) g.bodyRows = 1;

    float scrollbarWidth = 0.0f;
    bool scrollable = screen.totalRows > g.bodyRows;
    if (scrollable) {
        scrollbarWidth = 18.0f;
    }

    ImGui::BeginChild("body", ImVec2(ImGui::GetContentRegionAvail().x - scrollbarWidth, body), ImGuiChildFlags_None, ImGuiWindowFlags_NoScrollbar | ImGuiWindowFlags_NoScrollWithMouse);
    if (ImGui::IsWindowHovered(ImGuiHoveredFlags_ChildWindows)) {
        // Three rows a notch, keeping the fraction: a touchpad sends a fraction of a notch at a
        // time, and truncating each threw every one of them away.
        g.wheel += ImGui::GetIO().MouseWheel * 3.0f;
        const int32_t rows = static_cast<int32_t>(g.wheel);
        g.wheel -= static_cast<float>(rows);
        g.input.scrollDelta -= rows;
    } else {
        g.wheel = 0.0f;
    }

    ImGuiTableFlags flags = ImGuiTableFlags_SizingStretchProp | ImGuiTableFlags_NoPadOuterX;
    float tableWidth = ImGui::GetContentRegionAvail().x;
    if (screen.rowCount == 0 && screen.loading) {
        // An arc turning once a second, from the clock: while it shows, the window is drawn every frame.
        ImDrawList* spinner = ImGui::GetWindowDrawList();
        ImVec2 at = ImGui::GetCursorScreenPos();
        float radius = 8.0f;
        float lineHeight = ImGui::GetTextLineHeight();
        float start = static_cast<float>(std::fmod(GetTime(), 1.0)) * 2.0f * pi;
        spinner->PathArcTo(ImVec2(at.x + radius + 2.0f, at.y + lineHeight / 2.0f), radius, start, start + 1.5f * pi, 24);
        spinner->PathStroke(ImGui::GetColorU32(dim), ImDrawFlags_None, 2.5f);
        ImGui::Dummy(ImVec2(2.0f * radius + 4.0f, lineHeight));
        ImGui::SameLine();
        ImGui::TextColored(dim, "%s", Str(screen, screen.empty).c_str());
    } else if (screen.rowCount == 0) {
        ImGui::TextColored(dim, "%s", Str(screen, screen.empty).c_str());
    } else if (ImGui::BeginTable("rows", 6, flags)) {
        const ImGuiStyle& style = ImGui::GetStyle();
        // Short of the row's height rather than the sixteen a chip's icon is: the marks carry
        // detail, a Jenkins butler's face and the play badge on the Actions mark, that a sixteen
        // pixel square turned to a smudge.
        const float iconSize = rowHeight - 6.0f;
        // Reserved on every row once any row has one, so a group's row, which has no provider
        // logo, keeps its name in line with the rows under it, and the names line up where a
        // provider gave no repository URL to read a host mark from. Any row of the page rather
        // than of those in view, or the columns move as a row with one scrolls in or out.
        const bool anyIcon = screen.detailMarks != 0;
        const bool anyMark = screen.nameMarks != 0;
        const float markWidth = anyMark ? iconSize + style.ItemSpacing.x : 0.0f;

        // The name cell starts with a status square a row height wide, then is as wide as the widest
        // name across every row, not only those on screen, so it does not shift while scrolling.
        // Each name as the kind of row that shows it: a build's behind the mark, a member's
        // indented as well by the width of the arrow its group is drawn behind, and a group's
        // behind its arrow, with the mark where its row has one. Every name taken as a member's
        // gave the column an indent's width that no row drew in.
        // The indent is the open arrow's width, as it is drawn, and a group's name is measured
        // apart from it, as it is drawn: sized as the one text "v name", the column was measured
        // with a letter the row does not draw, and the name moved as the arrow changed.
        const float indent = screen.groupNameCount > 0 ? ImGui::CalcTextSize("▼ ").x : 0.0f;
        float nameText = 0.0f;
        const BmString* sized = screen.names;
        for (int32_t i = 0; i < screen.nameCount; i++, sized++) {
            nameText = std::max(nameText, markWidth + ImGui::CalcTextSize(Str(screen, *sized).c_str()).x);
        }

        for (int32_t i = 0; i < screen.groupNameCount; i++, sized++) {
            nameText = std::max(nameText, indent + ImGui::CalcTextSize(Str(screen, *sized).c_str()).x);
        }

        // A member's blank cell draws nothing, so it wants no room.
        for (int32_t i = 0; i < screen.memberNameCount; i++, sized++) {
            if (sized->length > 0) {
                nameText = std::max(nameText, markWidth + indent + ImGui::CalcTextSize(Str(screen, *sized).c_str()).x);
            }
        }

        for (int32_t i = 0; i < screen.markedGroupNameCount; i++, sized++) {
            nameText = std::max(nameText, markWidth + indent + ImGui::CalcTextSize(Str(screen, *sized).c_str()).x);
        }

        // The detail cell likewise, up to forty characters: past that a long pipeline or branch is cut
        // short rather than pushing every row's chips into the drop down. The details are text
        // alone, so the branch's mark is added to those that carry one.
        const float longestDetail = ImGui::CalcTextSize("0000000000000000000000000000000000000000").x;
        float detailText = 0.0f;
        for (int32_t i = 0; i < screen.detailCount; i++) {
            detailText = std::max(detailText, std::min(longestDetail, ImGui::CalcTextSize(Str(screen, screen.details[i]).c_str()).x));
        }

        for (int32_t i = 0; i < screen.markedDetailCount; i++) {
            const std::string detail = Str(screen, screen.details[screen.detailCount + i]);
            detailText = std::max(detailText, SpanIconWidth() + std::min(longestDetail, ImGui::CalcTextSize(detail.c_str()).x));
        }

        // The author of a failed build, as wide as the widest name up to twenty characters, and no
        // width when no failed build names anyone.
        float authorWidth = 0.0f;
        for (int32_t i = 0; i < screen.authorCount; i++) {
            authorWidth = std::max(authorWidth, ImGui::CalcTextSize(Str(screen, screen.authors[i]).c_str()).x);
        }

        authorWidth = std::min(authorWidth, ImGui::CalcTextSize("00000000000000000000").x);

        // Measured rather than fixed, so each cell holds its widest text at whatever size the font
        // was loaded: a countdown past an hour, and the widest set of chips a row carries.
        const float barWidth = 104.0f;
        const float timingWidth = ImGui::CalcTextSize("0:00:00 left").x;
        // Every chip the widest row can carry, with a gap between each, so the columns before
        // them do not move as builds gain and lose chips: a failed pull request build with a
        // checkout carries every one of them. Cancel is not among them; it never shares a row
        // with Retry, and a row that has it has nothing else.
        const float widestChips = ChipWidth("pull-request", "9999") +
                                  ChipWidth("retry", nullptr) +
                                  ChipWidth("log", nullptr) +
                                  ChipWidth("folder", nullptr) +
                                  ChipWidth("triage", nullptr) +
                                  4.0f * style.ItemSpacing.x;
        const float overflowWidth = ChipWidth(nullptr, overflowLabel);
        // Each boundary between the six columns carries cell padding on both sides of it. What is
        // left, the name, the detail, the bar and the chips share.
        // A column asked to be no width is still given the table's minimum, which is the frame
        // padding. Left out of the sums, the bar's column, hidden, and the author's, with no one
        // to name, took that much from the detail, the one column that stretches, and the widest
        // detail was cut short by it: "feature/inline" lost its last letter.
        const float minimumColumn = std::max(1.0f, style.FramePadding.x);
        const float shared = tableWidth - timingWidth - std::max(authorWidth, minimumColumn) - 5.0f * 2.0f * style.CellPadding.x;
        const float nameWanted = rowHeight + style.ItemSpacing.x + nameText + 2.0f * style.CellPadding.x;
        const float detailWanted = (anyIcon ? iconSize + style.ItemSpacing.x : 0.0f) + detailText;
        // The bar gives way before anything else, since the timing beside it says the same: it shows
        // only while the names, the detail and every chip still fit. Hidden, its column is kept at no
        // width, so the columns after it keep their indexes.
        const bool showBar = shared - barWidth - nameWanted - detailWanted >= widestChips;
        const float available = showBar ? shared - barWidth : shared - minimumColumn;
        // Then the chips: a row without room for all of them puts the last behind an
        // overflow chip, rather than the names being cut short. Only once no chip but that one fits
        // do the names shrink.
        const float spare = available - nameWanted - detailWanted;
        const float chipsWidth = spare >= widestChips ? widestChips : std::max(overflowWidth, spare);
        const float names = std::max(120.0f, available - chipsWidth);
        const float minimumDetail = 120.0f;
        const float nameWidth = std::max(40.0f, std::min(nameWanted, names - std::min(minimumDetail, detailWanted)));
        ImGui::TableSetupColumn("name", ImGuiTableColumnFlags_WidthFixed, nameWidth);
        ImGui::TableSetupColumn("detail", ImGuiTableColumnFlags_WidthStretch, 1.0f);
        ImGui::TableSetupColumn("bar", ImGuiTableColumnFlags_WidthFixed, showBar ? barWidth : 0.0f);
        ImGui::TableSetupColumn("timing", ImGuiTableColumnFlags_WidthFixed, timingWidth);
        ImGui::TableSetupColumn("author", ImGuiTableColumnFlags_WidthFixed, authorWidth);
        ImGui::TableSetupColumn("chips", ImGuiTableColumnFlags_WidthFixed, chipsWidth);
        g.overflowAnchors.assign(static_cast<size_t>(screen.rowCount), ImVec2(-1.0f, -1.0f));

        // Every cell is centred in its row, as the WinForms canvas centres its text, rather than hung
        // from the top of it, where a row taller than its chips leaves the text above the chips.
        const float cellHeight = rowHeight - 2.0f * style.CellPadding.y;
        const float textOffset = (cellHeight - ImGui::GetTextLineHeight()) / 2.0f;
        const float chipOffset = (cellHeight - ChipHeight()) / 2.0f;
        ImGui::PushStyleVar(ImGuiStyleVar_SelectableTextAlign, ImVec2(0.0f, 0.5f));
        for (int32_t i = 0; i < screen.rowCount; i++) {
            const BmRow& row = screen.rows[i];
            bool group = (row.flags & BM_ROW_GROUP) != 0;
            bool selected = (row.flags & BM_ROW_SELECTED) != 0;
            ImGui::PushID(i);
            ImGui::TableNextRow(ImGuiTableRowFlags_None, rowHeight);
            if (selected) {
                ImGui::TableSetBgColor(ImGuiTableBgTarget_RowBg0, ImGui::GetColorU32(selectedRow));
            }

            ImGui::TableSetColumnIndex(0);
            // The arrow is drawn before the name but is no part of it: it is what opens and
            // closes the group, so it rides on the selectable rather than the link and a click on
            // it reaches the row. Inside the link it left a closed group with no way to expand.
            std::string arrow;
            if (group) {
                arrow = (row.flags & BM_ROW_EXPANDED) ? "▼ " : "▶ ";
            }

            std::string name = Str(screen, row.name);

            ImVec4 colour = StatusColour(row.status);
            ImDrawList* draw = ImGui::GetWindowDrawList();
            ImVec2 cursor = ImGui::GetCursorScreenPos();
            // The full height of the row and flush with its neighbours, so a run of rows in one status
            // reads as one block rather than a column of dots. A cell's cursor starts the cell padding
            // below the top of its row, and every row is exactly rowHeight tall.
            float top = cursor.y - style.CellPadding.y;
            draw->AddRectFilled(ImVec2(cursor.x, top), ImVec2(cursor.x + rowHeight, top + rowHeight), ImGui::GetColorU32(colour));
            // Over the row's selectable, which allows overlap, so the square opens the run rather
            // than selecting the row. A group's square reports nothing: it stands for several runs.
            if (row.statusLink != BM_CHIP_NONE) {
                ImGui::SetCursorScreenPos(ImVec2(cursor.x, top));
                ImGui::PushID(i);
                if (ImGui::InvisibleButton("##status", ImVec2(rowHeight, rowHeight))) {
                    g.input.clickedChipRow = i;
                    g.input.clickedChip = row.statusLink;
                }

                Hovering(i, row.statusLink);
                if (ImGui::IsItemHovered()) {
                    ImGui::SetMouseCursor(ImGuiMouseCursor_Hand);
                }

                Tip(TipOf(screen, row, BM_PART_STATUS));
                ImGui::PopID();
                ImGui::SetCursorScreenPos(cursor);
            }

            ImGui::SetCursorPosX(ImGui::GetCursorPosX() + rowHeight + style.ItemSpacing.x);
            // A member starts where its group's name does, under the group rather than beside it.
            if ((row.flags & BM_ROW_MEMBER) != 0) {
                ImGui::SetCursorPosX(ImGui::GetCursorPosX() + indent);
            }

            // A name that opens the run is drawn as a link over the selectable rather than as its
            // label, and only its text is the link, so a click beside it still selects the row.
            bool nameLink = row.nameLink != BM_CHIP_NONE;
            ImVec2 nameAt = ImGui::GetCursorScreenPos();
            // The label carries the name only where nothing before it is drawn by hand. A mark
            // before the name is, and a label cannot be told to start after it.
            // The mark's width is reserved on every row once any row has one, so the names line up
            // where a provider gave no repository URL. Not on a group with no mark of its own: its
            // arrow already stands where the mark would, and a prefix group, which names no one
            // repository, read as an indented heading.
            const float rowMarkWidth = (!arrow.empty() && row.nameIcon.length == 0) ? 0.0f : markWidth;
            // Nor a group's, which starts the indent after the arrow rather than where whichever
            // arrow is drawn happens to end.
            bool nameDrawn = nameLink || rowMarkWidth > 0.0f || group;
            const std::string shownLabel = nameDrawn ? arrow : arrow + name;
            std::string selectableLabel = shownLabel + "##row";
            // As tall as the cell, so a click anywhere on the row selects it. A click on a group
            // toggles it, so the second press of a double click is dropped, or it would close what
            // the first opened.
            const ImGuiSelectableFlags rowFlags = ImGuiSelectableFlags_SpanAllColumns | ImGuiSelectableFlags_AllowOverlap;
            const bool rowPressed = ph::Marked(shownLabel)
                ? ph::SelectableLabel(shownLabel, false, rowFlags, ImVec2(0.0f, cellHeight))
                : ImGui::Selectable(selectableLabel.c_str(), false, rowFlags, ImVec2(0.0f, cellHeight));
            if (rowPressed &&
                !(group && ImGui::IsMouseDoubleClicked(ImGuiMouseButton_Left))) {
                g.input.clickedRow = i;
            }

            // On the selectable, which spans the row, so anywhere the row does not name something
            // more specific says what the row could not fit.
            Tip(TipOf(screen, row, BM_PART_ROW));

            if (ImGui::IsItemClicked(ImGuiMouseButton_Right)) {
                g.input.rightClickedRow = i;
            }

            if (nameDrawn) {
                // The indent rather than this arrow's own width, which is what the column was
                // sized with, so the name does not move as the group opens and its arrow changes.
                float arrowWidth = arrow.empty() ? 0.0f : indent;
                float markLeft = nameAt.x + arrowWidth;
                // The host's mark leads the name, and opens what the name does: it stands for the
                // same page, so a click on it is not a click on nothing.
                auto mark = g.rowIcons.find(Str(screen, row.nameIcon));
                if (row.nameIcon.length > 0 && mark != g.rowIcons.end()) {
                    float markTop = top + (rowHeight - iconSize) / 2.0f;
                    draw->AddImage(static_cast<ImTextureID>(mark->second.id), ImVec2(markLeft, markTop), ImVec2(markLeft + iconSize, markTop + iconSize));
                    if (nameLink) {
                        ImGui::SetCursorScreenPos(ImVec2(markLeft, markTop));
                        ImGui::PushID("mark");
                        if (ImGui::InvisibleButton("##repo", ImVec2(iconSize, iconSize))) {
                            g.input.clickedChipRow = i;
                            g.input.clickedChip = row.nameLink;
                        }

                        Hovering(i, row.nameLink);
                        if (ImGui::IsItemHovered()) {
                            ImGui::SetMouseCursor(ImGuiMouseCursor_Hand);
                        }

                        Tip(TipOf(screen, row, BM_PART_NAME));
                        ImGui::PopID();
                    }
                }

                ImVec2 textAt(markLeft + rowMarkWidth, nameAt.y + textOffset);
                if (nameLink) {
                    ImGui::SetCursorScreenPos(textAt);
                    ImGui::PushID("name");
                    LinkText(name.c_str(), name.c_str() + name.size(), i, row.nameLink, TipOf(screen, row, BM_PART_NAME));
                    ImGui::PopID();
                } else {
                    // Drawn where the label would have gone, without moving ImGui's cursor there:
                    // a cursor left past the end of a cell with no item after it is ImGui's
                    // "submit an item e.g. Dummy() afterwards" error, over the whole window.
                    draw->AddText(textAt, ImGui::GetColorU32(ImGuiCol_Text), name.c_str(), name.c_str() + name.size());
                }
            }

            ImGui::TableSetColumnIndex(1);
            // The logo leads the detail cell, beside the pipeline it ran, so a group's members,
            // whose first cell is empty, still show which service each came from.
            if (anyIcon) {
                auto icon = g.rowIcons.find(Str(screen, row.detailIcon));
                ImVec2 at = ImGui::GetCursorScreenPos();
                if (row.detailIcon.length > 0 && icon != g.rowIcons.end()) {
                    float iconTop = top + (rowHeight - iconSize) / 2.0f;
                    draw->AddImage(static_cast<ImTextureID>(icon->second.id), ImVec2(at.x, iconTop), ImVec2(at.x + iconSize, iconTop + iconSize));
                    // Over the row's selectable, which allows overlap, so the mark opens what
                    // it stands for rather than selecting the row. The cursor goes back after.
                    ImGui::SetCursorScreenPos(ImVec2(at.x, iconTop));
                    ImGui::PushID(i);
                    if (ImGui::InvisibleButton("##detailIcon", ImVec2(iconSize, iconSize))) {
                        g.input.clickedChipRow = i;
                        g.input.clickedChip = row.detailIconLink;
                    }

                    Hovering(i, row.detailIconLink);

                    Tip(TipOf(screen, row, BM_PART_DETAIL_ICON));
                    ImGui::PopID();
                    ImGui::SetCursorScreenPos(at);
                }

                ImGui::SetCursorPosX(ImGui::GetCursorPosX() + iconSize + style.ItemSpacing.x);
            }

            ImGui::SetCursorPosY(ImGui::GetCursorPosY() + textOffset);
            // Cut off at the cell's edge rather than the column's, which is only the cell padding
            // short of the bar, so a pipeline and branch too long for the cell stop short of it.
            ImVec2 detailAt = ImGui::GetCursorScreenPos();
            ImGui::PushClipRect(ImVec2(detailAt.x, top), ImVec2(detailAt.x + ImGui::GetContentRegionAvail().x, top + rowHeight), true);
            DrawDetail(screen, row, i);
            ImGui::PopClipRect();
            ImGui::TableSetColumnIndex(2);
            if (showBar && row.progress >= 0.0f) {
                // Drawn rather than submitted as a ProgressBar. That is a framed item, which moves the
                // row's text baseline down by the frame padding, and the timing text in the next cell
                // would follow it to sit below the rest of the row.
                ImVec2 trackMin(ImGui::GetCursorScreenPos().x, top + (rowHeight - 8.0f) / 2.0f);
                float filled = 94.0f * std::min(row.progress, 1.0f);
                draw->AddRectFilled(trackMin, ImVec2(trackMin.x + 94.0f, trackMin.y + 8.0f), ImGui::GetColorU32(barTrack), style.FrameRounding);
                draw->AddRectFilled(trackMin, ImVec2(trackMin.x + filled, trackMin.y + 8.0f), ImGui::GetColorU32(StatusColour(BM_STATUS_RUNNING)), style.FrameRounding);
            }

            ImGui::TableSetColumnIndex(3);
            ImGui::SetCursorPosY(ImGui::GetCursorPosY() + textOffset);
            ImGui::TextColored(dim, "%s", Str(screen, row.timing).c_str());
            Tip(TipOf(screen, row, BM_PART_TIMING));
            ImGui::TableSetColumnIndex(4);
            if (row.author.length > 0) {
                ImGui::SetCursorPosY(ImGui::GetCursorPosY() + textOffset);
                ImGui::TextUnformatted(Str(screen, row.author).c_str());
            }

            ImGui::TableSetColumnIndex(5);
            // Moved down only when a chip follows. A cursor moved with no item submitted after it is
            // an error to ImGui, which it reports with a tooltip over the window.
            if (row.chipCount > 0) {
                ImGui::SetCursorPosY(ImGui::GetCursorPosY() + chipOffset);
            }

            // The chips that fit, from the left, then an overflow chip in place of the rest. A chip is
            // drawn only with room after it for the overflow chip, unless it is the last, so the
            // overflow chip always fits where the first chip that did not would have gone.
            const float chipsRight = ImGui::GetCursorScreenPos().x + chipsWidth;
            for (int32_t c = 0; c < row.chipCount; c++) {
                const BmChip& item = screen.chips[row.chipOffset + c];
                std::string icon = Str(screen, item.icon);
                std::string label = Str(screen, item.text);
                if (c > 0) {
                    ImGui::SameLine();
                }

                ImGui::PushID(c);
                const float reserve = c == row.chipCount - 1 ? 0.0f : style.ItemSpacing.x + overflowWidth;
                const float width = ChipWidth(icon.c_str(), label.c_str());
                if (ImGui::GetCursorScreenPos().x + width + reserve > chipsRight) {
                    if (Chip(nullptr, overflowLabel, chip, text)) {
                        g.input.clickedOverflowRow = i;
                        g.input.overflowFrom = item.kind;
                    }

                    Hovering(i, item.kind);

                    Tip("More actions");

                    g.overflowAnchors[static_cast<size_t>(i)] = ImVec2(ImGui::GetItemRectMin().x, ImGui::GetItemRectMax().y);
                    ImGui::PopID();
                    break;
                }

                const bool clicked = Chip(icon.c_str(), label.c_str(), ChipColour(item.kind), ChipTextColour(item.kind));
                if (clicked) {
                    g.input.clickedChipRow = i;
                    g.input.clickedChip = item.kind;
                }

                Hovering(i, item.kind);

                Tip(Str(screen, item.tooltip));

                ImGui::PopID();
            }

            ImGui::PopID();
        }

        ImGui::PopStyleVar();
        ImGui::EndTable();
    }

    ImGui::EndChild();

    if (scrollable) {
        ImGui::SameLine();
        int maximum = screen.totalRows - g.bodyRows;
        int value = maximum - screen.scrollTop;
        if (value < 0) value = 0;
        ImGui::PushStyleColor(ImGuiCol_FrameBg, surface);
        ImGui::PushStyleColor(ImGuiCol_SliderGrab, border);
        if (ImGui::VSliderInt("##scroll", ImVec2(14.0f, body), &value, 0, maximum, "")) {
            g.input.scrollTo = maximum - value;
        }

        ImGui::PopStyleColor(2);
    }

    // The context menu, as a popup the managed side opened by sending menu items.
    if (screen.menuCount > 0) {
        bool overflow = screen.menuOverflow != 0;
        if (!g.menuOpen || g.menuRow != screen.menuRow || g.menuOverflow != overflow) {
            ImGui::OpenPopup("row_menu");
            g.menuOpen = true;
            g.menuRow = screen.menuRow;
            g.menuOverflow = overflow;
        }

        // The drop down of an overflow chip hangs under that chip; a context menu opens at the pointer.
        if (overflow && screen.menuRow >= 0 && screen.menuRow < static_cast<int32_t>(g.overflowAnchors.size()) &&
            g.overflowAnchors[static_cast<size_t>(screen.menuRow)].x >= 0.0f) {
            ImGui::SetNextWindowPos(g.overflowAnchors[static_cast<size_t>(screen.menuRow)], ImGuiCond_Appearing);
        }

        if (ImGui::BeginPopup("row_menu")) {
            for (int32_t i = 0; i < screen.menuCount; i++) {
                if ((screen.menu[i].flags & BM_MENU_SEPARATOR_ABOVE) != 0) {
                    ImGui::Separator();
                }

                ImGui::PushID(i);
                const std::string menuLabel = Str(screen, screen.menu[i].label);
                if (ph::Marked(menuLabel) ? ph::SelectableLabel(menuLabel) : ImGui::MenuItem(menuLabel.c_str())) {
                    g.input.clickedMenuItem = i;
                }

                ImGui::PopID();
            }

            ImGui::EndPopup();
        } else if (g.menuOpen) {
            g.input.menuClosed = 1;
            g.menuOpen = false;
        }
    } else {
        g.menuOpen = false;
        g.menuRow = -1;
        g.menuOverflow = false;
    }
}

// A label beside a box: level with the text in the box, and wrapped short of the box when it is
// wider than the column the boxes line up at.
// On the button beside a directory's box. Composed here rather than carried in the frame because
// no other head spells it differently either.
const char* const browseLabel = "Browse";

void BoxLabel(const std::string& label, float boxX) {
    ImGui::AlignTextToFramePadding();
    ImGui::PushTextWrapPos(boxX - ImGui::GetStyle().ItemSpacing.x);
    ImGui::TextColored(dim, "%s", label.c_str());
    ImGui::PopTextWrapPos();
    ImGui::SameLine(boxX);
}

void DrawForm(const BmScreen& screen, float bodyHeight) {
    ImGui::TextColored(dim, "%s", Str(screen, screen.formTitle).c_str());
    ImGui::Separator();
    ImGui::BeginChild("form", ImVec2(0, bodyHeight - ImGui::GetFrameHeight()), ImGuiChildFlags_None, ImGuiWindowFlags_None);
    // The boxes line up a gap past the widest label beside one, like an auto sized column, rather
    // than at a fixed offset that a label outgrows at a larger font and runs under its box. Never
    // so far right that the widest box would leave the window: a label wider than that wraps.
    float labelWidth = 0.0f;
    for (int32_t i = 0; i < screen.fieldCount; i++) {
        int32_t kind = screen.fields[i].kind;
        if (kind == BM_FIELD_TEXT || kind == BM_FIELD_PASSWORD || kind == BM_FIELD_NUMBER || kind == BM_FIELD_SELECT || kind == BM_FIELD_DIRECTORY) {
            labelWidth = std::max(labelWidth, ImGui::CalcTextSize(Str(screen, screen.fields[i].label).c_str()).x);
        }
    }

    const float boxX = std::min(labelWidth + 2.0f * ImGui::GetStyle().ItemSpacing.x, std::max(220.0f, ImGui::GetContentRegionAvail().x - 420.0f));
    for (int32_t i = 0; i < screen.fieldCount; i++) {
        const BmField& field = screen.fields[i];
        std::string id = Str(screen, field.id);
        std::string label = Str(screen, field.label);
        std::string value = Str(screen, field.value);
        bool enabled = (field.flags & BM_FIELD_ENABLED) != 0;
        ImGui::PushID(i);
        ImGui::BeginDisabled(!enabled && field.kind != BM_FIELD_LABEL && field.kind != BM_FIELD_LINK);
        switch (field.kind) {
            case BM_FIELD_CHECKBOX: {
                bool checked = value == "true";
                if (ImGui::Checkbox(label.c_str(), &checked)) {
                    g.edits.push_back({i, checked ? "true" : "false"});
                }

                break;
            }
            case BM_FIELD_TEXT:
            case BM_FIELD_PASSWORD:
            case BM_FIELD_NUMBER:
            case BM_FIELD_DIRECTORY: {
                std::string& buffer = g.buffers[id];
                if (g.activeField != id) {
                    buffer = value;
                }

                BoxLabel(label, boxX);
                // A directory's button comes out of the box's width rather than off the end of the
                // row, so the field still ends where every other one does.
                float boxWidth = 420.0f;
                if (field.kind == BM_FIELD_DIRECTORY) {
                    const ImGuiStyle& formStyle = ImGui::GetStyle();
                    boxWidth -= ImGui::CalcTextSize(browseLabel).x + 2.0f * formStyle.FramePadding.x + formStyle.ItemSpacing.x;
                }

                ImGui::SetNextItemWidth(field.kind == BM_FIELD_NUMBER ? 100.0f : boxWidth);
                ImGuiInputTextFlags flags = ImGuiInputTextFlags_CallbackResize;
                if (field.kind == BM_FIELD_PASSWORD) flags |= ImGuiInputTextFlags_Password;
                if (field.kind == BM_FIELD_NUMBER) flags |= ImGuiInputTextFlags_CharsDecimal;
                std::string hint = Str(screen, field.hint);
                ImGui::InputTextWithHint("##value", hint.c_str(), buffer.data(), buffer.capacity() + 1, flags, TextResize, &buffer);
                if (ImGui::IsItemActive()) {
                    g.activeField = id;
                } else if (g.activeField == id) {
                    g.activeField.clear();
                }

                if (ImGui::IsItemEdited()) {
                    g.edits.push_back({i, std::string(buffer.c_str())});
                }

                // Beside the box, not under it: a path is picked far more often than typed. The
                // click is reported against the field itself, which has no other click to confuse
                // it with.
                if (field.kind == BM_FIELD_DIRECTORY) {
                    ImGui::SameLine();
                    if (ImGui::Button(browseLabel)) {
                        g.input.clickedField = i;
                    }
                }

                // Beside the box, wrapped where the widest box ends rather than cut short: a note
                // is read whatever the box holds. Past a box that already reaches that edge it
                // still gets a readable width, rather than a word to a line.
                std::string note = Str(screen, field.note);
                if (!note.empty()) {
                    ImGui::SameLine();
                    ImGui::PushTextWrapPos(std::max(ImGui::GetCursorPosX() + 160.0f, boxX + 420.0f));
                    ImGui::TextColored(dim, "%s", note.c_str());
                    ImGui::PopTextWrapPos();
                }

                break;
            }
            case BM_FIELD_SELECT: {
                BoxLabel(label, boxX);
                ImGui::SetNextItemWidth(260.0f);
                if (ImGui::BeginCombo("##select", value.c_str())) {
                    for (int32_t o = 0; o < field.optionCount; o++) {
                        std::string option = Str(screen, screen.options[field.optionOffset + o]);
                        // An identity of its own, or two options that read the same were one item.
                        ImGui::PushID(o);
                        if (ph::SelectableLabel(option, option == value)) {
                            g.edits.push_back({i, option});
                        }

                        ImGui::PopID();
                    }

                    ImGui::EndCombo();
                }

                break;
            }
            case BM_FIELD_BUTTON:
                if (ImGui::Button(label.c_str())) {
                    g.input.clickedField = i;
                }

                break;
            case BM_FIELD_LINK:
                ImGui::TextColored(chipText, "%s", label.c_str());
                if (ImGui::IsItemHovered()) {
                    ImGui::SetMouseCursor(ImGuiMouseCursor_Hand);
                }

                if (ImGui::IsItemClicked()) {
                    g.input.clickedField = i;
                }

                break;
            case BM_FIELD_LIST_ROW: {
                std::string line = label.empty() ? value : label + ": " + value;
                ImGui::TextUnformatted(line.c_str());
                ImGui::SameLine();
                if (ImGui::SmallButton("x")) {
                    g.input.clickedField = i;
                }

                break;
            }
            case BM_FIELD_EDIT_ROW: {
                std::string line = label.empty() ? value : label + ": " + value;
                // Drawn as a link, the same as BM_FIELD_LINK: a Selectable shows nothing until it
                // is hovered, so a row that opens an editor read as plain text until the pointer
                // happened to cross it.
                ImGui::TextColored(chipText, "%s", line.c_str());
                if (ImGui::IsItemHovered()) {
                    ImGui::SetMouseCursor(ImGuiMouseCursor_Hand);
                }

                if (ImGui::IsItemClicked()) {
                    g.input.clickedField = i;
                }

                break;
            }
            default: {
                // A label alone is a heading over the fields below it: set apart from the field
                // above, or it read as one more of them, and without a colon pointing at nothing.
                bool heading = !label.empty() && value.empty();
                if (heading && i > 0) {
                    ImGui::Dummy(ImVec2(0.0f, ImGui::GetTextLineHeight() * 0.5f));
                }

                std::string line = heading ? label : label.empty() ? value : label + ": " + value;
                ImGui::PushStyleColor(ImGuiCol_Text, id == "error" ? errorText : text);
                ImGui::TextWrapped("%s", line.c_str());
                ImGui::PopStyleColor();
                break;
            }
        }

        ImGui::EndDisabled();
        ImGui::PopID();
    }

    ImGui::EndChild();
}

void DrawFooter(const BmScreen& screen) {
    ImGui::Separator();
    for (int32_t i = 0; i < screen.buttonCount; i++) {
        const BmButton& button = screen.buttons[i];
        ImGui::PushID(1000 + i);
        ImGui::BeginDisabled((button.flags & BM_BUTTON_ENABLED) == 0);
        std::string label = Str(screen, button.label);
        // One width for every short label, so a row of buttons lines up, and wider only for a label
        // that would not fit it.
        float width = std::max(90.0f, ImGui::CalcTextSize(label.c_str()).x + 2.0f * ImGui::GetStyle().FramePadding.x);
        if (ImGui::Button(label.c_str(), ImVec2(width, 0.0f))) {
            g.input.clickedButton = i;
        }

        Tip(Str(screen, button.tooltip));

        ImGui::EndDisabled();
        ImGui::PopID();
        ImGui::SameLine();
    }

    std::string status = Str(screen, screen.status);
    float width = ImGui::CalcTextSize(status.c_str()).x;
    ImGui::SetCursorPosX(ImGui::GetWindowWidth() - width - 12.0f);
    ImGui::TextColored(dim, "%s", status.c_str());
    // A click copies the status, as text drawn by ImGui can not be selected and an error in it can
    // run past the window's edge; a hover shows it whole first.
    Tip(Str(screen, screen.statusTooltip));
    if (ImGui::IsItemHovered()) {
        ImGui::SetMouseCursor(ImGuiMouseCursor_Hand);
    }

    if (ImGui::IsItemClicked(ImGuiMouseButton_Left)) {
        g.input.key = BM_KEY_COPY_STATUS;
    }
}

void Frame(const BmScreen& screen, int width, int height, bool feed) {
    ImGuiIO& io = ImGui::GetIO();
    if (!feed) {
        io.DisplaySize = ImVec2(static_cast<float>(width), static_cast<float>(height));
        io.DeltaTime = 1.0f / 60.0f;
    } else {
        // The size, the time and the input are the runtime's, fed before this: see bm_present.
        // What the pointer is on is decided again by this frame's items. Only on a frame with input
        // fed to it: a capture draws the screen with no pointer in it.
        g.hoveredChipRow = -1;
        g.hoveredChip = BM_CHIP_NONE;
    }

    ImGui::NewFrame();
    ImGui::SetNextWindowPos(ImVec2(0.0f, 0.0f));
    ImGui::SetNextWindowSize(io.DisplaySize);
    ImGuiWindowFlags flags = ImGuiWindowFlags_NoDecoration | ImGuiWindowFlags_NoMove | ImGuiWindowFlags_NoSavedSettings | ImGuiWindowFlags_NoScrollWithMouse | ImGuiWindowFlags_NoScrollbar | ImGuiWindowFlags_NoBringToFrontOnFocus;
    ImGui::Begin("BuildMonitor", nullptr, flags);
    float footer = ImGui::GetFrameHeightWithSpacing() + 12.0f;
    float bodyHeight = ImGui::GetContentRegionAvail().y - footer;
    bool formPage = screen.page == BM_PAGE_FORM;
    if (formPage) {
        // A form page draws no filter box, so the box cannot be what has the keyboard.
        g.searchActive = false;
        DrawForm(screen, bodyHeight);
    } else {
        DrawBuilds(screen, bodyHeight);
    }

    DrawFooter(screen);
    ImGui::End();
    if (feed) {
        g.wantTextInput = io.WantTextInput;
        g.formPage = formPage;
        g.itemActive = ImGui::IsAnyItemActive();
    }

    ImGui::Render();
}

void ApplyStyle(float scale) {
    ImGuiStyle& style = ImGui::GetStyle();
    style = ImGuiStyle();
    if (lightPalette) {
        ImGui::StyleColorsLight();
    } else {
        ImGui::StyleColorsDark();
    }

    style.WindowPadding = ImVec2(10.0f, 8.0f);
    style.FramePadding = ImVec2(8.0f, 4.0f);
    style.ItemSpacing = ImVec2(8.0f, 6.0f);
    style.FrameRounding = 4.0f;
    style.GrabRounding = 4.0f;
    // Longer than the 0.4s default. A row's cells touch, so at the default a pointer crossing one
    // row on its way somewhere else popped a tooltip over every cell it passed.
    style.HoverDelayNormal = 1.2f;
    style.WindowBorderSize = 0.0f;
    style.Colors[ImGuiCol_WindowBg] = background;
    style.Colors[ImGuiCol_ChildBg] = background;
    style.Colors[ImGuiCol_PopupBg] = surface;
    style.Colors[ImGuiCol_FrameBg] = surface;
    style.Colors[ImGuiCol_FrameBgHovered] = hoverRow;
    style.Colors[ImGuiCol_FrameBgActive] = selectedRow;
    style.Colors[ImGuiCol_Text] = text;
    style.Colors[ImGuiCol_TextDisabled] = dim;
    style.Colors[ImGuiCol_Border] = border;
    style.Colors[ImGuiCol_Separator] = border;
    style.Colors[ImGuiCol_Button] = chip;
    style.Colors[ImGuiCol_ButtonHovered] = selectedRow;
    style.Colors[ImGuiCol_ButtonActive] = selectedRow;
    style.Colors[ImGuiCol_Header] = selectedRow;
    style.Colors[ImGuiCol_HeaderHovered] = hoverRow;
    style.Colors[ImGuiCol_HeaderActive] = selectedRow;
    style.Colors[ImGuiCol_CheckMark] = chipText;
    style.Colors[ImGuiCol_SliderGrab] = border;
    style.Colors[ImGuiCol_TableRowBg] = background;
    style.Colors[ImGuiCol_TableRowBgAlt] = background;

    // Under X11 a pixel is a pixel whatever the display, so on one set to twice the size this head
    // was half size beside everything else on the desktop. A capture is drawn at 1.
    if (scale != 1.0f) {
        style.ScaleAllSizes(scale);
        style.FontScaleDpi = scale;
    }
}

// There is no dependable way to ask a Linux desktop for its colour scheme from here, so System
// stays dark, which is what this head always drew.
void UseTheme(int32_t theme) {
    bool light = theme == BM_THEME_LIGHT;
    if (paletteSet && light == lightPalette) {
        return;
    }

    paletteSet = true;
    UsePalette(light);
    ApplyStyle(ph::WindowScale());
}

ImFont* AddFonts() {
    ImFont* font = ph::AddEmbeddedFont(g.font.data(), static_cast<int32_t>(g.font.size()), g.fontPixels, 1.0f);
    // Merged over the text font, so a row's marks come out of it where the text font has no glyph:
    // the text font is a programming face with no emoji in it at all. Merged rather than drawn as
    // a picture because the marks sit inside runs of text, in a branch name and in the author
    // column. The build defines IMGUI_USE_WCHAR32, without which a codepoint above U+FFFF does not
    // survive being decoded and no font could supply it.
    if (font != nullptr && !g.emoji.empty()) {
        // Bounded to the marks it actually carries, so it claims nothing the text font has.
        // Static, because the atlas keeps the pointer.
        static const ImWchar emojiRange[] = {0x1F916, 0x1F916, 0};
        ImFontConfig emojiConfig;
        emojiConfig.FontDataOwnedByAtlas = false;
        emojiConfig.MergeMode = true;
        emojiConfig.GlyphRanges = emojiRange;
        ImGui::GetIO().Fonts->AddFontFromMemoryTTF(g.emoji.data(), static_cast<int>(g.emoji.size()), g.fontPixels, &emojiConfig);
    }

    return font;
}

bool LoadFont() {
    ImGuiIO& io = ImGui::GetIO();
    if (g.font.empty()) {
        g.captureFont = io.Fonts->AddFontDefault();
        return g.captureFont != nullptr;
    }

    // The capture's first, then the window's, which the machine's fonts are merged into for the
    // characters the embedded one lacks: a merge goes into the font added before it.
    g.captureFont = AddFonts();
    ImFont* window = AddFonts();
    if (window == nullptr) {
        return false;
    }

    io.FontDefault = window;
    ph::UseSystemFonts(window);
    return true;
}

// Asks for a font for every character of a screen the window's font cannot draw: every string of
// the frame is in the one blob.
void FindFontsFor(const BmScreen& screen) {
    if (screen.strings != nullptr && screen.stringsLength > 0) {
        ph::FindFontsFor(reinterpret_cast<const char*>(screen.strings), static_cast<size_t>(screen.stringsLength));
    }
}

} // namespace

extern "C" {

BM_API int32_t bm_init(int32_t width, int32_t height, const char* title, const uint8_t* fontTtf, int32_t fontLength, const uint8_t* emojiTtf, int32_t emojiLength, float fontSize, const BmPlacement* placement, int32_t hidden) {
    if (g.initialised) {
        return 1;
    }

    g.width = width;
    g.height = height;
    g.hidden = hidden != 0;
    // An em size on the ABI; ImGui rasterises by pixel height, which for this font is about a
    // third taller.
    g.fontPixels = fontSize * 1.33f;
    if (fontTtf != nullptr && fontLength > 0) {
        g.font.assign(fontTtf, fontTtf + fontLength);
    }

    if (emojiTtf != nullptr && emojiLength > 0) {
        g.emoji.assign(emojiTtf, emojiTtf + emojiLength);
    }

    // Made hidden, placed where it was left and then shown, by the runtime: made visible, it would
    // first appear centred and then jump. See ph::OpenWindow.
    ph::WindowOptions options;
    options.width = width;
    options.height = height;
    options.title = title != nullptr ? title : "BuildMonitor";
    options.hidden = g.hidden;
    ph::Placement remembered;
    if (placement != nullptr && placement->known != 0) {
        remembered.x = placement->x;
        remembered.y = placement->y;
        remembered.width = placement->width;
        remembered.height = placement->height;
        remembered.maximized = placement->maximized != 0;
        options.placement = &remembered;
    }

    if (!ph::OpenWindow(options)) {
        return 0;
    }

    IMGUI_CHECKVERSION();
    g.context = ImGui::CreateContext();
    ImGui::SetCurrentContext(g.context);
    ImGuiIO& io = ImGui::GetIO();
    io.IniFilename = nullptr;
    io.LogFilename = nullptr;
    // ImGui hands its textures, the font atlas among them, to the runtime's renderer to make, grow
    // and drop, so glyphs are rasterised as they are first drawn rather than all up front.
    io.BackendFlags |= ImGuiBackendFlags_RendererHasTextures;
    io.BackendFlags |= ImGuiBackendFlags_RendererHasVtxOffset;
    UseTheme(BM_THEME_DARK);
    if (!LoadFont()) {
        return 0;
    }

    ResetInput();
    g.built = false;
    g.initialised = true;
    return 1;
}

BM_API int32_t bm_present(const BmScreen* screen) {
    if (!g.initialised || g.windowGone || screen == nullptr) {
        return 0;
    }

    UseTheme(screen->theme);
    // Reported once, and cleared, so a window hidden on close can be shown again without closing
    // itself on its first frame back.
    if (ph::TakeCloseRequest()) {
        g.input.closeRequested = 1;
    }

    const float elapsed = ph::BeginPresent(g.context);

    // A frame is built only when it could look different from the last: a screen of another
    // generation, input since the last present, a font merged, an item still active, the spinner,
    // or ImGui settling after any of those. And drawn only when it does. Otherwise the present
    // only waits and reads events: an idle window built a whole frame sixty times a second to put
    // the same pixels on screen. Each of these is asked every time, since each takes what it finds.
    bool arrived = ph::Arrived();
    arrived = ph::TakeFonts() || arrived;
    const bool changed = screen->generation != g.drawnGeneration;
    g.drawnGeneration = screen->generation;
    const bool spinner = screen->page == BM_PAGE_BUILDS && screen->rowCount == 0 && screen->loading != 0;
    arrived = arrived || changed || g.itemActive || spinner;

    // A hidden or minimized window is neither built nor drawn: being shown again marks it to be.
    // Not before a frame has been built at all, since the rows the managed side slices by are
    // measured from one.
    if (ph::Hidden() && g.built) {
        if (changed) {
            FindFontsFor(*screen);
        }

        ph::Rest();
        return 1;
    }

    if (ph::CanLeaveAlone(arrived)) {
        ph::Rest();
        return 1;
    }

    // Keys too: the form's fields and the filter box are ImGui's text fields.
    ph::FeedImGui(elapsed, true);
    FindFontsFor(*screen);
    Frame(*screen, GetScreenWidth(), GetScreenHeight(), true);
    g.built = true;
    const Color clear = ClearColour();
    ph::Show(ImGui::GetDrawData(), clear.r, clear.g, clear.b, arrived);
    ph::Rest();
    return 1;
}

BM_API void bm_poll_input(BmInput* input) {
    if (input == nullptr) {
        return;
    }

    if (!g.edits.empty()) {
        Edit edit = g.edits.front();
        g.edits.pop_front();
        g.changedValue = edit.value;
        g.input.changedField = edit.field;
        g.input.changedValue = reinterpret_cast<const uint8_t*>(g.changedValue.data());
        g.input.changedValueLength = static_cast<int32_t>(g.changedValue.size());
    }

    // A key waits for a poll whose frame set no key of its own, such as a click on the status.
    if (g.initialised && g.input.key == BM_KEY_NONE) {
        g.input.key = ReadKey();
    }

    g.input.rows = g.bodyRows;
    g.input.hoveredChipRow = g.hoveredChipRow;
    g.input.hoveredChip = g.hoveredChip;
    if (g.initialised) {
        Sample();
    }

    *input = g.input;
    ResetInput();
}

BM_API int32_t bm_capture(const BmScreen* screen, int32_t width, int32_t height, const char* pngPath) {
    if (!g.initialised || screen == nullptr || pngPath == nullptr) {
        return 0;
    }

    RenderTexture2D target = LoadRenderTexture(width, height);
    if (target.id == 0) {
        return 0;
    }

    ImGui::SetCurrentContext(g.context);
    UseTheme(screen->theme);
    // At a scale of 1 and in the embedded font alone, whatever display and fonts the machine
    // capturing has, so a capture describes the app and not that machine.
    ImGuiIO& io = ImGui::GetIO();
    ImFont* windowFont = io.FontDefault;
    io.FontDefault = g.captureFont;
    const float scale = ph::WindowScale();
    if (scale != 1.0f) {
        ApplyStyle(1.0f);
    }

    // ImGui sizes a child's scrollbar from the content size of the previous frame, so a single
    // frame would draw a scrollbar the last capture needed and omit one this capture needs. The
    // first frame settles the layout and is discarded.
    Frame(*screen, width, height, false);
    BeginTextureMode(target);
    ClearBackground(ClearColour());
    Frame(*screen, width, height, false);
    ph::RenderDrawData(ImGui::GetDrawData());
    EndTextureMode();
    io.FontDefault = windowFont;
    if (scale != 1.0f) {
        ApplyStyle(scale);
    }

    Image image = LoadImageFromTexture(target.texture);
    ImageFlipVertical(&image);
    bool exported = ExportImage(image, pngPath);
    UnloadImage(image);
    UnloadRenderTexture(target);
    // The window's next frame is drawn whatever it comes out as: the capture's frames were not it.
    ph::MarkStale();
    return exported ? 1 : 0;
}

BM_API void bm_set_hidden(int32_t hidden) {
    if (!g.initialised) {
        return;
    }

    // Shown, a minimized window is brought back, and left maximized if it was: see ph::SetHidden.
    g.hidden = hidden != 0;
    ph::SetHidden(g.hidden);
}

BM_API void bm_focus(void) {
    if (!g.initialised) {
        return;
    }

    // It shows the window too.
    g.hidden = false;
    ph::Focus();
}

BM_API void bm_set_clipboard(const char* text) {
    if (g.initialised) {
        ph::SetClipboard(text);
    }
}

BM_API int32_t bm_pick_directory(const char* start, uint8_t* buffer, int32_t bufferLength) {
    // No panel of this head's own: a file chooser is a lot of desktop convention that Dear ImGui
    // would only approximate, and every Linux desktop already ships one as a program. -1 sends the
    // managed side to zenity or kdialog.
    (void) start;
    (void) buffer;
    (void) bufferLength;
    return -1;
}

BM_API int32_t bm_tray_available(void) {
    return 0;
}

BM_API int32_t bm_tray_init(void) {
    return 0;
}

BM_API void bm_tray_set_icon(int32_t kind, const uint8_t* png, int32_t length) {
}

BM_API void bm_tray_set_menu_icon(const char* name, const uint8_t* png, int32_t length) {
}

BM_API void bm_set_row_icon(const char* name, const uint8_t* png, int32_t length) {
    // A texture needs the GL context bm_init made.
    if (!g.initialised || name == nullptr || png == nullptr || length <= 0) {
        return;
    }

    Image image = LoadImageFromMemory(".png", png, length);
    if (image.data == nullptr) {
        return;
    }

    Texture2D texture = LoadTextureFromImage(image);
    UnloadImage(image);
    // The PNG is drawn at half its size, so filter rather than drop every other pixel.
    SetTextureFilter(texture, TEXTURE_FILTER_BILINEAR);
    auto existing = g.rowIcons.find(name);
    if (existing != g.rowIcons.end()) {
        UnloadTexture(existing->second);
    }

    g.rowIcons[name] = texture;
    ph::MarkStale();
}

BM_API void bm_shutdown(void) {
    if (!g.initialised) {
        return;
    }

    for (auto& icon : g.rowIcons) {
        UnloadTexture(icon.second);
    }

    g.rowIcons.clear();

    ImGui::DestroyContext(g.context);
    g.context = nullptr;
    g.captureFont = nullptr;
    // After the context, whose atlas was still reading glyphs out of the fonts merged in.
    ph::StopFontFinder();
    ph::CloseWindow();
    g.initialised = false;
    g.windowGone = true;
}

BM_API void bm_on_display_lost(BmDisplayLost lost) {
    ph::OnDisplayLost(lost);
}

BM_API int32_t bm_version(void) {
    return BM_VERSION;
}

} // extern "C"
