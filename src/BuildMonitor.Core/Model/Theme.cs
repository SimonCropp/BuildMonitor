/// <summary>
/// Which palette a head draws with. <see cref="System"/> is resolved by the head, because only it
/// can ask the platform; the state stays a pure function of settings.json.
/// <para>
/// Keep in sync with BmTheme in bm.h.
/// </para>
/// </summary>
enum Theme
{
    System,
    Dark,
    Light
}
