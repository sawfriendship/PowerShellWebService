param(
    [string]$Name = '*',
    [int]$Limit = 5
)

$ps = ps -Name $Name | select id,name -f $Limit

if ($__FORMAT__ -eq 'json') {
    $ps
} elseif ($__FORMAT__ -eq 'txt') {
    $ps | Out-String
} elseif ($__FORMAT__ -eq 'csv') {
    $ps | ConvertTo-Csv -Delimiter ';'
} elseif ($__FORMAT__ -eq 'html') {
@"
    <!DOCTYPE html>
    <html lang="en">
        <head>
            <meta charset="utf-8" />
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <title>$__SCRIPTNAME__</title>
            <link rel="stylesheet" href="/lib/bootstrap/dist/css/bootstrap.min.css" />
            <link rel="stylesheet" href="/lib/bootstrap/dist/css/bootstrap-icons.css" />
            <link rel="stylesheet" href="/css/site.css" />
            <link rel="stylesheet" href="/PowerShellWebService.styles.css" />
            <script src="/lib/jquery/dist/jquery.min.js"></script>
            <script src="/lib/bootstrap/dist/js/bootstrap.bundle.min.js"></script>
            <script src="/lib/j2ht/dist/j2ht.js"></script>
        </head>

        <script>
            `$(document).ready(function() {
                `$('main table').addClass('table')
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
                                    </div>
                                    <div class="input-group input-group-sm w-100">
                                    <label for="Limit" class="form-label w-25">Limit</label>
                                    <input id="Limit" name="Limit" class="form-control w-50" value="$Limit">
                                </div>
                                <input type="submit" class="btn btn-outline-dark w-25" value="Post!" />
                            </form>
                            <br>
                            1
                            $($ps | ConvertTo-Html -Property id,name)
                            2
                            <table class="table">
                                <thead>
                                    <th>id</th><th>Name</th>
                                </thead>
                                <tbody>
                                    $($ps | % {"<tr><td>$($_.id)</td><td>$($_.Name)</td></tr>"})
                                </tbody>
                            </table>

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

