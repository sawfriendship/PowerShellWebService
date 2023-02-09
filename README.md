# PowerShellWebService

## Getting started

Install [.NET 7.0 SDK (v7.0.102) - Windows x64](https://dotnet.microsoft.com/en-us/download/dotnet/thank-you/sdk-7.0.102-windows-x64-installer)

Install [PowerShell 7](https://github.com/PowerShell/PowerShell/releases)

### Check settings in conf.json file

**Publish and run**
```
cd <ProjectPath>
dotnet.exe publish --configuration PublishRelease
cd <ProjectPath>\bin\PublishRelease\net7.0\publish\
PowerShellWebService.exe
```

**self-contained Publish for Windows**
```
cd <ProjectPath>
dotnet publish --configuration PublishRelease --self-contained --runtime win10-x64
cd <ProjectPath>\bin\PublishRelease\net7.0\publish\win10-x64\
```
**self-contained Publish for Linux**
```
cd <ProjectPath>
dotnet publish --configuration PublishRelease --self-contained --runtime ubuntu.14.04-x64
cd <ProjectPath>\bin\PublishRelease\net7.0\publish\ubuntu.14.04-x64\
```



## Usage

**Python example GET request**
``` python
import requests, json, urllib3
urllib3.disable_warnings(urllib3.exceptions.InsecureRequestWarning)
POWERSHELL_WEBSERVICE_URL = 'http://127.0.0.1:81/PowerShell'
wrapper = 'ExampleWrapper'
script = 'Date'
param = dict(
    verify = False,
    method='GET',
    url = f"{POWERSHELL_WEBSERVICE_URL}/{wrapper}/{script}",
    headers = {'User-Agent':'pyapi','Depth':'3'},
)

responce = requests.request(**param).json()

result = dict(success = True, error = "", data = None,)
if responce.get('Success',False): # Net.CoreRuntime level (bool)
    streams = responce.get('Streams',{})
    if streams:
        had_errors = streams.get('HadErrors',False) # PowerShell level (bool)
        if had_errors:
            errors = streams.get('Errors',[])
            if errors:
                result['success'] = False
                result['error'] = errors
        else:
            psobjects = streams.get('PSObjects',[])
            if psobjects:
                result['data'] = psobjects
print(result)
```
**Python example POST request**
``` python
import requests, json, urllib3
urllib3.disable_warnings(urllib3.exceptions.InsecureRequestWarning)
POWERSHELL_WEBSERVICE_URL = 'http://127.0.0.1:81/PowerShell'
wrapper = 'ExampleWrapper'
script = 'Date'
param = dict(
    verify = False,
    method='POST',
    url = f"{POWERSHELL_WEBSERVICE_URL}/{wrapper}/{script}",
    headers = {'User-Agent':'pyapi','Depth':'3'},
)

responce = requests.request(**param, json={'Year':1900,'Month':1,'Day':1}).json()

result = dict(success = True, error = "", data = None,)
if responce.get('Success',False): # Net.CoreRuntime level (bool)
    streams = responce.get('Streams',{})
    if streams:
        had_errors = streams.get('HadErrors',False) # PowerShell level (bool)
        if had_errors:
            errors = streams.get('Errors',[])
            if errors:
                result['success'] = False
                result['error'] = errors
        else:
            psobjects = streams.get('PSObjects',[])
            if psobjects:
                result['data'] = psobjects
print(result)
```

***

## Docs

This service is designed to work as an internal service on a web server to simplify data exchange with PowerShell.

It is very simple because it is intended for those who find it difficult to understand C#, but knows other languages and wants to get the power of PowerShell.
### It's not worth publishing it for users! Instead, publish it to the address 127.0.0.1 or any other 127.0.0.*, if we are talking about Windows, yes, you can do that there :-)

After that, you can write a web service in your favorite language and use PowerShell Web Service inside it, it can be Go, Python, PHP, Perl, Java, JavaScript or something else at your discretion.
And please don't run any more PowerShell.exe and disassemble stdout, because it has 6 output streams under the hood instead of 2 and you can skip the most interesting :-)
See "Output Streams" below

## Run
- Example1: Get PowerShell Version
![img_get_version](https://github.com/sawfriendship/PowerShellWebService/raw/main/img/2023-02-08_13-21-10.png)
- Example2: Creating and sending a JSON string using the "Add" button
![img_post_params](https://github.com/sawfriendship/PowerShellWebService/raw/main/img/2023-02-08_13-19-14.png)
- Example3: Get PowerShell Error
![img_error](https://github.com/sawfriendship/PowerShellWebService/raw/main/img/2023-02-08_13-23-50.png)

### Links

- [About PowerShell Output Streams](https://learn.microsoft.com/en-us/powershell/module/microsoft.powershell.core/about/about_output_streams?view=powershell-7.3)
- [Publish an ASP.NET Core app to IIS](https://learn.microsoft.com/ru-ru/aspnet/core/tutorials/publish-to-iis?view=aspnetcore-7.0)
- [Tutorial: Create a minimal API with ASP.NET Core](https://learn.microsoft.com/ru-ru/aspnet/core/tutorials/min-web-api?view=aspnetcore-7.0)
- [ASP.NET documentation](https://learn.microsoft.com/ru-ru/aspnet/core/?view=aspnetcore-7.0)

***




