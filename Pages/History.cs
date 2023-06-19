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
    public void OnGet()
    {

    }

}

