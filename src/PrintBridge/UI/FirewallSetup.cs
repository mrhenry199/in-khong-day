using System.Net;
using PrintBridge.Network;
using PrintBridge.Printing;

namespace PrintBridge.UI;

internal static class FirewallSetup
{
    internal static void Show(int port)
    {
        using var form = new Form { Text = "Thiết bị được phép in", ClientSize = new Size(610, 530),
            StartPosition = FormStartPosition.CenterScreen, AutoScroll = true };
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown,
            WrapContents = false, AutoScroll = true, Padding = new Padding(12) };
        var host = new TextBox { Width = 220, Text = NetworkHelper.GetLocalIPv4Address() };
        var discover = new Button { Text = "Bật tìm máy in qua Bonjour", AutoSize = true };
        var scan = new Button { Text = "Quét thiết bị LAN", AutoSize = true };
        var list = new ListBox { Width = 550, Height = 160 };
        var client = new TextBox { Width = 220, PlaceholderText = "IPv4 máy khách" };
        var allow = new Button { Text = "Cho phép in", AutoSize = true };
        var revoke = new Button { Text = "Gỡ quyền in", AutoSize = true };
        var all = new Button { Text = "Chia sẻ cả LAN Private/Domain…", AutoSize = true };
        var buttons = new FlowLayoutPanel { AutoSize = true };
        buttons.Controls.AddRange([allow, revoke, all]);
        var status = new Label { Width = 550, Height = 54 };
        panel.Controls.AddRange([new Label { Text = "IPv4 máy chủ", AutoSize = true }, host, discover, scan, list,
            new Label { Text = "Chọn thiết bị hoặc nhập IPv4. Khi dùng danh sách, chỉ IP được duyệt mới được in.", AutoSize = true },
            client, buttons, status]);
        form.Controls.Add(panel);
        var seen = new Dictionary<string, string>();
        using var stop = new CancellationTokenSource();
        form.FormClosing += (_, _) => stop.Cancel();

        // Legacy versions only saved the grants in Windows Firewall.
        var legacy = new List<string>();
        if (ClientAccess.Allowed() == null)
        {
            try
            {
                var type = Type.GetTypeFromProgID("HNetCfg.FwPolicy2", throwOnError: true)!;
                dynamic policy = Activator.CreateInstance(type)!;
                foreach (dynamic rule in policy.Rules)
                    if (rule.Enabled && (int)rule.Action == 1 && (string)rule.Grouping == "PrintBridge Public IPP")
                        legacy.Add((string)rule.Name);
            }
            catch (System.Runtime.InteropServices.COMException) { }
        }

        void RefreshList()
        {
            list.Items.Clear();
            var allowed = ClientAccess.Allowed();
            var known = ClientAccess.Known();
            var prefix = $"PrintBridge.Public.{host.Text.Trim()}.";
            var previous = legacy.Where(name => name.StartsWith(prefix, StringComparison.Ordinal))
                .Select(name => name[prefix.Length..]).Where(ip => ValidAddress(ip, out _)).ToHashSet();
            var denied = ClientAccess.Denied();
            foreach (var ip in seen.Keys.Union(allowed ?? []).Union(known).Union(previous).Union(denied).Order())
            {
                string permission = allowed == null ? (previous.Contains(ip) ? "Đã có quy tắc cho phép" : "Theo firewall hiện tại") :
                    allowed.Contains(ip) ? "Được phép in" : "Chưa được phép in";
                list.Items.Add($"{ip}    {permission}    {seen.GetValueOrDefault(ip)}");
            }
        }

        list.SelectedIndexChanged += (_, _) =>
        {
            if (list.SelectedItem is string row) client.Text = row.Split(' ')[0];
        };
        host.TextChanged += (_, _) => { seen.Clear(); client.Clear(); RefreshList(); };
        scan.Click += async (_, _) =>
        {
            if (!ValidAddress(host.Text, out var local)) { status.Text = "IPv4 máy chủ chưa hợp lệ."; return; }
            scan.Enabled = host.Enabled = false;
            status.Text = "Đang tìm thiết bị…";
            try
            {
                var devices = await LanDiscovery.ScanAsync(local, stop.Token);
                if (stop.IsCancellationRequested) return;
                seen.Clear();
                foreach (var d in devices) seen[d.Address.ToString()] = d.Mac;
                RefreshList();
                status.Text = $"Thấy {devices.Count} thiết bị. Nếu thiếu iPhone, mở In → Chọn máy in rồi quét lại, hoặc nhập IPv4 từ cài đặt Wi-Fi của iPhone.";
            }
            catch (Exception ex) { if (!stop.IsCancellationRequested) status.Text = ex.Message; }
            finally { if (!stop.IsCancellationRequested) scan.Enabled = host.Enabled = true; }
        };
        discover.Click += (_, _) =>
        {
            if (!ValidAddress(host.Text, out var local)) { status.Text = "IPv4 máy chủ chưa hợp lệ."; return; }
            status.Text = FirewallHelper.EnableDiscovery(port, local)
                ? "Đã bật Bonjour. Trên iPhone mở In → Chọn máy in, rồi bấm Quét thiết bị LAN."
                : "Chưa bật được Bonjour. Kiểm tra quyền quản trị và IPv4 máy chủ.";
        };
        void SetPermission(bool remove)
        {
            if (!ValidAddress(host.Text, out var local) || !ValidAddress(client.Text, out var remote) || local.Equals(remote))
            {
                status.Text = "Nhập hai địa chỉ IPv4 khác nhau của máy chủ và máy khách.";
                return;
            }
            var ok = FirewallHelper.AllowClient(port, local, remote, !remove, remove);
            RefreshList();
            status.Text = ok ? $"Đã {(remove ? "gỡ quyền" : "cho phép")} in cho {remote}." : "Chưa áp dụng xong. Kiểm tra thông báo firewall.";
        }
        allow.Click += (_, _) => SetPermission(false);
        revoke.Click += (_, _) => SetPermission(true);
        all.Click += (_, _) =>
        {
            if (MessageBox.Show(form, "Tắt giới hạn danh sách IP của ứng dụng và cho phép toàn mạng Private/Domain in?", "Chia sẻ cả LAN",
                MessageBoxButtons.YesNo) != DialogResult.Yes) return;
            status.Text = FirewallHelper.AddFirewallRules(port, shareAll: true) ? "Đã chuyển sang quyền theo firewall." : "Chưa áp dụng được.";
            RefreshList();
        };
        RefreshList();
        form.Shown += (_, _) => scan.PerformClick();
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
