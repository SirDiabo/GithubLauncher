using GitHubLauncher.Core.Models;
using GitHubLauncher.Core.Services;
using GithubLauncher.Models;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Text.Json;

namespace GithubLauncher.Services
{
    public class GameManager : INotifyPropertyChanged, IDisposable
    {
        private static readonly GithubLauncherProfile Profile = GithubLauncherProfile.Instance;
        public AppSettings _settings = new();
        private readonly HttpClient _httpClient;
        private bool _disposed;
        private string _appsFolder;
        private readonly string _cacheFolder;
        private readonly string _appsConfigPath;
        private readonly string _legacyGamesConfigPath;

        public ObservableCollection<GameInfo> Games { get; set; } = [];
        public HttpClient HttpClient => _httpClient;
        public string AppsFolder => _appsFolder;
        public string GamesFolder => _appsFolder;
        public string CacheFolder => _cacheFolder;

        private string _currentVersionString = string.Empty;
        public string CurrentVersionString
        {
            get => _currentVersionString;
            set
            {
                if (_currentVersionString != value)
                {
                    _currentVersionString = value;
                    OnPropertyChanged(nameof(CurrentVersionString));
                }
            }
        }

        public GameManager()
        {
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", Profile.UserAgent);
            _httpClient.Timeout = TimeSpan.FromMinutes(30);

            try
            {
                _settings = AppSettings.Load();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load settings in GameManager: {ex.Message}");
                _settings = new AppSettings();
            }

            _appsFolder = !string.IsNullOrEmpty(_settings?.AppsPath)
                ? _settings.AppsPath
                : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, Profile.DefaultInstallFolderName);

