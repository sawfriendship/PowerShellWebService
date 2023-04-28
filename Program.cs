using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Data.Odbc;
using System.Data.Common;
using System.Security;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Collections.ObjectModel;
using System.Linq;
using System.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using System.Reflection;
using System.Diagnostics;
using Microsoft.PowerShell;
using Microsoft.PowerShell.Commands;
using System.Management.Automation;
using System.Management.Automation.Runspaces;

string ROOT_DIR = AppContext.BaseDirectory;

var WebAppBuilder = Microsoft.AspNetCore.Builder.WebApplication.CreateBuilder(args);
WebAppBuilder.Logging.AddJsonConsole();
WebAppBuilder.Services.AddRazorPages();
WebAppBuilder.Configuration
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddJsonFile($"appsettings.{System.Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")}.json", optional: true, reloadOnChange: true)
    .AddEnvironmentVariables();

await using var app = WebAppBuilder.Build();

string DateTimeLogFormat = app.Configuration.GetValue("DateTimeLogFormat", "yyyy-MM-dd HH:mm:ss")!;

Console.WriteLine($"{DateTime.Now.ToString(DateTimeLogFormat)}, StartUp");

string RESPONSE_CONTENT_TYPE = "application/json; charset=utf-8";
string ASPNETCORE_ENVIRONMENT = System.Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")!;
if (ASPNETCORE_ENVIRONMENT == "Development") { app.UseExceptionHandler("/Error"); }

app.UseStaticFiles();
app.UseRouting();
app.MapRazorPages();

string ScriptRoot = app.Configuration.GetValue("ScriptRoot", Path.Join(ROOT_DIR, ".scripts"))!;
var ScriptCache = new Dictionary<String, Dictionary<String, Dictionary<String, object>>>();
var CachedVariables = app.Configuration.GetSection("CachedVariables").GetChildren().ToArray().Select(x => x.Value!.ToString()).ToList();
var PSRunspaceVariables = app.Configuration.GetSection("Variables").GetChildren().ToList();
string UserCredentialVariable = app.Configuration.GetValue("UserCredentialVariable", "")!;
bool SqlLoggingEnabled = app.Configuration.GetValue("SqlLogging:Enabled", false);
bool AbortScriptOnSqlFailure = app.Configuration.GetValue("SqlLogging:AbortScriptOnFailure", true);
string SqlConnectionString = app.Configuration.GetValue("SqlLogging:ConnectionString", "")!;
string SqlTable = app.Configuration.GetValue("SqlLogging:Table", "Log")!;

var jOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = false };

if (SqlLoggingEnabled) {
    SqlTableCreate(SqlTable, SqlConnectionString);
}

ScriptLoader();

app.Map("/check", async (HttpContext Context) =>
    {
        bool test = true;
        try {
            app.Configuration.GetSection("ExecutionPolicy");
        } catch {
            test = false;
        }

        await Context.Response.WriteAsJsonAsync(new { Success = test }, jOptions);
    }
);

app.Map("/whoami", async (HttpContext Context) =>
    {
        Context.Response.Headers["Content-Type"] = RESPONSE_CONTENT_TYPE;
        var Headers = Context.Request.Headers.ToDictionary(x => x.Key, x => x.Value.ToString());
        string AuthorizationHeader = Context.Request.Headers.Where(x => x.Key.ToLower() == "authorization").Select(x => x.Key).FirstOrDefault("");
        if (Headers.ContainsKey(AuthorizationHeader)) { Headers[AuthorizationHeader] = "***"; }

        Dictionary<String, object> UserInfo = new()
        {
            ["Host"] = Context.Request.Host,
            ["Headers"] = Headers,
            ["Connection"] = Context.Connection,
            ["pid"] = Process.GetCurrentProcess().Id,
            ["User"] = Context.User,
        };

        string OutputString = ConvertToJson(UserInfo);
        await Context.Response.WriteAsync(OutputString);
    }
);

app.Map("/logoff", async (HttpContext Context) =>
    {
        Context.Response.StatusCode = 401;
        await Context.Response.WriteAsync("logoff");
    }
);

