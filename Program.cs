using System;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Configuration;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Collections.ObjectModel;
using Microsoft.PowerShell;
using Microsoft.PowerShell.Commands;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration.Json;
using System.Reflection;
using System.Linq;

string ROOT_DIR = AppContext.BaseDirectory;
var WebAppBuilder = Microsoft.AspNetCore.Builder.WebApplication.CreateBuilder(args);

WebAppBuilder.Logging.AddJsonConsole();
WebAppBuilder.Services.AddRazorPages();
await using var app = WebAppBuilder.Build();
var WebAppConfig = new ConfigurationBuilder().AddJsonFile("_config.json", optional: true, reloadOnChange: true).Build();

bool IsDevelopment = WebAppConfig.GetValue("IsDevelopment", false)!;
string DateTimeLogFormat = WebAppConfig.GetValue("DateTimeLogFormat", "yyyy-MM-dd HH:mm:ss")!;

app.Logger.LogInformation($"{DateTime.Now.ToString(DateTimeLogFormat)}, StartUp");

if (IsDevelopment) { app.UseExceptionHandler("/Error"); }

app.UseStaticFiles();
app.UseRouting();
app.MapRazorPages();

string WrapperDir = WebAppConfig.GetValue("Path:Wrappers", Path.Join(ROOT_DIR, "_wrappers"))!;
string ScriptDir = WebAppConfig.GetValue("Path:Scripts", Path.Join(ROOT_DIR, "_scripts"))!;
var VarCache = new Dictionary<String, Dictionary<String, Dictionary<String, object>>>();
List<string> Wrappers = SearchFiles(WrapperDir,"*.ps1",false);
List<string> Scripts = SearchFiles(ScriptDir,"*.ps1",false);
List<string> CachedVariables = WebAppConfig.GetSection("CachedVariables").GetChildren().ToArray().Select(x => x.Value!.ToString()).ToList();

foreach(string Wrapper_ in Wrappers) {
    VarCache[Wrapper_] = new Dictionary<String, Dictionary<String, object>>()!;
    foreach(string Script_ in Scripts) {
        VarCache[Wrapper_][Script_] = new Dictionary<String, object>()!;
        foreach(string CachedVariable_ in CachedVariables) {
            VarCache[Wrapper_][Script_][CachedVariable_] = null!;
        }
    }
}

app.Logger.LogInformation($"CachedVariables:{CachedVariables}");

app.Map("/PowerShell/{Wrapper}/{Script}", async (string Wrapper, string Script, HttpContext Context) =>
    {
        Context.Response.Headers["Content-Type"] = "application/json; charset=utf-8";

        var Request = Context.Request;
        var Query = new Dictionary<String, String>();
        var Headers = new Dictionary<String, String>();

        foreach (string Key in Request.Query.Keys) { Query[Key.ToUpper()] = Request.Query[Key]!; }
        foreach (string Key in Request.Headers.Keys) { Headers[Key.ToUpper()] = Request.Headers[Key]!; }

        int Depth = WebAppConfig.GetValue("Depth", 4);

        if (Headers.ContainsKey("DEPTH")) { if (int.TryParse(Headers["DEPTH"], out int Depth_)) { Depth = Depth_; } }

        using var streamReader = new StreamReader(Request.Body, encoding: System.Text.Encoding.UTF8);
        string Body = await streamReader.ReadToEndAsync();
        string pwsh_result = PSScriptRunner(Wrapper, Script, Query, Body, Depth, Context);
        await Context.Response.WriteAsync(pwsh_result);
    }
);

app.Map("/PowerShell/", async (HttpContext Context) =>
    {
        Context.Response.Headers["Content-Type"] = "application/json; charset=utf-8";
        OrderedDictionary ResultTable = new OrderedDictionary();
        List<String> WrapperList = new List<String>();
        List<String> ScriptList = new List<String>();

        bool success = true;
        string error = "";

        try
        {
            ScriptList = SearchFiles(ScriptDir, "*.ps1", true);
        }
        catch (Exception e)
        {
            success = false;
            error = e.ToString();
        }

        try
        {
            WrapperList = SearchFiles(WrapperDir, "*.ps1", true);
        }
        catch (Exception e)
        {
            success = false;
            error = e.ToString();
        }
        ResultTable["Success"] = success;
        ResultTable["Error"] = error;
        ResultTable["Wrappers"] = WrapperList;
        ResultTable["Scripts"] = ScriptList;

        JsonObject.ConvertToJsonContext jsonContext = new JsonObject.ConvertToJsonContext(maxDepth: 4, enumsAsStrings: false, compressOutput: false);
        string OutputString = JsonObject.ConvertToJson(ResultTable, jsonContext);
        await Context.Response.WriteAsync(OutputString);
    }
);

