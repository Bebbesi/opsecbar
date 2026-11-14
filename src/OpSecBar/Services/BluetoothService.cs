using OpSecBar.Core;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;
using Windows.Foundation;

namespace OpSecBar.Services;

/// <summary>Un dispositivo Bluetooth: già associato al PC, oppure trovato nelle vicinanze.</summary>
public sealed class BluetoothItem
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public bool Paired { get; init; }
    public bool Connected { get; init; }
    /// <summary>Ha un'uscita o un ingresso audio: lo si può collegare e scollegare a comando.</summary>
    public bool Audio { get; init; }
    public Guid? ContainerId { get; init; }
    public string Address { get; init; } = "";
    internal DeviceInformation? Native { get; init; }

    /// <summary>Chiave per riconoscere lo stesso dispositivo visto due volte (Bluetooth classico e Low Energy).</summary>
    public string Key => Address.Length > 0 ? Address : Id;
}

/// <summary>Richiesta all'utente durante un'associazione.</summary>
public enum PairingPromptKind { ShowPin, ConfirmPin, EnterPin }

public sealed record PairingPrompt(PairingPromptKind Kind, string DeviceName, string? Pin);

/// <summary>
/// Dispositivi Bluetooth: elenco di quelli associati, ricerca di quelli vicini, associazione e rimozione
/// (API Windows.Devices.Enumeration, le stesse delle Impostazioni di Windows).
/// </summary>
public static class BluetoothService
{
    private const string Classic = "{e0cbf06c-cd8b-4647-bb8a-263b43f0f974}", LowEnergy = "{bb7bb05e-5972-42b5-94fc-76eaa7084d49}";

    private static readonly string[] Properties =
    {
        "System.Devices.Aep.IsConnected", "System.Devices.Aep.IsPaired", "System.Devices.Aep.ContainerId", "System.Devices.Aep.DeviceAddress"
    };

    /// <summary>Dispositivi non associati nelle vicinanze: questa query avvia una ricerca radio (la usa lo Scanner, che resta attivo).</summary>
    private static readonly string NearbySelector =
        $"(System.Devices.Aep.ProtocolId:=\"{Classic}\" OR System.Devices.Aep.ProtocolId:=\"{LowEnergy}\") AND " +
        "System.Devices.Aep.IsPaired:=System.StructuredQueryType.Boolean#False";

    private static T? Property<T>(IReadOnlyDictionary<string, object> properties, string name) =>
        properties.TryGetValue(name, out object? value) && value is T typed ? typed : default;

    private static BluetoothItem? ToItem(DeviceInformation device, HashSet<Guid>? audio)
    {
        if (string.IsNullOrWhiteSpace(device.Name)) return null;
        Guid? container = device.Properties.TryGetValue("System.Devices.Aep.ContainerId", out object? c) && c is Guid g ? g : null;
        return new BluetoothItem
        {
            Id = device.Id, Name = device.Name,
            Paired = device.Pairing?.IsPaired == true,
            Connected = Property<bool>(device.Properties, "System.Devices.Aep.IsConnected"),
            Audio = container != null && audio?.Contains(container.Value) == true,
            ContainerId = container,
            Address = Property<string>(device.Properties, "System.Devices.Aep.DeviceAddress") ?? "",
            Native = device
        };
    }

