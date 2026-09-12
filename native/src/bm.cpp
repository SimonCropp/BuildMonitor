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

// Colours, transcribed from the WinForms head's Palette so every head agrees.
const ImVec4 background = ImVec4(24 / 255.0f, 24 / 255.0f, 24 / 255.0f, 1.0f);
const ImVec4 surface = ImVec4(32 / 255.0f, 32 / 255.0f, 32 / 255.0f, 1.0f);
const ImVec4 headerRow = ImVec4(38 / 255.0f, 38 / 255.0f, 38 / 255.0f, 1.0f);
const ImVec4 selectedRow = ImVec4(44 / 255.0f, 50 / 255.0f, 66 / 255.0f, 1.0f);
const ImVec4 hoverRow = ImVec4(36 / 255.0f, 36 / 255.0f, 40 / 255.0f, 1.0f);
const ImVec4 text = ImVec4(212 / 255.0f, 212 / 255.0f, 212 / 255.0f, 1.0f);
const ImVec4 dim = ImVec4(140 / 255.0f, 140 / 255.0f, 140 / 255.0f, 1.0f);
const ImVec4 border = ImVec4(56 / 255.0f, 56 / 255.0f, 56 / 255.0f, 1.0f);
const ImVec4 barTrack = ImVec4(58 / 255.0f, 58 / 255.0f, 58 / 255.0f, 1.0f);
const ImVec4 chip = ImVec4(52 / 255.0f, 52 / 255.0f, 56 / 255.0f, 1.0f);
const ImVec4 chipText = ImVec4(180 / 255.0f, 200 / 255.0f, 255 / 255.0f, 1.0f);
const ImVec4 retryChip = ImVec4(48 / 255.0f, 82 / 255.0f, 52 / 255.0f, 1.0f);
const ImVec4 cancelChip = ImVec4(96 / 255.0f, 52 / 255.0f, 52 / 255.0f, 1.0f);
const ImVec4 errorText = ImVec4(233 / 255.0f, 129 / 255.0f, 129 / 255.0f, 1.0f);

