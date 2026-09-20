/// <summary>
/// The constraint shared by every test that draws the app's own pixels, so that only one of them
/// runs at a time.
/// <para>
/// The theme is a process wide switch by design: <see cref="Palette"/> is static so that every
/// control and the owner drawn rows cannot disagree about which theme they are in. A paint is
/// therefore not isolated from another test that changes it, and the form captures change it
/// twice, once to light and once back. Given a key of its own, each class was serialised against
/// itself and against nothing else, so a canvas painted while a capture flipped the palette came
/// out half in one theme and half in the other: 99% of one pair of bitmaps differed, and 3% of
/// another where the flip landed mid paint. Only the one canvas test that compares two paints
/// noticed, about one run in eight.
/// </para>
/// </summary>
static class Painting
{
    public const string Key = "painting";
}
