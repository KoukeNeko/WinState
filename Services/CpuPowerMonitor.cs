using System;
using LibreHardwareMonitor.Hardware; // 確保已引用 LibreHardwareMonitor

namespace WinState.Services
{
    // 訪問者類，用於更新硬件狀態
    public class UpdateVisitor : IVisitor
    {
        public void VisitComputer(IComputer computer)
        {
            computer.Traverse(this);
        }

        public void VisitHardware(IHardware hardware)
        {
            hardware.Update();
            foreach (IHardware subHardware in hardware.SubHardware)
            {
                subHardware.Accept(this);
            }
        }

        public void VisitSensor(ISensor sensor) { }

        public void VisitParameter(IParameter parameter) { }
    }

    public static class CpuPowerMonitor
    {

        // Get CPU Power Usage
        public static double GetCpuPower()
        {
            Computer computer = new Computer { IsCpuEnabled = true };
            computer.Open();
            computer.IsCpuEnabled = true;
            computer.Accept(new UpdateVisitor());
            foreach (IHardware hardware in computer.Hardware)
            {
                if (hardware.HardwareType == HardwareType.Cpu)
                {
                    Console.WriteLine("CPU: {0}", hardware.Name);
                    foreach (ISensor sensor in hardware.Sensors)
                    {
                        if (sensor.SensorType == SensorType.Power)
                        {
                            Console.WriteLine("\tSensor: {0}, value: {1}", sensor.Name, sensor.Value);
                            if (sensor.Value == null)
                            {
                                return -1;
                            }
                            else
                            {
                                return (double)sensor.Value;
                            }
                        }
                    }
                }
            }
            computer.Close();
            return -1;
        }
    }
}
