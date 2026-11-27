$ErrorActionPreference = 'Stop'
$global:MockBridgeRule = $null
$script:created = 0
$script:updated = 0
function Get-NetIPAddress { [pscustomobject]@{IPAddress='192.0.2.10';InterfaceIndex=7;InterfaceAlias='Wi-Fi'} }
function Get-NetConnectionProfile { [pscustomobject]@{InterfaceIndex=7;InterfaceAlias='Wi-Fi';NetworkCategory='Public'} }
function Get-NetFirewallRule { $global:MockBridgeRule }
function New-NetFirewallRule {
    param($Name,$Profile,$Direction,$Action,$Protocol,$LocalPort,$LocalAddress,$RemoteAddress,$Program,$InterfaceAlias,$Enabled)
    $global:MockBridgeRule = [pscustomobject](@{} + $PSBoundParameters)
    $script:created++
}
function Set-NetFirewallRule { $script:updated++ }
function Get-Service { @{Status='Running'} }
function Get-Printer { }
function Get-PrinterDriver { }
function Get-PnpDevice { }
function Get-NetTCPConnection { [pscustomobject]@{LocalPort=631;LocalAddress='::'} }
function Get-NetFirewallPortFilter { $global:MockBridgeRule }
function Get-NetFirewallAddressFilter { $global:MockBridgeRule }
function Get-NetFirewallApplicationFilter { $global:MockBridgeRule }
function Get-NetFirewallInterfaceFilter { $global:MockBridgeRule }
$setup = Get-Content -Raw (Join-Path $PSScriptRoot '../scripts/setup-firewall.ps1')
# Run the rule logic against mocks without requesting host privileges.
$invoke = [scriptblock]::Create(($setup -replace '(?m)^#Requires[^\n]*',''))
$app = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes((Get-Process -Id $PID).Path))
& $invoke -IppPort 631 -LocalIPv4 192.0.2.10 -ClientIPv4 192.0.2.20 -AppBase64 $app
if ($global:MockBridgeRule.RemoteAddress -ne '192.0.2.20' -or $global:MockBridgeRule.LocalAddress -ne '192.0.2.10' -or $global:MockBridgeRule.Profile -ne 'Public' -or $global:MockBridgeRule.Protocol -ne 'TCP' -or $global:MockBridgeRule.LocalPort -ne 631) { throw 'Invalid firewall scope' }
& $invoke -IppPort 631 -LocalIPv4 192.0.2.10 -ClientIPv4 192.0.2.20 -AppBase64 $app
if ($created -ne 1 -or $updated -ne 1) { throw 'Expected one rule after repeated setup' }
$check = Join-Path $PSScriptRoot '../src/PrintBridge/Printing/StartupChecks.ps1'
$r = & $check 631 W10= $PID
if (!($r -match '\[OK\] Public:') -or !($r -match '\[OK\] PrintBridge đang lắng nghe')) { throw ($r -join '\n') }
$global:MockBridgeRule = $null
$r = & $check 631 W10= $PID
if (!($r -match 'Configure Firewall') -or $r -match '\[OK\] Public:') { throw 'Missing rule not detected' }
'PASS scoped rule, repeated setup, listener, active rule, missing rule'
