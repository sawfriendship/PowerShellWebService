# @(1..5) | select @{n='i';e={$_}} | % {ConvertTo-Json $_ -Compress}
@(1..5) | % {"$_ \r\n"}
get-date