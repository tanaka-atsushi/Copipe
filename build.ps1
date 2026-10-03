<#
    Copipe ビルドスクリプト

    Windows 標準の csc.exe (.NET Framework 4.8) のみを使う。SDK のインストールも
    管理者権限も不要。UI は WinForms なので XAML の埋め込みは無い。

    使い方:  .\build.ps1             通常ビルド
             .\build.ps1 -Run        ビルドして起動
             .\build.ps1 -Installer  ビルドして、インストーラー (bin\Copipe-Setup-<版>.exe) も作る
                                     (Inno Setup 6 が要る: winget install JRSoftware.InnoSetup --scope user)
#>
[CmdletBinding()]
param(
    [switch]$Run,
    [switch]$Installer,
    # -Debug は PowerShell の共通パラメーターと衝突するため別名にしている
    [switch]$DebugBuild
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$csc  = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'

if (-not (Test-Path -LiteralPath $csc)) {
    throw "csc.exe が見つかりません: $csc"
}

$outDir = Join-Path $root 'bin'
if (-not (Test-Path -LiteralPath $outDir)) {
    New-Item -ItemType Directory -Path $outDir | Out-Null
}
$outExe = Join-Path $outDir 'Copipe.exe'

# --- 起動中の Copipe を終了 -----------------------------------------------
# 常駐アプリなので起動したままだと exe がロックされ、上書きできない。
# 強制終了するとトレイにアイコンの抜け殻が残るので、正常終了を依頼する。
& (Join-Path $root 'tools\Stop-Copipe.ps1') -ExePath $outExe

# --- ソース収集 -----------------------------------------------------------
$sources = Get-ChildItem -Path (Join-Path $root 'src') -Recurse -Filter *.cs |
           Select-Object -ExpandProperty FullName
if (-not $sources) { throw 'src 配下に .cs が見つかりません' }

# --- 参照アセンブリ -------------------------------------------------------
# いずれも csc.exe と同じフォルダーにあり、既定の検索パスで解決できる。
$refs = @(
    'System.dll'
    'System.Drawing.dll'
    'System.Windows.Forms.dll'
    'System.Runtime.Serialization.dll'   # 履歴・定型文の JSON 保存 (DataContractJsonSerializer)
    'System.Xml.dll'                     # 定型文を字下げして書く JSON ライター (XmlDictionaryWriter) の基底クラス
)

# --- アイコン -------------------------------------------------------------
# Claude Design で作った 16〜256 の大きさ入りの ico
$iconFile = Join-Path $root 'assets\icon\copipe.ico'
if (-not (Test-Path -LiteralPath $iconFile)) { throw "アイコンが見つかりません: $iconFile" }
$qrFile = Join-Path $root 'assets\bmc-qr.png'
if (-not (Test-Path -LiteralPath $qrFile)) { throw "QR コードの画像が見つかりません: $qrFile" }

# $args は PowerShell の自動変数なので別名を使う
$cscArgs = @(
    '/nologo'
    '/target:winexe'
    '/platform:anycpu'
    '/langversion:5'                     # csc 4.8 は C# 5 までしか受け付けない
    '/codepage:65001'                    # ソースは BOM なし UTF-8。既定だと ANSI (Shift-JIS) として読まれる
    '/warnaserror-'
    '/warn:4'
    "/out:$outExe"
    # アイコン: exe 自体のもの (エクスプローラー・タスクバー) と、トレイ用に読むための埋め込み
    "/win32icon:$iconFile"
    "/resource:$iconFile,Copipe.copipe.ico"
    # About に出す Buy Me a Coffee の QR コード
    "/resource:$qrFile,Copipe.bmc-qr.png"
)
if ($DebugBuild) { $cscArgs += '/debug:full'; $cscArgs += '/define:DEBUG' } else { $cscArgs += '/optimize+' }
$cscArgs += ($refs | ForEach-Object { "/reference:$_" })
$cscArgs += $sources

Write-Host "ビルド中: $outExe" -ForegroundColor Cyan
Write-Host ("  ソース {0} ファイル" -f $sources.Count) -ForegroundColor DarkGray

$output = & $csc @cscArgs 2>&1
$exit = $LASTEXITCODE

foreach ($line in $output) {
    $text = [string]$line
    if ($text -match ': error ') {
        Write-Host $text -ForegroundColor Red
    } elseif ($text -match ': warning ') {
        Write-Host $text -ForegroundColor Yellow
    } elseif ($text.Trim()) {
        Write-Host $text
    }
}

if ($exit -ne 0) {
    $errCount = @($output | Where-Object { [string]$_ -match ': error ' }).Count
    throw "ビルドに失敗しました (エラー $errCount 件 / exit $exit)"
}

Write-Host "ビルド成功: $outExe" -ForegroundColor Green

# --- インストーラー -------------------------------------------------------
if ($Installer) {
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

    # バージョンは AssemblyInformationalVersion (exe の製品バージョン) を使う
    $version = (Get-Item -LiteralPath $outExe).VersionInfo.ProductVersion
    $iss = Join-Path $root 'installer\Copipe.iss'
    Write-Host "インストーラーを作成中 (バージョン $version)" -ForegroundColor Cyan
    $isccOutput = & $iscc '/Q' "/DAppVersion=$version" $iss 2>&1
    if ($LASTEXITCODE -ne 0) {
        $isccOutput | ForEach-Object { Write-Host ([string]$_) -ForegroundColor Red }
        throw "インストーラーの作成に失敗しました (exit $LASTEXITCODE)"
    }
    Write-Host ("インストーラー作成成功: {0}" -f (Join-Path $outDir "Copipe-Setup-$version.exe")) -ForegroundColor Green
}

if ($Run) {
    Write-Host '起動します...' -ForegroundColor Cyan
    Start-Process -FilePath $outExe
}
