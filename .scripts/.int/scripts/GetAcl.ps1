param(
	[Parameter(Mandatory)][string]$Path
)

New-PSDrive -Name FS -PSProvider FileSystem -Root $Path | Out-Null

$Acl = Get-Acl -Path FS:\

$Acl.GetAccessRules($true,$true,[System.Security.Principal.NTAccount]) | ? {$_.FileSystemRights -ge 1} | Select-Object -Property @(
    ,@{Name = 'FileSystemRights'; Expression = {$_.FileSystemRights}}
    ,@{Name = 'AccessControlType'; Expression = {$_.AccessControlType}}
    ,@{Name = 'IdentityReference'; Expression = {$_.IdentityReference}}
    ,@{Name = 'IsInherited'; Expression = {$_.IsInherited}}
    ,@{Name = 'InheritanceFlags'; Expression = {$_.InheritanceFlags}}
    ,@{Name = 'PropagationFlags'; Expression = {$_.PropagationFlags}}
)
