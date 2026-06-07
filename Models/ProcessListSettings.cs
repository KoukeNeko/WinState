namespace WinState.Models
{
    /// <summary>
    /// Number of processes shown in each category's process list.
    /// </summary>
    public class ProcessListSettings
    {
        public const int Min = 1;
        public const int Max = 50;
        public const int Default = 15;

        public int Cpu { get; set; } = Default;
        public int Memory { get; set; } = Default;
        public int Network { get; set; } = Default;
        public int Disk { get; set; } = Default;

        public static ProcessListSettings CreateDefault() => new ProcessListSettings();

        public static int Clamp(int value) => value < Min ? Min : value > Max ? Max : value;
    }
}
