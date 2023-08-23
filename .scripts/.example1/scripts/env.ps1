param(
    [string]$Name = '*'
)
ls env:\ | ? Name -like $Name | select Name,Value