string PSScriptRunner(string Wrapper, string Script, Dictionary<String, String> Query, string Body, int Depth, HttpContext Context)
{
    var PSObjects = new Collection<PSObject>();
    OrderedDictionary ResultTable = new OrderedDictionary();
    OrderedDictionary Streams = new OrderedDictionary();
    OrderedDictionary StateInfo = new OrderedDictionary();

    bool success = true;
    string error = "";
    string PSOutputString = "";

    string WrapperFile = Path.Join(WrapperDir, $"{Wrapper}.ps1");
    string ScriptFile = Path.Join(ScriptDir, $"{Script}.ps1");

    if (File.Exists(WrapperFile) && File.Exists(ScriptFile))
    {
        var initialSessionState = System.Management.Automation.Runspaces.InitialSessionState.CreateDefault();
        string conf_ExecPol = WebAppConfig.GetValue("ExecutionPolicy", "Unrestricted")!;
        var ExecPol = Enum.Parse(typeof(ExecutionPolicy), conf_ExecPol);
        initialSessionState.ExecutionPolicy = (ExecutionPolicy)ExecPol;
        var PSRunspace = System.Management.Automation.Runspaces.RunspaceFactory.CreateRunspace(initialSessionState);
        PSRunspace.Open();

        PSRunspace.SessionStateProxy.SetVariable("ErrorActionPreference", WebAppConfig.GetValue("ErrorActionPreference", "SilentlyContinue"));
        PSRunspace.SessionStateProxy.SetVariable("VerbosePreference", WebAppConfig.GetValue("VerbosePreference", "SilentlyContinue"));
        PSRunspace.SessionStateProxy.SetVariable("WarningPreference", WebAppConfig.GetValue("WarningPreference", "SilentlyContinue"));
        PSRunspace.SessionStateProxy.SetVariable("DebugPreference", WebAppConfig.GetValue("DebugPreference", "SilentlyContinue"));
        PSRunspace.SessionStateProxy.SetVariable("ErrorView", WebAppConfig.GetValue("ErrorView", "NormalView"));
        PSRunspace.SessionStateProxy.SetVariable("FormatEnumerationLimit", WebAppConfig.GetValue("FormatEnumerationLimit", 10));
        PSRunspace.SessionStateProxy.SetVariable("OFS", WebAppConfig.GetValue("OFS", ","));
        
        PowerShell PwSh = PowerShell.Create();
        PwSh.Runspace = PSRunspace;

        foreach (string CachedVariable_ in CachedVariables) {
            PwSh.Runspace.SessionStateProxy.SetVariable(CachedVariable_, VarCache[Wrapper][Script][CachedVariable_]);
        }
        
        PwSh.AddCommand(WrapperFile);
        PwSh.AddParameter("ScriptFile", ScriptFile);
        PwSh.AddParameter("Query", Query);
        PwSh.AddParameter("Body", Body);
        PwSh.AddParameter("Context", Context);

        try
        {
            PSObjects = PwSh.Invoke();
            
            Streams.Add("PSObjects", PSObjects);
            Streams.Add("HadErrors", PwSh.HadErrors);
            
            var ErrorList = new List<Object>();
            var WarningList = new List<Object>();
            var VerboseList = new List<Object>();
            var InformationList = new List<Object>();

            foreach (var Stream in PwSh.Streams.Error) {
                var StreamDictionary = new Dictionary<String, Object>();
                var InvocationInfo = Stream.InvocationInfo;
                var ExceptionInfo = Stream.Exception;
                var NewExceptionInfo = new Dictionary<String, Object>();
                var NewInvocationInfo = new Dictionary<String, Object>();
                NewInvocationInfo["ScriptName"] = $"{InvocationInfo.ScriptName}";
                NewInvocationInfo["ScriptLineNumber"] = InvocationInfo.ScriptLineNumber;
                NewInvocationInfo["Line"] = InvocationInfo.Line;
                NewInvocationInfo["PositionMessage"] = $"{InvocationInfo.PositionMessage}";
                NewInvocationInfo["PipelineLength"] = $"{InvocationInfo.PipelineLength}";
                NewInvocationInfo["PipelinePosition"] = $"{InvocationInfo.PipelinePosition}";
                NewExceptionInfo["Message"] = $"{ExceptionInfo.Message}";
                NewExceptionInfo["Source"] = $"{ExceptionInfo.Source}";
                StreamDictionary["Exception"] = NewExceptionInfo;
                StreamDictionary["InvocationInfo"] = NewInvocationInfo;
                StreamDictionary["TargetObject"] = $"{Stream.TargetObject}";
                StreamDictionary["FullyQualifiedErrorId"] = $"{Stream.FullyQualifiedErrorId}";
                ErrorList.Add(StreamDictionary);
            }

            foreach (var Stream in PwSh.Streams.Warning) {
                var StreamDictionary = new Dictionary<String, Object>();
                var NewInvocationInfo = new Dictionary<String, Object>();
                var InvocationInfo = Stream.InvocationInfo;
                NewInvocationInfo["Source"] = $"{InvocationInfo.MyCommand.Source}";
                NewInvocationInfo["PositionMessage"] = $"{InvocationInfo.PositionMessage}";
                StreamDictionary["Message"] = Stream.Message;
                StreamDictionary["InvocationInfo"] = NewInvocationInfo;
                WarningList.Add(StreamDictionary);
            }

            foreach (var Stream in PwSh.Streams.Verbose) {
                var StreamDictionary = new Dictionary<String, Object>();
                var NewInvocationInfo = new Dictionary<String, Object>();
                var InvocationInfo = Stream.InvocationInfo;
                NewInvocationInfo["Source"] = $"{InvocationInfo.MyCommand.Source}";
                NewInvocationInfo["PositionMessage"] = $"{InvocationInfo.PositionMessage}";
                StreamDictionary["Message"] = Stream.Message;
                StreamDictionary["InvocationInfo"] = NewInvocationInfo;
                VerboseList.Add(StreamDictionary);
            }

            foreach (var Stream in PwSh.Streams.Information) {
                var StreamDictionary = new Dictionary<String, Object>();
                StreamDictionary["Source"] = $"{Stream.Source}";
                StreamDictionary["TimeGenerated"] = $"{Stream.TimeGenerated}";
                StreamDictionary["MessageData"] = Stream.MessageData;
                InformationList.Add(StreamDictionary);
            }

            Streams["Error"] = ErrorList;
            Streams["Warning"] = WarningList;
            Streams["Verbose"] = VerboseList;
            Streams["Information"] = InformationList;
            Streams["Debug"] = PwSh.Streams.Debug;
            
            StateInfo["State"] = $"{PwSh.InvocationStateInfo.State}";
            StateInfo["StateCode"] = PwSh.InvocationStateInfo.State;
            StateInfo["Reason"] = $"{PwSh.InvocationStateInfo.Reason}";
        }
        catch (Exception e)
        {
            error = $"{e.Message}";
            success = false;
        }

        try
        {
            foreach (string CachedVariable_ in CachedVariables) {
                VarCache[Wrapper][Script][CachedVariable_] = PwSh.Runspace.SessionStateProxy.GetVariable(CachedVariable_);
            }

            PwSh.Runspace.Close();

            app.Logger.LogInformation($"RunspaceStateInfo:{PwSh.Runspace.RunspaceStateInfo.State}");
        }
        catch
        {
            app.Logger.LogError($"RunspaceStateInfo:{PwSh.Runspace.RunspaceStateInfo.State}");
        }

    }
    else
    {
        if (!File.Exists(ScriptFile))
        {
            error = $"Script '{Script}.ps1' not found in dir '{ScriptDir}'";
            success = false;
        }

        if (!File.Exists(WrapperFile))
        {
            error = $"Wrapper '{Wrapper}.ps1' not found in dir '{WrapperDir}'";
            success = false;
        }

    }


    if (success)
    {
        app.Logger.LogInformation($"{DateTime.Now.ToString(DateTimeLogFormat)}, Success run Script '{Script}.ps1' with Wrapper '{Wrapper}.ps1'");
    }
    else
    {
        app.Logger.LogError($"{DateTime.Now.ToString(DateTimeLogFormat)}, Script Error: '{error}'");
    }

    try
    {
        ResultTable["Success"] = success;
        ResultTable["Error"] = error;
        ResultTable["Streams"] = Streams;
        ResultTable["InvocationStateInfo"] = StateInfo;
        JsonObject.ConvertToJsonContext jsonContext = new JsonObject.ConvertToJsonContext(maxDepth: Depth, enumsAsStrings: false, compressOutput: false);
        PSOutputString = JsonObject.ConvertToJson(ResultTable, jsonContext);
    }
    catch (Exception e)
    {
        Streams.Clear();
        ResultTable["Success"] = false;
        ResultTable["Error"] = $"JSON serialization error: {e.ToString()}";
        ResultTable["Streams"] = Streams;
        ResultTable["InvocationStateInfo"] = StateInfo;
        JsonObject.ConvertToJsonContext jsonContext = new JsonObject.ConvertToJsonContext(maxDepth: Depth, enumsAsStrings: false, compressOutput: false);
        PSOutputString = JsonObject.ConvertToJson(ResultTable, jsonContext);
    }

    return PSOutputString;

}

List<String> SearchFiles(string Path, string Extension, bool RaiseError) {
    try {
        if (Directory.Exists(Path)) {
            var DirectoryInfo = new DirectoryInfo(Path);
            return DirectoryInfo.GetFiles(Extension).Select(x => x.Name.Replace(x.Extension,"")).ToList();
        } else {
            if (RaiseError) {
                throw new Exception($"Directory {Path} not found");
            } else {
                return new List<String>();
            }
        }
    } catch (Exception e) {
        if (RaiseError) {
            throw e;
        } else {
            return new List<String>();
        }
    }
}

await app.RunAsync();
