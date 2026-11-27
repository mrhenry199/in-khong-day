using System.Diagnostics;
using PrintBridge.Printing;

namespace PrintBridge.UI;

internal sealed class FirstRunForm : Form
{
    private readonly TextBox _report = new()
    {
        Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill
    };
    private readonly FlowLayoutPanel _actions = new() { Dock = DockStyle.Fill, AutoSize = true };
    private readonly Func<Task<string>> _check;

    internal FirstRunForm(Func<Task<string>> check, Func<int> port, Action configurePrinters)
    {
        _check = check;
        Text = "Thiết lập In không dây";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(720, 430);
        MinimumSize = new Size(570, 370);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), RowCount = 3, ColumnCount = 1 };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(new Label { AutoSize = true, Text = $".NET {Environment.Version} đang chạy. Kiểm tra máy in và mạng LAN bên dưới." }, 0, 0);
        layout.Controls.Add(_report, 0, 1);
        layout.Controls.Add(_actions, 0, 2);
        Controls.Add(layout);
        AddButton("Máy in Windows", () =>
        {
            try { Process.Start(new ProcessStartInfo("ms-settings:printers") { UseShellExecute = true }); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message); }
        });
        AddButton("Chọn máy in", configurePrinters);
        AddButton("Cấu hình firewall", () =>
        {
            FirewallSetup.Show(port());
            _ = RefreshAsync();
        });
        AddButton("Kiểm tra lại", () => _ = RefreshAsync());
        AddButton("Hoàn tất", () => { DialogResult = DialogResult.OK; Close(); });
        Shown += async (_, _) => await RefreshAsync();
    }

    private void AddButton(string text, Action action)
    {
        var button = new Button { Text = text, AutoSize = true };
        button.Click += (_, _) => action();
        _actions.Controls.Add(button);
    }

    private async Task RefreshAsync()
    {
        _actions.Enabled = false;
        _report.Text = "Đang kiểm tra…";
        var result = await _check();
        if (IsDisposed) return;
        _report.Text = result.ReplaceLineEndings(Environment.NewLine);
        _actions.Enabled = true;
    }
}
