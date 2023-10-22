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
    [Parameter(Mandatory=$false)][System.String]$__FORMAT__,
    [Parameter(Mandatory=$false)][System.String]$__TRANSCRIPT_FILE__
)

$null = Start-Transcript -Path $__TRANSCRIPT_FILE__ -Force

. $PSScriptRoot\middleware\init.ps1
. $PSScriptRoot\middleware\utils.ps1

[hashtable]$__PARAMS__ = @{}

$__QUERY__.GetEnumerator() | ForEach-Object {$__PARAMS__[$_.Key] = $_.Value}

if ($__BODY__) {
    ConvertFrom-Json -InputObject $__BODY__ -AsHashtable | Where-Object {$_.Key -notlike '__*__'} | ForEach-Object {$_.GetEnumerator()} | ForEach-Object {$__PARAMS__[$_.Key] = $_.Value}
}

Write-Debug "__WRAPPER__: $__WRAPPER__"
Write-Debug "__SCRIPTNAME__: $__SCRIPTNAME__"
Write-Debug "__QUERY__: $(ConvertTo-Json $__QUERY__)"
Write-Debug "__BODY__: $__BODY__"


# Using global var, that configured in CachedVariables section of config.json file
if (!$Global:StartUp) {$Global:StartUp = Get-Date}

Set-Alias -Name '__SCRIPT__' -Value $__SCRIPTFILE__

__SCRIPT__ @__PARAMS__

$null = Stop-Transcript
