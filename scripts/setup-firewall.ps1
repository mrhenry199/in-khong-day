#Requires -RunAsAdministrator
param([ValidateRange(1,65535)][int]$IppPort = 631, [switch]$Remove,
      [ipaddress]$LocalIPv4, [ipaddress]$ClientIPv4, [string]$AppBase64)
$ErrorActionPreference = 'Stop'
$group = 'PrintBridge Public IPP'
if ($ClientIPv4) {
    $address = Get-NetIPAddress -AddressFamily IPv4 -IPAddress $LocalIPv4.IPAddressToString -ErrorAction Stop
    $profile = Get-NetConnectionProfile -InterfaceIndex $address.InterfaceIndex
    if ($profile.NetworkCategory -ne 'Public') { throw 'Selected interface must be Public.' }
    $exe = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($AppBase64))
    $name = "PrintBridge.Public.$($LocalIPv4.IPAddressToString).$($ClientIPv4.IPAddressToString)"
    $params = @{Profile='Public'; Direction='Inbound'; Action='Allow'; Protocol='TCP'; LocalPort=$IppPort;
        LocalAddress=$LocalIPv4.IPAddressToString; RemoteAddress=$ClientIPv4.IPAddressToString;
        Program=$exe; InterfaceAlias=$address.InterfaceAlias; Enabled='True'}
    if (Get-NetFirewallRule -Name $name -ErrorAction SilentlyContinue) {
        Set-NetFirewallRule -Name $name @params
    } else {
        New-NetFirewallRule -Name $name -DisplayName $name -Group $group @params | Out-Null
    }
    $active = Get-NetFirewallRule -Name $name -PolicyStore ActiveStore -ErrorAction Stop
    if ($active.Enabled -ne 'True' -or $active.Action -ne 'Allow') { throw "Rule $name is not active." }
    $policy = Get-NetFirewallProfile -Name Public -PolicyStore ActiveStore
    if ($policy.Enabled -eq 'True' -and ($policy.AllowInboundRules -eq 'False' -or $policy.AllowLocalFirewallRules -eq 'False')) {
        throw 'Windows policy disables inbound or local firewall rules on Public. Contact your administrator.'
    }
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
