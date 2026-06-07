namespace WinState.Models
{
    /// <summary>
    /// Settings for tray icon order and visibility.
    /// </summary>
    public class TrayIconSettings
    {
        /// <summary>
        /// List of tray icon configurations in display order.
        /// </summary>
        public List<TrayIconEntry> Icons { get; set; } = new();

        /// <summary>
        /// Creates default settings with standard icon order.
        /// </summary>
        public static TrayIconSettings CreateDefault()
        {
            return new TrayIconSettings
            {
                Icons = new List<TrayIconEntry>
                {
                    new TrayIconEntry { Id = "CPU", DisplayName = "CPU", Order = 0, IsVisible = true },
                    new TrayIconEntry { Id = "GPU", DisplayName = "GPU", Order = 1, IsVisible = true },
                    new TrayIconEntry { Id = "RAM", DisplayName = "RAM", Order = 2, IsVisible = true },
                    new TrayIconEntry { Id = "DISK", DisplayName = "Disk", Order = 3, IsVisible = true },
                    new TrayIconEntry { Id = "NET", DisplayName = "Network", Order = 4, IsVisible = true },
                    new TrayIconEntry { Id = "POWER", DisplayName = "Power", Order = 5, IsVisible = true }
                }
            };
        }

    }

    /// <summary>
    /// Individual tray icon configuration.
    /// </summary>
    public class TrayIconEntry
    {
        // Default warning-color thresholds for percentage-based icons (CPU/GPU/RAM/DISK).
        public const int DefaultWarn = 70;
        public const int DefaultHigh = 80;
        public const int DefaultCritical = 90;

        /// <summary>
        /// Unique identifier: CPU, GPU, RAM, DISK, NET, POWER
        /// </summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// Display name for settings UI.
        /// </summary>
        public string DisplayName { get; set; } = string.Empty;

        /// <summary>
        /// Whether the icon should be shown in the system tray.
        /// </summary>
        public bool IsVisible { get; set; } = true;

        /// <summary>
        /// Order index for icon creation (lower = created first).
        /// </summary>
        public int Order { get; set; }

        /// <summary>
        /// Usage % at which the icon text turns yellow (percentage icons only).
        /// </summary>
        public int WarnThreshold { get; set; } = DefaultWarn;

        /// <summary>
        /// Usage % at which the icon text turns orange.
        /// </summary>
        public int HighThreshold { get; set; } = DefaultHigh;

        /// <summary>
        /// Usage % at which the icon text turns red.
        /// </summary>
        public int CriticalThreshold { get; set; } = DefaultCritical;
    }
}
