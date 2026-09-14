using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;

namespace SSnetUI.Services;

public enum RuleItemKind { Domain, ProcessName }

public class RuleGroup
{
    public string Tag { get; set; } = "";
    public string Type { get; set; } = "inline";

    public RuleItemKind ItemKind { get; set; } = RuleItemKind.Domain;
    public ObservableCollection<string> Items { get; set; } = new();

    public string Format { get; set; } = "binary";
    public string Url { get; set; } = "";
    public string UpdateInterval { get; set; } = "1d";

    // For local type
    public string Path { get; set; } = "";
    // Preserved original URL for re-download when type is local
    public string SourceUrl { get; set; } = "";

    public JsonObject? Original { get; set; }

    public bool IsInline => string.Equals(Type, "inline", StringComparison.OrdinalIgnoreCase);
    public bool IsRemote => string.Equals(Type, "remote", StringComparison.OrdinalIgnoreCase);
    public bool IsLocal  => string.Equals(Type, "local",  StringComparison.OrdinalIgnoreCase);

    public string KindBadge => IsRemote ? "remote"
        : IsLocal ? "local"
        : (ItemKind == RuleItemKind.ProcessName ? "proc" : "dom");
}

public static class RulesService
{
    public static List<RuleGroup> Load()
    {
        var result = new List<RuleGroup>();
        if (!File.Exists(Paths.RulesJson)) return result;

        try
        {
            var json = File.ReadAllText(Paths.RulesJson);
            var root = JsonNode.Parse(json) as JsonArray;
            if (root == null) return result;

            foreach (var item in root)
            {
                if (item is not JsonObject obj) continue;
                var group = new RuleGroup
                {
                    Tag = obj["tag"]?.GetValue<string>() ?? "",
                    Type = obj["type"]?.GetValue<string>() ?? "inline",
                    Original = (JsonObject)obj.DeepClone(),
                };

                if (group.IsRemote)
                {
                    group.Format = obj["format"]?.GetValue<string>() ?? "binary";
                    group.Url = obj["url"]?.GetValue<string>() ?? "";
                    group.UpdateInterval = obj["update_interval"]?.GetValue<string>() ?? "1d";
                    group.SourceUrl = group.Url;
                }
                else if (group.IsLocal)
                {
                    group.Format = obj["format"]?.GetValue<string>() ?? "binary";
                    group.Path = obj["path"]?.GetValue<string>() ?? "";
                    group.SourceUrl = obj["_source_url"]?.GetValue<string>() ?? "";
                }
                else
                {
                    if (obj["rules"] is JsonArray rules && rules.Count > 0 && rules[0] is JsonObject ro)
                    {
                        if (ro["process_name"] is JsonArray procs)
                        {
                            group.ItemKind = RuleItemKind.ProcessName;
                            foreach (var p in procs)
                            {
                                var s = p?.GetValue<string>();
                                if (!string.IsNullOrWhiteSpace(s)) group.Items.Add(s!);
                            }
                        }
                        else if (ro["domain"] is JsonArray domains)
                        {
                            group.ItemKind = RuleItemKind.Domain;
                            foreach (var d in domains)
                            {
                                var s = d?.GetValue<string>();
                                if (!string.IsNullOrWhiteSpace(s)) group.Items.Add(s!);
                            }
                        }
                    }
                }
                result.Add(group);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"RulesService.Load: {ex.Message}");
        }
        return result;
    }

