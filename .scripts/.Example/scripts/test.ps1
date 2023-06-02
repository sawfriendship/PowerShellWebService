# throw 13
# 1/0
# Write-Verbose -Verbose 'verb'
# 1
[ordered]@{
    StartUp = $StartUp
    UserCredential = $UserCredential
    ht = @{
        a = @{
            b = @{
                c = @{
                    d = @{
                        e = @(1..3)
                    }
                }
            }
        }
    }
}