    /// <summary>I dispositivi già associati, quelli collegati per primi.</summary>
    public static async Task<List<BluetoothItem>> PairedAsync()
    {
        var items = new Dictionary<string, BluetoothItem>(StringComparer.OrdinalIgnoreCase);
        try
        {
            Task<HashSet<Guid>> audioTask = Task.Run(AudioService.BluetoothAudioContainers);
            // I selettori di Windows per i dispositivi associati non avviano una ricerca radio: rispondono subito.
            // Una query AEP scritta a mano aspetterebbe la fine della ricerca (30 secondi).
            Task<DeviceInformationCollection>[] queries =
            {
                DeviceInformation.FindAllAsync(BluetoothDevice.GetDeviceSelectorFromPairingState(true), Properties, DeviceInformationKind.AssociationEndpoint).AsTask(),
                DeviceInformation.FindAllAsync(BluetoothLEDevice.GetDeviceSelectorFromPairingState(true), Properties, DeviceInformationKind.AssociationEndpoint).AsTask()
            };
            HashSet<Guid> audio = await audioTask;
            foreach (DeviceInformation device in (await Task.WhenAll(queries)).SelectMany(c => c))
            {
                if (device.Pairing?.IsPaired != true) continue;
                if (ToItem(device, audio) is not { } item) continue;
                // Classico + Low Energy dello stesso dispositivo: basta una riga, collegata se lo è almeno uno dei due.
                if (items.TryGetValue(item.Key, out BluetoothItem? seen) && (seen.Connected || !item.Connected)) continue;
                items[item.Key] = item;
            }
        }
        catch (Exception ex) { Log.Warn("Dispositivi Bluetooth associati: " + ex.Message); }
        return items.Values.OrderByDescending(i => i.Connected).ThenBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>Collega o scollega l'audio di un dispositivo associato.</summary>
    public static Task<bool> SetAudioConnectedAsync(BluetoothItem item, bool connect) =>
        item.ContainerId is { } container ? Task.Run(() => AudioService.SetBluetoothAudioConnected(container, connect)) : Task.FromResult(false);

    /// <summary>
    /// Associa un dispositivo trovato nelle vicinanze. <paramref name="prompt"/> viene chiamato (da un thread qualsiasi)
    /// quando serve l'utente: restituisce null per annullare, altrimenti il PIN digitato (o una stringa qualsiasi per confermare).
    /// </summary>
    public static async Task<(bool Ok, string? Error)> PairAsync(BluetoothItem item, Func<PairingPrompt, Task<string?>> prompt)
    {
        if (item.Native == null) return (false, "Device not available");
        DeviceInformationCustomPairing pairing = item.Native.Pairing.Custom;

        async void OnRequested(DeviceInformationCustomPairing sender, DevicePairingRequestedEventArgs e)
        {
            Deferral deferral = e.GetDeferral();
            try
            {
                switch (e.PairingKind)
                {
                    case DevicePairingKinds.ConfirmOnly:
                        e.Accept();
                        break;
                    case DevicePairingKinds.DisplayPin:
                        e.Accept();
                        _ = prompt(new PairingPrompt(PairingPromptKind.ShowPin, item.Name, e.Pin));
                        break;
                    case DevicePairingKinds.ConfirmPinMatch:
                        if (await prompt(new PairingPrompt(PairingPromptKind.ConfirmPin, item.Name, e.Pin)) != null) e.Accept();
                        break;
                    case DevicePairingKinds.ProvidePin:
                        string? pin = await prompt(new PairingPrompt(PairingPromptKind.EnterPin, item.Name, null));
                        if (!string.IsNullOrWhiteSpace(pin)) e.Accept(pin.Trim());
                        break;
                }
            }
            catch (Exception ex) { Log.Warn("Associazione Bluetooth: " + ex.Message); }
            finally { deferral.Complete(); }
        }

        pairing.PairingRequested += OnRequested;
        try
        {
            DevicePairingResult result = await pairing.PairAsync(
                DevicePairingKinds.ConfirmOnly | DevicePairingKinds.DisplayPin | DevicePairingKinds.ConfirmPinMatch | DevicePairingKinds.ProvidePin);
            return result.Status is DevicePairingResultStatus.Paired or DevicePairingResultStatus.AlreadyPaired
                ? (true, null)
                : (false, PairingError(result.Status));
        }
        catch (Exception ex)
        {
            Log.Warn($"Associazione di {item.Name}: {ex.Message}");
            return (false, "Pairing failed");
        }
        finally { pairing.PairingRequested -= OnRequested; }
    }

    private static string PairingError(DevicePairingResultStatus status) => status switch
    {
        DevicePairingResultStatus.NotReadyToPair => "The device isn't in pairing mode",
        DevicePairingResultStatus.ConnectionRejected or DevicePairingResultStatus.RejectedByHandler => "Pairing was cancelled",
        DevicePairingResultStatus.AuthenticationTimeout or DevicePairingResultStatus.AuthenticationFailure => "The PIN wasn't confirmed in time",
        DevicePairingResultStatus.TooManyConnections => "Too many devices are connected",
        DevicePairingResultStatus.HardwareFailure or DevicePairingResultStatus.Failed => "Couldn't reach the device",
        DevicePairingResultStatus.AccessDenied => "Windows blocked the pairing",
        _ => "Pairing failed"
    };

    /// <summary>Rimuove un dispositivo associato (andrà associato di nuovo per riusarlo).</summary>
    public static async Task<bool> RemoveAsync(BluetoothItem item)
    {
        try
        {
            if (item.Native == null) return false;
            DeviceUnpairingResult result = await item.Native.Pairing.UnpairAsync();
            return result.Status is DeviceUnpairingResultStatus.Unpaired or DeviceUnpairingResultStatus.AlreadyUnpaired;
        }
        catch (Exception ex)
        {
            Log.Warn($"Rimozione di {item.Name}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Ricerca dei dispositivi vicini non ancora associati. Resta attiva finché non viene chiusa; <see cref="Changed"/>
    /// arriva da un thread qualsiasi.
    /// </summary>
    public sealed class Scanner : IDisposable
    {
        private readonly DeviceWatcher _watcher;
        private readonly Dictionary<string, DeviceInformation> _found = new();

        public event Action? Changed;

        public Scanner()
        {
            _watcher = DeviceInformation.CreateWatcher(NearbySelector, Properties, DeviceInformationKind.AssociationEndpoint);
            _watcher.Added += OnAdded;
            _watcher.Updated += OnUpdated;
            _watcher.Removed += OnRemoved;
            _watcher.Start();
        }

        public List<BluetoothItem> Found()
        {
            lock (_found)
            {
                return _found.Values.Select(d => ToItem(d, null)).OfType<BluetoothItem>()
                    .GroupBy(i => i.Key, StringComparer.OrdinalIgnoreCase).Select(g => g.First())
                    .OrderBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
            }
        }

        private void OnAdded(DeviceWatcher sender, DeviceInformation device)
        {
            lock (_found) _found[device.Id] = device;
            Changed?.Invoke();
        }

        private void OnUpdated(DeviceWatcher sender, DeviceInformationUpdate update)
        {
            lock (_found)
            {
                if (!_found.TryGetValue(update.Id, out DeviceInformation? device)) return;
                device.Update(update);
            }
            Changed?.Invoke();
        }

        private void OnRemoved(DeviceWatcher sender, DeviceInformationUpdate update)
        {
            lock (_found)
            {
                if (!_found.Remove(update.Id)) return;
            }
            Changed?.Invoke();
        }

        public void Dispose()
        {
            _watcher.Added -= OnAdded;
            _watcher.Updated -= OnUpdated;
            _watcher.Removed -= OnRemoved;
            try
            {
                if (_watcher.Status is DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted) _watcher.Stop();
            }
            catch { /* già fermo */ }
        }
    }
}