app.Map("/logout", async (HttpContext Context) =>
    {
        Context.Response.StatusCode = 401;
        await Context.Response.WriteAsync("logout");
    }
);

app.Map("/reload", async (HttpContext Context) =>
    {
        bool UserIsInRoleAdmin = app.Configuration.GetSection("Roles:Admin").GetChildren().ToList().Any(x => Context.User.IsInRole($"{x.Value}"));

        if (!UserIsInRoleAdmin) {
            await Context.Response.WriteAsJsonAsync(new { Success = false, Error = "access denied" }, jOptions);
        } else {
            ScriptLoader();
            await Context.Response.WriteAsJsonAsync(new { Success = true, Error = "" }, jOptions);
        }
    }
);

app.Map("/clear", async (HttpContext Context) =>
    {
        bool UserIsInRoleAdmin = app.Configuration.GetSection("Roles:Admin").GetChildren().ToList().Any(x => Context.User.IsInRole($"{x.Value}"));

        if (!UserIsInRoleAdmin) {
            await Context.Response.WriteAsJsonAsync(new { Success = false, Error = "access denied" }, jOptions);
        } else {
            ClearCache();
            await Context.Response.WriteAsJsonAsync(new { Success = true, Error = "" }, jOptions);
        }
    }
);

app.Map("/PowerShell/", async (HttpContext Context) =>
    {
        bool UserIsInRoleAdmin = app.Configuration.GetSection("Roles:Admin").GetChildren().ToList().Any(x => Context.User.IsInRole($"{x.Value}"));
        bool UserIsInRoleUser = app.Configuration.GetSection("Roles:User").GetChildren().ToList().Any(x => Context.User.IsInRole($"{x.Value}"));

        Console.WriteLine($"{DateTime.Now.ToString(DateTimeLogFormat)}, URL: '/PowerShell/', UserName: '{Context.User.Identity.Name}'");

        System.Text.RegularExpressions.Regex regex = new Regex(@"^[a-z0-9]", RegexOptions.IgnoreCase);

        if (!(UserIsInRoleAdmin || UserIsInRoleUser)) {
            await Context.Response.WriteAsJsonAsync(new { Success = false, Error = "access denied" }, jOptions);
        } else {
            Dictionary<string,List<string>> Wrappers = ScriptCache.Where(x => UserIsInRoleAdmin || regex.IsMatch(x.Key)).ToDictionary(x => x.Key, x => x.Value.Keys.ToList());
            await Context.Response.WriteAsJsonAsync(new { Success = true, Data = Wrappers}, jOptions);
        }
    }
);

app.Map("/PowerShell/{Wrapper}", async (string Wrapper, HttpContext Context) =>
    {
        bool UserIsInRoleAdmin = app.Configuration.GetSection("Roles:Admin").GetChildren().ToList().Any(x => Context.User.IsInRole($"{x.Value}"));
        bool UserIsInRoleUser = app.Configuration.GetSection("Roles:User").GetChildren().ToList().Any(x => Context.User.IsInRole($"{x.Value}"));

        Console.WriteLine($"{DateTime.Now.ToString(DateTimeLogFormat)}, URL: '/PowerShell/{Wrapper}', UserName: '{Context.User.Identity.Name}'");

        if (!(UserIsInRoleAdmin || UserIsInRoleUser)) {
            await Context.Response.WriteAsJsonAsync(new { Success = false, Error = "access denied" }, jOptions);
        } else {
            List<string> Scripts = new();
            if (ScriptCache.ContainsKey(Wrapper)) {Scripts = ScriptCache[Wrapper].Keys.ToList();}
            await Context.Response.WriteAsJsonAsync(new { Success = true, Data = Scripts }, jOptions);
        }
    }
);

