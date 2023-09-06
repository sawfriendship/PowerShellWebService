Get-InstalledModule -ErrorAction SilentlyContinue | Select-Object -Property @{n='Name';e={$_.Name -as [string]}},@{n='Version';e={$_.Version -as [string]}}
