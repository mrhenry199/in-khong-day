#Requires -RunAsAdministrator
param([ValidateRange(1,65535)][int]$IppPort = 631, [switch]$Remove)
$ErrorActionPreference = 'Stop'
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
