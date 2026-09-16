/// <summary>
/// Writes every image the heads embed. Tray icons are a coloured disc with a white mark, so
/// the state reads at sixteen pixels; menu and row glyphs are the bare Lucide stroke in a mid
/// grey that survives a light or a dark menu.
/// </summary>
static class Program
{
    static int[] traySizes = [16, 20, 24, 32, 48, 256];
    static int[] pixmapSizes = [16, 22, 24, 32, 48];
    static int[] glyphSizes = [16, 32];

    // How much of the largest square inside the disc a tray mark fills. The rest keeps the round caps
    // at the ends of a cross off the rim.
    const float markFill = 0.86f;

    // Heavier than Lucide's 2, so a mark scaled into sixteen pixels is still more than a pixel wide.
    const string markStroke = "2.5";

    /// <summary>
    /// Bare marks rather than circled ones: the disc is the circle already, and a circled mark drew a
    /// second ring inside it, which left the tick or the cross a few pixels across at sixteen.
    /// </summary>
    static (string Name, string Svg, SKColor Background)[] tray =
    [
        ("idle", Lucide.Hammer.Svg, new(0x7A, 0x7A, 0x7A)),
        ("running", Lucide.LoaderCircle.Svg, new(0x3B, 0x82, 0xF6)),
        ("success", Lucide.Check.Svg, new(0x22, 0xA0, 0x5B)),
        ("failed", Lucide.X.Svg, new(0xD9, 0x3A, 0x3A)),
        ("attention", WithoutCircle(Lucide.CircleAlert.Svg), new(0xE0, 0x8A, 0x1E))
    ];

    static (string Name, Icon Icon)[] glyphs =
    [
        ("open", Lucide.AppWindow),
        ("refresh", Lucide.RefreshCw),
        ("options", Lucide.Settings),
        ("filters", Lucide.ListFilter),
        ("logs", Lucide.FolderOpen),
        ("issue", Lucide.Bug),
        ("update", Lucide.CircleArrowUp),
        ("exit", Lucide.LogOut),
        ("folder", Lucide.Folder)
    ];

    static SKColor glyphColour = new(0x8A, 0x8A, 0x8A);

    /// <summary>
    /// Provider logos from Simple Icons, keyed by provider id. Iconify carries the shapes but not
    /// the brand colours, so those are transcribed from simple-icons. A brand that is near black,
    /// GitHub and TeamCity, is drawn in the glyph grey instead, or it would vanish on a dark theme.
    /// </summary>
    static (string Id, Icon Icon, SKColor Colour)[] providers =
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

        foreach (var (name, svg, background) in tray)
        {
            using var mark = Load(Heavier(svg), SKColors.White, name);
            var painted = Painted(mark.Picture!);
            var frames = new Dictionary<int, byte[]>();
            foreach (var size in traySizes.Union(pixmapSizes).Distinct().Order())
            {
                using var bitmap = Disc(mark.Picture!, painted, background, size);
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

    /// <summary>
    /// The disc, filling the icon, then the mark sized from the part of it that is painted rather than
    /// from its 24 unit box, which Lucide pads by a different amount for each mark. Sized by the box, a
    /// tick came out smaller than a cross, and both smaller than the disc had room for.
    /// </summary>
    static SKBitmap Disc(SKPicture mark, SKRect painted, SKColor background, int size)
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
        var radius = centre - Math.Max(0.5f, size / 32f);
        canvas.DrawCircle(centre, centre, radius, paint);
        var side = radius * MathF.Sqrt(2) * markFill;
        canvas.Translate(centre, centre);
        canvas.Scale(side / Math.Max(painted.Width, painted.Height));
        canvas.Translate(-painted.MidX, -painted.MidY);
        canvas.DrawPicture(mark);
        return bitmap;
    }

    /// <summary>
    /// The part of a picture that is painted, found by drawing it large and reading the alpha back:
    /// the picture's own bounds are its whole box, padding and all.
    /// </summary>
    static SKRect Painted(SKPicture picture)
    {
        const int scale = 16;
        var box = picture.CullRect;
        var width = (int) Math.Ceiling(box.Width * scale);
        var height = (int) Math.Ceiling(box.Height * scale);
        using var bitmap = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Transparent);
            canvas.Scale(scale);
            canvas.Translate(-box.Left, -box.Top);
            canvas.DrawPicture(picture);
        }

        var left = width;
        var top = height;
        var right = 0;
        var bottom = 0;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (bitmap.GetPixel(x, y).Alpha == 0)
                {
                    continue;
                }

                left = Math.Min(left, x);
                top = Math.Min(top, y);
                right = Math.Max(right, x + 1);
                bottom = Math.Max(bottom, y + 1);
            }
        }

        return new(box.Left + left / (float) scale, box.Top + top / (float) scale, box.Left + right / (float) scale, box.Top + bottom / (float) scale);
    }

    /// <summary>
    /// A circled mark without its circle, for a mark Lucide only draws in one.
    /// </summary>
    static string WithoutCircle(string svg)
    {
        var start = svg.IndexOf("<circle", StringComparison.Ordinal);
        var end = start < 0 ? -1 : svg.IndexOf("/>", start, StringComparison.Ordinal);
        if (end < 0)
        {
            throw new InvalidOperationException("No self closing circle to remove");
        }

        return svg.Remove(start, end + 2 - start);
    }

    static string Heavier(string svg)
    {
        var heavier = svg.Replace("stroke-width=\"2\"", $"stroke-width=\"{markStroke}\"");
        return heavier == svg ? throw new InvalidOperationException("No stroke width to change") : heavier;
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

    static void Draw(SKCanvas canvas, Icon icon, SKColor colour, float x, float y, float size)
    {
        using var loaded = Load(icon.Svg, colour, icon.Name);
        var picture = loaded.Picture!;
        var bounds = picture.CullRect;
        var scale = size / Math.Max(bounds.Width, bounds.Height);
        canvas.Save();
        canvas.Translate(x, y);
        canvas.Scale(scale);
        canvas.DrawPicture(picture);
        canvas.Restore();
    }

    /// <summary>
    /// The SVG says currentColor; Skia has no CSS cascade to give it one, so it is substituted
    /// before parsing.
    /// </summary>
    static SKSvg Load(string svg, SKColor colour, string name)
    {
        var hex = $"#{colour.Red:x2}{colour.Green:x2}{colour.Blue:x2}";
        var loaded = new SKSvg();
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(svg.Replace("currentColor", hex)));
        if (loaded.Load(stream) is null)
        {
            loaded.Dispose();
            throw new InvalidOperationException($"Could not parse {name}");
        }

        return loaded;
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
