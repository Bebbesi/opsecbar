using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using OpSecBar.Core;

namespace OpSecBar.Services;

/// <summary>
/// CPU, RAM, batteria e (su richiesta) velocità della rete. Il timer gira solo quando un widget ne ha bisogno, e la
/// rete si misura solo se c'è un widget che la mostra.
/// </summary>
public sealed class SystemInfoService : ObservableObject
{
    private readonly DispatcherTimer _timer;
    private long _lastIdle, _lastTotal;
    private int _cpu, _ram, _battery = -1;
    private bool _charging, _network;
    private ulong _ramTotal, _ramUsed;
    private double _download, _upload;

    // Rete: contatori dell'ultimo campione e schede che li hanno prodotti (se cambiano, il confronto non vale).
    private long _lastReceived = -1, _lastSent, _lastNetworkStamp;
    private string _lastAdapters = "";

    public int Cpu { get => _cpu; private set => Set(ref _cpu, value); }
    public int Ram { get => _ram; private set => Set(ref _ram, value); }
    /// <summary>Memoria fisica totale e in uso, in byte.</summary>
    public ulong RamTotal { get => _ramTotal; private set => Set(ref _ramTotal, value); }
    public ulong RamUsed { get => _ramUsed; private set => Set(ref _ramUsed, value); }
    /// <summary>Percentuale batteria, -1 se il PC non ha batteria.</summary>
    public int Battery { get => _battery; private set => Set(ref _battery, value); }
    public bool Charging { get => _charging; private set => Set(ref _charging, value); }
    /// <summary>Velocità di download e upload in byte al secondo (0 finché la rete non è misurata).</summary>
    public double DownloadSpeed { get => _download; private set => Set(ref _download, value); }
    public double UploadSpeed { get => _upload; private set => Set(ref _upload, value); }

    public event Action? Updated;

    public SystemInfoService()
    {
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(2) };
        _timer.Tick += (_, _) => Sample();
    }

    /// <param name="enabled">Campionare CPU, memoria e batteria.</param>
    /// <param name="network">Misurare anche la velocità della rete (vale solo se <paramref name="enabled"/>).</param>
    public void SetEnabled(bool enabled, bool network = false)
    {
        network &= enabled;
        bool networkChanged = network != _network;
        _network = network;
        if (!network)
        {
            _lastReceived = -1;
            DownloadSpeed = UploadSpeed = 0;
        }
        if (enabled == _timer.IsEnabled)
        {
            if (enabled && networkChanged) Sample();
            return;
        }
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
            var before = (_cpu, _ram, _battery, _charging, _download, _upload);
            if (Native.GetSystemTimes(out long idle, out long kernel, out long user))
            {
                long total = kernel + user;
                long dTotal = total - _lastTotal, dIdle = idle - _lastIdle;
                if (_lastTotal != 0 && dTotal > 0) Cpu = (int)Math.Clamp(Math.Round(100.0 * (dTotal - dIdle) / dTotal), 0, 100);
                _lastTotal = total;
                _lastIdle = idle;
            }

            var mem = new Native.MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<Native.MEMORYSTATUSEX>() };
            if (Native.GlobalMemoryStatusEx(ref mem))
            {
                Ram = (int)mem.dwMemoryLoad;
                RamTotal = mem.ullTotalPhys;
                RamUsed = mem.ullTotalPhys - Math.Min(mem.ullAvailPhys, mem.ullTotalPhys);
            }

            if (Native.GetSystemPowerStatus(out var power))
            {
                bool hasBattery = (power.BatteryFlag & 128) == 0 && power.BatteryLifePercent <= 100;
                Battery = hasBattery ? power.BatteryLifePercent : -1;
                Charging = power.ACLineStatus == 1;
            }

            if (_network) SampleNetwork();
            if (before != (_cpu, _ram, _battery, _charging, _download, _upload)) Updated?.Invoke();
        }
        catch (Exception ex) { Log.Warn("SystemInfo: " + ex.Message); }
    }

    /// <summary>Byte ricevuti e inviati da tutte le schede attive (esclusi loopback e tunnel), divisi per il tempo trascorso.</summary>
    private void SampleNetwork()
    {
        long received = 0, sent = 0;
        var adapters = new System.Text.StringBuilder();
        foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up ||
                nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
            try
            {
                IPInterfaceStatistics stats = nic.GetIPStatistics();
                received += stats.BytesReceived;
                sent += stats.BytesSent;
                adapters.Append(nic.Id).Append('|');
            }
            catch (NetworkInformationException) { } // scheda appena sparita
        }

        long now = Stopwatch.GetTimestamp();
        string ids = adapters.ToString();
        // Una scheda che si accende o si spegne sposta i totali: per un campione non si calcola nulla.
        if (_lastReceived >= 0 && ids == _lastAdapters)
        {
            double seconds = (now - _lastNetworkStamp) / (double)Stopwatch.Frequency;
            if (seconds > 0.2)
            {
                DownloadSpeed = Math.Max(0, (received - _lastReceived) / seconds);
                UploadSpeed = Math.Max(0, (sent - _lastSent) / seconds);
            }
        }
        _lastReceived = received;
        _lastSent = sent;
        _lastNetworkStamp = now;
        _lastAdapters = ids;
    }
}
