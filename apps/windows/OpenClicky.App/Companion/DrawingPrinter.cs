using System.Drawing.Drawing2D;
using System.Drawing.Printing;
using System.Drawing.Text;
using OpenClicky.Core.Drawing;
using OpenClicky.Overlay;

namespace OpenClicky.App;

/// <summary>
/// Prints a saved drawing: its final parts scaled onto white, with the title above and the spoken lines in Segoe Print 11 pt
/// below (carrying on to more pages when they run long). A 3D figure prints as its label pills only. Local only: nothing is
/// sent anywhere but the printer the user picks.
/// </summary>
internal static class DrawingPrinter
{
    private const float Quality = 3, DrawingShare = 0.55f, MaxUpscale = 4;
    private const int TitleUnits = 40;

    /// <summary>Asks which printer (PrintDialog), then prints. False when the user cancelled.</summary>
    public static bool Print(IWin32Window? owner, SavedDrawing drawing, Color accent)
    {
        using var document = Build(drawing, accent);
        using var dialog = new PrintDialog { Document = document, UseEXDialog = true, AllowSomePages = false, AllowSelection = false };
        if (dialog.ShowDialog(owner) != DialogResult.OK) return false;
        document.Print();
        return true;
    }

    /// <summary>The document, without printing it: a test or preview drives it with its own PrintController.</summary>
    public static PrintDocument Build(SavedDrawing drawing, Color accent)
    {
        ArgumentNullException.ThrowIfNull(drawing);
        var document = new PrintDocument { DocumentName = "OpenClicky drawing" };
        var text = string.Join("\n\n", drawing.Lines.Select(l => l.Spoken).Where(s => s.Length > 0));
        var written = 0;
        var started = false;

        document.PrintPage += (_, e) =>
        {
            var g = e.Graphics!;
            using var title = new Font("Segoe UI Semibold", 14, FontStyle.Regular, GraphicsUnit.Point);
            using var body = new Font(HandFamily, 11, FontStyle.Regular, GraphicsUnit.Point);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            var page = e.MarginBounds;
            var top = page.Top;
            if (!started)
            {
                started = true;
                g.DrawString(drawing.Title, title, Brushes.Black, new RectangleF(page.Left, top, page.Width, TitleUnits), new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap });
                top += TitleUnits;
                var area = new Rectangle(page.Left, top, page.Width, (int)((page.Bottom - top) * DrawingShare));
                using var picture = Render(drawing, new Size((int)(area.Width * Quality), (int)(area.Height * Quality)), accent);
                if (picture is not null)
                {
                    g.DrawImage(picture, area);
                    top = area.Bottom + 20;
                }
            }
            if (written < text.Length)
            {
                var format = new StringFormat { Trimming = StringTrimming.Word };
                var rest = text[written..];
                var box = new SizeF(page.Width, Math.Max(0, page.Bottom - top));
                g.MeasureString(rest, body, box, format, out var fitted, out int _);
                if (fitted > 0)
                {
                    g.DrawString(rest[..fitted], body, Brushes.Black, new RectangleF(page.Left, top, box.Width, box.Height), format);
                    written += fitted;
                    while (written < text.Length && char.IsWhiteSpace(text[written])) written++; // a cut at a space
                }
                else if (top == page.Top) written = text.Length; // not even one character fits a whole page: stop rather than loop
                e.HasMorePages = written < text.Length;
            }
        };
        document.EndPrint += (_, _) =>
        {
            started = false;
            written = 0;
        };
        return document;
    }

    /// <summary>The finished drawing on white at print quality, or null when it has no part to show.</summary>
    internal static Bitmap? Render(SavedDrawing drawing, Size size, Color accent)
    {
        if (size.Width < 16 || size.Height < 16) return null;
        const float margin = 12;
        var target = new Box(margin * Quality, margin * Quality, size.Width - 2 * margin * Quality, size.Height - 2 * margin * Quality);
        var document = new DrawingDocument();
        foreach (var line in DrawingReplayPlan.Build(drawing, target, MaxUpscale)) document.Apply(line.Ops, TimeSpan.Zero);
        return document.IsEmpty ? null : DrawingRenderer.RenderStill(document.Parts, size, Color.White, accent, Quality);
    }

    private static string HandFamily
    {
        get
        {
            using var fonts = new InstalledFontCollection();
            return fonts.Families.Any(f => f.Name == "Segoe Print") ? "Segoe Print" : "Segoe UI";
        }
    }
}
