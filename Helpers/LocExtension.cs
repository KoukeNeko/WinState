using System.Windows.Data;
using System.Windows.Markup;
using WinState.Services;

namespace WinState.Helpers
{
    /// <summary>
    /// XAML markup extension: {helpers:Loc Key=Some_Key} resolves to the current localized string
    /// and updates live when the language changes. Backed by a OneWay binding to
    /// LocalizationService.Instance["Some_Key"], so the indexer's PropertyChanged drives refreshes.
    /// </summary>
    [MarkupExtensionReturnType(typeof(string))]
    public class LocExtension : MarkupExtension
    {
        public string Key { get; set; } = string.Empty;

        public LocExtension() { }
        public LocExtension(string key) { Key = key; }

        public override object ProvideValue(IServiceProvider serviceProvider)
        {
            var binding = new Binding($"[{Key}]")
            {
                Source = LocalizationService.Instance,
                Mode = BindingMode.OneWay,
            };
            return binding.ProvideValue(serviceProvider);
        }
    }
}
