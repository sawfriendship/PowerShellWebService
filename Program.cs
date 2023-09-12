using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Configuration;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Collections.ObjectModel;
using System.Data;
using System.Data.SqlClient;
using System.Data.Common;
using System.Diagnostics;
using System.Security;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Linq;
using System.Reflection;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using Microsoft.PowerShell;
using Microsoft.PowerShell.Commands;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;

string ASPNETCORE_ENVIRONMENT = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")!;
var WebAppBuilder = Microsoft.AspNetCore.Builder.WebApplication.CreateBuilder(args);
WebAppBuilder.Services.AddDirectoryBrowser();
WebAppBuilder.Services.AddRazorPages();
WebAppBuilder.Logging.AddJsonConsole();
WebAppBuilder.Configuration.
    AddJsonFile("appsettings.json", optional: false, reloadOnChange: true).
    AddJsonFile($"appsettings.{ASPNETCORE_ENVIRONMENT}.json", optional: true, reloadOnChange: true);

await using var app = WebAppBuilder.Build();

string ROOT_DIR = AppContext.BaseDirectory;
bool IsDevelopment = app.Configuration.GetValue("IsDevelopment", false);

string RESPONSE_CONTENT_TYPE = "application/json; charset=utf-8";
string DateTimeLogFormat = app.Configuration.GetValue("DateTimeLogFormat", "yyyy-MM-dd HH:mm:ss")!;
Console.WriteLine($"StartUp:'{DateTime.Now.ToString(DateTimeLogFormat)}', IsDevelopment:'{IsDevelopment}'");

if (!IsDevelopment) {
    app.UseExceptionHandler("/Error");
}

app.UseStaticFiles(new StaticFileOptions {FileProvider = new PhysicalFileProvider(Path.Combine(ROOT_DIR,"wwwroot")), RequestPath = "/wwwroot"});
app.UseStaticFiles();
// app.UseDirectoryBrowser();

app.UseRouting();
app.UseHttpsRedirection();
app.MapRazorPages();

List<string> PSModulePath = app.Configuration.GetSection("PSModulePath").GetChildren().Select(x => x.Value!.ToString()).ToList<string>();
if (PSModulePath.Count > 0) {
    string PSModulePathStr = System.String.Join(";",PSModulePath);
    System.Environment.SetEnvironmentVariable("PSModulePath",PSModulePathStr);
}

string ScriptRoot = app.Configuration.GetValue("ScriptRoot", Path.Join(ROOT_DIR, ".scripts"))!;
string PwShUrl = app.Configuration.GetValue("PwShUrl", "PowerShell")!;
string UserCredentialVariable = app.Configuration.GetValue("UserCredentialVariable", "")!;
string SqlConnectionString = app.Configuration.GetValue("SqlLogging:ConnectionString", "")!;
string SqlTable = app.Configuration.GetValue("SqlLogging:Table", "Log")!;
bool SqlLoggingEnabled = app.Configuration.GetValue("SqlLogging:Enabled", false);
bool AbortScriptOnSqlFailure = app.Configuration.GetValue("SqlLogging:AbortScriptOnFailure", true);
bool Always200 = app.Configuration.GetValue("Always200", true);
var ScriptCache = new Dictionary<string, Dictionary<string, Dictionary<string, object>>>();
var CachedVariables = app.Configuration.GetSection("CachedVariables").GetChildren().ToArray().Select(x => $"{x.Value}").ToList();
var PSRunspaceVariables = app.Configuration.GetSection("Variables").GetChildren().ToList();
var jOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = false , MaxDepth = 5, WriteIndented = true};
Dictionary<string,Dictionary<string,string>> FormatMap = app.Configuration.GetSection("FormatMapping").GetChildren().
    ToDictionary(x => x.Key, x => new Dictionary<string, string>() {["type"] = $"{x["type"]}", ["separator"] = $"{x["separator"]}"});

if (SqlLoggingEnabled) {SqlTableCreate(SqlConnectionString, SqlTable);}

ScriptLoader();

