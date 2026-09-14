/// <summary>
/// Writes every image the heads embed. Tray icons are a coloured disc with a white glyph, so
/// the state reads at sixteen pixels; menu and row glyphs are the bare Lucide stroke in a mid
/// grey that survives a light or a dark menu.
/// </summary>
static class Program
{
    static readonly int[] traySizes = [16, 20, 24, 32, 48, 256];
    static readonly int[] pixmapSizes = [16, 22, 24, 32, 48];
    static readonly int[] glyphSizes = [16, 32];

    static readonly (string Name, Icon Icon, SKColor Background)[] tray =
    [
        ("idle", Lucide.Hammer, new(0x7A, 0x7A, 0x7A)),
        ("running", Lucide.LoaderCircle, new(0x3B, 0x82, 0xF6)),
        ("success", Lucide.CircleCheck, new(0x22, 0xA0, 0x5B)),
        ("failed", Lucide.CircleX, new(0xD9, 0x3A, 0x3A)),
        ("attention", Lucide.TriangleAlert, new(0xE0, 0x8A, 0x1E))
    ];

    static readonly (string Name, Icon Icon)[] glyphs =
    [
        ("open", Lucide.AppWindow),
        ("refresh", Lucide.RefreshCw),
        ("options", Lucide.Settings),
        ("filters", Lucide.ListFilter),
        ("logs", Lucide.FolderOpen),
        ("issue", Lucide.Bug),
        ("update", Lucide.CircleArrowUp),
        ("exit", Lucide.LogOut),
        ("build", Lucide.ExternalLink),
        ("retry", Lucide.RotateCcw),
        ("cancel", Lucide.Ban),
        ("branch", Lucide.GitBranch),
        ("pull-request", Lucide.GitPullRequest),
        ("queued", Lucide.Clock),
        ("running", Lucide.LoaderCircle),
        ("succeeded", Lucide.CircleCheck),
        ("failed", Lucide.CircleX),
        ("cancelled", Lucide.CircleSlash),
        ("unknown", Lucide.CircleDashed)
    ];

    static readonly SKColor glyphColour = new(0x8A, 0x8A, 0x8A);

    /// <summary>
    /// Provider logos from Simple Icons, keyed by provider id. Iconify carries the shapes but not
    /// the brand colours, so those are transcribed from simple-icons. A brand that is near black,
    /// GitHub and TeamCity, is drawn in the glyph grey instead, or it would vanish on a dark theme.
    /// </summary>
    static readonly (string Id, Icon Icon, SKColor Colour)[] providers =
    [
        ("appveyor", SimpleIcons.Appveyor, new(0x00, 0xB3, 0xE0)),
        ("travis", SimpleIcons.Travisci, new(0x3E, 0xAA, 0xAF)),
        ("jenkins", SimpleIcons.Jenkins, new(0xD2, 0x49, 0x39)),
        ("github", SimpleIcons.Github, glyphColour),
        ("azure-devops", SimpleIcons.Azuredevops, new(0x00, 0x78, 0xD7)),
        ("teamcity", SimpleIcons.Teamcity, glyphColour),
        ("gitlab", SimpleIcons.Gitlab, new(0xFC, 0x6D, 0x26)),
        ("gocd", SimpleIcons.Gocd, new(0x94, 0x39, 0x9E)),
        ("bitbucket", SimpleIcons.Bitbucket, new(0x00, 0x52, 0xCC)),
        ("octopus", SimpleIcons.Octopusdeploy, new(0x2F, 0x93, 0xE0))
    ];

