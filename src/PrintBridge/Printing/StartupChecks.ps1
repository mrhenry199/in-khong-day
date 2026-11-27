param([int]$IppPort, [string]$SelectedQueues, [int]$BridgePid)
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)

try {
    $service = Get-Service Spooler
    if ($service.Status -ne 'Running') { "[!] Print Spooler: $($service.Status). Mở services.msc và khởi động Print Spooler." }
    else { "[OK] Print Spooler" }
    $printers = @(Get-Printer)
    if (!$printers.Count) { '[!] Windows chưa có hàng đợi. Kết nối máy in và cài driver của hãng.' }
    $selected = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($SelectedQueues)) | ConvertFrom-Json
    foreach ($name in $selected) {
        if ($name -notin $printers.Name) { "[!] $name đang được quảng bá nhưng hàng đợi đã mất. Kiểm tra Settings." }
    }
    $drivers = $null
    try { $drivers = @(Get-PrinterDriver -ErrorAction Stop | Select-Object -ExpandProperty Name) }
    catch { "[!] Không đọc được danh sách driver đã đăng ký: $($_.Exception.Message)" }
    foreach ($printer in $printers) {
        if ($null -ne $drivers) {
            if ($printer.DriverName -and $drivers -contains $printer.DriverName) {
                "[OK] $($printer.Name): Windows đã đăng ký driver $($printer.DriverName)."
            } else {
                "[!] $($printer.Name): chưa đối chiếu được driver '$($printer.DriverName)' trong danh sách trên máy chủ. Hãy in trang thử tại Windows."
            }
        }
        if ($printer.PrinterStatus -notin @('Normal','Printing','Processing','WarmingUp','PowerSave')) {
            "[!] $($printer.Name): Windows báo $($printer.PrinterStatus). Kiểm tra hàng đợi và kết nối máy in."
        }
    }
} catch { "[!] Không đọc được trạng thái in: $($_.Exception.Message)" }

try {
    Get-PnpDevice -PresentOnly | Where-Object { ($_.Class -in @('Printer','PrintQueue') -or $_.InstanceId -like 'USBPRINT\*') -and $_.Status -ne 'OK' } |
        ForEach-Object { "[!] $($_.FriendlyName): $($_.Status). Kiểm tra driver trong Device Manager." }
} catch { "[!] Không đọc được danh sách thiết bị PnP: $($_.Exception.Message)" }

