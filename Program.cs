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
using System.Data.SqlClient;

var WebAppConfig = new ConfigurationBuilder().AddJsonFile("_config.json", optional: true, reloadOnChange: true).Build();
var WebAppBuilder = Microsoft.AspNetCore.Builder.WebApplication.CreateBuilder(args);

WebAppBuilder.Logging.AddJsonConsole();
WebAppBuilder.Services.AddRazorPages();

await using var app = WebAppBuilder.Build();
string ResponseContentType = "application/json; charset=utf-8";
bool IsDevelopment = WebAppConfig.GetValue("IsDevelopment", false)!;
string DateTimeLogFormat = WebAppConfig.GetValue("DateTimeLogFormat", "yyyy-MM-dd HH:mm:ss")!;

app.Logger.LogInformation($"{DateTime.Now.ToString(DateTimeLogFormat)}, StartUp");

if (IsDevelopment) { app.UseExceptionHandler("/Error"); }
app.UseStaticFiles();
app.UseRouting();
app.UseAuthorization();
app.MapRazorPages();

string ROOT_DIR = AppContext.BaseDirectory;
string ScriptRoot = WebAppConfig.GetValue("ScriptRoot", Path.Join(ROOT_DIR, "_scripts"))!;
var ScriptCache = new Dictionary<String, Dictionary<String, Dictionary<String, object>>>();
List<string> CachedVariables = WebAppConfig.GetSection("CachedVariables").GetChildren().ToArray().Select(x => x.Value!.ToString()).ToList();
var PSRunspaceVariables = WebAppConfig.GetSection("Variables").GetChildren().ToList();
bool SqlLoggingEnabled = WebAppConfig.GetValue("SqlLogging:Enabled", false);
bool AbortScriptOnSqlFailure = WebAppConfig.GetValue("SqlLogging:AbortScriptOnFailure", true);
string SqlConnectionString = WebAppConfig.GetValue("SqlLogging:ConnectionString", "")!;
string SqlTable = WebAppConfig.GetValue("SqlLogging:Table", "Log")!;

if (SqlLoggingEnabled) {
    OrderedDictionary SqlLog = new OrderedDictionary();
    string SqlQuery = $"IF OBJECT_ID(N'[{SqlTable}]') IS NULL CREATE TABLE {SqlTable} ( [id] [bigint] IDENTITY(1,1) NOT NULL PRIMARY KEY CLUSTERED, [BeginDate] [datetime] NOT NULL DEFAULT (GETDATE()), [EndDate] [datetime] NULL, [Method] [nvarchar](16) NULL, [Wrapper] [nvarchar](256) NULL, [Script] [nvarchar](256) NULL, [Body] [text] NULL, [Error] [nvarchar](512) NULL, [Success] [bit] NULL, [HadErrors] [bit] NULL, [PSObjects] [text] NULL, [StreamError] [text] NULL, [StreamWarning] [text] NULL, [StreamVerbose] [text] NULL, [StreamInformation] [text] NULL )";
    IvokeSqlWithParam(SqlConnectionString,SqlQuery,SqlLog);
}

ScriptLoader();

app.Map("/whoami", async (HttpContext Context) =>
    {
        var UserInfo = new Dictionary<String, Dictionary<String, object>>();
        UserInfo["User"] = new Dictionary<String, object>();
        UserInfo["User"]["Identity"] = Context.User.Identity;
        var result = ConvertToJson(UserInfo);
        await Context.Response.WriteAsync(result);
    }
);

app.Map("/PowerShell/", async (HttpContext Context) =>
    {
        var WrapperDict = ScriptCache.ToDictionary(x => x.Key, x => x.Value.Keys.ToList());
        string OutputString = ConvertToJson(WrapperDict,1);
        Context.Response.Headers["Content-Type"] = ResponseContentType;
        await Context.Response.WriteAsync(OutputString);
    }
);

app.Map("/PowerShell/{Wrapper}", async (string Wrapper, HttpContext Context) =>
    {
        List<string> Scripts = new List<string>();
        if (ScriptCache.ContainsKey(Wrapper)) {Scripts = ScriptCache[Wrapper].Keys.ToList();}
        string OutputString = ConvertToJson(Scripts,1);
        Context.Response.Headers["Content-Type"] = ResponseContentType;
        await Context.Response.WriteAsync(OutputString);
    }
);

