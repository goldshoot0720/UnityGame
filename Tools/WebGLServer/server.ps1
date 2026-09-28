# MoeGames web version: minimal static web server (localhost only) for the Unity WebGL build.
# Usage: start.bat [port]   (default port 8080; the next free port is used if it is busy)
param([int]$Port = 8080)
$root = $PSScriptRoot
$types = @{
    '.html' = 'text/html; charset=utf-8'; '.js' = 'application/javascript'; '.css' = 'text/css'
    '.wasm' = 'application/wasm'; '.json' = 'application/json'; '.png' = 'image/png'; '.ico' = 'image/x-icon'
    '.jpg' = 'image/jpeg'; '.svg' = 'image/svg+xml'; '.txt' = 'text/plain'
}
$listener = $null
for ($p = $Port; $p -lt $Port + 20; $p++) {
    try {
        $listener = New-Object System.Net.HttpListener
        $listener.Prefixes.Add("http://localhost:$p/")
        $listener.Start()
        $Port = $p
        break
    } catch { $listener = $null }
}
if (-not $listener) { Write-Host "No free port found near $Port"; exit 1 }
$url = "http://localhost:$Port/"
Write-Host "MoeGames: $url"
Write-Host "Close this window (or press Ctrl+C) to stop the server."
Start-Process $url
while ($listener.IsListening) {
    $ctx = $listener.GetContext()
    $res = $ctx.Response
    try {
        $rel = [Uri]::UnescapeDataString($ctx.Request.Url.AbsolutePath.TrimStart('/'))
        if ($rel -eq '') { $rel = 'index.html' }
        $path = [IO.Path]::GetFullPath((Join-Path $root $rel))
        if (-not $path.StartsWith($root) -or -not (Test-Path $path -PathType Leaf)) {
            $res.StatusCode = 404
        } else {
            $name = $path
            # Unity build files: "Build.wasm.gz" / ".br" are served pre-compressed; ".unityweb" is decompressed by the loader.
            if ($name.EndsWith('.gz')) { $res.AddHeader('Content-Encoding', 'gzip'); $name = $name.Substring(0, $name.Length - 3) }
            elseif ($name.EndsWith('.br')) { $res.AddHeader('Content-Encoding', 'br'); $name = $name.Substring(0, $name.Length - 3) }
            $ext = [IO.Path]::GetExtension($name).ToLower()
            $res.ContentType = if ($types.ContainsKey($ext)) { $types[$ext] } else { 'application/octet-stream' }
            $bytes = [IO.File]::ReadAllBytes($path)
            $res.ContentLength64 = $bytes.Length
            $res.OutputStream.Write($bytes, 0, $bytes.Length)
        }
    } catch { $res.StatusCode = 500 }
    $res.Close()
}
