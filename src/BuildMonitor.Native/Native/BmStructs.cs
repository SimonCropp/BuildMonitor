// ReSharper disable InconsistentNaming
// Mirrors native/include/bm.h. BmStructTests pins the sizes.

[StructLayout(LayoutKind.Sequential)]
struct BmString
{
    public int Offset;
    public int Length;
}

[StructLayout(LayoutKind.Sequential)]
struct BmRow
{
    public int Status;
    public int Flags;
    public BmString Name;
    public BmString Detail;
    public BmString NameIcon;
    public BmString DetailIcon;
    public BmString Timing;
    public int ChipOffset;
    public int ChipCount;
    public float Progress;
    public int NameLink;
    public int DetailIconLink;
    public int StatusLink;
    public int SpanOffset;
    public int SpanCount;
    public BmString Author;
    public int TooltipOffset;
    public int TooltipCount;
}

[StructLayout(LayoutKind.Sequential)]
struct BmChip
{
    public BmString Label;
    public BmString Tooltip;
    public BmString Icon;
    public BmString Text;
    public int Kind;
}

[StructLayout(LayoutKind.Sequential)]
struct BmSpan
{
    public BmString Text;
    public int Link;
}

[StructLayout(LayoutKind.Sequential)]
struct BmTooltip
{
    public BmString Text;
    public int Part;
}

[StructLayout(LayoutKind.Sequential)]
struct BmField
{
    public int Kind;
    public int Flags;
    public BmString Id;
    public BmString Label;
    public BmString Value;
    public BmString Hint;
    public BmString Note;
    public int OptionOffset;
    public int OptionCount;
}

[StructLayout(LayoutKind.Sequential)]
struct BmButton
{
    public BmString Label;
    public BmString Tooltip;
    public int Flags;
}

[StructLayout(LayoutKind.Sequential)]
struct BmMenuItem
{
    public BmString Label;
    public int Flags;
}

[StructLayout(LayoutKind.Sequential)]
struct BmTrayItem
{
    public BmString Id;
    public BmString Label;
    public BmString Icon;
    public int Flags;
}

[StructLayout(LayoutKind.Sequential)]
struct BmScreen
{
    public byte* Strings;
    public int StringsLength;

    public int Page;
    public BmString Title;
    public BmString Status;
    public BmString StatusTooltip;

    public BmString Header;
    public BmRow* Rows;
    public int RowCount;
    public int ScrollTop;
    public int TotalRows;
    public int SelectedRow;
    public int Loading;
    public BmString* Names;
    public int NameCount;
    public int GroupNameCount;
    public BmString* Details;
    public int DetailCount;
    public BmString* Authors;
    public int AuthorCount;
    public BmChip* Chips;
    public int ChipCount;
    public BmSpan* Spans;
    public int SpanCount;
    public BmTooltip* Tooltips;
    public int TooltipCount;
    public BmString Search;
    public BmString SearchTooltip;
    public BmString Empty;

    public BmString FormTitle;
    public BmField* Fields;
    public int FieldCount;
    public BmString* Options;
    public int OptionCount;

    public BmButton* Buttons;
    public int ButtonCount;

    public BmMenuItem* Menu;
    public int MenuCount;
    public int MenuRow;
    public int MenuOverflow;

    public int TrayIcon;
    public BmString TrayTooltip;
    public BmTrayItem* TrayItems;
    public int TrayItemCount;

    public int Theme;

    public int Generation;
}

[StructLayout(LayoutKind.Sequential)]
struct BmPlacement
{
    public int X;
    public int Y;
    public int Width;
    public int Height;
    public int Maximized;
    public int Known;
}

[StructLayout(LayoutKind.Sequential)]
struct BmInput
{
    public int Key;
    public int ClickedButton;
    public int ClickedRow;
    public int ClickedChipRow;
    public int ClickedChip;
    public int ClickedOverflowRow;
    public int OverflowFrom;
    public int HoveredChipRow;
    public int HoveredChip;
    public int RightClickedRow;
    public int ClickedMenuItem;
    public int MenuClosed;
    public int ChangedField;
    public byte* ChangedValue;
    public int ChangedValueLength;
    public int ClickedField;
    public int ClickedTrayItem;
    public int TrayIconClicked;
    public int ScrollDelta;
    public int ScrollTo;
    public int CloseRequested;
    public int Rows;
    public BmPlacement Placement;
}

enum BmKey
{
    None = 0,
    ScrollUp = 1,
    ScrollDown = 2,
    PageUp = 3,
    PageDown = 4,
    Home = 5,
    End = 6,
    NextRow = 7,
    PreviousRow = 8,
    OpenBuild = 9,
    Retry = 10,
    CancelBuild = 11,
    Refresh = 12,
    Copy = 13,
    Back = 14,
    Hide = 15,
    Quit = 16,
    CopyStatus = 17,
    CopyLog = 18,
    Triage = 19,
    OpenMenu = 20
}

static class BmFlags
{
    public const int RowSelected = 1 << 0;
    public const int RowGroup = 1 << 1;
    public const int RowExpanded = 1 << 2;
    public const int RowMember = 1 << 3;
    public const int FieldEnabled = 1 << 0;
    public const int ButtonEnabled = 1 << 0;
    public const int TrayEnabled = 1 << 0;
    public const int TraySeparator = 1 << 1;
    public const int MenuSeparatorAbove = 1 << 0;
}
