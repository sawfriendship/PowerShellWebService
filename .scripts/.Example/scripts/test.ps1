# throw 13
# 1/0
qwe
# Write-Verbose -Verbose 'verb'
# 1
[ordered]@{
    nest_test = $Context.User.IsInRole('net\nest_test')
    _test_ = $_test_
    StartUp = $StartUp
    Result = Get-Date @Params
    UserCredential = $UserCredential
}

