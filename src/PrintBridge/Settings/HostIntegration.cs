using Microsoft.Win32;
using PrintBridge.Printing;
using System.Diagnostics;

namespace PrintBridge.Settings;

/// <summary>
/// Everything PrintBridge changes on the host outside its own folder, kept in one
/// place so it can be removed again:
/// HKCU Run value, %ProgramData%\PrintBridge, HKLM\SOFTWARE\PrintBridge and the
/// firewall rules created by setup-firewall.ps1. Printers, drivers and spooler
/// settings are never modified.
/// </summary>
internal static class HostIntegration
{
    private const string RunKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValue = "PrintBridge";

    internal static void ApplyAutoStart(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, true);
            if (enable && Environment.ProcessPath is { } exe)
                key?.SetValue(RunValue, $"\"{exe}\"");
            else
                key?.DeleteValue(RunValue, false);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to update autostart: {ex.Message}");
        }
    }

    /// <summary>
    /// Removes firewall rules, approved devices, autostart and settings. The exe is
    /// left in place so the user can delete it. Returns false if the firewall part
    /// was cancelled or failed (it needs administrator approval).
    /// </summary>
    internal static bool Uninstall(SettingsManager settings)
    {
        var firewallRemoved = FirewallHelper.RemoveFirewallRules();
        ApplyAutoStart(false);

        try
        {
            var dir = Path.GetDirectoryName(settings.ConfigFilePath);
            if (dir != null && Path.GetFileName(dir).Equals("PrintBridge", StringComparison.OrdinalIgnoreCase)
                && Directory.Exists(dir))
                Directory.Delete(dir, true);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to delete settings: {ex.Message}");
        }

        ScheduleExtractionCleanup();
        return firewallRemoved;
    }

    /// <summary>
    /// The single-file build unpacks native libraries to %TEMP%\.net\PrintBridge.
    /// They are in use while we run, so delete them shortly after exit.
    /// </summary>
    private static void ScheduleExtractionCleanup()
    {
        var root = Environment.GetEnvironmentVariable("DOTNET_BUNDLE_EXTRACT_BASE_DIR") is { Length: > 0 } custom
            ? custom
            : Path.Combine(Path.GetTempPath(), ".net");
        var dir = Path.Combine(root, "PrintBridge");
        if (!Directory.Exists(dir)) return;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.SystemDirectory, "cmd.exe"),
                Arguments = $"/c ping -n 6 127.0.0.1 >nul & rmdir /s /q \"{dir}\"",
                CreateNoWindow = true,
                UseShellExecute = false
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to schedule cleanup: {ex.Message}");
        }
    }
}
