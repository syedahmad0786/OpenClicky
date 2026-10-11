namespace OpenClicky.App;

internal static class Program
{
    private static Mutex? _singleInstance;

    [STAThread]
    private static void Main()
    {
        // Prevent a second tray host from fighting over the same PTT hook / Credential Manager session.
        _singleInstance = new Mutex(initiallyOwned: true, @"Local\OpenClicky.App.SingleInstance", out var createdNew);
        if (!createdNew)
        {
            MessageBox.Show(
                "OpenClicky is already running in the system tray.",
                "OpenClicky",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            _singleInstance.Dispose();
            _singleInstance = null;
            return;
        }

        try
        {
            ApplicationConfiguration.Initialize();
            Application.ApplicationExit += (_, _) => ReleaseMutex();
            Application.Run(new TrayApplicationContext());
        }
        finally
        {
            ReleaseMutex();
        }
    }

    private static void ReleaseMutex()
    {
        if (_singleInstance is null)
        {
            return;
        }

        try
        {
            _singleInstance.ReleaseMutex();
        }
        catch
        {
            // already released
        }

        _singleInstance.Dispose();
        _singleInstance = null;
    }
}
