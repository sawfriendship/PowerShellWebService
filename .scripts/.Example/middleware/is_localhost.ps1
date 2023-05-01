if ([IPAddress]($Context.Connection.RemoteIpAddress.Address)) {
	throw 'only for local'
}