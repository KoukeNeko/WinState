namespace WinState.Models
{
    /// <summary>
    /// Per-category data refresh intervals, in milliseconds.
    /// </summary>
    public class RefreshSettings
    {
        public const int Default = 1000;
        public const int Min = 250;
        public const int Max = 10000;

        public int Cpu { get; set; } = Default;
        public int Gpu { get; set; } = Default;
        public int Memory { get; set; } = Default;
        public int Disk { get; set; } = Default;
        public int Network { get; set; } = Default;

        public static RefreshSettings CreateDefault() => new RefreshSettings();

        public static int Clamp(int value) => value < Min ? Min : (value > Max ? Max : value);
    }
}
