$ErrorActionPreference = 'Stop'
$global:MockBridgeRules = @{}
$script:created = 0
$script:updated = 0
function Get-NetIPAddress { [pscustomobject]@{IPAddress='192.0.2.10';InterfaceIndex=7;InterfaceAlias='Wi-Fi'} }
function Get-NetConnectionProfile { [pscustomobject]@{InterfaceIndex=7;InterfaceAlias='Wi-Fi';NetworkCategory='Public'} }
function Get-NetFirewallProfile { [pscustomobject]@{Enabled='True';AllowInboundRules='NotConfigured';AllowLocalFirewallRules='True'} }
function Remove-NetFirewallRule { param($Name); if ($Name) { $global:MockBridgeRules.Remove($Name) } }
function Get-NetFirewallRule {
    param($Name)
    foreach ($key in $global:MockBridgeRules.Keys) { if ($key -like $Name) { $global:MockBridgeRules[$key] } }
}
function New-NetFirewallRule {
    param($Name,$DisplayName,$Group,$Profile,$Direction,$Action,$Protocol,$LocalPort,$LocalAddress,$RemoteAddress,$Program,$InterfaceAlias,$Enabled)
    $global:MockBridgeRules[$Name] = [pscustomobject](@{} + $PSBoundParameters)
    $script:created++
}
function Set-NetFirewallRule { throw "Set-NetFirewallRule should not be needed" }
function Get-Service { @{Status='Running'} }
function Get-Printer { }
function Get-PrinterDriver { }
function Get-PnpDevice { }
function Get-NetTCPConnection { [pscustomobject]@{LocalPort=631;LocalAddress='::'} }
function Get-NetFirewallPortFilter { process { $_ } }
function Get-NetFirewallAddressFilter { process { $_ } }
function Get-NetFirewallApplicationFilter { process { $_ } }
function Get-NetFirewallInterfaceFilter { process { $_ } }
$setup = Get-Content -Raw (Join-Path $PSScriptRoot '../scripts/setup-firewall.ps1')
# Run the rule logic against mocks without requesting host privileges.
$invoke = [scriptblock]::Create(($setup -replace '(?m)^#Requires[^\n]*',''))
$app = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes((Get-Process -Id $PID).Path))
& $invoke -IppPort 631 -LocalIPv4 192.0.2.10 -ClientIPv4 192.0.2.20 -AppBase64 $app -EnableMdns
if ($global:MockBridgeRules['PrintBridge.Public.192.0.2.10.192.0.2.20'].RemoteAddress -ne '192.0.2.20' -or $global:MockBridgeRules['PrintBridge.Public.192.0.2.10.192.0.2.20'].LocalAddress -ne '192.0.2.10' -or $global:MockBridgeRules['PrintBridge.Public.192.0.2.10.192.0.2.20'].Profile -ne 'Public' -or $global:MockBridgeRules['PrintBridge.Public.192.0.2.10.192.0.2.20'].Protocol -ne 'TCP' -or $global:MockBridgeRules['PrintBridge.Public.192.0.2.10.192.0.2.20'].LocalPort -ne 631) { throw 'Invalid firewall scope' }
& $invoke -IppPort 631 -LocalIPv4 192.0.2.10 -ClientIPv4 192.0.2.20 -AppBase64 $app -EnableMdns
if ($created -ne 4 -or $updated -ne 0) { throw 'Expected idempotent TCP and mDNS rules'  }
$check = Join-Path $PSScriptRoot '../src/PrintBridge/Printing/StartupChecks.ps1'
$r = & $check 631 W10= $PID
if (!($r -match '\[OK\] Public:') -or !($r -match '\[OK\] PrintBridge đang lắng nghe')) { throw ($r -join '\n') }
$global:MockBridgeRules = @{}
$r = & $check 631 W10= $PID
if (!($r -match 'Configure Firewall') -or $r -match '\[OK\] Public:') { throw 'Missing rule not detected' }
'PASS scoped rule, repeated setup, listener, active rule, missing rule'
