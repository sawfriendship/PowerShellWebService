@(1..5) | select @{n='i';e={$_}} | % {ConvertTo-Json $_ -Compress}