ImVec4 StatusColour(int32_t status) {
    switch (status) {
        case BM_STATUS_QUEUED: return ImVec4(150 / 255.0f, 150 / 255.0f, 150 / 255.0f, 1.0f);
        case BM_STATUS_RUNNING: return ImVec4(86 / 255.0f, 156 / 255.0f, 214 / 255.0f, 1.0f);
        case BM_STATUS_SUCCEEDED: return ImVec4(126 / 255.0f, 214 / 255.0f, 139 / 255.0f, 1.0f);
        case BM_STATUS_FAILED: return ImVec4(233 / 255.0f, 129 / 255.0f, 129 / 255.0f, 1.0f);
        case BM_STATUS_CANCELLED: return ImVec4(160 / 255.0f, 160 / 255.0f, 160 / 255.0f, 1.0f);
        default: return ImVec4(120 / 255.0f, 120 / 255.0f, 120 / 255.0f, 1.0f);
    }
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
            unsigned int texture = static_cast<unsigned int>(reinterpret_cast<intptr_t>(cmd.GetTexID()));
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
    if (screen.rowCount == 0) {
        ImGui::TextColored(dim, "Nothing to show yet.");
    } else if (ImGui::BeginTable("rows", 8, flags)) {
        ImGui::TableSetupColumn("pipeline", ImGuiTableColumnFlags_WidthStretch, 3.0f);
        ImGui::TableSetupColumn("repo", ImGuiTableColumnFlags_WidthStretch, 3.5f);
        ImGui::TableSetupColumn("run", ImGuiTableColumnFlags_WidthFixed, 70.0f);
        ImGui::TableSetupColumn("status", ImGuiTableColumnFlags_WidthFixed, 90.0f);
        ImGui::TableSetupColumn("bar", ImGuiTableColumnFlags_WidthFixed, 120.0f);
        ImGui::TableSetupColumn("timing", ImGuiTableColumnFlags_WidthFixed, 90.0f);
        ImGui::TableSetupColumn("links", ImGuiTableColumnFlags_WidthFixed, 210.0f);
        ImGui::TableSetupColumn("actions", ImGuiTableColumnFlags_WidthFixed, 80.0f);

        for (int32_t i = 0; i < screen.rowCount; i++) {
            const BmRow& row = screen.rows[i];
            bool header = (row.flags & BM_ROW_HEADER) != 0;
            bool selected = (row.flags & BM_ROW_SELECTED) != 0;
            ImGui::PushID(i);
            ImGui::TableNextRow(ImGuiTableRowFlags_None, rowHeight);
            if (selected) {
                ImGui::TableSetBgColor(ImGuiTableBgTarget_RowBg0, ImGui::GetColorU32(selectedRow));
            } else if (header) {
                ImGui::TableSetBgColor(ImGuiTableBgTarget_RowBg0, ImGui::GetColorU32(headerRow));
            }

            ImGui::TableSetColumnIndex(0);
            std::string pipeline = Str(screen, row.pipeline);
            if (header) {
                std::string label = std::string((row.flags & BM_ROW_FOLDED) ? "> " : "v ") + pipeline;
                ImGui::PushStyleColor(ImGuiCol_Text, dim);
                if (ImGui::Selectable(label.c_str(), false, ImGuiSelectableFlags_SpanAllColumns | ImGuiSelectableFlags_AllowOverlap)) {
                    g.input.clickedRow = i;
                }

                ImGui::PopStyleColor();
                if (ImGui::IsItemClicked(ImGuiMouseButton_Right)) {
                    g.input.rightClickedRow = i;
                }

                ImGui::PopID();
                continue;
            }

            ImVec4 colour = StatusColour(row.status);
            ImDrawList* draw = ImGui::GetWindowDrawList();
            ImVec2 cursor = ImGui::GetCursorScreenPos();
            draw->AddCircleFilled(ImVec2(cursor.x + 8.0f, cursor.y + ImGui::GetFrameHeight() / 2.0f), 5.0f, ImGui::GetColorU32(colour));
            ImGui::SetCursorPosX(ImGui::GetCursorPosX() + 20.0f);
            std::string selectableLabel = pipeline + "##row";
            if (ImGui::Selectable(selectableLabel.c_str(), false, ImGuiSelectableFlags_SpanAllColumns | ImGuiSelectableFlags_AllowOverlap)) {
                g.input.clickedRow = i;
            }

            if (ImGui::IsItemClicked(ImGuiMouseButton_Right)) {
                g.input.rightClickedRow = i;
            }

            if (row.tooltip.length > 0 && ImGui::IsItemHovered(ImGuiHoveredFlags_ForTooltip)) {
                ImGui::SetTooltip("%s", Str(screen, row.tooltip).c_str());
            }

            ImGui::TableSetColumnIndex(1);
            ImGui::TextColored(dim, "%s", Str(screen, row.repoBranch).c_str());
            ImGui::TableSetColumnIndex(2);
            ImGui::TextColored(dim, "%s", Str(screen, row.runNumber).c_str());
            ImGui::TableSetColumnIndex(3);
            ImGui::TextColored(colour, "%s", Str(screen, row.statusText).c_str());
            ImGui::TableSetColumnIndex(4);
            if (row.progress >= 0.0f) {
                ImGui::PushStyleColor(ImGuiCol_PlotHistogram, StatusColour(BM_STATUS_RUNNING));
                ImGui::PushStyleColor(ImGuiCol_FrameBg, barTrack);
                ImGui::SetCursorPosY(ImGui::GetCursorPosY() + (ImGui::GetFrameHeight() - 8.0f) / 2.0f);
                ImGui::ProgressBar(row.progress, ImVec2(110.0f, 8.0f), "");
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
    ImGui::StyleColorsDark();
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
    io.Fonts->SetTexID(static_cast<ImTextureID>(static_cast<intptr_t>(g.fontTexture.id)));
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
    ApplyStyle();
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

    BeginDrawing();
    ClearBackground(Color{24, 24, 24, 255});
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

    BeginTextureMode(target);
    ClearBackground(Color{24, 24, 24, 255});
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

BM_API void bm_shutdown(void) {
    if (!g.initialised) {
        return;
    }

    if (g.fontTexture.id != 0) {
        UnloadTexture(g.fontTexture);
        g.fontTexture = Texture2D{};
    }

    ImGui::DestroyContext();
    CloseWindow();
    g.initialised = false;
    g.windowGone = true;
}

BM_API int32_t bm_version(void) {
    return BM_VERSION;
}

} // extern "C"