string PSScriptRunner(string Wrapper, string Script, string Body, string Format, HttpContext Context) {
    ScriptRoot = app.Configuration.GetValue("ScriptRoot", Path.Join(ROOT_DIR, ".scripts"))!;
    PSRunspaceVariables = app.Configuration.GetSection("Variables").GetChildren().ToList();
    UserCredentialVariable = app.Configuration.GetValue("UserCredentialVariable", "")!;
    System.Text.RegularExpressions.Regex __Keys__ = new Regex(@"^__.+__$", RegexOptions.IgnoreCase);
    Dictionary<string, string> Query = Context.Request.Query.Where(x => !__Keys__.IsMatch(x.Key)).ToDictionary(x => x.Key, x => $"{x.Value}");
    Dictionary<string, string> Headers = Context.Request.Headers.Where(x => !__Keys__.IsMatch(x.Key)).ToDictionary(x => x.Key, x => $"{x.Value}");
    string AuthorizationHeader = Context.Request.Headers.Where(x => x.Key.ToLower() == "authorization").Select(x => x.Key).FirstOrDefault("");
    string ContentType = $"{Context.Request.ContentType}";
    if (Headers.ContainsKey(AuthorizationHeader)) { Headers[AuthorizationHeader] = $"{Context.User.Identity!.AuthenticationType} ***"; }

    int maxDepth = app.Configuration.GetValue("JsonSerialization:maxDepth", 4);
    bool enumsAsStrings = app.Configuration.GetValue("JsonSerialization:enumsAsStrings", true);
    bool compressOutput = app.Configuration.GetValue("JsonSerialization:compressOutput", false);

    string h_depth = Context.Request.Headers.Where(x => x.Key.ToLower() == "depth" || x.Key.ToLower() == "maxdepth").Select(x => x.Key).FirstOrDefault("");
    string h_enums = Context.Request.Headers.Where(x => x.Key.ToLower() == "enums" || x.Key.ToLower() == "enumsAsStrings").Select(x => x.Key).FirstOrDefault("");
    string h_compress = Context.Request.Headers.Where(x => x.Key.ToLower() == "compress" || x.Key.ToLower() == "compressOutput").Select(x => x.Key).FirstOrDefault("");

    if (h_depth.Length > 0) { if (int.TryParse(Headers[h_depth], out int h_depth_)) { maxDepth = h_depth_; }}
    if (h_enums.Length > 0) { if (int.TryParse(Headers[h_enums], out int h_enums_)) { enumsAsStrings = Convert.ToBoolean(h_enums_); }}
    if (h_compress.Length > 0) { if (int.TryParse(Headers[h_compress], out int h_compress_)) { compressOutput = Convert.ToBoolean(h_compress_); }}

    Process CurrentProcess = Process.GetCurrentProcess();
    string PidFStr = CurrentProcess.Id.ToString("000000");
    Dictionary<string,object> SqlRecord = new();
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

    string TranscriptPath = app.Configuration.GetValue("TranscriptPath", ScriptRoot)!;
    string TranscriptFullPath = Path.GetFullPath(TranscriptPath);
    string DateStr = DateTime.Now.ToString("yyyy-MM-dd");
    string TimeStr = DateTime.Now.ToString("HH-mm-ss-ffffff");
    string GuidStr = System.Guid.NewGuid().ToString();
    string TranscriptFile = Path.Join(Wrapper, Script, DateStr, $"{Wrapper}_{Script}_{DateStr}_{TimeStr}_{PidFStr}_{GuidStr}.txt");

    if (SqlLoggingEnabled) {
        AbortScriptOnSqlFailure = app.Configuration.GetValue("SqlLogging:AbortScriptOnFailure", true);
        SqlConnectionString = app.Configuration.GetValue("SqlLogging:ConnectionString", "")!;

        if (SqlTable != app.Configuration.GetValue("SqlLogging:Table", "Log")) {
            SqlTable = app.Configuration.GetValue("SqlLogging:Table", "Log")!;
            SqlTableCreate(SqlConnectionString, SqlTable);
            Console.WriteLine($"{DateTime.Now.ToString(DateTimeLogFormat)}, SQL TABLE CHANGED!");
        }

        Dictionary<string,object> SqlLogParam = new()
        {
            ["PID"] = Process.GetCurrentProcess().Id,
            ["IPAddress"] = $"{Context.Connection.RemoteIpAddress}",
            ["Method"] = Context.Request.Method,
            ["ContentType"] = ContentType,
            ["Wrapper"] = Wrapper,
            ["Script"] = Script,
            ["TranscriptFile"] = TranscriptFile,
        };

        if (app.Configuration.GetValue("SqlLogging:Fields:Headers", true)) {SqlLogParam["Headers"] = ConvertToJson(Headers,compressOutput:true);}
        if (app.Configuration.GetValue("SqlLogging:Fields:Query", true)) {SqlLogParam["Query"] = ConvertToJson(Query,compressOutput:true);}
        if (app.Configuration.GetValue("SqlLogging:Fields:Body", true)) {SqlLogParam["Body"] = Body;}

        if (Context.User.Identity!.Name is not null) {SqlLogParam["UserName"] = Context.User.Identity.Name;}
        
        try {
            SqlRecord = SqlInsert(SqlConnectionString,SqlTable,SqlLogParam).First();
        } catch (Exception e) {
            Console.WriteLine(e.ToString());
        }
    }

    if (SqlLoggingEnabled && AbortScriptOnSqlFailure && SqlRecord.Count < 1) {
        success = false;
        error = $"SQL Error";
    } else {
        var initialSessionState = System.Management.Automation.Runspaces.InitialSessionState.CreateDefault();
        string ExecutionPolicyString = app.Configuration.GetValue("ExecutionPolicy", "Unrestricted")!;
        Enum.TryParse(ExecutionPolicyString, out ExecutionPolicy ExecutionPolicyEnum);
        if (ExecutionPolicyEnum > 0) {
            initialSessionState.ExecutionPolicy = ExecutionPolicyEnum;
        } else {
            initialSessionState.ExecutionPolicy = ExecutionPolicy.Unrestricted;
        }
        var PSRunspace = System.Management.Automation.Runspaces.RunspaceFactory.CreateRunspace(initialSessionState);
        PSRunspace.Open();

        foreach (var _ in PSRunspaceVariables) {
            PSRunspace.SessionStateProxy.SetVariable(_.Key, _.Value);
        }

        if (UserCredentialVariable.Length > 0) {

            PSCredential UserCredential = null!;
            
            if (Context.User.Identity!.AuthenticationType is not null && Context.User.Identity!.AuthenticationType.ToString().ToLower() == "basic") {
                var Encoding = System.Text.Encoding.GetEncoding("utf-8");
                string Authorization = Context.Request.Headers.Where(x => x.Key.ToLower() == "authorization").
                    Select(x => Regex.Replace(x.Value.ToString(),@"^basic\s*","",RegexOptions.IgnoreCase)).
                    Select(x => Encoding.GetString(Convert.FromBase64String(x))).
                    FirstOrDefault("");
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

        PwSh.
            AddCommand(WrapperFile).
            AddParameter("__SCRIPTFILE__", ScriptFile).
            AddParameter("__SCRIPTNAME__", Script).
            AddParameter("__WRAPPER__", Wrapper).
            AddParameter("__QUERY__", Query).
            AddParameter("__BODY__", Body).
            AddParameter("__METHOD__", Context.Request.Method).
            AddParameter("__USER__", Context.User).
            AddParameter("__CONTEXT__", Context).
            AddParameter("__CONTENTTYPE__", ContentType).
            AddParameter("__FORMAT__", Format).
            AddParameter("__TRANSCRIPT_FILE__", Path.Join(TranscriptFullPath,TranscriptFile));

        DateTime BeginDate = DateTime.Now;

        try {
            PSObjects = PwSh.Invoke();
            Streams.Add("PSObjects", PSObjects);
            HadErrors = PwSh.HadErrors;
            Streams.Add("HadErrors", HadErrors);
        } catch (Exception e) {
            error = $"{e.Message}";
            success = false;
        }

        try {
            foreach (var Stream in PwSh.Streams.Error) {
                try {
                    var InvocationInfo = Stream.InvocationInfo;
                    var ExceptionInfo = Stream.Exception;
                    Dictionary<String, Object> NewExceptionInfo = new()
                    {
                        ["Message"] = $"{ExceptionInfo.Message}",
                        ["Source"] = $"{ExceptionInfo.Source}"
                    };
                    Dictionary<String, Object> StreamDictionary = new()
                    {
                        ["Exception"] = NewExceptionInfo,
                        ["TargetObject"] = $"{Stream.TargetObject}",
                        ["FullyQualifiedErrorId"] = $"{Stream.FullyQualifiedErrorId}"
                    };
                    if (InvocationInfo is not null) {
                        Dictionary<String, Object> NewInvocationInfo = new()
                        {
                            ["ScriptName"] = $"{InvocationInfo.ScriptName}",
                            ["ScriptLineNumber"] = InvocationInfo.ScriptLineNumber,
                            ["Line"] = InvocationInfo.Line,
                            ["PositionMessage"] = $"{InvocationInfo.PositionMessage}",
                            ["PipelineLength"] = $"{InvocationInfo.PipelineLength}",
                            ["PipelinePosition"] = $"{InvocationInfo.PipelinePosition}"
                        };
                        StreamDictionary["InvocationInfo"] = NewInvocationInfo;
                    } else {
                        StreamDictionary["InvocationInfo"] = new Dictionary<String, Object>();
                    }
                    ErrorList.Add(StreamDictionary);
                } catch (Exception e) {
                    success = false;
                    ResultTable["Error"] = $"{e}";
                }
            }
            if (app.Configuration.GetValue("JsonSerialization:Fields:Error",true)) {Streams["Error"] = ErrorList;} else {Streams["Error"] = new List<Object>();}
        } catch (Exception e) {
            success = false;
            ResultTable["Error"] = $"{e}";
        }
            
        try {
            foreach (var Stream in PwSh.Streams.Warning) {
                try {
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
                } catch (Exception e) {
                    success = false;
                    ResultTable["Error"] = $"{e}";
                }
            }
            if (app.Configuration.GetValue("JsonSerialization:Fields:Warning",true)) {Streams["Warning"] = WarningList;} else {Streams["Warning"] = new List<Object>();}
        } catch (Exception e) {
            success = false;
            ResultTable["Error"] = $"{e}";
        }
            
        try {
            foreach (var Stream in PwSh.Streams.Verbose) {
                try {
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
                } catch (Exception e) {
                    success = false;
                    ResultTable["Error"] = $"{e}";
                }
            }
            if (app.Configuration.GetValue("JsonSerialization:Fields:Verbose",true)) {Streams["Verbose"] = VerboseList;} else {Streams["Verbose"] = new List<Object>();}
        } catch (Exception e) {
            success = false;
            ResultTable["Error"] = $"{e}";
        }

        try {
            foreach (var Stream in PwSh.Streams.Information) {
                try {
                    Dictionary<String, Object> StreamDictionary = new()
                    {
                        ["Source"] = $"{Stream.Source}",
                        ["TimeGenerated"] = $"{Stream.TimeGenerated}",
                        ["MessageData"] = Stream.MessageData
                    };
                    InformationList.Add(StreamDictionary);
                } catch (Exception e) {
                    success = false;
                    ResultTable["Error"] = $"{e}";
                }
            }
            if (app.Configuration.GetValue("JsonSerialization:Fields:Information",true)) {Streams["Information"] = InformationList;} else {Streams["Information"] = new List<Object>();}
        } catch (Exception e) {
            success = false;
            ResultTable["Error"] = $"{e}";
        }
        
        try {
            StateInfo["State"] = $"{PwSh.InvocationStateInfo.State}";
            DateTime EndDate = DateTime.Now;
            StateInfo["Duration"] = (EndDate - BeginDate).TotalSeconds;
        } catch (Exception e) {
            success = false;
            ResultTable["Error"] = $"{e}";
        }

        try {
            foreach (string _ in CachedVariables) {
                ScriptCache[Wrapper][Script][_] = PwSh.Runspace.SessionStateProxy.GetVariable(_);
            }
        } finally {
            PwSh.Runspace.Close();
        }

    }

    if (Format == "json") {
        try {
            ResultTable["Error"] = error;
            ResultTable["Success"] = success;
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
    } else {
        string FormatSeparator = FormatMap.GetValueOrDefault(Format,new Dictionary<string,string>(){["separator"]=$"{(char)13}"})["separator"];
        StringBuilder Strings = PSObjects.Select(x => x).Aggregate(new StringBuilder(), (current, next) => current.Append(next).Append(FormatSeparator));
        PSOutputString = Strings.ToString();
    }


    if (SqlLoggingEnabled && SqlRecord.Count > 0) {

        Dictionary<string,object> SqlRecordData = new() {["EndDate"] = DateTime.Now, ["Error"] = error, ["Success"] = success, ["HadErrors"] = HadErrors};
        List<Dictionary<string,object>> SqlRecordFilter = new() {
            new Dictionary<string,object>(){
                ["column"] = "id", ["operator"] = "=", ["value"] = SqlRecord["id"]
            }
        };
        if (app.Configuration.GetValue("SqlLogging:Fields:PSObjects", true)) {SqlRecordData["PSObjects"] = ConvertToJson(PSObjects,compressOutput:true,RaiseError:false);}
        if (app.Configuration.GetValue("SqlLogging:Fields:StreamError", true)) {SqlRecordData["StreamError"] = ConvertToJson(ErrorList,compressOutput:true,RaiseError:false);}
        if (app.Configuration.GetValue("SqlLogging:Fields:StreamWarning", true)) {SqlRecordData["StreamWarning"] = ConvertToJson(WarningList,compressOutput:true,RaiseError:false);}
        if (app.Configuration.GetValue("SqlLogging:Fields:StreamInformation", true)) {SqlRecordData["StreamInformation"] = ConvertToJson(InformationList,compressOutput:true,RaiseError:false);}
        if (app.Configuration.GetValue("SqlLogging:Fields:StreamVerbose", true)) {SqlRecordData["StreamVerbose"] = ConvertToJson(VerboseList,compressOutput:true,RaiseError:false);}

        SqlUpdate(SqlConnectionString,SqlTable,SqlRecordData,SqlRecordFilter);
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

object ConvertFromJson(string data, int maxDepth = 4, bool RaiseError = false) {
    object Result = new();
    try {
        Result = JsonObject.ConvertFromJson(data, out ErrorRecord err);
    } catch (Exception e) {
        if (RaiseError) {
            DateTimeLogFormat = app.Configuration.GetValue("DateTimeLogFormat", "yyyy-MM-dd HH:mm:ss")!;
            app.Logger.LogError($"{DateTime.Now.ToString(DateTimeLogFormat)}, ConvertFromJson Error: '{e}'");
            throw;
        }
    }
    return Result;
}

void SqlTableCreate(string ConnectionString, string Table) {
    string SqlQuery = $@"
        IF OBJECT_ID(N'[{Table}]') IS NULL
        CREATE TABLE {Table} (
            [id] [bigint] IDENTITY(1,1) NOT NULL PRIMARY KEY CLUSTERED,
            [BeginDate] [datetime] NOT NULL DEFAULT (GETDATE()), [EndDate] [datetime] NULL, [PID] int NULL,
            [UserName] [nvarchar](64) NULL DEFAULT '-', [IPAddress] [nvarchar](64) NULL DEFAULT '-', [ContentType] [nvarchar](128) NULL DEFAULT '-', [Method] [nvarchar](16) NULL DEFAULT '-',
            [Wrapper] [nvarchar](256) NULL DEFAULT '-', [Script] [nvarchar](256) NULL DEFAULT '-',
            [Headers] [text] NULL DEFAULT '{{}}', [Query] [text] NULL DEFAULT '{{}}', [Body] [text] NULL DEFAULT '{{}}',
            [Error] [text] NULL DEFAULT '-', [Success] [bit] NULL DEFAULT 0, [HadErrors] [bit] NULL DEFAULT 1,
            [PSObjects] [text] NULL DEFAULT '[]', [StreamError] [text] NULL DEFAULT '[]', [StreamWarning] [text] NULL DEFAULT '[]', [StreamInformation] [text] NULL DEFAULT '[]', [StreamVerbose] [text] NULL DEFAULT '[]',
            [TranscriptPath] [nvarchar](512) NULL
        )
    ";
    var connection = new System.Data.SqlClient.SqlConnection(SqlConnectionString);
    var command = new System.Data.SqlClient.SqlCommand(SqlQuery, connection);
    System.Data.SqlClient.SqlDataAdapter adapter = new();
    adapter.SelectCommand = command;
    System.Data.DataSet DataSet = new();
    adapter.Fill(DataSet);
}

List<Dictionary<string,object>> SqlInsert(string ConnectionString, string Table, Dictionary<string,dynamic> Data) {
    List<Dictionary<string,object>> result = new();
    string Query = $@"
        INSERT INTO [{Table.Trim('[',']')}]
        ({String.Join(',',Data.Keys.Select(x => $"[{x}]"))})
        OUTPUT INSERTED.*
        VALUES({String.Join(',',Data.Keys.Select(x => $"@data_{x}"))})
    ";
    var connection = new System.Data.SqlClient.SqlConnection(ConnectionString);
    var command = new System.Data.SqlClient.SqlCommand(Query, connection);
    foreach (string Key in Data.Keys) {command.Parameters.AddWithValue($"data_{Key}",Data[Key]);}
    System.Data.DataTable dt = new();
    System.Data.SqlClient.SqlDataAdapter adapter = new();
    adapter.SelectCommand = command;
    adapter.Fill(dt);
    if (!dt.HasErrors) {
        List<string> cols = new();
        foreach(DataColumn col in dt.Columns) {cols.Add(col.ColumnName);}
        foreach(DataRow row in dt.Rows) {result.Add(cols.ToDictionary(x => x, x => row[x]));}
    }
    return result;
}

List<Dictionary<string,object>> SqlUpdate(string ConnectionString, string Table, Dictionary<string,dynamic> Data, List<Dictionary<string,dynamic>> Filters, int RowCount = 0) {
    List<Dictionary<string,object>> result = new();
    List<string> Operators = new() {"=","!=",">",">=","<","<=","LIKE","NOT LIKE","IS NULL","IS NOT NULL"};
    List<Dictionary<string,object>> Values = Data.
        Select((x,i) => new Dictionary<string,object>() {["index"]=i+1,["key"]=x.Key,["value"]=x.Value}).
        Select(x => new Dictionary<string,object>() {["column"]=$"{x["key"]}",["variable"]=$"@data_{x["key"]}_{x["index"]}",["value"]=x["value"]}).ToList();
    
    for (int index = 0; index < Filters.Count; index++) {
        if (Filters[index].GetValueOrDefault("column","").ToString().Length < 1) {
            throw new Exception($"empty column name");
        } else if (!Operators.Contains(Filters[index].GetValueOrDefault("operator","=").ToString().ToUpper())) {
            throw new Exception($"invalid operator: '{Filters[index]["operator"]}', use one of [=,!=,>,>=,<,<=,LIKE,NOT LIKE,IS NULL,IS NOT NULL]");
        } else {
            if (Filters[index]["operator"].ToString().ToUpper().Contains("LIKE") && Filters[index]["value"].ToString()!.Contains("*")) {
                Filters[index]["value"] = Filters[index]["value"].ToString()!.Replace("*","%");
            }
            if (Filters[index].GetValueOrDefault("operator","") is System.Text.Json.JsonElement) {
                Filters[index]["value"] = Filters[index]["value"].ToString();
            }
            if (Filters[index]["operator"].ToString().ToUpper().Contains("NULL")) {
                Filters[index]["value"] = "";
            } else {
                Filters[index]["variable"] = $"@filter_{Filters[index]["column"]}_{index}";
            }
        }
    }

    string Query = $@"
        SET ROWCOUNT {RowCount};
        UPDATE [{Table.Trim('[',']')}]
        SET {String.Join(',',Values.Select(x => $"[{x["column"]}]={x["variable"]}"))}
        OUTPUT INSERTED.*
        WHERE 1=1 {String.Join(' ',Filters.Select(x => $"AND [{x["column"]}] {x.GetValueOrDefault("operator","=")} {x.GetValueOrDefault("variable","")}"))}
    ";
    
    var connection = new System.Data.SqlClient.SqlConnection(ConnectionString);
    var command = new System.Data.SqlClient.SqlCommand(Query, connection);
    foreach (Dictionary<string,object> Value_ in Values) {
        command.Parameters.AddWithValue(Value_["variable"].ToString(),Value_["value"]);
    }
    foreach (Dictionary<string,object> Filter_ in Filters) {
        command.Parameters.AddWithValue(Filter_["variable"].ToString(),Filter_["value"]);
    }
    System.Data.DataTable dt = new();
    System.Data.SqlClient.SqlDataAdapter adapter = new();
    adapter.SelectCommand = command;
    adapter.Fill(dt);
    if (!dt.HasErrors) {
        List<string> cols = new();
        foreach(DataColumn col in dt.Columns) {cols.Add(col.ColumnName);}
        foreach(DataRow row in dt.Rows) {result.Add(cols.ToDictionary(x => x, x => row[x]));}
    }
    return result;
}

List<Dictionary<string,object>> SqlSelect(string ConnectionString, string Table, List<Dictionary<string,dynamic>> Filters, int Order = 1, bool ASC = false, int RowCount = 0) {
    List<Dictionary<string,object>> result = new();
    List<string> Operators = new() {"=","!=",">",">=","<","<=","LIKE","NOT LIKE","IS NULL","IS NOT NULL"};
    string OrderDirection = "";
    if (ASC) {OrderDirection = "ASC";} else {OrderDirection = "DESC";}

    for (int index = 0; index < Filters.Count; index++) {
        if (Filters[index].GetValueOrDefault("column","").ToString().Length < 1) {
            throw new Exception($"empty column name");
        } else if (!Operators.Contains(Filters[index].GetValueOrDefault("operator","=").ToString().ToUpper())) {
            throw new Exception($"invalid operator: '{Filters[index]["operator"]}', use one of [=,!=,>,>=,<,<=,LIKE,NOT LIKE,IS NULL,IS NOT NULL]");
        } else {
            if (Filters[index].GetValueOrDefault("operator","") is System.Text.Json.JsonElement) {
                Filters[index]["value"] = Filters[index]["value"].ToString();
            }
            if (Filters[index]["operator"].ToString().ToUpper().Contains("LIKE") && Filters[index]["value"].ToString()!.Contains("*")) {
                Filters[index]["value"] = Filters[index]["value"].ToString()!.Replace("*","%");
            }
            if (Filters[index]["operator"].ToString().ToUpper().Contains("NULL")) {
                Filters[index]["value"] = "";
            } else {
                Filters[index]["variable"] = $"@filter_{Filters[index]["column"]}_{index}";
            }
        }
    }
    
    string Query = $@"
        SET ROWCOUNT {RowCount};
        SELECT * FROM [{Table.Trim('[',']')}]
        WHERE 1=1 {String.Join(' ',Filters.Select(x => $"AND [{x["column"]}] {x.GetValueOrDefault("operator","=")} {x.GetValueOrDefault("variable","")}"))}
        ORDER BY {Order} {OrderDirection}
    ";
    
    var connection = new System.Data.SqlClient.SqlConnection(ConnectionString);
    var command = new System.Data.SqlClient.SqlCommand(Query, connection);

    for (int i = 0; i < Filters.Count; i++) {
        if (Filters[i].GetValueOrDefault("variable","").ToString().Length > 0) {
            command.Parameters.AddWithValue(Filters[i]["variable"].ToString(),Filters[i]["value"]);
        }
    }

    System.Data.DataTable dt = new();
    System.Data.SqlClient.SqlDataAdapter adapter = new();
    adapter.SelectCommand = command;
    adapter.Fill(dt);

    if (!dt.HasErrors) {
        List<string> cols = new();
        foreach(DataColumn col in dt.Columns) {cols.Add(col.ColumnName);}
        foreach(DataRow row in dt.Rows) {result.Add(cols.ToDictionary(x => x, x => row[x]));}
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

bool hasRole(HttpContext Context, string Role = "") {
    List<IConfigurationSection> Roles = app.Configuration.GetSection("Roles").GetChildren().Where(x => Role.Length == 0 || x.Key.ToLower() == Role.ToLower()).ToList();
    foreach (IConfigurationSection Role_ in Roles) {
        if (app.Configuration.GetSection(Role_.Path).GetChildren().Any(x => Context.User.IsInRole($"{x.Value}"))) {
            return true;
        };
    }
    return false;
}

List<string> getRoles(HttpContext Context) {
    List<IConfigurationSection> Roles = app.Configuration.GetSection("Roles").GetChildren().ToList();
    List<string> Result = new();
    foreach (IConfigurationSection Role_ in Roles) {
        if (app.Configuration.GetSection(Role_.Path).GetChildren().Any(x => Context.User.IsInRole($"{x.Value}"))) {
            Result.Add(Role_.Key);
        };
    }
    return Result;
}

app.MapGet("/config/check", async (HttpContext Context) =>
    {
        bool success = true;
        string ErrorMessage = "";
        try {
            string FileContent = File.ReadAllText("appsettings.json");
            var obj = JsonObject.ConvertFromJson(FileContent, out ErrorRecord error);
        } catch (Exception e) {
            ErrorMessage = e.Message;
            success = false;
        }

        await Context.Response.WriteAsJsonAsync(new { Success = success, Error = ErrorMessage }, jOptions);
    }
);

app.MapGet("/config/show", async (HttpContext Context) =>
    {
        IsDevelopment = app.Configuration.GetValue("IsDevelopment", false);
        Context.Response.Headers["Content-Type"] = RESPONSE_CONTENT_TYPE;
        if (hasRole(Context,"Admin") || IsDevelopment) {
            try {
                string FileContent = File.ReadAllText("appsettings.json");
                var JContent = ConvertFromJson(FileContent,RaiseError:true);
                var jContent = ConvertToJson(JContent,RaiseError:true);
                await Context.Response.WriteAsync(jContent);
            } catch (Exception e) {
                Context.Response.StatusCode = (int)System.Net.HttpStatusCode.ServiceUnavailable;
                await Context.Response.WriteAsync(e.Message);
            }
            
        } else {
            Context.Response.StatusCode = (int)System.Net.HttpStatusCode.Forbidden;
            await Context.Response.WriteAsJsonAsync(new { Success = false, Error = "access denied" }, jOptions);
        }
    }
);

app.MapPost("/config/upload", async (HttpContext Context) =>
    {
        IsDevelopment = app.Configuration.GetValue("IsDevelopment", false);
        Context.Response.Headers["Content-Type"] = RESPONSE_CONTENT_TYPE;
        if (!hasRole(Context,"Admin") && !IsDevelopment) {
            await Context.Response.WriteAsJsonAsync(new { Success=false, Error="access denied" }, jOptions);
        } else if (!Context.Request.HasJsonContentType()) {
            await Context.Response.WriteAsJsonAsync(new { Success=false, Error="is not JSON" }, jOptions);
        } else {
            string Body = "";
            try {
                var streamReader = new StreamReader(Context.Request.Body, encoding: System.Text.Encoding.UTF8);
                Body = await streamReader.ReadToEndAsync();
                try {
                    object jBody = ConvertFromJson(Body,RaiseError:true);
                    string JBody = ConvertToJson(jBody,RaiseError:true);
                    try {
                        await File.WriteAllTextAsync("appsettings.json", JBody, Encoding.UTF8);
                    } catch (Exception e) {
                        await Context.Response.WriteAsJsonAsync(new { Success=false, Error=e.Message }, jOptions);
                    }
                } catch {
                    await Context.Response.WriteAsJsonAsync(new { Success=false, Error="invalid JSON" }, jOptions);
                }
            } catch (Exception e) {
                await Context.Response.WriteAsJsonAsync(new { Success=false, Error=e.Message }, jOptions);
            }
            
        }
    }
);

app.Map("/getRoles", async (HttpContext Context) =>
    {
        var get_roles = getRoles(Context);
        await Context.Response.WriteAsJsonAsync(new { get_roles = get_roles, Error = "" }, jOptions);
    }
);

app.Map("/hasRole", async (HttpContext Context) =>
    {
        bool has_role = hasRole(Context);
        await Context.Response.WriteAsJsonAsync(new { has_role = has_role, Error = "" }, jOptions);
    }
);

app.Map("/hasRole/{RoleName}", async (string RoleName, HttpContext Context) =>
    {
        bool has_role = hasRole(Context,RoleName);
        await Context.Response.WriteAsJsonAsync(new { has_role = has_role, Error = "" }, jOptions);
    }
);

app.Map("/whoami", async (HttpContext Context) =>
    {
        Context.Response.Headers["Content-Type"] = RESPONSE_CONTENT_TYPE;
        var Headers = Context.Request.Headers.ToDictionary(x => x.Key, x => x.Value.ToString());
        string AuthorizationHeader = Context.Request.Headers.Where(x => x.Key.ToLower() == "authorization").Select(x => x.Key).FirstOrDefault("");
        if (Headers.ContainsKey(AuthorizationHeader)) { Headers[AuthorizationHeader] = $"{Context.User.Identity!.AuthenticationType} ***"; }
        string CookieHeader = Context.Request.Headers.Where(x => x.Key.ToLower() == "cookie").Select(x => x.Key).FirstOrDefault("");
        if (Headers.ContainsKey(CookieHeader)) { Headers[CookieHeader] = "***"; }

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

app.Map("/logout", async (HttpContext Context) =>
    {   
        long dt = DateTime.Now.ToFileTime();
        Context.Response.Redirect($"/login?dt={dt}", permanent: false);
        await Context.Response.WriteAsync("logout");
    }
);

app.Map("/login", async (HttpContext Context) =>
    {
        DateTime dt = DateTime.Now;
        Dictionary<string,string> Query = Context.Request.Query.ToDictionary(x => x.Key.ToString().ToLower(), x => x.Value.ToString());
        if (long.TryParse(Query.GetValueOrDefault("dt",$"{dt.ToFileTime()}"), out long dt_)) {dt = DateTime.FromFileTime(dt_);}
        if (Query.ContainsKey("dt") && dt.AddSeconds(1.5) < DateTime.Now) {
            Context.Response.StatusCode = (int)System.Net.HttpStatusCode.Unauthorized;
            await Context.Response.WriteAsync("logout");
        } else {
            Context.Response.Redirect("/", permanent: false);
            await Context.Response.WriteAsync("login");
        }
    }
);

app.Map("/reload", async (HttpContext Context) =>
    {
        IsDevelopment = app.Configuration.GetValue("IsDevelopment", false);
        if (hasRole(Context,"Admin") || IsDevelopment) {
            ScriptLoader();
            await Context.Response.WriteAsJsonAsync(new { Success = true, Error = "" }, jOptions);
        } else {
            if (!Always200) {Context.Response.StatusCode = (int)System.Net.HttpStatusCode.Forbidden;}
            await Context.Response.WriteAsJsonAsync(new { Success = false, Error = "access denied" }, jOptions);
        }
    }
);

app.Map("/clear", async (HttpContext Context) =>
    {
        IsDevelopment = app.Configuration.GetValue("IsDevelopment", false);
        if (hasRole(Context,"Admin") || IsDevelopment) {
            ClearCache();
            await Context.Response.WriteAsJsonAsync(new { Success = true, Error = "" }, jOptions);
        } else {
            if (!Always200) {Context.Response.StatusCode = (int)System.Net.HttpStatusCode.Forbidden;}
            await Context.Response.WriteAsJsonAsync(new { Success = false, Error = "access denied" }, jOptions);
        }
    }
);

app.Map($"/{PwShUrl}/", async (HttpContext Context) =>
    {
        bool UserIsInRoleAdmin = hasRole(Context);
        IsDevelopment = app.Configuration.GetValue("IsDevelopment", false);
        Console.WriteLine($"DateTime:'{DateTime.Now.ToString(DateTimeLogFormat)}', Path:'{Context.Request.Path}', QueryString:'{Context.Request.QueryString}', UserName:'{Context.User.Identity!.Name}'");
        System.Text.RegularExpressions.Regex regex = new Regex(@"^[a-z0-9]", RegexOptions.IgnoreCase);

        if (UserIsInRoleAdmin || IsDevelopment) {
            Dictionary<string,List<string>> Wrappers = ScriptCache.Where(x => IsDevelopment || UserIsInRoleAdmin || regex.IsMatch(x.Key)).ToDictionary(x => x.Key, x => x.Value.Keys.ToList());
            await Context.Response.WriteAsJsonAsync(new { Success = true, Data = Wrappers}, jOptions);
        } else {
            if (!Always200) {Context.Response.StatusCode = (int)System.Net.HttpStatusCode.Forbidden;}
            await Context.Response.WriteAsJsonAsync(new { Success = false, Error = "access denied" }, jOptions);
        }
    }
);

app.Map($"/{PwShUrl}/{{Wrapper}}", async (string Wrapper, HttpContext Context) =>
    {
        IsDevelopment = app.Configuration.GetValue("IsDevelopment", false);
        bool WrapperIsPublic = app.Configuration.GetSection($"WrapperPermissions:{Wrapper}").GetChildren().Count() == 0;
        Console.WriteLine($"DateTime:'{DateTime.Now.ToString(DateTimeLogFormat)}', Path:'{Context.Request.Path}', QueryString:'{Context.Request.QueryString}', UserName:'{Context.User.Identity!.Name}'");
        if (hasRole(Context) || WrapperIsPublic || IsDevelopment) {
            List<string> Scripts = new();
            if (ScriptCache.ContainsKey(Wrapper)) {
                Scripts = ScriptCache[Wrapper].Keys.ToList();
            } else {
                if (!Always200) {Context.Response.StatusCode = (int)System.Net.HttpStatusCode.NotFound;}
            }
            await Context.Response.WriteAsJsonAsync(new { Success = true, Data = Scripts }, jOptions);
        } else {
            if (!Always200) {Context.Response.StatusCode = (int)System.Net.HttpStatusCode.Forbidden;}
            await Context.Response.WriteAsJsonAsync(new { Success = false, Error = "access denied" }, jOptions);
        }
    }
);

app.Map($"/{PwShUrl}/{{Wrapper}}/{{Script}}.{{Format}}", async (string Wrapper, string Script, string Format, HttpContext Context) =>
    {
        IsDevelopment = app.Configuration.GetValue("IsDevelopment", false);
        Context.Response.Headers["Content-Type"] = RESPONSE_CONTENT_TYPE;
        bool WrapperIsPublic = app.Configuration.GetSection($"WrapperPermissions:{Wrapper}").GetChildren().Count() == 0;
        bool WrapperPermission = app.Configuration.GetSection($"WrapperPermissions:{Wrapper}").GetChildren().Any(x => Context.User.IsInRole($"{x.Value}"));

        if (!WrapperPermission && !WrapperIsPublic && !IsDevelopment) {
            if (!Always200) {Context.Response.StatusCode = (int)System.Net.HttpStatusCode.Forbidden;}
            await Context.Response.WriteAsJsonAsync(new { Success = false, Error = "access denied" }, jOptions);
        } else if (!FormatMap.ContainsKey(Format.ToLower())) {
            await Context.Response.WriteAsJsonAsync(new { Success = false, Error = $"{Format} - not in [{String.Join(",",FormatMap.Keys)}]" }, jOptions);
        } else {
            ScriptRoot = app.Configuration.GetValue("ScriptRoot", Path.Join(ROOT_DIR, ".scripts"))!;
            string WrapperFile = Path.Join(ScriptRoot, Wrapper, "wrapper.ps1");
            string ScriptFile = Path.Join(ScriptRoot, Wrapper, "scripts", $"{Script}.ps1");
            string hostname = Context.Request.Host.ToString();
            
            if (!ScriptCache.ContainsKey(Wrapper)) {
                if (!Always200) {Context.Response.StatusCode = (int)System.Net.HttpStatusCode.NotFound;}
                await Context.Response.WriteAsJsonAsync(new { Success = false, Error = $"Wrapper '{Wrapper}' not found in cache, use {hostname}/reload to load new scripts or wrappers and {hostname}/clear to clear all" }, jOptions);
            } else if (!ScriptCache[Wrapper].ContainsKey(Script)) {
                if (!Always200) {Context.Response.StatusCode = (int)System.Net.HttpStatusCode.NotFound;}
                await Context.Response.WriteAsJsonAsync(new { Success = false, Error = $"Script '{Script}' not found in cache, use {hostname}/reload to load new scripts or wrappers and {hostname}/clear to clear all" }, jOptions);
            } else if (!File.Exists(WrapperFile)) {
                if (!Always200) {Context.Response.StatusCode = (int)System.Net.HttpStatusCode.NotFound;}
                await Context.Response.WriteAsJsonAsync(new { Success = false, Error = $"Wrapper '{Wrapper}' not found on disk, use {hostname}/reload to load new scripts or wrappers and {hostname}/clear to clear all" }, jOptions);
            } else if (!File.Exists(ScriptFile)) {
                if (!Always200) {Context.Response.StatusCode = (int)System.Net.HttpStatusCode.NotFound;}
                await Context.Response.WriteAsJsonAsync(new { Success = false, Error = $"Script '{Script}' not found on disk, use {hostname}/reload to load new scripts or wrappers and {hostname}/clear to clear all" }, jOptions);
            } else {
                Console.WriteLine($"DateTime:'{DateTime.Now.ToString(DateTimeLogFormat)}', Path:'{Context.Request.Path}', QueryString:'{Context.Request.QueryString}', UserName:'{Context.User.Identity!.Name}'");
                var streamReader = new StreamReader(Context.Request.Body, encoding: System.Text.Encoding.UTF8);
                string OutputString = "";
                string Body = await streamReader.ReadToEndAsync();
                bool success = true;
                string error = "";

                if (Context.Request.HasJsonContentType()) {
                    try {
                        object _ = ConvertFromJson(Body,RaiseError:true);
                    } catch {
                        success = false;
                        error = "invalid JSON";
                    }
                } else {
                    try {
                        var ParsedBody = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(Body).ToDictionary(x => x.Key, x => x.Value.ToString());
                        Body = ConvertToJson(ParsedBody, maxDepth:2, compressOutput:true);
                    } catch {
                        success = false;
                        error = "invalid Body";
                    }
                }

                if (success) {
                    OutputString = PSScriptRunner(Wrapper, Script, Body, Format, Context);
                    Context.Response.Headers["Content-Type"] = FormatMap.GetValueOrDefault(Format,new Dictionary<string,string>(){["type"]="text/plain; charset=utf-8"})["type"];
                    await Context.Response.WriteAsync(OutputString);
                } else {
                    if (!Always200) {Context.Response.StatusCode = (int)System.Net.HttpStatusCode.BadRequest;}
                    await Context.Response.WriteAsJsonAsync(new { Success = false, Error = error }, jOptions);
                }

            }
        }
    }
);

app.MapPost("/log", async (HttpContext Context) =>
    {
        List<Dictionary<string,object>> data = new();
        List<string> Columns = new() {"id","BeginDate","EndDate","PID","UserName","IPAddress","Method","Wrapper","Script","Headers","Query","Body","Error","Success","HadErrors","PSObjects","StreamError","StreamWarning","StreamInformation","StreamVerbose"};
        string Output = "";
        bool success = true;
        string error = "";
        int Limit = 10;
        int Order = 1;
        bool ASC = false;
        List<Dictionary<string,object>> Filters = new();
        var streamReader = new StreamReader(Context.Request.Body, encoding: System.Text.Encoding.UTF8);
        string Body = await streamReader.ReadToEndAsync();

        if (Context.Request.HasJsonContentType()) {
            try {
                Filters = System.Text.Json.JsonDocument.Parse(Body).Deserialize<List<Dictionary<string,dynamic>>>();
                // Filters = Context.Request.ReadFromJsonAsync<List<Dictionary<string,dynamic>>>(jOptions);
            } catch {
                await Context.Response.WriteAsJsonAsync(new { Success = false, Error = "invalid JSON" }, jOptions);
                return;
            }
        } else {
            await Context.Response.WriteAsJsonAsync(new { Success = false, Error = "Has not Json ContentType" }, jOptions);
            return;
        }

        JsonObject.ConvertToJsonContext jsonContext = new JsonObject.ConvertToJsonContext(maxDepth: 4, enumsAsStrings: true, compressOutput: false);
        Context.Response.Headers["Content-Type"] = RESPONSE_CONTENT_TYPE;
        
        IsDevelopment = app.Configuration.GetValue("IsDevelopment", false);
        SqlTable = app.Configuration.GetValue("SqlLogging:Table", "Log")!;
        SqlConnectionString = app.Configuration.GetValue("SqlLogging:ConnectionString", "")!;

        Dictionary<string,string> Query = Context.Request.Query.ToDictionary(x => x.Key.ToString().ToLower(), x => x.Value.ToString());

        if (int.TryParse(Query.GetValueOrDefault("limit","10"), out int Limit_)) {Limit = Limit_;} else {success=false;error="limit is not integer";}
        if (int.TryParse(Query.GetValueOrDefault("order","1"), out int Order_)) {Order = Order_;} else {success=false;error="order is not integer";}
        if (int.TryParse(Query.GetValueOrDefault("asc","0"), out int ASC_)) {ASC = ASC_ > 0 ? true : false;} else {success=false;error="asc is not integer";}

        List<string> DateTimeColumns = new(){"begindate","enddate"};
        for (int i = 0; i < Filters.Count; i++) {
            if (DateTimeColumns.Contains(Filters[i]["column"].ToString().ToLower())) {
                DateTime dt = new();
                string v = Filters[i].GetValueOrDefault("value","").ToString();
                if (DateTime.TryParse(v, out dt)) {
                    Filters[i]["value"] = dt;
                };
            }
        }
        
        SqlLoggingEnabled = app.Configuration.GetValue("SqlLogging:Enabled", false);
        if (!SqlLoggingEnabled) {
            if (!Always200) {Context.Response.StatusCode = (int)System.Net.HttpStatusCode.MethodNotAllowed;}
            await Context.Response.WriteAsJsonAsync(new {Success=false,Error="sql logging disabled"}, jOptions);
        } else if (!success) {
            if (!Always200) {Context.Response.StatusCode = (int)System.Net.HttpStatusCode.BadRequest;}
            await Context.Response.WriteAsJsonAsync(new {Success=false,Error=error}, jOptions);
        } else if (!hasRole(Context,"Admin") && !IsDevelopment) {
            if (!Always200) {Context.Response.StatusCode = (int)System.Net.HttpStatusCode.Forbidden;}
            await Context.Response.WriteAsJsonAsync(new {Success=false,Error="access denied"}, jOptions);
        } else if (Filters.Select(x => x.GetValueOrDefault("column","").ToString().ToLower()).Any(c => !Columns.Select(x => x.ToLower()).Contains(c))) {
            if (!Always200) {Context.Response.StatusCode = (int)System.Net.HttpStatusCode.BadRequest;}
            await Context.Response.WriteAsJsonAsync(new {Success=false,Error="unknown column"}, jOptions);
        } else {
            try {
                var rows = SqlSelect(SqlConnectionString,SqlTable,Filters,Order,ASC,Limit);
                foreach(Dictionary<string,object> row in rows) {
                    data.Add(
                        new Dictionary<string,object>() {
                            ["id"] = row["id"],
                            ["BeginDate"] = row["BeginDate"],
                            ["EndDate"] = row["EndDate"],
                            ["PID"] = row["PID"],
                            ["UserName"] = row["UserName"],
                            ["IPAddress"] = row["IPAddress"],
                            ["ContentType"] = row["ContentType"],
                            ["Method"] = row["Method"],
                            ["Wrapper"] = row["Wrapper"],
                            ["Script"] = row["Script"],
                            ["Success"] = row["Success"],
                            ["Error"] = row["Error"],
                            ["HadErrors"] = row["HadErrors"],
                            ["Query"] = ConvertFromJson($"{row["Query"]}"),
                            ["Body"] = ConvertFromJson($"{row["Body"]}"),
                            ["PSObjects"] = ConvertFromJson($"{row["PSObjects"]}"),
                            ["StreamError"] = ConvertFromJson($"{row["StreamError"]}"),
                            ["StreamWarning"] = ConvertFromJson($"{row["StreamWarning"]}"),
                            ["StreamInformation"] = ConvertFromJson($"{row["StreamInformation"]}"),
                            ["StreamVerbose"] = ConvertFromJson($"{row["StreamVerbose"]}"),
                            ["Headers"] = ConvertFromJson($"{row["Headers"]}"),
                            ["TranscriptFile"] = row["TranscriptFile"],
                        }
                    );
                }

            } catch (Exception e) {
                success = false;
                error = e.Message;
            }
            Dictionary<string,object> result = new() {["Success"]=success,["Error"]=error,["Count"]=data.Count,["Data"]=data};
            Output = ConvertToJson(result);
            await Context.Response.WriteAsync(Output);
        }
    }
);

app.MapGet("/log/{id:int}", async (int id, HttpContext Context) =>
    {
        IsDevelopment = app.Configuration.GetValue("IsDevelopment", false);
        SqlTable = app.Configuration.GetValue("SqlLogging:Table", "Log")!;
        SqlConnectionString = app.Configuration.GetValue("SqlLogging:ConnectionString", "")!;

        List<Dictionary<string,dynamic>> Filters = new(){
            new Dictionary<string,dynamic>() {
                ["column"] = "id", ["operator"] = "=", ["value"] = id
            }
        };

        SqlLoggingEnabled = app.Configuration.GetValue("SqlLogging:Enabled", false);
        if (!SqlLoggingEnabled) {
            if (!Always200) {Context.Response.StatusCode = (int)System.Net.HttpStatusCode.MethodNotAllowed;}
            await Context.Response.WriteAsJsonAsync(new {Success=false, Error="sql logging disabled"}, jOptions);
        } else if (!hasRole(Context,"Admin") && !IsDevelopment) {
            if (!Always200) {Context.Response.StatusCode = (int)System.Net.HttpStatusCode.Forbidden;}
            await Context.Response.WriteAsJsonAsync(new {Success=false, Error="access denied"}, jOptions);
        } else {
            try {
                var rows = SqlSelect(SqlConnectionString,SqlTable,Filters,Order:1,ASC:true,RowCount:1);
                var row = rows.First();
                Dictionary<string,object> data = new() {
                    ["id"] = row["id"],
                    ["BeginDate"] = row["BeginDate"],
                    ["EndDate"] = row["EndDate"],
                    ["PID"] = row["PID"],
                    ["UserName"] = row["UserName"],
                    ["IPAddress"] = row["IPAddress"],
                    ["ContentType"] = row["ContentType"],
                    ["Method"] = row["Method"],
                    ["Wrapper"] = row["Wrapper"],
                    ["Script"] = row["Script"],
                    ["Success"] = row["Success"],
                    ["Error"] = row["Error"],
                    ["HadErrors"] = row["HadErrors"],
                    ["Query"] = ConvertFromJson($"{row["Query"]}"),
                    ["Body"] = ConvertFromJson($"{row["Body"]}"),
                    ["PSObjects"] = ConvertFromJson($"{row["PSObjects"]}"),
                    ["StreamError"] = ConvertFromJson($"{row["StreamError"]}"),
                    ["StreamWarning"] = ConvertFromJson($"{row["StreamWarning"]}"),
                    ["StreamInformation"] = ConvertFromJson($"{row["StreamInformation"]}"),
                    ["StreamVerbose"] = ConvertFromJson($"{row["StreamVerbose"]}"),
                    ["Headers"] = ConvertFromJson($"{row["Headers"]}"),
                    ["TranscriptFile"] = row["TranscriptFile"],
                };

                Context.Response.Headers["Content-Type"] = RESPONSE_CONTENT_TYPE;
                Dictionary<string,object> result = new() {["Success"]=true,["Error"]="",["Data"]=data};
                string Output = ConvertToJson(result);
                await Context.Response.WriteAsync(Output);
                
            } catch (Exception e) {
                await Context.Response.WriteAsJsonAsync(new {Success=false, Error=e.Message});
            }
        }
    }
);

app.Map("/transcript/{id:int}", async (int id, HttpContext Context) =>
    {
        List<Dictionary<string,object>> data = new();

        IsDevelopment = app.Configuration.GetValue("IsDevelopment", false);
        SqlTable = app.Configuration.GetValue("SqlLogging:Table", "Log")!;
        SqlConnectionString = app.Configuration.GetValue("SqlLogging:ConnectionString", "")!;

        List<Dictionary<string,dynamic>> Filters = new(){
            new Dictionary<string,dynamic>() {
                ["column"] = "id", ["operator"] = "=", ["value"] = id
            }
        };
        
        SqlLoggingEnabled = app.Configuration.GetValue("SqlLogging:Enabled", false);
        if (!SqlLoggingEnabled) {
            Context.Response.StatusCode = (int)System.Net.HttpStatusCode.MethodNotAllowed;
            await Context.Response.WriteAsync("sql logging disabled");
        } else if (!hasRole(Context,"Admin") && !IsDevelopment) {
            Context.Response.StatusCode = (int)System.Net.HttpStatusCode.Forbidden;
            await Context.Response.WriteAsync("access denied");
        } else {

            try {
                var rows = SqlSelect(SqlConnectionString,SqlTable,Filters);
                foreach(Dictionary<string,object> row_ in rows) {
                    data.Add(
                        new Dictionary<string,object>() {
                            ["TranscriptFile"] = row_["TranscriptFile"],
                        }
                    );
                }
                var row = data.First();
                string TranscriptPath = app.Configuration.GetValue("TranscriptPath", ScriptRoot)!;
                string TranscriptFile = Path.Join(TranscriptPath,row["TranscriptFile"].ToString()!);
                var TranscriptFileContent = File.ReadAllText(TranscriptFile);
                await Context.Response.WriteAsync(TranscriptFileContent);

            } catch (Exception e) {
                if (!Always200) {Context.Response.StatusCode = (int)System.Net.HttpStatusCode.BadRequest;}
                await Context.Response.WriteAsync(e.GetBaseException().Message);
            }
        }
    }
);

await app.RunAsync();

