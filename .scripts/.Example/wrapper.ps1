param(
    [System.String]$ScriptFile,
    [System.Object]$Query,
    [System.Object]$Body,
    [System.Object]$Context
)

# Using checks
# . $PSScriptRoot\middleware\checks.ps1

if ($Body) {
	[hashtable]$Params = ConvertFrom-Json -InputObject $Body -AsHashtable   
}

# Using global var, that configured in CachedVariables section of config.json file
if (!$Global:StartUp) {$Global:StartUp = Get-Date}

$ScriptItem = Get-Item -Path $ScriptFile
$ScriptName = $ScriptItem.BaseName
Start-Transcript -Path "$PSScriptRoot\Transcript\$ScriptName\$((Get-Date).ToString('yyyy-MM-dd'))\$((Get-Date).ToString('yyyy-MM-dd_HH-mm-ss-ffffff')).txt" -Force | Out-Null

Set-Alias -Name 'Script' -Value $ScriptFile

Script @Params

Stop-Transcript | Out-Null
