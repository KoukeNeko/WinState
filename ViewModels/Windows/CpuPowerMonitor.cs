using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace WinState.ViewModels.Windows
{
    public static class CpuPowerMonitor
    {
        // Windows API 
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS systemPowerStatus);

        [StructLayout(LayoutKind.Sequential)]
        private struct SYSTEM_POWER_STATUS
        {
            public byte ACLineStatus;
            public byte BatteryFlag;
            public byte BatteryLifePercent;
            public byte SystemStatusFlag;
            public uint BatteryLifeTime;
            public uint BatteryFullLifeTime;
        }


        public static async Task<double> GetCpuPowerConsumption()
        {
            try
            {
                // 使用 WMI 查詢 MSAcpi_ThermalZoneTemperature 類
                using var searcher = new ManagementObjectSearcher(
                    "root\\WMI",
                    "SELECT * FROM MSAcpi_ThermalZoneTemperature");

                using var collection = searcher.Get();
                double totalPower = 0;
                int count = 0;

                foreach (ManagementObject obj in collection)
                {
                    // 獲取溫度資訊(以開爾文為單位)
                    var temperature = Convert.ToDouble(obj["CurrentTemperature"]) / 10 - 273.15; // 轉換為攝氏度

                    // 獲取 CPU 使用率
                    var cpuUsage = await GetCpuUsage();

                    // 根據溫度和使用率估算功耗
                    // 這是一個簡化的估算公式，實際功耗還與 CPU 型號、電壓等因素有關
                    var estimatedPower = (temperature * 0.5 + cpuUsage * 0.8) * GetPowerFactor();

                    totalPower += estimatedPower;
                    count++;
                }

                return count > 0 ? totalPower / count : 0;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error getting CPU power consumption: {ex.Message}");
                return -1;
            }
        }

        private static async Task<float> GetCpuUsage()
        {
            var startTime = DateTime.UtcNow;
            var startCpuUsage = Process.GetCurrentProcess().TotalProcessorTime;

            await Task.Delay(500); // 等待 500ms 以獲取更準確的使用率

            var endTime = DateTime.UtcNow;
            var endCpuUsage = Process.GetCurrentProcess().TotalProcessorTime;

            var cpuUsedMs = (endCpuUsage - startCpuUsage).TotalMilliseconds;
            var totalMsPassed = (endTime - startTime).TotalMilliseconds;
            var cpuUsageTotal = cpuUsedMs / (Environment.ProcessorCount * totalMsPassed);

            return (float)(cpuUsageTotal * 100);
        }

        /// <summary>
        /// 獲取功率係數(基於供電狀態)
        /// </summary>
        private static float GetPowerFactor()
        {
            try
            {
                GetSystemPowerStatus(out var powerStatus);

                // 根據供電狀態回傳不同的功率係數
                return powerStatus.ACLineStatus == 1 ? 1.2f : 0.8f;
            }
            catch
            {
                return 1.0f; // 預設係數
            }
        }
    }
}