    public static void Save(IEnumerable<RuleGroup> groups)
    {
        var arr = new JsonArray();
        foreach (var g in groups)
        {
            JsonObject item = g.Original != null
                ? (JsonObject)g.Original.DeepClone()
                : new JsonObject();

            item["type"] = g.Type;
            item["tag"] = g.Tag;

            if (g.IsRemote)
            {
                item["format"] = g.Format;
                item["url"] = g.Url;
                item["update_interval"] = g.UpdateInterval;
                item.Remove("rules");
                item.Remove("path");
                item.Remove("_source_url");
            }
            else if (g.IsLocal)
            {
                item["format"] = g.Format;
                item["path"] = g.Path;
                if (!string.IsNullOrEmpty(g.SourceUrl))
                    item["_source_url"] = g.SourceUrl;
                item.Remove("rules");
                item.Remove("url");
                item.Remove("update_interval");
            }
            else
            {
                var itemsArr = new JsonArray();
                foreach (var s in g.Items) itemsArr.Add(s);
                var ruleObj = new JsonObject();
                var key = g.ItemKind == RuleItemKind.ProcessName ? "process_name" : "domain";
                ruleObj[key] = itemsArr;
                item["rules"] = new JsonArray(ruleObj);
                item.Remove("format");
                item.Remove("url");
                item.Remove("update_interval");
                item.Remove("path");
                item.Remove("_source_url");
            }

            arr.Add(item);
        }
        var json = arr.ToJsonString(JsonOpts);
        File.WriteAllText(Paths.RulesJson, json);
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// Runs SSnetCli.exe. The CLI uses Console.ReadKey() which can't read from a redirected
    /// stdin — so we give it its own hidden console, then attach to it and push Enter
    /// keystrokes directly into its input buffer via WriteConsoleInput.
    /// </summary>
    public static Task<(bool ok, string output)> ApplyAsync() => Task.Run<(bool ok, string output)>(() =>
    {
        if (!File.Exists(Paths.SSnetCli)) return (false, "SSnetCli.exe not found");

        var psi = new ProcessStartInfo
        {
            FileName = Paths.SSnetCli,
            WorkingDirectory = Paths.AppRoot,
            UseShellExecute = false,
            CreateNoWindow = true,
            // NO redirection — ReadKey requires a real console input buffer.
        };

        Process? p;
        try { p = Process.Start(psi); }
        catch (Exception ex) { return (false, ex.Message); }
        if (p == null) return (false, "failed to start SSnetCli");

        // Background pump: attach to the child's hidden console and keep injecting
        // Enter keypresses until the process exits.
        var pumpStop = new CancellationTokenSource();
        var pumpTask = Task.Run(() =>
        {
            try
            {
                Thread.Sleep(250); // let the child create its console

                FreeConsole(); // detach from any console we might be on
                if (!AttachConsole((uint)p.Id)) return;
                try
                {
                    var hIn = GetStdHandle(STD_INPUT_HANDLE);
                    if (hIn == IntPtr.Zero || hIn == new IntPtr(-1)) return;

                    while (!pumpStop.Token.IsCancellationRequested && !p.HasExited)
                    {
                        PushEnter(hIn);
                        Thread.Sleep(400);
                    }
                }
                finally
                {
                    FreeConsole();
                }
            }
            catch { /* best effort */ }
        });

        var finished = p.WaitForExit(20000);
        pumpStop.Cancel();
        try { pumpTask.Wait(1000); } catch { }

        if (!finished)
        {
            try { p.Kill(true); } catch { }
            return (false, "SSnetCli не завершился за 20 секунд");
        }
        return (p.ExitCode == 0, $"Exit code: {p.ExitCode}");
    });

    // --- WinAPI for pushing Enter into a console input buffer ---

    private const int STD_INPUT_HANDLE = -10;
    private const ushort KEY_EVENT = 0x0001;
    private const ushort VK_RETURN = 0x0D;

    [StructLayout(LayoutKind.Sequential)]
    private struct KEY_EVENT_RECORD
    {
        public int bKeyDown;
        public ushort wRepeatCount;
        public ushort wVirtualKeyCode;
        public ushort wVirtualScanCode;
        public char UnicodeChar;
        public uint dwControlKeyState;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct INPUT_RECORD
    {
        [FieldOffset(0)] public ushort EventType;
        [FieldOffset(4)] public KEY_EVENT_RECORD KeyEvent;
    }

    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool AttachConsole(uint dwProcessId);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool FreeConsole();
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr GetStdHandle(int nStdHandle);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool WriteConsoleInputW(IntPtr hConsoleInput,
        [MarshalAs(UnmanagedType.LPArray)] INPUT_RECORD[] lpBuffer,
        uint nLength, out uint lpNumberOfEventsWritten);

    private static void PushEnter(IntPtr hIn)
    {
        var down = new INPUT_RECORD
        {
            EventType = KEY_EVENT,
            KeyEvent = new KEY_EVENT_RECORD
            {
                bKeyDown = 1,
                wRepeatCount = 1,
                wVirtualKeyCode = VK_RETURN,
                wVirtualScanCode = 0x1C,
                UnicodeChar = '\r',
                dwControlKeyState = 0,
            }
        };
        var up = down;
        up.KeyEvent.bKeyDown = 0;

        var arr = new[] { down, up };
        WriteConsoleInputW(hIn, arr, (uint)arr.Length, out _);
    }
}
