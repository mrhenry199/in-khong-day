using System.Net;
using PrintBridge.Network;
using PrintBridge.Printing;

namespace PrintBridge.UI;

internal static class FirewallSetup
{
    internal static void Show(int port)
    {
        using var form = new Form { Text = "Cấu hình firewall", ClientSize = new Size(565, 440),
            StartPosition = FormStartPosition.CenterScreen, FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false, MinimizeBox = false };
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown,
            WrapContents = false, Padding = new Padding(12) };
        var lan = new RadioButton { Text = "LAN Private/Domain (IPP và tự tìm máy in)", AutoSize = true };
        var single = new RadioButton { Text = "Public – cho phép một máy khách in bằng địa chỉ IPP", AutoSize = true, Checked = true };
        var host = new TextBox { Width = 200, Text = NetworkHelper.GetLocalIPv4Address() };
        var client = new TextBox { Width = 200, PlaceholderText = "IPv4 máy khách" };
        var scan = new Button { Text = "Quét máy trong LAN", AutoSize = true };
        var list = new ListBox { Width = 510, Height = 110 };
        var status = new Label { Width = 510, Height = 38, Text = "Quét rồi chọn máy khách hoặc nhập IPv4 bên dưới." };
        using var stop = new CancellationTokenSource();
        form.FormClosing += (_, _) => stop.Cancel();
        var apply = new Button { Text = "Chấp nhận", AutoSize = true };
        panel.Controls.AddRange([lan, single, new Label { Text = "IPv4 máy chủ", AutoSize = true }, host, scan, list, status,
            new Label { Text = "IPv4 máy khách được phép", AutoSize = true }, client, apply]);
        form.Controls.Add(panel);
        single.CheckedChanged += (_, _) => scan.Enabled = list.Enabled = host.Enabled = client.Enabled = single.Checked;
        host.TextChanged += (_, _) => { list.Items.Clear(); client.Clear(); };
        list.SelectedIndexChanged += (_, _) =>
        {
            if (list.SelectedItem is LanDiscovery.Device device) client.Text = device.Address.ToString();
        };
        scan.Click += async (_, _) =>
        {
            if (!ValidAddress(host.Text, out var local)) { status.Text = "IPv4 máy chủ chưa hợp lệ."; return; }
            scan.Enabled = apply.Enabled = host.Enabled = client.Enabled = lan.Enabled = single.Enabled = false;
            list.Items.Clear();
            status.Text = "Đang tìm thiết bị trên LAN…";
            try
            {
                var devices = await LanDiscovery.ScanAsync(local, stop.Token);
                if (stop.IsCancellationRequested) return;
                list.Items.AddRange(devices.ToArray());
                status.Text = $"Tìm thấy {devices.Count} thiết bị. Chọn đúng IPv4 máy khách; có thể nhập tay nếu máy chưa xuất hiện.";
            }
            catch (OperationCanceledException) { if (!stop.IsCancellationRequested) status.Text = "Quét hết thời gian. Có thể nhập IP thủ công."; }
            catch (Exception ex) { if (!stop.IsCancellationRequested) status.Text = ex.Message; }
            finally { if (!stop.IsCancellationRequested) scan.Enabled = apply.Enabled = host.Enabled = client.Enabled = lan.Enabled = single.Enabled = true; }
        };
        apply.Click += (_, _) =>
        {
            bool ok;
            if (single.Checked)
            {
                if (!ValidAddress(host.Text, out var local) || !ValidAddress(client.Text, out var remote) || local.Equals(remote))
                {
                    MessageBox.Show(form, "Nhập hai địa chỉ IPv4 khác nhau của máy chủ và máy khách.");
                    return;
                }
                ok = FirewallHelper.AllowClient(port, local, remote);
            }
            else ok = FirewallHelper.AddFirewallRules(port);
            MessageBox.Show(form, ok ? $"Đã lưu quy tắc cho TCP/{port}. Hãy thử từ máy khách."
                : "Chưa áp dụng được. Kiểm tra quyền quản trị, IP máy chủ và profile của card mạng.");
            if (ok) form.Close();
        };
        form.ShowDialog();
    }

    private static bool ValidAddress(string value, out IPAddress address)
    {
        var input = value.Trim();
        if (IPAddress.TryParse(input, out var ip) && ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
            && ip.ToString() == input && !IPAddress.IsLoopback(ip) && ip.GetAddressBytes()[0] is > 0 and < 224)
        {
            address = ip;
            return true;
        }
        address = IPAddress.None;
        return false;
    }
}
