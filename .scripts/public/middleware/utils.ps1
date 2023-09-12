Function ConvertTo-Prom {
	param(
		[Parameter(Mandatory=$true,ValueFromPipeline=$true)][System.Object[]]$InputObject,
		[Parameter(Mandatory=$true)][System.String]$Name,
		[Parameter(Mandatory=$false)][System.Object[]]$Property,
		[Parameter(Mandatory=$true)][ScriptBlock]$Value,
		[Parameter(Mandatory=$false)][System.String]$Help,
		[Parameter(Mandatory=$false)][ValidateSet('Counter','Gauge','Histogram','Summary')][System.String]$Type
	)
	Begin {
		if ($Help) {"# HELP $Name $Help"}
		if ($Type) {"# TYPE $Name $($Type.ToLower())"}
		$Strings = [System.Collections.Generic.List[System.String]]::new()
		$Select = @{Property = $Property; ErrorAction = 'SilentlyContinue'}
		$repl = @('[\t\r\n\"\{\}\(\)]+',' ')
		if ($Property) {
			$ForeachParam = @{Process = {
				$String = "$($_.Name){$($_.Property)}"
				if ($Strings.Contains($String)) {
					Write-Warning -Message "Duplicated record: '$($_.Name){$($_.Property)} $($_.Result)'"
				} else {
					Write-Output -InputObject "$($_.Name){$($_.Property)} $($_.Result)"
					$Strings.Add($String)
				}
			}}
		} else {
			$ForeachParam = @{
				Process = {
					$String = "$($_.Name)"
					if ($Strings.Contains($String)) {
						Write-Warning -Message "Duplicated record: '$($_.Name) $($_.Result)'"
					} else {
						Write-Output -InputObject "$($_.Name) $($_.Result)"
						$Strings.Add($String)
					}
				}
			}
		}
	}
	Process {
		$InputObject | Select-Object -Property @(
			,@{Name = 'Name'; Expression = {$Name}}
			,@{Name = 'Property'; Expression = {
				(($_ | Select-Object @Select).psobject.Properties | % {"$($_.Name)=`"$($_.Value -replace $repl)`""}) -join ','
			}}
			,@{Name = 'Result'; Expression = $Value}
		) | % {
			if ([System.String]::IsNullOrWhiteSpace("$($_.Result)")) {
				Write-Warning -Message "Empty result: '$($_.Name){$($_.Property)}'"
			} else {
				Write-Output -InputObject $_
			}
		} | % @ForeachParam

	}
	End {}
}