    static int Main()
    {
        var root = FindRepositoryRoot();
        var images = Path.Combine(root, "src", "BuildMonitor.Core", "Images");
        Directory.CreateDirectory(images);
        // Only what this writes. Images.cs lives in the same folder.
        foreach (var file in Directory.GetFiles(images).Where(_ => Path.GetExtension(_) is ".png" or ".ico" or ".argb"))
        {
            File.Delete(file);
        }

        foreach (var (name, icon, background) in tray)
        {
            var frames = new Dictionary<int, byte[]>();
            foreach (var size in traySizes.Union(pixmapSizes).Distinct().Order())
            {
                using var bitmap = Disc(icon, background, size);
                var png = Png(bitmap);
                frames[size] = png;
                if (traySizes.Contains(size))
                {
                    File.WriteAllBytes(Path.Combine(images, $"tray-{name}-{size}.png"), png);
                }

                if (pixmapSizes.Contains(size))
                {
                    File.WriteAllBytes(Path.Combine(images, $"tray-{name}-{size}.argb"), Argb(bitmap));
                }
            }

            File.WriteAllBytes(Path.Combine(images, $"tray-{name}.ico"), Ico.Pack(traySizes.Select(_ => (_, frames[_])).ToList()));
        }

        foreach (var (name, icon) in glyphs)
        {
            foreach (var size in glyphSizes)
            {
                using var bitmap = Glyph(icon, glyphColour, size);
                File.WriteAllBytes(Path.Combine(images, $"glyph-{name}-{size}.png"), Png(bitmap));
            }
        }

        foreach (var (id, icon, colour) in providers)
        {
            foreach (var size in glyphSizes)
            {
                using var bitmap = Glyph(icon, colour, size);
                File.WriteAllBytes(Path.Combine(images, $"glyph-provider-{id}-{size}.png"), Png(bitmap));
            }
        }

        File.Copy(Path.Combine(images, "tray-idle-256.png"), Path.Combine(root, "src", "icon.png"), true);
        Console.WriteLine($"Wrote {Directory.GetFiles(images).Length} files to {images}");
        return 0;
    }

    static SKBitmap Disc(Icon icon, SKColor background, int size)
    {
        var bitmap = new SKBitmap(size, size, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        using var paint = new SKPaint
        {
            Color = background,
            IsAntialias = true
        };
        var centre = size / 2f;
        canvas.DrawCircle(centre, centre, centre - Math.Max(0.5f, size / 32f), paint);
        var inset = size * 0.2f;
        Draw(canvas, icon, SKColors.White, inset, inset, size - 2 * inset);
        return bitmap;
    }

    static SKBitmap Glyph(Icon icon, SKColor colour, int size)
    {
        var bitmap = new SKBitmap(size, size, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        var inset = size * 0.05f;
        Draw(canvas, icon, colour, inset, inset, size - 2 * inset);
        return bitmap;
    }

    /// <summary>
    /// The SVG says currentColor; Skia has no CSS cascade to give it one, so it is substituted
    /// before parsing.
    /// </summary>
    static void Draw(SKCanvas canvas, Icon icon, SKColor colour, float x, float y, float size)
    {
        var hex = $"#{colour.Red:x2}{colour.Green:x2}{colour.Blue:x2}";
        var svg = icon.Svg.Replace("currentColor", hex);
        using var loaded = new SKSvg();
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(svg));
        var picture = loaded.Load(stream) ??
                      throw new InvalidOperationException($"Could not parse {icon.Name}");
        var bounds = picture.CullRect;
        var scale = size / Math.Max(bounds.Width, bounds.Height);
        canvas.Save();
        canvas.Translate(x, y);
        canvas.Scale(scale);
        canvas.DrawPicture(picture);
        canvas.Restore();
    }

    static byte[] Png(SKBitmap bitmap)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    /// <summary>
    /// ARGB32 in network byte order, unpremultiplied: what a StatusNotifierItem pixmap is.
    /// </summary>
    static byte[] Argb(SKBitmap bitmap)
    {
        using var unpremultiplied = bitmap.Copy(SKColorType.Bgra8888);
        var result = new byte[bitmap.Width * bitmap.Height * 4];
        var index = 0;
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                result[index++] = pixel.Alpha;
                result[index++] = pixel.Red;
                result[index++] = pixel.Green;
                result[index++] = pixel.Blue;
            }
        }

        return result;
    }

    static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "global.json")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not find the repository root");
    }
}
