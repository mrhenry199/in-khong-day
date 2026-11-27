using Makaretu.Dns;
using PrintBridge.Models;
using PrintBridge.Network;
using System.Net;

namespace PrintBridge.Discovery;

/// <summary>
/// Broadcasts DNS-SD / mDNS announcements on the local Wi-Fi / LAN so that
/// Apple devices (AirPrint) and Android devices (Mopria / IPP Everywhere)
/// discover shared printers automatically.
/// </summary>
public sealed class AirPrintAdvertiser : IDisposable
{
    private MulticastService? _mdns;
    private ServiceDiscovery? _sd;
    private readonly List<ServiceProfile> _profiles = new();
    private readonly object _lock = new();
    private bool _disposed;

    /// <summary>
    /// Starts advertising all enabled printers.
    /// </summary>
    public void Start(IEnumerable<PrinterConfig> printers, int ippPort)
    {
        lock (_lock)
        {
            Stop();

            try
            {
                _mdns = new MulticastService();
                _sd = new ServiceDiscovery(_mdns);

                _mdns.QueryReceived += (_, query) => LanDiscovery.Observe(query.RemoteEndPoint.Address);
                _mdns.Start();
                var ipList = NetworkHelper.GetLocalIPv4Addresses()
                    .Where(ip => !IPAddress.IsLoopback(ip))
                    .ToList();

                foreach (var printer in printers.Where(p => p.Enabled))
                {
                    var profile = CreateProfile(printer, ippPort, ipList);
                    _profiles.Add(profile);
                    _sd.Advertise(profile);
                    _sd.Announce(profile, 2);
                }

            }
            catch (Exception ex)
            {
                Stop();
                throw new InvalidOperationException("Không khởi động được mDNS.", ex);
            }
        }
    }

    /// <summary>
    /// Stops advertising and unregisters services.
    /// </summary>
    public void Stop()
    {
        lock (_lock)
        {
            if (_sd != null)
            {
                foreach (var p in _profiles)
                {
                    try { _sd.Unadvertise(p); } catch { }
                }
            }
            _profiles.Clear();

            try { _sd?.Dispose(); } catch { }
            _sd = null;

            try { _mdns?.Stop(); } catch { }
            try { _mdns?.Dispose(); } catch { }
            _mdns = null;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }

    internal static ServiceProfile CreateProfile(PrinterConfig printer, int ippPort, IReadOnlyList<IPAddress> addresses)
    {
        var safeName = SanitizeServiceName(string.IsNullOrWhiteSpace(printer.DisplayName)
            ? printer.WindowsPrinterName
            : printer.DisplayName);

        // Service type _ipp._tcp is used by AirPrint, Mopria, and IPP Everywhere
        var profile = new ServiceProfile(safeName, "_ipp._tcp", (ushort)ippPort, addresses.Count > 0 ? addresses : null);

        // The library derives the SRV target from the instance name ("Canon LBP2900.ipp.local").
        // Spaces and accents are not valid in a host name, so clients that build
        // ipp://<host>/ from the SRV record cannot connect and show the printer as offline.
        var hostName = new DomainName($"{HostLabel()}.local");
        profile.HostName = hostName;
        foreach (var record in profile.Resources)
        {
            if (record is SRVRecord srv) srv.Target = hostName;
            else if (record is AddressRecord address) address.Name = hostName;
        }

        var safeQueue = PrinterIdentity.GetQueuePath(printer);

        profile.AddProperty("txtvers", "1");
        profile.AddProperty("qtotal", "1");
        profile.AddProperty("rp", $"printers/{safeQueue}");
        profile.AddProperty("ty", safeName);
        profile.AddProperty("note", string.IsNullOrEmpty(printer.Location) ? "Windows Wi-Fi Printer" : printer.Location);
        profile.AddProperty("product", $"({safeName})");
        profile.AddProperty("air", "none");
        profile.AddProperty("kind", "document,photo,envelope,label");
        profile.AddProperty("pdl", "application/pdf,image/jpeg,image/png,image/urf");

        var urfList = new List<string> { "W8", "CP1", "RS300-600" };
        if (printer.Color) urfList.Insert(1, "SRGB24");
        if (printer.Duplex) urfList.Add("DM1");
        profile.AddProperty("URF", string.Join(",", urfList));

        profile.AddProperty("Color", printer.Color ? "T" : "F");
        profile.AddProperty("Duplex", printer.Duplex ? "T" : "F");
        profile.AddProperty("Scan", "F");
        profile.AddProperty("Fax", "F");
        profile.AddProperty("usb_MFG", "PrintBridge");
        profile.AddProperty("usb_MDL", safeName);
        profile.AddProperty("priority", "10");
        profile.AddProperty("UUID", PrinterIdentity.GetUuid(printer.WindowsPrinterName));

        // Subtype _universal is required by Apple AirPrint
        profile.Subtypes.Add("_universal");
        return profile;
    }

    /// <summary>
    /// DNS-safe host label (letters, digits, hyphen). Prefixed so it never collides
    /// with the name Windows itself answers for over mDNS.
    /// </summary>
    internal static string HostLabel()
    {
        var label = new string($"PrintBridge-{Environment.MachineName}"
            .Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-').ToArray()).Trim('-');
        return label.Length > 63 ? label[..63].TrimEnd('-') : label;
    }

    private static string SanitizeServiceName(string name)
    {
        var clean = name.Replace('.', ' ').Replace('\\', ' ').Replace('/', ' ').Trim();
        if (clean.Length > 60) clean = clean[..60];
        return string.IsNullOrWhiteSpace(clean) ? "Windows Printer" : clean;
    }
}
