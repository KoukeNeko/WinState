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
        
        CpuSettings GetCpuSettings();
        void SaveCpuSettings(CpuSettings settings);
    }

    /// <summary>
    /// CPU display settings.
    /// </summary>
    public class CpuSettings
    {
        /// <summary>
        /// Number of processes to display in the process list. Default: 15
        /// </summary>
        public int ProcessCount { get; set; } = 15;

        public static CpuSettings CreateDefault() => new CpuSettings { ProcessCount = 15 };
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
        private CpuSettings? _cachedCpuSettings;

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
            try
            {
                var wrapper = LoadSettingsWrapper();
                wrapper.TrayIconSettings = settings;
                
                var json = JsonSerializer.Serialize(wrapper, _jsonOptions);
                File.WriteAllText(_settingsFilePath, json);
                
                _cachedTraySettings = settings;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save settings: {ex.Message}");
            }
        }

        public void ResetTrayIconSettings()
        {
            var defaultSettings = TrayIconSettings.CreateDefault();
            SaveTrayIconSettings(defaultSettings);
        }

        public CpuSettings GetCpuSettings()
        {
            if (_cachedCpuSettings != null)
            {
                return _cachedCpuSettings;
            }

            var wrapper = LoadSettingsWrapper();
            if (wrapper.CpuSettings != null)
            {
                _cachedCpuSettings = wrapper.CpuSettings;
                return _cachedCpuSettings;
            }

            _cachedCpuSettings = CpuSettings.CreateDefault();
            SaveCpuSettings(_cachedCpuSettings);
            return _cachedCpuSettings;
        }

        public void SaveCpuSettings(CpuSettings settings)
        {
            try
            {
                var wrapper = LoadSettingsWrapper();
                wrapper.CpuSettings = settings;
                
                var json = JsonSerializer.Serialize(wrapper, _jsonOptions);
                File.WriteAllText(_settingsFilePath, json);
                
                _cachedCpuSettings = settings;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save CPU settings: {ex.Message}");
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
            public CpuSettings? CpuSettings { get; set; }
        }
    }
}
