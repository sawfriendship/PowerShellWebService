
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
using System.Data.Odbc;
using System.Data.Common;

string ROOT_DIR = AppContext.BaseDirectory;

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

string ScriptRoot = WebAppConfig.GetValue("ScriptRoot", Path.Join(ROOT_DIR, "_scripts"))!;
var ScriptCache = new Dictionary<String, Dictionary<String, Dictionary<String, object>>>();
var CachedVariables = WebAppConfig.GetSection("CachedVariables").GetChildren().ToArray().Select(x => x.Value!.ToString()).ToList();
var PSRunspaceVariables = WebAppConfig.GetSection("Variables").GetChildren().ToList();
bool SqlLoggingEnabled = WebAppConfig.GetValue("SqlLogging:Enabled", false);
bool AbortScriptOnSqlFailure = WebAppConfig.GetValue("SqlLogging:AbortScriptOnFailure", true);
string SqlConnectionString = WebAppConfig.GetValue("SqlLogging:ConnectionString", "")!;
string SqlTable = WebAppConfig.GetValue("SqlLogging:Table", "Log")!;

if (SqlLoggingEnabled) {
    string SqlQuery = $"IF OBJECT_ID(N'[{SqlTable}]') IS NULL CREATE TABLE {SqlTable} ( [id] [bigint] IDENTITY(1,1) NOT NULL PRIMARY KEY CLUSTERED, [BeginDate] [datetime] NOT NULL DEFAULT (GETDATE()), [EndDate] [datetime] NULL, [UserName] [nvarchar](64) NULL, [IPAddress] [nvarchar](64) NULL, [Method] [nvarchar](16) NULL, [Wrapper] [nvarchar](256) NULL, [Script] [nvarchar](256) NULL, [Body] [text] NULL, [Error] [nvarchar](512) NULL, [Success] [bit] NULL, [HadErrors] [bit] NULL, [PSObjects] [text] NULL, [StreamError] [text] NULL, [StreamWarning] [text] NULL, [StreamVerbose] [text] NULL, [StreamInformation] [text] NULL )";
    var Connection = new OdbcConnection(SqlConnectionString);
    var Command = new OdbcCommand(SqlQuery, Connection);
    System.Data.Odbc.OdbcDataAdapter DataAdapter = new();
    DataAdapter.SelectCommand = Command;
    System.Data.DataSet DataSet = new();
    DataAdapter.Fill(DataSet);
}

ScriptLoader();

app.Map("/whoami", async (HttpContext Context) =>
    {
        var Headers = Context.Request.Headers.ToDictionary(x => x.Key, x => x.Value.ToString());
        if (Headers.ContainsKey("Authorization")) { Headers["Authorization"] = "***"; }

        Dictionary<String, object> UserInfo = new()
        {
            ["Host"] = Context.Request.Host,
            ["Headers"] = Headers,
            ["Connection"] = Context.Connection,
            ["User"] = Context.User,
        };

        var result = ConvertToJson(UserInfo);
        Context.Response.Headers["Content-Type"] = ResponseContentType;
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
        List<string> Scripts = new();
        if (ScriptCache.ContainsKey(Wrapper)) {Scripts = ScriptCache[Wrapper].Keys.ToList();}
        string OutputString = ConvertToJson(Scripts,1);
        Context.Response.Headers["Content-Type"] = ResponseContentType;
        await Context.Response.WriteAsync(OutputString);
    }
);