app.Map("/PowerShell/{Wrapper}/{Script}", async (string Wrapper, string Script, HttpContext Context) =>
    {
        Context.Response.Headers["Content-Type"] = RESPONSE_CONTENT_TYPE;
        bool UserIsInRoleAdmin = app.Configuration.GetSection("Roles:Admin").GetChildren().ToList().Any(x => Context.User.IsInRole($"{x.Value}"));
        bool UserIsInRoleUser = app.Configuration.GetSection("Roles:User").GetChildren().ToList().Any(x => Context.User.IsInRole($"{x.Value}"));
        bool WrapperPermission = app.Configuration.GetSection($"WrapperPermissions:{Wrapper}").GetChildren().ToList().Any(x => Context.User.IsInRole($"{x.Value}"));

        if (!(UserIsInRoleAdmin || UserIsInRoleUser)) {
            await Context.Response.WriteAsJsonAsync(new { Success = false, Error = "access denied: user" }, jOptions);
        } else if (!WrapperPermission) {
            await Context.Response.WriteAsJsonAsync(new { Success = false, Error = "access denied: wrapper" }, jOptions);
        } else {
            ScriptRoot = app.Configuration.GetValue("ScriptRoot", Path.Join(ROOT_DIR, ".scripts"))!;
            string WrapperFile = Path.Join(ScriptRoot, Wrapper, "wrapper.ps1");
            string ScriptFile = Path.Join(ScriptRoot, Wrapper, "scripts", $"{Script}.ps1");
            if (!ScriptCache.ContainsKey(Wrapper)) {
                await Context.Response.WriteAsJsonAsync(new { Success = false, Error = $"Wrapper '{Wrapper}' not found in cache, use {Context.Request.Host}/reload for load new scripts or wrappers and {Context.Request.Host}/clear for clear all" }, jOptions);
            } else if (!ScriptCache[Wrapper].ContainsKey(Script)) {
                await Context.Response.WriteAsJsonAsync(new { Success = false, Error = $"Script '{Script}' not found in cache, use {Context.Request.Host}/reload for load new scripts or wrappers and {Context.Request.Host}/clear for clear all" }, jOptions);
            } else if (!File.Exists(WrapperFile)) {
                await Context.Response.WriteAsJsonAsync(new { Success = false, Error = $"Wrapper '{Wrapper}' not found on disk, use {Context.Request.Host}/reload for load new scripts or wrappers and {Context.Request.Host}/clear for clear all" }, jOptions);
            } else if (!File.Exists(ScriptFile)) {
                await Context.Response.WriteAsJsonAsync(new { Success = false, Error = $"Script '{Script}' not found on disk, use {Context.Request.Host}/reload for load new scripts or wrappers and {Context.Request.Host}/clear for clear all" }, jOptions);
            } else {
                var Query = Context.Request.Query.ToDictionary(x => x.Key, x => x.Value.ToString());
                

                Console.WriteLine($"{DateTime.Now.ToString(DateTimeLogFormat)}, URL: '/PowerShell/{Wrapper}/{Script}', UserName: '{Context.User.Identity.Name}'");

                var streamReader = new StreamReader(Context.Request.Body, encoding: System.Text.Encoding.UTF8);
                string Body = await streamReader.ReadToEndAsync();
                string OutputString = PSScriptRunner(Wrapper, Script, Query, Body, Context);
                await Context.Response.WriteAsync(OutputString);
            }
        }
    }
);

