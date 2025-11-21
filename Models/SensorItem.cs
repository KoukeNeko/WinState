using System;

namespace WinState.Models
{
    public class SensorItem
    {
        public string Name { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
        public string Unit { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string SensorType { get; set; } = string.Empty;
        public double RawValue { get; set; }
        public double Min { get; set; }
        public double Max { get; set; }
    }
}
