using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace SSnetUI.Services;

public static class TaskService
{
    public const string TaskName = "SSnet-UI";
    public const string LegacyTaskName = "SSnet";
    public const string AutostartArg = "--autostart";

    public static bool IsInstalled() => QueryExists(TaskName);

    private static bool QueryExists(string name)
    {
        var (ok, _) = RunSchtasks("/query", "/tn", name);
        return ok;
    }

    public static Task<(bool ok, string output)> InstallAsync() => Task.Run<(bool, string)>(() =>
    {
        try
        {
            if (QueryExists(LegacyTaskName))
                RunSchtasks("/delete", "/tn", LegacyTaskName, "/f");

            var exe = Paths.UiExe;
            if (string.IsNullOrEmpty(exe))
                return (false, "Не удалось определить путь к SSnetUI.exe");

            // /tr value must be: "<path-with-spaces>" <args>
            // ArgumentList wraps it in outer quotes and escapes inner ones automatically.
            var trValue = $"\"{exe}\" {AutostartArg}";

            return RunSchtasks(
                "/create",
                "/tn", TaskName,
                "/tr", trValue,
                "/sc", "onlogon",
                "/rl", "highest",
                "/f");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    });

    public static Task<(bool ok, string output)> DeleteAsync() => Task.Run<(bool, string)>(() =>
    {
        try
        {
            var (ok, out1) = RunSchtasks("/delete", "/tn", TaskName, "/f");
            if (QueryExists(LegacyTaskName))
                RunSchtasks("/delete", "/tn", LegacyTaskName, "/f");
            return (ok, out1);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    });

    private static (bool ok, string output) RunSchtasks(params string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = OemEncoding,
                StandardErrorEncoding = OemEncoding,
            };
            foreach (var a in args) psi.ArgumentList.Add(a);

            using var p = Process.Start(psi);
            if (p == null) return (false, "failed to start schtasks.exe");
            var stdout = p.StandardOutput.ReadToEnd();
            var stderr = p.StandardError.ReadToEnd();
            p.WaitForExit(5000);

            var msg = (stderr + "\n" + stdout).Trim();
            return (p.ExitCode == 0, msg);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    private static readonly System.Text.Encoding OemEncoding = ResolveOemEncoding();

    private static System.Text.Encoding ResolveOemEncoding()
    {
        try
        {
            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
            var cp = System.Globalization.CultureInfo.CurrentCulture.TextInfo.OEMCodePage;
            return System.Text.Encoding.GetEncoding(cp);
        }
        catch { return System.Text.Encoding.UTF8; }
    }
}
