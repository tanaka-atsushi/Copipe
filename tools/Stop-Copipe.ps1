<#
    起動中の Copipe を終了する (build.ps1 と tools\verify.ps1 から呼ぶ)

    小窓 (タイトル "Copipe") に WM_CLOSE を送り、正常に終了させる。
    強制終了 (Stop-Process -Force) だとトレイアイコンが削除されず、マウスを重ねるまで
    通知領域にアイコンの抜け殻が残るため、正常に終わらなかったときだけ強制終了する。

    使い方:  & tools\Stop-Copipe.ps1 -ExePath <bin\Copipe.exe のフルパス>
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ExePath,
    [int]$TimeoutMs = 5000
)

$ErrorActionPreference = 'Stop'

$targets = @(Get-Process -Name 'Copipe' -ErrorAction SilentlyContinue |
             Where-Object { $_.Path -and ($_.Path -ieq $ExePath) })
if ($targets.Count -eq 0) { return }

if (-not ('CopipeStop.Win' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Text;

namespace CopipeStop
{
    public static class Win
    {
        private delegate bool EnumProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc proc, IntPtr lParam);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr h, StringBuilder sb, int max);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr h, StringBuilder sb, int max);
        [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);

        /// <summary>プロセスのトップレベルウインドウから小窓 (タイトル Copipe、ダイアログ以外) を探す。</summary>
        public static IntPtr FindPopup(int pid)
        {
            IntPtr found = IntPtr.Zero;
            EnumWindows(delegate(IntPtr h, IntPtr l)
            {
                uint p;
                GetWindowThreadProcessId(h, out p);
                if (p != (uint)pid) { return true; }
                StringBuilder cls = new StringBuilder(64);
                GetClassName(h, cls, cls.Capacity);
                StringBuilder title = new StringBuilder(64);
                GetWindowText(h, title, title.Capacity);
                if (cls.ToString() != "#32770" && title.ToString() == "Copipe")
                {
                    found = h;
                    return false;
                }
                return true;
            }, IntPtr.Zero);
            return found;
        }
    }
}
'@
}

foreach ($p in $targets) {
    Write-Host ("起動中の Copipe を終了します (PID {0})" -f $p.Id) -ForegroundColor Yellow
    $popup = [CopipeStop.Win]::FindPopup($p.Id)
    if ($popup -ne [IntPtr]::Zero) {
        [void][CopipeStop.Win]::PostMessage($popup, 0x0010 <# WM_CLOSE #>, [IntPtr]::Zero, [IntPtr]::Zero)
    }
    if (-not $p.WaitForExit($TimeoutMs)) {
        Write-Host ("  正常に終了しなかったので強制終了します (PID {0})" -f $p.Id) -ForegroundColor Yellow
        $p.Kill()
        [void]$p.WaitForExit(5000)
    }
}
