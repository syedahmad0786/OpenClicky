namespace OpenClicky.Sandbox;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        if (!args.Contains("--demo-desktop", StringComparer.OrdinalIgnoreCase))
        {
            Application.Run(new SandboxForm(args.Contains("--multiline", StringComparer.OrdinalIgnoreCase)));
            return;
        }

        RunDemoDesktop();
    }

    private static void RunDemoDesktop()
    {
        const string fixtureText = "DEMO-20261011";
        var screens = Screen.AllScreens;
        var primary = Screen.PrimaryScreen ?? screens[0];
        using var main = new SandboxForm
        {
            FormBorderStyle = FormBorderStyle.None,
            StartPosition = FormStartPosition.Manual,
            MinimumSize = Size.Empty,
            MaximumSize = Size.Empty,
            Bounds = primary.Bounds,
            TopMost = false,
        };

        var marker = new Label
        {
            AutoSize = true,
            Text = fixtureText,
            Font = new Font("Segoe UI", 16, FontStyle.Bold),
            BackColor = Color.White,
            ForeColor = Color.Black,
        };
        main.Controls.Add(marker);
        void PositionMarker() => marker.Location = new Point(
            Math.Max(16, main.ClientSize.Width - marker.Width - 24),
            18);
        main.Resize += (_, _) => PositionMarker();
        PositionMarker();

        var covers = new List<Form>();
        main.Shown += (_, _) =>
        {
            foreach (var screen in screens.Where(screen =>
                !string.Equals(screen.DeviceName, primary.DeviceName, StringComparison.OrdinalIgnoreCase)))
            {
                var cover = new Form
                {
                    Text = fixtureText,
                    FormBorderStyle = FormBorderStyle.None,
                    StartPosition = FormStartPosition.Manual,
                    Bounds = screen.Bounds,
                    BackColor = Color.FromArgb(30, 36, 48),
                    ShowInTaskbar = true,
                    TopMost = false,
                };
                cover.Controls.Add(new Label
                {
                    Dock = DockStyle.Fill,
                    Text = fixtureText,
                    TextAlign = ContentAlignment.MiddleCenter,
                    Font = new Font("Segoe UI", 28, FontStyle.Bold),
                    BackColor = cover.BackColor,
                    ForeColor = Color.White,
                });
                cover.FormClosing += (_, e) =>
                {
                    if (e.CloseReason == CloseReason.UserClosing) e.Cancel = true;
                };
                covers.Add(cover);
                cover.Show(main);
            }

            main.Activate();
        };
        main.FormClosed += (_, _) =>
        {
            foreach (var cover in covers) cover.Dispose();
            covers.Clear();
        };

        Application.Run(main);
    }
}