app.Map("/PowerShell/{Wrapper}/{Script}", async (string Wrapper, string Script, HttpContext Context) =>
    {
        Dictionary<String, String> Query = Context.Request.Query.ToDictionary(x => x.Key.ToUpper().ToString(), x => x.Value.ToString());
        Dictionary<String, String> Headers = Context.Request.Headers.ToDictionary(x => x.Key.ToUpper().ToString(), x => x.Value.ToString());

        int Depth = WebAppConfig.GetValue("Depth", 4);
        if (Headers.ContainsKey("DEPTH")) { if (int.TryParse(Headers["DEPTH"], out int Depth_)) { Depth = Depth_; } }

        using var streamReader = new StreamReader(Context.Request.Body, encoding: System.Text.Encoding.UTF8);
        string Body = await streamReader.ReadToEndAsync();
        string pwsh_result = PSScriptRunner(Wrapper, Script, Query, Body, Depth, Context);
        Context.Response.Headers["Content-Type"] = ResponseContentType;
        await Context.Response.WriteAsync(pwsh_result);
    }
);

app.Map("/PowerShell/reload", async (HttpContext Context) =>
    {
        ScriptLoader();
        await Context.Response.WriteAsync("ok");
    }
);

app.Map("/PowerShell/clear", async (HttpContext Context) =>
    {
        ClearCache();
        await Context.Response.WriteAsync("ok");
    }
);

