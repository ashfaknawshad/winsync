using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;

namespace WinSync
{
    public sealed class UpdateInfo
    {
        public Version Version;
        public string TagName;
        public string HtmlUrl;
        public string DownloadUrl; // direct .msi asset if the release has one, else the release page

        public bool CanSelfUpdate =>
            !string.IsNullOrEmpty(DownloadUrl) && DownloadUrl.EndsWith(".msi", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Result of an update check: distinguishes "checked, none available"
    /// from "the check itself failed" (offline, rate-limited, GitHub down), so a
    /// manual "Check for Updates" click can say something meaningful either way.</summary>
    public sealed class UpdateCheckResult
    {
        public bool Success;
        public UpdateInfo Update; // null if up to date, or if Success is false
    }

    /// <summary>Checks GitHub Releases for a newer version, and can silently install one.</summary>
    public static class UpdateChecker
    {
        private const string ApiUrl = "https://api.github.com/repos/ashfaknawshad/winsync/releases/latest";

        /// <summary>Never throws. Success is false only if the check itself couldn't
        /// complete (network, rate limit, etc.) — that's distinct from "checked fine,
        /// already up to date" (Success true, Update null).</summary>
        public static async Task<UpdateCheckResult> CheckForUpdateAsync()
        {
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
                http.DefaultRequestHeaders.UserAgent.ParseAdd("WinSync-UpdateChecker");

                using var resp = await http.GetAsync(ApiUrl);
                if (!resp.IsSuccessStatusCode) return new UpdateCheckResult { Success = false };

                var json = await resp.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                string tag = root.GetProperty("tag_name").GetString();
                if (string.IsNullOrEmpty(tag)) return new UpdateCheckResult { Success = false };

                string htmlUrl = root.TryGetProperty("html_url", out var h) ? h.GetString() : null;

                string versionText = tag.TrimStart('v', 'V');
                if (!Version.TryParse(versionText, out var remoteVersion))
                    return new UpdateCheckResult { Success = false };

                var currentVersion = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0, 0);
                if (remoteVersion <= currentVersion) return new UpdateCheckResult { Success = true, Update = null };

                string downloadUrl = htmlUrl;
                if (root.TryGetProperty("assets", out var assets))
                {
                    foreach (var asset in assets.EnumerateArray())
                    {
                        var name = asset.TryGetProperty("name", out var n) ? n.GetString() : null;
                        if (name != null && name.EndsWith(".msi", StringComparison.OrdinalIgnoreCase))
                        {
                            downloadUrl = asset.GetProperty("browser_download_url").GetString();
                            break;
                        }
                    }
                }

                return new UpdateCheckResult
                {
                    Success = true,
                    Update = new UpdateInfo
                    {
                        Version = remoteVersion,
                        TagName = tag,
                        HtmlUrl = htmlUrl,
                        DownloadUrl = downloadUrl
                    }
                };
            }
            catch
            {
                return new UpdateCheckResult { Success = false };
            }
        }

        /// <summary>Downloads the update's .msi to a temp file and returns its path.</summary>
        public static async Task<string> DownloadUpdateAsync(UpdateInfo info)
        {
            string path = Path.Combine(Path.GetTempPath(), "WinSync-Update.msi");
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("WinSync-UpdateChecker");
            using var resp = await http.GetAsync(info.DownloadUrl);
            resp.EnsureSuccessStatusCode();
            using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
                await resp.Content.CopyToAsync(fs);
            return path;
        }

        /// <summary>
        /// Installs the downloaded MSI silently and relaunches WinSync — like Telegram's
        /// "restart to update", not a browser download + manual double-click. Runs
        /// msiexec from a short detached script rather than directly, because the MSI
        /// needs to overwrite WinSync.exe, which Windows won't allow while this process
        /// still has it open; a couple of seconds' delay in the script gives this
        /// process time to actually exit first. Ends by terminating this process
        /// (Environment.Exit, not Application.Exit — this class has no WinForms
        /// dependency, and a hard exit is exactly what's needed here to drop the file
        /// lock immediately rather than wait on the message loop).
        /// </summary>
        public static void InstallUpdateAndRestart(string msiPath)
        {
            string exePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WinSync", "WinSync.exe");

            string script =
                "/c timeout /t 2 /nobreak >nul & " +
                $"msiexec /i \"{msiPath}\" /qn & " +
                $"start \"\" \"{exePath}\" & " +
                $"del \"{msiPath}\"";

            Process.Start(new ProcessStartInfo("cmd.exe", script)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            });

            Environment.Exit(0);
        }
    }
}
