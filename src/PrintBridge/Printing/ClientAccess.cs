using Microsoft.Win32;
using System.Net;

namespace PrintBridge.Printing;

internal static class ClientAccess
{
    internal static string[]? Allowed() =>
        (string[]?)Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\PrintBridge\ClientAccess", "Allowed", null);

    internal static string[] Known() =>
        (string[]?)Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\PrintBridge\ClientAccess", "Known", null) ?? [];

    internal static bool IsLocal(IPAddress? remote, IPAddress? local)
    {
        if (remote == null) return false;
        if (remote.IsIPv4MappedToIPv6) remote = remote.MapToIPv4();
        if (local?.IsIPv4MappedToIPv6 == true) local = local.MapToIPv4();
        return IPAddress.IsLoopback(remote) || remote.Equals(local);
    }

    internal static bool Permits(IPAddress? remote, IPAddress? local)
    {
        if (remote == null) return false;
        if (IsLocal(remote, local)) return true;
        if (remote.IsIPv4MappedToIPv6) remote = remote.MapToIPv4();
        var allowed = Allowed();
        return allowed == null || allowed.Contains(remote.ToString());
    }
}
