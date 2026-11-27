using PrintBridge.Ipp;
using PrintBridge.Models;
using PrintBridge.Printing;
using PrintBridge.Settings;
using System.Buffers.Binary;

var settings = new SettingsManager(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()));
settings.Current.Printers.Add(new PrinterConfig { WindowsPrinterName = "A printer" });
settings.Current.Printers.Add(new PrinterConfig { WindowsPrinterName = "B" });
settings.Current.Printers.Add(new PrinterConfig { WindowsPrinterName = "Offline", Enabled = false });
var spool = new WindowsPrinterBridge();
var handler = new IppRequestHandler(settings, spool);
int Send(ushort op, string queue, int? job = null, bool data = false)
{
    var request = new IppResponseBuilder(2, 0, op, 1).BeginOperationAttributes();
    if (job.HasValue) request.AddInteger("job-id", job.Value);
    var bytes = request.Build();
    if (data) bytes = [..bytes, 1];
    return BinaryPrimitives.ReadUInt16BigEndian(handler.HandleRequest(bytes, queue).AsSpan(2));
}
void Check(bool ok, string name)
{
    if (!ok) throw new Exception(name);
    Console.WriteLine("PASS " + name);
}
Check(Send(2, "Missing", data:true) == 0x406, "unknown printer");
Check(Send(2, "Offline", data:true) == 0x406, "disabled printer");
Check(Send(5, "A_printer") == 0, "create on advertised queue");
var id = handler.GetAllJobs().Single().JobId;
Check(Send(8, "B", id) == 0x406, "cross-queue cancellation");
Check(Send(6, "B", id, true) == 0x406, "cross-queue submission");
Check(Send(6, "A_printer", id, true) == 0, "submit");
Check(Send(6, "A_printer", id, true) == 0x404, "duplicate submission");
Check(spool.Count == 1, "single spooler dispatch");
Check((int)PrintJobState.Aborted == 8 && (int)PrintJobState.Canceled == 7, "IPP job-state codes");

Check(handler.GetAllJobs().Single().FromNetwork, "IPP job marked for silent printing");
Check(!handler.SubmitDirectJob("B", "local", "text/plain", [1]).FromNetwork, "local job keeps host notifications");

// media-col / orientation / number-up must reach the spooler options
var mediaJob = new IppResponseBuilder(2, 0, IppEncoding.OpPrintJob, 1).BeginOperationAttributes()
    .BeginCollection("media-col")
      .AddMemberAttrName("media-size")
      .BeginCollectionValue()
        .AddMemberAttrName("x-dimension").AddMemberInteger(21590)
        .AddMemberAttrName("y-dimension").AddMemberInteger(27940)
      .EndCollection()
    .EndCollection()
    .AddEnum("orientation-requested", 4)
    .AddInteger("number-up", 4)
    .Build();
handler.HandleRequest([..mediaJob, 1], "B");
Check(spool.LastOptions is { MediaWidthHundredthsMm: 21590, MediaHeightHundredthsMm: 27940,
    Landscape: true, PagesPerSheet: 4 }, "media-col, orientation and number-up applied");

var mediaKwJob = new IppResponseBuilder(2, 0, IppEncoding.OpPrintJob, 1).BeginOperationAttributes()
    .AddKeyword("media", "iso_a4_210x297mm").Build();
handler.HandleRequest([..mediaKwJob, 1], "B");
Check(spool.LastOptions is { MediaWidthHundredthsMm: 21000, MediaHeightHundredthsMm: 29700 },
    "media keyword applied");
var sidesJob = new IppResponseBuilder(2, 0, IppEncoding.OpPrintJob, 1).BeginOperationAttributes()
    .AddKeyword("sides", "two-sided-short-edge").Build();
handler.HandleRequest([..sidesJob, 1], "B");
Check(spool.LastOptions is { Duplex: true, DuplexShortEdge: true }, "short-edge duplex applied");
var unsupported = new IppResponseBuilder(2, 0, 0x003C, 1).BeginOperationAttributes().Build();
Check(BinaryPrimitives.ReadUInt16BigEndian(handler.HandleRequest(unsupported, "B").AsSpan(2)) == IppEncoding.StatusOperationNotSupported,
    "unsupported operation reports operation-not-supported");

for (int i = 0; i < 230; i++)
    handler.SubmitDirectJob("B", $"bulk {i}", "application/pdf", [1, 2, 3]);
var history = handler.GetAllJobs();
Check(history.Count <= 201 && history.Count(j => j.DocumentData.Length > 0) <= 21, "job history bounded in memory");

// URF: 2x2 sRGB page. Row 0: red pixel then 0x80 (rest of line white).
// Row 1: two literal pixels blue, green.
byte[] urf =
[
    (byte)'U', (byte)'N', (byte)'I', (byte)'R', (byte)'A', (byte)'S', (byte)'T', 0, 0, 0, 0, 1,
    24, 1, 0, 4, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 2, 0, 0, 0, 2, 0, 0, 1, 44, 0, 0, 0, 0, 0, 0, 0, 0,
    0, 0x00, 255, 0, 0, 0x80,
    0, 0xFF, 0, 0, 255, 0, 255, 0
];
var urfReader = new UrfReader(urf);
var urfHeader = urfReader.PeekHeader();
var urfRows = new List<byte[]>();
var urfOk = urfReader.ReadPage((_, row) => urfRows.Add(row.ToArray()));
Check(urfHeader is { Width: 2, Height: 2, Dpi: 300 } && urfOk && urfReader.PeekHeader() == null, "URF header and page bounds");
Check(urfRows[0].SequenceEqual(new byte[] { 0, 0, 255, 255, 255, 255 }), "URF 0x80 blanks rest of line without consuming a pixel");
Check(urfRows[1].SequenceEqual(new byte[] { 255, 0, 0, 0, 255, 0 }), "URF RGB delivered as BGR for GDI+");

var profile = PrintBridge.Discovery.AirPrintAdvertiser.CreateProfile(
    new PrinterConfig { WindowsPrinterName = "Canon LBP 2900", DisplayName = "Máy in Canon" }, 631,
    [System.Net.IPAddress.Parse("192.0.2.22")]);
var srv = profile.Resources.OfType<Makaretu.Dns.SRVRecord>().Single();
var host = srv.Target.ToString();
Check(host.EndsWith(".local") && host.TrimEnd('.').All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '.')
    && profile.Resources.OfType<Makaretu.Dns.AddressRecord>().All(a => a.Name.Equals(srv.Target)),
    "Bonjour SRV host name is DNS-safe and matches A records");

var local = System.Net.IPAddress.Parse("192.0.2.22");
var neighbors = PrintBridge.Network.LanDiscovery.ProbeAddresses(local, 30).Select(ip => ip.ToString()).ToArray();
Check(neighbors.SequenceEqual(new[] { "192.0.2.21" }), "LAN probe stays in subnet and excludes self and broadcast");
Check(PrintBridge.Network.LanDiscovery.ProbeAddresses(local, 16).Count() == 253, "large subnet scan bounded");

namespace PrintBridge.Printing
{
    public sealed class WindowsPrinterBridge
    {
        public int Count { get; private set; }
        public PrintOptions? LastOptions { get; private set; }
        public uint GetQueueStatusBits(string printer) => 0;
        public PrintResult SendToSpooler(string printer, byte[] data, string name, string mime, PrintOptions? options, bool fromNetwork = false)
        {
            Count++;
            LastOptions = options;
            return PrintResult.Ok();
        }
    }
}
