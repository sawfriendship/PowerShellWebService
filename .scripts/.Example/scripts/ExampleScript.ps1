$DebugPreference = "SilentlyContinue"
$VerbosePreference = "SilentlyContinue"

write-host 'host' -ForegroundColor Black -BackgroundColor Blue
write-host 'host red' -ForegroundColor Cyan -BackgroundColor Red
write-verbose 'verbose'
write-warning 'warning'
write-debug 'ddd'
