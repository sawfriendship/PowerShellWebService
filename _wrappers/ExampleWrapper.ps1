param(
    [System.String]$ScriptFile,
    [System.Object]$Query,
    [System.Object]$Body,
    [System.Object]$Context
)

if ($Context.Request.Method -eq 'GET') {
	[hashtable]$Params = $Query
} else {
	[hashtable]$Params = ConvertFrom-Json -InputObject $Body -AsHashtable   
}

Set-Alias -Name 'Script' -Value $ScriptFile

Script @Params
