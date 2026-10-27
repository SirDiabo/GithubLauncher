using System.Diagnostics;
using System.Text.Json;

namespace GithubLauncher.Services;

public static class MacOSUpdater
{
    public static async Task ExtractArchiveAsync(string archive, string destination)
    {
        var startInfo = new ProcessStartInfo("/usr/bin/ditto")
        {
            UseShellExecute = false,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add("-x");
        startInfo.ArgumentList.Add("-k");
        startInfo.ArgumentList.Add(archive);
        startInfo.ArgumentList.Add(destination);
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not extract the macOS update.");
        string error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"macOS update extraction failed: {error}");
    }

    public static Task StartAsync(string updateDirectory, string version)
    {
        string bundle = LauncherPaths.AppBundle
            ?? throw new InvalidOperationException("Run the launcher from its .app bundle to update it.");
        return StartAsync(updateDirectory, version, bundle, LauncherPaths.DataDirectory);
    }

    public static async Task StartAsync(string updateDirectory, string version, string bundle, string dataDirectory)
    {
        string source = Path.Combine(updateDirectory, "GithubLauncher.app");
        string executable = Path.Combine(source, "Contents", "MacOS", "GithubLauncher");
        if (!File.Exists(executable))
            throw new InvalidOperationException("The update does not contain a complete GithubLauncher.app.");

        var verify = new ProcessStartInfo("/usr/bin/codesign")
        {
            UseShellExecute = false,
            RedirectStandardError = true
        };
        verify.ArgumentList.Add("--verify");
        verify.ArgumentList.Add("--deep");
        verify.ArgumentList.Add("--strict");
        verify.ArgumentList.Add(source);
        using (var process = Process.Start(verify)
            ?? throw new InvalidOperationException("Could not verify the update signature."))
        {
            string error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            if (process.ExitCode != 0)
                throw new InvalidOperationException($"Update signature verification failed: {error}");
        }

        string scriptPath = Path.Combine(Path.GetTempPath(), $"GithubLauncher-update-{Guid.NewGuid():N}.sh");
        string backup = bundle + $".backup-{Guid.NewGuid():N}";
        string statePath = Path.Combine(dataDirectory, "update_check.json");
        string state = JsonSerializer.Serialize(new
        {
            CurrentVersion = version,
            LastCheckTime = DateTime.UtcNow,
            LastKnownVersion = version,
            ETag = string.Empty,
            UpdateAvailable = false
        });
        string script = $"""
            #!/bin/bash
            set -eu
            count=0
            while kill -0 {Environment.ProcessId} 2>/dev/null; do
                if [ "$count" -ge 120 ]; then
                    printf '%s\n' 'Launcher did not exit; update aborted.' >&2
                    exit 1
                fi
                count=$((count + 1))
                sleep 1
            done
            rollback() (
                result=$?
                if [ "$result" -ne 0 ] && [ -d {Quote(backup)} ]; then
                    rm -rf {Quote(bundle)}
                    mv {Quote(backup)} {Quote(bundle)} || exit 1
                    printf '%s\n' 'Update failed. Original app restored.' >&2
                fi
                exit "$result"
            )
            trap rollback EXIT
            mv {Quote(bundle)} {Quote(backup)}
            if ! /usr/bin/ditto {Quote(source)} {Quote(bundle)}; then
                printf '%s\n' 'Update failed.' >&2
                exit 1
            fi
            if ! /usr/bin/codesign --verify --deep --strict {Quote(bundle)}; then
                printf '%s\n' 'Installed signature invalid.' >&2
                exit 1
            fi
            printf '%s\n' {Quote(state)} > {Quote(statePath)}
            /usr/bin/open {Quote(bundle)}
            rm -rf {Quote(backup)}
            printf '%s\n' 'Update complete.'
            """;
        await File.WriteAllTextAsync(scriptPath, script);
        var startInfo = new ProcessStartInfo("/bin/bash") { UseShellExecute = false };
        startInfo.ArgumentList.Add(scriptPath);
        using var updater = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start the macOS updater.");
    }

    private static string Quote(string value) => "'" + value.Replace("'", "'\"'\"'") + "'";
}
