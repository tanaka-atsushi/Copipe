<#
    bin\Copipe.exe からインストーラー (bin\Copipe-Setup-<版>.exe) を作る (build.ps1 -Installer から呼ぶ)

    exe はビルドし直さない。バージョンは exe の製品バージョン (AssemblyInformationalVersion) を使う。
    Inno Setup 6 が要る: winget install JRSoftware.InnoSetup --scope user

    使い方:  & tools\Build-Installer.ps1     作ったインストーラーのフルパスを返す
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$exe  = Join-Path $root 'bin\Copipe.exe'
if (-not (Test-Path -LiteralPath $exe)) { throw "exe がありません: $exe" }

$iscc = @(
    (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe')
    (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe')
    (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')
) | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $iscc) {
    $cmd = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($cmd) { $iscc = $cmd.Source }
}
if (-not $iscc) {
    throw 'ISCC.exe (Inno Setup 6) が見つかりません。winget install JRSoftware.InnoSetup --scope user で入れてください'
}

$version = (Get-Item -LiteralPath $exe).VersionInfo.ProductVersion
$iss = Join-Path $root 'installer\Copipe.iss'
Write-Host "インストーラーを作成中 (バージョン $version)" -ForegroundColor Cyan
$isccOutput = & $iscc '/Q' "/DAppVersion=$version" $iss 2>&1
if ($LASTEXITCODE -ne 0) {
    $isccOutput | ForEach-Object { Write-Host ([string]$_) -ForegroundColor Red }
    throw "インストーラーの作成に失敗しました (exit $LASTEXITCODE)"
}
$setup = Join-Path $root "bin\Copipe-Setup-$version.exe"
Write-Host "インストーラー作成成功: $setup" -ForegroundColor Green
$setup
