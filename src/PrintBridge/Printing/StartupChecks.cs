using System.Diagnostics;
using System.Text;

namespace PrintBridge.Printing;

internal static class StartupChecks
{
    public static async Task<string> RunAsync(int port, string[] queues)
    {
        try
        {
            using var stream = typeof(StartupChecks).Assembly.GetManifestResourceStream("StartupChecks.ps1")
                ?? throw new InvalidOperationException("Thiếu chương trình kiểm tra cấu hình.");
            using var reader = new StreamReader(stream);
            var names = Convert.ToBase64String(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(queues));
            var script = "& {\n" + await reader.ReadToEndAsync() + "\n} " + port + " '" + names + "'";
            var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe"))
            {
                CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true,
                RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8
            };
            foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-EncodedCommand",
                Convert.ToBase64String(Encoding.Unicode.GetBytes(script)) })
                start.ArgumentList.Add(arg);
            using var process = Process.Start(start) ?? throw new InvalidOperationException("PowerShell không chạy.");
            var output = process.StandardOutput.ReadToEndAsync();
            var errors = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException)
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                return "[!] Kiểm tra vượt quá 30 giây. Kiểm tra spooler và chạy lại từ khay hệ thống.";
            }
            var report = await output;
            if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(report))
                report += "\n[!] Kiểm tra không hoàn tất. " + await errors;
            return report;
        }
        catch (Exception ex) { return "[!] Không thể kiểm tra cấu hình. " + ex.Message; }
    }
}
