param(
    [Parameter(Mandatory=$true)][System.String]$__SCRIPTFILE__,
    [Parameter(Mandatory=$true)][System.String]$__SCRIPTNAME__,
    [Parameter(Mandatory=$true)][System.String]$__WRAPPER__,
    [Parameter(Mandatory=$true)][System.Object]$__QUERY__,
    [Parameter(Mandatory=$true)][System.Object]$__BODY__,
    [Parameter(Mandatory=$true)][System.String]$__METHOD__,
    [Parameter(Mandatory=$true)][System.Object]$__USER__,
    [Parameter(Mandatory=$true)][System.Object]$__CONTEXT__,
    [Parameter(Mandatory=$false)][System.String]$__CONTENTTYPE__,
    [Parameter(Mandatory=$false)][System.String]$__TRANSCRIPT_FILE__
)

# Using checks
# . $PSScriptRoot\middleware\checks.ps1

[hashtable]$__PARAMS__ = @{}

$__QUERY__.GetEnumerator() | ForEach-Object {$__PARAMS__[$_.Key] = $_.Value}

if ($__BODY__) {
    ConvertFrom-Json -InputObject $__BODY__ -AsHashtable | ForEach-Object {$_.GetEnumerator()} | ForEach-Object {$__PARAMS__[$_.Key] = $_.Value}
}

# Write-Debug "__WRAPPER__:"
# Write-Debug "PSBoundParameters: $(ConvertTo-Json $PSBoundParameters)"
# Write-Debug "__SCRIPT__"
# Write-Debug "__PARAMS__: $(ConvertTo-Json $__PARAMS__)"

$null = Start-Transcript -Path $__TRANSCRIPT_FILE__ -Force

# Using global var, that configured in CachedVariables section of config.json file
if (!$Global:StartUp) {$Global:StartUp = Get-Date}

Set-Alias -Name '__SCRIPT__' -Value $__SCRIPTFILE__

__SCRIPT__ @__PARAMS__

$null = Stop-Transcript