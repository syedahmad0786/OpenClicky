using System.Numerics;
using System.Text.Json;

namespace OpenClicky.Core.Drawing;

/// <summary>Where a 3D figure was put: its title, its screen (1-based, of <see cref="Screens"/>) and its place in words.</summary>
public sealed record FigurePlacement(string Title, int Screen, int Screens, string Where)
{
    /// <summary>"Cube is on the right side of your screen." (names the screen only when there are several).</summary>
    public string Sentence => $"{Title} is {Where} of {(Screens > 1 ? $"screen {Screen}" : "your screen")}.";
}

/// <summary>
/// Where 3D figures go on screen. A figure is drawn only when its whole box is on its screenshot, so a box the model guessed
/// partly off screen used to vanish; boxes are now pulled on screen and kept big enough to read. Also turns a scene3d
/// artifact (the classic window's cube or pyramid) into an on-screen figure, and names a box's place in words.
/// </summary>
public static class FigureStage
{
    /// <summary>Smallest figure side, as a share of the screenshot's shorter side.</summary>
    public const float MinShare = 0.3f;
    /// <summary>Largest figure side, as a share of that screenshot side.</summary>
    public const float MaxShare = 0.9f;

    /// <summary>The figure op with its box (and its spec's box) moved and sized onto its screenshot; other ops pass unchanged.</summary>
    public static DrawOp Fit(DrawOp op, IReadOnlyList<ScreenCaptureResult> screens)
    {
        if (op.Kind != DrawOpKind.Figure || op.Figure is not { } figure || screens.Count == 0) return op;
        var screen = op.Screen ?? CursorScreen(screens);
        if (screen < 1 || screen > screens.Count) return op;
        var shot = screens[screen - 1];
        if (shot.PixelWidth <= 0 || shot.PixelHeight <= 0) return op;
        var min = MinShare * Math.Min(shot.PixelWidth, shot.PixelHeight);
        float w = Math.Clamp(op.Box.W, min, MaxShare * shot.PixelWidth), h = Math.Clamp(op.Box.H, min, MaxShare * shot.PixelHeight);
        // Grow or shrink about the box's centre, then slide the whole box onto the screenshot.
        var c = op.Box.Centre;
        var x = Math.Clamp(c.X - w / 2, 0, shot.PixelWidth - w - 1);
        var y = Math.Clamp(c.Y - h / 2, 0, shot.PixelHeight - h - 1);
        var box = new Box(x, y, w, h);
        return box == op.Box ? op : op with { Box = box, Figure = figure with { Box = box } };
    }

    /// <summary>The box's place on its screenshot in words: "in the centre", "on the right side", "in the top left"…</summary>
    public static string Where(Box box, ScreenCaptureResult shot)
    {
        var c = box.Centre;
        var col = c.X < shot.PixelWidth / 3f ? 0 : c.X > shot.PixelWidth * 2 / 3f ? 2 : 1;
        var row = c.Y < shot.PixelHeight / 3f ? 0 : c.Y > shot.PixelHeight * 2 / 3f ? 2 : 1;
        return (row, col) switch
        {
            (1, 1) => "in the centre",
            (1, 0) => "on the left side",
            (1, 2) => "on the right side",
            (0, 1) => "at the top",
            (2, 1) => "at the bottom",
            _ => "in the " + (row == 0 ? "top" : "bottom") + " " + (col == 0 ? "left" : "right"),
        };
    }

