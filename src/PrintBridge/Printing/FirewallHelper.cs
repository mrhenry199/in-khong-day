using System.Diagnostics;
using System.Text;

namespace PrintBridge.Printing;

public static class FirewallHelper
{
    public static bool AddFirewallRules(int ippPort = 631) =>
        ippPort is >= 1 and <= 65535 && Run("-IppPort " + ippPort);

    public static void RemoveFirewallRules() => Run("-Remove");

    private static bool Run(string arguments)
    {
        try
        {
            using var stream = typeof(FirewallHelper).Assembly.GetManifestResourceStream("Firewall.ps1")!;
            using var reader = new StreamReader(stream);
            var command = "& {\n" + reader.ReadToEnd() + "\n} " + arguments;
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
