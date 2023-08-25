1/0
@(1..5) | select @{n='i';e={$_}} | ConvertTo-Html