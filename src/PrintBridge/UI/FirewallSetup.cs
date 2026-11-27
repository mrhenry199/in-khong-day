using System.Net;
using PrintBridge.Network;
using PrintBridge.Printing;

namespace PrintBridge.UI;

internal static class FirewallSetup
{
    internal static void Show(int port)
    {
        using var form = new Form { Text = "Cấu hình firewall", ClientSize = new Size(490, 240),
            StartPosition = FormStartPosition.CenterScreen, FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false, MinimizeBox = false };
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown,
            WrapContents = false, Padding = new Padding(12) };
        var lan = new RadioButton { Text = "LAN Private/Domain (IPP và tự tìm máy in)", AutoSize = true, Checked = true };
        var single = new RadioButton { Text = "Public – cho phép một máy khách in bằng địa chỉ IPP", AutoSize = true };
        var host = new TextBox { Width = 200, Text = NetworkHelper.GetLocalIPv4Address(), Enabled = false };
        var client = new TextBox { Width = 200, PlaceholderText = "IPv4 máy khách", Enabled = false };
        var apply = new Button { Text = "Áp dụng", AutoSize = true };
        panel.Controls.AddRange([lan, single, new Label { Text = "IPv4 máy chủ", AutoSize = true }, host,
            new Label { Text = "IPv4 máy khách được phép", AutoSize = true }, client, apply]);
        form.Controls.Add(panel);
        single.CheckedChanged += (_, _) => host.Enabled = client.Enabled = single.Checked;
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
