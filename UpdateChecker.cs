using System;
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
    }

    /// <summary>Checks GitHub Releases for a newer version than the one currently running.</summary>
    public static class UpdateChecker
    {
        private const string ApiUrl = "https://api.github.com/repos/ashfaknawshad/winsync/releases/latest";

        /// <summary>Returns update info if a newer release exists, otherwise null.
        /// Never throws — a failed check (offline, rate-limited, GitHub down) is treated
        /// the same as "no update available" so it can never block startup.</summary>
        public static async Task<UpdateInfo> CheckForUpdateAsync()
        {
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
                http.DefaultRequestHeaders.UserAgent.ParseAdd("WinSync-UpdateChecker");

                using var resp = await http.GetAsync(ApiUrl);
                if (!resp.IsSuccessStatusCode) return null;

                var json = await resp.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                string tag = root.GetProperty("tag_name").GetString();
                if (string.IsNullOrEmpty(tag)) return null;

                string htmlUrl = root.TryGetProperty("html_url", out var h) ? h.GetString() : null;

                string versionText = tag.TrimStart('v', 'V');
                if (!Version.TryParse(versionText, out var remoteVersion)) return null;

                var currentVersion = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0, 0);
                if (remoteVersion <= currentVersion) return null;

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

                return new UpdateInfo
                {
                    Version = remoteVersion,
                    TagName = tag,
                    HtmlUrl = htmlUrl,
                    DownloadUrl = downloadUrl
                };
            }
            catch
            {
                return null;
            }
        }
    }
}
