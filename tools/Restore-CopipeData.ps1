<#
    途中で止まった検証 (tools\verify.ps1) が書き換えたままの、利用者の設定・履歴・定型文を元に戻す

    verify.ps1 は E2E の前に %LOCALAPPDATA%\Copipe の settings.ini・history.json・phrases.json を
    %TEMP%\CopipeVerify-<番号> に退避し、最後に元へ戻す。検証を途中で止める (ウインドウを閉じる・
    プロセスが強制終了される) と、戻す処理が動かず、検証用の中身が残る。
    退避したときに「まだ戻していない」印 (restore-pending.txt) を書いておき、このスクリプトはその印が
    残っている退避から元に戻す。verify.ps1 も始めにこれを呼ぶので、次に検証を流しても戻る。

    使い方:  powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools\Restore-CopipeData.ps1
#>
[CmdletBinding()]
param(
    [string]$ExePath
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
if (-not $ExePath) { $ExePath = Join-Path $root 'bin\Copipe.exe' }

$marker = 'restore-pending.txt'
$pending = @(Get-ChildItem -LiteralPath $env:TEMP -Directory -Filter 'CopipeVerify-*' -ErrorAction SilentlyContinue |
             Where-Object { Test-Path -LiteralPath (Join-Path $_.FullName $marker) } |
             Sort-Object CreationTime)
if ($pending.Count -eq 0) {
    Write-Host '戻す必要のある退避はありません (途中で止まった検証はありません)。' -ForegroundColor DarkCyan
    return
}

# 止まった検証が 2 回続いたときは、後の検証は前の検証の中身を退避している。
# 利用者の本来の中身は一番古い退避にある
$source = $pending[0].FullName

# 起動中の Copipe が戻した後の履歴を上書きしないよう、先に止める (最後に起動し直す)
$wasRunning = @(Get-Process -Name 'Copipe' -ErrorAction SilentlyContinue |
                Where-Object { $_.Path -and ($_.Path -ieq $ExePath) }).Count -gt 0
& (Join-Path $root 'tools\Stop-Copipe.ps1') -ExePath $ExePath

$dataDir = Join-Path $env:LOCALAPPDATA 'Copipe'
if (-not (Test-Path -LiteralPath $dataDir)) { New-Item -ItemType Directory -Path $dataDir | Out-Null }

# 印には、退避した時点で各ファイルがあったか (present / absent) が書いてある
$states = @{}
foreach ($line in Get-Content -LiteralPath (Join-Path $source $marker)) {
    $parts = $line -split '=', 2
    if ($parts.Count -eq 2) { $states[$parts[0].Trim()] = $parts[1].Trim() }
}
$files = @(
    @{ Key = 'settings'; Name = 'settings.ini';  Backup = 'settings-backup.ini' },
    @{ Key = 'history';  Name = 'history.json';  Backup = 'history-backup.json' },
    @{ Key = 'phrases';  Name = 'phrases.json';  Backup = 'phrases-backup.json' }
)
foreach ($f in $files) {
    $dest = Join-Path $dataDir $f.Name
    $backup = Join-Path $source $f.Backup
    if ($states[$f.Key] -eq 'present' -and (Test-Path -LiteralPath $backup)) {
        Copy-Item -LiteralPath $backup -Destination $dest -Force
        Write-Host ("  {0} を戻しました" -f $f.Name)
    } elseif ($states[$f.Key] -eq 'absent' -and (Test-Path -LiteralPath $dest)) {
        # 検証の前には無かったファイル (検証が作ったもの)
        Remove-Item -LiteralPath $dest -Force
        Write-Host ("  {0} を消しました (検証の前には無かったため)" -f $f.Name)
    }
}

foreach ($dir in $pending) { Remove-Item -LiteralPath $dir.FullName -Recurse -Force }
Write-Host ("途中で止まった検証の退避 ({0}) から、設定・履歴・定型文を元に戻しました。" -f (Split-Path -Leaf $source)) -ForegroundColor DarkCyan

if ($wasRunning) {
    Start-Process -FilePath $ExePath
    Write-Host 'Copipe を起動し直しました。' -ForegroundColor DarkCyan
}
