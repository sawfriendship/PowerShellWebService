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
} elseif ($Query.Count) {
    [hashtable]$Params = $Query
} else {
    [hashtable]$Params = @{}
}

$RouteValues = $Context.Request.RouteValues | % -Begin {$h=@{}} -Process {$h[$_.Key]=$_.Value} -End {$h}

$Wrapper = $RouteValues['Wrapper']
$Script = $RouteValues['Script']

Start-Transcript -Path "$PSScriptRoot\Transcript\$Script\$((Get-Date).ToString('yyyy-MM-dd'))\$((Get-Date).ToString('yyyy-MM-dd_HH-mm-ss-ffffff')).txt" -Force | Out-Null

# Using global var, that configured in CachedVariables section of config.json file
if (!$Global:StartUp) {$Global:StartUp = Get-Date}

Set-Alias -Name 'Script' -Value $ScriptFile

Script @Params

Stop-Transcript | Out-Null
