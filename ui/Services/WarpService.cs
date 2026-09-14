using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Security;

namespace SSnetUI.Services;

public class WarpResult
{
    public bool Ok { get; init; }
    public string Message { get; init; } = "";
    public string? PublicKey { get; init; }
    public string? Address6 { get; init; }
}

/// <summary>
/// Registers a fresh Cloudflare WARP account (wgcf-style) and writes the new
/// private key + IPv6 address into data/warp.conf and build/config.json.
/// The AmneziaWG obfuscation params (jc/jmin/i1/…) and the peer are preserved.
/// </summary>
public static class WarpService
{
    private const string RegUrl = "https://api.cloudflareclient.com/v0a2158/reg";

    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(25),
    };

    /// <summary>Generates a WireGuard (X25519) key pair, base64-encoded.</summary>
    public static (string priv, string pub) GenerateKeyPair()
    {
        var rnd = new SecureRandom();
        var priv = new X25519PrivateKeyParameters(rnd);
        var pub = priv.GeneratePublicKey();
        return (Convert.ToBase64String(priv.GetEncoded()),
                Convert.ToBase64String(pub.GetEncoded()));
    }

    public static async Task<WarpResult> RegenerateAsync(string? mirrorPrefix = null)
    {
        string priv, pub;
        try { (priv, pub) = GenerateKeyPair(); }
        catch (Exception ex) { return new WarpResult { Ok = false, Message = "Не удалось сгенерировать ключи: " + ex.Message }; }

        // --- Register with Cloudflare ---
        string address6;
        try
        {
            var body = new JsonObject
            {
                ["key"] = pub,
                ["install_id"] = "",
                ["fcm_token"] = "",
                ["tos"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                ["model"] = "PC",
                ["serial_number"] = "",
                ["locale"] = "en_US",
            };

            var url = string.IsNullOrWhiteSpace(mirrorPrefix)
                ? RegUrl
                : mirrorPrefix.TrimEnd('/') + "/" + RegUrl;

            using var req = new HttpRequestMessage(HttpMethod.Post, url);
            req.Headers.TryAddWithoutValidation("User-Agent", "okhttp/3.12.1");
            req.Headers.TryAddWithoutValidation("CF-Client-Version", "a-6.30-2158");
            req.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");

            using var resp = await Http.SendAsync(req);
            var text = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode)
                return new WarpResult { Ok = false, Message = $"Cloudflare вернул HTTP {(int)resp.StatusCode}. Возможно API заблокирован — попробуй указать mirror.\n\n{Trunc(text)}" };

            var json = JsonNode.Parse(text) as JsonObject;
            var v6 = json?["config"]?["interface"]?["addresses"]?["v6"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(v6))
                return new WarpResult { Ok = false, Message = "Ответ Cloudflare без IPv6-адреса:\n" + Trunc(text) };

            address6 = v6;
        }
        catch (Exception ex)
        {
            return new WarpResult { Ok = false, Message = "Ошибка регистрации: " + ex.Message + "\n(Возможно API Cloudflare недоступен в твоей сети — укажи mirror-префикс.)" };
        }

        // --- Write files (with backup) ---
        try
        {
            UpdateWarpConf(priv, address6);
            UpdateConfigJson(priv, address6);
        }
        catch (Exception ex)
        {
            return new WarpResult { Ok = false, Message = "Регистрация прошла, но запись файлов не удалась: " + ex.Message };
        }

        return new WarpResult
        {
            Ok = true,
            PublicKey = pub,
            Address6 = address6,
            Message = $"Новый WARP-аккаунт зарегистрирован.\nIPv6: {address6}",
        };
    }

    private static void UpdateWarpConf(string priv, string v6)
    {
        var path = Paths.WarpConf;
        if (!File.Exists(path)) return;
        Backup(path);

        var text = File.ReadAllText(path);
        // Address = 172.16.0.2, <v6>   (this file uses no CIDR suffix)
        text = Regex.Replace(text, @"(?m)^PrivateKey\s*=.*$", $"PrivateKey = {priv}");
        text = Regex.Replace(text, @"(?m)^Address\s*=.*$", $"Address = 172.16.0.2, {v6}");
        File.WriteAllText(path, text);
    }

    private static void UpdateConfigJson(string priv, string v6)
    {
        var path = Paths.ConfigJson;
        if (!File.Exists(path)) return;
        Backup(path);

        var root = JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
        var endpoints = root?["endpoints"] as JsonArray;
        if (endpoints == null) return;

        foreach (var e in endpoints)
        {
            if (e is JsonObject obj && obj["tag"]?.GetValue<string>() == "warp-out")
            {
                obj["private_key"] = priv;
                obj["address"] = new JsonArray("172.16.0.2/32", $"{v6}/128");
                break;
            }
        }

        var opts = new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
        File.WriteAllText(path, root!.ToJsonString(opts));
    }

    private static void Backup(string path)
    {
        try
        {
            var bak = path + ".bak";
            File.Copy(path, bak, overwrite: true);
        }
        catch { /* best effort */ }
    }

    private static string Trunc(string s, int max = 400)
        => s.Length <= max ? s : s.Substring(0, max) + "…";
}
