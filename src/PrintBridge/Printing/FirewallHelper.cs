using System.Diagnostics;
using System.Text;

namespace PrintBridge.Printing;

public static class FirewallHelper
{
    public static bool AddFirewallRules(int ippPort = 631, bool shareAll = false) =>
        ippPort is >= 1 and <= 65535 && Run("-IppPort " + ippPort + AppArgument + (shareAll ? " -ShareAll" : ""));

    // Lets the script clear Windows' own block rules for this exe and remove its rules on uninstall.
    private static string AppArgument => Environment.ProcessPath is { } path
        ? $" -AppBase64 '{Convert.ToBase64String(Encoding.UTF8.GetBytes(path))}'"
        : "";

    internal static bool AllowClient(int port, System.Net.IPAddress local, System.Net.IPAddress remote, bool enableMdns, bool revoke = false)
    {
        if (port is < 1 or > 65535 || Environment.ProcessPath is not { } path) return false;
        var executable = Convert.ToBase64String(Encoding.UTF8.GetBytes(path));
        return Run($"-IppPort {port} -LocalIPv4 '{local}' -ClientIPv4 '{remote}' -AppBase64 '{executable}' " + (enableMdns ? "-EnableMdns " : "") + (revoke ? "-Revoke" : ""));
    }

    internal static bool EnableDiscovery(int port, System.Net.IPAddress local)
    {
        var exe = Convert.ToBase64String(Encoding.UTF8.GetBytes(Environment.ProcessPath!));
        return Run($"-IppPort {port} -LocalIPv4 '{local}' -AppBase64 '{exe}' -DiscoveryOnly -EnableMdns");
    }

    public static bool RemoveFirewallRules() => Run("-Remove" + AppArgument);

    private static bool Run(string arguments)
    {
        try
        {
            using var stream = typeof(FirewallHelper).Assembly.GetManifestResourceStream("Firewall.ps1")!;
            using var reader = new StreamReader(stream);
            var command = "try { & {\n" + reader.ReadToEnd() + "\n} " + arguments +
                "; exit 0 } catch { Add-Type -AssemblyName System.Windows.Forms; " +
                "[void][System.Windows.Forms.MessageBox]::Show($_.Exception.Message, 'PrintBridge - Windows Firewall'); exit 1 }";
            var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(command));
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe"),
                Arguments = "-NoProfile -NonInteractive -EncodedCommand " + encoded,
                Verb = "runas", UseShellExecute = true, WindowStyle = ProcessWindowStyle.Hidden
            });
            if (process == null) return false;
            process.WaitForExit();
            return process.ExitCode == 0;
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
            return false;
        }
    }
}
