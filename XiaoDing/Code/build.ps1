param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $root "src\XiaoDing.csproj"
$publish = Join-Path $root "publish"
$output = Join-Path $root "src\bin\$Configuration\net472"

Write-Host "编译 $Configuration ..."
dotnet build $project -c $Configuration -v minimal
if ($LASTEXITCODE -ne 0) {
    throw "编译失败"
}

if (Test-Path $publish) {
    Remove-Item $publish -Recurse -Force
}

New-Item -ItemType Directory -Force $publish | Out-Null
Copy-Item (Join-Path $output "XiaoDing.exe") $publish

$size = [math]::Round((Get-Item (Join-Path $publish "XiaoDing.exe")).Length / 1KB, 1)
Write-Host "输出: $publish\XiaoDing.exe ($size KB)"
