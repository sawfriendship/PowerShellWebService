using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Linq;

namespace PowerShellWebService.Pages;

public class HistoryModel : PageModel
{
    private readonly IConfiguration Configuration;
    private readonly IWebHostEnvironment Environment;

    public HistoryModel(IConfiguration conf, IWebHostEnvironment env)
    {
        this.Configuration = conf;
        this.Environment = env;
    }

    public string UserName { get; private set; } = "";
    public bool IsAuthenticated { get; private set; } = false;
    public bool IsDevelopment { get; private set; } = false;
    public bool SqlLoggingEnabled { get; private set; } = false;
    public string PwShUrl { get; private set; } = "";
    public bool UserIsInRoleAdmin { get; private set; } = false;
    public bool UserIsInRoleUser { get; private set; } = false;
    public bool has_role { get; private set; } = false;
    // public Dictionary<string,string> Query { get; private set; } = new();

    public void OnGet()
    {
        var User = HttpContext.User;
        UserName = HttpContext.User.Identity!.Name ?? "%UserName%";
        // Query = HttpContext.Request.Query.ToDictionary(x => x.Key.ToString().ToLower(), x => x.Value.ToString());
        IsAuthenticated = HttpContext.User.Identity.IsAuthenticated;
        IsDevelopment = Configuration.GetValue("IsDevelopment",false);
        SqlLoggingEnabled = Configuration.GetValue("SqlLogging:Enabled", false);
        PwShUrl = Configuration.GetValue("PwShUrl","PowerShell")!;
        UserIsInRoleAdmin = Configuration.GetSection("Roles:Admin").GetChildren().ToList().Select(x => x.Value!.ToString()).Any(x => HttpContext.User.IsInRole(x));
        UserIsInRoleUser = Configuration.GetSection("Roles:User").GetChildren().ToList().Select(x => x.Value!.ToString()).Any(x => HttpContext.User.IsInRole(x));
        has_role = hasRole(HttpContext);
    }

    bool hasRole(HttpContext Context, string Role = "") {
        List<IConfigurationSection> Roles = Configuration.GetSection("Roles").GetChildren().Where(x => Role.Length == 0 || x.Key.ToLower() == Role.ToLower()).ToList();
        foreach (IConfigurationSection Role_ in Roles) {
            if (Configuration.GetSection(Role_.Path).GetChildren().Any(x => Context.User.IsInRole($"{x.Value}"))) {
                return true;
            };
        }
        return false;
    }

    List<string> getRoles(HttpContext Context) {
        List<IConfigurationSection> Roles = Configuration.GetSection("Roles").GetChildren().ToList();
        List<string> Result = new();
        foreach (IConfigurationSection Role_ in Roles) {
            if (Configuration.GetSection(Role_.Path).GetChildren().Any(x => Context.User.IsInRole($"{x.Value}"))) {
                Result.Add(Role_.Key);
            };
        }
        return Result;
    }

}

