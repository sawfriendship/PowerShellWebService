using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Linq;

namespace PowerShellWebService.Pages;

public class ScriptRunnerModel : PageModel
{
    private readonly IConfiguration Configuration;
    private readonly IWebHostEnvironment Environment;

    public ScriptRunnerModel(IConfiguration conf, IWebHostEnvironment env)
    {
        this.Configuration = conf;
        this.Environment = env;
    }

    public void OnGet()
    {

    }
    
}

