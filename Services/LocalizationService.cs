using System.ComponentModel;
using System.Globalization;
using System.Resources;
using System.Runtime.CompilerServices;

namespace WinState.Services
{
    /// <summary>
    /// Live, restart-free localization for the WPF app. XAML binds string properties to this
    /// service's string indexer via the {helpers:Loc Key=...} markup extension. When the culture
    /// changes we raise PropertyChanged for the indexer ("Item[]"), which tells every bound
    /// element to re-pull its string — so switching language updates the whole UI instantly.
    /// </summary>
    public sealed class LocalizationService : INotifyPropertyChanged
    {
        public static LocalizationService Instance { get; } = new();

        // Base name must match the embedded .resources: {RootNamespace}.Resources.Strings →
        // Resources\Strings.resx (English/neutral) with a Strings.zh-Hant.resx satellite.
        private static readonly ResourceManager Rm =
            new("WinState.Resources.Strings", typeof(LocalizationService).Assembly);

        private CultureInfo _culture = CultureInfo.CurrentUICulture;

        public CultureInfo Culture
        {
            get => _culture;
            set
            {
                if (Equals(_culture, value)) return;
                _culture = value;
                // Refresh every {Loc} binding plus anything watching Culture directly.
                OnPropertyChanged("Item[]");
                OnPropertyChanged(nameof(Culture));
            }
        }

        [IndexerName("Item")]
        public string this[string key]
        {
            get
            {
                if (string.IsNullOrEmpty(key)) return string.Empty;
                return Rm.GetString(key, _culture) ?? key;
            }
        }

        public string Get(string key) => this[key];

        /// <summary>The languages the app ships, in the order shown in the settings picker.</summary>
        public static readonly (string Code, string DisplayKey)[] SupportedLanguages =
        {
            ("Auto", "Settings_LanguageAuto"),
            ("en", "English"),
            ("zh-Hant", "繁體中文"),
        };

        /// <summary>
        /// Apply a saved language code ("Auto" / "en" / "zh-Hant"). Auto maps to the system UI
        /// culture, falling back to English for anything that isn't a Chinese variant.
        /// </summary>
        public void ApplyLanguage(string? code)
        {
            Culture = ResolveCulture(code);
        }

        private static CultureInfo ResolveCulture(string? code)
        {
            if (string.IsNullOrEmpty(code) || code == "Auto")
            {
                var sys = CultureInfo.CurrentUICulture;
                // Treat any Chinese variant as Traditional Chinese; everything else as English.
                return sys.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase)
                    ? new CultureInfo("zh-Hant")
                    : new CultureInfo("en");
            }
            try { return new CultureInfo(code); }
            catch { return new CultureInfo("en"); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
