param(
    [System.String]$ScriptFile,
    [System.Object]$Query,
    [System.Object]$Body,
    [System.Object]$Context
)

# Using checks
. $PSScriptRoot\middleware\is_localhost.ps1

# Using global var, that configured in CachedVariables section of config.json file
if (!$Global:StartUp) {$Global:StartUp = Get-Date}


if ($Context.Request.Method -eq 'GET') {
	[hashtable]$Params = $Query
} else {
	[hashtable]$Params = ConvertFrom-Json -InputObject $Body -AsHashtable   
}

Set-Alias -Name 'Script' -Value $ScriptFile

Script @Params
