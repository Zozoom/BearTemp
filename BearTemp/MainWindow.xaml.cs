using System.Diagnostics;
using System.Management;
using System.Security.Principal;
using System.Windows;
using System.Windows.Threading;
using LibreHardwareMonitor.Hardware;

namespace BearTemp
{
    public partial class MainWindow : Window
    {
        private float? _cpuMinTemp, _cpuMaxTemp, _gpuMinTemp, _gpuMaxTemp;
        private readonly Computer _computer;

        public MainWindow()
        {
            InitializeComponent();
            _computer = new Computer { IsCpuEnabled = true, IsGpuEnabled = true, IsMemoryEnabled = true };
            _computer.Open();

            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            timer.Tick += UpdateStats;
            timer.Start();
        }

        private void UpdateStats(object sender, EventArgs e)
        {
            float? coreAverageTemp = null, cpuPackageTemp = null, gpuAverageTemp = null, memoryInUse = null, memoryTotal = null, memoryFree = null, memorySpeed = null, cpuLoad = null, gpuLoad = null;
            var cpuTemps = new List<float>();
            var gpuTemps = new List<float>();

            foreach (var hardware in _computer.Hardware)
            {
                hardware.Update();
                if (hardware.HardwareType == HardwareType.Cpu)
                {
                    foreach (var sensor in hardware.Sensors)
                    {
                        if (sensor.SensorType == SensorType.Temperature && sensor.Value.HasValue)
                        {
                            if (sensor.Name.Contains("Core")) cpuTemps.Add(sensor.Value.Value);
                            if (sensor.Name == "CPU Package") cpuPackageTemp = sensor.Value.Value;
                        }
                        if (sensor.SensorType == SensorType.Load && sensor.Name.Contains("CPU Total") && sensor.Value.HasValue)
                            cpuLoad = sensor.Value.Value;
                    }
                    if (cpuTemps.Count > 0) coreAverageTemp = cpuTemps.Average();
                    if (cpuPackageTemp.HasValue)
                    {
                        if (!_cpuMinTemp.HasValue || cpuPackageTemp < _cpuMinTemp) _cpuMinTemp = cpuPackageTemp;
                        if (!_cpuMaxTemp.HasValue || cpuPackageTemp > _cpuMaxTemp) _cpuMaxTemp = cpuPackageTemp;
                    }
                }
                if (hardware.HardwareType == HardwareType.GpuNvidia || hardware.HardwareType == HardwareType.GpuAmd)
                {
                    foreach (var sensor in hardware.Sensors)
                    {
                        if (sensor.SensorType == SensorType.Temperature && sensor.Value.HasValue) gpuTemps.Add(sensor.Value.Value);
                        if (sensor.SensorType == SensorType.Load && sensor.Name.Contains("GPU Core") && sensor.Value.HasValue) gpuLoad = sensor.Value.Value;
                    }
                    if (gpuTemps.Count > 0) gpuAverageTemp = gpuTemps.Average();
                    if (gpuAverageTemp.HasValue)
                    {
                        if (!_gpuMinTemp.HasValue || gpuAverageTemp < _gpuMinTemp) _gpuMinTemp = gpuAverageTemp;
                        if (!_gpuMaxTemp.HasValue || gpuAverageTemp > _gpuMaxTemp) _gpuMaxTemp = gpuAverageTemp;
                    }
                }
            }

            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT TotalVisibleMemorySize, FreePhysicalMemory FROM Win32_OperatingSystem");
                foreach (var obj in searcher.Get())
                {
                    memoryTotal = Convert.ToSingle(obj["TotalVisibleMemorySize"]) / 1024;
                    memoryFree = Convert.ToSingle(obj["FreePhysicalMemory"]) / 1024;
                    memoryInUse = memoryTotal - memoryFree;
                }
            }
            catch { memoryInUse = memoryTotal = memoryFree = null; }

            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT Speed FROM Win32_PhysicalMemory");
                foreach (var obj in searcher.Get())
                {
                    memorySpeed = Convert.ToSingle(obj["Speed"]);
                    break;
                }
            }
            catch { memorySpeed = null; }

            CpuTempText.Text = cpuPackageTemp?.ToString("F1") + " °C" ?? "-- °C";
            CpuTempMinMaxText.Text = cpuPackageTemp.HasValue ? $"(Min: {_cpuMinTemp:F1} °C, Max: {_cpuMaxTemp:F1} °C)" : "(Min: -- °C, Max: -- °C)";
            CoreAverageText.Text = coreAverageTemp.HasValue ? $"Core Avg: {coreAverageTemp:F1} °C" : "Core Avg: -- °C";
            GpuTempText.Text = gpuAverageTemp?.ToString("F1") + " °C" ?? "-- °C";
            GpuTempMinMaxText.Text = gpuAverageTemp.HasValue ? $"(Min: {_gpuMinTemp:F1} °C, Max: {_gpuMaxTemp:F1} °C)" : "(Min: -- °C, Max: -- °C)";
            if (memoryTotal.HasValue && memoryInUse.HasValue && memoryFree.HasValue)
            {
                var memoryUsagePercentage = (memoryInUse.Value / memoryTotal.Value) * 100;
                MemoryLoadPercentageText.Text = $"{memoryUsagePercentage:F1} %";
                MemoryLoadDetailsText.Text = $"Total: {memoryTotal / 1024:F1} GB | Used: {memoryInUse / 1024:F1} GB | Free: {memoryFree / 1024:F1} GB | Speed: {memorySpeed:F0} MT/s";
            }
            else
            {
                MemoryLoadPercentageText.Text = "-- %";
                MemoryLoadDetailsText.Text = "Total: -- GB | Used: -- GB | Free: -- GB | Speed: -- MT/s";
            }
            CpuLoadText.Text = cpuLoad?.ToString("F1") + " %" ?? "-- %";
            GpuLoadText.Text = gpuLoad?.ToString("F1") + " %" ?? "-- %";
        }

        private void ResetValues_Click(object sender, RoutedEventArgs e)
        {
            CpuTempText.Text = GpuTempText.Text = "-- °C";
            CpuTempMinMaxText.Text = GpuTempMinMaxText.Text = "(Min: -- °C, Max: -- °C)";
            CoreAverageText.Text = "Core Avg: -- °C";
            MemoryLoadPercentageText.Text = "-- %";
            MemoryLoadDetailsText.Text = "Total: -- GB | Used: -- GB | Free: -- GB | Speed: -- MT/s";
            CpuLoadText.Text = GpuLoadText.Text = "-- %";
            _cpuMinTemp = _cpuMaxTemp = _gpuMinTemp = _gpuMaxTemp = null;
        }

        private void RunAsAdmin_Click(object sender, RoutedEventArgs e)
        {
            if (!new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = Process.GetCurrentProcess().MainModule.FileName,
                        UseShellExecute = true,
                        Verb = "runas"
                    });
                    Application.Current.Shutdown();
                }
                catch
                {
                    MessageBox.Show("Failed to restart as administrator.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            else
            {
                MessageBox.Show("Application is already running as administrator.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Application.Current.Shutdown();

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            _computer.Close();
        }
    }
}
