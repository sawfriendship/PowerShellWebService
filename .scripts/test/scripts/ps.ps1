param(
    [string]$Name = '*',
    [int]$Limit = 5
)
22343
$ps = ps -Name $Name | select id,name -f $Limit

if ($__FORMAT__ -eq 'json') {
    $ps
} elseif ($__FORMAT__ -eq 'txt') {
    $ps | Out-String
} elseif ($__FORMAT__ -eq 'csv') {
    $ps | ConvertTo-Csv -Delimiter ';'
} elseif ($__FORMAT__ -eq 'prom') {
    $ps | ConvertTo-Prom -Name 'Process' -Help 'ps' -Type Gauge -Property Name -Value {$_.id}
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

                        <div id="LEFT" class="w-100">
                            <form action="$__ACTION_PATH__" method="POST">
                                <div class="input-group input-group-sm w-100">
                                    <label for="Name" class="form-label w-25">Wrapper</label>
                                    <input id="Name" name="Name" class="form-control w-50" value="$Name">
                                </div>
                                <div class="input-group input-group-sm w-100">
                                    <label for="Limit" class="form-label w-25">Limit</label>
                                    <input id="Limit" name="Limit" class="form-control w-50" value="$Limit">
                                </div>
                                <input type="submit" class="btn btn-outline-dark w-25" value="Post!" />
                            </form>
                            <br>
                            $__ACTION_PATH__
                            <br>
                            $($ps | ConvertTo-Html -Fragment -Property id,name | Out-String)
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

