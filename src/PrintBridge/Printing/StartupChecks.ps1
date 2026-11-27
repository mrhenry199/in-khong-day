param([int]$IppPort, [string]$SelectedQueues)
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
    foreach ($printer in $printers) {
        try {
            $driver = Get-PrinterDriver -Name $printer.DriverName
            if (!$driver.DriverPath -or !(Test-Path -LiteralPath $driver.DriverPath -PathType Leaf)) {
                "[!] $($printer.Name): thiếu tệp driver. Cài lại $($printer.DriverName)."
            } else { "[OK] $($printer.Name): tìm thấy driver $($driver.Name)." }
            if ($printer.PrinterStatus -notin @('Normal','Printing','Processing','WarmingUp','PowerSave')) {
                "[!] $($printer.Name): Windows báo $($printer.PrinterStatus). Kiểm tra hàng đợi và kết nối máy in."
            }
        } catch { "[!] $($printer.Name): không xác minh được driver. $($_.Exception.Message)" }
    }
} catch { "[!] Không đọc được trạng thái in: $($_.Exception.Message)" }

try {
    Get-PnpDevice -PresentOnly | Where-Object { ($_.Class -in @('Printer','PrintQueue') -or $_.InstanceId -like 'USBPRINT\*') -and $_.Status -ne 'OK' } |
        ForEach-Object { "[!] $($_.FriendlyName): $($_.Status). Kiểm tra driver trong Device Manager." }
} catch { "[!] Không đọc được danh sách thiết bị PnP: $($_.Exception.Message)" }

try {
    $trusted = @(Get-NetConnectionProfile | Where-Object NetworkCategory -in @('Private','DomainAuthenticated'))
    if (!$trusted.Count) { '[!] Chưa thấy kết nối Private/Domain. Kiểm tra cấu hình mạng LAN đang dùng.' }
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
