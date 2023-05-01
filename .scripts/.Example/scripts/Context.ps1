[ordered]@{
    RemoteIpAddress = [IPAddress]($Context.Connection.RemoteIpAddress.Address)
    Host = $Context.Request.Host
    UserName = $Context.User.Identity.Name
    UserGroups = $Context.User.Identity.Groups
}
