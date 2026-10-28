namespace OpenClicky.App;

internal static class Program
{
    private static Mutex? _singleInstance;

    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Length > 0 && args[0] == ChromeBridgeNativeHostApp.ExtensionOrigin)
        {
            Environment.ExitCode = ChromeBridgeNativeHostApp.RunAsync(args, Console.OpenStandardInput(), Console.OpenStandardOutput(), CancellationToken.None).GetAwaiter().GetResult();
            return;
        }
        using var reopenRequest = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\OpenClicky.App.OpenWorkspace");
        // Prevent a second tray host from fighting over the same PTT hook / Credential Manager session.
        _singleInstance = new Mutex(initiallyOwned: true, @"Local\OpenClicky.App.SingleInstance", out var createdNew);
        var background = args.Contains("--background");
        if (!createdNew)
        {
            // A login start (--background) must not pop the running instance open.
            if (!background) reopenRequest.Set();
            _singleInstance.Dispose();
            _singleInstance = null;
            return;
        }

        try
        {
            ApplicationConfiguration.Initialize();
            // An unexpected UI-thread exception (e.g. in an async void handler) is logged and the tray keeps running,
            // instead of the WinForms exception dialog. Only the type is logged: messages may carry user text.
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (_, e) => LogUnhandled("ui", e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (_, e) => LogUnhandled("fatal", e.ExceptionObject as Exception);
            Application.ApplicationExit += (_, _) => ReleaseMutex();
            // --background (launch at login): start in the tray and open nothing.
            using var context = new TrayApplicationContext(background);
            var reopenWait = RegisterReopenRequest(reopenRequest, context.RequestShowHome);
            try { Application.Run(context); }
            finally { reopenWait.Unregister(null); }
        }
        finally
        {
            ReleaseMutex();
        }
    }

    internal static RegisteredWaitHandle RegisterReopenRequest(WaitHandle request, Action reopen) =>
        ThreadPool.RegisterWaitForSingleObject(request, (_, _) => reopen(), null, Timeout.Infinite, executeOnlyOnce: false);

    private static void LogUnhandled(string kind, Exception? error)
    {
        try { DebugLog.Append($"[{DateTimeOffset.Now:HH:mm:ss}] unhandled_{kind}_exception={error?.GetType().Name ?? "unknown"}"); }
        catch { /* logging must never throw from here */ }
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