app.Map("/PowerShell/{Wrapper}/{Script}", async (string Wrapper, string Script, HttpContext Context) =>
    {
        var Query = Context.Request.Query.ToDictionary(x => x.Key.ToUpper().ToString(), x => x.Value.ToString());
        var Headers = Context.Request.Headers.ToDictionary(x => x.Key.ToUpper().ToString(), x => x.Value.ToString());

        int Depth = WebAppConfig.GetValue("Depth", 4);
        if (Headers.ContainsKey("DEPTH")) { if (int.TryParse(Headers["DEPTH"], out int Depth_)) { Depth = Depth_; } }

        var streamReader = new StreamReader(Context.Request.Body, encoding: System.Text.Encoding.UTF8);
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
    Dictionary<string,object> SqlLogOutput = new();
    Collection<PSObject> PSObjects = new();
    OrderedDictionary ResultTable = new();
    OrderedDictionary Streams = new();
    OrderedDictionary StateInfo = new();
    List<Object> ErrorList = new();
    List<Object> WarningList = new();
    List<Object> VerboseList = new();
    List<Object> InformationList = new();
    bool success = true;
    bool HadErrors = false;
    string error = "";
    string PSOutputString = "";
    string WrapperFile = Path.Join(ScriptRoot, Wrapper, "wrapper.ps1");
    string ScriptFile = Path.Join(ScriptRoot, Wrapper, "scripts", $"{Script}.ps1");

    if (SqlLoggingEnabled) {
        Dictionary<string,object> SqlLogParam = new()
        {
            ["Method"] = Context.Request.Method,
            ["Wrapper"] = Wrapper,
            ["Script"] = Script,
            ["Body"] = Body,
            ["IPAddress"] = $"{Context.Connection.RemoteIpAddress}",
        };
        if (Context.User.Identity.Name is not null) {
            SqlLogParam["UserName"] = Context.User.Identity.Name;
        }
        try {
            SqlLogOutput = SqlHelper(SqlTable,SqlLogParam,"INSERT",SqlConnectionString,PrimaryKey:"id");
        } catch (Exception e) {
            Console.WriteLine(e.ToString());
        }
    }



    if (SqlLoggingEnabled && AbortScriptOnSqlFailure && SqlLogOutput.Count < 1) {
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

        foreach (var _ in PSRunspaceVariables) {
            PSRunspace.SessionStateProxy.SetVariable(_.Key, _.Value);
        }

        PowerShell PwSh = PowerShell.Create();
        PwSh.Runspace = PSRunspace;
        
        foreach (string _ in CachedVariables) {
            PwSh.Runspace.SessionStateProxy.SetVariable(_, ScriptCache[Wrapper][Script][_]);
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
                var InvocationInfo = Stream.InvocationInfo;
                var ExceptionInfo = Stream.Exception;
                Dictionary<String, Object> NewInvocationInfo = new()
                {
                    ["ScriptName"] = $"{InvocationInfo.ScriptName}",
                    ["ScriptLineNumber"] = InvocationInfo.ScriptLineNumber,
                    ["Line"] = InvocationInfo.Line,
                    ["PositionMessage"] = $"{InvocationInfo.PositionMessage}",
                    ["PipelineLength"] = $"{InvocationInfo.PipelineLength}",
                    ["PipelinePosition"] = $"{InvocationInfo.PipelinePosition}"
                };
                Dictionary<String, Object> NewExceptionInfo = new()
                {
                    ["Message"] = $"{ExceptionInfo.Message}",
                    ["Source"] = $"{ExceptionInfo.Source}"
                };
                Dictionary<String, Object> StreamDictionary = new()
                {
                    ["Exception"] = NewExceptionInfo,
                    ["InvocationInfo"] = NewInvocationInfo,
                    ["TargetObject"] = $"{Stream.TargetObject}",
                    ["FullyQualifiedErrorId"] = $"{Stream.FullyQualifiedErrorId}"
                };
                ErrorList.Add(StreamDictionary);
            }
            
            foreach (var Stream in PwSh.Streams.Warning) {
                var InvocationInfo = Stream.InvocationInfo;
                Dictionary<String, Object> NewInvocationInfo = new()
                {
                    ["Source"] = $"{InvocationInfo.MyCommand.Source}",
                    ["PositionMessage"] = $"{InvocationInfo.PositionMessage}"
                };
                Dictionary<String, Object> StreamDictionary = new()
                {
                    ["Message"] = Stream.Message,
                    ["InvocationInfo"] = NewInvocationInfo
                };
                WarningList.Add(StreamDictionary);
            }

            foreach (var Stream in PwSh.Streams.Verbose) {
                var InvocationInfo = Stream.InvocationInfo;
                Dictionary<String, Object> NewInvocationInfo = new()
                {
                    ["Source"] = $"{InvocationInfo.MyCommand.Source}",
                    ["PositionMessage"] = $"{InvocationInfo.PositionMessage}"
                };
                Dictionary<String, Object> StreamDictionary = new()
                {
                    ["Message"] = Stream.Message,
                    ["InvocationInfo"] = NewInvocationInfo
                };
                VerboseList.Add(StreamDictionary);
            }

            foreach (var Stream in PwSh.Streams.Information) {
                Dictionary<String, Object> StreamDictionary = new()
                {
                    ["Source"] = $"{Stream.Source}",
                    ["TimeGenerated"] = $"{Stream.TimeGenerated}",
                    ["MessageData"] = Stream.MessageData
                };
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
            foreach (string _ in CachedVariables) {
                ScriptCache[Wrapper][Script][_] = PwSh.Runspace.SessionStateProxy.GetVariable(_);
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
        ResultTable["Error"] = $"JSON serialization error: {e}";
        ResultTable["Streams"] = Streams;
        ResultTable["InvocationStateInfo"] = StateInfo;
        PSOutputString = ConvertToJson(ResultTable,Depth,RaiseError:false);
    }

    if (SqlLoggingEnabled && SqlLogOutput.Count > 0) {
        string PSObjectsJson = ConvertToJson(PSObjects,4,true,true,false);
        string ErrorListJson = ConvertToJson(ErrorList,3,true,true,false);
        string WarningListJson = ConvertToJson(WarningList,3,true,true,false);
        string InformationListJson = ConvertToJson(InformationList,3,true,true,false);
        string VerboseListJson = ConvertToJson(VerboseList,3,true,true,false);

        Dictionary<string,object> SqlLogParam = new()
        {
            ["id"] = SqlLogOutput["id"],
            ["EndDate"] = DateTime.Now,
            ["Error"] = error,
            ["Success"] = success,
            ["HadErrors"] = HadErrors,
            ["PSObjects"] = PSObjectsJson,
            ["StreamError"] = ErrorListJson,
            ["StreamWarning"] = WarningListJson,
            ["StreamInformation"] = InformationListJson,
            ["StreamVerbose"] = VerboseListJson
        };

        SqlHelper(SqlTable,SqlLogParam,"UPDATE",SqlConnectionString,PrimaryKey:"id");
    }
    GC.Collect();
    
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

Dictionary<string,object> SqlHelper(string SqlTable, Dictionary<string,object> Params, string Operation, string ConnectionString, string PrimaryKey = "id") {
    Dictionary<string,object> result = new();
    List<string> Keys = Params.Select(x => x.Key).Where(x => x != PrimaryKey).ToList();
    string Query = "";
    switch (Operation.ToUpper())
    {
        case "INSERT":
            Query = $"INSERT INTO [{SqlTable.Trim('[',']')}] ({String.Join(',',Keys.Select(x => $"[{x}]"))}) OUTPUT INSERTED.* VALUES({String.Join(',',Keys.Select(x => "?"))})";
            break;
        case "UPDATE":
            if (!Params.ContainsKey(PrimaryKey)) {new Exception($"Params not contains the specified PrimaryKey: '{PrimaryKey}'");}
            Query = $"UPDATE [{SqlTable.Trim('[',']')}] SET {String.Join(',',Keys.Select(x => $"[{x}]=?"))} OUTPUT INSERTED.* WHERE [{PrimaryKey.Trim('[',']')}] = ?";
            break;
    }
    Console.WriteLine(Query);
    var Connection = new OdbcConnection(ConnectionString);
    var Command = new OdbcCommand(Query, Connection);
    foreach (string Key in Keys) {
        var Value = Params[Key];
        if (Value.GetType() == typeof(System.DateTime)) {
            Command.Parameters.AddWithValue(Key,Value).Scale = 7;
        } else {
           Command.Parameters.AddWithValue(Key,Value);
        }
    }
    if (Params.ContainsKey(PrimaryKey)) {Command.Parameters.AddWithValue(PrimaryKey,Params[PrimaryKey]);}

    System.Data.Odbc.OdbcDataAdapter DataAdapter = new();
    DataAdapter.SelectCommand = Command;
    System.Data.DataSet DataSet = new();
    DataAdapter.Fill(DataSet);
    if (!DataSet.HasErrors) {
        var Table = DataSet.Tables[0];
        var Columns = Table.Columns;
        var Row = Table.Rows[0];
        result = System.Linq.Enumerable.Range(0,Columns.Count).ToDictionary(x => Columns[x].ColumnName, x => Row[x]);
    }
    return result;
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
    } catch {
        if (RaiseError) {
            throw;
        } else {
            return new List<String>();
        }
    }
}

Dictionary<String, List<String>> ScriptLoader() {
    var DirectoryInfo = new DirectoryInfo(ScriptRoot);
    var Wrappers = DirectoryInfo.GetDirectories().Where(x => File.Exists(Path.Join(ScriptRoot,x.Name,"wrapper.ps1"))).Select(x => x.Name).ToList();
    Dictionary<String, List<String>> results = new();
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
    var DirectoryInfo = new DirectoryInfo(ScriptRoot);
    var Wrappers = DirectoryInfo.GetDirectories().Where(x => File.Exists(Path.Join(ScriptRoot,x.Name,"wrapper.ps1"))).Select(x => x.Name).ToList();
    Dictionary<String, Dictionary<String, Dictionary<String, object>>> ScriptCache = new();
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
