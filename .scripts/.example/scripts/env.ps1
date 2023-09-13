param(
    [string]$Name = '*'
)

# $ls = ls -Path env:\ | ? Name -like $Name | select Name,Value
$ls = ls -Path "env:\$Name" | select Name,Value

if ($__FORMAT__ -eq 'json') {
    $ls
} elseif ($__FORMAT__ -eq 'txt') {
    $ls | Out-String
} elseif ($__FORMAT__ -eq 'csv') {
    $ls | ConvertTo-Csv -Delimiter ';'
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
                            <form action="$($__CONTEXT__.Request.Path)$($__CONTEXT__.Request.QueryString)" method="POST">
                                <div class="input-group input-group-sm w-100">
                                    <label for="Name" class="form-label w-25">Wrapper</label>
                                    <input id="Name" name="Name" class="form-control w-50" value="$Name">
                                    <input type="submit" class="btn btn-outline-dark w-25" value="Post!" />
                                </div>
                            </form>
                            <br>
                            $($ls | ConvertTo-Html -Fragment -Property Name,Value | Out-String)
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

