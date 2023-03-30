param(
	[Parameter(Mandatory)][string]$Path
)

New-PSDrive -Name FS -PSProvider FileSystem -Root $Path | Out-Null

$Acl = Get-Acl -Path FS:\

$Acl.GetAccessRules($true,$true,[System.Security.Principal.NTAccount]) | ? {$_.FileSystemRights -ge 1} | Select-Object -Property @(
    ,@{Name = 'FileSystemRights'; Expression = {$_.FileSystemRights.ToString()}}
    ,@{Name = 'AccessControlType'; Expression = {$_.AccessControlType.ToString()}}
    ,@{Name = 'IdentityReference'; Expression = {$_.IdentityReference.ToString()}}
    ,@{Name = 'IsInherited'; Expression = {$_.IsInherited}}
    ,@{Name = 'InheritanceFlags'; Expression = {$_.InheritanceFlags.ToString()}}
    ,@{Name = 'PropagationFlags'; Expression = {$_.PropagationFlags.ToString()}}
)
