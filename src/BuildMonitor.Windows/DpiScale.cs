/// <summary>
/// Padding in device pixels. The app is per monitor DPI aware, so fonts grow on a scaled display;
/// spacing written as raw pixels would stay put and crowd the larger text. Control scales an int
/// and a Size itself but has no overload for Padding.
/// </summary>
static class DpiScale
{
    public static Padding Spacing(Control control, int left, int top, int right, int bottom) =>
        new(
            control.LogicalToDeviceUnits(left),
            control.LogicalToDeviceUnits(top),
            control.LogicalToDeviceUnits(right),
            control.LogicalToDeviceUnits(bottom));

    public static Padding Spacing(Control control, int all) =>
        new(control.LogicalToDeviceUnits(all));
}
