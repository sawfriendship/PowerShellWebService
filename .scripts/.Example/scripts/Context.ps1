[ordered]@{
    RemoteIpAddress = [IPAddress]($__CONTEXT__.Connection.RemoteIpAddress.Address)
    Host = $__CONTEXT__.Request.Host
    UserName = $__CONTEXT__.User.Identity.Name
    UserGroups = $__CONTEXT__.User.Identity.Groups
}
