param(
    [int]$Year = (Get-Date).Year,
    [int]$Month = (Get-Date).Month,
    [int]$Day = (Get-Date).Day
)
    
$Date = Get-Date @__PARAMS__ | % Date | Select-Object -Property *

if ($__FORMAT__ -eq 'json') {
    $Date
} elseif ($__FORMAT__ -eq 'txt') {
    $Date | Out-String
} elseif ($__FORMAT__ -eq 'csv') {
    $Date | ConvertTo-Csv -Delimiter ';'
} elseif ($__FORMAT__ -eq 'prom') {
    $Date | ConvertTo-Prom -Name 'Date' -Help 'dt' -Type Gauge -Property Name -Value {$_.Year}
} elseif ($__FORMAT__ -eq 'html') {
@"
    <!DOCTYPE html>
    <html lang="en">
        <head>
            <meta charset="utf-8" />
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <link rel="stylesheet" href="/wwwroot/lib/bootstrap/dist/css/bootstrap.min.css" />
            <link rel="stylesheet" href="/wwwroot/lib/bootstrap/dist/css/bootstrap-icons.css" />
            <link rel="stylesheet" href="/wwwroot/css/site.css" />
            <script src="/wwwroot/lib/jquery/dist/jquery.min.js"></script>
            <script src="/wwwroot/lib/bootstrap/dist/js/bootstrap.bundle.min.js"></script>
            <script src="/wwwroot/lib/j2ht/dist/j2ht.js"></script>
            <link rel="icon" type="image/x-icon" href="/wwwroot/favicon.ico">
            <title>$__SCRIPTNAME__</title>
        </head>

        <script>
            `$(document).ready(function() {
                `$('table').addClass('table')
            });
        </script>

        <body>
            
            <header>
            </header>

            <main role="main">
                <div class="container">
                    <div class="row row-cols-2">
                        <div id="LEFT" class="col-xs-12 col-sm-12 col-md-8 col-lg-4 col-xl-4 col-xxl-4" style="z-index:2">
                            <a href="$__ACTION_PATH__">$__ACTION_PATH__</a>
                            <br>
                            <form action="$__ACTION_PATH__" method="POST">
                                <div class="input-group input-group-sm w-100">
                                    <label for="Year" class="form-label w-25">Year</label>
                                    <input id="Year" name="Year" class="form-control w-50" value="$Year">
                                </div>
                                <div class="input-group input-group-sm w-100">
                                    <label for="Month" class="form-label w-25">Month</label>
                                    <input id="Month" name="Month" class="form-control w-50" value="$Month">
                                </div>
                                <div class="input-group input-group-sm w-100">
                                    <label for="Day" class="form-label w-25">Day</label>
                                    <input id="Day" name="Day" class="form-control w-50" value="$Day">
                                </div>
                                <input type="submit" class="btn btn-outline-dark w-25" value="Post!" />
                            </form>
                            <br>
                            $($Date | ConvertTo-Html -Fragment -As List -Property * | Out-String)
                        </div>
                    </div>
                </div>
            </main>
                                    
            <footer class="border-top footer fixed-bottom text-muted bg-dark">
                <div class="container container-fluid">&copy; $((Get-Date).ToString('yyyy-MM-dd HH:mm:ss')) - PowerShellWebService</div>
            </footer>
                                    
                                    
        </body>

    </html>
"@
}

