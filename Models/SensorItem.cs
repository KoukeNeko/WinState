using System;

namespace WinState.Models
{
    // INPC-enabled so SystemInfoService can mutate items in-place each tick (via a per-sensor
    // pool) instead of allocating a fresh SensorItem on every refresh. With ~50 sensors and
    // multiple ticks/second while a window is open, the old allocation pattern was a sizeable
    // contributor to gen-0 GC pressure.
    public partial class SensorItem : ObservableObject
    {
        [ObservableProperty] private string _name = string.Empty;
        [ObservableProperty] private string _value = string.Empty;
        [ObservableProperty] private string _unit = string.Empty;
        [ObservableProperty] private string _category = string.Empty;
        [ObservableProperty] private string _sensorType = string.Empty;
        [ObservableProperty] private double _rawValue;
        [ObservableProperty] private double _min;
        [ObservableProperty] private double _max;
    }
}
