namespace OpenClicky.App;

internal static class AppTheme
{
    public static readonly Color Background = Color.FromArgb(247, 248, 244);
    public static readonly Color Ink = Color.FromArgb(29, 43, 36);
    public static readonly Color Muted = Color.FromArgb(99, 112, 103);
    public static readonly Color Accent = Color.FromArgb(37, 102, 76);
    public static readonly Color Tint = Color.FromArgb(227, 239, 229);
    public static readonly Color Border = Color.FromArgb(216, 223, 215);
    public static readonly Icon Icon = new(typeof(AppTheme).Assembly.GetManifestResourceStream("OpenClicky.App.Assets.openclicky.ico")!);
    public static readonly Image Logo = Image.FromStream(typeof(AppTheme).Assembly.GetManifestResourceStream("OpenClicky.App.Assets.openclicky.png")!);

    public static void Apply(Form form)
    {
        form.SuspendLayout();
        form.Font = new Font("Segoe UI", 10f);
        form.ForeColor = Ink;
        form.BackColor = Background;
        form.Icon = Icon;
        form.AutoScaleDimensions = new SizeF(96, 96);
        form.AutoScaleMode = AutoScaleMode.Dpi;
    }

    public static Button Button(string text, bool primary = false)
    {
        var button = new Button
        {
            Text = text, AccessibleName = text, AutoSize = true, MinimumSize = new Size(100, 38),
            FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand, Padding = new Padding(12, 4, 12, 4),
            BackColor = primary ? Accent : Color.White, ForeColor = primary ? Color.White : Ink,
            UseVisualStyleBackColor = false,
        };
        button.FlatAppearance.BorderColor = primary ? Accent : Border;
        button.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(29, 80, 60) : Tint;
        return button;
    }

    public static Label Label(string text, float size = 10, bool bold = false) => new()
    {
        Text = text, AutoSize = true, MaximumSize = new Size(620, 0), Margin = new Padding(0, 0, 0, 12),
        Font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular),
        ForeColor = bold ? Ink : Muted,
    };
}
