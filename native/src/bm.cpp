/*
 * The Linux head: raylib for the window, GL context and input; Dear ImGui for the widgets,
 * rendered through rlgl. A renderer for the screen model in bm.h, not a toolkit binding: the
 * managed side owns every decision about what is on screen, this file only draws it and reports
 * what was clicked.
 */
#include "bm.h"

#include "raylib.h"
#include "rlgl.h"
#include "imgui.h"

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
    Texture2D fontTexture{};
    std::unordered_map<std::string, Texture2D> rowIcons;
    BmInput input{};
    std::deque<Edit> edits;
    std::string changedValue;
    std::unordered_map<std::string, std::string> buffers;
    std::string activeField;
    bool menuOpen = false;
    int32_t menuRow = -1;
    int bodyRows = 1;
    bool keyDown[ImGuiKey_NamedKey_END]{};
};

State g;

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

const char* Begin(const BmScreen& screen, const BmString& value) {
    return reinterpret_cast<const char*>(screen.strings) + value.offset;
}

const char* End(const BmScreen& screen, const BmString& value) {
    return Begin(screen, value) + value.length;
}

std::string Str(const BmScreen& screen, const BmString& value) {
    return std::string(Begin(screen, value), static_cast<size_t>(value.length));
}

void ResetInput() {
    g.input.key = BM_KEY_NONE;
    g.input.clickedButton = -1;
    g.input.clickedRow = -1;
    g.input.clickedLinkRow = -1;
    g.input.clickedLink = BM_LINK_NONE;
    g.input.clickedActionRow = -1;
    g.input.clickedAction = BM_ACTION_NONE;
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

// Input: raylib to ImGui

struct KeyMap {
    int raylib;
    ImGuiKey imgui;
};

const KeyMap keyMap[] = {
    {KEY_TAB, ImGuiKey_Tab}, {KEY_LEFT, ImGuiKey_LeftArrow}, {KEY_RIGHT, ImGuiKey_RightArrow},
    {KEY_UP, ImGuiKey_UpArrow}, {KEY_DOWN, ImGuiKey_DownArrow}, {KEY_PAGE_UP, ImGuiKey_PageUp},
    {KEY_PAGE_DOWN, ImGuiKey_PageDown}, {KEY_HOME, ImGuiKey_Home}, {KEY_END, ImGuiKey_End},
    {KEY_INSERT, ImGuiKey_Insert}, {KEY_DELETE, ImGuiKey_Delete}, {KEY_BACKSPACE, ImGuiKey_Backspace},
    {KEY_SPACE, ImGuiKey_Space}, {KEY_ENTER, ImGuiKey_Enter}, {KEY_KP_ENTER, ImGuiKey_KeypadEnter},
    {KEY_ESCAPE, ImGuiKey_Escape},
    {KEY_LEFT_CONTROL, ImGuiKey_LeftCtrl}, {KEY_RIGHT_CONTROL, ImGuiKey_RightCtrl},
    {KEY_LEFT_SHIFT, ImGuiKey_LeftShift}, {KEY_RIGHT_SHIFT, ImGuiKey_RightShift},
    {KEY_LEFT_ALT, ImGuiKey_LeftAlt}, {KEY_RIGHT_ALT, ImGuiKey_RightAlt},
    {KEY_LEFT_SUPER, ImGuiKey_LeftSuper}, {KEY_RIGHT_SUPER, ImGuiKey_RightSuper},
    {KEY_A, ImGuiKey_A}, {KEY_C, ImGuiKey_C}, {KEY_V, ImGuiKey_V}, {KEY_X, ImGuiKey_X},
    {KEY_Y, ImGuiKey_Y}, {KEY_Z, ImGuiKey_Z},
};

void FeedInput(ImGuiIO& io) {
    io.AddMousePosEvent(static_cast<float>(GetMouseX()), static_cast<float>(GetMouseY()));
    io.AddMouseButtonEvent(0, IsMouseButtonDown(MOUSE_BUTTON_LEFT));
    io.AddMouseButtonEvent(1, IsMouseButtonDown(MOUSE_BUTTON_RIGHT));
    io.AddMouseButtonEvent(2, IsMouseButtonDown(MOUSE_BUTTON_MIDDLE));

    for (const KeyMap& map : keyMap) {
        bool down = IsKeyDown(map.raylib);
        bool& previous = g.keyDown[map.imgui - ImGuiKey_NamedKey_BEGIN];
        if (down != previous) {
            previous = down;
            io.AddKeyEvent(map.imgui, down);
        }
    }

    io.AddKeyEvent(ImGuiMod_Ctrl, IsKeyDown(KEY_LEFT_CONTROL) || IsKeyDown(KEY_RIGHT_CONTROL));
    io.AddKeyEvent(ImGuiMod_Shift, IsKeyDown(KEY_LEFT_SHIFT) || IsKeyDown(KEY_RIGHT_SHIFT));
    io.AddKeyEvent(ImGuiMod_Alt, IsKeyDown(KEY_LEFT_ALT) || IsKeyDown(KEY_RIGHT_ALT));
    io.AddKeyEvent(ImGuiMod_Super, IsKeyDown(KEY_LEFT_SUPER) || IsKeyDown(KEY_RIGHT_SUPER));

    for (int character = GetCharPressed(); character != 0; character = GetCharPressed()) {
        io.AddInputCharacter(static_cast<unsigned int>(character));
    }
}

// The keys the app owns, when no text field has the keyboard.
void ReadShortcuts(const ImGuiIO& io, bool formPage) {
    if (io.WantTextInput) {
        return;
    }

    bool control = IsKeyDown(KEY_LEFT_CONTROL) || IsKeyDown(KEY_RIGHT_CONTROL);
    if (IsKeyPressed(KEY_UP)) g.input.key = BM_KEY_PREVIOUS_ROW;
    else if (IsKeyPressed(KEY_DOWN)) g.input.key = BM_KEY_NEXT_ROW;
    else if (IsKeyPressed(KEY_PAGE_UP)) g.input.key = BM_KEY_PAGE_UP;
    else if (IsKeyPressed(KEY_PAGE_DOWN)) g.input.key = BM_KEY_PAGE_DOWN;
    else if (IsKeyPressed(KEY_HOME)) g.input.key = BM_KEY_HOME;
    else if (IsKeyPressed(KEY_END)) g.input.key = BM_KEY_END;
    else if (IsKeyPressed(KEY_F5)) g.input.key = BM_KEY_REFRESH;
    else if (IsKeyPressed(KEY_ESCAPE)) g.input.key = formPage ? BM_KEY_BACK : BM_KEY_HIDE;
    else if (control && IsKeyPressed(KEY_Q)) g.input.key = BM_KEY_QUIT;
    else if (!formPage && control && IsKeyPressed(KEY_C)) g.input.key = BM_KEY_COPY;
    else if (!formPage && IsKeyPressed(KEY_ENTER)) g.input.key = BM_KEY_OPEN_BUILD;
    else if (!formPage && !control && IsKeyPressed(KEY_R)) g.input.key = BM_KEY_RETRY;
}

// Rendering ImGui's draw lists through rlgl.

void RenderDrawData(ImDrawData* data) {
    rlDrawRenderBatchActive();
    rlDisableBackfaceCulling();
    for (int n = 0; n < data->CmdListsCount; n++) {
        const ImDrawList* list = data->CmdLists[n];
        const ImDrawVert* vertices = list->VtxBuffer.Data;
        const ImDrawIdx* indices = list->IdxBuffer.Data;
        for (int c = 0; c < list->CmdBuffer.Size; c++) {
            const ImDrawCmd& cmd = list->CmdBuffer[c];
            if (cmd.UserCallback != nullptr) {
                cmd.UserCallback(list, &cmd);
                continue;
            }

            if (cmd.ElemCount == 0) {
                continue;
            }

            int clipX = static_cast<int>(cmd.ClipRect.x);
            int clipY = static_cast<int>(cmd.ClipRect.y);
            int clipWidth = static_cast<int>(cmd.ClipRect.z - cmd.ClipRect.x);
            int clipHeight = static_cast<int>(cmd.ClipRect.w - cmd.ClipRect.y);
            if (clipWidth <= 0 || clipHeight <= 0) {
                continue;
            }

            BeginScissorMode(clipX, clipY, clipWidth, clipHeight);
            unsigned int texture = static_cast<unsigned int>(cmd.GetTexID());
            rlBegin(RL_TRIANGLES);
            rlSetTexture(texture);
            for (unsigned int i = 0; i < cmd.ElemCount; i += 3) {
                if (rlCheckRenderBatchLimit(3)) {
                    rlBegin(RL_TRIANGLES);
                    rlSetTexture(texture);
                }

                for (unsigned int k = 0; k < 3; k++) {
                    ImDrawIdx index = indices[cmd.IdxOffset + i + k];
                    const ImDrawVert& vertex = vertices[cmd.VtxOffset + index];
                    ImU32 colour = vertex.col;
                    rlColor4ub(
                        static_cast<unsigned char>(colour & 0xFF),
                        static_cast<unsigned char>((colour >> 8) & 0xFF),
                        static_cast<unsigned char>((colour >> 16) & 0xFF),
                        static_cast<unsigned char>((colour >> 24) & 0xFF));
                    rlTexCoord2f(vertex.uv.x, vertex.uv.y);
                    rlVertex2f(vertex.pos.x, vertex.pos.y);
                }
            }

            rlEnd();
            rlSetTexture(0);
            EndScissorMode();
        }
    }

    rlDrawRenderBatchActive();
    rlEnableBackfaceCulling();
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

bool Chip(const char* label, const ImVec4& colour, const ImVec4& foreground) {
    ImGui::PushStyleColor(ImGuiCol_Button, colour);
    ImGui::PushStyleColor(ImGuiCol_ButtonHovered, ImVec4(colour.x + 0.08f, colour.y + 0.08f, colour.z + 0.08f, 1.0f));
    ImGui::PushStyleColor(ImGuiCol_ButtonActive, colour);
    ImGui::PushStyleColor(ImGuiCol_Text, foreground);
    bool clicked = ImGui::SmallButton(label);
    ImGui::PopStyleColor(4);
    return clicked;
}

void DrawBuilds(const BmScreen& screen, float bodyHeight) {
    ImGui::TextColored(dim, "%s", Str(screen, screen.header).c_str());
    ImGui::Separator();

    float rowHeight = ImGui::GetFrameHeightWithSpacing();
    g.bodyRows = rowHeight > 0 ? static_cast<int>((bodyHeight - ImGui::GetFrameHeight()) / rowHeight) : 1;
    if (g.bodyRows < 1) g.bodyRows = 1;

    float scrollbarWidth = 0.0f;
    bool scrollable = screen.totalRows > g.bodyRows;
    if (scrollable) {
        scrollbarWidth = 18.0f;
    }

    ImGui::BeginChild("body", ImVec2(ImGui::GetContentRegionAvail().x - scrollbarWidth, bodyHeight - ImGui::GetFrameHeight()), ImGuiChildFlags_None, ImGuiWindowFlags_NoScrollbar | ImGuiWindowFlags_NoScrollWithMouse);
    if (ImGui::IsWindowHovered(ImGuiHoveredFlags_ChildWindows)) {
        float wheel = GetMouseWheelMove();
        if (wheel != 0.0f) {
            g.input.scrollDelta -= static_cast<int32_t>(wheel * 3);
        }
    }

    ImGuiTableFlags flags = ImGuiTableFlags_SizingStretchProp | ImGuiTableFlags_NoPadOuterX;
    if (screen.rowCount == 0 && screen.loading) {
        // An arc turning once a second, from the clock: the window is drawn every frame anyway.
        ImDrawList* spinner = ImGui::GetWindowDrawList();
        ImVec2 at = ImGui::GetCursorScreenPos();
        float radius = 8.0f;
        float lineHeight = ImGui::GetTextLineHeight();
        float start = static_cast<float>(std::fmod(GetTime(), 1.0)) * 2.0f * IM_PI;
        spinner->PathArcTo(ImVec2(at.x + radius + 2.0f, at.y + lineHeight / 2.0f), radius, start, start + 1.5f * IM_PI, 24);
        spinner->PathStroke(ImGui::GetColorU32(dim), ImDrawFlags_None, 2.5f);
        ImGui::Dummy(ImVec2(2.0f * radius + 4.0f, lineHeight));
        ImGui::SameLine();
        ImGui::TextColored(dim, "Loading builds");
    } else if (screen.rowCount == 0) {
        ImGui::TextColored(dim, "Nothing to show yet.");
    } else if (ImGui::BeginTable("rows", 8, flags)) {
        // The name cell starts with a status square a row height wide, and holds
        // the repository and branch, the longer of the two names, so it takes most of the stretch;
        // the pipeline beside it is usually a short workflow name.
        ImGui::TableSetupColumn("name", ImGuiTableColumnFlags_WidthStretch, 4.2f);
        ImGui::TableSetupColumn("detail", ImGuiTableColumnFlags_WidthStretch, 2.7f);
        ImGui::TableSetupColumn("run", ImGuiTableColumnFlags_WidthFixed, 70.0f);
        ImGui::TableSetupColumn("status", ImGuiTableColumnFlags_WidthFixed, 90.0f);
        ImGui::TableSetupColumn("bar", ImGuiTableColumnFlags_WidthFixed, 104.0f);
        ImGui::TableSetupColumn("timing", ImGuiTableColumnFlags_WidthFixed, 90.0f);
        ImGui::TableSetupColumn("links", ImGuiTableColumnFlags_WidthFixed, 210.0f);
        ImGui::TableSetupColumn("actions", ImGuiTableColumnFlags_WidthFixed, 80.0f);

        // Reserved on every row once any row has an icon, so a group's row, which has none, keeps its
        // name in line with the rows under it.
        const float iconSize = 16.0f;
        bool anyIcon = false;
        for (int32_t i = 0; i < screen.rowCount; i++) {
            anyIcon = anyIcon || screen.rows[i].provider.length > 0;
        }

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
            std::string name = Str(screen, row.name);
            if (group) {
                name = std::string((row.flags & BM_ROW_EXPANDED) ? "v " : "> ") + name;
            }

            ImVec4 colour = StatusColour(row.status);
            ImDrawList* draw = ImGui::GetWindowDrawList();
            ImVec2 cursor = ImGui::GetCursorScreenPos();
            // The full height of the row and flush with its neighbours, so a run of rows in one status
            // reads as one block rather than a column of dots. A cell's cursor starts the cell padding
            // below the top of its row, and every row is exactly rowHeight tall.
            float top = cursor.y - ImGui::GetStyle().CellPadding.y;
            draw->AddRectFilled(ImVec2(cursor.x, top), ImVec2(cursor.x + rowHeight, top + rowHeight), ImGui::GetColorU32(colour));
            ImGui::SetCursorPosX(ImGui::GetCursorPosX() + rowHeight + ImGui::GetStyle().ItemSpacing.x);
            std::string selectableLabel = name + "##row";
            // A click on a group toggles it, so the second press of a double click is dropped, or it
            // would close what the first opened.
            if (ImGui::Selectable(selectableLabel.c_str(), false, ImGuiSelectableFlags_SpanAllColumns | ImGuiSelectableFlags_AllowOverlap) &&
                !(group && ImGui::IsMouseDoubleClicked(ImGuiMouseButton_Left))) {
                g.input.clickedRow = i;
            }

            if (ImGui::IsItemClicked(ImGuiMouseButton_Right)) {
                g.input.rightClickedRow = i;
            }

            ImGui::TableSetColumnIndex(1);
            // The logo leads the detail cell, beside the pipeline it ran, so a group's members,
            // whose first cell is empty, still show which service each came from.
            if (anyIcon) {
                auto icon = g.rowIcons.find(Str(screen, row.provider));
                ImVec2 at = ImGui::GetCursorScreenPos();
                if (row.provider.length > 0 && icon != g.rowIcons.end()) {
                    float iconTop = top + (rowHeight - iconSize) / 2.0f;
                    draw->AddImage(static_cast<ImTextureID>(icon->second.id), ImVec2(at.x, iconTop), ImVec2(at.x + iconSize, iconTop + iconSize));
                }

                ImGui::SetCursorPosX(ImGui::GetCursorPosX() + iconSize + ImGui::GetStyle().ItemSpacing.x);
            }

            ImGui::TextColored(dim, "%s", Str(screen, row.detail).c_str());
            ImGui::TableSetColumnIndex(2);
            ImGui::TextColored(dim, "%s", Str(screen, row.runNumber).c_str());
            ImGui::TableSetColumnIndex(3);
            ImGui::TextColored(colour, "%s", Str(screen, row.statusText).c_str());
            ImGui::TableSetColumnIndex(4);
            if (row.progress >= 0.0f) {
                ImGui::PushStyleColor(ImGuiCol_PlotHistogram, StatusColour(BM_STATUS_RUNNING));
                ImGui::PushStyleColor(ImGuiCol_FrameBg, barTrack);
                ImGui::SetCursorPosY(ImGui::GetCursorPosY() + (ImGui::GetFrameHeight() - 8.0f) / 2.0f);
                ImGui::ProgressBar(row.progress, ImVec2(94.0f, 8.0f), "");
                ImGui::PopStyleColor(2);
            }

            ImGui::TableSetColumnIndex(5);
            ImGui::TextColored(dim, "%s", Str(screen, row.timing).c_str());
            ImGui::TableSetColumnIndex(6);
            if (row.buildLabel.length > 0) {
                if (Chip(Str(screen, row.buildLabel).c_str(), chip, chipText)) {
                    g.input.clickedLinkRow = i;
                    g.input.clickedLink = BM_LINK_BUILD;
                }

                ImGui::SameLine();
            }

            if (row.branchLabel.length > 0) {
                if (Chip(Str(screen, row.branchLabel).c_str(), chip, chipText)) {
                    g.input.clickedLinkRow = i;
                    g.input.clickedLink = BM_LINK_BRANCH;
                }

                ImGui::SameLine();
            }

            if (row.pullRequestLabel.length > 0) {
                if (Chip(Str(screen, row.pullRequestLabel).c_str(), chip, chipText)) {
                    g.input.clickedLinkRow = i;
                    g.input.clickedLink = BM_LINK_PULL_REQUEST;
                }
            }

            ImGui::TableSetColumnIndex(7);
            if (row.flags & BM_ROW_CAN_RETRY) {
                if (Chip("Retry", retryChip, text)) {
                    g.input.clickedActionRow = i;
                    g.input.clickedAction = BM_ACTION_RETRY;
                }

                ImGui::SameLine();
            }

            if (row.flags & BM_ROW_CAN_CANCEL) {
                if (Chip("Cancel", cancelChip, text)) {
                    g.input.clickedActionRow = i;
                    g.input.clickedAction = BM_ACTION_CANCEL;
                }
            }

            ImGui::PopID();
        }

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
        if (ImGui::VSliderInt("##scroll", ImVec2(14.0f, bodyHeight - ImGui::GetFrameHeight()), &value, 0, maximum, "")) {
            g.input.scrollTo = maximum - value;
        }

        ImGui::PopStyleColor(2);
    }

    // The context menu, as a popup the managed side opened by sending menu items.
    if (screen.menuCount > 0) {
        if (!g.menuOpen || g.menuRow != screen.menuRow) {
            ImGui::OpenPopup("row_menu");
            g.menuOpen = true;
            g.menuRow = screen.menuRow;
        }

        if (ImGui::BeginPopup("row_menu")) {
            for (int32_t i = 0; i < screen.menuCount; i++) {
                ImGui::PushID(i);
                if (ImGui::MenuItem(Str(screen, screen.menu[i].label).c_str())) {
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
    }
}

void DrawForm(const BmScreen& screen, float bodyHeight) {
    ImGui::TextColored(dim, "%s", Str(screen, screen.formTitle).c_str());
    ImGui::Separator();
    ImGui::BeginChild("form", ImVec2(0, bodyHeight - ImGui::GetFrameHeight()), ImGuiChildFlags_None, ImGuiWindowFlags_None);
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
            case BM_FIELD_NUMBER: {
                std::string& buffer = g.buffers[id];
                if (g.activeField != id) {
                    buffer = value;
                }

                ImGui::TextColored(dim, "%s", label.c_str());
                ImGui::SameLine(220.0f);
                ImGui::SetNextItemWidth(field.kind == BM_FIELD_NUMBER ? 100.0f : 420.0f);
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

                break;
            }
            case BM_FIELD_SELECT: {
                ImGui::TextColored(dim, "%s", label.c_str());
                ImGui::SameLine(220.0f);
                ImGui::SetNextItemWidth(260.0f);
                if (ImGui::BeginCombo("##select", value.c_str())) {
                    for (int32_t o = 0; o < field.optionCount; o++) {
                        std::string option = Str(screen, screen.options[field.optionOffset + o]);
                        if (ImGui::Selectable(option.c_str(), option == value)) {
                            g.edits.push_back({i, option});
                        }
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
            default: {
                std::string line = label.empty() ? value : label + ": " + value;
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
        if (ImGui::Button(Str(screen, button.label).c_str(), ImVec2(90.0f, 0.0f))) {
            g.input.clickedButton = i;
        }

        ImGui::EndDisabled();
        ImGui::PopID();
        ImGui::SameLine();
    }

    std::string status = Str(screen, screen.status);
    float width = ImGui::CalcTextSize(status.c_str()).x;
    ImGui::SetCursorPosX(ImGui::GetWindowWidth() - width - 12.0f);
    ImGui::TextColored(dim, "%s", status.c_str());
}

void Frame(const BmScreen& screen, int width, int height, bool feed) {
    ImGuiIO& io = ImGui::GetIO();
    io.DisplaySize = ImVec2(static_cast<float>(width), static_cast<float>(height));
    io.DeltaTime = feed ? GetFrameTime() : 1.0f / 60.0f;
    if (io.DeltaTime <= 0.0f) io.DeltaTime = 1.0f / 60.0f;
    if (feed) {
        FeedInput(io);
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
        DrawForm(screen, bodyHeight);
    } else {
        DrawBuilds(screen, bodyHeight);
    }

    DrawFooter(screen);
    ImGui::End();
    if (feed) {
        ReadShortcuts(io, formPage);
    }

    ImGui::Render();
}

void ApplyStyle() {
    if (lightPalette) {
        ImGui::StyleColorsLight();
    } else {
        ImGui::StyleColorsDark();
    }

    ImGuiStyle& style = ImGui::GetStyle();
    style.WindowPadding = ImVec2(10.0f, 8.0f);
    style.FramePadding = ImVec2(8.0f, 4.0f);
    style.ItemSpacing = ImVec2(8.0f, 6.0f);
    style.FrameRounding = 4.0f;
    style.GrabRounding = 4.0f;
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
    ApplyStyle();
}

bool LoadFont() {
    ImGuiIO& io = ImGui::GetIO();
    io.Fonts->Clear();
    if (g.font.empty()) {
        io.Fonts->AddFontDefault();
    } else {
        ImFontConfig config;
        config.FontDataOwnedByAtlas = false;
        io.Fonts->AddFontFromMemoryTTF(g.font.data(), static_cast<int>(g.font.size()), g.fontPixels, &config);
    }

    unsigned char* pixels = nullptr;
    int width = 0;
    int height = 0;
    io.Fonts->GetTexDataAsRGBA32(&pixels, &width, &height);
    Image image{};
    image.data = pixels;
    image.width = width;
    image.height = height;
    image.mipmaps = 1;
    image.format = PIXELFORMAT_UNCOMPRESSED_R8G8B8A8;
    g.fontTexture = LoadTextureFromImage(image);
    io.Fonts->SetTexID(static_cast<ImTextureID>(g.fontTexture.id));
    return g.fontTexture.id != 0;
}

} // namespace

extern "C" {

BM_API int32_t bm_init(int32_t width, int32_t height, const char* title, const uint8_t* fontTtf, int32_t fontLength, float fontSize, int32_t hidden) {
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

    SetTraceLogLevel(LOG_WARNING);
    unsigned int flags = FLAG_WINDOW_RESIZABLE | FLAG_VSYNC_HINT;
    if (g.hidden) {
        flags |= FLAG_WINDOW_HIDDEN;
    }

    SetConfigFlags(flags);
    InitWindow(width, height, title != nullptr ? title : "BuildMonitor");
    if (!IsWindowReady()) {
        return 0;
    }

    SetExitKey(0);
    SetTargetFPS(60);
    IMGUI_CHECKVERSION();
    ImGui::CreateContext();
    ImGuiIO& io = ImGui::GetIO();
    io.IniFilename = nullptr;
    io.LogFilename = nullptr;
    io.BackendFlags |= ImGuiBackendFlags_RendererHasVtxOffset;
    UseTheme(BM_THEME_DARK);
    if (!LoadFont()) {
        return 0;
    }

    ResetInput();
    g.initialised = true;
    return 1;
}

BM_API int32_t bm_present(const BmScreen* screen) {
    if (!g.initialised || g.windowGone || screen == nullptr) {
        return 0;
    }

    UseTheme(screen->theme);
    BeginDrawing();
    ClearBackground(ClearColour());
    Frame(*screen, GetScreenWidth(), GetScreenHeight(), true);
    RenderDrawData(ImGui::GetDrawData());
    EndDrawing();

    if (WindowShouldClose()) {
        g.input.closeRequested = 1;
    }

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

    g.input.rows = g.bodyRows;
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

    UseTheme(screen->theme);
    // ImGui sizes a child's scrollbar from the content size of the previous frame, so a single
    // frame would draw a scrollbar the last capture needed and omit one this capture needs. The
    // first frame settles the layout and is discarded.
    Frame(*screen, width, height, false);
    BeginTextureMode(target);
    ClearBackground(ClearColour());
    Frame(*screen, width, height, false);
    RenderDrawData(ImGui::GetDrawData());
    EndTextureMode();
    Image image = LoadImageFromTexture(target.texture);
    ImageFlipVertical(&image);
    bool exported = ExportImage(image, pngPath);
    UnloadImage(image);
    UnloadRenderTexture(target);
    return exported ? 1 : 0;
}

BM_API void bm_set_hidden(int32_t hidden) {
    if (!g.initialised) {
        return;
    }

    g.hidden = hidden != 0;
    if (g.hidden) {
        SetWindowState(FLAG_WINDOW_HIDDEN);
    } else {
        ClearWindowState(FLAG_WINDOW_HIDDEN);
        RestoreWindow();
    }
}

BM_API void bm_focus(void) {
    if (!g.initialised) {
        return;
    }

    ClearWindowState(FLAG_WINDOW_HIDDEN);
    RestoreWindow();
    SetWindowFocused();
}

BM_API void bm_set_clipboard(const char* text) {
    if (g.initialised && text != nullptr) {
        SetClipboardText(text);
    }
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
}

BM_API void bm_shutdown(void) {
    if (!g.initialised) {
        return;
    }

    if (g.fontTexture.id != 0) {
        UnloadTexture(g.fontTexture);
        g.fontTexture = Texture2D{};
    }

    for (auto& icon : g.rowIcons) {
        UnloadTexture(icon.second);
    }

    g.rowIcons.clear();

    ImGui::DestroyContext();
    CloseWindow();
    g.initialised = false;
    g.windowGone = true;
}

BM_API int32_t bm_version(void) {
    return BM_VERSION;
}

} // extern "C"
