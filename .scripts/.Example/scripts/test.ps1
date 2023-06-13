$User = 1
Remove-Variable User
[ordered]@{
    StartUp = $StartUp
    UserCredential = $UserCredential
    User = $User
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

