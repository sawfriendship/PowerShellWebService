using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Linq;

namespace PowerShellWebService.Pages;

public class IndexModel : PageModel
{
    private readonly IConfiguration Configuration;
    private readonly IWebHostEnvironment Environment;

    public IndexModel(IConfiguration conf, IWebHostEnvironment env)
    {
        this.Configuration = conf;
        this.Environment = env;
    }

    // public string UserName { get; private set; } = "";
    // public bool IsAuthenticated { get; private set; } = false;
    // public bool IsDevelopment { get; private set; } = false;
    // public bool UserIsInRoleAdmin { get; private set; } = false;
    // public bool UserIsInRoleUser { get; private set; } = false;

    public void OnGet()
    {
        // var User = HttpContext.User;
        // UserName = HttpContext.User.Identity!.Name ?? "%UserName%";
        // IsAuthenticated = HttpContext.User.Identity.IsAuthenticated;
        // IsDevelopment = Configuration.GetValue("IsDevelopment",false);
        // UserIsInRoleAdmin = Configuration.GetSection("Roles:Admin").GetChildren().ToList().Select(x => x.Value!.ToString()).Any(x => HttpContext.User.IsInRole(x));
        // UserIsInRoleUser = Configuration.GetSection("Roles:User").GetChildren().ToList().Select(x => x.Value!.ToString()).Any(x => HttpContext.User.IsInRole(x));
    }
}
