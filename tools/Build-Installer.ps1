<#
    bin\Copipe.exe からインストーラー (bin\Copipe-Setup-<版>.exe) を作る (コミットの前に、build.ps1 の後で実行する)

    exe はビルドし直さない。バージョンは exe の製品バージョン (AssemblyInformationalVersion) を使う。
    Inno Setup 7 (または 6) が要る: winget install JRSoftware.InnoSetup --scope user
    見つからなければ警告を出して何もしない (Inno Setup の無い PC でもビルドは通す)。

    使い方:  & tools\Build-Installer.ps1     作ったインストーラーのフルパスを返す
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$exe  = Join-Path $root 'bin\Copipe.exe'
if (-not (Test-Path -LiteralPath $exe)) { throw "exe がありません: $exe" }

$iscc = foreach ($v in 7, 6) {
    (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup $v\ISCC.exe")
    (Join-Path ${env:ProgramFiles(x86)} "Inno Setup $v\ISCC.exe")
    (Join-Path $env:ProgramFiles "Inno Setup $v\ISCC.exe")
}
$iscc = $iscc | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $iscc) {
    $cmd = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($cmd) { $iscc = $cmd.Source }
}
if (-not $iscc) {
    Write-Warning 'ISCC.exe (Inno Setup 7 / 6) が見つからないので、インストーラーは作りません。winget install JRSoftware.InnoSetup --scope user で入れてください'
    return
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

# 最新の 1 つだけ残す (古い版は git の履歴から取り出せる)
Get-ChildItem -LiteralPath (Join-Path $root 'bin') -Filter 'Copipe-Setup-*.exe' |
    Where-Object { $_.FullName -ne $setup } |
    ForEach-Object { Write-Host "古いインストーラーを削除: $($_.Name)" -ForegroundColor DarkGray; Remove-Item -LiteralPath $_.FullName }
$setup