try {
    $profiles = @(Get-NetConnectionProfile -ErrorAction Stop)
    $trusted = @($profiles | Where-Object NetworkCategory -in @('Private','DomainAuthenticated'))
    $listen = @(Get-NetTCPConnection -State Listen -OwningProcess $BridgePid -ErrorAction SilentlyContinue |
        Where-Object { $_.LocalPort -eq $IppPort -and $_.LocalAddress -notin @('127.0.0.1','::1') })
    if ($listen.Count) { "[OK] PrintBridge đang lắng nghe TCP/$IppPort." }
    else { "[!] Chưa xác minh được PrintBridge lắng nghe TCP/$IppPort. Kiểm tra lỗi khởi động hoặc cổng bị chiếm." }
    $exe = (Get-Process -Id $BridgePid).Path
    foreach ($network in @($profiles | Where-Object NetworkCategory -eq 'Public')) {
        $local = @(Get-NetIPAddress -InterfaceIndex $network.InterfaceIndex -AddressFamily IPv4 | Select-Object -ExpandProperty IPAddress)
        $found = $false
        $policy = Get-NetFirewallProfile -Name Public -PolicyStore ActiveStore -ErrorAction Stop
        if ($policy.Enabled -eq 'True' -and ($policy.AllowInboundRules -eq 'False' -or $policy.AllowLocalFirewallRules -eq 'False')) {
            '[!] Chính sách Public chặn quy tắc cục bộ hoặc kết nối vào. Liên hệ quản trị mạng.'
        }
        $ruleErrors = @()
        $rules = @(Get-NetFirewallRule -Name 'PrintBridge.Public.*' -PolicyStore ActiveStore -ErrorAction SilentlyContinue -ErrorVariable ruleErrors)
        $readFailures = @($ruleErrors | Where-Object { $_.CategoryInfo.Category -ne 'ObjectNotFound' })
        if ($readFailures.Count) { "[!] Không đọc được quy tắc firewall: $($readFailures[0].Exception.Message)"; continue }
        foreach ($rule in $rules) {
            if ($rule.Enabled -ne 'True' -or $rule.Action -ne 'Allow' -or $rule.Direction -ne 'Inbound' -or $rule.Profile -ne 'Public') { continue }
            $ports = $rule | Get-NetFirewallPortFilter
            $ips = $rule | Get-NetFirewallAddressFilter
            $app = $rule | Get-NetFirewallApplicationFilter
            $iface = $rule | Get-NetFirewallInterfaceFilter
            if ($ports.Protocol -in @('TCP','6') -and $ports.LocalPort -contains [string]$IppPort -and ([string]$ips.LocalAddress) -in $local -and
                [Environment]::ExpandEnvironmentVariables([string]$app.Program) -eq $exe -and $iface.InterfaceAlias -contains $network.InterfaceAlias -and $rule.Name -eq "PrintBridge.Public.$($ips.LocalAddress).$($ips.RemoteAddress)") {
                "[OK] Public: quy tắc IPP cho $($ips.RemoteAddress) tới $($ips.LocalAddress), TCP/$IppPort."
                $found = $true
            } elseif (([string]$ips.LocalAddress) -in $local) {
                "[!] Quy tắc $($rule.Name) chưa khớp ứng dụng, card mạng hoặc cổng hiện tại. Chấp nhận lại IP sau khi đặt PrintBridge.exe ở thư mục cố định."
            }
        }
        if (!$found) { "[!] $($network.InterfaceAlias) Public: vào Configure Firewall → Public, bấm Quét máy trong LAN, chọn đúng IP và bấm Chấp nhận." }
        $mdnsName = "PrintBridge.Public.mDNS.$($local[0])"
        $mdnsRule = Get-NetFirewallRule -Name $mdnsName -PolicyStore ActiveStore -ErrorAction SilentlyContinue
        if ($mdnsRule -and $mdnsRule.Enabled -eq 'True' -and $mdnsRule.Action -eq 'Allow') {
            $mdnsPorts = $mdnsRule | Get-NetFirewallPortFilter
            $mdnsIps = $mdnsRule | Get-NetFirewallAddressFilter
            $mdnsApp = $mdnsRule | Get-NetFirewallApplicationFilter
            $mdnsIf = $mdnsRule | Get-NetFirewallInterfaceFilter
            if ($mdnsPorts.Protocol -in @('UDP','17') -and $mdnsPorts.LocalPort -contains '5353' -and
                ([string]$mdnsIps.LocalAddress) -in $local -and $mdnsIps.RemoteAddress -contains 'LocalSubnet' -and
                [Environment]::ExpandEnvironmentVariables([string]$mdnsApp.Program) -eq $exe -and
                $mdnsIf.InterfaceAlias -contains $network.InterfaceAlias) {
                "[OK] Public: Bonjour/mDNS UDP/5353 chỉ trong LAN."
            } else { "[!] Quy tắc Public mDNS chưa khớp ứng dụng/card mạng. Chấp nhận lại thiết lập firewall." }
        } else { "[!] iPhone chưa thể tìm thấy máy in. Configure Firewall → Public → bật Cho iPhone tìm máy in." }
    }
    if (!$profiles.Count) { '[!] Chưa tìm thấy kết nối LAN.' }
    if (!$trusted.Count) { return }
    foreach ($network in $trusted) { "[OK] LAN $($network.InterfaceAlias): $($network.NetworkCategory)" }
    foreach ($entry in @(@('TCP',$IppPort,'PrintBridge IPP Server (TCP 631)'),@('UDP',5353,'PrintBridge mDNS Discovery (UDP 5353)'))) {
        $rules = @(Get-NetFirewallRule -PolicyStore ActiveStore -DisplayName $entry[2] -ErrorAction SilentlyContinue |
            Where-Object { $_.Enabled -eq 'True' -and $_.Direction -eq 'Inbound' -and $_.Action -eq 'Allow' })
        $matchesPort = $false
        foreach ($rule in $rules) {
            foreach ($filter in @($rule | Get-NetFirewallPortFilter)) {
                if ($filter.Protocol -eq $entry[0] -and $filter.LocalPort -contains [string]$entry[1]) {
                    foreach ($network in $trusted) {
                        $profile = if ($network.NetworkCategory -eq 'DomainAuthenticated') { 'Domain' } else { 'Private' }
                        if ($rule.Profile -eq 'Any' -or ([string]$rule.Profile -split ',\s*') -contains $profile) { $matchesPort = $true }
                    }
                }
            }
        }
        if ($matchesPort) { "[OK] Quy tắc $($entry[0])/$($entry[1]) trên mạng tin cậy." }
        else { "[!] Chưa xác minh được quy tắc $($entry[0])/$($entry[1]). Kiểm tra Windows Firewall hoặc dùng Configure Firewall." }
    }
} catch { "[!] Không đọc được cấu hình mạng/firewall: $($_.Exception.Message)" }
