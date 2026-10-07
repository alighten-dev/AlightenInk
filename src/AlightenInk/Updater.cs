using System;
using System.Threading.Tasks;
using Velopack;
using Velopack.Sources;

namespace AlightenInk
{
    /// <summary>
    /// Velopack updates from GitHub Releases. Only runs in an installed copy; every failure
    /// (no network, repo not created yet, rate limits) is swallowed and only reported when the
    /// user asked explicitly.
    /// </summary>
    internal static class Updater
    {
        public const string RepoUrl = "https://github.com/alighten-dev/AlightenInk";
        private static bool _running;
        private static UpdateInfo? _pending;

        public static string CurrentVersion
        {
            get
            {
                try
                {
                    var mgr = CreateManager();
                    if (mgr.IsInstalled && mgr.CurrentVersion != null) return mgr.CurrentVersion.ToString();
                }
                catch
                {
                    // not installed / no metadata
                }
                var v = typeof(Updater).Assembly.GetName().Version;
                return v == null ? "1.0.0" : $"{v.Major}.{v.Minor}.{v.Build}";
            }
        }

        private static UpdateManager CreateManager() => new UpdateManager(new GithubSource(RepoUrl, null, false));

        public static async Task CheckAsync(bool manual, Action<string> toast)
        {
            if (_running) return;
            _running = true;
            try
            {
                var mgr = CreateManager();
                if (!mgr.IsInstalled)
                {
                    if (manual) toast("Updates work in the installed app");
                    return;
                }

                if (_pending != null)
                {
                    if (manual)
                    {
                        toast("Restarting to update…");
                        await Task.Delay(700);
                        mgr.ApplyUpdatesAndRestart(_pending.TargetFullRelease);
                    }
                    return;
                }

                if (manual) toast("Checking for updates…");
                var info = await mgr.CheckForUpdatesAsync().ConfigureAwait(true);
                if (info == null)
                {
                    if (manual) toast("You're up to date ✓");
                    return;
                }

                if (manual) toast($"Downloading {info.TargetFullRelease.Version}…");
                await mgr.DownloadUpdatesAsync(info).ConfigureAwait(true);
                _pending = info;
                if (manual)
                {
                    toast("Restarting to update…");
                    await Task.Delay(700);
                    mgr.ApplyUpdatesAndRestart(info.TargetFullRelease);
                }
                else
                {
                    // Apply quietly the next time the app exits.
                    mgr.WaitExitThenApplyUpdates(info.TargetFullRelease, true, false);
                    toast($"Update {info.TargetFullRelease.Version} ready — installs on exit");
                }
            }
            catch (Exception ex)
            {
                App.Log("Update check: " + ex.Message);
                if (manual) toast("Couldn't reach the update server");
            }
            finally
            {
                _running = false;
            }
        }
    }
}
