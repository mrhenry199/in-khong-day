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

namespace PrintBridge.Printing
{
    public sealed class WindowsPrinterBridge
    {
        public int Count { get; private set; }
        public PrintResult SendToSpooler(string printer, byte[] data, string name, string mime, PrintOptions? options)
        {
            Count++;
            return PrintResult.Ok();
        }
    }
}
