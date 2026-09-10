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
        public string DownloadUrl; // WinSync-Setup.exe (the bootstrapper) if the release has one, else the release page

        public bool CanSelfUpdate =>
            !string.IsNullOrEmpty(DownloadUrl) && DownloadUrl.EndsWith("WinSync-Setup.exe", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Result of an update check: distinguishes "checked, none available"
    /// from "the check itself failed" (offline, rate-limited, GitHub down), so a
    /// manual "Check for Updates" click can say something meaningful either way.</summary>
    public sealed class UpdateCheckResult
    {
        public bool Success;
        public UpdateInfo Update; // null if up to date, or if Success is false
    }

    /// <summary>Checks GitHub Releases for a newer version, and can fetch an installer for it.</summary>
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

                // Prefer the bootstrapper (WinSync-Setup.exe) — it's what most installs
                // came from, and it's the one that correctly supersedes a prior install
                // of itself. Downloading and silently re-running the wrapped .msi
                // directly used to be the self-update path, but that leaves the
                // bootstrapper's own Add/Remove Programs entry orphaned (it's a
                // separate product from Windows' point of view, with its own
                // UpgradeCode) — pointing at files a same-UpgradeCode .msi upgrade
                // already swapped out from under it. Confirmed from a real report:
                // two "WinSync" entries in Installed Apps after using that path, and
                // neither one launching.
                string downloadUrl = htmlUrl;
                if (root.TryGetProperty("assets", out var assets))
                {
                    foreach (var asset in assets.EnumerateArray())
                    {
                        var name = asset.TryGetProperty("name", out var n) ? n.GetString() : null;
                        if (string.Equals(name, "WinSync-Setup.exe", StringComparison.OrdinalIgnoreCase))
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

        /// <summary>Downloads the update's installer to a temp file and returns its path.</summary>
        public static async Task<string> DownloadUpdateAsync(UpdateInfo info)
        {
            string path = Path.Combine(Path.GetTempPath(), "WinSync-Setup-Update.exe");
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("WinSync-UpdateChecker");
            using var resp = await http.GetAsync(info.DownloadUrl);
            resp.EnsureSuccessStatusCode();
            using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
                await resp.Content.CopyToAsync(fs);
            return path;
        }

        /// <summary>
        /// Launches the downloaded bootstrapper's own normal install UI (license,
        /// progress, finish/launch — the same few clicks as installing fresh) and
        /// exits this process so it isn't holding WinSync.exe open when the installer
        /// gets to it.
        ///
        /// This used to run msiexec /qn from a hidden cmd.exe script instead, fully
        /// silent. That's exactly the process pattern (unsigned app quietly spawns a
        /// shell that silently installs something) Windows Defender and similar
        /// commonly flag or kill on an unsigned binary — WinSync isn't signed yet
        /// (see SIGNING.md) — and a real report showed it breaking mid-install,
        /// leaving two conflicting registrations behind. Showing the installer's own
        /// UI trades a few seconds of visible clicking for not silently doing
        /// something that looks, to antivirus software, indistinguishable from
        /// malware installing a payload.
        /// </summary>
        public static void InstallUpdate(string installerPath)
        {
            Process.Start(new ProcessStartInfo(installerPath) { UseShellExecute = true });
            Environment.Exit(0);
        }
    }
}
