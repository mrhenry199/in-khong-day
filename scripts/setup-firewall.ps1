#Requires -RunAsAdministrator
param([ValidateRange(1,65535)][int]$IppPort = 631, [switch]$Remove,
      [ipaddress]$LocalIPv4, [ipaddress]$ClientIPv4, [string]$AppBase64, [switch]$EnableMdns, [switch]$ShareAll, [switch]$DiscoveryOnly, [switch]$Revoke)
$ErrorActionPreference = 'Stop'
$group = 'PrintBridge Public IPP'
$policyKey = 'HKLM:\SOFTWARE\PrintBridge\ClientAccess'
if ($ClientIPv4 -or $DiscoveryOnly) {
    $address = Get-NetIPAddress -AddressFamily IPv4 -IPAddress $LocalIPv4.IPAddressToString -ErrorAction Stop
    $profile = Get-NetConnectionProfile -InterfaceIndex $address.InterfaceIndex
    $category = if ($profile.NetworkCategory -eq 'DomainAuthenticated') { 'Domain' } else { [string]$profile.NetworkCategory }
    $policy = Get-NetFirewallProfile -Name $category -PolicyStore ActiveStore
    if ($policy.Enabled -eq 'True' -and ($policy.AllowInboundRules -eq 'False' -or $policy.AllowLocalFirewallRules -eq 'False')) {
        throw 'Windows policy disables local inbound rules.'
    }
    $exe = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($AppBase64))
    $mdnsName = "PrintBridge.Public.mDNS.$($LocalIPv4.IPAddressToString)"
    if ($EnableMdns) {
        $mdnsParams = @{Profile=$category; Direction='Inbound'; Action='Allow'; Protocol='UDP'; LocalPort=5353;
            LocalAddress=@($LocalIPv4.IPAddressToString,'224.0.0.251'); RemoteAddress='LocalSubnet'; Program=$exe;
            InterfaceAlias=$address.InterfaceAlias; Enabled='True'}
        Get-NetFirewallRule -Name $mdnsName -ErrorAction SilentlyContinue | Remove-NetFirewallRule
        New-NetFirewallRule -Name $mdnsName -DisplayName 'PrintBridge Public mDNS (UDP 5353)' -Group $group @mdnsParams | Out-Null
        $mdnsActive = Get-NetFirewallRule -Name $mdnsName -PolicyStore ActiveStore -ErrorAction Stop
        if ($mdnsActive.Enabled -ne 'True' -or $mdnsActive.Action -ne 'Allow') { throw "mDNS rule $mdnsName is not active." }
    }
    if ($DiscoveryOnly) { return }
    # Seed a new allowlist with clients approved by previous builds on this server IP.
    $saved = Get-ItemProperty -LiteralPath $policyKey -Name Allowed -ErrorAction SilentlyContinue
    if ($null -eq $saved) {
        $prefix = "PrintBridge.Public.$($LocalIPv4.IPAddressToString)."
        $allowed = @(Get-NetFirewallRule -Name "$prefix*" -ErrorAction SilentlyContinue |
            Where-Object { $_.Enabled -eq 'True' -and $_.Action -eq 'Allow' } |
            ForEach-Object { $_.Name.Substring($prefix.Length) })
    } else { $allowed = @($saved.Allowed) }
    $ip = $ClientIPv4.IPAddressToString
    $allowed = @($allowed | Where-Object { $_ -and $_ -ne $ip })
    if (!$Revoke) { $allowed += $ip }
    $name = "PrintBridge.Public.$($LocalIPv4.IPAddressToString).$ip"
    if ($Revoke) {
        Get-NetFirewallRule -Name $name -ErrorAction SilentlyContinue | Remove-NetFirewallRule
    } else {
        $params = @{Profile=$category; Direction='Inbound'; Action='Allow'; Protocol='TCP'; LocalPort=$IppPort;
            LocalAddress=$LocalIPv4.IPAddressToString; RemoteAddress=$ClientIPv4.IPAddressToString;
            Program=$exe; InterfaceAlias=$address.InterfaceAlias; Enabled='True'}
        Get-NetFirewallRule -Name $name -ErrorAction SilentlyContinue | Remove-NetFirewallRule
        New-NetFirewallRule -Name $name -DisplayName $name -Group $group @params | Out-Null
        $active = Get-NetFirewallRule -Name $name -PolicyStore ActiveStore -ErrorAction Stop
        if ($active.Enabled -ne 'True' -or $active.Action -ne 'Allow') { throw "Rule $name is not active." }
    }
    if (!(Test-Path $policyKey)) { New-Item -Path $policyKey -Force | Out-Null }
    $previous = Get-ItemProperty -Path $policyKey -Name Known -ErrorAction SilentlyContinue
    $known = @( (@($previous.Known) + @($allowed) + @($ip)) | Where-Object { $_ } | Sort-Object -Unique )
    New-ItemProperty -Path $policyKey -Name Known -Value ([string[]]$known) -PropertyType MultiString -Force | Out-Null
    New-ItemProperty -Path $policyKey -Name Allowed -Value ([string[]]$allowed) -PropertyType MultiString -Force | Out-Null
    return
}
if ($Remove) { Get-NetFirewallRule -Group $group -ErrorAction SilentlyContinue | Remove-NetFirewallRule }
$ipp = 'PrintBridge IPP Server (TCP 631)'
$mdns = 'PrintBridge mDNS Discovery (UDP 5353)'
foreach ($name in @($ipp,$mdns,'PrintBridge Application','PrintBridge mDNS (UDP 5353)')) {
    Get-NetFirewallRule -DisplayName $name -ErrorAction SilentlyContinue | Remove-NetFirewallRule
}
# Older development builds used the port number in the IPP display name.
Get-NetFirewallRule -DisplayName 'PrintBridge IPP (TCP *)' -ErrorAction SilentlyContinue |
    Where-Object DisplayName -match '^PrintBridge IPP \(TCP [0-9]+\)$' | Remove-NetFirewallRule
if (!$Remove) {
    $common = @{Direction='Inbound'; Action='Allow'; Profile=@('Private','Domain'); RemoteAddress='LocalSubnet'}
    New-NetFirewallRule @common -DisplayName $ipp -Protocol TCP -LocalPort $IppPort | Out-Null
    New-NetFirewallRule @common -DisplayName $mdns -Protocol UDP -LocalPort 5353 | Out-Null
}

if (($ShareAll -or $Remove) -and (Get-ItemProperty -Path $policyKey -Name Allowed -ErrorAction SilentlyContinue)) {
    Remove-ItemProperty -Path $policyKey -Name Allowed -ErrorAction Stop
}
