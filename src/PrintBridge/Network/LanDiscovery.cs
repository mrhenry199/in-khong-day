using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace PrintBridge.Network;

internal static class LanDiscovery
{
    internal sealed record Device(IPAddress Address, string Mac)
    {
        public override string ToString() => string.IsNullOrEmpty(Mac) ? Address.ToString() : $"{Address}    {Mac}";
    }

    internal static IEnumerable<IPAddress> ProbeAddresses(IPAddress local, int prefix)
    {
        // Bound discovery on large networks to the local /24. ARP can still list other on-link devices.
        var bits = Math.Max(prefix, 24);
        var host = BinaryPrimitives.ReadUInt32BigEndian(local.GetAddressBytes());
        uint mask = uint.MaxValue << (32 - bits);
        var network = host & mask;
        uint count = 1u << (32 - bits);
        for (uint n = 0; n < count; n++)
        {
            if (network + n == host || (bits <= 30 && (n == 0 || n == count - 1))) continue;
            var bytes = new byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(bytes, network + n);
            yield return new IPAddress(bytes);
        }
    }

    internal static async Task<List<Device>> ScanAsync(IPAddress local, CancellationToken token)
    {
        var adapter = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses)
            .FirstOrDefault(a => a.Address.Equals(local)) ?? throw new InvalidOperationException("IP máy chủ không thuộc card mạng đang kết nối.");
        var devices = new ConcurrentDictionary<IPAddress, string>();
        await Parallel.ForEachAsync(ProbeAddresses(local, adapter.PrefixLength),
            new ParallelOptions { MaxDegreeOfParallelism = 16, CancellationToken = token }, async (address, cancel) =>
            {
                using var ping = new Ping();
                try
                {
                    if ((await ping.SendPingAsync(address, 350).WaitAsync(cancel)).Status == IPStatus.Success)
                        devices.TryAdd(address, "");
                }
                catch (PingException) { }
            });
        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "arp.exe"))
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
        foreach (var arg in new[] { "-a", "-N", local.ToString() }) start.ArgumentList.Add(arg);
        using var arp = Process.Start(start) ?? throw new InvalidOperationException("Không đọc được bảng ARP.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        string rows;
        try
        {
            rows = await arp.StandardOutput.ReadToEndAsync(timeout.Token);
            await arp.WaitForExitAsync(timeout.Token);
        }
        finally { if (!arp.HasExited) arp.Kill(); }
        foreach (var row in rows.Split('\n'))
        {
            var fields = row.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length < 2 || !IPAddress.TryParse(fields[0], out var ip)
                || ip.AddressFamily != AddressFamily.InterNetwork || ip.Equals(local)
                || ip.GetAddressBytes()[0] is 0 or >= 224) continue;
            if (!PhysicalAddress.TryParse(fields[1].ToUpperInvariant(), out var mac)) continue;
            var octets = mac.GetAddressBytes();
            if (octets.Length == 6 && (octets[0] & 1) == 0 && octets.Any(b => b != 0))
                devices[ip] = fields[1];
        }
        return devices.OrderBy(p => BinaryPrimitives.ReadUInt32BigEndian(p.Key.GetAddressBytes()))
            .Select(p => new Device(p.Key, p.Value)).ToList();
    }
}