    /// <summary>
    /// A scene3d artifact (<c>{"kind":"scene3d","shape":"cube"|"pyramid","color","rotation","title"}</c>) as a spinning figure in
    /// the middle of the right half of <paramref name="screen"/>; null for any other artifact.
    /// </summary>
    public static FigureSpec? FromScene3d(JsonElement artifact, int screen, ScreenCaptureResult shot)
    {
        if (artifact.ValueKind != JsonValueKind.Object || !artifact.TryGetProperty("kind", out var kind) || kind.GetString() != "scene3d") return null;
        var shape = artifact.TryGetProperty("shape", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() : "cube";
        var color = artifact.TryGetProperty("color", out var c) && c.ValueKind == JsonValueKind.String && c.GetString() is { Length: 7 } hex && hex[0] == '#' ? hex : "";
        var turn = artifact.TryGetProperty("rotation", out var r) && r.ValueKind == JsonValueKind.Number ? Math.Clamp(r.GetDouble(), -360, 360) : 24;
        var title = artifact.TryGetProperty("title", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
        var part = shape == "pyramid"
            ? new FigurePart(FigureKind.Cone, new Vector3(1.4f, 1.2f, 1.4f), Vector3.Zero, new Vector3(0, (float)turn, 0), color, null, new Dictionary<string, double>(), null)
            : new FigurePart(FigureKind.Box, Vector3.One, Vector3.Zero, new Vector3(20, (float)turn, 0), color, null, new Dictionary<string, double>(), null);
        var side = 0.45f * Math.Min(shot.PixelWidth, shot.PixelHeight);
        var box = new Box(shot.PixelWidth * 0.72f - side / 2, shot.PixelHeight * 0.5f - side / 2, side, side);
        return new FigureSpec(null, box, screen, Spin: true, title ?? (shape == "pyramid" ? "Pyramid" : "Cube"), [part]);
    }

    private static readonly (string Pattern, FigureKind Kind, string Title, Vector3 Size)[] Primitives =
    [
        (@"\b(cube|box)\b", FigureKind.Box, "Cube", Vector3.One),
        (@"\b(sphere|ball)\b", FigureKind.Sphere, "Sphere", Vector3.One),
        (@"\bcylinder\b", FigureKind.Cylinder, "Cylinder", new(1, 1.6f, 1)),
        (@"\bpyramid\b", FigureKind.Cone, "Pyramid", new(1.4f, 1.2f, 1.4f)),
        (@"\bcone\b", FigureKind.Cone, "Cone", new(1, 1.4f, 1)),
        (@"\b(torus|donut|doughnut)\b", FigureKind.Torus, "Torus", new(1.6f, 0.5f, 1.6f)),
        (@"\b(capsule|pill)\b", FigureKind.Capsule, "Capsule", new(0.8f, 1.8f, 0.8f)),
        (@"\b(gear|cog)\b", FigureKind.Gear, "Gear", new(1.6f, 0.4f, 1.6f)),
        (@"\b(spring|coil)\b", FigureKind.Spring, "Spring", new(0.9f, 1.8f, 0.9f)),
    ];

    /// <summary>
    /// A request for one simple 3D shape ("show a 3d cylinder") as a spinning figure, for when the reply carried no figure
    /// block: the model sometimes says it placed one without writing it. Null unless the request says 3d and names a primitive.
    /// </summary>
    public static FigureSpec? FromRequest(string request, int screen, ScreenCaptureResult shot)
    {
        const System.Text.RegularExpressions.RegexOptions options = System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant;
        if (string.IsNullOrWhiteSpace(request) || request.Length > 500
            || !System.Text.RegularExpressions.Regex.IsMatch(request, @"\b(3d|3-d|three[\s-]?dimensional)\b", options, TimeSpan.FromMilliseconds(50))) return null;
        foreach (var (pattern, kind, title, size) in Primitives)
        {
            if (!System.Text.RegularExpressions.Regex.IsMatch(request, pattern, options, TimeSpan.FromMilliseconds(50))) continue;
            var parameters = kind switch
            {
                FigureKind.Gear => new Dictionary<string, double> { ["teeth"] = 14 },
                FigureKind.Spring => new Dictionary<string, double> { ["coils"] = 6 },
                _ => new Dictionary<string, double>(),
            };
            var part = new FigurePart(kind, size, Vector3.Zero, new Vector3(kind == FigureKind.Box ? 20 : 15, 24, 0), "", null, parameters, null);
            var side = 0.45f * Math.Min(shot.PixelWidth, shot.PixelHeight);
            var box = new Box(shot.PixelWidth * 0.72f - side / 2, shot.PixelHeight * 0.5f - side / 2, side, side);
            return new FigureSpec(null, box, screen, Spin: true, title, [part]);
        }
        return null;
    }

    internal static int CursorScreen(IReadOnlyList<ScreenCaptureResult> screens)
    {
        for (var i = 0; i < screens.Count; i++) if (screens[i].IsCursorMonitor) return i + 1;
        return 1;
    }
}
