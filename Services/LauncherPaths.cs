using System;
using System.IO;

namespace GithubLauncher.Services
{
    public static class LauncherPaths
    {
        public static string DataDirectory { get; } = ResolveDataDirectory();

        private static string ResolveDataDirectory()
        {
            if (!OperatingSystem.IsMacOS())
                return AppContext.BaseDirectory;

            string baseDirectory = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (string.IsNullOrEmpty(baseDirectory))
                return AppContext.BaseDirectory;

            string directory = Path.Combine(baseDirectory, GithubLauncherProfile.Instance.ApplicationId);
            Directory.CreateDirectory(directory);
            return directory;
        }

        public static string? AppBundle
        {
            get
            {
                if (!OperatingSystem.IsMacOS())
                    return null;

                var directory = new DirectoryInfo(AppContext.BaseDirectory);
                if (directory.Name == "MacOS" && directory.Parent?.Name == "Contents" &&
                    directory.Parent.Parent?.Extension == ".app")
                    return directory.Parent.Parent.FullName;

                return null;
            }
        }
    }
}
