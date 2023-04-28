﻿using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Linq;

namespace PowerShellWebService.Pages;

public class ScriptRunnerModel : PageModel
{
    private readonly IConfiguration Configuration;

    public ScriptRunnerModel(IConfiguration configuration)
    {
        Configuration = configuration;
    }

    public string UserName { get; private set; } = "";
    public bool UserIsInRoleAdmin { get; private set; } = false;
    public bool UserIsInRoleUser { get; private set; } = false;

    public void OnGet()
    {
        var User = HttpContext.User;
        UserName = HttpContext.User.Identity.Name ?? "%UserName%";
        UserIsInRoleAdmin = Configuration.GetSection("Roles:Admin").GetChildren().ToList().Select(x => x.Value!.ToString()).Any(x => HttpContext.User.IsInRole(x));
        UserIsInRoleUser = Configuration.GetSection("Roles:User").GetChildren().ToList().Select(x => x.Value!.ToString()).Any(x => HttpContext.User.IsInRole(x));
    }
}