            _cacheFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Cache");
            _appsConfigPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "apps.json");
            _legacyGamesConfigPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "games.json");

            try
            {
                Directory.CreateDirectory(_appsFolder);
                Directory.CreateDirectory(_cacheFolder);
                GitHubApiCache.Initialize(_cacheFolder);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to create directories: {ex.Message}");
            }

            LoadVersionString();
            _ = ValidateAndFixAppsJsonAsync();
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
                return;

            if (disposing)
                _httpClient.Dispose();

            _disposed = true;
        }

        public async Task CheckAllUpdatesAsync()
        {
            await LoadGamesAsync(forceUpdateCheck: true);
        }

        private async Task ValidateAndFixAppsJsonAsync()
        {
            try
            {
                var apps = await LoadAppsFromJsonAsync().ConfigureAwait(false);
                await SaveAppsToJsonAsync(apps).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error during apps.json integrity check: {ex.Message}");
            }
        }

        private void LoadVersionString()
        {
            try
            {
                string versionFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "version.txt");
                CurrentVersionString = File.Exists(versionFilePath)
                    ? File.ReadAllText(versionFilePath).Trim()
                    : "Version information not found";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading version: {ex.Message}");
                CurrentVersionString = "Version loading failed";
            }
        }

        public GameInfo? GetLatestPlayedInstalledGame()
        {
            if (Games == null || string.IsNullOrEmpty(_appsFolder))
                return null;

            DateTime latestTime = DateTime.MinValue;
            GameInfo? latestGame = null;
            foreach (var game in Games)
            {
                if (game == null || string.IsNullOrEmpty(game.FolderName))
                    continue;

                var gamePath = game.GetInstallPath(_appsFolder);
                var lastPlayedPath = Path.Combine(gamePath, "LastPlayed.txt");
                if (File.Exists(lastPlayedPath))
                {
                    var timeString = File.ReadAllText(lastPlayedPath).Trim();
                    if (DateTime.TryParseExact(timeString, "yyyy-MM-dd HH:mm:ss", null, System.Globalization.DateTimeStyles.None, out DateTime lastPlayed) && lastPlayed > latestTime)
                    {
                        latestTime = lastPlayed;
                        latestGame = game;
                    }
                }
            }
            return latestGame;
        }

        private async Task<List<GameInfo>> LoadAppsFromJsonAsync()
        {
            if (!File.Exists(_appsConfigPath))
            {
                if (File.Exists(_legacyGamesConfigPath))
                {
                    var migratedApps = await LoadAppsFromFileAsync(_legacyGamesConfigPath).ConfigureAwait(false);
                    await SaveAppsToJsonAsync(migratedApps).ConfigureAwait(false);
                    return migratedApps;
                }

                await SaveAppsToJsonAsync([]).ConfigureAwait(false);
                return [];
            }

            return await LoadAppsFromFileAsync(_appsConfigPath).ConfigureAwait(false);
        }

        private async Task<List<GameInfo>> LoadAppsFromFileAsync(string path)
        {
            try
            {
                string json = await File.ReadAllTextAsync(path).ConfigureAwait(false);
                using var document = JsonDocument.Parse(json);
                return ParseAppsRoot(document.RootElement);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error reading {Path.GetFileName(path)}: {ex.Message}");
                return [];
            }
        }

        private List<GameInfo> ParseAppsRoot(JsonElement root)
        {
            var apps = new List<GameInfo>();

            if (root.ValueKind == JsonValueKind.Array)
            {
                apps.AddRange(ParseAppArray(root));
                return apps;
            }

            if (root.TryGetProperty("apps", out var appsArray))
            {
                apps.AddRange(ParseAppArray(appsArray));
            }

            foreach (var legacySection in new[] { "standard", "experimental", "custom" })
            {
                if (root.TryGetProperty(legacySection, out var legacyArray))
                    apps.AddRange(ParseAppArray(legacyArray));
            }

            return apps
                .GroupBy(app => app.Repository ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();
        }

        private List<GameInfo> ParseAppArray(JsonElement appsArray)
        {
            var apps = new List<GameInfo>();

            foreach (var appElement in appsArray.EnumerateArray())
            {
                try
                {
                    var app = new GameInfo
                    {
                        Name = (appElement.TryGetProperty("name", out var nameElement) ? nameElement.GetString() : null) ?? string.Empty,
                        Repository = (appElement.TryGetProperty("repository", out var repoElement) ? repoElement.GetString() : null) ?? string.Empty,
                        FolderName = (appElement.TryGetProperty("folderName", out var folderElement) ? folderElement.GetString() : null) ?? string.Empty,
                        InstallPath = appElement.TryGetProperty("installPath", out var installPathElement) ? installPathElement.GetString() : null,
                        GameIconUrl = GetIconUrl(appElement),
                        PreferredVersion = appElement.TryGetProperty("preferredVersion", out var preferredVersionElement) ? preferredVersionElement.GetString() : null,
                        SkippedUpdateVersion = appElement.TryGetProperty("skippedUpdateVersion", out var skippedUpdateVersionElement) ? skippedUpdateVersionElement.GetString() : null,
                        IsExperimental = false,
                        IsCustom = true,
                        GameManager = this,
                    };

                    apps.Add(app);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error parsing app: {ex.Message}");
                }
            }

            return apps;
        }

        private static string? GetIconUrl(JsonElement appElement)
        {
            if (appElement.TryGetProperty("appIconUrl", out var appIconUrlElement) && appIconUrlElement.ValueKind != JsonValueKind.Null)
                return appIconUrlElement.GetString();

            if (appElement.TryGetProperty("gameIconUrl", out var gameIconUrlElement) && gameIconUrlElement.ValueKind != JsonValueKind.Null)
                return gameIconUrlElement.GetString();

            if (appElement.TryGetProperty("customDefaultIconUrl", out var legacyIconElement) && legacyIconElement.ValueKind != JsonValueKind.Null)
                return legacyIconElement.GetString();

            return null;
        }

        private static object SerializeApp(GameInfo app)
        {
            return new
            {
                name = app.Name,
                repository = app.Repository,
                folderName = app.FolderName,
                installPath = app.InstallPath,
                appIconUrl = app.GameIconUrl,
                preferredVersion = app.PreferredVersion,
                skippedUpdateVersion = app.SkippedUpdateVersion
            };
        }

        private async Task SaveAppsToJsonAsync(List<GameInfo> apps)
        {
            var data = new
            {
                apps = apps.Select(SerializeApp).ToList()
            };

            var options = new JsonSerializerOptions { WriteIndented = true };
            await File.WriteAllTextAsync(_appsConfigPath, JsonSerializer.Serialize(data, options)).ConfigureAwait(false);
        }

        private void SaveAppsToJson(List<GameInfo> apps)
        {
            var data = new
            {
                apps = apps.Select(SerializeApp).ToList()
            };

            var options = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(_appsConfigPath, JsonSerializer.Serialize(data, options));
        }

        private async Task LoadCustomAndCachedIconsAsync()
        {
            if (Games == null || string.IsNullOrEmpty(_cacheFolder))
                return;

            foreach (var game in Games)
            {
                game?.LoadCustomIcon(_cacheFolder);
            }

            var tasks = Games
                .Where(g => g != null)
                .Select(g => g.LoadAndCacheDefaultIconAsync(_cacheFolder));

            await Task.WhenAll(tasks);
        }

        public async Task ClearIconCacheAsync()
        {
            try
            {
                var iconsDir = Path.Combine(_cacheFolder, "Icons");
                if (Directory.Exists(iconsDir))
                {
                    Directory.Delete(iconsDir, true);
                    await LoadCustomAndCachedIconsAsync();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to clear icon cache: {ex.Message}");
            }
        }

        public GameInfo? FindGameByName(string name)
        {
            return Games.FirstOrDefault(g => string.Equals(g.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        public GameInfo? FindGameByFolderName(string folderName)
        {
            return Games.FirstOrDefault(g => string.Equals(g.FolderName, folderName, StringComparison.OrdinalIgnoreCase));
        }

        public Task<List<GameInfo>> GetAppsAsync()
        {
            return LoadAppsFromJsonAsync();
        }

        public Task SaveAppsAsync(List<GameInfo> apps)
        {
            return SaveAppsToJsonAsync(apps);
        }

        public async Task LoadGamesAsync(bool forceUpdateCheck = false)
        {
            var settings = AppSettings.Load();

            Games ??= [];
            var allApps = await LoadAppsFromJsonAsync();
            var filteredApps = allApps
                .Where(app => app != null && !IsGameHidden(settings, app))
                .ToList();

            Games.Clear();

            foreach (var app in filteredApps)
            {
                if (app != null)
                    Games.Add(app);
            }

            await LoadCustomAndCachedIconsAsync();

            if (string.IsNullOrEmpty(_appsFolder))
                return;

            await Task.WhenAll(Games.Where(app => app != null).Select(async app =>
            {
                try
                {
                    await app.CheckStatusAsync(_httpClient, _appsFolder, forceUpdateCheck);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error checking status for {app.Name}: {ex.Message}");
                }
            }));
        }

        public async Task ExportGamesAsync()
        {
            try
            {
                var apps = await LoadAppsFromJsonAsync().ConfigureAwait(false);
                await SaveAppsToJsonAsync(apps).ConfigureAwait(false);
                System.Diagnostics.Debug.WriteLine($"Apps exported successfully to {_appsConfigPath}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error exporting apps: {ex.Message}");
            }
        }

        public async Task UpdateGamesFolderAsync(string newPath)
        {
            try
            {
                string targetPath;

                if (!string.IsNullOrWhiteSpace(newPath))
                {
                    if (!Directory.Exists(newPath))
                        Directory.CreateDirectory(newPath);

                    targetPath = newPath;
                }
                else
                {
                    targetPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, Profile.DefaultInstallFolderName);
                    Directory.CreateDirectory(targetPath);
                }

                _appsFolder = targetPath;
                Games.Clear();

                await LoadGamesAsync();

                OnPropertyChanged(nameof(Games));
                OnPropertyChanged(nameof(AppsFolder));
                OnPropertyChanged(nameof(GamesFolder));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating apps folder: {ex.Message}");
                _appsFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, Profile.DefaultInstallFolderName);
                Directory.CreateDirectory(_appsFolder);
                throw;
            }
        }

        private static string GetHiddenGameKey(GameInfo game)
        {
            if (!string.IsNullOrWhiteSpace(game.FolderName))
                return $"folder:{game.FolderName}";

            if (!string.IsNullOrWhiteSpace(game.Repository))
                return $"repo:{game.Repository}";

            return $"name:{game.Name ?? string.Empty}";
        }

        private static bool IsGameHidden(AppSettings settings, GameInfo game)
        {
            if (settings?.HiddenApps == null)
                return false;

            var hiddenKey = GetHiddenGameKey(game);
            return settings.HiddenApps.Contains(hiddenKey) ||
                   (!string.IsNullOrWhiteSpace(game.Name) && settings.HiddenApps.Contains(game.Name)) ||
                   IsGameManuallyHidden(settings, game);
        }

        public void ToggleUserHide(GameInfo game)
        {
            if (game == null)
                return;

            var settings = AppSettings.Load();
            if (IsGameManuallyHidden(settings, game))
            {
                RemoveManuallyHiddenGame(settings, game);
            }
            else
            {
                AddManuallyHiddenGame(settings, game);
            }
            AppSettings.Save(settings);
            FilterGames(settings);
        }

        public bool IsManuallyHidden(GameInfo game)
        {
            var settings = AppSettings.Load();
            return IsGameManuallyHidden(settings, game);
        }

        private static void AddHiddenGame(AppSettings settings, GameInfo game)
        {
            if (settings?.HiddenApps == null)
                return;

            var hiddenKey = GetHiddenGameKey(game);
            if (!settings.HiddenApps.Contains(hiddenKey))
                settings.HiddenApps.Add(hiddenKey);
        }

        public void HideGame(GameInfo game)
        {
            if (game == null)
                return;

            var settings = AppSettings.Load();
            if (!IsGameHidden(settings, game))
            {
                AddHiddenGame(settings, game);
                AppSettings.Save(settings);
                FilterGames(settings);
            }
        }

        public void UnhideAllGames()
        {
            var settings = AppSettings.Load();
            settings.HiddenApps.Clear();
            AppSettings.Save(settings);
            FilterGames(settings);
        }

        public async Task HideAllNonInstalledGames()
        {
            var settings = AppSettings.Load();
            settings.HiddenApps.Clear();
            AppSettings.Save(settings);

            await LoadGamesAsync();

            foreach (var game in Games)
            {
                if (game != null && game.Status == GameStatus.NotInstalled && !IsGameHidden(settings, game))
                    AddHiddenGame(settings, game);
            }
            AppSettings.Save(settings);
            await LoadGamesAsync();
        }

        private List<GameInfo> LoadGamesFromJson()
        {
            return LoadAppsFromJsonAsync().GetAwaiter().GetResult();
        }

        private void FilterGames(AppSettings settings)
        {
            if (Games == null || settings?.HiddenApps == null)
                return;

            for (int i = Games.Count - 1; i >= 0; i--)
            {
                if (Games[i] != null && IsGameHidden(settings, Games[i]))
                    Games.RemoveAt(i);
            }
        }

        private static bool IsGameManuallyHidden(AppSettings settings, GameInfo game)
        {
            if (settings?.ManuallyHiddenApps == null)
                return false;

            var key = GetHiddenGameKey(game);
            return settings.ManuallyHiddenApps.Contains(key) ||
                   (!string.IsNullOrWhiteSpace(game.Name) && settings.ManuallyHiddenApps.Contains(game.Name));
        }

        private static void AddManuallyHiddenGame(AppSettings settings, GameInfo game)
        {
            if (settings?.ManuallyHiddenApps == null)
                return;

            var key = GetHiddenGameKey(game);
            if (!settings.ManuallyHiddenApps.Contains(key))
                settings.ManuallyHiddenApps.Add(key);
        }

        private static void RemoveManuallyHiddenGame(AppSettings settings, GameInfo game)
        {
            if (settings?.ManuallyHiddenApps == null)
                return;

            var key = GetHiddenGameKey(game);
            settings.ManuallyHiddenApps.Remove(key);
            if (!string.IsNullOrWhiteSpace(game.Name))
                settings.ManuallyHiddenApps.Remove(game.Name);
        }

        public void RefreshGamesWithFilter(AppSettings settings)
        {
            _ = LoadGamesAsync();
        }

        public void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