string PSScriptRunner(string Wrapper, string Script, Dictionary<String, String> Query, string Body, int Depth, HttpContext Context) {
    int SqlLogID = 0;
    if (SqlLoggingEnabled) {
        OrderedDictionary SqlLog = new OrderedDictionary();
        SqlLog["Method"] = Context.Request.Method;
        SqlLog["Wrapper"] = Wrapper;
        SqlLog["Script"] = Script;
        SqlLog["Body"] = Body;
        string SqlQuery = $"INSERT INTO {SqlTable} ([Method],[Wrapper],[Script],[Body]) OUTPUT INSERTED.ID VALUES(@Method,@Wrapper,@Script,@Body)";
        SqlLogID = IvokeSqlWithParam(SqlConnectionString,SqlQuery,SqlLog);
    }

    var PSObjects = new Collection<PSObject>();
    OrderedDictionary ResultTable = new OrderedDictionary();
    OrderedDictionary Streams = new OrderedDictionary();
    OrderedDictionary StateInfo = new OrderedDictionary();
    bool success = true;
    bool HadErrors = false;
    string error = "";
    string PSOutputString = "";
    var ErrorList = new List<Object>();
    var WarningList = new List<Object>();
    var VerboseList = new List<Object>();
    var InformationList = new List<Object>();

    string WrapperFile = Path.Join(ScriptRoot, Wrapper, "wrapper.ps1");
    string ScriptFile = Path.Join(ScriptRoot, Wrapper, "scripts", $"{Script}.ps1");


    if (AbortScriptOnSqlFailure && SqlLogID == 0) {
        success = false;
        error = $"SQL Failure";
    } else if (!ScriptCache.ContainsKey(Wrapper)) {
        success = false;
        error = $"Wrapper '{Wrapper}' not found in cache, use {Context.Request.Host}/PowerShell/reload for load new scripts or wrappers and {Context.Request.Host}/PowerShell/clear for clear all";
    } else if (!ScriptCache[Wrapper].ContainsKey(Script)) {
        success = false;
        error = $"Script '{Script}' not found in cache, use {Context.Request.Host}/PowerShell/reload for load new scripts or wrappers and {Context.Request.Host}/PowerShell/clear for clear all";
    } else if (!File.Exists(WrapperFile)) {
        success = false;
        error = $"Wrapper '{Wrapper}' not found on disk, use {Context.Request.Host}/PowerShell/reload for load new scripts or wrappers and {Context.Request.Host}/PowerShell/clear for clear all";
    } else if (!File.Exists(ScriptFile)) {
        success = false;
        error = $"Script '{Script}' not found on disk, use {Context.Request.Host}/PowerShell/reload for load new scripts or wrappers and {Context.Request.Host}/PowerShell/clear for clear all";
    } else {
        var initialSessionState = System.Management.Automation.Runspaces.InitialSessionState.CreateDefault();
        string conf_ExecPol = WebAppConfig.GetValue("ExecutionPolicy", "Unrestricted")!;
        var ExecPol = Enum.Parse(typeof(ExecutionPolicy), conf_ExecPol);
        initialSessionState.ExecutionPolicy = (ExecutionPolicy)ExecPol;
        var PSRunspace = System.Management.Automation.Runspaces.RunspaceFactory.CreateRunspace(initialSessionState);
        PSRunspace.Open();

        foreach (var PSRunspaceVariable_ in PSRunspaceVariables) {
            PSRunspace.SessionStateProxy.SetVariable(PSRunspaceVariable_.Key, PSRunspaceVariable_.Value);
        }

        PowerShell PwSh = PowerShell.Create();
        PwSh.Runspace = PSRunspace;
        
        foreach (string CachedVariable_ in CachedVariables) {
            PwSh.Runspace.SessionStateProxy.SetVariable(CachedVariable_, ScriptCache[Wrapper][Script][CachedVariable_]);
        }
        
        PwSh.AddCommand(WrapperFile);
        PwSh.AddParameter("ScriptFile", ScriptFile);
        PwSh.AddParameter("Query", Query);
        PwSh.AddParameter("Body", Body);
        PwSh.AddParameter("Context", Context);
        try {
            DateTime BeginDate = DateTime.Now;
            PSObjects = PwSh.Invoke();
            DateTime EndDate = DateTime.Now;
            
            Streams.Add("PSObjects", PSObjects);
            HadErrors = PwSh.HadErrors;
            Streams.Add("HadErrors", HadErrors);

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
            
            StateInfo["Duration"] = (EndDate - BeginDate).TotalSeconds;
            StateInfo["State"] = $"{PwSh.InvocationStateInfo.State}";
            StateInfo["Reason"] = $"{PwSh.InvocationStateInfo.Reason}";

        } catch (Exception e) {
            error = $"{e.Message}";
            success = false;
        }

        try {
            foreach (string CachedVariable_ in CachedVariables) {
                ScriptCache[Wrapper][Script][CachedVariable_] = PwSh.Runspace.SessionStateProxy.GetVariable(CachedVariable_);
            }
        } finally {
            PwSh.Runspace.Close();
        }

    }

    try
    {
        ResultTable["Success"] = success;
        ResultTable["Error"] = error;
        ResultTable["Streams"] = Streams;
        ResultTable["InvocationStateInfo"] = StateInfo;
        PSOutputString = ConvertToJson(ResultTable,Depth,RaiseError:true);
    } catch (Exception e) {
        Streams.Clear();
        success = false;
        ResultTable["Success"] = success;
        ResultTable["Error"] = $"JSON serialization error: {e.ToString()}";
        ResultTable["Streams"] = Streams;
        ResultTable["InvocationStateInfo"] = StateInfo;
        PSOutputString = ConvertToJson(ResultTable,Depth,RaiseError:false);
    }

    if (SqlLoggingEnabled && SqlLogID > 0) {
        string PSObjectsJson = ConvertToJson(PSObjects,4,true,true,false);
        string ErrorListJson = ConvertToJson(ErrorList,3,true,true,false);
        string WarningListJson = ConvertToJson(WarningList,3,true,true,false);
        string InformationListJson = ConvertToJson(InformationList,3,true,true,false);
        string VerboseListJson = ConvertToJson(VerboseList,3,true,true,false);

        OrderedDictionary SqlLog = new OrderedDictionary();

        SqlLog["EndDate"] = DateTime.Now;
        SqlLog["HadErrors"] = HadErrors;
        SqlLog["Error"] = error;
        SqlLog["Success"] = success;
        SqlLog["PSObjects"] = PSObjectsJson;
        SqlLog["StreamError"] = ErrorListJson;
        SqlLog["StreamWarning"] = WarningListJson;
        SqlLog["StreamInformation"] = InformationListJson;
        SqlLog["StreamVerbose"] = VerboseListJson;
        string SqlQuery = $"UPDATE {SqlTable} SET [EndDate]=@EndDate,[Success]=@Success,[PSObjects]=@PSObjects,[StreamError]=@StreamError,[StreamWarning]=@StreamWarning,[StreamInformation]=@StreamInformation,[StreamVerbose]=@StreamVerbose,[HadErrors]=@HadErrors,[Error]=@Error WHERE ID = {SqlLogID}";
        IvokeSqlWithParam(SqlConnectionString,SqlQuery,SqlLog);
    }

    return PSOutputString;

}

