namespace WinState.Models
{
    /// <summary>
    /// Number of processes shown in each category's process list.
    /// </summary>
    public class ProcessListSettings
    {
        public const int Default = 15;

        public int Cpu { get; set; } = Default;
        public int Memory { get; set; } = Default;
        public int Network { get; set; } = Default;
        public int Disk { get; set; } = Default;

        public static ProcessListSettings CreateDefault() => new ProcessListSettings();
    }
}
