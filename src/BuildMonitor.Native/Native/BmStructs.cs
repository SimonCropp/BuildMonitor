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
    public BmString Pipeline;
    public BmString RepoBranch;
    public BmString RunNumber;
    public BmString StatusText;
    public BmString Timing;
    public BmString Tooltip;
    public BmString BuildLabel;
    public BmString BranchLabel;
    public BmString PullRequestLabel;
    public float Progress;
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
    public int OptionOffset;
    public int OptionCount;
}

[StructLayout(LayoutKind.Sequential)]
struct BmButton
{
    public BmString Label;
    public int Flags;
}

[StructLayout(LayoutKind.Sequential)]
struct BmMenuItem
{
    public BmString Label;
}

[StructLayout(LayoutKind.Sequential)]
struct BmTrayItem
{
    public BmString Id;
    public BmString Label;
    public BmString Icon;
    public int Flags;
    public int Depth;
}

[StructLayout(LayoutKind.Sequential)]
struct BmScreen
{
    public byte* Strings;
    public int StringsLength;

    public int Page;
    public BmString Title;
    public BmString Status;

    public BmString Header;
    public BmRow* Rows;
    public int RowCount;
    public int ScrollTop;
    public int TotalRows;
    public int SelectedRow;

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

    public int TrayIcon;
    public BmString TrayTooltip;
    public BmTrayItem* TrayItems;
    public int TrayItemCount;

    public int Theme;
}

[StructLayout(LayoutKind.Sequential)]
struct BmInput
{
    public int Key;
    public int ClickedButton;
    public int ClickedRow;
    public int ClickedLinkRow;
    public int ClickedLink;
    public int ClickedActionRow;
    public int ClickedAction;
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
    Quit = 16
}

static class BmFlags
{
    public const int RowSelected = 1 << 0;
    public const int RowHeader = 1 << 1;
    public const int RowFolded = 1 << 2;
    public const int RowCanRetry = 1 << 3;
    public const int RowCanCancel = 1 << 4;
    public const int FieldEnabled = 1 << 0;
    public const int ButtonEnabled = 1 << 0;
    public const int TrayEnabled = 1 << 0;
    public const int TraySeparator = 1 << 1;
}
