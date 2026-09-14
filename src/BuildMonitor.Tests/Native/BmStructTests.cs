/// <summary>
/// The managed mirrors of bm.h, sized against the header by hand: a field added on one side
/// and not the other reads every element after it at the wrong offset. The snapshot pins the
/// sizes so a change here is a deliberate one, made together with BM_VERSION.
/// </summary>
public class BmStructTests
{
    [Test]
    [Arguments(typeof(BmString), 8)]
    [Arguments(typeof(BmRow), 8 + 8 * 8 + 4)]
    [Arguments(typeof(BmField), 8 + 4 * 8 + 8)]
    [Arguments(typeof(BmButton), 12)]
    [Arguments(typeof(BmMenuItem), 8)]
    [Arguments(typeof(BmTrayItem), 3 * 8 + 8)]
    public async Task Sizes(Type type, int expected) =>
        await Assert.That(Marshal.SizeOf(type)).IsEqualTo(expected);

    [Test]
    public Task Layout() =>
        Verify(new
        {
            PointerSize = IntPtr.Size,
            Screen = Marshal.SizeOf<BmScreen>(),
            Input = Marshal.SizeOf<BmInput>(),
            ScreenOffsets = Offsets<BmScreen>(),
            InputOffsets = Offsets<BmInput>(),
            Keys = Enum.GetValues<BmKey>().Select(_ => $"{_}={(int) _}"),
            Version = Bm.ExpectedVersion
        });

    static IEnumerable<string> Offsets<T>() =>
        typeof(T).GetFields(BindingFlags.Public | BindingFlags.Instance)
            .Select(_ => $"{_.Name}@{Marshal.OffsetOf<T>(_.Name)}");
}
