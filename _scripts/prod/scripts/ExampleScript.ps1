$DebugPreference = "SilentlyContinue"
$VerbosePreference = "SilentlyContinue"

write-host 'host' -ForegroundColor Black -BackgroundColor Blue
write-host 'host red' -ForegroundColor Cyan -BackgroundColor Red
write-verbose 'verbose'
1..1  | % {$_} | % {write-verbose 'verbose 1'}
write-warning 'warning'
write-debug 'ddd'