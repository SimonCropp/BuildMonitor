/// <summary>
/// Byte counts as a person reads them. Sizes reach the user through a prompt saying why an
/// artifact was left behind, where the exact byte count answers nothing the rounded one does not
/// and is harder to weigh against a limit written the same way.
/// </summary>
static class ByteSize
{
    static readonly string[] units = ["B", "KB", "MB", "GB", "TB"];

    /// <summary>
    /// One decimal place below ten of a unit and none above it, so a size reads at the precision it
    /// was chosen at: a limit set at 20 MB is worth saying to the megabyte, and 2.1 GB against it
    /// is worth the tenth that says how far past it went.
    /// </summary>
    public static string Human(long bytes)
    {
        if (bytes < 0)
        {
            return "unknown size";
        }

        var size = (double) bytes;
        var unit = 0;
        while (size >= 1024 &&
               unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        // Bytes never take a decimal: a file is a whole number of them, and "819.2 B" reads as a
        // rounding of something it is not.
        if (unit == 0)
        {
            return $"{bytes} B";
        }

        if (size < 10)
        {
            return $"{size.ToString("0.#", CultureInfo.InvariantCulture)} {units[unit]}";
        }

        return $"{size.ToString("0", CultureInfo.InvariantCulture)} {units[unit]}";
    }
}