string PSScriptRunner(string Wrapper, string Script, Dictionary<String, String> Query, string Body, HttpContext Context) {
    ScriptRoot = app.Configuration.GetValue("ScriptRoot", Path.Join(ROOT_DIR, ".scripts"))!;
    PSRunspaceVariables = app.Configuration.GetSection("Variables").GetChildren().ToList();
    UserCredentialVariable = app.Configuration.GetValue("UserCredentialVariable", "")!;

    var Headers = Context.Request.Headers.ToDictionary(x => x.Key, x => x.Value.ToString());

    int maxDepth = app.Configuration.GetValue("JsonSerialization:maxDepth", 4);
    bool enumsAsStrings = app.Configuration.GetValue("JsonSerialization:enumsAsStrings", true);
    bool compressOutput = app.Configuration.GetValue("JsonSerialization:compressOutput", false);

    string h_depth = Context.Request.Headers.Where(x => x.Key.ToLower() == "depth" || x.Key.ToLower() == "maxdepth").Select(x => x.Key).FirstOrDefault("");
    string h_enums = Context.Request.Headers.Where(x => x.Key.ToLower() == "enums" || x.Key.ToLower() == "enumsAsStrings").Select(x => x.Key).FirstOrDefault("");
    string h_compress = Context.Request.Headers.Where(x => x.Key.ToLower() == "compress" || x.Key.ToLower() == "compressOutput").Select(x => x.Key).FirstOrDefault("");

    if (h_depth.Length > 0) { if (int.TryParse(Headers[h_depth], out int h_depth_)) { maxDepth = h_depth_; }}
    if (h_enums.Length > 0) { if (int.TryParse(Headers[h_enums], out int h_enums_)) { enumsAsStrings = Convert.ToBoolean(h_enums_); }}
    if (h_compress.Length > 0) { if (int.TryParse(Headers[h_compress], out int h_compress_)) { compressOutput = Convert.ToBoolean(h_compress_); }}

    var CurrentProcess = Process.GetCurrentProcess();
    Dictionary<string,object> SqlLogOutput = new();
    OrderedDictionary ResultTable = new();
    Collection<PSObject> PSObjects = new();
    OrderedDictionary Streams = new();
    OrderedDictionary StateInfo = new() {["pid"] = CurrentProcess.Id};
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
    SqlLoggingEnabled = app.Configuration.GetValue("SqlLogging:Enabled", false);

    if (SqlLoggingEnabled) {
        AbortScriptOnSqlFailure = app.Configuration.GetValue("SqlLogging:AbortScriptOnFailure", true);
        SqlConnectionString = app.Configuration.GetValue("SqlLogging:ConnectionString", "")!;

        if (SqlTable != app.Configuration.GetValue("SqlLogging:Table", "Log")) {
            SqlTable = app.Configuration.GetValue("SqlLogging:Table", "Log")!;
            SqlTableCreate(SqlTable, SqlConnectionString);
            Console.WriteLine($"{DateTime.Now.ToString(DateTimeLogFormat)}, SQL TABLE CHANGED!");
        }

        Dictionary<string,object> SqlLogParam = new()
        {
            ["PID"] = Process.GetCurrentProcess().Id,
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
    } else {
        var initialSessionState = System.Management.Automation.Runspaces.InitialSessionState.CreateDefault();
        string conf_ExecPol = app.Configuration.GetValue("ExecutionPolicy", "Unrestricted")!;
        var ExecPol = Enum.Parse(typeof(ExecutionPolicy), conf_ExecPol);
        initialSessionState.ExecutionPolicy = (ExecutionPolicy)ExecPol;
        var PSRunspace = System.Management.Automation.Runspaces.RunspaceFactory.CreateRunspace(initialSessionState);
        PSRunspace.Open();

        foreach (var _ in PSRunspaceVariables) {
            PSRunspace.SessionStateProxy.SetVariable(_.Key, _.Value);
        }

        if (UserCredentialVariable.Length > 0) {

            PSCredential UserCredential = null!;
            
            if (Context.User.Identity.AuthenticationType is not null && Context.User.Identity.AuthenticationType.ToString().ToLower() == "basic") {
                var Encoding = System.Text.Encoding.GetEncoding("utf-8");
                string Authorization = Context.Request.Headers.Where(x => x.Key.ToLower() == "authorization")
                    .Select(x => Regex.Replace(x.Value.ToString(),@"^basic\s*","",RegexOptions.IgnoreCase))
                    .Select(x => Encoding.GetString(Convert.FromBase64String(x)))
                    .FirstOrDefault("");
                if (Authorization.Length > 0) {
                    string u = Authorization.Split(":")[0];
                    string p = Authorization.Split(":")[1];
                    var s = new System.Security.SecureString();
                    p.ToCharArray().ToList().ForEach(x => s.AppendChar(x));
                    UserCredential = new System.Management.Automation.PSCredential(u,s);
                }
            }

            PSRunspace.SessionStateProxy.SetVariable(UserCredentialVariable, UserCredential);
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
        PSOutputString = ConvertToJson(ResultTable,maxDepth:maxDepth,enumsAsStrings:enumsAsStrings,compressOutput:compressOutput,RaiseError:true);
    } catch (Exception e) {
        Streams.Clear();
        success = false;
        ResultTable["Success"] = success;
        ResultTable["Error"] = $"JSON serialization error: {e}";
        ResultTable["Streams"] = Streams;
        ResultTable["InvocationStateInfo"] = StateInfo;
        PSOutputString = ConvertToJson(ResultTable,maxDepth:maxDepth,enumsAsStrings:enumsAsStrings,compressOutput:compressOutput,RaiseError:false);
    }

    if (SqlLoggingEnabled && SqlLogOutput.Count > 0) {
        string PSObjectsJson = ConvertToJson(PSObjects,maxDepth:4,enumsAsStrings:true,compressOutput:true,RaiseError:false);
        string ErrorListJson = ConvertToJson(ErrorList,maxDepth:3,enumsAsStrings:true,compressOutput:true,RaiseError:false);
        string WarningListJson = ConvertToJson(WarningList,maxDepth:3,enumsAsStrings:true,compressOutput:true,RaiseError:false);
        string InformationListJson = ConvertToJson(InformationList,maxDepth:3,enumsAsStrings:true,compressOutput:true,RaiseError:false);
        string VerboseListJson = ConvertToJson(VerboseList,maxDepth:3,enumsAsStrings:true,compressOutput:true,RaiseError:false);

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
            DateTimeLogFormat = app.Configuration.GetValue("DateTimeLogFormat", "yyyy-MM-dd HH:mm:ss")!;
            app.Logger.LogError($"{DateTime.Now.ToString(DateTimeLogFormat)}, ConvertToJson Error: '{e}'");
            throw;
        }
    }
    return Result;
}

void SqlTableCreate(string SqlTable, string ConnectionString) {
    string SqlQuery = $"IF OBJECT_ID(N'[{SqlTable}]') IS NULL CREATE TABLE {SqlTable} ( [id] [bigint] IDENTITY(1,1) NOT NULL PRIMARY KEY CLUSTERED, [BeginDate] [datetime] NOT NULL DEFAULT (GETDATE()), [EndDate] [datetime] NULL, [PID] int NULL, [UserName] [nvarchar](64) NULL, [IPAddress] [nvarchar](64) NULL, [Method] [nvarchar](16) NULL, [Wrapper] [nvarchar](256) NULL, [Script] [nvarchar](256) NULL, [Body] [text] NULL, [Error] [nvarchar](512) NULL, [Success] [bit] NULL, [HadErrors] [bit] NULL, [PSObjects] [text] NULL, [StreamError] [text] NULL, [StreamWarning] [text] NULL, [StreamVerbose] [text] NULL, [StreamInformation] [text] NULL )";
    var Connection = new OdbcConnection(SqlConnectionString);
    var Command = new OdbcCommand(SqlQuery, Connection);
    System.Data.Odbc.OdbcDataAdapter DataAdapter = new();
    DataAdapter.SelectCommand = Command;
    System.Data.DataSet DataSet = new();
    DataAdapter.Fill(DataSet);
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
    ScriptRoot = app.Configuration.GetValue("ScriptRoot", Path.Join(ROOT_DIR, ".scripts"))!;
    CachedVariables = app.Configuration.GetSection("CachedVariables").GetChildren().ToArray().Select(x => x.Value!.ToString()).ToList();

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

    ScriptRoot = app.Configuration.GetValue("ScriptRoot", Path.Join(ROOT_DIR, ".scripts"))!;
    ScriptCache = new Dictionary<String, Dictionary<String, Dictionary<String, object>>>();
    CachedVariables = app.Configuration.GetSection("CachedVariables").GetChildren().ToArray().Select(x => x.Value!.ToString()).ToList();

    var DirectoryInfo = new DirectoryInfo(ScriptRoot);
    var Wrappers = DirectoryInfo.GetDirectories().Where(x => File.Exists(Path.Join(ScriptRoot,x.Name,"wrapper.ps1"))).Select(x => x.Name).ToList();
    ScriptCache = new Dictionary<String, Dictionary<String, Dictionary<String, object>>>();
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

