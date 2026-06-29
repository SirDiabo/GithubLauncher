using System;
using System.IO;
using System.Runtime.InteropServices;

namespace GithubLauncher
{
    /// <summary>
    /// Resolves where the launcher stores writable data.
    ///
    /// On macOS the launcher ships as a signed <c>.app</c> bundle whose contents
    /// must stay immutable — writing next to the executable (inside
    /// <c>Contents/MacOS</c>) invalidates the code signature and prevents the app
    /// from living in a read-only location such as <c>/Applications</c>. So on
    /// macOS user data lives in <c>~/Library/Application Support/GithubLauncher</c>.
    ///
    /// On Windows/Linux behavior is unchanged: data stays next to the executable
    /// (portable app layout).
    /// </summary>
    public static class AppPaths
    {
        /// <summary>Executable location. Use only for self-update; never for user data on macOS.</summary>
        public static string AppDirectory => AppContext.BaseDirectory;

        private static readonly Lazy<string> _dataDirectory = new(() =>
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                string appSupport = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "Library", "Application Support", "GithubLauncher");
                Directory.CreateDirectory(appSupport);
                return appSupport;
            }

            return AppContext.BaseDirectory;
        });

        /// <summary>Writable data root (settings, app registry, caches, installed apps). Created on first access.</summary>
        public static string DataDirectory => _dataDirectory.Value;
    }
}