string ConvertToJson(object data, int maxDepth = 4, bool enumsAsStrings = true, bool compressOutput = false, bool RaiseError = false) {
    JsonObject.ConvertToJsonContext jsonContext = new JsonObject.ConvertToJsonContext(maxDepth: maxDepth, enumsAsStrings: enumsAsStrings, compressOutput: compressOutput);
    string Result = "[]";
    try {
        Result = JsonObject.ConvertToJson(data, jsonContext);
    } catch (Exception e) {
        if (RaiseError) {
            app.Logger.LogError($"{DateTime.Now.ToString(DateTimeLogFormat)}, ConvertToJson Error: '{e}'");
            throw;
        }
    }
    return Result;
}

int IvokeSqlWithParam(string ConnectionString, string Query, OrderedDictionary Params) {
    SqlConnection Connection = new SqlConnection(ConnectionString);
    SqlCommand Command = new SqlCommand(Query, Connection);
    int id = 0;
    foreach (string Param_ in Params.Keys) {
        Command.Parameters.AddWithValue($"@{Param_.TrimStart('@')}",Params[Param_]);
    }
    try {
        Connection.Open();
        using (SqlDataReader DataReader = Command.ExecuteReader()) {
            if (DataReader.Read()) {
                id = Convert.ToInt32(DataReader["id"]);
            }
        }
        app.Logger.LogInformation($"SQL Write Success, id: {id}");
    } catch (Exception e) {
        app.Logger.LogError($"SQL Write Error: {e}");
    } finally {
        Connection.Close();
    }

    return id;
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
            throw;
        } else {
            return new List<String>();
        }
    }
}

Dictionary<String, List<String>> ScriptLoader() {
    var results = new Dictionary<String, List<String>>();
    var DirectoryInfo = new DirectoryInfo(ScriptRoot);
    var Wrappers = DirectoryInfo.GetDirectories().Where(x => File.Exists(Path.Join(ScriptRoot,x.Name,"wrapper.ps1"))).Select(x => x.Name).ToList();
    foreach (string Wrapper_ in Wrappers) {
        if (!ScriptCache.ContainsKey(Wrapper_)) {ScriptCache[Wrapper_] = new Dictionary<String, Dictionary<String, object>>();}
        string ScriptPath = Path.Join(ScriptRoot,Wrapper_,"scripts");
        var Scripts = SearchFiles(ScriptPath,"*.ps1",false);
        results[Wrapper_] = Scripts;
        foreach(string Script_ in Scripts) {
            if (!ScriptCache[Wrapper_].ContainsKey(Script_)) {ScriptCache[Wrapper_][Script_] = new Dictionary<String, object>();}
            foreach(string CachedVariable_ in CachedVariables) {
                if (!ScriptCache[Wrapper_][Script_].ContainsKey(CachedVariable_)) {ScriptCache[Wrapper_][Script_][CachedVariable_] = null!;}
            }
        }
    }
    return results;
}
 
void ClearCache() {
    ScriptCache = new Dictionary<String, Dictionary<String, Dictionary<String, object>>>();
    var DirectoryInfo = new DirectoryInfo(ScriptRoot);
    var Wrappers = DirectoryInfo.GetDirectories().Where(x => File.Exists(Path.Join(ScriptRoot,x.Name,"wrapper.ps1"))).Select(x => x.Name).ToList();
    foreach(string Wrapper_ in Wrappers) {
        List<string> Scripts = SearchFiles(Path.Join(ScriptRoot,Wrapper_,"scripts"),"*.ps1",false);
        ScriptCache[Wrapper_] = new Dictionary<String, Dictionary<String, object>>();
        foreach(string Script_ in Scripts) {
            ScriptCache[Wrapper_][Script_] = new Dictionary<String, object>();
            foreach(string CachedVariable_ in CachedVariables) {
                ScriptCache[Wrapper_][Script_][CachedVariable_] = null!;
            }
        }
    }
}

await app.RunAsync();

