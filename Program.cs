using System;
using System.IO;
using System.Text;
using System.Configuration;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using Microsoft.PowerShell;
using Microsoft.PowerShell.Commands;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;

string ROOT_DIR = AppContext.BaseDirectory;
var WebAppBuilder = Microsoft.AspNetCore.Builder.WebApplication.CreateBuilder(args);

WebAppBuilder.Logging.AddJsonConsole();
WebAppBuilder.Services.AddRazorPages();
await using var app = WebAppBuilder.Build();
var WebAppConfig = new ConfigurationBuilder().AddJsonFile("conf.json", optional: true, reloadOnChange: true).Build();

bool IsDevelopment = WebAppConfig.GetValue("IsDevelopment", false);
string DateTimeLogFormat = WebAppConfig.GetValue("DateTimeLogFormat", "yyyy-MM-dd HH:mm:ss");

app.Logger.LogInformation($"{DateTime.Now.ToString(DateTimeLogFormat)}, StartUp");

if (IsDevelopment) { app.UseExceptionHandler("/Error"); }
app.UseStaticFiles();
app.UseRouting();
app.MapRazorPages();

app.Map("/PowerShell/{Wrapper}/{Script}", async (string Wrapper, string Script, HttpContext Context) =>
{
    Context.Response.Headers["Content-Type"] = "application/json; charset=utf-8";

    var Request = Context.Request;
    var Query = new Dictionary<String, String>();
    var Headers = new Dictionary<String, String>();

    foreach (string Key in Request.Query.Keys) { Query[Key.ToUpper()] = Request.Query[Key]; }
    foreach (string Key in Request.Headers.Keys) { Headers[Key.ToUpper()] = Request.Headers[Key]; }

    int Depth = WebAppConfig.GetValue("Depth", 4);

    if (Headers.ContainsKey("DEPTH")) { if (int.TryParse(Headers["DEPTH"], out int Depth_)) { Depth = Depth_; } }

    using var streamReader = new StreamReader(Request.Body, encoding: System.Text.Encoding.UTF8);
    string Body = await streamReader.ReadToEndAsync();
    string pwsh_result = PSScriptRunner(Wrapper, Script, Query, Body, Depth, Context);
    await Context.Response.WriteAsync(pwsh_result);
}
);

string PSScriptRunner(string Wrapper, string Script, Dictionary<String, String> Query, string Body, int Depth, HttpContext Context)
{
    var PSObjects = new Collection<PSObject>();
    OrderedDictionary ResultTable = new OrderedDictionary();
    OrderedDictionary Streams = new OrderedDictionary();

    bool success = true;
    string error = "";
    string PSOutputString = "";

    string WrapperDir = WebAppConfig.GetValue("Path:Wrappers", Path.Join(ROOT_DIR, "_wrappers"));
    string ScriptDir = WebAppConfig.GetValue("Path:Scripts", Path.Join(ROOT_DIR, "_scripts"));

    string WrapperFile = Path.Join(WrapperDir, $"{Wrapper}.ps1");
    string ScriptFile = Path.Join(ScriptDir, $"{Script}.ps1");

    if (File.Exists(WrapperFile) && File.Exists(ScriptFile))
    {

        InitialSessionState initialSessionState = InitialSessionState.CreateDefault();
        string conf_ExecPol = WebAppConfig.GetValue("ExecutionPolicy", "Unrestricted");
        var ExecPol = Enum.Parse(typeof(ExecutionPolicy), conf_ExecPol);
        initialSessionState.ExecutionPolicy = (ExecutionPolicy)ExecPol;

        Runspace PSRunspace = RunspaceFactory.CreateRunspace(initialSessionState);
        PSRunspace.Open();

        PSRunspace.SessionStateProxy.SetVariable("ErrorActionPreference", WebAppConfig.GetValue("ErrorActionPreference", "SilentlyContinue"));
        PSRunspace.SessionStateProxy.SetVariable("VerbosePreference", WebAppConfig.GetValue("VerbosePreference", "SilentlyContinue"));
        PSRunspace.SessionStateProxy.SetVariable("WarningPreference", WebAppConfig.GetValue("WarningPreference", "SilentlyContinue"));
        PSRunspace.SessionStateProxy.SetVariable("ErrorView", WebAppConfig.GetValue("ErrorView", "NormalView"));

        PowerShell PwSh = PowerShell.Create();
        PwSh.Runspace = PSRunspace;

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
            Streams.Add("Error", PwSh.Streams.Error);
            Streams.Add("Warning", PwSh.Streams.Warning);
            Streams.Add("Verbose", PwSh.Streams.Verbose);
            Streams.Add("Debug", PwSh.Streams.Debug);
            Streams.Add("Information", PwSh.Streams.Information);
        }
        catch (Exception e)
        {
            error = e.ToString();
            success = false;
        }

        try
        {
            PwSh.Runspace.Close();
            app.Logger.LogInformation($"RunspaceStateInfo:{PwSh.Runspace.RunspaceStateInfo.State.ToString()}");
        }
        catch
        {
            app.Logger.LogError($"RunspaceStateInfo:{PwSh.Runspace.RunspaceStateInfo.State.ToString()}");
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
        ResultTable.Add("Success", success);
        ResultTable.Add("Error", error);
        ResultTable.Add("Streams", Streams);
        JsonObject.ConvertToJsonContext jsonContext = new JsonObject.ConvertToJsonContext(maxDepth: Depth, enumsAsStrings: false, compressOutput: false);
        PSOutputString = JsonObject.ConvertToJson(ResultTable, jsonContext);
    }
    catch (Exception e)
    {
        Streams.Clear();
        ResultTable["Streams"] = Streams;
        ResultTable["Success"] = false;
        ResultTable["Error"] = $"JSON serialization error: {e.ToString()}";
        JsonObject.ConvertToJsonContext jsonContext = new JsonObject.ConvertToJsonContext(maxDepth: Depth, enumsAsStrings: false, compressOutput: false);
        PSOutputString = JsonObject.ConvertToJson(ResultTable, jsonContext);
    }

    return PSOutputString;

}

await app.RunAsync();
