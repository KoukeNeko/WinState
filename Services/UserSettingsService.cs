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

        private TrayIconSettings? _cachedSettings;

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
            if (_cachedSettings != null)
            {
                return _cachedSettings;
            }

            if (File.Exists(_settingsFilePath))
            {
                try
                {
                    var json = File.ReadAllText(_settingsFilePath);
                    var settingsWrapper = JsonSerializer.Deserialize<UserSettingsWrapper>(json, _jsonOptions);
                    
                    if (settingsWrapper?.TrayIconSettings != null)
                    {
                        _cachedSettings = settingsWrapper.TrayIconSettings;
                        return _cachedSettings;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to load settings: {ex.Message}");
                }
            }

            _cachedSettings = TrayIconSettings.CreateDefault();
            SaveTrayIconSettings(_cachedSettings);
            return _cachedSettings;
        }

        public void SaveTrayIconSettings(TrayIconSettings settings)
        {
            try
            {
                var wrapper = LoadSettingsWrapper();
                wrapper.TrayIconSettings = settings;
                
                var json = JsonSerializer.Serialize(wrapper, _jsonOptions);
                File.WriteAllText(_settingsFilePath, json);
                
                _cachedSettings = settings;
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
        }
    }
}
