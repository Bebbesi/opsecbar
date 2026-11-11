using System.Runtime.InteropServices;
using System.Windows.Threading;
using OpSecBar.Core;

namespace OpSecBar.Services;

/// <summary>CPU, RAM e batteria. Il timer gira solo quando il widget è attivo.</summary>
public sealed class SystemInfoService : ObservableObject
{
    private readonly DispatcherTimer _timer;
    private long _lastIdle, _lastTotal;
    private int _cpu, _ram, _battery = -1;
    private bool _charging;

    public int Cpu { get => _cpu; private set => Set(ref _cpu, value); }
    public int Ram { get => _ram; private set => Set(ref _ram, value); }
    /// <summary>Percentuale batteria, -1 se il PC non ha batteria.</summary>
    public int Battery { get => _battery; private set => Set(ref _battery, value); }
    public bool Charging { get => _charging; private set => Set(ref _charging, value); }

    public event Action? Updated;

    public SystemInfoService()
    {
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(2) };
        _timer.Tick += (_, _) => Sample();
    }

    public void SetEnabled(bool enabled)
    {
        if (enabled == _timer.IsEnabled) return;
        if (enabled)
        {
            Sample();
            _timer.Start();
        }
        else _timer.Stop();
    }

    private void Sample()
    {
        try
        {
            if (Native.GetSystemTimes(out long idle, out long kernel, out long user))
            {
                long total = kernel + user;
                long dTotal = total - _lastTotal, dIdle = idle - _lastIdle;
                if (_lastTotal != 0 && dTotal > 0) Cpu = (int)Math.Clamp(Math.Round(100.0 * (dTotal - dIdle) / dTotal), 0, 100);
                _lastTotal = total;
                _lastIdle = idle;
            }

            var mem = new Native.MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<Native.MEMORYSTATUSEX>() };
            if (Native.GlobalMemoryStatusEx(ref mem)) Ram = (int)mem.dwMemoryLoad;

            if (Native.GetSystemPowerStatus(out var power))
            {
                bool hasBattery = (power.BatteryFlag & 128) == 0 && power.BatteryLifePercent <= 100;
                Battery = hasBattery ? power.BatteryLifePercent : -1;
                Charging = power.ACLineStatus == 1;
            }
            Updated?.Invoke();
        }
        catch (Exception ex) { Log.Warn("SystemInfo: " + ex.Message); }
    }
}
