using System;
using System.Linq;
using System.Threading;
using System.Windows;

namespace SSnetUI;

public partial class App : Application
{
    /// <summary>True when the app was launched with --autostart (by Task Scheduler at logon).</summary>
    public static bool IsAutostart { get; private set; }

    /// <summary>Delay before the auto-restart fires.</summary>
    public static TimeSpan AutostartDelay { get; } = TimeSpan.FromSeconds(5);

    // Named mutex so the Inno Setup installer (AppMutex) can detect/close a running
    // instance during a silent OTA update.
    private static Mutex? _mutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        try { _mutex = new Mutex(false, "SSnetUI_AppMutex"); } catch { }

        IsAutostart = e.Args.Any(a => string.Equals(a, Services.TaskService.AutostartArg,
            StringComparison.OrdinalIgnoreCase));
        base.OnStartup(e);
    }
}
