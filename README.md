# PowerShellWebService

## Getting started

Install [.NET 7.0 SDK (v7.0.102) - Windows x64](https://dotnet.microsoft.com/en-us/download/dotnet/thank-you/sdk-7.0.102-windows-x64-installer)

Install [PowerShell 7](https://github.com/PowerShell/PowerShell/releases)

**Check settings in conf.json file**

```
cd <ProjectPath>
dotnet.exe publish --configuration PublishRelease
cd <ProjectPath>\bin\PublishRelease\net7.0\publish\
PowerShellWebService.exe
```

## Usage
Check conf.json file

***

## Docs

This service is designed to work as an internal service on a web server to simplify data exchange with PowerShell.

It is very simple because it is intended for those who find it difficult to understand C#, but knows other languages and wants to get the power of PowerShell.
### It's not worth publishing it for users! Instead, publish it to the address 127.0.0.1 or any other 127.0.0.*, if we are talking about Windows, yes, you can do that there :-)

After that, you can write a web service in your favorite language and use PowerShell Web Service inside it, it can be Go, Python, PHP, Perl, Java, JavaScript or something else at your discretion.
And please don't run any more PowerShell.exe and disassemble stdout, because it has 6 output streams under the hood instead of 2 and you can skip the most interesting :-)
See "Output Streams" below

- [About PowerShell Output Streams](https://learn.microsoft.com/en-us/powershell/module/microsoft.powershell.core/about/about_output_streams?view=powershell-7.3)
- [Publish an ASP.NET Core app to IIS](https://learn.microsoft.com/ru-ru/aspnet/core/tutorials/publish-to-iis?view=aspnetcore-7.0)
- [Tutorial: Create a minimal API with ASP.NET Core](https://learn.microsoft.com/ru-ru/aspnet/core/tutorials/min-web-api?view=aspnetcore-7.0)
- [ASP.NET documentation](https://learn.microsoft.com/ru-ru/aspnet/core/?view=aspnetcore-7.0)

***




