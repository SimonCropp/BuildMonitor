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

    // Above the 200 pixels GitHub recommends. Its largest badge draws the logo 70 across, so this stays
    // sharp at any display density.
    const int logoSize = 512;

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
        ("connections", Lucide.Plug),
        ("options", Lucide.Settings),
        ("filters", Lucide.ListFilter),
        ("logs", Lucide.FolderOpen),
        ("issue", Lucide.Bug),
        ("update", Lucide.CircleArrowUp),
        ("exit", Lucide.LogOut),
        ("folder", Lucide.Folder),
        // The row chips. Each is the noun the drop down spells out, so a row can carry all of them
        // in the width one label used to take. Retry is a plain rotate rather than the refresh
        // arrows, which are the Refresh button beneath the rows.
        ("pull-request", Lucide.GitPullRequest),
        ("retry", Lucide.RotateCw),
        // A cross rather than a stop: Lucide's stop is a square inside a circle, and at the size a
        // chip draws one the square closed up into a dot. The cross is also the tray's mark for a
        // build that failed, which this is not, but it sits in a red button on a row that says
        // "Cancel" in the drop down and on hover, where nothing else reads as clearly.
        ("cancel", Lucide.X),
        ("log", Lucide.ScrollText),
        ("triage", Lucide.Stethoscope),
        // What the triage chip carries while its download is still going. The prompt only reaches
        // the clipboard at the end, so a chip that looked the same throughout had people pasting
        // whatever they copied before the click.
        ("busy", Lucide.Hourglass),
        // An arrow into a bar: the queue's front is a place a build is moved to, which a plain up
        // arrow reads as one step up rather than all the way.
        ("run-next", Lucide.ArrowUpToLine),
        // Not a chip: the mark leading a branch in a row's text, which is what sets it apart from
        // the pipeline before it. Separated by a space alone, "Build and Test fix perf" could be
        // split anywhere.
        ("branch", Lucide.GitBranch)
    ];

    static SKColor glyphColour = new(0x8A, 0x8A, 0x8A);

    /// <summary>
    /// The badge every provider logo carries, saying what the mark opens: a play for a run, in
    /// the tray's running blue, a cross for one that broke, in its failed red, and a clock for
    /// the pipeline's own page, which is a list of past runs and no run at all. The badge is what
    /// tells a CI service's logo from the source host's, which for GitHub is the same octocat.
    /// <para>
    /// The clock is the glyph grey rather than a third colour, so that a coloured badge keeps
    /// meaning "this run" and a grey one "the pipeline".
    /// </para>
    /// </summary>
    static SKColor runningColour = new(0x3B, 0x82, 0xF6);

    /// <summary>
    /// How much of the mark a badge takes. The clock's is larger than the play's and the cross's
    /// because it carries more: a dial and two hands, where they are one solid shape each, and at
    /// the play's size the hands closed up. Sized to what each has to show rather than to each
    /// other, since no row draws two of them at once.
    /// </summary>
    const float badgeFill = 0.2f;

    const float clockFill = 0.28f;

    static SKColor failedColour = new(0xD9, 0x3A, 0x3A);

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

    /// <summary>
    /// The marks for the services that host source, drawn before a row's repository name. The
    /// same drawings as the provider logos of the same name, under the names the rows ask for.
    /// GitHub is the exception the pair exists for: its provider logo is the octocat badged with
    /// Actions, since a row shows the repository beside the pipeline that built it.
    /// <para>
    /// This list and RepoHosts.All, in the core, say the same thing twice, and nothing in a normal
    /// build runs this, so a mark named there and not here is a row with a gap where its picture
    /// should be. ImagesTests is what notices.
    /// </para>
    /// </summary>
    static (string Id, Icon Icon, SKColor Colour)[] hosts =
    [
        Provider("github"),
        Provider("gitlab"),
        Provider("bitbucket"),
        Provider("azure-devops")
    ];

    static (string Id, Icon Icon, SKColor Colour) Provider(string id) =>
        providers.Single(_ => _.Id == id);

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
                // One for each thing the mark can open. Which is which is decided by
                // ProviderMarks in the core, from these names.
                using var run = Badged(icon, colour, size, runningColour, Play);
                File.WriteAllBytes(Path.Combine(images, $"glyph-provider-{id}-run-{size}.png"), Png(run));
                using var failed = Badged(icon, colour, size, failedColour, Cross);
                File.WriteAllBytes(Path.Combine(images, $"glyph-provider-{id}-failed-{size}.png"), Png(failed));
                using var history = Badged(icon, colour, size, glyphColour, Clock, clockFill);
                File.WriteAllBytes(Path.Combine(images, $"glyph-provider-{id}-history-{size}.png"), Png(history));
            }
        }

        foreach (var (id, icon, colour) in hosts)
        {
            foreach (var size in glyphSizes)
            {
                using var bitmap = Glyph(icon, colour, size);
                File.WriteAllBytes(Path.Combine(images, $"glyph-host-{id}-{size}.png"), Png(bitmap));
            }
        }

        var idle = tray.Single(_ => _.Name == "idle");
        File.WriteAllBytes(Path.Combine(root, "src", "icon.png"), Logo(idle.Svg, idle.Background));
        Console.WriteLine($"Wrote {Directory.GetFiles(images).Length} files to {images}");
        Console.WriteLine($"Upload src/icon.png as the GitHub OAuth App's logo, with badge background color {Hex(idle.Background)}");
        return 0;
    }

    /// <summary>
    /// src/icon.png: the package's icon, the readme's, and the GitHub OAuth App's logo, which the
    /// authorize page shows in place of an identicon. The tray's disc, drawn at logo size, so the
    /// package and the readme show the icon the app does. A full bleed square read as a different
    /// icon beside the tray's disc. GitHub draws the logo at 55% of its own disc in the app's badge
    /// colour, so set the badge colour to the disc's for the two to read as one.
    /// </summary>
    static byte[] Logo(string svg, SKColor background)
    {
        using var mark = Load(Heavier(svg), SKColors.White, "logo");
        using var bitmap = Disc(mark.Picture!, Painted(mark.Picture!), background, logoSize);
        return Png(bitmap);
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
        if (heavier == svg)
        {
            throw new InvalidOperationException("No stroke width to change");
        }

        return heavier;
    }

    /// <summary>
    /// A logo with a badge in its corner: the service's own mark, so the row still says which
    /// service, and the badge saying that this is the run there rather than the source. The badge
    /// is punched out of the logo before it is drawn, so the two read as two things rather than
    /// one blob wherever a row's background shows between them.
    /// </summary>
    static SKBitmap Badged(Icon icon, SKColor colour, int size, SKColor badgeColour, Action<SKCanvas, SKPoint, float> mark) =>
        Badged(icon, colour, size, badgeColour, mark, badgeFill);

    static SKBitmap Badged(Icon icon, SKColor colour, int size, SKColor badgeColour, Action<SKCanvas, SKPoint, float> mark, float fill)
    {
        var bitmap = new SKBitmap(size, size, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        Draw(canvas, icon, colour, 0, 0, size * 0.88f);

        var radius = size * fill;
        var centre = new SKPoint(size - radius, size - radius);
        using (var clear = new SKPaint
               {
                   BlendMode = SKBlendMode.Clear,
                   IsAntialias = true
               })
        {
            canvas.DrawCircle(centre, radius + MathF.Max(1, size * 0.06f), clear);
        }

        using (var paint = new SKPaint
               {
                   Color = badgeColour,
                   IsAntialias = true
               })
        {
            canvas.DrawCircle(centre, radius, paint);
        }

        mark(canvas, centre, radius * 0.52f);
        return bitmap;
    }

    /// <summary>
    /// Drawn rather than taken from Lucide: a stroked triangle with rounded joins closes up at the
    /// few pixels across a badge is, where a solid one still reads as pointing somewhere.
    /// </summary>
    static void Play(SKCanvas canvas, SKPoint centre, float half)
    {
        using var builder = new SKPathBuilder();
        builder.MoveTo(centre.X - half * 0.8f, centre.Y - half);
        builder.LineTo(centre.X + half * 0.9f, centre.Y);
        builder.LineTo(centre.X - half * 0.8f, centre.Y + half);
        builder.Close();
        using var path = builder.Snapshot();
        canvas.DrawPath(path, White());
    }

    /// <summary>
    /// Two strokes rather than a glyph, for the same reason: at a badge's size Lucide's cross is
    /// mostly the round caps at its ends.
    /// </summary>
    static void Cross(SKCanvas canvas, SKPoint centre, float half)
    {
        using var paint = White();
        paint.Style = SKPaintStyle.Stroke;
        paint.StrokeWidth = half * 0.62f;
        paint.StrokeCap = SKStrokeCap.Round;
        canvas.DrawLine(centre.X - half, centre.Y - half, centre.X + half, centre.Y + half, paint);
        canvas.DrawLine(centre.X + half, centre.Y - half, centre.X - half, centre.Y + half, paint);
    }

    /// <summary>
    /// A white dial with grey hands drawn on it, rather than a ring, and rather than hands knocked
    /// through to whatever is behind: a ring closes into a doughnut at the size a badge is drawn,
    /// and knocked out hands are the row's own colour, which on a light theme is a white dial with
    /// white hands and no clock at all.
    /// <para>
    /// The face fills the badge past its rim, leaving the grey showing as the clock's own edge,
    /// and the hands reach most of the way across it. Everything smaller read as a dot.
    /// </para>
    /// </summary>
    static void Clock(SKCanvas canvas, SKPoint centre, float half)
    {
        var face = half * 1.35f;
        canvas.DrawCircle(centre, face, White());
        using var paint = new SKPaint
        {
            Color = glyphColour,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = half * 0.42f,
            StrokeCap = SKStrokeCap.Round
        };
        canvas.DrawLine(centre.X, centre.Y, centre.X, centre.Y - face * 0.85f, paint);
        canvas.DrawLine(centre.X, centre.Y, centre.X + face * 0.68f, centre.Y, paint);
    }

    static SKPaint White() =>
        new()
        {
            Color = SKColors.White,
            IsAntialias = true
        };

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
        var loaded = new SKSvg();
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(svg.Replace("currentColor", $"#{Hex(colour)}")));
        if (loaded.Load(stream) is null)
        {
            loaded.Dispose();
            throw new InvalidOperationException($"Could not parse {name}");
        }

        return loaded;
    }

    static string Hex(SKColor colour) =>
        $"{colour.Red:x2}{colour.Green:x2}{colour.Blue:x2}";

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
