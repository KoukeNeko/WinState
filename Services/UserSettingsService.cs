using System.IO;
using System.Text.Json;
using WinState.Models;

namespace WinState.Services
{
    /// <summary>
    /// Interface for user settings management.
    /// </summary>
    public interface IUserSettingsService
    {
        TrayIconSettings GetTrayIconSettings();
        void SaveTrayIconSettings(TrayIconSettings settings);
        void ResetTrayIconSettings();

        ProcessListSettings GetProcessListSettings();
        void SaveProcessListSettings(ProcessListSettings settings);

        RefreshSettings GetRefreshSettings();
        void SaveRefreshSettings(RefreshSettings settings);
    }

    /// <summary>
    /// Service for managing user settings persistence.
    /// </summary>
    public class UserSettingsService : IUserSettingsService
    {
        private const string SETTINGS_FOLDER_NAME = "WinState";
        private const string SETTINGS_FILE_NAME = "usersettings.json";

        private readonly string _settingsFilePath;
        private readonly JsonSerializerOptions _jsonOptions;

        private TrayIconSettings? _cachedTraySettings;
        private ProcessListSettings? _cachedProcessListSettings;
        private RefreshSettings? _cachedRefreshSettings;

        public UserSettingsService()
        {
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var settingsFolder = Path.Combine(appDataPath, SETTINGS_FOLDER_NAME);

            if (!Directory.Exists(settingsFolder))
            {
                Directory.CreateDirectory(settingsFolder);
            }

            _settingsFilePath = Path.Combine(settingsFolder, SETTINGS_FILE_NAME);
            _jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };
        }

        // Serialises every read-modify-write across the public Save* methods. Without it, two
        // concurrent saves (e.g. a settings UI handler firing while a tray-icon reorder is mid-
        // save) can both Load then Write and the second one silently clobbers the first.
        private readonly object _settingsLock = new();

        // Atomic write: serialise to a sibling .tmp file then rename over the target. A crash or
        // power loss mid-write can leave the .tmp behind but never a half-written settings file
        // (which would otherwise refuse to parse on the next launch).
        private void WriteSettingsAtomically(string json)
        {
            var tempPath = _settingsFilePath + ".tmp";
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, _settingsFilePath, overwrite: true);
        }

        public TrayIconSettings GetTrayIconSettings()
        {
            if (_cachedTraySettings != null)
            {
                return _cachedTraySettings;
            }

            var wrapper = LoadSettingsWrapper();
            if (wrapper.TrayIconSettings != null)
            {
                _cachedTraySettings = wrapper.TrayIconSettings;
                return _cachedTraySettings;
            }

            _cachedTraySettings = TrayIconSettings.CreateDefault();
            SaveTrayIconSettings(_cachedTraySettings);
            return _cachedTraySettings;
        }

        public void SaveTrayIconSettings(TrayIconSettings settings)
        {
            lock (_settingsLock)
            {
                try
                {
                    var wrapper = LoadSettingsWrapper();
                    wrapper.TrayIconSettings = settings;

                    var json = JsonSerializer.Serialize(wrapper, _jsonOptions);
                    WriteSettingsAtomically(json);

                    _cachedTraySettings = settings;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to save settings: {ex.Message}");
                }
            }
        }

        public void ResetTrayIconSettings()
        {
            var defaultSettings = TrayIconSettings.CreateDefault();
            SaveTrayIconSettings(defaultSettings);
        }

        public ProcessListSettings GetProcessListSettings()
        {
            if (_cachedProcessListSettings != null)
            {
                return _cachedProcessListSettings;
            }

            var wrapper = LoadSettingsWrapper();
            if (wrapper.ProcessListSettings != null)
            {
                _cachedProcessListSettings = wrapper.ProcessListSettings;
                return _cachedProcessListSettings;
            }

            _cachedProcessListSettings = ProcessListSettings.CreateDefault();
            SaveProcessListSettings(_cachedProcessListSettings);
            return _cachedProcessListSettings;
        }

        public void SaveProcessListSettings(ProcessListSettings settings)
        {
            lock (_settingsLock)
            {
                try
                {
                    var wrapper = LoadSettingsWrapper();
                    wrapper.ProcessListSettings = settings;

                    var json = JsonSerializer.Serialize(wrapper, _jsonOptions);
                    WriteSettingsAtomically(json);

                    _cachedProcessListSettings = settings;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to save process list settings: {ex.Message}");
                }
            }
        }

        public RefreshSettings GetRefreshSettings()
        {
            if (_cachedRefreshSettings != null)
            {
                return _cachedRefreshSettings;
            }

            var wrapper = LoadSettingsWrapper();
            if (wrapper.RefreshSettings != null)
            {
                _cachedRefreshSettings = wrapper.RefreshSettings;
                return _cachedRefreshSettings;
            }

            _cachedRefreshSettings = RefreshSettings.CreateDefault();
            SaveRefreshSettings(_cachedRefreshSettings);
            return _cachedRefreshSettings;
        }

        public void SaveRefreshSettings(RefreshSettings settings)
        {
            // Keep intervals within the supported range regardless of caller input.
            settings.Cpu = RefreshSettings.Clamp(settings.Cpu);
            settings.Gpu = RefreshSettings.Clamp(settings.Gpu);
            settings.Memory = RefreshSettings.Clamp(settings.Memory);
            settings.Disk = RefreshSettings.Clamp(settings.Disk);
            settings.Network = RefreshSettings.Clamp(settings.Network);

            lock (_settingsLock)
            {
                try
                {
                    var wrapper = LoadSettingsWrapper();
                    wrapper.RefreshSettings = settings;

                    var json = JsonSerializer.Serialize(wrapper, _jsonOptions);
                    WriteSettingsAtomically(json);

                    _cachedRefreshSettings = settings;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to save refresh settings: {ex.Message}");
                }
            }
        }

        private UserSettingsWrapper LoadSettingsWrapper()
        {
            if (File.Exists(_settingsFilePath))
            {
                try
                {
                    var json = File.ReadAllText(_settingsFilePath);
                    return JsonSerializer.Deserialize<UserSettingsWrapper>(json, _jsonOptions) ?? new UserSettingsWrapper();
                }
                catch
                {
                    return new UserSettingsWrapper();
                }
            }
            return new UserSettingsWrapper();
        }

        /// <summary>
        /// Wrapper class for all user settings to allow future expansion.
        /// </summary>
        private class UserSettingsWrapper
        {
            public TrayIconSettings? TrayIconSettings { get; set; }
            public ProcessListSettings? ProcessListSettings { get; set; }
            public RefreshSettings? RefreshSettings { get; set; }
        }
    }
}
