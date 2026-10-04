<#
    Copipe の検証ハーネス

    ビルド済みの bin\Copipe.exe を読み込んで純粋関数とクリップボード読み取りを検証し、
    さらに exe を実際に起動して「ホットキーを押している間だけ小窓が出る」ことを E2E で確認する。

    Windows PowerShell 5.1 (.NET Framework 4.8) で実行すること。
    PowerShell 7 は .NET 8 のため、この 4.8 向けアセンブリの検証には使わない。

    実行:  powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools\verify.ps1
           E2E を片方だけ:  ... -E2E History (履歴モード) / -E2E Phrases (定型文モード)
           E2E を省く:      ... -SkipE2E (マウスとキーボードを使わない。数秒で終わる)

    注意:
      - クリップボードを書き換える (元がテキストなら最後に戻す)
      - 起動中の Copipe を終了する
      - 設定ファイル (%LOCALAPPDATA%\Copipe\settings.ini) を書き換える (最後に元へ戻す)
      - E2E ではマウスカーソルを動かし、設定したホットキーを擬似入力し、
        表示中の小窓の中央を 1 回左クリックする (カーソル位置は最後に戻す)。
      - E2E の間は、他のアプリのウインドウをすべて最小化する (最後に元へ戻す)。
        実行中はマウスとキーボードに触らないこと
#>
[CmdletBinding()]
param(
    # E2E (exe を起動してキー入力を送る部分) を省略する
    [switch]$SkipE2E,
    # E2E のうち流す部分。All (既定) / History (履歴モード) / Phrases (定型文モード)。
    # マウスとキーボードを使わない検証 1〜5 は、どれでも流す (数秒で終わる)
    [ValidateSet('All', 'History', 'Phrases')]
    [string]$E2E = 'All',
    # 指定すると、E2E で表示中の小窓を撮影して PNG で保存する (見た目の確認用)
    [string]$ScreenshotPath
)

$ErrorActionPreference = 'Stop'

if ($PSVersionTable.PSEdition -eq 'Core') {
    throw '.NET Framework 4.8 のアセンブリを読むため Windows PowerShell 5.1 で実行してください。'
}

$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$exe  = Join-Path $root 'bin\Copipe.exe'
if (-not (Test-Path -LiteralPath $exe)) { throw "ビルドされていません: $exe" }

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
[void][Reflection.Assembly]::LoadFrom($exe)
# 表示の言葉は日本語で確かめる (Windows PowerShell 5.1 は、Windows が日本語でも UI の言語を英語にしていることがある)
[Copipe.Lang]::Japanese = $true

if ([Threading.Thread]::CurrentThread.ApartmentState -ne 'STA') {
    throw 'クリップボードを操作するため STA で実行してください (powershell.exe の既定は STA)。'
}

# 前に途中で止まった検証が、利用者の設定・履歴・定型文を書き換えたままなら、先に元へ戻す
# (戻さずに始めると、検証用の中身を「利用者のもの」として退避してしまう)
& (Join-Path $root 'tools\Restore-CopipeData.ps1') -ExePath $exe

# exe に入っているアイコンの数 (エクスプローラーやタスクバーに出るもの)
Add-Type -Namespace CopipeVerify -Name Shell -MemberDefinition @'
[DllImport("shell32.dll", CharSet = CharSet.Unicode)]
private static extern uint ExtractIconEx(string file, int index, IntPtr[] large, IntPtr[] small, uint count);
public static int IconCount(string file) { return (int)ExtractIconEx(file, -1, null, null, 0); }
'@

# ハーネス用の Win32 API
Add-Type -Namespace CopipeVerify -Name Native -MemberDefinition @'
[DllImport("user32.dll", SetLastError = true)]
public static extern bool OpenClipboard(IntPtr hWndNewOwner);
[DllImport("user32.dll", SetLastError = true)]
public static extern bool CloseClipboard();
[DllImport("user32.dll", SetLastError = true)]
public static extern bool EmptyClipboard();
[DllImport("user32.dll", SetLastError = true)]
public static extern IntPtr SetClipboardData(uint format, IntPtr hMem);
[DllImport("kernel32.dll", SetLastError = true)]
public static extern IntPtr GlobalAlloc(uint flags, UIntPtr bytes);
[DllImport("kernel32.dll", SetLastError = true)]
public static extern IntPtr GlobalLock(IntPtr hMem);
[DllImport("kernel32.dll", SetLastError = true)]
public static extern bool GlobalUnlock(IntPtr hMem);
[DllImport("kernel32.dll", SetLastError = true)]
public static extern IntPtr GlobalFree(IntPtr hMem);
[DllImport("user32.dll")]
public static extern IntPtr GetClipboardOwner();
'@

Add-Type -ReferencedAssemblies System.Drawing, Accessibility -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;

namespace CopipeVerify
{
    public static class Win
    {
        [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X; public int Y; }
        [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left; public int Top; public int Right; public int Bottom; }
        private delegate bool EnumProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] private static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
        [DllImport("user32.dll")] private static extern void mouse_event(uint flags, int dx, int dy, uint data, UIntPtr extra);
        [DllImport("user32.dll")] private static extern uint MapVirtualKey(uint code, uint mapType);
        [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
        [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT p);
        [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(POINT p);
        [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr h, uint flags);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr h, int index);
        [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr h, int id, uint modifiers, uint vk);
        [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr h, int id);

        /// <summary>ListBox の項目を外から読む (LB_GETCOUNT / LB_GETTEXTLEN / LB_GETTEXT)。</summary>
        public static string[] ListItems(IntPtr listBox)
        {
            UIntPtr result;
            if (SendMessageTimeout(listBox, 0x018B /* LB_GETCOUNT */, IntPtr.Zero, IntPtr.Zero, SMTO_ABORTIFHUNG, 2000, out result) == IntPtr.Zero)
            {
                return null;
            }
            int count = (int)result.ToUInt32();
            List<string> items = new List<string>();
            // LB_GETTEXT は受け取る側の大きさを見ずに書き込む。長さを聞いてから読むまでの間に一覧が作り直されて
            // 長い行に変わると、はみ出して PowerShell ごと落ちる (実測)。行の長さの上限 (履歴 1 件の上限 10 万文字 +
            // 📌 などの印) より十分大きい領域を使い回し、それより長い行は読まない
            const int BufferChars = 262144;
            IntPtr buffer = Marshal.AllocHGlobal(BufferChars * 2);
            try
            {
                for (int i = 0; i < count; i++)
                {
                    if (SendMessageTimeout(listBox, 0x018A /* LB_GETTEXTLEN */, new IntPtr(i), IntPtr.Zero, SMTO_ABORTIFHUNG, 2000, out result) == IntPtr.Zero)
                    {
                        return null;
                    }
                    // 読んでいる途中で一覧が作り直されると LB_ERR (-1) が返る。そのときは読めなかったことにする
                    ulong length = result.ToUInt64();
                    if (length >= BufferChars)
                    {
                        return null;
                    }
                    UIntPtr copied;
                    if (SendMessageTimeoutW(listBox, 0x0189 /* LB_GETTEXT */, new IntPtr(i), buffer, SMTO_ABORTIFHUNG, 2000, out copied) == IntPtr.Zero ||
                        copied.ToUInt64() >= BufferChars)
                    {
                        return null;
                    }
                    items.Add(Marshal.PtrToStringUni(buffer, (int)copied.ToUInt64()));
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
            return items.ToArray();
        }
        // 文字列を受け取る先を IntPtr で渡すときは、Unicode 版を明示する (既定の ANSI 版では Shift_JIS で返る)
        [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW")]
        private static extern IntPtr SendMessageTimeoutW(IntPtr h, uint msg, IntPtr w, IntPtr l, uint flags, uint timeout, out UIntPtr result);
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc proc, IntPtr lParam);
        [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr parent, EnumProc proc, IntPtr lParam);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr h, StringBuilder sb, int max);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessageTimeout(IntPtr h, uint msg, IntPtr w, StringBuilder l, uint flags, uint timeout, out UIntPtr result);
        [DllImport("user32.dll")]
        private static extern IntPtr SendMessageTimeout(IntPtr h, uint msg, IntPtr w, IntPtr l, uint flags, uint timeout, out UIntPtr result);

        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        private const uint MOUSEEVENTF_LEFTUP = 0x0004;
        private const uint MAPVK_VK_TO_VSC = 0;
        private const uint GA_ROOT = 2;
        private const uint SMTO_ABORTIFHUNG = 0x0002;

        public static void KeyDown(byte vk) { keybd_event(vk, (byte)MapVirtualKey(vk, MAPVK_VK_TO_VSC), 0, UIntPtr.Zero); }
        public static void KeyUp(byte vk) { keybd_event(vk, (byte)MapVirtualKey(vk, MAPVK_VK_TO_VSC), KEYEVENTF_KEYUP, UIntPtr.Zero); }
        public static void LeftClick() { mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero); mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero); }
        public static void DoubleClick() { LeftClick(); LeftClick(); }
        public static void LeftDown() { mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero); }
        public static void LeftUp() { mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero); }
        /// <summary>カーソルを動かし、マウスが動いたという入力も送る (ドラッグ中の WM_MOUSEMOVE を確実に出す)。</summary>
        public static void MoveMouse(int x, int y) { SetCursorPos(x, y); mouse_event(0x0001 /* MOVE */, 0, 0, 0, UIntPtr.Zero); }
        /// <summary>マウスのホイールを回す (delta: 上へ +120、下へ -120 が 1 ノッチ)。</summary>
        public static void Wheel(int delta) { mouse_event(0x0800 /* WHEEL */, 0, 0, unchecked((uint)delta), UIntPtr.Zero); }
        public static void RightClick() { mouse_event(0x0008 /* RIGHTDOWN */, 0, 0, 0, UIntPtr.Zero); mouse_event(0x0010 /* RIGHTUP */, 0, 0, 0, UIntPtr.Zero); }
        [DllImport("user32.dll")] public static extern bool IsWindowEnabled(IntPtr h);
        [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr h);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessageTimeout(IntPtr h, uint msg, IntPtr w, string l, uint flags, uint timeout, out UIntPtr result);

        [DllImport("oleacc.dll")]
        private static extern int AccessibleObjectFromWindow(IntPtr h, uint id, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out object obj);
        [DllImport("oleacc.dll")]
        private static extern int AccessibleChildren(Accessibility.IAccessible acc, int start, int count, [Out] object[] children, out int obtained);

        /// <summary>
        /// WinForms のメニュー (ToolStripDropDown) の項目を MSAA で読む。メニューでなければ空。
        /// UI オートメーションからは項目が見えないため (実測)、MSAA を直接使う。
        /// </summary>
        public static List<KeyValuePair<string, Rectangle>> MenuItems(IntPtr h)
        {
            List<KeyValuePair<string, Rectangle>> items = new List<KeyValuePair<string, Rectangle>>();
            try
            {
                ReadMenuItems(h, items);
            }
            catch (ArgumentException) { }       // 読んでいる途中でメニューが閉じた
            catch (COMException) { }
            return items;
        }

        private static void ReadMenuItems(IntPtr h, List<KeyValuePair<string, Rectangle>> items)
        {
            Guid iid = new Guid("618736E0-3C3D-11CF-810C-00AA00389B71");   // IAccessible
            object obj;
            if (AccessibleObjectFromWindow(h, 0xFFFFFFFC /* OBJID_CLIENT */, ref iid, out obj) != 0)
            {
                return;
            }
            Accessibility.IAccessible menu = obj as Accessibility.IAccessible;
            if (menu == null || !(menu.get_accRole(0) is int) || (int)menu.get_accRole(0) != 11 /* ROLE_SYSTEM_MENUPOPUP */)
            {
                return;
            }
            int count = menu.accChildCount;
            object[] children = new object[count];
            int obtained;
            AccessibleChildren(menu, 0, count, children, out obtained);
            for (int i = 0; i < obtained; i++)
            {
                // WinForms のメニューは項目ごとのオブジェクト、Windows 標準のメニュー (#32768) は子の番号で返る
                Accessibility.IAccessible child = children[i] as Accessibility.IAccessible;
                Accessibility.IAccessible owner = child ?? menu;
                object id = child != null ? (object)0 : children[i];
                int left, top, width, height;
                owner.accLocation(out left, out top, out width, out height, id);
                items.Add(new KeyValuePair<string, Rectangle>(owner.get_accName(id), new Rectangle(left, top, width, height)));
            }

        }

        /// <summary>他のプロセスの入力欄に文字を入れる (WM_SETTEXT。WinForms の TextChanged も起きる)。</summary>
        public static bool SetText(IntPtr h, string text)
        {
            UIntPtr result;
            return SendMessageTimeout(h, 0x000C /* WM_SETTEXT */, IntPtr.Zero, text, SMTO_ABORTIFHUNG, 2000, out result) != IntPtr.Zero;
        }

        /// <summary>ListBox で選ばれている項目の位置 (LB_GETCURSEL)。選ばれていなければ -1。</summary>
        public static int ListSelectedIndex(IntPtr listBox)
        {
            UIntPtr result;
            if (SendMessageTimeout(listBox, 0x0188 /* LB_GETCURSEL */, IntPtr.Zero, IntPtr.Zero, SMTO_ABORTIFHUNG, 2000, out result) == IntPtr.Zero)
            {
                return -2;
            }
            return (int)result.ToUInt64();
        }

        /// <summary>ListBox の 1 行の高さ (LB_GETITEMHEIGHT)。</summary>
        public static int ListItemHeight(IntPtr listBox)
        {
            UIntPtr result;
            if (SendMessageTimeout(listBox, 0x01A1 /* LB_GETITEMHEIGHT */, IntPtr.Zero, IntPtr.Zero, SMTO_ABORTIFHUNG, 2000, out result) == IntPtr.Zero)
            {
                return -1;
            }
            return (int)result.ToUInt32();
        }

        public static Point GetCursor() { POINT p; GetCursorPos(out p); return new Point(p.X, p.Y); }

        [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vk);

        /// <summary>そのキーが今押されているか。</summary>
        public static bool IsKeyDown(byte vk)
        {
            return (GetAsyncKeyState(vk) & 0x8000) != 0;
        }

        [DllImport("user32.dll")] private static extern short GetKeyState(int vk);
        /// <summary>CapsLock などがオンか。</summary>
        public static bool IsToggled(byte vk)
        {
            return (GetKeyState(vk) & 1) != 0;
        }

        // ---- キーが Copipe に握りつぶされずに届いたかを数える (低レベルのキーボードフック) ----
        // 低レベルのフックは後から付けたものが先に呼ばれるので、Copipe を起動する前に付けておくと、
        // Copipe のフックが通したキーだけがここに届く。CapsLock を握りつぶしたことを、オン・オフの読み取り
        // (読むスレッドによっては古い値になる) に頼らずに確かめられる。
        // フックはキー入力をこちらの返事まで待たせるので、メッセージを処理し続ける専用のスレッドで動かす
        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint threadId);
        [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hhk);
        [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string name);
        [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
        [StructLayout(LayoutKind.Sequential)] private struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam; public IntPtr lParam; public uint time; public int x; public int y; }
        [DllImport("user32.dll")] private static extern int GetMessage(out MSG msg, IntPtr hWnd, uint min, uint max);
        [DllImport("user32.dll")] private static extern bool PeekMessage(out MSG msg, IntPtr hWnd, uint min, uint max, uint remove);
        [DllImport("user32.dll")] private static extern bool PostThreadMessage(uint threadId, uint msg, IntPtr wParam, IntPtr lParam);
        private static LowLevelKeyboardProc keyWatchProc;
        private static System.Threading.Thread keyWatchThread;
        private static uint keyWatchThreadId;
        private static int keyWatchVk;
        private static int keyWatchDowns;

        /// <summary>vk の押し下げを数え始める。付けられなければ false。</summary>
        public static bool StartKeyWatch(int vk)
        {
            StopKeyWatch();
            keyWatchVk = vk;
            keyWatchDowns = 0;
            keyWatchProc = KeyWatchCallback;
            System.Threading.ManualResetEvent ready = new System.Threading.ManualResetEvent(false);
            bool hooked = false;
            keyWatchThread = new System.Threading.Thread(delegate()
            {
                keyWatchThreadId = GetCurrentThreadId();
                MSG msg;
                PeekMessage(out msg, IntPtr.Zero, 0, 0, 0);   // WM_QUIT を受け取れるようキューを作る
                IntPtr hook = SetWindowsHookEx(13 /* WH_KEYBOARD_LL */, keyWatchProc, GetModuleHandle(null), 0);
                hooked = hook != IntPtr.Zero;
                ready.Set();
                if (!hooked) { return; }
                while (GetMessage(out msg, IntPtr.Zero, 0, 0) > 0) { }
                UnhookWindowsHookEx(hook);
            });
            keyWatchThread.IsBackground = true;
            keyWatchThread.Start();
            ready.WaitOne(3000);
            return hooked;
        }

        /// <summary>数え始めてから (または ResetKeyWatch から)、vk の押し下げが届いた回数。</summary>
        public static int KeyWatchDowns { get { return System.Threading.Interlocked.CompareExchange(ref keyWatchDowns, 0, 0); } }

        public static void ResetKeyWatch() { System.Threading.Interlocked.Exchange(ref keyWatchDowns, 0); }

        public static void StopKeyWatch()
        {
            if (keyWatchThread == null) { return; }
            if (keyWatchThread.IsAlive)
            {
                PostThreadMessage(keyWatchThreadId, 0x0012 /* WM_QUIT */, IntPtr.Zero, IntPtr.Zero);
                keyWatchThread.Join(1000);
            }
            keyWatchThread = null;
        }

        private static IntPtr KeyWatchCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            // KBDLLHOOKSTRUCT: vkCode (0)、scanCode (4)、flags (8)。flags の 0x80 は押し上げ
            if (nCode >= 0 && Marshal.ReadInt32(lParam) == keyWatchVk && (Marshal.ReadInt32(lParam, 8) & 0x80) == 0)
            {
                System.Threading.Interlocked.Increment(ref keyWatchDowns);
            }
            return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
        }

        /// <summary>そのウインドウのスレッドが、Timeout ミリ秒以内にメッセージを処理するか (WM_NULL を送って確かめる)。</summary>
        public static bool Responds(IntPtr window, uint timeout)
        {
            UIntPtr result;
            return SendMessageTimeout(window, 0x0000 /* WM_NULL */, IntPtr.Zero, IntPtr.Zero, SMTO_ABORTIFHUNG, timeout, out result) != IntPtr.Zero;
        }

        /// <summary>そのホットキーが空いているか (登録してすぐ解除して確かめる)。</summary>
        public static bool CanRegisterHotkey(IntPtr window, uint modifiers, uint vk)
        {
            if (!RegisterHotKey(window, 9001, modifiers | 0x4000 /* MOD_NOREPEAT */, vk)) { return false; }
            UnregisterHotKey(window, 9001);
            return true;
        }

        /// <summary>他のアプリがそのホットキーを使っている状態を作る (ReleaseHotkey で解除する)。</summary>
        public static bool HoldHotkey(IntPtr window, uint modifiers, uint vk)
        {
            return RegisterHotKey(window, 9002, modifiers | 0x4000 /* MOD_NOREPEAT */, vk);
        }

        public static void ReleaseHotkey(IntPtr window)
        {
            UnregisterHotKey(window, 9002);
        }

        /// <summary>画面上の点にあるウインドウのトップレベルウインドウ。</summary>
        public static IntPtr RootWindowAt(int x, int y)
        {
            POINT p;
            p.X = x;
            p.Y = y;
            IntPtr h = WindowFromPoint(p);
            return h == IntPtr.Zero ? IntPtr.Zero : GetAncestor(h, GA_ROOT);
        }

        public static Rectangle GetRect(IntPtr h)
        {
            RECT r;
            GetWindowRect(h, out r);
            return Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
        }

        public static string GetText(IntPtr h)
        {
            UIntPtr len;
            if (SendMessageTimeout(h, 0x000E /* WM_GETTEXTLENGTH */, IntPtr.Zero, IntPtr.Zero, SMTO_ABORTIFHUNG, 2000, out len) == IntPtr.Zero)
            {
                return null;
            }
            StringBuilder sb = new StringBuilder((int)len.ToUInt32() + 1);
            UIntPtr copied;
            SendMessageTimeout(h, 0x000D /* WM_GETTEXT */, new IntPtr(sb.Capacity), sb, SMTO_ABORTIFHUNG, 2000, out copied);
            return sb.ToString();
        }

        public static string GetClass(IntPtr h)
        {
            StringBuilder sb = new StringBuilder(256);
            GetClassName(h, sb, sb.Capacity);
            return sb.ToString();
        }

        public static IntPtr[] TopWindows(int pid)
        {
            List<IntPtr> list = new List<IntPtr>();
            EnumWindows(delegate(IntPtr h, IntPtr l)
            {
                uint p;
                GetWindowThreadProcessId(h, out p);
                if (p == (uint)pid) { list.Add(h); }
                return true;
            }, IntPtr.Zero);
            return list.ToArray();
        }

        public static IntPtr[] Children(IntPtr parent)
        {
            List<IntPtr> list = new List<IntPtr>();
            EnumChildWindows(parent, delegate(IntPtr h, IntPtr l) { list.Add(h); return true; }, IntPtr.Zero);
            return list.ToArray();
        }
    }
}
'@

# 座標を実ピクセルで比べるため、ウインドウを作る前に DPI 対応を宣言する (Copipe 本体と同じ)
[void][CopipeVerify.Win]::SetProcessDPIAware()

$script:Pass = 0
$script:Fail = 0

function Check {
    param([string]$Name, [bool]$Condition, [string]$Detail = '')
    if ($Condition) {
        $script:Pass++
        Write-Host ("  [OK]   " + $Name) -ForegroundColor Green
    } else {
        $script:Fail++
        Write-Host ("  [FAIL] " + $Name) -ForegroundColor Red
        if ($Detail) { Write-Host ("         " + $Detail) -ForegroundColor DarkRed }
    }
}

function Info { param([string]$Text) Write-Host ("  [INFO] " + $Text) -ForegroundColor DarkCyan }

# セクション単位で例外を捕まえ、後続のセクションは続行する
function Section {
    param([string]$Title, [scriptblock]$Body)
    Write-Host "`n$Title" -ForegroundColor Cyan
    try {
        & $Body
    } catch {
        $script:Fail++
        Write-Host ("  [FAIL] 例外で中断: " + $_.Exception.Message) -ForegroundColor Red
        Write-Host ("         " + $_.InvocationInfo.PositionMessage) -ForegroundColor DarkRed
    }
}

# 例外を捕まえて、PowerShell が包んだ MethodInvocationException の中身を返す (例外が無ければ $null)
function Get-ThrownException {
    param([scriptblock]$Body)
    try { & $Body } catch {
        $e = $_.Exception
        if ($e -is [System.Management.Automation.MethodInvocationException] -and $e.InnerException) { return $e.InnerException }
        return $e
    }
    return $null
}

function Format-ExceptionText {
    param($Exception)
    if ($null -eq $Exception) { return '例外なし' }
    return $Exception.GetType().FullName + ': ' + $Exception.Message
}

function Pt   { param([int]$X, [int]$Y) New-Object System.Drawing.Point($X, $Y) }
function Sz   { param([int]$W, [int]$H) New-Object System.Drawing.Size($W, $H) }
function Rect { param([int]$X, [int]$Y, [int]$W, [int]$H) New-Object System.Drawing.Rectangle($X, $Y, $W, $H) }

# 条件を満たすまで待ち、かかった ms を返す (時間切れは -1)。
# Sleep だとタイマーの刻み (約 15.6 ms) でしか測れないので、待ち時間の計測のために空回りで待つ
function Wait-Until {
    param([scriptblock]$Condition, [int]$TimeoutMs)
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while ($sw.ElapsedMilliseconds -lt $TimeoutMs) {
        if (& $Condition) { return $sw.ElapsedMilliseconds }
        [void][Threading.Thread]::Yield()
    }
    if (& $Condition) { return $sw.ElapsedMilliseconds }
    return -1
}

# ==============================================================================
Section '検証 1: 表示位置の計算 (PopupPlacement)' {
# ==============================================================================
    $size = Sz 400 240
    $wa   = Rect 0 0 1920 1040

    $p = [Copipe.UI.PopupPlacement]::Place((Pt 100 100), $size, $wa, 16)
    Check '通常はカーソルの右下 (offset 分ずらす)' ($p.X -eq 116 -and $p.Y -eq 116) "got $p"

    $p = [Copipe.UI.PopupPlacement]::Place((Pt 1800 100), $size, $wa, 16)
    Check '右端では左側に回り込む' ($p.X -eq 1384 -and $p.Y -eq 116) "got $p"

    $p = [Copipe.UI.PopupPlacement]::Place((Pt 100 1000), $size, $wa, 16)
    Check '下端では上側に回り込む' ($p.X -eq 116 -and $p.Y -eq 744) "got $p"

    $p = [Copipe.UI.PopupPlacement]::Place((Pt 1919 1039), $size, $wa, 16)
    Check '右下隅では左上側に回り込む' ($p.X -eq 1503 -and $p.Y -eq 783) "got $p"

    $left = Rect -1920 130 1920 1032
    $p = [Copipe.UI.PopupPlacement]::Place((Pt -10 1150), $size, $left, 16)
    Check 'マイナス座標のモニター (右下隅付近) でも回り込む' ($p.X -eq -426 -and $p.Y -eq 894) "got $p"

    $p = [Copipe.UI.PopupPlacement]::Place((Pt -1900 140), $size, $left, 16)
    Check 'マイナス座標のモニター (左上付近) は右下に出る' ($p.X -eq -1884 -and $p.Y -eq 156) "got $p"

    $small = Rect 0 0 500 300
    $p = [Copipe.UI.PopupPlacement]::Place((Pt 250 150), $size, $small, 16)
    Check '左右どちらにも入らないときは作業領域に押し込む' ($p.X -eq 0 -and $p.Y -eq 0) "got $p"

    $tiny = Rect 100 50 300 200
    $p = [Copipe.UI.PopupPlacement]::Place((Pt 200 100), $size, $tiny, 16)
    Check '作業領域より大きいときは作業領域の左上に揃える' ($p.X -eq 100 -and $p.Y -eq 50) "got $p"

    # 実在しうる 2 枚のモニターを細かく走査し、はみ出し・カーソルとの重なりが無いことを確認
    $monitors = @((Rect 0 0 2560 1392), (Rect -1920 130 1920 1032))
    $outside = 0; $covers = 0; $total = 0
    foreach ($m in $monitors) {
        for ($x = $m.Left; $x -lt $m.Right; $x += 37) {
            for ($y = $m.Top; $y -lt $m.Bottom; $y += 29) {
                $total++
                $c = Pt $x $y
                $q = [Copipe.UI.PopupPlacement]::Place($c, $size, $m, 16)
                $r = New-Object System.Drawing.Rectangle($q, $size)
                if (-not $m.Contains($r)) { $outside++ }
                if ($r.Contains($c)) { $covers++ }
            }
        }
    }
    Check ("走査 $total 点: 作業領域からはみ出さない") ($outside -eq 0) "はみ出し $outside 件"
    Check ("走査 $total 点: 小窓がカーソルに重ならない") ($covers -eq 0) "重なり $covers 件"
}

# ==============================================================================
Section '検証 1b: アプリとトレイのアイコン (AppIcon)' {
# ==============================================================================
    $asm = [Copipe.UI.ItemNumber].Assembly
    Check 'アイコン: exe 自体にアイコンが付いている (エクスプローラー・タスクバー・Alt+Tab 用)' ([CopipeVerify.Shell]::IconCount($exe) -ge 1) "count=$([CopipeVerify.Shell]::IconCount($exe))"
    Check 'アイコン: exe に copipe.ico が埋め込まれている (トレイ用)' ($asm.GetManifestResourceNames() -contains 'Copipe.copipe.ico') "names=$($asm.GetManifestResourceNames() -join ',')"
    $AI = $asm.GetType('Copipe.UI.AppIcon', $true)
    $load = $AI.GetMethod('Load', [Reflection.BindingFlags]'Static, Public, NonPublic')
    # トレイの大きさ (拡大率 100% で 16、125% で 20、150% で 24、200% で 32) のものが ico に入っていて、そのまま読める
    foreach ($s in 16, 20, 24, 32) {
        $size = (New-Object System.Drawing.Size $s, $s).psobject.BaseObject
        $icon = $load.Invoke($null, [object[]]@($size))
        Check "アイコン: $s px のものをそのまま読める (縮めてぼやけない)" ($null -ne $icon -and $icon.Width -eq $s -and $icon.Height -eq $s) "got=$($icon.Width)x$($icon.Height)"
        if ($icon) { $icon.Dispose() }
    }
}

# ==============================================================================
Section '検証 2: 一覧に出す 1 行の文字列 (PreviewText)' {
# ==============================================================================
    $line = { param($text, $max) [Copipe.UI.PreviewText]::Line($text, $max) }

    Check '短いテキストはそのまま' ((& $line 'abc' 100) -ceq 'abc')
    Check '& は消えない' ((& $line 'A&B && C' 100) -ceq 'A&B && C')
    Check 'タブは空白にする' ((& $line "A`tB" 100) -ceq 'A B')
    Check '改行 (CRLF) は空白 1 つにする' ((& $line "1行目`r`n2行目" 100) -ceq '1行目 2行目')
    Check '改行 (LF だけ) も空白にする' ((& $line "1行目`n2行目" 100) -ceq '1行目 2行目')
    Check '前後の空白と改行は落とす' ((& $line "  `r`n abc `t " 100) -ceq 'abc')
    Check '空文字列は空のまま' ((& $line '' 100) -ceq '')
    Check 'null でも例外にしない' ((& $line $null 100) -ceq '')

    $long = New-Object string ([char]'x'), 150
    $d = & $line $long 100
    Check '上限を超えたら先頭だけ + …' ($d.Length -eq 101 -and $d.EndsWith('…') -and $d.StartsWith((New-Object string ([char]'x'), 100))) "length=$($d.Length)"

    $exact = New-Object string ([char]'y'), 100
    Check 'ちょうど上限なら … を付けない' ((& $line $exact 100) -ceq $exact)

    $emoji = [char]::ConvertFromUtf32(0x1F600)
    $surr = (New-Object string ([char]'a'), 99) + $emoji + 'b'
    $d = & $line $surr 100
    $lone = $false
    foreach ($ch in $d.ToCharArray()) { if ([char]::IsSurrogate($ch)) { $lone = $true } }
    Check 'サロゲートペアの途中では切らない' ((-not $lone) -and $d -ceq ((New-Object string ([char]'a'), 99) + '…')) "got length=$($d.Length)"

    foreach ($bad in 0, -1) {
        $e = Get-ThrownException { [void](& $line 'abc' $bad) }
        Check "maxChars が $bad なら ArgumentOutOfRangeException" ($e -is [ArgumentOutOfRangeException]) (Format-ExceptionText $e)
    }

    # 一覧の番号と数字キー (1〜9、0 の順。10 件目が 0。11 件目以降は番号なし)
    $IN = [Copipe.UI.ItemNumber]
    $K = [System.Windows.Forms.Keys]
    Check '番号: 1 件目は 1' ($IN::Label(0) -ceq '1')
    Check '番号: 9 件目は 9' ($IN::Label(8) -ceq '9')
    Check '番号: 10 件目は 0' ($IN::Label(9) -ceq '0')
    Check '番号: 11 件目以降は番号なし' ($null -eq $IN::Label(10) -and $null -eq $IN::Label(49))
    Check '番号: 範囲外 (-1) は番号なし' ($null -eq $IN::Label(-1))
    Check 'ピン止めの文字: 1 件目は a、26 件目は z' ($IN::PinLabel(0) -ceq 'a' -and $IN::PinLabel(25) -ceq 'z')
    Check 'ピン止めの文字: 27 件目以降と範囲外は無し' ($null -eq $IN::PinLabel(26) -and $null -eq $IN::PinLabel(-1))
    Check '数字キー: 上段の 1 は 1 件目' ($IN::IndexFromKey($K::D1) -eq 0)
    Check '数字キー: 上段の 9 は 9 件目' ($IN::IndexFromKey($K::D9) -eq 8)
    Check '数字キー: 上段の 0 は 10 件目' ($IN::IndexFromKey($K::D0) -eq 9)
    Check '数字キー: テンキーの 1 は 1 件目' ($IN::IndexFromKey($K::NumPad1) -eq 0)
    Check '数字キー: テンキーの 0 は 10 件目' ($IN::IndexFromKey($K::NumPad0) -eq 9)
    Check '数字キー: 修飾キーのビットは無視する' ($IN::IndexFromKey(($K::Control -bor $K::D2)) -eq 1)
    Check '数字キー: 数字以外は -1' ($IN::IndexFromKey($K::A) -eq -1 -and $IN::IndexFromKey($K::F1) -eq -1 -and $IN::IndexFromKey($K::Pause) -eq -1)
    Check '数字キー: 受け取るキーは上段とテンキーの 20 個' ($IN::AllKeys.Count -eq 20)
}

# ------------------------------------------------------------------------------
# ここから先は実際のクリップボードを使う。元がテキストなら最後に戻す。
# 途中で失敗・中断 (Ctrl+C) しても戻すよう、finally で後片付けする。
# ------------------------------------------------------------------------------
$clipBackup = $null
$tempDir = Join-Path $env:TEMP ('CopipeVerify-' + [Guid]::NewGuid().ToString('N'))
# Copipe と同じく、自分のウインドウを指定してクリップボードを開くためのウインドウ
$ownerForm = New-Object System.Windows.Forms.Form
$owner = $ownerForm.Handle

# CopyQ・Clibor などの履歴ツールはコピーのたびに一瞬クリップボードを開くので、
# ハーネスから直接開くときは少し待ってリトライする
function Invoke-ClipboardOpen {
    param([scriptblock]$Body, [IntPtr]$Window = [IntPtr]::Zero)
    $deadline = (Get-Date).AddSeconds(5)
    while (-not [CopipeVerify.Native]::OpenClipboard($Window)) {
        if ((Get-Date) -gt $deadline) { throw 'ハーネスからクリップボードを開けませんでした' }
        Start-Sleep -Milliseconds 10
    }
    try { & $Body } finally { [void][CopipeVerify.Native]::CloseClipboard() }
}

# 別プロセスでクリップボードに細工する。準備ができたら合図のファイルを作らせ、それを待つ。
#   hold       : ウインドウを指定してクリップボードを開いたまま HoldMs 待つ。
#                (NULL で開くと他プロセスの OpenClipboard(NULL) を妨げない (実測) ので、ウインドウを指定する)
#   unrendered : テキスト (CF_UNICODETEXT) を遅延レンダリングで置き、要求されても描画しないまま
#                HoldMs の間メッセージを処理し続ける
#   settext-hold : Text を置いて閉じ (変化の通知が飛ぶ)、すぐ開き直して HoldMs 持ち続ける。
#                コピー直後に CopyQ などがクリップボードを開いたままにする状況を再現する
# 準備できなければ $null を返す。
function Start-ClipboardHelper {
    param([ValidateSet('hold', 'unrendered', 'settext-hold')][string]$Mode, [int]$HoldMs, [string]$Text = '')
    $signal = Join-Path $tempDir ('helper-' + [Guid]::NewGuid().ToString('N') + '.txt')
    $script = @'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -Namespace H -Name N -MemberDefinition '[DllImport("user32.dll")] public static extern bool OpenClipboard(System.IntPtr h); [DllImport("user32.dll")] public static extern bool CloseClipboard(); [DllImport("user32.dll")] public static extern bool EmptyClipboard(); [DllImport("user32.dll")] public static extern System.IntPtr SetClipboardData(uint f, System.IntPtr h); [DllImport("kernel32.dll")] public static extern System.IntPtr GlobalAlloc(uint f, System.UIntPtr n); [DllImport("kernel32.dll")] public static extern System.IntPtr GlobalLock(System.IntPtr h); [DllImport("kernel32.dll")] public static extern bool GlobalUnlock(System.IntPtr h);'
$form = New-Object System.Windows.Forms.Form
$deadline = (Get-Date).AddSeconds(10)
while (-not [H.N]::OpenClipboard($form.Handle)) { if ((Get-Date) -gt $deadline) { exit 1 }; Start-Sleep -Milliseconds 5 }
if ('__MODE__' -eq 'unrendered') {
    [void][H.N]::EmptyClipboard()
    [void][H.N]::SetClipboardData(13, [IntPtr]::Zero)
    [void][H.N]::CloseClipboard()
    [IO.File]::WriteAllText('__SIGNAL__', 'ready')
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while ($sw.ElapsedMilliseconds -lt __HOLDMS__) { [System.Windows.Forms.Application]::DoEvents(); Start-Sleep -Milliseconds 5 }
} elseif ('__MODE__' -eq 'settext-hold') {
    $text = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('__TEXT64__'))
    $bytes = [Text.Encoding]::Unicode.GetBytes($text + [char]0)
    $mem = [H.N]::GlobalAlloc(2 <# GMEM_MOVEABLE #>, [UIntPtr][uint32]$bytes.Length)
    $ptr = [H.N]::GlobalLock($mem)
    [Runtime.InteropServices.Marshal]::Copy($bytes, 0, $ptr, $bytes.Length)
    [void][H.N]::GlobalUnlock($mem)
    [void][H.N]::EmptyClipboard()
    [void][H.N]::SetClipboardData(13, $mem)
    [void][H.N]::CloseClipboard()
    # 閉じた瞬間に変化の通知が飛ぶ。Copipe が読みに来る前に開き直して持ち続ける
    while (-not [H.N]::OpenClipboard($form.Handle)) { }
    [IO.File]::WriteAllText('__SIGNAL__', 'ready')
    Start-Sleep -Milliseconds __HOLDMS__
    [void][H.N]::CloseClipboard()
} else {
    [IO.File]::WriteAllText('__SIGNAL__', 'ready')
    Start-Sleep -Milliseconds __HOLDMS__
    [void][H.N]::CloseClipboard()
}
'@
    $text64 = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($Text))
    $script = $script.Replace('__MODE__', $Mode).Replace('__SIGNAL__', $signal).Replace('__HOLDMS__', [string]$HoldMs).Replace('__TEXT64__', $text64)
    $enc = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($script))
    $proc = Start-Process powershell.exe -ArgumentList '-NoProfile', '-EncodedCommand', $enc -WindowStyle Hidden -PassThru
    $deadline = (Get-Date).AddSeconds(15)
    while ((Get-Date) -lt $deadline -and -not $proc.HasExited) {
        if (Test-Path -LiteralPath $signal) { return $proc }
        Start-Sleep -Milliseconds 10
    }
    if (-not $proc.HasExited) { $proc.Kill() }
    return $null
}

function Stop-ClipboardHelper {
    param($Process)
    if ($Process -and -not $Process.WaitForExit(10000)) { $Process.Kill() }
}

# 他のアプリがクリップボードを開いたままだと、ハーネス自身がクリップボードを用意できない。
# 検証の失敗と紛らわしいので、始める前に確かめる。
function Test-ClipboardAvailable {
    param([int]$TimeoutMs = 3000)
    $deadline = (Get-Date).AddMilliseconds($TimeoutMs)
    while ((Get-Date) -lt $deadline) {
        if ([CopipeVerify.Native]::OpenClipboard($owner)) {
            [void][CopipeVerify.Native]::CloseClipboard()
            return $true
        }
        Start-Sleep -Milliseconds 50
    }
    return $false
}

# 履歴ツールがコピー直後に読みに来るので、ハーネスからの書き込みは少し待ってやり直す
function Invoke-ClipboardWrite {
    param([scriptblock]$Body)
    $lastError = $null
    for ($i = 0; $i -lt 10; $i++) {
        try {
            & $Body
            return
        } catch {
            $lastError = $_.Exception
            Start-Sleep -Milliseconds 100
        }
    }
    throw ('ハーネスからクリップボードに書き込めませんでした (他のアプリがクリップボードを開いたままの可能性があります): ' + $lastError.Message)
}

function Set-ClipboardText {
    param([string]$Text)
    # ここは .NET の API を直接呼ぶこと (Set-ClipboardText を呼ぶと無限再帰になる)
    Invoke-ClipboardWrite { [System.Windows.Forms.Clipboard]::SetText($Text) }
}

try {
    New-Item -ItemType Directory -Path $tempDir | Out-Null

    # ==========================================================================
    Section '検証 3: 設定とキー名 (Settings / HotkeyText)' {
    # ==========================================================================
        $HT = [Copipe.UI.HotkeyText]
        $K = [System.Windows.Forms.Keys]
        $S = [Copipe.Services.Settings]

        # 画面に出す名前 (日本語キーボードのキーは日本語で)
        Check '表示名: F1' ($HT::Display($K::F1) -ceq 'F1')
        Check '表示名: Ctrl+Space' ($HT::Display($K::Control -bor $K::Space) -ceq 'Ctrl+Space')
        Check '表示名: 無変換' ($HT::Display($K::IMENonconvert) -ceq '無変換')
        Check '表示名: 変換' ($HT::Display($K::IMEConvert) -ceq '変換')
        Check '表示名: カタカナ ひらがな' ($HT::Display($K::KanaMode) -ceq 'カタカナ ひらがな')
        Check '表示名: 修飾キーを付けた無変換' ($HT::Display($K::Control -bor $K::Shift -bor $K::IMENonconvert) -ceq 'Ctrl+Shift+無変換') "got=$($HT::Display($K::Control -bor $K::Shift -bor $K::IMENonconvert))"

        # .NET の Keys に名前が無いキー (日本語入力の切り替えキーなど)。
        # KeysConverter は空文字列を返すので、名前も保存形式も自前で用意する
        $zenkakuOff = [System.Windows.Forms.Keys]0xF3   # 半角/全角 (IME オフのとき)
        $zenkakuOn  = [System.Windows.Forms.Keys]0xF4   # 半角/全角 (IME オンのとき)
        $eisu       = [System.Windows.Forms.Keys]0xF0   # 英数
        $katakana   = [System.Windows.Forms.Keys]0xF2   # カタカナ ひらがな
        $unknown    = [System.Windows.Forms.Keys]0xE8   # 名前も用途も無いキー

        Check '表示名: 半角/全角 (IME オフ時の vk 0xF3)' ($HT::Display($zenkakuOff) -ceq '半角/全角') "got=[$($HT::Display($zenkakuOff))]"
        Check '表示名: 半角/全角 (IME オン時の vk 0xF4)' ($HT::Display($zenkakuOn) -ceq '半角/全角') "got=[$($HT::Display($zenkakuOn))]"
        Check '表示名: 英数' ($HT::Display($eisu) -ceq '英数') "got=[$($HT::Display($eisu))]"
        Check '表示名: カタカナ ひらがな (vk 0xF2)' ($HT::Display($katakana) -ceq 'カタカナ ひらがな') "got=[$($HT::Display($katakana))]"
        Check '表示名: 名前の無いキーは番号で示す' ($HT::Display($unknown) -ceq 'キー (0xE8)') "got=[$($HT::Display($unknown))]"
        Check '表示名: 空にはならない (どのキーでも何か表示する)' ($HT::Display($zenkakuOff).Length -gt 0 -and $HT::Display($unknown).Length -gt 0)

        # 押している間の判定ができないキー (押して離しても GetAsyncKeyState が押下のままになる。実測)
        foreach ($imeKey in $eisu, $katakana, $zenkakuOff, $zenkakuOn) {
            Check ("離したことを判定できないキーは使えない: " + $HT::Display($imeKey)) (-not $HT::IsValid($imeKey))
            Check ("離したことを判定できないキーだと分かる: " + $HT::Display($imeKey)) $HT::CannotDetectRelease($imeKey)
            Check ("キーボードフックの対象ではない: " + $HT::Display($imeKey)) (-not $HT::UsesKeyboardHook($imeKey))
        }
        # CapsLock は、ホットキーにするとキーボードフックで受け取って握りつぶすので使える
        # (半角/全角はフックでも押し上げを感知できなかったので、使えないキー。上の foreach)
        foreach ($hookKey in $K::Capital) {
            Check ("フックで受け取るキーはホットキーに使える: " + $HT::Display($hookKey)) ($HT::IsValid($hookKey) -and -not $HT::CannotDetectRelease($hookKey))
            Check ("フックで受け取るキーだと分かる: " + $HT::Display($hookKey)) $HT::UsesKeyboardHook($hookKey)
            Check ("フックで受け取るキーには、本来の働きが無くなる説明がある: " + $HT::Display($hookKey)) (-not [string]::IsNullOrEmpty($HT::HookKeyNote($hookKey)))
        }
        Check 'フックの対象ではない: F1・修飾キー付きの CapsLock' (-not $HT::UsesKeyboardHook($K::F1) -and -not $HT::UsesKeyboardHook($K::Shift -bor $K::Capital) -and $null -eq $HT::HookKeyNote($K::F1))
        # .NET Framework の KeysConverter は Capital と返すので、キートップの刻印 (CapsLock) にそろえる
        Check '表示名: CapsLock (Capital ではない)' ($HT::Display($K::Capital) -ceq 'CapsLock') "got=[$($HT::Display($K::Capital))]"
        Check '保存形式: CapsLock' ($HT::ToSetting($K::Capital) -ceq 'CapsLock') "got=[$($HT::ToSetting($K::Capital))]"
        Check 'フックで見張るキー: CapsLock は 0x14 だけ' (($HT::HookVirtualKeys($K::Capital) -join ',') -eq '20')
        Check '無変換は使える (離したことを判定できる)' ($HT::IsValid($K::IMENonconvert) -and -not $HT::CannotDetectRelease($K::IMENonconvert))
        Check '変換は使える' ($HT::IsValid($K::IMEConvert) -and -not $HT::CannotDetectRelease($K::IMEConvert))
        Check 'NumLock は使える (実測で離したことを判定できる)' ($HT::IsValid($K::NumLock))
        Check '修飾キー単独は「離せない」ではなく別の理由で使えない' (-not $HT::CannotDetectRelease($K::Control -bor $K::ControlKey))

        # 名前の無いキーでも設定ファイルに書けて、読み戻せること
        Check '保存形式: 名前の無いキーは VK 番号' ($HT::ToSetting($unknown) -ceq 'VK232') "got=[$($HT::ToSetting($unknown))]"
        Check '保存形式: 修飾キー付きでも VK 番号' ($HT::ToSetting($K::Control -bor $unknown) -ceq 'Ctrl+VK232') "got=[$($HT::ToSetting($K::Control -bor $unknown))]"
        $parsed = $K::None
        Check '読み戻し: VK 番号を読める' ($HT::TryParse('VK232', [ref]$parsed) -and $parsed -eq $unknown) "parsed=$parsed"
        $parsed = $K::None
        Check '読み戻し: 修飾キー付きは使えないので false (Ctrl+VK232)' (-not $HT::TryParse('Ctrl+VK232', [ref]$parsed)) "parsed=$parsed"
        foreach ($badVk in 'VK', 'VK0', 'VK999', 'VKxyz') {
            $parsed = $K::None
            Check "読み戻し: 不正な VK 番号 '$badVk' は false" (-not $HT::TryParse($badVk, [ref]$parsed))
        }
        $parsed = $K::None
        Check '読み戻し: 使えないキー (英数) は false' (-not $HT::TryParse($HT::ToSetting($eisu), [ref]$parsed)) "setting=[$($HT::ToSetting($eisu))] parsed=$parsed"
        foreach ($zk in $zenkakuOff, $zenkakuOn) {
            $parsed = $K::None
            Check ("読み戻し: 使えないキー (半角/全角 " + $HT::ToSetting($zk) + ") は false") (-not $HT::TryParse($HT::ToSetting($zk), [ref]$parsed)) "setting=[$($HT::ToSetting($zk))] parsed=$parsed"
        }

        # 設定ファイルに書く形式と、その読み戻し
        Check '保存形式: F1' ($HT::ToSetting($K::F1) -ceq 'F1')
        Check '保存形式: Ctrl+Space' ($HT::ToSetting($K::Control -bor $K::Space) -ceq 'Ctrl+Space')
        foreach ($keys in @($K::F1, $K::Pause, $K::IMENonconvert, $K::Oem3, $K::Capital)) {
            $text = $HT::ToSetting($keys)
            $parsed = $K::None
            $ok = $HT::TryParse($text, [ref]$parsed)
            Check "保存形式から読み戻せる: $text" ($ok -and $parsed -eq $keys) "text=$text parsed=$parsed"
        }

        foreach ($bad in 'ごみ', '', 'Ctrl+', 'F99') {
            $parsed = $K::None
            Check "読み戻し: 不正な値 '$bad' は false" (-not $HT::TryParse($bad, [ref]$parsed))
        }
        $parsed = $K::None
        Check '読み戻し: null は false' (-not $HT::TryParse($null, [ref]$parsed))

        # ホットキーにできるキーかどうか (修飾キー単独は RegisterHotKey で登録できない)
        Check '妥当性: F1 は使える' $HT::IsValid($K::F1)
        # 修飾キー (Ctrl・Shift・Alt) との組み合わせは使えない (モードキーと同じ)
        foreach ($withModifier in ($K::Control -bor $K::Space), ($K::Alt -bor $K::A), ($K::Shift -bor $K::F2), ($K::Control -bor $K::Alt -bor $K::F9)) {
            Check "妥当性: 修飾キー付きの $withModifier は使えない" (-not $HT::IsValid($withModifier))
        }
        Check '妥当性: 無変換は使える' $HT::IsValid($K::IMENonconvert)
        # 数字キーは、小窓の一覧から選ぶために使うのでホットキーにはできない
        foreach ($digit in $K::D1, $K::D0, $K::NumPad5, ($K::Control -bor $K::D1)) {
            Check "妥当性: 数字キー $digit は使えない" (-not $HT::IsValid($digit))
            Check "数字キーだと分かる: $digit" $HT::IsDigitKey($digit)
        }
        Check '数字キーだと分かる: F1 は数字キーではない' (-not $HT::IsDigitKey($K::F1))
        # 上下の矢印キー・Enter は一覧の操作に使わないので、ホットキーの妥当性には関わらない (Enter は設定画面が弾く)
        Check '妥当性: Up は使える' $HT::IsValid($K::Up)
        foreach ($invalid in $K::None, $K::ControlKey, $K::ShiftKey, $K::Menu, $K::LWin, ($K::Control -bor $K::ControlKey)) {
            Check "妥当性: $invalid は使えない" (-not $HT::IsValid($invalid))
        }

        # 設定ファイルの読み書き
        $settingsDir = Join-Path $tempDir 'settings'
        $path = Join-Path $settingsDir 'settings.ini'
        Check '保存先の既定は %LOCALAPPDATA%\Copipe\settings.ini' ($S::DefaultPath -ceq (Join-Path $env:LOCALAPPDATA 'Copipe\settings.ini')) "got=$($S::DefaultPath)"

        $loaded = $S::Load($path)
        Check '読み込み: ファイルが無ければ既定 (CapsLock)' ($loaded.Hotkey -eq $S::DefaultHotkey -and $S::DefaultHotkey -eq $K::Capital) "got=$($loaded.Hotkey)"

        $loaded.Hotkey = $K::F2
        $loaded.Save($path)
        Check '保存: フォルダーが無ければ作る' (Test-Path -LiteralPath $path)
        Check '保存: 内容は Hotkey=F2 の行を含む' ((Get-Content -LiteralPath $path -Raw) -match 'Hotkey\s*=\s*F2')
        Check '保存: メモ帳で開けるよう UTF-8 BOM 付き' ((([IO.File]::ReadAllBytes($path))[0..2] -join ',') -eq '239,187,191')
        Check '読み込み: 保存した値が戻る' ($S::Load($path).Hotkey -eq $K::F2)

        # 前の版で保存した修飾キー付きのホットキーは、既定に戻す (修飾キーだけ外すと、そのキーが全アプリで打てなくなるため)
        foreach ($legacy in 'Ctrl+Space', 'Alt+A', 'Win+J', 'Ctrl+Shift+F13') {
            Set-Content -LiteralPath $path -Value "Hotkey=$legacy" -Encoding UTF8
            Check "読み込み: 修飾キー付きの $legacy が書かれていたら既定 (CapsLock) に戻す" ($S::Load($path).Hotkey -eq $S::DefaultHotkey) "got=$($S::Load($path).Hotkey)"
        }

        Set-Content -LiteralPath $path -Value "# コメント`r`n`r`n   Hotkey = 無変換ではない何か   `r`n" -Encoding UTF8
        Check '読み込み: 読めない値なら既定に戻す' ($S::Load($path).Hotkey -eq $S::DefaultHotkey)

        Set-Content -LiteralPath $path -Value "Hotkey=ControlKey" -Encoding UTF8
        Check '読み込み: 修飾キー単独が書かれていたら既定に戻す' ($S::Load($path).Hotkey -eq $S::DefaultHotkey)

        Set-Content -LiteralPath $path -Value "Hotkey=VK240" -Encoding UTF8
        Check '読み込み: 手で書かれた英数 (VK240) は既定に戻す' ($S::Load($path).Hotkey -eq $S::DefaultHotkey)
        Set-Content -LiteralPath $path -Value "Hotkey=VK243" -Encoding UTF8
        Check '読み込み: 手で書かれた半角/全角 (VK243) は既定に戻す' ($S::Load($path).Hotkey -eq $S::DefaultHotkey) "got=$($S::Load($path).Hotkey)"
        Set-Content -LiteralPath $path -Value "Hotkey=CapsLock" -Encoding UTF8
        Check '読み込み: CapsLock は読める' ($S::Load($path).Hotkey -eq $K::Capital) "got=$($S::Load($path).Hotkey)"

        Set-Content -LiteralPath $path -Value "# 設定`r`n`r`n  hotkey = IMENonconvert  `r`nUnknownKey=1`r`n" -Encoding UTF8
        Check '読み込み: コメント・空行・前後の空白・大小文字・未知の項目を無視する' ($S::Load($path).Hotkey -eq $K::IMENonconvert) "got=$($S::Load($path).Hotkey)"

        [IO.File]::WriteAllBytes($path, [byte[]](0, 1, 2, 3, 255))
        Check '読み込み: 壊れたファイルでも例外にせず既定に戻す' ($S::Load($path).Hotkey -eq $S::DefaultHotkey)

        # 履歴の保持数は 10 件に固定 (設定には無い)。前の版の HistoryCount の行は読み捨て、保存し直すと消える
        Check '保持数: 設定に HistoryCount は無い' ($null -eq [Copipe.Services.Settings].GetProperty('HistoryCount'))
        Set-Content -LiteralPath $path -Value "Hotkey=F2`r`nHistoryCount=50" -Encoding UTF8
        $old = $S::Load($path)
        Check '保持数: 前の版の HistoryCount の行があっても読める' ($old.Hotkey -eq $K::F2)
        $old.Save($path)
        Check '保持数: 保存し直すと HistoryCount の行は消える' ((Get-Content -LiteralPath $path -Raw) -notmatch 'HistoryCount')

        # 貼り付けの操作 (ダブルクリック / シングルクリック)
        $IC = [Copipe.Services.InsertClick]
        Check '貼り付けの操作: 既定はダブルクリック' ($S::Load((Join-Path $settingsDir 'none.ini')).InsertClick -eq $IC::Double)
        $saved2 = New-Object Copipe.Services.Settings
        $saved2.InsertClick = $IC::Single
        $saved2.Save($path)
        Check '貼り付けの操作: シングルクリックを保存して読み戻せる' ($S::Load($path).InsertClick -eq $IC::Single) "got=$($S::Load($path).InsertClick)"
        Check '貼り付けの操作: 保存した内容に InsertClick の行がある' ((Get-Content -LiteralPath $path -Raw) -match 'InsertClick\s*=\s*Single')
        # 数字 ('1' など) を列挙値として読んでしまわないこと
        foreach ($bad in 'Triple', '', '1', '0') {
            Set-Content -LiteralPath $path -Value "InsertClick=$bad" -Encoding UTF8
            Check "貼り付けの操作: 読めない値 '$bad' は既定 (ダブルクリック) に戻す" ($S::Load($path).InsertClick -eq $IC::Double) "got=$($S::Load($path).InsertClick)"
        }
        Set-Content -LiteralPath $path -Value "InsertClick=single" -Encoding UTF8
        Check '貼り付けの操作: 大文字小文字は区別しない' ($S::Load($path).InsertClick -eq $IC::Single)

        # 言語 (Auto・Japanese・English)
        $UL = [Copipe.UiLanguage]
        Check '言語: ファイルが無ければ Auto' ($S::Load((Join-Path $settingsDir 'none.ini')).Language -eq $UL::Auto)
        $savedLang = New-Object Copipe.Services.Settings
        $savedLang.Language = $UL::English
        $savedLang.Save($path)
        Check '言語: 保存して読み戻せる' ($S::Load($path).Language -eq $UL::English) "got=$($S::Load($path).Language)"
        Set-Content -LiteralPath $path -Value "Language=japanese" -Encoding UTF8
        Check '言語: 大文字小文字は区別しない' ($S::Load($path).Language -eq $UL::Japanese)
        foreach ($bad in 'ja', '1', '') {
            Set-Content -LiteralPath $path -Value "Language=$bad" -Encoding UTF8
            Check "言語: 読めない値 '$bad' は Auto に戻す" ($S::Load($path).Language -eq $UL::Auto) "got=$($S::Load($path).Language)"
        }
        [Copipe.Lang]::Apply($UL::English)
        $en = [Copipe.Lang]::T('日本語', 'English')
        [Copipe.Lang]::Apply($UL::Japanese)
        $ja = [Copipe.Lang]::T('日本語', 'English')
        [Copipe.Lang]::Japanese = $true
        Check '言語: English にすると英語、Japanese にすると日本語の言葉になる' ($en -ceq 'English' -and $ja -ceq '日本語') "en=$en ja=$ja"

        # モードキー (ホットキーを押したまま押して、履歴と定型文を切り替えるキー)
        Check 'モードキー: 既定は Tab' ($S::DefaultModeKey -eq $K::Tab)
        Check 'モードキー: ファイルが無ければ既定 (Tab)' ($S::Load((Join-Path $settingsDir 'none.ini')).ModeKey -eq $K::Tab)
        $saved3 = New-Object Copipe.Services.Settings
        $saved3.ModeKey = $K::F2
        $saved3.Save($path)
        Check 'モードキー: 保存して読み戻せる' ($S::Load($path).ModeKey -eq $K::F2) "got=$($S::Load($path).ModeKey)"
        Check 'モードキー: 保存した内容に ModeKey の行がある' ((Get-Content -LiteralPath $path -Raw) -match 'ModeKey\s*=\s*F2')
        Set-Content -LiteralPath $path -Value "ModeKey=IMENonconvert" -Encoding UTF8
        Check 'モードキー: 無変換も読める' ($S::Load($path).ModeKey -eq $K::IMENonconvert)
        # 修飾キー付き・数字キー・修飾キーだけ・読めない値は使えない
        foreach ($bad in 'Ctrl+Tab', 'D1', 'NumPad3', 'ShiftKey', 'VK243', 'abc', '') {
            Set-Content -LiteralPath $path -Value "ModeKey=$bad" -Encoding UTF8
            Check "モードキー: 使えない値 '$bad' は既定 (Tab) に戻す" ($S::Load($path).ModeKey -eq $K::Tab) "got=$($S::Load($path).ModeKey)"
        }
        Check 'モードキーの妥当性: Tab・F2・Space は使える' ($HT::IsValidModeKey($K::Tab) -and $HT::IsValidModeKey($K::F2) -and $HT::IsValidModeKey($K::Space))
        Check 'モードキーの妥当性: 修飾キー付き (Ctrl+Tab) は使えない' (-not $HT::IsValidModeKey($K::Control -bor $K::Tab))
        Check 'モードキーの妥当性: 数字キー・修飾キーだけ・半角/全角は使えない' (-not $HT::IsValidModeKey($K::D1) -and -not $HT::IsValidModeKey($K::ShiftKey) -and -not $HT::IsValidModeKey([System.Windows.Forms.Keys]0xF4))
        Check 'モードキーとホットキーの重なり: F1 と F1 は重なる' $HT::ConflictsWithHotkey($K::F1, $K::F1)
        Check 'モードキーとホットキーの重なり: Space と Space は重なる' $HT::ConflictsWithHotkey($K::Space, $K::Space)
        Check 'モードキーとホットキーの重なり: F1 と Tab は重ならない' (-not $HT::ConflictsWithHotkey($K::F1, $K::Tab))

        # ---- 前の版の設定 (ダブルタップ・ホットキーなし) を読んでも困らない ----
        Set-Content -LiteralPath $path -Value "Hotkey=Pause`r`nDoubleTap=Ctrl`r`nModeKey=F2" -Encoding UTF8
        $old = $S::Load($path)
        Check '前の版の設定: DoubleTap= の行は読み飛ばし、ほかの値は読める' ($old.Hotkey -eq $K::Pause -and $old.ModeKey -eq $K::F2) "hotkey=$($old.Hotkey) mode=$($old.ModeKey)"
        Set-Content -LiteralPath $path -Value "Hotkey=None`r`nDoubleTap=Shift" -Encoding UTF8
        Check '前の版の設定: Hotkey=None (ダブルタップだけで使っていた) は、開けなくならないよう既定 (CapsLock) にする' ($S::Load($path).Hotkey -eq $S::DefaultHotkey) "hotkey=$($S::Load($path).Hotkey)"
        (New-Object Copipe.Services.Settings).Save($path)
        Check '保存: DoubleTap の行は書かない' (-not ((Get-Content -LiteralPath $path -Raw) -match 'DoubleTap'))
    }

    # ==========================================================================
    Section '検証 3b: 設定画面のキーの取り込み (SettingsDialog)' {
    # ==========================================================================
        # 設定画面を開き、キーが押されたときの処理 (ProcessCmdKey・OnKeyUp) を、押されたときと同じ引数で直接呼ぶ。
        # 本物のキー入力は送らない (Windows や他のアプリが先に受け取る組み合わせも確かめられるように。
        # マウスとキーボードは使わない)。画面には設定画面が一瞬出る
        $K = [System.Windows.Forms.Keys]
        $F = [Reflection.BindingFlags]'Public,NonPublic,Instance'
        # SettingsDialog は internal なので、型は exe から名前で取り出して作る
        $DT = [Copipe.UI.HotkeyText].Assembly.GetType('Copipe.UI.SettingsDialog', $true)
        function New-Dialog([System.Windows.Forms.Keys]$Hotkey, [System.Windows.Forms.Keys]$Mode) {
            $d = [Activator]::CreateInstance($DT, $F, $null, [object[]]@($Hotkey, $Mode, [Copipe.Services.InsertClick]::Double, [Copipe.UiLanguage]::Auto), $null)
            $d.StartPosition = 'Manual'
            $d.Location = New-Object System.Drawing.Point 200, 150
            $d.Show()
            [System.Windows.Forms.Application]::DoEvents()
            return $d
        }
        function Get-DialogField($d, [string]$Name) { return $DT.GetField($Name, $F).GetValue($d) }
        function Send-DialogKey($d, [string]$Box, [System.Windows.Forms.Keys]$KeyData) {
            $d.ActiveControl = Get-DialogField $d $Box
            $msg = [System.Windows.Forms.Message]::Create($d.Handle, 0x0100, [IntPtr]([int]($KeyData -band $K::KeyCode)), [IntPtr]::Zero)
            [void]$DT.GetMethod('ProcessCmdKey', $F).Invoke($d, [object[]]@($msg, $KeyData))
            [System.Windows.Forms.Application]::DoEvents()
        }
        function Send-DialogKeyUp($d, [System.Windows.Forms.Keys]$Key) {
            $e = New-Object System.Windows.Forms.KeyEventArgs $Key
            [void]$DT.GetMethod('OnKeyUp', $F).Invoke($d, [object[]]@($e.PSObject.BaseObject))
            [System.Windows.Forms.Application]::DoEvents()
        }
        function Get-DialogState($d) {
            $note = Get-DialogField $d '_note'
            return @{ Box = (Get-DialogField $d '_hotkeyBox').Text; Mode = (Get-DialogField $d '_modeKeyBox').Text; Note = $note.Text
                      Red = ($note.ForeColor.ToArgb() -ne [System.Drawing.SystemColors]::GrayText.ToArgb()); Ok = (Get-DialogField $d '_ok').Enabled
                      Hotkey = $d.SelectedHotkey; ModeKey = $d.SelectedModeKey }
        }

        # ホットキーの欄
        $d = New-Dialog $K::Pause $K::Tab
        try {
            $s = Get-DialogState $d
            Check '設定画面: 開いたときは今のホットキー (Pause) が出て、OK を押せる' ($s.Box -ceq 'Pause' -and $s.Ok) "box=$($s.Box) ok=$($s.Ok)"
            Send-DialogKey $d '_hotkeyBox' ($K::Control -bor $K::Space); $s = Get-DialogState $d
            Check '設定画面: Ctrl+Space を押すと、修飾キーを外して Space になる' ($s.Box -ceq 'Space' -and $s.Hotkey -eq $K::Space -and $s.Ok -and -not $s.Red) "box=$($s.Box) hotkey=$($s.Hotkey) note=$($s.Note)"
            Send-DialogKey $d '_hotkeyBox' ($K::Alt -bor $K::A); $s = Get-DialogState $d
            Check '設定画面: Alt+A を押すと A になる' ($s.Box -ceq 'A' -and $s.Hotkey -eq $K::A) "box=$($s.Box)"
            Send-DialogKey $d '_hotkeyBox' ($K::Control -bor $K::Shift -bor $K::F9); $s = Get-DialogState $d
            Check '設定画面: Ctrl+Shift+F9 を押すと F9 になる' ($s.Box -ceq 'F9' -and $s.Hotkey -eq $K::F9) "box=$($s.Box)"
            foreach ($only in ($K::Control -bor $K::ControlKey), ($K::Shift -bor $K::ShiftKey), ($K::Alt -bor $K::Menu), $K::LWin) {
                Send-DialogKey $d '_hotkeyBox' $only; $s = Get-DialogState $d
                Check "設定画面: 修飾キーだけ ($($only -band $K::KeyCode)) を押しても何も変わらない" ($s.Box -ceq 'F9' -and $s.Hotkey -eq $K::F9 -and $s.Ok -and -not $s.Red) "box=$($s.Box) ok=$($s.Ok) note=$($s.Note)"
            }
            Send-DialogKey $d '_hotkeyBox' ($K::Control -bor $K::D1); $s = Get-DialogState $d
            Check '設定画面: 数字キー (Ctrl+1) は理由を赤字で出し、OK を押せない' ($s.Box -ceq '1' -and $s.Red -and -not $s.Ok -and $s.Note -like '*数字キー*ホットキーにはできません*') "box=$($s.Box) ok=$($s.Ok) note=$($s.Note)"
            Send-DialogKeyUp $d $K::D1; $s = Get-DialogState $d
            Check '設定画面: 使えないキーを離すと、選ばれていたキー (F9) の表示に戻り、OK を押せる' ($s.Box -ceq 'F9' -and $s.Ok -and $s.Hotkey -eq $K::F9) "box=$($s.Box) ok=$($s.Ok)"
            Send-DialogKey $d '_hotkeyBox' ([System.Windows.Forms.Keys]0xF0); $s = Get-DialogState $d
            Check '設定画面: 英数 は離したことを判定できない理由を赤字で出す' ($s.Red -and -not $s.Ok -and $s.Note -like '*離したことを判定できない*') "note=$($s.Note)"
            $note = Get-DialogField $d '_note'
            $need = [System.Windows.Forms.TextRenderer]::MeasureText($note.Text, $note.Font, (New-Object System.Drawing.Size $note.Width, 0), [System.Windows.Forms.TextFormatFlags]'WordBreak').Height
            Check '設定画面: 赤字の説明が欄に収まる (切れない)' ($need -le $note.Height) "need=$need height=$($note.Height)"
            Send-DialogKeyUp $d ([System.Windows.Forms.Keys]0xF0)
            # 半角/全角 も、英数と同じで離したことを判定できないので使えない
            Send-DialogKey $d '_hotkeyBox' ([System.Windows.Forms.Keys]0xF3); $s = Get-DialogState $d
            Check '設定画面: 半角/全角 は離したことを判定できない理由を赤字で出す' ($s.Red -and -not $s.Ok -and $s.Note -like '*離したことを判定できない*') "note=$($s.Note)"
            Send-DialogKeyUp $d ([System.Windows.Forms.Keys]0xF3)
            # CapsLock は選べる。そのキー本来の働きが無くなることを赤字で知らせる (説明は欄に収まる)
            foreach ($hookKey in $K::Capital) {
                Send-DialogKey $d '_hotkeyBox' $hookKey; $s = Get-DialogState $d
                $note = Get-DialogField $d '_note'
                $need = [System.Windows.Forms.TextRenderer]::MeasureText($note.Text, $note.Font, (New-Object System.Drawing.Size $note.Width, 0), [System.Windows.Forms.TextFormatFlags]'WordBreak').Height
                Check "設定画面: $($s.Box) は選べて、本来の働きが無くなることを赤字で知らせる" ($s.Hotkey -eq $hookKey -and $s.Ok -and $s.Red -and $s.Note -like '*切り替わらなくなります*' -and $need -le $note.Height) "box=$($s.Box) ok=$($s.Ok) note=$($s.Note) need=$need height=$($note.Height)"
            }
            Send-DialogKey $d '_hotkeyBox' $K::Tab; $s = Get-DialogState $d
            Check '設定画面: モードキーと同じキー (Tab) にすると、重なっている理由を出し OK を押せない' ($s.Red -and -not $s.Ok -and $s.Note -like '*同じキーは使えません*') "ok=$($s.Ok) note=$($s.Note)"
            Send-DialogKey $d '_hotkeyBox' $K::F2; $s = Get-DialogState $d
            Check '設定画面: 別のキー (F2) にすると重なりが解け、OK を押せる' ($s.Box -ceq 'F2' -and $s.Ok -and -not $s.Red) "ok=$($s.Ok) note=$($s.Note)"
        } finally { $d.Close(); $d.Dispose() }

        # About ボタン: OK の左にあり、押すと設定画面の上に「Copipe について」が出る
        $d = New-Dialog $K::Pause $K::Tab
        try {
            $about = $d.Controls['aboutButton']
            $okButton = Get-DialogField $d '_ok'
            Check '設定画面: About ボタンが OK の左にある (同じ行で、重ならない)' `
                ($null -ne $about -and $about.Top -eq $okButton.Top -and $about.Right -le $okButton.Left) `
                "about=$(if ($about) { $about.Bounds }) ok=$($okButton.Bounds)"
            if ($null -ne $about) {
                $script:aboutSeen = $null
                $closer = New-Object System.Windows.Forms.Timer
                $closer.Interval = 300
                $closer.add_Tick({
                    $closer.Stop()
                    # ShowDialog の中から閉じる (閉じないと検証が止まるので、何があっても閉じる)
                    foreach ($f in @([System.Windows.Forms.Application]::OpenForms)) {
                        if ($f.GetType().Name -eq 'AboutDialog') {
                            try { $script:aboutSeen = @{ Owner = ($f.Owner -eq $d); Visible = $f.Visible; Text = $f.Text } } catch { }
                            $f.Close()
                        }
                    }
                })
                $closer.Start()
                $about.PerformClick()
                $closer.Dispose()
                Check '設定画面: About ボタンを押すと、設定画面を親にして「Copipe について」が出る' `
                    ($null -ne $script:aboutSeen -and $script:aboutSeen.Owner -and $script:aboutSeen.Visible) `
                    "seen=$(if ($script:aboutSeen) { $script:aboutSeen.Text + ' owner=' + $script:aboutSeen.Owner })"
                Check '設定画面: 「Copipe について」を閉じても設定画面は開いたまま' ($d.Visible -and -not $d.IsDisposed)
            }
        } finally { $d.Close(); $d.Dispose() }

        # モードキーの欄 (ホットキーと同じ決まり)
        $d = New-Dialog $K::Pause $K::Tab
        try {
            Send-DialogKey $d '_modeKeyBox' ($K::Shift -bor $K::Tab); $s = Get-DialogState $d
            Check '設定画面: モードキーで Shift+Tab を押すと Tab になる' ($s.Mode -ceq 'Tab' -and $s.ModeKey -eq $K::Tab -and $s.Ok) "mode=$($s.Mode)"
            Send-DialogKey $d '_modeKeyBox' ($K::Control -bor $K::F3); $s = Get-DialogState $d
            Check '設定画面: モードキーで Ctrl+F3 を押すと F3 になる' ($s.Mode -ceq 'F3' -and $s.ModeKey -eq $K::F3) "mode=$($s.Mode)"
            Send-DialogKey $d '_modeKeyBox' ($K::Control -bor $K::ControlKey); $s = Get-DialogState $d
            Check '設定画面: モードキーで Ctrl だけを押しても何も変わらない' ($s.Mode -ceq 'F3' -and $s.Ok) "mode=$($s.Mode)"
            Send-DialogKey $d '_modeKeyBox' $K::NumPad5; $s = Get-DialogState $d
            Check '設定画面: モードキーでテンキーの 5 は「モードキーにはできません」と出す' ($s.Red -and -not $s.Ok -and $s.Note -like '*モードキーにはできません*') "note=$($s.Note)"
            Send-DialogKeyUp $d $K::NumPad5
            # 半角/全角 はモードキーにもできない
            Send-DialogKey $d '_modeKeyBox' ([System.Windows.Forms.Keys]0xF4); $s = Get-DialogState $d
            Check '設定画面: モードキーで 半角/全角 は離したことを判定できない理由を赤字で出す' ($s.Red -and -not $s.Ok -and $s.ModeKey -eq $K::F3 -and $s.Note -like '*離したことを判定できない*') "mode=$($s.ModeKey) note=$($s.Note)"
            Send-DialogKeyUp $d ([System.Windows.Forms.Keys]0xF4)
            Send-DialogKey $d '_modeKeyBox' $K::Pause; $s = Get-DialogState $d
            Check '設定画面: モードキーをホットキーと同じキー (Pause) にすると OK を押せない' (-not $s.Ok -and $s.Note -like '*同じキーは使えません*') "note=$($s.Note)"
        } finally { $d.Close(); $d.Dispose() }

        # ホットキーを「なし」にする欄は無い (ホットキーが無いと小窓を出せない)
        $d = New-Dialog $K::Pause $K::Tab
        try {
            Check '設定画面: ホットキーの「なし」ボタンもダブルタップの欄も無い' ($null -eq $DT.GetField('_hotkeyClear', $F) -and $null -eq $DT.GetField('_doubleTapBox', $F))
        } finally { $d.Close(); $d.Dispose() }

        # 確定と取り消し
        $d = New-Dialog $K::Pause $K::Tab
        try {
            Send-DialogKey $d '_hotkeyBox' ($K::Alt -bor $K::F7)
            Send-DialogKey $d '_hotkeyBox' $K::Enter
            Check '設定画面: Enter で確定して閉じる (選んだのは F7)' ($d.DialogResult -eq 'OK' -and $d.SelectedHotkey -eq $K::F7) "result=$($d.DialogResult) hotkey=$($d.SelectedHotkey)"
        } finally { $d.Dispose() }
        $d = New-Dialog $K::Pause $K::Tab
        try {
            Send-DialogKey $d '_hotkeyBox' $K::D5
            Send-DialogKey $d '_hotkeyBox' $K::Enter
            Check '設定画面: 使えないキー (5) を表示している間は Enter で閉じない' ($d.Visible -and $d.DialogResult -eq 'None') "result=$($d.DialogResult)"
            Send-DialogKey $d '_hotkeyBox' $K::Escape
            Check '設定画面: Esc で取り消して閉じる' ($d.DialogResult -eq 'Cancel') "result=$($d.DialogResult)"
        } finally { $d.Dispose() }
    }

    # ==========================================================================
    Section '検証 4: クリップボードの履歴 (ClipboardHistory)' {
    # ==========================================================================
        $H = [Copipe.Services.ClipboardHistory]
        $historyDir = Join-Path $tempDir 'history'
        $historyPath = Join-Path $historyDir 'history.json'

        Check '保存先の既定は %LOCALAPPDATA%\Copipe\history.json' ($H::DefaultPath -ceq (Join-Path $env:LOCALAPPDATA 'Copipe\history.json')) "got=$($H::DefaultPath)"

        # 小窓で履歴に振れる番号 (1〜9、0) と同じ 10 件だけ残す (設定では変えられない)
        Check '保持数: 10 件に固定 (番号 1〜9、0 と同じ数)' ($H::MaxItems -eq 10 -and $H::MaxItems -eq [Copipe.UI.ItemNumber]::Count)
        $h = New-Object Copipe.Services.ClipboardHistory 10
        Check '最初は空' ($h.Items.Count -eq 0)

        Check '追加できる' ($h.Add('あ'))
        Check '追加された内容が先頭にある' ($h.Items.Count -eq 1 -and $h.Items[0] -ceq 'あ')
        Check '同じ内容を続けてコピーしても増えない' ((-not $h.Add('あ')) -and $h.Items.Count -eq 1)

        [void]$h.Add('い')
        [void]$h.Add('う')
        Check '新しい順に並ぶ' (($h.Items -join ',') -ceq 'う,い,あ') "got=$($h.Items -join ',')"

        Check '前にコピーした内容をまたコピーすると先頭へ移動する' ($h.Add('あ') -and (($h.Items -join ',') -ceq 'あ,う,い')) "got=$($h.Items -join ',')"
        Check '移動しても件数は増えない' ($h.Items.Count -eq 3)

        Check '空文字列は履歴に入れない' ((-not $h.Add('')) -and $h.Items.Count -eq 3)
        Check 'null も履歴に入れない' ((-not $h.Add($null)) -and $h.Items.Count -eq 3)

        Check '1 件あたりの上限は 10 万文字' ($H::MaxTextLength -eq 100000)
        $big = New-Object string ([char]'z'), 100001
        Check '上限を超えるコピーは履歴に入れない' ((-not $h.Add($big)) -and $h.Items.Count -eq 3)
        $justFits = New-Object string ([char]'z'), 100000
        Check 'ちょうど上限なら履歴に入る' ($h.Add($justFits) -and $h.Items.Count -eq 4)
        [void]$h.Items  # 参照を保持しない確認用
        $h.Clear()

        $h = New-Object Copipe.Services.ClipboardHistory 3
        foreach ($t in 'a', 'b', 'c', 'd') { [void]$h.Add($t) }
        Check '保持数を超えたら古いものから捨てる' (($h.Items -join ',') -ceq 'd,c,b') "got=$($h.Items -join ',')"
        $h.Capacity = 2
        Check '保持数を減らすと古いものを捨てる' (($h.Items -join ',') -ceq 'd,c') "got=$($h.Items -join ',')"
        $h.Capacity = 5
        Check '保持数を増やしても今ある分はそのまま' (($h.Items -join ',') -ceq 'd,c')

        $e = Get-ThrownException { New-Object Copipe.Services.ClipboardHistory 0 }
        Check '保持数 0 で作ろうとしたら例外' ($e -is [ArgumentOutOfRangeException]) (Format-ExceptionText $e)

        $h.Clear()
        Check '消去できる' ($h.Items.Count -eq 0)

        # 保存と読み込み
        $h = New-Object Copipe.Services.ClipboardHistory 10
        $tricky = "改行`r`nとタブ`tと 日本語 & 記号 " + [char]::ConvertFromUtf32(0x1F600)
        [void]$h.Add('ふつうの文字')
        [void]$h.Add($tricky)
        $h.Save($historyPath)
        Check '保存: フォルダーが無ければ作る' (Test-Path -LiteralPath $historyPath)
        Check '保存: 一時ファイルを残さない' ((Get-ChildItem -LiteralPath $historyDir).Count -eq 1) "files=$((Get-ChildItem -LiteralPath $historyDir).Name -join ', ')"

        $loaded = $H::Load($historyPath, 10)
        Check '読み込み: 内容と順番が戻る' (($loaded.Items.Count -eq 2) -and ($loaded.Items[0] -ceq $tricky) -and ($loaded.Items[1] -ceq 'ふつうの文字')) "got=$($loaded.Items.Count) 件"

        $loaded = $H::Load($historyPath, 1)
        Check '読み込み: 保持数より多ければ新しい方だけ読む' ($loaded.Items.Count -eq 1 -and $loaded.Items[0] -ceq $tricky)

        Check '読み込み: ファイルが無ければ空' ($H::Load((Join-Path $historyDir 'none.json'), 10).Items.Count -eq 0)

        [IO.File]::WriteAllBytes($historyPath, [byte[]](0, 1, 2, 3, 255))
        Check '読み込み: 壊れたファイルでも例外にせず空にする' ($H::Load($historyPath, 10).Items.Count -eq 0)

        $h = New-Object Copipe.Services.ClipboardHistory 10
        $h.Save($historyPath)
        Check '保存: 空でも保存できる' ($H::Load($historyPath, 10).Items.Count -eq 0)

        # ---- ピン止め ----
        $h = New-Object Copipe.Services.ClipboardHistory 3
        foreach ($t in 'a', 'b', 'c') { [void]$h.Add($t) }
        Check 'ピン止め: 最初は無い' ($h.Pinned.Count -eq 0)
        Check 'ピン止め: 普通の履歴の項目をピン止めできる' ($h.Pin('b') -and ($h.Pinned -join ',') -ceq 'b' -and ($h.Items -join ',') -ceq 'c,a') "pinned=$($h.Pinned -join ',') items=$($h.Items -join ',')"
        Check 'ピン止め: 新しくピン止めしたものがピン止めの先頭' ($h.Pin('a') -and ($h.Pinned -join ',') -ceq 'a,b') "pinned=$($h.Pinned -join ',')"
        Check 'ピン止め: 同じものをもう一度ピン止めしても変わらない' ((-not $h.Pin('a')) -and ($h.Pinned -join ',') -ceq 'a,b')
        Check 'ピン止め: 履歴に無いものはピン止めしない' ((-not $h.Pin('無い')) -and $h.Pinned.Count -eq 2)
        foreach ($t in 'd', 'e', 'f', 'g') { [void]$h.Add($t) }
        Check 'ピン止め: 保持数に数えない (普通の履歴は 3 件まで、ピン止めは残る)' (($h.Items -join ',') -ceq 'g,f,e' -and ($h.Pinned -join ',') -ceq 'a,b') "pinned=$($h.Pinned -join ',') items=$($h.Items -join ',')"
        Check 'ピン止め: ピン止めした内容をまたコピーしても増えず、位置も変わらない' ((-not $h.Add('b')) -and ($h.Items -join ',') -ceq 'g,f,e' -and ($h.Pinned -join ',') -ceq 'a,b')
        Check 'ピン止め: Contains はピン止めも見る' ($h.Contains('b') -and $h.Contains('g') -and -not $h.Contains('c'))
        Check 'ピン止めを外す: 普通の履歴の先頭に戻り、保持数を超えた分は古いものから捨てる' ($h.Unpin('b') -and ($h.Pinned -join ',') -ceq 'a' -and ($h.Items -join ',') -ceq 'b,g,f') "pinned=$($h.Pinned -join ',') items=$($h.Items -join ',')"
        Check 'ピン止めを外す: ピン止めに無いものは何もしない' ((-not $h.Unpin('g')) -and ($h.Items -join ',') -ceq 'b,g,f')
        Check '削除: 普通の履歴から消し、後ろが詰まる' ($h.Remove('g') -and ($h.Items -join ',') -ceq 'b,f' -and ($h.Pinned -join ',') -ceq 'a') "items=$($h.Items -join ',')"
        Check '削除: ピン止めからも消せる' ($h.Remove('a') -and $h.Pinned.Count -eq 0 -and ($h.Items -join ',') -ceq 'b,f') "pinned=$($h.Pinned -join ',')"
        Check '削除: 無いものは何もしない' (-not $h.Remove('zz'))
        [void]$h.PinText('a')
        $h.Clear()
        Check '消去: 普通の履歴だけ消し、ピン止めは残る' ($h.Items.Count -eq 0 -and ($h.Pinned -join ',') -ceq 'a')
        $h.Capacity = 1
        Check '保持数を減らしても、ピン止めは残る' (($h.Pinned -join ',') -ceq 'a')

        # 並べ替え (ドラッグ＆ドロップ)。ピン止めはピン止めの中、普通の履歴は普通の履歴の中だけで動かす
        $h = New-Object Copipe.Services.ClipboardHistory 10
        foreach ($t in 'i4', 'i3', 'i2', 'i1') { [void]$h.Add($t) }
        foreach ($t in 'p3', 'p2', 'p1') { [void]$h.PinText($t) }
        Check '並べ替え: (準備) ピン止め p1,p2,p3 / 履歴 i1,i2,i3,i4' ((($h.Pinned -join ',') -ceq 'p1,p2,p3') -and (($h.Items -join ',') -ceq 'i1,i2,i3,i4'))
        Check '並べ替え: ピン止めを上へ移すと、間のものは 1 つずつ下がる' ($h.Move('p3', 0) -and ($h.Pinned -join ',') -ceq 'p3,p1,p2') "pinned=$($h.Pinned -join ',')"
        Check '並べ替え: ピン止めを下へ移す' ($h.Move('p3', 2) -and ($h.Pinned -join ',') -ceq 'p1,p2,p3') "pinned=$($h.Pinned -join ',')"
        Check '並べ替え: 普通の履歴を上へ移す (ピン止めは変わらない)' ($h.Move('i4', 0) -and ($h.Items -join ',') -ceq 'i4,i1,i2,i3' -and ($h.Pinned -join ',') -ceq 'p1,p2,p3') "items=$($h.Items -join ',')"
        Check '並べ替え: 普通の履歴を下へ移す' ($h.Move('i4', 2) -and ($h.Items -join ',') -ceq 'i1,i2,i4,i3') "items=$($h.Items -join ',')"
        Check '並べ替え: 同じ位置なら何も変わらない' ((-not $h.Move('i1', 0)) -and ($h.Items -join ',') -ceq 'i1,i2,i4,i3')
        Check '並べ替え: 範囲の外には移さない' ((-not $h.Move('i1', 4)) -and (-not $h.Move('i1', -1)) -and (-not $h.Move('p1', 3)) -and ($h.Items -join ',') -ceq 'i1,i2,i4,i3' -and ($h.Pinned -join ',') -ceq 'p1,p2,p3')
        Check '並べ替え: 無い内容は何もしない' ((-not $h.Move('無い', 0)) -and (-not $h.Move($null, 0)))

        # 定型文から履歴にピン止めする (履歴に無い内容でもピン止めできる)
        $h = New-Object Copipe.Services.ClipboardHistory 3
        foreach ($t in 'a', 'b') { [void]$h.Add($t) }
        [void]$h.Pin('a')
        Check '定型文のピン止め: 履歴に無い内容はピン止めの先頭に加わる' ($h.PinText('定型') -and ($h.Pinned -join ',') -ceq '定型,a' -and ($h.Items -join ',') -ceq 'b') "pinned=$($h.Pinned -join ',') items=$($h.Items -join ',')"
        Check '定型文のピン止め: 普通の履歴にある内容は、そこから移る' ($h.PinText('b') -and ($h.Pinned -join ',') -ceq 'b,定型,a' -and $h.Items.Count -eq 0) "pinned=$($h.Pinned -join ',') items=$($h.Items -join ',')"
        Check '定型文のピン止め: ピン止め済みの内容は、ピン止めの先頭へ移る (増えない)' ($h.PinText('a') -and ($h.Pinned -join ',') -ceq 'a,b,定型') "pinned=$($h.Pinned -join ',')"
        Check '定型文のピン止め: 先頭と同じなら何も変わらない' ((-not $h.PinText('a')) -and ($h.Pinned -join ',') -ceq 'a,b,定型')
        Check '定型文のピン止め: 空の内容はピン止めしない' ((-not $h.PinText('')) -and (-not $h.PinText($null)) -and $h.Pinned.Count -eq 3)
        Check '定型文のピン止め: 長すぎる内容はピン止めしない (コピーと同じ上限)' ((-not $h.PinText(('x' * ($H::MaxTextLength + 1)))) -and $h.Pinned.Count -eq 3)
        for ($i = $h.Pinned.Count; $i -lt 26; $i++) { [void]$h.PinText("満$i") }
        [void]$h.Add('あふれ')
        Check 'ピン止めの上限: 26 件で満杯' ($h.Pinned.Count -eq 26 -and $h.IsPinnedFull)
        Check 'ピン止めの上限: 満杯なら履歴の項目をピン止めしない' ((-not $h.Pin('あふれ')) -and $h.Pinned.Count -eq 26 -and $h.Items.Contains('あふれ'))
        Check 'ピン止めの上限: 満杯なら新しい内容をピン止めしない' ((-not $h.PinText('新しい')) -and $h.Pinned.Count -eq 26)
        Check 'ピン止めの上限: 満杯でもピン止め済みの内容は先頭へ移せる' ($h.PinText('b') -and $h.Pinned[0] -ceq 'b' -and $h.Pinned.Count -eq 26)

        $h = New-Object Copipe.Services.ClipboardHistory 10
        foreach ($t in 'p1', 'p2', 'x', 'y') { [void]$h.Add($t) }
        [void]$h.Pin('p1'); [void]$h.Pin('p2')
        $h.Save($historyPath)
        $loaded = $H::Load($historyPath, 10)
        Check 'ピン止め: 保存して読み戻せる (順番も)' (($loaded.Pinned -join ',') -ceq 'p2,p1' -and ($loaded.Items -join ',') -ceq 'y,x') "pinned=$($loaded.Pinned -join ',') items=$($loaded.Items -join ',')"
        $loaded = $H::Load($historyPath, 1)
        Check 'ピン止め: 保持数より多くても、ピン止めは全部読む' ($loaded.Pinned.Count -eq 2 -and $loaded.Items.Count -eq 1)
        Set-Content -LiteralPath $historyPath -Encoding UTF8 -Value '{"Items":["x","y"]}'
        $loaded = $H::Load($historyPath, 10)
        Check 'ピン止め: Pinned の無い前の形式の history.json も読める' ($loaded.Pinned.Count -eq 0 -and ($loaded.Items -join ',') -ceq 'x,y')
        Set-Content -LiteralPath $historyPath -Encoding UTF8 -Value '{"Items":["x","p","y"],"Pinned":["p","","p","q"]}'
        $loaded = $H::Load($historyPath, 10)
        Check 'ピン止め: 空・重複は捨て、普通の履歴と重なった内容はピン止めを優先して読む' (($loaded.Pinned -join ',') -ceq 'p,q' -and ($loaded.Items -join ',') -ceq 'x,y') "pinned=$($loaded.Pinned -join ',') items=$($loaded.Items -join ',')"
    }

    try {
        if ([System.Windows.Forms.Clipboard]::ContainsText()) {
            $clipBackup = [System.Windows.Forms.Clipboard]::GetText()
        }
    } catch {
        Write-Host ("警告: 元のクリップボードを退避できなかったので、最後に戻しません: " + $_.Exception.Message) -ForegroundColor Yellow
    }

    if (-not (Test-ClipboardAvailable)) {
        Write-Host ''
        Write-Host '検証 5・6 を実行できません: 他のアプリがクリップボードを開いたままです。' -ForegroundColor Yellow
        Write-Host '  Copipe の不具合ではなく、このパソコンの今の状態が原因です。' -ForegroundColor Yellow
        Write-Host '  クリップボード履歴ツール (CopyQ・Clibor など) を終了するか、メモ帳などで何かコピーし直してから、もう一度実行してください。' -ForegroundColor Yellow
        Write-Host ''
        Write-Host ("結果: 検証 1〜4 のみ実行 (成功 {0} 件 / 失敗 {1} 件)。検証 5・6 は未実行" -f $script:Pass, $script:Fail) -ForegroundColor Yellow
        exit 2
    }

    # ==========================================================================
    Section '検証 4b: 定型文 (PhraseNode / PhraseBook)' {
    # ==========================================================================
        $PN = [Copipe.Model.PhraseNode]
        $PB = [Copipe.Services.PhraseBook]
        $phraseDir = Join-Path $tempDir 'phrases'
        [void](New-Item -ItemType Directory -Path $phraseDir -Force)
        $phrasePath = Join-Path $phraseDir 'phrases.json'

        Check '保存先の既定は %LOCALAPPDATA%\Copipe\phrases.json' ($PB::DefaultPath -ceq (Join-Path $env:LOCALAPPDATA 'Copipe\phrases.json')) "got=$($PB::DefaultPath)"
        Check '1 階層の枠は 10 個' ($PN::SlotCount -eq 10)

        $book = $PB::Load((Join-Path $phraseDir 'none.json'))
        Check 'ファイルが無ければ、一番上に空の枠が 10 個' ($book.Root.IsGroup -and $book.Root.Slots.Count -eq 10 -and @($book.Root.Slots | Where-Object { $null -ne $_ }).Count -eq 0)

        # 表示名: 定型文は表示名、無ければ本文の最初の空でない行。グループは名前
        Check '表示名: 表示名があればそれ' ($PN::CreatePhrase('挨拶', "お世話に`r`nなっております", '').Label -ceq '挨拶')
        Check '表示名: 表示名が空なら本文の最初の空でない行' ($PN::CreatePhrase('', "`r`n  `r`n  お世話に  `r`nなっております", '').Label -ceq 'お世話に')
        Check '表示名: グループは名前' ($PN::CreateGroup('社外').Label -ceq '社外')
        Check '種類: グループは IsGroup' ($PN::CreateGroup('社外').IsGroup -and -not $PN::CreatePhrase('', 'x', '').IsGroup)
        Check 'グループを作ると空の枠が 10 個ある' ($PN::CreateGroup('社外').Slots.Count -eq 10)

        # 保存して読み戻す (入れ子のグループ、空きの枠、改行・タブ・記号を含む本文)
        $root = $book.Root
        $outer = $PN::CreateGroup('社外')
        $inner = $PN::CreateGroup('挨拶')
        $inner.Slots[9] = $PN::CreatePhrase('', "いつもお世話になっております。`r`n`t株式会社 A&B", '')
        $outer.Slots[0] = $inner
        $outer.Slots[2] = $PN::CreatePhrase('締め', '取り急ぎご連絡まで。', '')
        $root.Slots[1] = $outer
        $root.Slots[4] = $PN::CreatePhrase('', '"引用" \ 円記号', '')
        $book.Save($phrasePath)
        $again = $PB::Load($phrasePath)
        $r = $again.Root
        Check '保存: 一番上の 2 番目がグループ「社外」' ($null -ne $r.Slots[1] -and $r.Slots[1].IsGroup -and $r.Slots[1].Label -ceq '社外')
        Check '保存: 空きの枠は空きのまま' ($null -eq $r.Slots[0] -and $null -eq $r.Slots[9])
        Check '保存: 入れ子のグループの中の定型文 (0 番) も戻る' ($r.Slots[1].Slots[0].IsGroup -and $r.Slots[1].Slots[0].Slots[9].Text -ceq "いつもお世話になっております。`r`n`t株式会社 A&B")
        Check '保存: 表示名も戻る' ($r.Slots[1].Slots[2].Label -ceq '締め' -and $r.Slots[1].Slots[2].Title -ceq '締め')
        Check '保存: 記号を含む本文も戻る' ($r.Slots[4].Text -ceq '"引用" \ 円記号')
        Check '保存: 手で編集できるよう、JSON は人が読める (日本語がそのまま書かれている)' ((Get-Content -LiteralPath $phrasePath -Raw -Encoding UTF8) -match '社外')

        # 手で書いた JSON (枠が足りない・多すぎる・読めない種類)
        Set-Content -LiteralPath $phrasePath -Encoding UTF8 -Value '{"Slots":[{"Kind":"Phrase","Text":"一つ目"},null,{"Kind":"Group","Name":"G","Slots":[{"Kind":"Phrase","Text":"中"}]}]}'
        $r = $PB::Load($phrasePath).Root
        Check '手書き: 枠が 10 個に足りなければ空きで埋める' ($r.Slots.Count -eq 10 -and $r.Slots[0].Text -ceq '一つ目' -and $null -eq $r.Slots[1] -and $null -eq $r.Slots[9])
        Check '手書き: グループの中も 10 個にそろえる' ($r.Slots[2].IsGroup -and $r.Slots[2].Slots.Count -eq 10 -and $r.Slots[2].Slots[0].Text -ceq '中')
        $many = '{"Slots":[' + ((1..12 | ForEach-Object { '{"Kind":"Phrase","Text":"p' + $_ + '"}' }) -join ',') + ']}'
        Set-Content -LiteralPath $phrasePath -Encoding UTF8 -Value $many
        $r = $PB::Load($phrasePath).Root
        Check '手書き: 11 個目以降は読み捨てる' ($r.Slots.Count -eq 10 -and $r.Slots[9].Text -ceq 'p10')
        Set-Content -LiteralPath $phrasePath -Encoding UTF8 -Value '{"Slots":[{"Kind":"Folder","Name":"x"},{"Kind":"Phrase"},{"Kind":"Phrase","Text":""},{"Kind":"Group"}]}'
        $r = $PB::Load($phrasePath).Root
        Check '手書き: 読めない種類・本文の無い定型文は空きにする' ($null -eq $r.Slots[0] -and $null -eq $r.Slots[1] -and $null -eq $r.Slots[2])
        Check '手書き: 名前の無いグループは名前を空にして読む' ($r.Slots[3].IsGroup -and $r.Slots[3].Label -ceq '')
        # ---- ドラッグ＆ドロップの並べ替え (PhraseMoves) ----
        $PM = [Copipe.Model.PhraseMoves]
        # root: [A, G(中: [x]), 空き, B, 満杯(10 件)]、G の中に H (グループ)
        function New-Tree {
            $t = @{}
            $t.Root = $PN::CreateGroup('')
            $t.A = $PN::CreatePhrase('', 'A', ''); $t.B = $PN::CreatePhrase('', 'B', ''); $t.X = $PN::CreatePhrase('', 'x', '')
            $t.G = $PN::CreateGroup('G'); $t.H = $PN::CreateGroup('H'); $t.Full = $PN::CreateGroup('満杯')
            $t.G.Slots[0] = $t.X; $t.G.Slots[1] = $t.H
            for ($i = 0; $i -lt 10; $i++) { $t.Full.Slots[$i] = $PN::CreatePhrase('', "f$i", '') }
            $t.Root.Slots[0] = $t.A; $t.Root.Slots[1] = $t.G; $t.Root.Slots[3] = $t.B; $t.Root.Slots[4] = $t.Full
            return $t
        }
        $t = New-Tree
        Check '並べ替え: 同じ階層の定型文どうしを入れ替えられる' ($PM::Swap($t.Root, 0, $t.Root, 3) -and $t.Root.Slots[0] -eq $t.B -and $t.Root.Slots[3] -eq $t.A)
        $t = New-Tree
        Check '並べ替え: 空きの枠に移せる (元は空きになる)' ($PM::Swap($t.Root, 0, $t.Root, 2) -and $t.Root.Slots[2] -eq $t.A -and $null -eq $t.Root.Slots[0])
        $t = New-Tree
        Check '並べ替え: 定型文とグループも入れ替えられる' ($PM::Swap($t.Root, 0, $t.Root, 1) -and $t.Root.Slots[0] -eq $t.G -and $t.Root.Slots[1] -eq $t.A)
        $t = New-Tree
        Check '並べ替え: 同じ枠どうしは入れ替えない' (-not $PM::CanSwap($t.Root, 0, $t.Root, 0))
        Check '並べ替え: 空きの枠はドラッグできない' (-not $PM::CanSwap($t.Root, 2, $t.Root, 0) -and -not $PM::CanMoveInto($t.Root, 2, $t.G))
        Check '並べ替え: 別の階層の枠とも入れ替えられる (G の中の x と、一番上の A)' ($PM::Swap($t.G, 0, $t.Root, 0) -and $t.Root.Slots[0] -eq $t.X -and $t.G.Slots[0] -eq $t.A)
        $t = New-Tree
        Check '並べ替え: グループを自分の中の枠と入れ替えられない (入れ子が輪になる)' (-not $PM::CanSwap($t.Root, 1, $t.G, 0))
        Check '並べ替え: 中の項目を、それを含むグループと入れ替えられない (入れ子が輪になる)' (-not $PM::CanSwap($t.G, 0, $t.Root, 1))
        Check '並べ替え: 中の項目を、それを含むグループの外の枠となら入れ替えられる' ($PM::CanSwap($t.G, 1, $t.Root, 3))

        $t = New-Tree
        Check 'グループに入れる: 最初の空きに入り、元は空きになる' ($PM::MoveInto($t.Root, 0, $t.G) -and $t.G.Slots[2] -eq $t.A -and $null -eq $t.Root.Slots[0])
        $t = New-Tree
        Check 'グループに入れる: 空きの無いグループには入れない (何も変わらない)' ((-not $PM::MoveInto($t.Root, 0, $t.Full)) -and $t.Root.Slots[0] -eq $t.A)
        Check 'グループに入れる: 自分自身には入れない' (-not $PM::CanMoveInto($t.Root, 1, $t.G))
        Check 'グループに入れる: 自分の中のグループには入れない' (-not $PM::CanMoveInto($t.Root, 1, $t.H))
        Check 'グループに入れる: 今いるグループには入れない (変わらないので)' (-not $PM::CanMoveInto($t.Root, 0, $t.Root))
        Check 'グループに入れる: 中の項目を上の階層に出せる' ($PM::MoveInto($t.G, 0, $t.Root) -and $t.Root.Slots[2] -eq $t.X -and $null -eq $t.G.Slots[0])
        $t = New-Tree
        Check '含むか: G は H を含む、H は G を含まない、自分自身は含む' ($PM::Contains($t.G, $t.H) -and -not $PM::Contains($t.H, $t.G) -and $PM::Contains($t.G, $t.G))
        Check '含むか: 定型文は自分以外を含まない' (-not $PM::Contains($t.A, $t.G))

        foreach ($bad in 'これは JSON ではない', '', '{"Slots":5}', '[]') {
            Set-Content -LiteralPath $phrasePath -Encoding UTF8 -Value $bad
            $e = Get-ThrownException { $script:badBook = $PB::Load($phrasePath) }
            Check "手書き: 壊れたファイル '$bad' でも例外にせず、空の 10 枠で読む" ($null -eq $e -and $script:badBook.Root.Slots.Count -eq 10 -and @($script:badBook.Root.Slots | Where-Object { $null -ne $_ }).Count -eq 0) "e=$e"
        }
    }

    # 起動中の Copipe は、ここから先の検証がクリップボードに置く文字を履歴に拾ってしまう。
    # 先に止め、最後に起動し直す (後片付けを参照)
    $script:copipeWasRunning = @(Get-Process -Name 'Copipe' -ErrorAction SilentlyContinue |
                                 Where-Object { $_.Path -and ($_.Path -ieq $exe) }).Count -gt 0
    & (Join-Path $root 'tools\Stop-Copipe.ps1') -ExePath $exe

    # ==========================================================================
    Section '検証 5: クリップボードの読み取り (ClipboardReader)' {
    # ==========================================================================
        $CB = [System.Windows.Forms.Clipboard]
        $read = { [Copipe.Services.ClipboardReader]::Read($owner) }

        $e = Get-ThrownException { [void][Copipe.Services.ClipboardReader]::Read([IntPtr]::Zero) }
        Check 'ウインドウ指定なし (IntPtr.Zero) は ArgumentException (排他制御から外れるため)' ($e -is [ArgumentException]) (Format-ExceptionText $e)

        $text = "日本語 & テスト`tタブ`r`n2行目 " + [char]::ConvertFromUtf32(0x1F600)
        Set-ClipboardText ($text)
        $r = & $read
        Check 'テキスト: 種類が Text' ($r.Kind -eq 'Text') "kind=$($r.Kind)"
        Check 'テキスト: 内容が一致 (日本語・&・タブ・改行・絵文字)' ($r.Text -ceq $text) "got=[$($r.Text)]"

        $f1 = Join-Path $tempDir 'a.txt'
        $f2 = Join-Path $tempDir 'b 日本語.xlsx'
        Set-Content -LiteralPath $f1 -Value 'a'
        Set-Content -LiteralPath $f2 -Value 'b'
        $list = New-Object System.Collections.Specialized.StringCollection
        [void]$list.Add($f1); [void]$list.Add($f2)
        Invoke-ClipboardWrite { $CB::SetFileDropList($list) }
        $r = & $read
        Check 'ファイル: 種類が Files' ($r.Kind -eq 'Files') "kind=$($r.Kind)"
        Check 'ファイル: パスが一致 (2 件・日本語名)' ($r.Files.Count -eq 2 -and $r.Files[0] -ceq $f1 -and $r.Files[1] -ceq $f2) "got=$($r.Files -join ' | ')"

        # 1 件ずつ DragQueryFile で取り出すと件数の 2 乗の時間がかかる (実測: 1 万件で約 3 秒)
        $manyList = New-Object System.Collections.Specialized.StringCollection
        for ($i = 0; $i -lt 10000; $i++) { [void]$manyList.Add(('C:\Users\someone\Documents\業務\2026年度\資料\file_{0:D6}.xlsx' -f $i)) }
        Invoke-ClipboardWrite { $CB::SetFileDropList($manyList) }
        $sw = [Diagnostics.Stopwatch]::StartNew()
        $r = & $read
        $sw.Stop()
        Check 'ファイル 1 万件: 件数と先頭・末尾のパスが一致' ($r.Kind -eq 'Files' -and $r.Files.Count -eq 10000 -and $r.Files[0] -ceq $manyList[0] -and $r.Files[9999] -ceq $manyList[9999]) "kind=$($r.Kind) count=$($r.Files.Count)"
        Info ("ファイル 1 万件の読み取り時間: {0} ms" -f $sw.ElapsedMilliseconds)
        Check 'ファイル 1 万件: 200 ms 未満 (小窓の表示を待たせない)' ($sw.ElapsedMilliseconds -lt 200) "$($sw.ElapsedMilliseconds) ms"

        # 古いアプリが置く ANSI 版の CF_HDROP (DROPFILES.fWide = 0)。名前はシステムのコードページ (日本語 Windows では Shift-JIS)
        $ansiNames = @('C:\ansi\a.txt', 'C:\ansi\日本語フォルダー\表.xls')
        $ansiEnc = [Text.Encoding]::Default
        $bytes = New-Object System.Collections.Generic.List[byte]
        $header = New-Object byte[] 20
        [BitConverter]::GetBytes([int]20).CopyTo($header, 0)   # pFiles: 名前の並びの開始位置。fWide (16 バイト目) は 0
        $bytes.AddRange($header)
        foreach ($n in $ansiNames) { $bytes.AddRange($ansiEnc.GetBytes($n)); $bytes.Add(0) }
        $bytes.Add(0)
        $raw = $bytes.ToArray()
        $hMem = [CopipeVerify.Native]::GlobalAlloc(0x0002 <# GMEM_MOVEABLE #>, [UIntPtr][uint32]$raw.Length)
        $pMem = [CopipeVerify.Native]::GlobalLock($hMem)
        [Runtime.InteropServices.Marshal]::Copy($raw, 0, $pMem, $raw.Length)
        [void][CopipeVerify.Native]::GlobalUnlock($hMem)
        # SetClipboardData にはウインドウを指定して開いたクリップボードが要る
        Invoke-ClipboardOpen -Window $owner {
            [void][CopipeVerify.Native]::EmptyClipboard()
            if ([CopipeVerify.Native]::SetClipboardData(15 <# CF_HDROP #>, $hMem) -eq [IntPtr]::Zero) {
                [void][CopipeVerify.Native]::GlobalFree($hMem)
                throw 'ANSI 版 CF_HDROP を置けませんでした'
            }
        }
        $r = & $read
        Check 'ファイル (ANSI 版 DROPFILES): 日本語名も含めてパスが一致' ($r.Kind -eq 'Files' -and $r.Files.Count -eq 2 -and $r.Files[0] -ceq $ansiNames[0] -and $r.Files[1] -ceq $ansiNames[1]) "kind=$($r.Kind) got=$($r.Files -join ' | ')"

        $bmp = New-Object System.Drawing.Bitmap 8, 8
        Invoke-ClipboardWrite { $CB::SetImage($bmp) }
        $r = & $read
        Check '画像: 種類が Image' ($r.Kind -eq 'Image') "kind=$($r.Kind)"

        $both = New-Object System.Windows.Forms.DataObject
        $both.SetText('テキストと画像')
        $both.SetImage($bmp)
        Invoke-ClipboardWrite { $CB::SetDataObject($both, $true) }
        $r = & $read
        Check 'テキストと画像の両方 (Excel など): テキストを優先' ($r.Kind -eq 'Text' -and $r.Text -ceq 'テキストと画像') "kind=$($r.Kind)"
        $bmp.Dispose()

        Invoke-ClipboardWrite { $CB::SetData('CopipeVerifyFormat', 'x') }
        $r = & $read
        Check '独自形式だけ: 種類が Other' ($r.Kind -eq 'Other') "kind=$($r.Kind)"

        Set-ClipboardText ('空にする前')
        Invoke-ClipboardOpen { [void][CopipeVerify.Native]::EmptyClipboard() }
        $r = & $read
        Check '空 (EmptyClipboard): 種類が Empty' ($r.Kind -eq 'Empty') "kind=$($r.Kind)"

        # WinForms の Clipboard.Clear() は空の DataObject を置くので、OLE 管理用の形式
        # (DataObject / Ole Private Data) だけが残る (実測)。利用者から見れば空なので Empty とする
        Set-ClipboardText ('空にする前')
        Invoke-ClipboardWrite { $CB::Clear() }
        $r = & $read
        Check '空 (WinForms の Clipboard.Clear で OLE 管理用の形式だけ残る): 種類が Empty' ($r.Kind -eq 'Empty') "kind=$($r.Kind)"

        $big = New-Object string ([char]'z'), 5000000
        Set-ClipboardText ($big)
        $sw = [Diagnostics.Stopwatch]::StartNew()
        $r = & $read
        $sw.Stop()
        Check '巨大なテキスト (500 万文字): 全文を読める' ($r.Kind -eq 'Text' -and $r.Text.Length -eq 5000000) "kind=$($r.Kind) length=$($r.Text.Length)"
        Info ("巨大なテキストの読み取り時間: {0} ms" -f $sw.ElapsedMilliseconds)
        Check '巨大なテキスト: 200 ms 未満' ($sw.ElapsedMilliseconds -lt 200) "$($sw.ElapsedMilliseconds) ms"
        $big = $null; $r = $null
        [GC]::Collect()

        # テキストがあると報告されるのに、持ち主のアプリが描画しない (応答しない・描画に失敗した) 場合
        $helper = Start-ClipboardHelper -Mode unrendered -HoldMs 1500
        Check '(準備) 別プロセスが、描画しない遅延レンダリングのテキストを置いた' ($null -ne $helper)
        if ($helper) {
            try {
                $r = & $read
                Check 'テキストがあるのに読めないとき: Unavailable (「テキスト以外のデータ」にしない)' ($r.Kind -eq 'Unavailable') "kind=$($r.Kind)"
            } finally {
                Stop-ClipboardHelper $helper
            }
        }

        # 別プロセスがクリップボードを開いたままにしている状態
        Set-ClipboardText ('開いたまま検証')
        $helper = Start-ClipboardHelper -Mode hold -HoldMs 2500
        Check '(準備) 別プロセスがクリップボードを開いた' ($null -ne $helper)
        if ($helper) {
            try {
                $sw = [Diagnostics.Stopwatch]::StartNew()
                $r = & $read
                $sw.Stop()
                Check '開けないとき: 種類が Unavailable' ($r.Kind -eq 'Unavailable') "kind=$($r.Kind)"
                Info ("開けないときに戻るまでの時間: {0} ms" -f $sw.ElapsedMilliseconds)
                Check '開けないとき: 200 ms 未満で戻る (固まらない)' ($sw.ElapsedMilliseconds -lt 200) "$($sw.ElapsedMilliseconds) ms"
            } finally {
                Stop-ClipboardHelper $helper
            }
            $r = & $read
            Check '相手が閉じた後は再び読める' ($r.Kind -eq 'Text' -and $r.Text -ceq '開いたまま検証') "kind=$($r.Kind)"
        }

        # 書き込み (ClipboardWriter)。ダブルクリックで入力するときに使う
        $W8 = [Copipe.Services.ClipboardWriter]
        $writeText = "書き込み検証`r`n改行`tタブ " + [char]::ConvertFromUtf32(0x1F600)
        Check '書き込み: 自分のウインドウを指定して書ける' ($W8::SetText($owner, $writeText))
        $r = & $read
        Check '書き込み: 書いた内容がそのまま読める (改行・タブ・絵文字)' ($r.Kind -eq 'Text' -and $r.Text -ceq $writeText) "kind=$($r.Kind)"
        Check '書き込み: 持ち主が自分のウインドウになる' ($W8::IsOwnedBy($owner))
        Set-ClipboardText ('他のアプリが書き込んだ代わり')
        Check '書き込み: 他が書き込んだら、持ち主は自分でなくなる' (-not $W8::IsOwnedBy($owner))
        Check '書き込み: IsOwnedBy(IntPtr.Zero) は false' (-not $W8::IsOwnedBy([IntPtr]::Zero))
        $e = Get-ThrownException { [void]$W8::SetText([IntPtr]::Zero, 'x') }
        Check '書き込み: ウインドウ指定なし (IntPtr.Zero) は ArgumentException' ($e -is [ArgumentException]) (Format-ExceptionText $e)
        # PowerShell は .NET の string 引数に $null を渡すと空文字列にしてしまうので、本物の null を渡す
        $e = Get-ThrownException { [void]$W8::SetText($owner, [NullString]::Value) }
        Check '書き込み: null は ArgumentNullException' ($e -is [ArgumentNullException]) (Format-ExceptionText $e)
    }

    # ==========================================================================
    Section '検証 6: E2E (bin\Copipe.exe を起動してホットキーを擬似入力)' {
    # ==========================================================================
        if ($SkipE2E) { Info '-SkipE2E が指定されたので省略'; return }
        $runHistory = ($E2E -ne 'Phrases')
        $runPhrases = ($E2E -ne 'History')
        if ($E2E -ne 'All') { Info "-E2E ${E2E}: $(if ($runHistory) { '履歴モード' } else { '定型文モード' }) の E2E だけ流す" }

        $W = [CopipeVerify.Win]
        $stopScript = Join-Path $root 'tools\Stop-Copipe.ps1'

        $script:hotkeyDown = $false

        # 設定ファイルにホットキーを書き、ハーネスが擬似入力するキーもそれに合わせる
        # 貼り付けの操作は毎回はっきり書く (利用者の設定が検証に紛れ込まないように)
        function Use-Hotkey([System.Windows.Forms.Keys]$Keys, [string]$InsertClick = 'Double') {
            $settings = [Copipe.Services.Settings]::Load([Copipe.Services.Settings]::DefaultPath)
            $settings.Hotkey = $Keys
            $settings.InsertClick = [Copipe.Services.InsertClick]$InsertClick
            $settings.ModeKey = [System.Windows.Forms.Keys]::Tab
            $settings.Save([Copipe.Services.Settings]::DefaultPath)
            $script:hotkeyKeys = $Keys
            $script:hotkeyName = [Copipe.UI.HotkeyText]::Display($Keys)
            $script:hotkeySetting = [Copipe.UI.HotkeyText]::ToSetting($Keys)
            $script:keyVk = [byte](([int]$Keys) -band 0xFFFF)
            $script:modifierVks = @()
            if (($Keys -band [System.Windows.Forms.Keys]::Control) -ne 0) { $script:modifierVks += [byte]0x11 }
            if (($Keys -band [System.Windows.Forms.Keys]::Shift) -ne 0)   { $script:modifierVks += [byte]0x10 }
            if (($Keys -band [System.Windows.Forms.Keys]::Alt) -ne 0)     { $script:modifierVks += [byte]0x12 }
            Info "ホットキー: $script:hotkeyName (設定ファイルに $script:hotkeySetting と書いた)"
        }

        # そのキーの擬似入力が本当に届くか (別のアプリが低レベルフックで横取りしていると届かない。
        # このマシンでは F1 が横取りされていた)
        function Test-InjectedKey([System.Windows.Forms.Keys]$Keys) {
            $vk = [byte](([int]$Keys) -band 0xFFFF)
            $W::KeyDown($vk)
            Start-Sleep -Milliseconds 80
            $arrived = $W::IsKeyDown($vk)
            $W::KeyUp($vk)
            Start-Sleep -Milliseconds 80
            return $arrived
        }

        # 登録できて、かつ擬似入力も届くホットキーを選ぶ
        function Find-UsableHotkey([System.Windows.Forms.Keys[]]$Candidates) {
            $K = [System.Windows.Forms.Keys]
            foreach ($candidate in $Candidates) {
                $mod = 0
                if (($candidate -band $K::Alt) -ne 0)     { $mod = $mod -bor 1 }
                if (($candidate -band $K::Control) -ne 0) { $mod = $mod -bor 2 }
                if (($candidate -band $K::Shift) -ne 0)   { $mod = $mod -bor 4 }
                if (-not $W::CanRegisterHotkey($owner, [uint32]$mod, [uint32]([int]$candidate -band 0xFFFF))) { continue }
                if (-not (Test-InjectedKey $candidate)) {
                    Info ("{0} は擬似入力が届かない (別のアプリが横取りしている) ので使わない" -f [Copipe.UI.HotkeyText]::Display($candidate))
                    continue
                }
                return $candidate
            }
            return $null
        }

        # 離してから 100 ms は押し直さない。Copipe は離したことを短い間隔で調べて気づくので、
        # 気づく前に押し直すと (人の手では起きない速さ)、押し続けている (キーリピート) とみなされて小窓が出ない。
        # ダイアログを閉じた直後は気づくのが遅れ、数 ms 差で押し直して失敗していた (実測)
        $script:hotkeyReleasedAt = [Diagnostics.Stopwatch]::StartNew()
        function Invoke-HotkeyPress {
            while ($script:hotkeyReleasedAt.ElapsedMilliseconds -lt 100) {
                [System.Windows.Forms.Application]::DoEvents()
                Start-Sleep -Milliseconds 5
            }
            foreach ($m in $script:modifierVks) { $W::KeyDown($m) }
            $W::KeyDown($script:keyVk)
            $script:hotkeyDown = $true
        }
        function Invoke-HotkeyRelease {
            $W::KeyUp($script:keyVk)
            for ($i = $script:modifierVks.Count - 1; $i -ge 0; $i--) { $W::KeyUp($script:modifierVks[$i]) }
            $script:hotkeyDown = $false
            $script:hotkeyReleasedAt.Restart()
        }

        function Find-Popup([int]$ProcessId) {
            foreach ($h in $W::TopWindows($ProcessId)) {
                if ($W::GetClass($h) -ne '#32770' -and $W::GetText($h) -ceq 'Copipe') { return $h }
            }
            return [IntPtr]::Zero
        }

        function Find-Dialog([int]$ProcessId) {
            foreach ($h in $W::TopWindows($ProcessId)) {
                if ($W::GetClass($h) -eq '#32770' -and $W::IsWindowVisible($h)) { return $h }
            }
            return [IntPtr]::Zero
        }

        # 小窓の一覧 (ListBox) の項目を、外から読む (読めなければ空の配列)。
        # PowerShell は 1 要素の配列を返すと中身の文字列に展開してしまうので、, を付けて配列のまま返す
        # 文字列の項目の先頭の ✍ は外して返す (-Raw なら付けたまま)
        function Get-PopupItems([IntPtr]$Popup, [switch]$Raw) {
            foreach ($child in $W::Children($Popup)) {
                if ($W::GetClass($child) -like '*LISTBOX*') {
                    $items = $W::ListItems($child)
                    if ($null -eq $items) { return ,@() }
                    if (-not $Raw) {
                        $mark = '✍ '   # PopupRow.TextMark (internal なので読めない)
                        $items = @($items | ForEach-Object { if ($_.StartsWith($mark, [StringComparison]::Ordinal)) { $_.Substring($mark.Length) } else { $_ } })
                    }
                    return ,$items
                }
            }
            return ,@()
        }

        # 小窓の一覧の Index 番目の項目の真ん中 (画面座標)
        function Get-ItemCenter([IntPtr]$Popup, [int]$Index) {
            foreach ($child in $W::Children($Popup)) {
                if ($W::GetClass($child) -like '*LISTBOX*') {
                    $r = $W::GetRect($child)
                    $h = $W::ListItemHeight($child)
                    return (Pt ($r.Left + [int]($r.Width / 2)) ($r.Top + $h * $Index + [int]($h / 2)))
                }
            }
            return $null
        }

        function Format-Items($Items) {
            return ('[' + (@($Items) -join '] [') + ']')
        }

        # カーソルを At に置き、クリップボードに目印を入れてホットキーを押し続け、表示・内容・位置・消え方を確認する
        function Test-Hold([System.Drawing.Point]$At, [string]$Label, [IntPtr]$Popup, [string]$Text, [string]$Screenshot, [switch]$ClickPopup) {
            [void]$W::SetCursorPos($At.X, $At.Y)
            if (-not $Text) { $Text = "Copipe検証 & テスト`t" + [Guid]::NewGuid().ToString('N') }
            Set-ClipboardText ($Text)
            # CopyQ などがコピー直後にクリップボードを読み終えるのを待つ
            Start-Sleep -Milliseconds 400
            $fg = $W::GetForegroundWindow()

            # 置いたつもりの位置ではなく、押す直前に実際に読んだカーソル位置で検証する
            # (検証中に人がマウスに触れることがあり、アプリはそのときの位置を見るため)
            $actual = $W::GetCursor()
            if ($actual -ne $At) { Info ("${Label}: カーソルが " + $actual + " に動いていたので、その位置で検証する") }
            $At = $actual

            Invoke-HotkeyPress
            $shownMs = Wait-Until { $W::IsWindowVisible($Popup) } 1000
            Check "${Label}: $hotkeyName を押すと小窓が表示される" ($shownMs -ge 0)
            if ($shownMs -ge 0) {
                Info "${Label}: 表示までの時間 $shownMs ms"
                # 一覧は新しい順なので、いまコピーした内容 (全文) が先頭にあるはず
                $items = Get-PopupItems $Popup
                Check "${Label}: 一覧の先頭に、いまコピーした内容がある" ($items.Count -ge 1 -and $items[0] -ceq $Text) ("items=" + (Format-Items $items))
                $raw = Get-PopupItems $Popup -Raw
                Check "${Label}: 文字列の項目は先頭に ✍ が付く" ($raw.Count -ge 1 -and $raw[0] -ceq ('✍ ' + $Text)) ("items=" + (Format-Items $raw))
                Check "${Label}: フォアグラウンドのウインドウが変わらない" ($W::GetForegroundWindow() -eq $fg) "before=$fg after=$($W::GetForegroundWindow())"

                $r = $W::GetRect($Popup)
                $wa = [System.Windows.Forms.Screen]::FromPoint($At).WorkingArea
                Check "${Label}: カーソルのあるモニターの作業領域に収まる" ($wa.Contains($r)) "popup=$r workingArea=$wa"
                Check "${Label}: 小窓がカーソルに重ならない" (-not $r.Contains($At)) "popup=$r cursor=$At"

                if ($Screenshot) {
                    $margin = 24
                    $bmp = New-Object System.Drawing.Bitmap ($r.Width + $margin * 2), ($r.Height + $margin * 2)
                    $g = [System.Drawing.Graphics]::FromImage($bmp)
                    $g.CopyFromScreen($r.Left - $margin, $r.Top - $margin, 0, 0, $bmp.Size)
                    $g.Dispose()
                    $bmp.Save($Screenshot, [System.Drawing.Imaging.ImageFormat]::Png)
                    $bmp.Dispose()
                    Info "${Label}: スクリーンショットを保存 $Screenshot"
                }

                if ($ClickPopup) {
                    $cx = $r.Left + [int]($r.Width / 2)
                    $cy = $r.Top + [int]($r.Height / 2)
                    [void]$W::SetCursorPos($cx, $cy)
                    # 小窓が消えていて別のウインドウをクリックしてしまわないよう、クリック位置を確かめる
                    $rootAt = $W::RootWindowAt($cx, $cy)
                    Check "${Label}: クリックする位置に小窓がある" ($rootAt -eq $Popup) "root=$rootAt popup=$Popup"
                    if ($rootAt -eq $Popup) {
                        $W::LeftClick()
                        Start-Sleep -Milliseconds 200
                        Check "${Label}: 小窓をクリックしてもフォーカスを奪わない" ($W::GetForegroundWindow() -eq $fg) "before=$fg after=$($W::GetForegroundWindow())"
                    }
                }

                Start-Sleep -Milliseconds 300
                Check "${Label}: 押している間は表示されたまま" ($W::IsWindowVisible($Popup))
            }

            Invoke-HotkeyRelease
            $hiddenMs = Wait-Until { -not $W::IsWindowVisible($Popup) } 1000
            Check "${Label}: $hotkeyName を離すと小窓が消える" ($hiddenMs -ge 0)
            if ($hiddenMs -ge 0) { Info "${Label}: 消えるまでの時間 $hiddenMs ms" }
        }

        # 起動中の Copipe を終了 (二重起動にならないように)
        & $stopScript -ExePath $exe

        # 途中で止められても戻せるよう、退避の前に「まだ戻していない」印を書く。
        # 印には、各ファイルが検証の前にあったかを書いておく (tools\Restore-CopipeData.ps1 が読む)
        $pendingMarker = Join-Path $tempDir 'restore-pending.txt'
        $markerLines = foreach ($pair in @(@('settings', [Copipe.Services.Settings]::DefaultPath), @('history', [Copipe.Services.ClipboardHistory]::DefaultPath), @('phrases', [Copipe.Services.PhraseBook]::DefaultPath))) {
            '{0}={1}' -f $pair[0], $(if (Test-Path -LiteralPath $pair[1]) { 'present' } else { 'absent' })
        }
        Set-Content -LiteralPath $pendingMarker -Value $markerLines -Encoding ASCII

        # 設定ファイルと履歴を書き換えるので、利用者のものを退避しておく
        $settingsPath = [Copipe.Services.Settings]::DefaultPath
        $settingsBackup = $null
        if (Test-Path -LiteralPath $settingsPath) {
            $settingsBackup = Join-Path $tempDir 'settings-backup.ini'
            Copy-Item -LiteralPath $settingsPath -Destination $settingsBackup
        }
        # 起動した Copipe は、検証でコピーした目印を履歴に貯める。利用者の履歴に混ざらないよう、
        # 退避してから空の履歴で始め、最後に元へ戻す
        $historyPath = [Copipe.Services.ClipboardHistory]::DefaultPath
        $historyBackup = $null
        if (Test-Path -LiteralPath $historyPath) {
            $historyBackup = Join-Path $tempDir 'history-backup.json'
            Copy-Item -LiteralPath $historyPath -Destination $historyBackup
            Remove-Item -LiteralPath $historyPath -Force
        }
        # 定型文も、検証用の中身に差し替えるので退避しておく
        $phrasesPath = [Copipe.Services.PhraseBook]::DefaultPath
        $phrasesBackup = $null
        if (Test-Path -LiteralPath $phrasesPath) {
            $phrasesBackup = Join-Path $tempDir 'phrases-backup.json'
            Copy-Item -LiteralPath $phrasesPath -Destination $phrasesBackup
            Remove-Item -LiteralPath $phrasesPath -Force
        }

        $savedCursor = $W::GetCursor()
        # CapsLock をホットキーにする E2E で切り替えるので、最後に元のオン・オフへ戻す
        $savedCapsLock = $W::IsToggled(0x14)
        # 他のアプリ (Orca など) が途中で前面を取ると、擬似入力が検証用の窓に届かず失敗する (実測)。
        # デスクトップだけが見える状態にしてから始め、最後に元へ戻す
        $shell = New-Object -ComObject Shell.Application
        $shell.MinimizeAll()
        Start-Sleep -Milliseconds 800
        $app = $null
        $second = $null
        $helper = $null
        try {
            $K = [System.Windows.Forms.Keys]
            # 既定の CapsLock はフックで受け取る特別なキーなので、ふだんの検証は Pause で行う (CapsLock は後の節で確かめる)
            $mainHotkey = Find-UsableHotkey @($K::Pause)
            Check '(準備) 登録できて擬似入力も届くホットキーが見つかる' ($null -ne $mainHotkey)
            if ($null -eq $mainHotkey) { return }
            if ($runHistory) {   # ---- ここから履歴の E2E (起動・表示位置・二重起動・設定したホットキー) ----
            # ---- 初回起動: 設定ファイルが無ければ、最初に設定画面を出す (マウス・キーは使わない) ----
            # Copipe の設定画面 (タイトル「設定」) を探す
            function Find-SettingsDialog([int]$ProcessId) {
                foreach ($h in $W::TopWindows($ProcessId)) {
                    if ($W::IsWindowVisible($h) -and $W::GetText($h) -ceq '設定') { return $h }
                }
                return [IntPtr]::Zero
            }
            # 小窓に WM_CLOSE を送って終わらせ、終了コードを返す (終わらなければ $null)
            function Close-Copipe($Process) {
                [void]$W::PostMessage((Find-Popup $Process.Id), 0x0010 <# WM_CLOSE #>, [IntPtr]::Zero, [IntPtr]::Zero)
                if ($Process.WaitForExit(5000)) { return $Process.ExitCode }
                $Process.Kill()
                return $null
            }
            # 初回起動は既定のホットキーを登録する (キーは押さない)。他のアプリが登録していたら省略する
            if (-not $W::CanRegisterHotkey($owner, [uint32]0, [uint32]([int][Copipe.Services.Settings]::DefaultHotkey -band 0xFFFF))) {
                Info "初回起動の検証は省略 (既定のホットキー $([Copipe.UI.HotkeyText]::Display([Copipe.Services.Settings]::DefaultHotkey)) を他のアプリが登録しているため)"
            } else {
                [System.IO.File]::Delete($settingsPath)
                $firstRun = Start-Process -FilePath $exe -PassThru
                $null = $firstRun.Handle
                $firstDialog = [IntPtr]::Zero
                $limit = (Get-Date).AddSeconds(5)
                while ($firstDialog -eq [IntPtr]::Zero -and (Get-Date) -lt $limit -and -not $firstRun.HasExited) {
                    Start-Sleep -Milliseconds 100
                    $firstDialog = Find-SettingsDialog $firstRun.Id
                }
                Check '初回起動: 設定ファイルが無ければ、最初に設定画面が出る' ($firstDialog -ne [IntPtr]::Zero) "exited=$($firstRun.HasExited)"
                if ($firstDialog -ne [IntPtr]::Zero) {
                    # 取り消して閉じる
                    [void]$W::PostMessage($firstDialog, 0x0010 <# WM_CLOSE #>, [IntPtr]::Zero, [IntPtr]::Zero)
                    $created = Wait-Until { [System.IO.File]::Exists($settingsPath) } 3000
                    Check '初回起動: 設定画面を取り消しても、既定の設定で設定ファイルができる (次からは出ない)' ($created -ge 0 -and [Copipe.Services.Settings]::Load($settingsPath).Hotkey -eq [Copipe.Services.Settings]::DefaultHotkey)
                }
                $firstExit = Close-Copipe $firstRun
                Check '初回起動: 終了すると正常に終わる (終了コード 0。エラーのダイアログを出さない)' ($firstExit -eq 0) "exit=$firstExit"

                $nextRun = Start-Process -FilePath $exe -PassThru
                $null = $nextRun.Handle
                $limit = (Get-Date).AddSeconds(5)
                while ((Find-Popup $nextRun.Id) -eq [IntPtr]::Zero -and (Get-Date) -lt $limit -and -not $nextRun.HasExited) { Start-Sleep -Milliseconds 50 }
                Start-Sleep -Milliseconds 1500
                Check '初回起動: 2 回目からは設定画面を出さない' ((Find-SettingsDialog $nextRun.Id) -eq [IntPtr]::Zero)
                $nextExit = Close-Copipe $nextRun
                Check '2 回目の起動: 終了すると正常に終わる (終了コード 0)' ($nextExit -eq 0) "exit=$nextExit"
            }

            Use-Hotkey $mainHotkey
            $app = Start-Process -FilePath $exe -PassThru
            $null = $app.Handle   # 終了コードを後で読むため (Windows PowerShell の Process の癖)

            $popup = [IntPtr]::Zero
            $deadline = (Get-Date).AddSeconds(5)
            while ($popup -eq [IntPtr]::Zero -and (Get-Date) -lt $deadline -and -not $app.HasExited) {
                Start-Sleep -Milliseconds 50
                $popup = Find-Popup $app.Id
            }
            Check '起動: 小窓 (タイトル Copipe) が作られている' ($popup -ne [IntPtr]::Zero) "exited=$($app.HasExited)"
            if ($popup -eq [IntPtr]::Zero) { return }

            Check '起動: 小窓は最初は表示されていない' (-not $W::IsWindowVisible($popup))
            $ex = $W::GetWindowLong($popup, -20)
            Check '小窓: フォーカスを奪わない (WS_EX_NOACTIVATE)' (($ex -band 0x08000000) -ne 0) ('exstyle=0x{0:X8}' -f $ex)
            Check '小窓: タスクバーと Alt+Tab に出ない (WS_EX_TOOLWINDOW)' (($ex -band 0x80) -ne 0) ('exstyle=0x{0:X8}' -f $ex)
            Check '小窓: 最前面 (WS_EX_TOPMOST)' (($ex -band 0x8) -ne 0) ('exstyle=0x{0:X8}' -f $ex)

            Start-Sleep -Milliseconds 700
            $alive = -not $app.HasExited
            $noDialog = ($alive -and (Find-Dialog $app.Id) -eq [IntPtr]::Zero)
            Check '起動: 1 秒後も動いていて、エラーのダイアログも出ていない (ホットキーの登録に成功)' ($alive -and $noDialog)
            # ホットキーを他のアプリに送ってしまわないよう、起動に失敗していたらここで打ち切る
            if (-not ($alive -and $noDialog)) { return }

            $primary = [System.Windows.Forms.Screen]::PrimaryScreen.WorkingArea
            $center = Pt ($primary.Left + [int]($primary.Width / 2)) ($primary.Top + [int]($primary.Height / 2))

            $sample = $null
            if ($ScreenshotPath) {
                $sample = "Copipe 見た目の確認 & 表示テスト`r`n" +
                          "列A`t列B`t列C`r`n" +
                          "東京都千代田区`t03-1234-5678`t" + [char]::ConvertFromUtf32(0x1F600) + "`r`n" +
                          "https://example.com/a/very/long/path/that/does/not/contain/any/spaces/and/keeps/going/on/and/on/index.html`r`n" +
                          "吾輩は猫である。名前はまだ無い。どこで生れたかとんと見当がつかぬ。何でも薄暗いじめじめした所でニャーニャー泣いていた事だけは記憶している。"
            }
            Test-Hold $center '主モニター中央' $popup -Text $sample -Screenshot $ScreenshotPath -ClickPopup
            Test-Hold (Pt ($primary.Right - 1) ($primary.Bottom - 1)) '主モニター右下隅' $popup
            foreach ($s in [System.Windows.Forms.Screen]::AllScreens) {
                if ($s.Primary) { continue }
                $wa = $s.WorkingArea
                Test-Hold (Pt ($wa.Right - 1) ($wa.Bottom - 1)) ("サブモニター右下隅 " + $wa) $popup
                Test-Hold (Pt ($wa.Left + 2) ($wa.Top + 2)) ("サブモニター左上隅 " + $wa) $popup
            }

            [void]$W::SetCursorPos($center.X, $center.Y)
            $ok = 0
            for ($i = 0; $i -lt 5; $i++) {
                Invoke-HotkeyPress
                $s1 = Wait-Until { $W::IsWindowVisible($popup) } 1000
                Invoke-HotkeyRelease
                $s2 = Wait-Until { -not $W::IsWindowVisible($popup) } 1000
                if ($s1 -ge 0 -and $s2 -ge 0) { $ok++ }
            }
            Check '押して離すを 5 回繰り返しても、毎回 表示 → 非表示 になる' ($ok -eq 5) "ok=$ok"

            Invoke-HotkeyPress
            Invoke-HotkeyRelease
            Start-Sleep -Milliseconds 400
            Check '一瞬だけ押した場合も、小窓が出たまま残らない' (-not $W::IsWindowVisible($popup))

            # コピー直後に他のアプリがクリップボードを開いたままにして、変化の通知では読めなかった場合。
            # 別プロセスが「文字を置く → すぐ開き直して持ち続ける」ことで、CopyQ などの割り込みを再現する
            $marker = 'Copipe再試行検証 ' + [Guid]::NewGuid().ToString('N')
            $helper = Start-ClipboardHelper -Mode settext-hold -HoldMs 1200 -Text $marker
            Check '(準備) 別プロセスが文字を置いて、クリップボードを開いたままにした' ($null -ne $helper)
            if ($helper) {
                Invoke-HotkeyPress
                $shownMs = Wait-Until { $W::IsWindowVisible($popup) } 1000
                Check '読めない状態で押す: 小窓は表示される (今ある履歴を出す)' ($shownMs -ge 0)
                $first = Get-PopupItems $popup
                if ($first.Count -ge 1 -and $first[0] -ceq $marker) {
                    # Copipe が先に読めてしまい、競合を再現できなかった (失敗ではない)
                    Info '読めない状態を再現できなかったので、押している間の読み直しの検査は省略'
                } else {
                    $updatedMs = Wait-Until { $cur = Get-PopupItems $popup; $cur.Count -ge 1 -and $cur[0] -ceq $marker } 5000
                    Check '押している間に読めるようになったら、一覧の先頭に加わる' ($updatedMs -ge 0) ("items=" + (Format-Items (Get-PopupItems $popup)))
                    if ($updatedMs -ge 0) { Info "読めるようになってから一覧に加わるまで (押してからの時間): $updatedMs ms" }
                }
                Invoke-HotkeyRelease
                Check '読み直しの後も、離すと小窓が消える' ((Wait-Until { -not $W::IsWindowVisible($popup) } 1000) -ge 0)
                Stop-ClipboardHelper $helper
                $helper = $null
            }

            # 二重起動
            $second = Start-Process -FilePath $exe -PassThru
            $null = $second.Handle
            $dialog = [IntPtr]::Zero
            $deadline = (Get-Date).AddSeconds(5)
            while ($dialog -eq [IntPtr]::Zero -and (Get-Date) -lt $deadline -and -not $second.HasExited) {
                Start-Sleep -Milliseconds 50
                $dialog = Find-Dialog $second.Id
            }
            Check '二重起動: 2 つ目はダイアログを出す' ($dialog -ne [IntPtr]::Zero) "exited=$($second.HasExited)"
            if ($dialog -ne [IntPtr]::Zero) {
                $dialogText = (@($W::Children($dialog) | ForEach-Object { $W::GetText($_) }) -join ' ')
                Check '二重起動: ダイアログは「すでに起動しています」で、ホットキーの登録の失敗ではない' `
                    (($dialogText -like '*すでに起動しています*' -or $dialogText -like '*already running*') -and $dialogText -notlike "*$hotkeyName*") $dialogText
                [void]$W::PostMessage($dialog, 0x0010 <# WM_CLOSE #>, [IntPtr]::Zero, [IntPtr]::Zero)
            }
            Check '二重起動: 2 つ目は終了する' ($second.WaitForExit(5000))
            if ($second.HasExited) { Check '二重起動: 2 つ目の終了コードは 1' ($second.ExitCode -eq 1) "exit=$($second.ExitCode)" }
            Check '二重起動: 1 つ目は動き続けている' (-not $app.HasExited)
            if (-not $app.HasExited) { Test-Hold $center '二重起動の後の 1 つ目' $popup }

            $app.Refresh()
            Info ("常駐中のメモリ: ワーキングセット {0:N1} MB / プライベート {1:N1} MB" -f ($app.WorkingSet64 / 1MB), ($app.PrivateMemorySize64 / 1MB))

            # taskkill (/f なし) と同じく WM_CLOSE で終了を依頼し、後片付けして終わることを確認する
            [void]$W::PostMessage($popup, 0x0010 <# WM_CLOSE #>, [IntPtr]::Zero, [IntPtr]::Zero)
            Check '終了: 小窓に WM_CLOSE を送ると Copipe が終了する' ($app.WaitForExit(5000))
            if ($app.HasExited) { Check '終了: 終了コードは 0' ($app.ExitCode -eq 0) "exit=$($app.ExitCode)" }

            # ---- 設定ファイルで指定した別のホットキーで動くか ----
            # 既定 (CapsLock) とも、ここまで使った Pause とも違うキーを選ぶ。他のアプリが使っているキーや、擬似入力が横取りされるキーは避ける
            # (ホットキーは修飾キーを付けられないので、単独のキーから選ぶ)
            $chosen = Find-UsableHotkey @(@($K::F13, $K::F14, $K::F9) | Where-Object { $_ -ne [Copipe.Services.Settings]::DefaultHotkey -and $_ -ne $mainHotkey })
            Check '(準備) 変更先に使える空きホットキーが見つかる' ($null -ne $chosen)
            if ($null -eq $chosen) { return }
            Use-Hotkey $chosen
            $app = Start-Process -FilePath $exe -PassThru
            $null = $app.Handle
            $popup = [IntPtr]::Zero
            $deadline = (Get-Date).AddSeconds(5)
            while ($popup -eq [IntPtr]::Zero -and (Get-Date) -lt $deadline -and -not $app.HasExited) {
                Start-Sleep -Milliseconds 50
                $popup = Find-Popup $app.Id
            }
            Check '設定したホットキー: 起動して小窓が作られている' ($popup -ne [IntPtr]::Zero) "exited=$($app.HasExited)"
            Start-Sleep -Milliseconds 700
            $alive = -not $app.HasExited
            $noDialog = ($alive -and (Find-Dialog $app.Id) -eq [IntPtr]::Zero)
            Check '設定したホットキー: 登録に成功している (エラーのダイアログが出ていない)' ($alive -and $noDialog)
            if ($popup -ne [IntPtr]::Zero -and $alive -and $noDialog) {
                Test-Hold $center ("設定したホットキー (" + $hotkeyName + ")") $popup
            }
            [void]$W::PostMessage($popup, 0x0010 <# WM_CLOSE #>, [IntPtr]::Zero, [IntPtr]::Zero)
            [void]$app.WaitForExit(5000)

            # 他のアプリがそのキーを使っていると、メッセージに設定したキー名が出て終了する
            if ($W::HoldHotkey($owner, [uint32]0, [uint32]([int]$chosen -band 0xFFFF))) {
                try {
                    $blocked = Start-Process -FilePath $exe -PassThru
                    $null = $blocked.Handle
                    $dialog = [IntPtr]::Zero
                    $deadline = (Get-Date).AddSeconds(5)
                    while ($dialog -eq [IntPtr]::Zero -and (Get-Date) -lt $deadline -and -not $blocked.HasExited) {
                        Start-Sleep -Milliseconds 50
                        $dialog = Find-Dialog $blocked.Id
                    }
                    Check '他のアプリが使っているキー: 登録できなかったことを知らせる' ($dialog -ne [IntPtr]::Zero) "exited=$($blocked.HasExited)"
                    if ($dialog -ne [IntPtr]::Zero) {
                        $dialogText = (@($W::Children($dialog) | ForEach-Object { $W::GetText($_) }) -join ' ')
                        Check "他のアプリが使っているキー: メッセージに設定したキー名 ($hotkeyName) が出る" ($dialogText -like "*$hotkeyName*") $dialogText
                        [void]$W::PostMessage($dialog, 0x0010 <# WM_CLOSE #>, [IntPtr]::Zero, [IntPtr]::Zero)
                    }
                    Check '他のアプリが使っているキー: 終了する (終了コード 1)' ($blocked.WaitForExit(5000) -and $blocked.ExitCode -eq 1)
                    if (-not $blocked.HasExited) { $blocked.Kill() }
                } finally {
                    $W::ReleaseHotkey($owner)
                }
            } else {
                Info "他のアプリが使っているキーの検証は省略 ($hotkeyName を登録できなかったため)"
            }
            }

            # ---- クリップボードの履歴 ----
            # ここまでの検証でコピーした目印が残っているので、空の履歴から始める。
            # Copipe は起動時に今のクリップボードも拾うので、クリップボードも空にしておく
            if (Test-Path -LiteralPath $historyPath) { Remove-Item -LiteralPath $historyPath -Force }
            Invoke-ClipboardOpen { [void][CopipeVerify.Native]::EmptyClipboard() }
            Use-Hotkey $mainHotkey

            # 履歴を確かめるために Copipe を起動し、小窓の一覧を読む
            function Start-Copipe {
                $proc = Start-Process -FilePath $exe -PassThru
                $null = $proc.Handle
                $found = [IntPtr]::Zero
                $limit = (Get-Date).AddSeconds(5)
                while ($found -eq [IntPtr]::Zero -and (Get-Date) -lt $limit -and -not $proc.HasExited) {
                    Start-Sleep -Milliseconds 50
                    $found = Find-Popup $proc.Id
                }
                Start-Sleep -Milliseconds 500
                return @{ Process = $proc; Popup = $found }
            }
            function Read-History([hashtable]$Copipe) {
                Invoke-HotkeyPress
                $shown = Wait-Until { $W::IsWindowVisible($Copipe.Popup) } 1000
                $items = @()
                if ($shown -ge 0) {
                    $items = Get-PopupItems $Copipe.Popup
                } else {
                    # 小窓が出なかった理由の手がかりを残す
                    Info ("小窓が出なかった: Copipe 終了={0} ダイアログ={1} キーが届いた={2}" -f `
                        $Copipe.Process.HasExited, ((Find-Dialog $Copipe.Process.Id) -ne [IntPtr]::Zero), $W::IsKeyDown($script:keyVk))
                }
                Invoke-HotkeyRelease
                [void](Wait-Until { -not $W::IsWindowVisible($Copipe.Popup) } 1000)
                return ,$items
            }
            function Stop-Copipe([hashtable]$Copipe) {
                [void]$W::PostMessage($Copipe.Popup, 0x0010 <# WM_CLOSE #>, [IntPtr]::Zero, [IntPtr]::Zero)
                [void]$Copipe.Process.WaitForExit(5000)
            }

            if ($runHistory) {   # 履歴の E2E
            [void]$W::SetCursorPos($center.X, $center.Y)
            $copipe = Start-Copipe
            $app = $copipe.Process
            $popup = $copipe.Popup
            Check '履歴: 起動できる' ($popup -ne [IntPtr]::Zero) "exited=$($app.HasExited)"
            if ($popup -ne [IntPtr]::Zero) {
                $items = Read-History $copipe
                Check '履歴: 何もコピーしていなければ、空きの枠が 10 個出る' ($items.Count -eq 10 -and @($items | Where-Object { $_ -cne '' }).Count -eq 0) "got=[$($items -join '] [')]"

                foreach ($text in 'あ 1件目', "い 2件目`r`n2行目", 'う 3件目') {
                    Set-ClipboardText ($text)
                    Start-Sleep -Milliseconds 400
                }
                $items = Read-History $copipe
                Check '履歴: コピーした順の逆 (新しい順) に並び、残りは空きの枠' ($items.Count -eq 10 -and $items[3] -ceq '' -and $items[0] -ceq 'う 3件目' -and $items[1] -ceq "い 2件目`r`n2行目" -and $items[2] -ceq 'あ 1件目') "got=[$($items -join '] [')]"

                Set-ClipboardText ('あ 1件目')
                Start-Sleep -Milliseconds 400
                $items = Read-History $copipe
                Check '履歴: 前にコピーした内容をまたコピーすると先頭へ移動する' ($items.Count -eq 10 -and $items[0] -ceq 'あ 1件目' -and $items[1] -ceq 'う 3件目' -and $items[3] -ceq '') "got=[$($items -join '] [')]"

                Stop-Copipe $copipe
                $copipe = Start-Copipe
                $app = $copipe.Process
                $popup = $copipe.Popup
                Check '履歴: 起動し直せる' ($popup -ne [IntPtr]::Zero)
                if ($popup -ne [IntPtr]::Zero) {
                    $items = Read-History $copipe
                    Check '履歴: 終了して起動し直しても残っている' ($items.Count -eq 10 -and $items[0] -ceq 'あ 1件目' -and $items[3] -ceq '') "got=[$($items -join '] [')]"

                    # エクスプローラーでのコピーと同じ形 (CF_HDROP) で、ファイルとフォルダーをまとめてコピーする
                    $copyFile = Join-Path $tempDir 'コピー履歴.txt'
                    $copyDir = Join-Path $tempDir 'コピー履歴フォルダー'
                    Set-Content -LiteralPath $copyFile -Value 'x'
                    New-Item -ItemType Directory -Path $copyDir -Force | Out-Null
                    $drop = New-Object System.Collections.Specialized.StringCollection
                    [void]$drop.Add($copyFile)
                    [void]$drop.Add($copyDir)
                    Invoke-ClipboardWrite { [System.Windows.Forms.Clipboard]::SetFileDropList($drop) }
                    Start-Sleep -Milliseconds 400
                    $items = Read-History $copipe
                    Check '履歴: ファイル・フォルダーのコピーは、1 つずつ名前と印付きで入る (コピーした並びのまま)' ($items[0] -ceq '📄 コピー履歴.txt' -and $items[1] -ceq '📁 コピー履歴フォルダー' -and $items[2] -ceq 'あ 1件目') "got=[$($items -join '] [')]"
                    $saved = [Copipe.Services.ClipboardHistory]::Load($historyPath, 10)
                    $path = $null; $label = $null
                    Check '履歴: ファイルは開く項目 (パス付き) として保存される' ([Copipe.Services.ClipboardHistory]::TryGetLaunchPath($saved.Items[0], [ref]$path, [ref]$label) -and $path -ceq $copyFile) "path=$path"

                    $bmp = New-Object System.Drawing.Bitmap 4, 4
                    Invoke-ClipboardWrite { [System.Windows.Forms.Clipboard]::SetImage($bmp) }
                    $bmp.Dispose()
                    Start-Sleep -Milliseconds 400
                    $items = Read-History $copipe
                    Check '履歴: 画像のコピーは履歴に入れない' ($items[0] -ceq '📄 コピー履歴.txt' -and $items[3] -ceq 'う 3件目' -and $items[5] -ceq '') "got=[$($items -join '] [')]"
                }
            }
            }
            if ($app -and -not $app.HasExited) { Stop-Copipe $copipe }

            # ---- ダブルクリックで、テキストカーソルの位置に入力する ----
            if (Test-Path -LiteralPath $historyPath) { Remove-Item -LiteralPath $historyPath -Force }
            Invoke-ClipboardOpen { [void][CopipeVerify.Native]::EmptyClipboard() }
            Use-Hotkey $mainHotkey
            $copipe = Start-Copipe
            $app = $copipe.Process
            $popup = $copipe.Popup
            Check '入力: 起動できる' ($popup -ne [IntPtr]::Zero)
            if ($popup -ne [IntPtr]::Zero) {
                $insertItems = @('一番古い 1件目', "二番目の項目`r`n改行と`tタブを含む", '一番新しい 3件目')
                foreach ($text in $insertItems) {
                    Set-ClipboardText ($text)
                    Start-Sleep -Milliseconds 400
                }

                # 入力先: ハーネスが出すテキストボックス (貼り付けを処理できるよう、待つ間はメッセージを回す)
                function Wait-Pumping([scriptblock]$Condition, [int]$TimeoutMs) {
                    $sw = [Diagnostics.Stopwatch]::StartNew()
                    while ($sw.ElapsedMilliseconds -lt $TimeoutMs) {
                        [System.Windows.Forms.Application]::DoEvents()
                        if (& $Condition) { return $sw.ElapsedMilliseconds }
                        Start-Sleep -Milliseconds 5
                    }
                    return -1
                }
                $target = New-Object System.Windows.Forms.Form
                $target.Text = 'Copipe 検証の入力先'
                $target.StartPosition = 'Manual'
                $target.TopMost = $true
                $target.Bounds = Rect ($primary.Left + 80) ($primary.Top + 80) 480 260
                $box = New-Object System.Windows.Forms.TextBox
                $box.Multiline = $true
                $box.AcceptsTab = $true
                $box.Dock = 'Fill'
                $target.Controls.Add($box)
                try {
                    $target.Show()
                    [void](Wait-Pumping { $false } 300)
                    # テキストボックスをクリックして前面にし、テキストカーソルを末尾に置く
                    $boxRect = $W::GetRect($box.Handle)
                    [void]$W::SetCursorPos($boxRect.Left + 40, $boxRect.Top + 20)
                    $W::LeftClick()
                    [void](Wait-Pumping { $W::GetForegroundWindow() -eq $target.Handle } 2000)
                    $box.Text = '前:'
                    $box.SelectionStart = $box.Text.Length
                    [void](Wait-Pumping { $false } 100)
                    Check '(準備) 入力先が前面になった' ($W::GetForegroundWindow() -eq $target.Handle)

                    if ($runHistory) {   # 履歴の E2E: ダブルクリックでの入力
                    # 小窓が入力先に重ならないよう、入力先から離れた位置でホットキーを押す
                    [void]$W::SetCursorPos($boxRect.Right + 300, $boxRect.Top + 40)
                    Invoke-HotkeyPress
                    $shownMs = Wait-Until { $W::IsWindowVisible($popup) } 1000
                    Check '入力: ホットキーで小窓が出る' ($shownMs -ge 0)
                    $before = Get-PopupItems $popup
                    Check '入力: 一覧は新しい順' ($before.Count -eq 10 -and $before[3] -ceq '' -and $before[0] -ceq $insertItems[2] -and $before[1] -ceq $insertItems[1]) ("items=" + (Format-Items $before))

                    # 一覧の 2 件目の真ん中をダブルクリックする
                    $listBox = [IntPtr]::Zero
                    foreach ($child in $W::Children($popup)) { if ($W::GetClass($child) -like '*LISTBOX*') { $listBox = $child } }
                    $listRect = $W::GetRect($listBox)
                    $itemHeight = $W::ListItemHeight($listBox)
                    $clickX = $listRect.Left + [int]($listRect.Width / 2)
                    $clickY = $listRect.Top + $itemHeight + [int]($itemHeight / 2)
                    [void]$W::SetCursorPos($clickX, $clickY)
                    Check '(準備) ダブルクリックする位置に小窓がある' ($W::RootWindowAt($clickX, $clickY) -eq $popup)
                    $W::DoubleClick()

                    $expected = '前:' + $insertItems[1]
                    $insertedMs = Wait-Pumping { $box.Text -ceq $expected } 3000
                    Check '入力: ダブルクリックで、テキストカーソルの位置に全文 (改行・タブを含む) が入る' ($insertedMs -ge 0) ("text=[" + $box.Text + "]")
                    if ($insertedMs -ge 0) { Info "ダブルクリックしてから入力されるまで: $insertedMs ms" }
                    Check '入力: 入力しても前面のウインドウは入力先のまま' ($W::GetForegroundWindow() -eq $target.Handle)
                    Check '入力: キーを離すまで小窓は出たまま' ($W::IsWindowVisible($popup))
                    $during = Get-PopupItems $popup
                    Check '入力: 入力しても一覧の並びは変わらない' ((Format-Items $during) -ceq (Format-Items $before)) ("items=" + (Format-Items $during))

                    Invoke-HotkeyRelease
                    Check '入力: キーを離すと小窓が消える' ((Wait-Until { -not $W::IsWindowVisible($popup) } 1000) -ge 0)

                    # 読むときも、履歴ツールが開いている間は少し待ってやり直す
                    Invoke-ClipboardWrite { $script:clipAfterInsert = [System.Windows.Forms.Clipboard]::GetText() }
                    Check '入力: クリップボードには入力した内容が残る' ($script:clipAfterInsert -ceq $insertItems[1]) ("clipboard=[" + $script:clipAfterInsert + "]")

                    $again = Read-History $copipe
                    Check '入力: 入力した項目は履歴の先頭に移動しない' ($again.Count -eq 10 -and $again[3] -ceq '' -and $again[0] -ceq $insertItems[2] -and $again[1] -ceq $insertItems[1]) ("items=" + (Format-Items $again))

                    }
                    # 入力先のテキストを「前:」に戻し、テキストカーソルを末尾に置く
                    function Reset-Target {
                        $box.Text = '前:'
                        $box.SelectionStart = $box.Text.Length
                        [void](Wait-Pumping { $false } 100)
                    }
                    # ホットキーを押したまま、一覧の Index 番目をクリック (Double なら 2 回) して、入力されるのを待つ
                    function Test-ClickInsert([int]$Index, [switch]$Double, [int]$WaitMs = 1500) {
                        [void]$W::SetCursorPos($boxRect.Right + 300, $boxRect.Top + 40)
                        Invoke-HotkeyPress
                        [void](Wait-Until { $W::IsWindowVisible($popup) } 1000)
                        $pt = Get-ItemCenter $popup $Index
                        [void]$W::SetCursorPos($pt.X, $pt.Y)
                        if ($Double) { $W::DoubleClick() } else { $W::LeftClick() }
                        # 入力されない (はずの) 場合も確かめるため、決まった時間だけ待ってから結果を読む
                        [void](Wait-Pumping { $false } $WaitMs)
                        $result = $box.Text
                        Invoke-HotkeyRelease
                        [void](Wait-Until { -not $W::IsWindowVisible($popup) } 1000)
                        return $result
                    }

                    if ($runHistory) {   # 履歴の E2E: ダブル / シングルクリックの切り替え
                    # ダブルクリックの設定 (既定) では、シングルクリックでは入力しない
                    Reset-Target
                    $text = Test-ClickInsert 0
                    Check '貼り付けの操作 (ダブルクリック): シングルクリックでは入力しない' ($text -ceq '前:') ("text=[" + $text + "]")

                    # シングルクリックの設定に切り替えて起動し直す (履歴はファイルから読み戻される)
                    Stop-Copipe $copipe
                    Use-Hotkey $mainHotkey -InsertClick Single
                    $copipe = Start-Copipe
                    $app = $copipe.Process
                    $popup = $copipe.Popup
                    Check '貼り付けの操作 (シングルクリック): 起動できる' ($popup -ne [IntPtr]::Zero)
                    if ($popup -ne [IntPtr]::Zero) {
                        # クリップボードには前の Copipe が貼り付けに使った内容が残っているが、
                        # 起動し直しても、それが履歴の先頭に戻ってこないこと
                        $restarted = Read-History $copipe
                        Check '入力: 起動し直しても、貼り付けに使った項目は先頭に戻らない' ($restarted.Count -eq 10 -and $restarted[3] -ceq '' -and $restarted[0] -ceq $insertItems[2] -and $restarted[1] -ceq $insertItems[1]) ("items=" + (Format-Items $restarted))

                        # 起動し直したので、入力先を前面に戻す
                        [void]$W::SetCursorPos($boxRect.Left + 40, $boxRect.Top + 20)
                        $W::LeftClick()
                        [void](Wait-Pumping { $W::GetForegroundWindow() -eq $target.Handle } 2000)

                        Reset-Target
                        $text = Test-ClickInsert 0
                        Check '貼り付けの操作 (シングルクリック): クリック 1 回で入力される' ($text -ceq ('前:' + $insertItems[2])) ("text=[" + $text + "]")

                        Reset-Target
                        $text = Test-ClickInsert 2 -Double
                        Check '貼り付けの操作 (シングルクリック): うっかりダブルクリックしても入力は 1 回だけ' ($text -ceq ('前:' + $insertItems[0])) ("text=[" + $text + "]")
                    }

                    }
                    # ---- 数字キーで選んで入力する (1〜9、0。10 件目が 0) ----
                    Stop-Copipe $copipe
                    if (Test-Path -LiteralPath $historyPath) { Remove-Item -LiteralPath $historyPath -Force }
                    Invoke-ClipboardOpen { [void][CopipeVerify.Native]::EmptyClipboard() }
                    Use-Hotkey $mainHotkey
                    $copipe = Start-Copipe
                    $app = $copipe.Process
                    $popup = $copipe.Popup
                    Check '数字キー: 起動できる' ($popup -ne [IntPtr]::Zero)
                    if ($popup -ne [IntPtr]::Zero) {
                        # 11 件コピーする。履歴は 10 件までなので「数字キー検証 01」は消え、一覧の 1 件目は「数字キー検証 11」、10 件目は「数字キー検証 02」
                        for ($n = 1; $n -le 11; $n++) {
                            Set-ClipboardText (('数字キー検証 {0:D2}' -f $n))
                            Start-Sleep -Milliseconds 400
                        }
                        [void]$W::SetCursorPos($boxRect.Left + 40, $boxRect.Top + 20)
                        $W::LeftClick()
                        [void](Wait-Pumping { $W::GetForegroundWindow() -eq $target.Handle } 2000)
                        Reset-Target
                        if ($runHistory) {   # 履歴の E2E: 数字キー

                        [void]$W::SetCursorPos($boxRect.Right + 300, $boxRect.Top + 40)
                        Invoke-HotkeyPress
                        [void](Wait-Until { $W::IsWindowVisible($popup) } 1000)
                        # 上段の 2、テンキーの 3、上段の 0 を、間を空けずに続けて押す
                        foreach ($vk in 0x32, 0x63, 0x30) {
                            $W::KeyDown([byte]$vk)
                            $W::KeyUp([byte]$vk)
                        }
                        $expected = '前:数字キー検証 10数字キー検証 09数字キー検証 02'
                        $typedMs = Wait-Pumping { $box.Text -ceq $expected } 4000
                        Check '数字キー: 2・テンキーの 3・0 を続けて押すと、2 件目・3 件目・10 件目が順に入力される' ($typedMs -ge 0) ("text=[" + $box.Text + "]")
                        if ($typedMs -ge 0) { Info "数字キー 3 つを押してから全部入力されるまで: $typedMs ms" }
                        Check '数字キー: 入力しても、キーを離すまで小窓は出たまま' ($W::IsWindowVisible($popup))
                        Invoke-HotkeyRelease
                        [void](Wait-Until { -not $W::IsWindowVisible($popup) } 1000)
                        [void](Wait-Pumping { $false } 300)
                        Check '数字キー: 押した数字そのものは入力先に入らない' ($box.Text -ceq $expected) ("text=[" + $box.Text + "]")

                        # 小窓を消した後は、数字キーを横取りしない (解除漏れが無い)
                        Reset-Target
                        $W::KeyDown([byte]0x37)
                        $W::KeyUp([byte]0x37)
                        $W::KeyDown([byte]0x65)   # テンキーの 5
                        $W::KeyUp([byte]0x65)
                        $freeMs = Wait-Pumping { $box.Text -ceq '前:75' } 2000
                        Check '数字キー: 小窓を消した後は、数字キー (上段・テンキー) が普通に入力できる' ($freeMs -ge 0) ("text=[" + $box.Text + "]")

                        }
                        # ---- 矢印キーと Enter は横取りしない。数字キーで選んだ項目は強調表示する ----
                        function Get-Selected {
                            foreach ($child in $W::Children($popup)) {
                                if ($W::GetClass($child) -like '*LISTBOX*') { return $W::ListSelectedIndex($child) }
                            }
                            return -3
                        }
                        function Send-Key([byte]$Vk, [int]$Times = 1) {
                            for ($i = 0; $i -lt $Times; $i++) { $W::KeyDown($Vk); $W::KeyUp($Vk) }
                            [void](Wait-Pumping { $false } 150)
                        }
                        # 見えている Copipe の右クリックメニューを探す。
                        # UI オートメーションからは項目が見えないので、MSAA で読む (実測)
                        function Find-RowMenu {
                            foreach ($h in $W::TopWindows($app.Id)) {
                                if ($h -eq $popup -or -not $W::IsWindowVisible($h) -or $W::GetClass($h) -eq 'SysShadow') { continue }
                                if ($W::MenuItems($h).Count -gt 0) { return $h }
                            }
                            return $null
                        }
                        function Get-MenuNames($Menu) { return (@($W::MenuItems($Menu) | ForEach-Object { if ($_.Key) { $_.Key } else { '-' } }) -join ' | ') }   # 区切り線は名前が無いので「-」
                        # ホットキーを押したまま Index 番目の行を右クリックし、出たメニューを返す。-Phrases なら定型文モードにしてから
                        function Open-RowMenu([int]$Index, [switch]$Phrases) {
                            [void]$W::SetCursorPos($boxRect.Right + 300, $boxRect.Top + 40)
                            $fgAtPress = $W::GetForegroundWindow()
                            Invoke-HotkeyPress
                            $shownMs = Wait-Until { $W::IsWindowVisible($popup) } 1000
                            if ($Phrases) { Send-Key 0x09 1 }
                            $pt = Get-ItemCenter $popup $Index
                            [void]$W::SetCursorPos($pt.X, $pt.Y)
                            $W::RightClick()
                            [void](Wait-Pumping { $null -ne (Find-RowMenu) } 1500)
                            $found = Find-RowMenu
                            if ($null -eq $found) {
                                # 見つからなかった理由の手がかり: Copipe の見えているウインドウの一覧
                                $list = foreach ($h in $W::TopWindows($app.Id)) {
                                    if ($W::IsWindowVisible($h)) {
                                        $ctl = $W::MenuItems($h).Count
                                        "{0} [{1}] {2} {3}" -f $W::GetClass($h), $W::GetText($h), $ctl, $W::GetRect($h)
                                    }
                                }
                                $fg = $W::GetForegroundWindow()
                                # 応答・ホットキー押下・登録済み: ホットキーが Copipe に届かなかったのか、Copipe が止まっていたのかの手がかり
                                Info ("メニューが見つからない。見えているウインドウ: " + ($list -join ' / ') +
                                      " | Copipe 終了=" + $app.HasExited + " 小窓=" + $W::IsWindowVisible($popup) +
                                      " 前面=" + $W::GetClass($fg) + " [" + $W::GetText($fg) + "]" +
                                      " 応答=" + $W::Responds($popup, 500) + " ホットキー押下=" + $W::IsKeyDown($script:keyVk) +
                                      " ホットキー登録済み=" + (-not $W::CanRegisterHotkey($owner, [uint32]0, [uint32]$script:keyVk)) +
                                      " 押した時の前面=" + $W::GetClass($fgAtPress) + " [" + $W::GetText($fgAtPress) + "] 小窓が出るまで=$shownMs ms")
                            }
                            return $found
                        }
                        function Invoke-MenuItem($Menu, [string]$Name) {
                            foreach ($item in $W::MenuItems($Menu)) {
                                if ($item.Key -ceq $Name) {
                                    $r = $item.Value
                                    [void]$W::SetCursorPos([int]($r.X + $r.Width / 2), [int]($r.Y + $r.Height / 2))
                                    $W::LeftClick()
                                    return $true
                                }
                            }
                            return $false
                        }
                        # 定型文の削除は、メニューを閉じてから確認のダイアログで確かめる。
                        # 「削除」を選び、出てきた確認 (#32770) を返す。ホットキーはその後で離す。
                        # $script:menuClosedAtConfirm: 確認が出たとき、メニューが閉じていたか
                        function Open-DeleteConfirm([int]$Index) {
                            $menu = Open-RowMenu $Index -Phrases
                            if ($null -eq $menu -or -not (Invoke-MenuItem $menu '削除')) { Invoke-HotkeyRelease; return [IntPtr]::Zero }
                            [void](Wait-Pumping { (Find-Dialog $app.Id) -ne [IntPtr]::Zero } 3000)
                            $script:menuClosedAtConfirm = $null -eq (Find-RowMenu)
                            Invoke-HotkeyRelease
                            return (Find-Dialog $app.Id)
                        }
                        # 確認のボタン (6 = はい、7 = いいえ) を押して閉じる。はいのときは保存を待つ。
                        # 閉じた後に元のアプリが前面に戻ったかを返す
                        function Close-Confirm([IntPtr]$Confirm, [int]$Button) {
                            if ($Confirm -eq [IntPtr]::Zero) { return $false }
                            $writtenBefore = (Get-PhrasesWritten)
                            [void]$W::PostMessage($Confirm, 0x0111 <# WM_COMMAND #>, [IntPtr]$Button, [IntPtr]::Zero)
                            [void](Wait-Pumping { -not $W::IsWindow($Confirm) } 2000)
                            if ($Button -eq 6) {
                                [void](Wait-Pumping { (Get-PhrasesWritten) -ne $writtenBefore } 2000)
                            }
                            [void](Wait-Pumping { $W::GetForegroundWindow() -eq $target.Handle } 2000)
                            $fg = $W::GetForegroundWindow()
                            $script:confirmForeground = $W::GetClass($fg) + " [" + $W::GetText($fg) + "]"
                            return ($fg -eq $target.Handle)
                        }
                        if ($runHistory) {   # 履歴の E2E: 矢印キー・Enter・強調表示
                        # 2 行にして 2 行目の末尾から ↑ を押す (入力先に届いていれば、カーソルが 1 行目へ動いてわかる)
                        $box.Text = "前:`r`n後:"
                        $box.SelectionStart = $box.Text.Length
                        [void](Wait-Pumping { $false } 100)
                        $caretBefore = $box.SelectionStart
                        [void]$W::SetCursorPos($boxRect.Right + 300, $boxRect.Top + 40)
                        Invoke-HotkeyPress
                        [void](Wait-Until { $W::IsWindowVisible($popup) } 1000)
                        Check '強調表示: 小窓を出した時点で、どの項目も選ばれていない' ((Get-Selected) -eq -1) "selected=$(Get-Selected)"
                        Send-Key 0x26 1   # ↑
                        Check '矢印キー: 何も選んでいないときの ↑ は、最後の項目を選ぶ' ((Get-Selected) -eq 9) "selected=$(Get-Selected)"
                        Check '矢印キー: ↑ は入力先に届かない (カーソルが動かない)' ($box.SelectionStart -eq $caretBefore) ("caret=" + $box.SelectionStart)
                        Send-Key 0x28 1   # ↓
                        Check '矢印キー: 最後の項目で ↓ を押すと、最初の項目に戻る' ((Get-Selected) -eq 0) "selected=$(Get-Selected)"
                        Send-Key 0x28 1
                        Check '矢印キー: ↓ で次の項目を選ぶ' ((Get-Selected) -eq 1) "selected=$(Get-Selected)"
                        Send-Key 0x0D 1   # Enter
                        $enterMs = Wait-Pumping { $box.Text -ceq "前:`r`n後:数字キー検証 10" } 3000
                        Check 'Enter: 選んでいる項目を入力する (入力先の改行にはならない)' ($enterMs -ge 0) ("text=[" + $box.Text + "]")
                        Send-Key 0x33 1   # 3
                        $numMs = Wait-Pumping { $box.Text -ceq "前:`r`n後:数字キー検証 10数字キー検証 09" } 3000
                        Check '数字キー: 選んでいる項目があっても、数字キーの項目が入力される' ($numMs -ge 0) ("text=[" + $box.Text + "]")
                        Check '強調表示: 数字キーで入力した項目 (3 件目) が選ばれた状態 (強調表示) になる' ((Get-Selected) -eq 2) "selected=$(Get-Selected)"
                        Invoke-HotkeyRelease
                        [void](Wait-Until { -not $W::IsWindowVisible($popup) } 1000)

                        }
                        # 小窓の上の見出し (Label) の文字を読む
                        function Get-PopupLabels {
                            $texts = @()
                            foreach ($child in $W::Children($popup)) {
                                if ($W::GetClass($child) -like '*STATIC*') { $texts += $W::GetText($child) }
                            }
                            return ($texts -join ' | ')
                        }
                        # 見出しの左 (今のモード) がちょうどその文字か。右の案内 (Tab: …) と区別するため、完全一致で見る
                        function Test-Title([string]$Labels, [string]$Title) {
                            return (($Labels -split ' \| ') -contains $Title)
                        }
                        # 一覧の Index 行目の、上から Frac の高さの位置
                        function Get-RowPoint([int]$Index, [double]$Frac = 0.5) {
                            foreach ($child in $W::Children($popup)) {
                                if ($W::GetClass($child) -like '*LISTBOX*') {
                                    $r = $W::GetRect($child)
                                    $h = $W::ListItemHeight($child)
                                    return (Pt ($r.Left + [int]($r.Width / 2)) ($r.Top + [int]($h * ($Index + $Frac))))
                                }
                            }
                            return $null
                        }
                        # 左ボタンを押したまま From から To へ少しずつ動かす。HoldMs だけ To で止めてから離す
                        function Invoke-Drag($From, $To, [int]$HoldMs = 0, [switch]$NoRelease) {
                            $W::MoveMouse($From.X, $From.Y)
                            [void](Wait-Pumping { $false } 50)
                            $W::LeftDown()
                            [void](Wait-Pumping { $false } 50)
                            for ($i = 1; $i -le 8; $i++) {
                                $W::MoveMouse([int]($From.X + ($To.X - $From.X) * $i / 8), [int]($From.Y + ($To.Y - $From.Y) * $i / 8))
                                [void](Wait-Pumping { $false } 25)
                            }
                            if ($HoldMs -gt 0) { [void](Wait-Pumping { $false } $HoldMs) }
                            if (-not $NoRelease) {
                                $W::LeftUp()
                                [void](Wait-Pumping { $false } 300)
                            }
                        }
                        # 他のアプリ役のドロップ先。落とされたものを $script:dropped に「TEXT:…」か「FILE:…」で残す。
                        # 入力先の下に置く (小窓は入力先の右に出るので重ならない)
                        $dropTarget = New-Object System.Windows.Forms.Form
                        $dropTarget.Text = 'Copipe 検証のドロップ先'
                        $dropTarget.StartPosition = 'Manual'
                        $dropTarget.TopMost = $true
                        $dropTarget.ShowInTaskbar = $false
                        $dropTarget.Bounds = Rect ($primary.Left + 80) ($primary.Top + 360) 480 160
                        $dropTarget.AllowDrop = $true
                        $onDropOver = { $_.Effect = $_.AllowedEffect -band [System.Windows.Forms.DragDropEffects]::Copy }
                        $dropTarget.add_DragEnter($onDropOver)
                        $dropTarget.add_DragOver($onDropOver)
                        $dropTarget.add_DragDrop({
                            # 別のプロセスから落とされたデータは、PowerShell からは COM オブジェクトに見えて
                            # GetData を呼べない (実測)。DataObject で包んでから読む
                            $data = New-Object System.Windows.Forms.DataObject -ArgumentList (, $_.Data)
                            $files = $data.GetData([System.Windows.Forms.DataFormats]::FileDrop)
                            $script:droppedEffects = $_.AllowedEffect
                            # ANSI のテキスト (CF_TEXT) も入っているか (変換で作られたものは数えない)
                            $script:droppedAnsi = $data.GetDataPresent([System.Windows.Forms.DataFormats]::Text, $false)
                            $script:dropped = if ($files) { 'FILE:' + ($files -join '|') } else { 'TEXT:' + $data.GetData([System.Windows.Forms.DataFormats]::UnicodeText) }
                        })
                        # ドロップ先を出す。出すと前面になるので、入力先をクリックして前面に戻す (小窓を出す前に呼ぶ)
                        function Show-DropTarget {
                            $dropTarget.Show()
                            [void]$W::SetCursorPos($boxRect.Left + 40, $boxRect.Top + 20)
                            $W::LeftClick()
                            [void](Wait-Pumping { $W::GetForegroundWindow() -eq $target.Handle } 2000)
                        }
                        # 一覧の Index 行目を、小窓の外のドロップ先までドラッグして落とす。落とされたもの (無ければ $null) を返す
                        # 何も落ちないはずのとき (グループ) は、-TimeoutMs を短くして待ち時間を減らす
                        function Invoke-DragOut([int]$Index, [int]$TimeoutMs = 1500) {
                            $script:dropped = $null
                            $script:droppedEffects = $null
                            $script:droppedAnsi = $null
                            $to = Pt ($dropTarget.Left + 240) ($dropTarget.Top + 80)
                            Invoke-Drag (Get-RowPoint $Index) $to -NoRelease
                            $W::MoveMouse($to.X, $to.Y + 1)
                            [void](Wait-Pumping { $false } 200)
                            $script:dropUnder = ($W::RootWindowAt($to.X, $to.Y) -eq $dropTarget.Handle)
                            $W::LeftUp()
                            [void](Wait-Pumping { $null -ne $script:dropped } $TimeoutMs)
                            return $script:dropped
                        }
                        # 定型文の読み書きの確認は、履歴の E2E (履歴→定型文のドラッグ) でも使うので、定型文の E2E の外で定義する
                        # (-E2E History だけで流すと、中で定義した関数が無くて止まっていた)
                        function Read-Phrases { [void](Get-PhrasesWritten); return [Copipe.Services.PhraseBook]::Load($phrasesPath).Root }
                        # phrases.json を書き換えた時刻。Copipe が保存の途中 (一時ファイルとの置き換え中) で一瞬無いときは、少し待って読み直す
                        function Get-PhrasesWritten {
                            for ($i = 0; $i -lt 20; $i++) {
                                if ([System.IO.File]::Exists($phrasesPath)) { return [System.IO.File]::GetLastWriteTimeUtc($phrasesPath) }
                                Start-Sleep -Milliseconds 10
                            }
                            return [DateTime]::MinValue
                        }
                        if ($runPhrases) {   # ---- ここから定型文の E2E ----
                        # ---- モードキー (Tab) で、履歴と定型文を切り替える ----
                        # 入力先で Tab を受け付ける (Tab が漏れたら文字として入り、分かるように)
                        $box.AcceptsTab = $true
                        Reset-Target
                        [void]$W::SetCursorPos($boxRect.Right + 300, $boxRect.Top + 40)
                        Invoke-HotkeyPress
                        [void](Wait-Until { $W::IsWindowVisible($popup) } 1000)
                        $labels = Get-PopupLabels
                        $items = Get-PopupItems $popup
                        Check 'モード: 小窓はクリップボード履歴で開く (見出し)' (Test-Title $labels 'クリップボード履歴') "labels=[$labels]"
                        Check 'モード: 見出しに、モードキー (Tab) で定型文に切り替えられることが出ている' ($labels -like '*Tab*定型文*') "labels=[$labels]"
                        Check 'モード: 履歴の一覧が出ている' ($items.Count -eq 10 -and $items[0] -ceq '数字キー検証 11') ("items=" + (Format-Items $items))

                        Send-Key 0x09 1   # Tab
                        $labels = Get-PopupLabels
                        $items = Get-PopupItems $popup
                        Check 'モード: Tab で定型文モードに切り替わる (見出し)' ((Test-Title $labels '定型文') -and -not (Test-Title $labels 'クリップボード履歴')) "labels=[$labels]"
                        Check 'モード: 定型文モードの見出しに、Tab で履歴に戻れることが出ている' ($labels -like '*Tab*履歴*') "labels=[$labels]"
                        Check 'モード: 定型文が無いときは、空きの枠が 10 個出る' ($items.Count -eq 10 -and @($items | Where-Object { $_ -cne '' }).Count -eq 0) ("items=" + (Format-Items $items))
                        Check 'モード: 切り替えても小窓は出たまま' ($W::IsWindowVisible($popup))
                        Send-Key 0x31 1   # 1
                        [void](Wait-Pumping { $false } 600)
                        Check 'モード: 定型文モードで数字キーを押しても、何も入力しない' ($box.Text -ceq '前:') ("text=[" + $box.Text + "]")

                        Send-Key 0x09 1   # Tab
                        $labels = Get-PopupLabels
                        $items = Get-PopupItems $popup
                        Check 'モード: もう一度 Tab で履歴に戻る' ((Test-Title $labels 'クリップボード履歴') -and $items.Count -eq 10 -and $items[0] -ceq '数字キー検証 11') ("labels=[$labels] items=" + (Format-Items $items))

                        Send-Key 0x09 1   # 定型文にしたまま離す
                        Invoke-HotkeyRelease
                        [void](Wait-Until { -not $W::IsWindowVisible($popup) } 1000)
                        [void](Wait-Pumping { $false } 200)
                        Check 'モード: 押した Tab そのものは入力先に届かない' ($box.Text -ceq '前:') ("text=[" + $box.Text + "]")

                        Invoke-HotkeyPress
                        [void](Wait-Until { $W::IsWindowVisible($popup) } 1000)
                        $labels = Get-PopupLabels
                        Invoke-HotkeyRelease
                        [void](Wait-Until { -not $W::IsWindowVisible($popup) } 1000)
                        Check 'モード: 定型文モードで離しても、次に開くときはクリップボード履歴' (Test-Title $labels 'クリップボード履歴') "labels=[$labels]"

                        # 押し続けたとき (キーリピートで KeyDown が続けて届く) も、閉じたり開き直したりしない (定型文モードのまま)
                        Invoke-HotkeyPress
                        [void](Wait-Until { $W::IsWindowVisible($popup) } 1000)
                        Send-Key 0x09 1   # 定型文
                        $repeatHidden = $false
                        for ($i = 0; $i -lt 10; $i++) {
                            $W::KeyDown($script:keyVk)
                            [void](Wait-Pumping { $false } 40)
                            if (-not $W::IsWindowVisible($popup)) { $repeatHidden = $true }
                        }
                        $labels = Get-PopupLabels
                        Invoke-HotkeyRelease
                        [void](Wait-Until { -not $W::IsWindowVisible($popup) } 1000)
                        Check 'モード: 押し続けて (キーリピート) も、小窓は閉じず定型文モードのまま' (-not $repeatHidden -and (Test-Title $labels '定型文') -and -not (Test-Title $labels 'クリップボード履歴')) "labels=[$labels] 閉じた=$repeatHidden"

                        # マウスのホイール: 小窓の上で ↓ に回すと定型文モード、↑ に回すと履歴モード (端で止まる。回り込まない)
                        function Send-Wheel([int]$Delta) {
                            $W::Wheel($Delta)
                            [void](Wait-Pumping { $false } 200)
                        }
                        [void]$W::SetCursorPos($boxRect.Right + 300, $boxRect.Top + 40)
                        Invoke-HotkeyPress
                        [void](Wait-Until { $W::IsWindowVisible($popup) } 1000)
                        $wheelPt = Get-ItemCenter $popup 0
                        [void]$W::SetCursorPos($wheelPt.X, $wheelPt.Y)
                        [void](Wait-Pumping { $false } 100)
                        Send-Wheel 120
                        Check 'ホイール: 履歴モードで ↑ に回しても履歴のまま (回り込まない)' (Test-Title (Get-PopupLabels) 'クリップボード履歴') "labels=[$(Get-PopupLabels)]"
                        Send-Wheel -120
                        Check 'ホイール: ↓ に回すと定型文モードになる' (Test-Title (Get-PopupLabels) '定型文') "labels=[$(Get-PopupLabels)]"
                        Send-Wheel -120
                        Check 'ホイール: 定型文モードで ↓ に回しても定型文のまま (回り込まない)' (Test-Title (Get-PopupLabels) '定型文') "labels=[$(Get-PopupLabels)]"
                        Send-Wheel 120
                        Check 'ホイール: ↑ に回すと履歴モードに戻る' (Test-Title (Get-PopupLabels) 'クリップボード履歴') "labels=[$(Get-PopupLabels)]"
                        # 高精度ホイールの細かい量は、1 ノッチ分 (120) たまるまで切り替えない
                        Send-Wheel -40
                        $afterSmall = Test-Title (Get-PopupLabels) 'クリップボード履歴'
                        Send-Wheel -40
                        Send-Wheel -40
                        Check 'ホイール: 細かい量は 1 ノッチ分たまってから切り替わる' ($afterSmall -and (Test-Title (Get-PopupLabels) '定型文')) "small=$afterSmall labels=[$(Get-PopupLabels)]"
                        # 見出しの上で回しても切り替わる
                        foreach ($child in $W::Children($popup)) {
                            if ($W::GetClass($child) -like '*STATIC*' -and $W::GetText($child) -like '定型文*') {
                                $hr = $W::GetRect($child)
                                [void]$W::SetCursorPos($hr.Left + 10, $hr.Top + [int]($hr.Height / 2))
                            }
                        }
                        Send-Wheel 120
                        Check 'ホイール: 見出しの上で回しても切り替わる' (Test-Title (Get-PopupLabels) 'クリップボード履歴') "labels=[$(Get-PopupLabels)]"
                        [void]$W::SetCursorPos($boxRect.Right + 300, $boxRect.Top + 40)
                        Invoke-HotkeyRelease
                        [void](Wait-Until { -not $W::IsWindowVisible($popup) } 1000)
                        Check 'ホイール: 入力先には何も入らない' ($box.Text -ceq '前:') ("text=[" + $box.Text + "]")

                        # 小窓を消した後は、Tab を横取りしない
                        Reset-Target
                        Send-Key 0x09 1
                        $tabMs = Wait-Pumping { $box.Text -ceq "前:`t" } 2000
                        Check 'モード: 小窓を消した後は、Tab が普通に入力先に届く' ($tabMs -ge 0) ("text=[" + $box.Text + "] Tab を登録できるか=" + $W::CanRegisterHotkey($owner, [uint32]0, [uint32]9) + " 前面=" + ($W::GetForegroundWindow() -eq $target.Handle) + " フォーカス=" + $box.Focused)

                        # ---- 定型文: 階層の表示と移動 (数字キー・ダブルクリック・Esc)、入力 ----
                        # 起動中に phrases.json を書き換える。定型文モードに入るたびに読み直すこと
                        $phraseJson = '{"Slots":[' +
                            '{"Kind":"Phrase","Text":"定型文検証 一番上"},' +
                            '{"Kind":"Group","Name":"社外","Slots":[null,null,' +
                                '{"Kind":"Phrase","Title":"締め","Text":"定型文検証 締め"},' +
                                '{"Kind":"Group","Name":"挨拶","Slots":[{"Kind":"Phrase","Text":"定型文検証 挨拶\r\n2行目"}]}' +
                            ']}]}'
                        [System.IO.File]::WriteAllText($phrasesPath, $phraseJson, (New-Object System.Text.UTF8Encoding $false))
                        $escVk = [uint32]0x1B
                        Reset-Target
                        [void]$W::SetCursorPos($boxRect.Right + 300, $boxRect.Top + 40)
                        Invoke-HotkeyPress
                        [void](Wait-Until { $W::IsWindowVisible($popup) } 1000)
                        Check '定型文: 履歴モードの間は Esc を横取りしない' ($W::CanRegisterHotkey($owner, [uint32]0, $escVk))
                        Send-Key 0x09 1
                        $labels = Get-PopupLabels
                        $items = Get-PopupItems $popup
                        Check '定型文: 書き換えた phrases.json を読み直して、一番上の階層を出す' ((Test-Title $labels '定型文') -and $items.Count -eq 10 -and $items[0] -ceq '定型文検証 一番上' -and $items[1] -ceq '├ 社外' -and $items[2] -ceq '') ("labels=[$labels] items=" + (Format-Items $items))
                        Check '定型文: 定型文モードの間は Esc を Copipe が受け取る' (-not $W::CanRegisterHotkey($owner, [uint32]0, $escVk))
                        Send-Key 0x1B 1
                        Check '定型文: 一番上で Esc を押しても、一番上のまま (小窓も出たまま)' ((Test-Title (Get-PopupLabels) '定型文') -and $W::IsWindowVisible($popup)) "labels=[$(Get-PopupLabels)]"

                        Send-Key 0x32 1   # 2: グループ「社外」
                        $labels = Get-PopupLabels
                        $items = Get-PopupItems $popup
                        Check '定型文: グループの番号を押すと、その中に入る (見出しに階層が出る)' ((Test-Title $labels '定型文 > 社外') -and $items.Count -eq 10 -and $items[2] -ceq '締め' -and $items[3] -ceq '├ 挨拶' -and $items[0] -ceq '') ("labels=[$labels] items=" + (Format-Items $items))
                        Check '定型文: グループに入っても、入力はしない' ($box.Text -ceq '前:') ("text=[" + $box.Text + "]")
                        Send-Key 0x34 1   # 4: グループ「挨拶」
                        $labels = Get-PopupLabels
                        $items = Get-PopupItems $popup
                        Check '定型文: さらに中のグループにも入れる。表示名が無い定型文は本文の 1 行目を出す' ((Test-Title $labels '定型文 > 社外 > 挨拶') -and $items[0] -ceq '定型文検証 挨拶') ("labels=[$labels] items=" + (Format-Items $items))
                        Send-Key 0x31 1   # 1: 定型文
                        $phraseMs = Wait-Pumping { $box.Text -ceq "前:定型文検証 挨拶`r`n2行目" } 3000
                        Check '定型文: 定型文の番号を押すと、本文の全文 (改行を含む) が入力される' ($phraseMs -ge 0) ("text=[" + $box.Text + "]")
                        Check '定型文: 入力しても小窓は出たまま' ($W::IsWindowVisible($popup))
                        Send-Key 0x1B 1
                        Check '定型文: Esc で 1 つ上の階層に戻る' (Test-Title (Get-PopupLabels) '定型文 > 社外') "labels=[$(Get-PopupLabels)]"
                        Send-Key 0x1B 1
                        Check '定型文: もう一度 Esc で一番上に戻る' (Test-Title (Get-PopupLabels) '定型文') "labels=[$(Get-PopupLabels)]"

                        # クリックは「貼り付けの操作」の設定に従う (この Copipe はダブルクリック)
                        $pt = Get-ItemCenter $popup 1
                        [void]$W::SetCursorPos($pt.X, $pt.Y)
                        $W::LeftClick()
                        [void](Wait-Pumping { $false } 700)
                        Check '定型文: ダブルクリックの設定では、グループをシングルクリックしても入らない' (Test-Title (Get-PopupLabels) '定型文') "labels=[$(Get-PopupLabels)]"
                        $W::DoubleClick()
                        [void](Wait-Pumping { Test-Title (Get-PopupLabels) '定型文 > 社外' } 1000)
                        Check '定型文: グループをダブルクリックすると中に入る' (Test-Title (Get-PopupLabels) '定型文 > 社外') "labels=[$(Get-PopupLabels)]"
                        $pt = Get-ItemCenter $popup 2
                        [void]$W::SetCursorPos($pt.X, $pt.Y)
                        $W::DoubleClick()
                        $clickMs = Wait-Pumping { $box.Text -ceq "前:定型文検証 挨拶`r`n2行目定型文検証 締め" } 3000
                        Check '定型文: 定型文をダブルクリックすると入力される' ($clickMs -ge 0) ("text=[" + $box.Text + "]")
                        $pt = Get-ItemCenter $popup 0
                        [void]$W::SetCursorPos($pt.X, $pt.Y)
                        $W::DoubleClick()
                        [void](Wait-Pumping { $false } 600)
                        Check '定型文: 空きの枠をダブルクリックしても何もしない' ((Test-Title (Get-PopupLabels) '定型文 > 社外') -and $box.Text -ceq "前:定型文検証 挨拶`r`n2行目定型文検証 締め") ("labels=[$(Get-PopupLabels)] text=[" + $box.Text + "]")
                        [void]$W::SetCursorPos($boxRect.Right + 300, $boxRect.Top + 40)

                        Send-Key 0x09 1   # 履歴へ
                        Check '定型文: 履歴モードに戻すと Esc の横取りをやめる' ($W::CanRegisterHotkey($owner, [uint32]0, $escVk))
                        Send-Key 0x09 1   # また定型文へ
                        Check '定型文: 小窓を出したまま定型文モードに入り直すと、前にいた階層 (社外) から' (Test-Title (Get-PopupLabels) '定型文 > 社外') "labels=[$(Get-PopupLabels)]"
                        # 社外に入ったまま離す
                        Invoke-HotkeyRelease
                        [void](Wait-Until { -not $W::IsWindowVisible($popup) } 1000)
                        Check '定型文: 小窓を消した後は Esc を横取りしない' ($W::CanRegisterHotkey($owner, [uint32]0, $escVk))
                        Invoke-HotkeyPress
                        [void](Wait-Until { $W::IsWindowVisible($popup) } 1000)
                        Send-Key 0x09 1
                        $labels = Get-PopupLabels
                        Invoke-HotkeyRelease
                        [void](Wait-Until { -not $W::IsWindowVisible($popup) } 1000)
                        Check '定型文: 開き直すと一番上から' (Test-Title $labels '定型文') "labels=[$labels]"

                        # ---- 定型文: 右クリックのメニューと、登録・編集・削除のダイアログ ----
                        function Find-TopWindow([string]$Title) {
                            foreach ($h in $W::TopWindows($app.Id)) {
                                if ($W::IsWindowVisible($h) -and $W::GetText($h) -ceq $Title) { return $h }
                            }
                            return [IntPtr]::Zero
                        }
                        # ダイアログの中の部品を探す。入力欄は 1 行 (表示名・グループ名) か複数行 (本文) か、
                        # ボタンは文字で見分ける (UI オートメーションでは WinForms の Name を読めなかった。実測)
                        function Find-Part([IntPtr]$Window, [string]$Id) {
                            foreach ($child in $W::Children($Window)) {
                                $class = $W::GetClass($child)
                                if ($class -like '*EDIT*') {
                                    $multiline = ($W::GetWindowLong($child, -16 <# GWL_STYLE #>) -band 0x0004 <# ES_MULTILINE #>) -ne 0
                                    if (($Id -eq 'textBox' -and $multiline) -or ($Id -in 'titleBox', 'nameBox' -and -not $multiline)) { return $child }
                                } elseif ($class -like '*BUTTON*') {
                                    $text = $W::GetText($child)
                                    if (($Id -eq 'okButton' -and $text -ceq 'OK') -or ($Id -eq 'cancelButton' -and $text -ceq 'キャンセル')) { return $child }
                                }
                            }
                            return [IntPtr]::Zero
                        }
                        # メニューの項目を選んで、出てきたダイアログ (Title) を返す。ホットキーはその後で離す
                        function Open-PhraseDialog([int]$Index, [string]$MenuName, [string]$Title) {
                            $menu = Open-RowMenu $Index -Phrases
                            if ($null -eq $menu -or -not (Invoke-MenuItem $menu $MenuName)) {
                                Invoke-HotkeyRelease
                                return [IntPtr]::Zero
                            }
                            [void](Wait-Pumping { (Find-TopWindow $Title) -ne [IntPtr]::Zero } 3000)
                            $script:popupHiddenAtDialog = -not $W::IsWindowVisible($popup)
                            $script:escFreeAtDialog = $W::CanRegisterHotkey($owner, [uint32]0, $escVk)
                            Invoke-HotkeyRelease
                            return (Find-TopWindow $Title)
                        }
                        # ボタンを押して閉じる。OK のときは、Copipe が phrases.json を書き終えるまで待つ
                        # (ダイアログが消えた直後に読むと、保存の前の内容を読んでしまう)
                        function Close-Dialog([IntPtr]$Dialog, [string]$ButtonId) {
                            $writtenBefore = (Get-PhrasesWritten)
                            [void]$W::PostMessage((Find-Part $Dialog $ButtonId), 0x00F5 <# BM_CLICK #>, [IntPtr]::Zero, [IntPtr]::Zero)
                            $closedMs = Wait-Pumping { -not $W::IsWindow($Dialog) -or -not $W::IsWindowVisible($Dialog) } 3000
                            if ($ButtonId -eq 'okButton') {
                                [void](Wait-Pumping { (Get-PhrasesWritten) -ne $writtenBefore } 2000)
                            }
                            # 元のアプリ役 (このハーネスの画面) は、実際のアプリと同じくメッセージを処理しながら前面に戻るのを待つ
                            [void](Wait-Pumping { $W::GetForegroundWindow() -eq $target.Handle } 2000)
                            return $closedMs
                        }

                        $phraseJson2 = '{"Slots":[null,' +
                            '{"Kind":"Group","Name":"社外","Slots":[{"Kind":"Phrase","Text":"中の定型文"},{"Kind":"Group","Name":"中のグループ"}]},' +
                            '{"Kind":"Phrase","Text":"既存の定型文"}]}'
                        [System.IO.File]::WriteAllText($phrasesPath, $phraseJson2, (New-Object System.Text.UTF8Encoding $false))
                        Reset-Target

                        # 空きの枠: 登録とグループの作成。ホットキーを離すとメニューも閉じる
                        $menu = Open-RowMenu 0 -Phrases
                        $names = if ($menu) { Get-MenuNames $menu } else { '' }
                        Check '右クリック: 空きの枠では「定型文を登録...」「グループを作成...」が出る' ($names -ceq '定型文を登録... | グループを作成...') "names=[$names]"
                        Invoke-HotkeyRelease
                        [void](Wait-Pumping { $null -eq (Find-RowMenu) -and -not $W::IsWindowVisible($popup) } 1500)
                        Check '右クリック: 選ばずにホットキーを離すと、メニューも小窓も閉じる' ($null -eq (Find-RowMenu) -and -not $W::IsWindowVisible($popup))

                        # 定型文を登録する
                        $dlg = Open-PhraseDialog 0 '定型文を登録...' '定型文を登録'
                        Check '登録: 「定型文を登録」のダイアログが出る' ($dlg -ne [IntPtr]::Zero)
                        if ($dlg -ne [IntPtr]::Zero) {
                            Check '登録: ダイアログを出すと小窓は閉じる' $script:popupHiddenAtDialog
                            Check '登録: ダイアログを出すと、数字キーや Esc の横取りをやめる' $script:escFreeAtDialog
                            [void](Wait-Pumping { $W::GetForegroundWindow() -eq $dlg } 2000)
                            Check '登録: ダイアログが前面になり、すぐ入力できる' ($W::GetForegroundWindow() -eq $dlg)
                            $titlePart = Find-Part $dlg 'titleBox'
                            $textPart = Find-Part $dlg 'textBox'
                            $okPart = Find-Part $dlg 'okButton'
                            Check '登録: 表示名・本文の欄と OK がある' ($titlePart -ne [IntPtr]::Zero -and $textPart -ne [IntPtr]::Zero -and $okPart -ne [IntPtr]::Zero)
                            Check '登録: 本文が空の間は OK を押せない' (-not $W::IsWindowEnabled($okPart))
                            # 本文の欄にフォーカスがあり、数字も普通に打てる (Copipe が横取りしていない)
                            foreach ($vk in 0x31, 0x32) { $W::KeyDown([byte]$vk); $W::KeyUp([byte]$vk) }
                            [void](Wait-Pumping { $W::GetText($textPart) -ceq '12' } 1500)
                            Check '登録: 開いたときは本文の欄にフォーカスがあり、数字キーも入力できる' ($W::GetText($textPart) -ceq '12') "text=[$($W::GetText($textPart))]"
                            Check '登録: 本文を入れると OK を押せる' ($W::IsWindowEnabled($okPart))
                            [void]$W::SetText($titlePart, 'E2E表示名')
                            [void]$W::SetText($textPart, "E2E本文`r`n2行目")
                            Check '登録: OK で閉じる' ((Close-Dialog $dlg 'okButton') -ge 0)
                            $r = Read-Phrases
                            Check '登録: 空きだった 1 番に保存される' ($null -ne $r.Slots[0] -and -not $r.Slots[0].IsGroup -and $r.Slots[0].Title -ceq 'E2E表示名' -and $r.Slots[0].Text -ceq "E2E本文`r`n2行目") ("slot=" + $(if ($r.Slots[0]) { $r.Slots[0].Title + '/' + $r.Slots[0].Text } else { 'null' }))
                            Check '登録: 他の枠は変わらない' ($r.Slots[1].IsGroup -and $r.Slots[1].Slots[0].Text -ceq '中の定型文' -and $r.Slots[2].Text -ceq '既存の定型文')
                            [void](Wait-Pumping { $W::GetForegroundWindow() -eq $target.Handle } 2000)
                            $fg = $W::GetForegroundWindow()
                            Check '登録: 閉じると、元のアプリが前面に戻る' ($fg -eq $target.Handle) ("前面=" + $W::GetClass($fg) + " [" + $W::GetText($fg) + "]")
                        }

                        # 登録した定型文が、すぐに小窓に出て入力できる
                        Reset-Target
                        [void]$W::SetCursorPos($boxRect.Right + 300, $boxRect.Top + 40)
                        Invoke-HotkeyPress
                        [void](Wait-Until { $W::IsWindowVisible($popup) } 1000)
                        Send-Key 0x09 1
                        $items = Get-PopupItems $popup
                        Send-Key 0x31 1
                        $regMs = Wait-Pumping { $box.Text -ceq "前:E2E本文`r`n2行目" } 3000
                        Invoke-HotkeyRelease
                        [void](Wait-Until { -not $W::IsWindowVisible($popup) } 1000)
                        Check '登録: 登録した定型文が表示名で出て、数字キーで本文を入力できる' ($items[0] -ceq 'E2E表示名' -and $regMs -ge 0) ("items=" + (Format-Items $items) + " text=[" + $box.Text + "] 前面=" + ($W::GetForegroundWindow() -eq $target.Handle) + " フォーカス=" + $box.Focused + " 1 を登録できるか=" + $W::CanRegisterHotkey($owner, [uint32]0, [uint32]0x31))

                        # 定型文の枠: 編集と削除
                        $menu = Open-RowMenu 0 -Phrases
                        $names = if ($menu) { Get-MenuNames $menu } else { '' }
                        Check '右クリック: 定型文では「履歴にピン止め」、区切り線、「編集...」「削除」の順に出る' ($names -ceq '履歴にピン止め | - | 編集... | 削除') "names=[$names]"
                        Invoke-HotkeyRelease
                        [void](Wait-Pumping { $null -eq (Find-RowMenu) } 1500)
                        $dlg = Open-PhraseDialog 0 '編集...' '定型文を編集'
                        Check '編集: 「定型文を編集」のダイアログが出る' ($dlg -ne [IntPtr]::Zero)
                        if ($dlg -ne [IntPtr]::Zero) {
                            $titlePart = Find-Part $dlg 'titleBox'
                            $textPart = Find-Part $dlg 'textBox'
                            Check '編集: 今の表示名と本文が入っている' ($W::GetText($titlePart) -ceq 'E2E表示名' -and $W::GetText($textPart) -ceq "E2E本文`r`n2行目") ("title=[$($W::GetText($titlePart))] text=[$($W::GetText($textPart))]")
                            [void]$W::SetText($titlePart, '')
                            [void]$W::SetText($textPart, "  `r`nE2E編集後`r`n続き")
                            [void](Close-Dialog $dlg 'okButton')
                            $r = Read-Phrases
                            Check '編集: 変えた本文が保存される' ($r.Slots[0].Text -ceq "  `r`nE2E編集後`r`n続き") "text=[$($r.Slots[0].Text)]"
                            Check '編集: 表示名を空にすると、本文の最初の空でない行が出る' ($r.Slots[0].Label -ceq 'E2E編集後') "label=[$($r.Slots[0].Label)]"
                        }

                        # 定型文を履歴にピン止めする (本文がピン止めの先頭に入る。ダイアログは出ない)
                        # 後の履歴のピン止めの検証が今の履歴を前提にしているので、終わったら履歴を元に戻す
                        $historyBeforePin = if ([System.IO.File]::Exists($historyPath)) { [System.IO.File]::ReadAllBytes($historyPath) } else { $null }
                        $pinText = (Read-Phrases).Slots[0].Text
                        $menu = Open-RowMenu 0 -Phrases
                        $chose = ($null -ne $menu) -and (Invoke-MenuItem $menu '履歴にピン止め')
                        $pinMs = Wait-Pumping {
                            $pinned = ([Copipe.Services.ClipboardHistory]::Load($historyPath, 10)).Pinned
                            $pinned.Count -gt 0 -and $pinned[0] -ceq $pinText
                        } 3000
                        Check '定型文のピン止め: 「履歴にピン止め」で本文が履歴のピン止めの先頭に入る' ($chose -and $pinMs -ge 0) "chose=$chose"
                        Check '定型文のピン止め: 選んだ後も小窓は出たまま、元のアプリが前面のまま' ($W::IsWindowVisible($popup) -and $W::GetForegroundWindow() -eq $target.Handle)
                        Send-Key 0x09 1   # 履歴へ
                        $items = Get-PopupItems $popup
                        Invoke-HotkeyRelease
                        [void](Wait-Until { -not $W::IsWindowVisible($popup) } 1000)
                        Check '定型文のピン止め: 履歴モードの一番上に 📌 付きで出る' ($items.Count -gt 0 -and $items[0] -like '📌 *E2E編集後*') ("items=" + (Format-Items $items))
                        Stop-Copipe $copipe
                        # Copipe は起動時に今のクリップボードも拾うので、空にしておく (直前に入力した定型文が残っている)
                        Invoke-ClipboardOpen { [void][CopipeVerify.Native]::EmptyClipboard() }
                        if ($null -ne $historyBeforePin) { [System.IO.File]::WriteAllBytes($historyPath, $historyBeforePin) } else { [System.IO.File]::Delete($historyPath) }
                        $copipe = Start-Copipe
                        $app = $copipe.Process
                        $popup = $copipe.Popup
                        [void]$W::SetCursorPos($boxRect.Left + 40, $boxRect.Top + 20)
                        $W::LeftClick()
                        [void](Wait-Pumping { $W::GetForegroundWindow() -eq $target.Handle } 2000)
                        [void]$W::SetCursorPos($boxRect.Right + 300, $boxRect.Top + 40)
                        Reset-Target

                        # 取り消すと何も変わらない
                        $before = [System.IO.File]::ReadAllText($phrasesPath)
                        $dlg = Open-PhraseDialog 3 '定型文を登録...' '定型文を登録'
                        if ($dlg -ne [IntPtr]::Zero) {
                            [void]$W::SetText((Find-Part $dlg 'textBox'), '取り消される')
                            [void](Close-Dialog $dlg 'cancelButton')
                        }
                        Check '取り消し: キャンセルで閉じると、定型文は変わらない' ($dlg -ne [IntPtr]::Zero -and [System.IO.File]::ReadAllText($phrasesPath) -ceq $before)

                        # グループを作る
                        $dlg = Open-PhraseDialog 3 'グループを作成...' 'グループを作成'
                        Check 'グループ: 「グループを作成」のダイアログが出る' ($dlg -ne [IntPtr]::Zero)
                        if ($dlg -ne [IntPtr]::Zero) {
                            $namePart = Find-Part $dlg 'nameBox'
                            $okPart = Find-Part $dlg 'okButton'
                            Check 'グループ: 名前が空の間は OK を押せない' ($namePart -ne [IntPtr]::Zero -and -not $W::IsWindowEnabled($okPart))
                            [void]$W::SetText($namePart, 'E2Eグループ')
                            [void](Close-Dialog $dlg 'okButton')
                            $r = Read-Phrases
                            Check 'グループ: 空きだった 4 番に、空のグループができる' ($null -ne $r.Slots[3] -and $r.Slots[3].IsGroup -and $r.Slots[3].Name -ceq 'E2Eグループ' -and @($r.Slots[3].Slots | Where-Object { $null -ne $_ }).Count -eq 0)
                        }

                        # グループの名前を変える (中身はそのまま)
                        $menu = Open-RowMenu 1 -Phrases
                        $names = if ($menu) { Get-MenuNames $menu } else { '' }
                        Check '右クリック: グループでは「名前を変更...」「削除」が出る' ($names -ceq '名前を変更... | 削除') "names=[$names]"
                        Invoke-HotkeyRelease
                        [void](Wait-Pumping { $null -eq (Find-RowMenu) } 1500)
                        $dlg = Open-PhraseDialog 1 '名前を変更...' 'グループの名前を変更'
                        if ($dlg -ne [IntPtr]::Zero) {
                            $namePart = Find-Part $dlg 'nameBox'
                            Check '名前の変更: 今の名前が入っている' ($W::GetText($namePart) -ceq '社外') "name=[$($W::GetText($namePart))]"
                            [void]$W::SetText($namePart, '社外2')
                            [void](Close-Dialog $dlg 'okButton')
                        }
                        $r = Read-Phrases
                        Check '名前の変更: 名前だけ変わり、中身はそのまま' ($dlg -ne [IntPtr]::Zero -and $r.Slots[1].Name -ceq '社外2' -and $r.Slots[1].Slots[0].Text -ceq '中の定型文' -and $r.Slots[1].Slots[1].IsGroup)

                        # 削除は、メニューを閉じてから確認のダイアログで確かめる。いいえなら消さない
                        # (メニューの中で確かめると、メニューを開いたままになり、その間は CapsLock のホットキーのフックが呼ばれなかった)
                        $confirm = Open-DeleteConfirm 1
                        $confirmText = if ($confirm -ne [IntPtr]::Zero) { (@($W::Children($confirm) | ForEach-Object { $W::GetText($_) }) -join ' ') } else { '' }
                        Check '削除: グループは確認が出て、名前と中の件数が書いてある' ($confirmText -like '*社外2*' -and $confirmText -like '*定型文 1 件*' -and $confirmText -like '*グループ 1 件*') "text=[$confirmText]"
                        Check '削除: 確認が出たときには、メニューは閉じている' $script:menuClosedAtConfirm
                        $back = Close-Confirm $confirm 7
                        Check '削除: 「いいえ」なら消さない' ((Read-Phrases).Slots[1].Name -ceq '社外2')
                        Check '削除: 「いいえ」で閉じると、元のアプリが前面に戻る' $back "前面=$script:confirmForeground"
                        $confirm = Open-DeleteConfirm 1
                        $back = Close-Confirm $confirm 6
                        $r = Read-Phrases
                        Check '削除: 「はい」でグループを中身ごと消して空きにする' ($confirm -ne [IntPtr]::Zero -and $null -eq $r.Slots[1] -and $r.Slots[2].Text -ceq '既存の定型文')
                        Check '削除: 「はい」で閉じると、元のアプリが前面に戻る' $back "前面=$script:confirmForeground"
                        $confirm = Open-DeleteConfirm 0
                        $confirmText = if ($confirm -ne [IntPtr]::Zero) { (@($W::Children($confirm) | ForEach-Object { $W::GetText($_) }) -join ' ') } else { '' }
                        Check '削除: 定型文も確認が出て、名前が書いてある' ($confirmText -like '*E2E編集後*') "text=[$confirmText]"
                        [void](Close-Confirm $confirm 6)
                        Check '削除: 「はい」で定型文を消して空きにする' ($null -eq (Read-Phrases).Slots[0])

                        # ---- 定型文: ドラッグ＆ドロップで並べ替え・グループに入れる・上の階層に出す ----
                        # 一番上: [A, 箱(中: [中1]), 空き, B, 満杯(10 件)]
                        $fullSlots = (0..9 | ForEach-Object { '{"Kind":"Phrase","Text":"f' + $_ + '"}' }) -join ','
                        $phraseJson3 = '{"Slots":[' +
                            '{"Kind":"Phrase","Text":"ドラッグA"},' +
                            '{"Kind":"Group","Name":"箱","Slots":[{"Kind":"Phrase","Text":"中1"}]},' +
                            'null,' +
                            '{"Kind":"Phrase","Text":"ドラッグB"},' +
                            '{"Kind":"Group","Name":"満杯","Slots":[' + $fullSlots + ']}]}'
                        [System.IO.File]::WriteAllText($phrasesPath, $phraseJson3, (New-Object System.Text.UTF8Encoding $false))
                        Reset-Target

                        # 見出しの左端 (一番上の「定型文」の文字の上)
                        function Get-TopLevelPoint {
                            foreach ($child in $W::Children($popup)) {
                                if ($W::GetClass($child) -like '*STATIC*' -and $W::GetText($child) -like '定型文*') {
                                    $r = $W::GetRect($child)
                                    return (Pt ($r.Left + 12) ($r.Top + [int]($r.Height / 2)))
                                }
                            }
                            return $null
                        }
                        function Open-Phrases {
                            [void]$W::SetCursorPos($boxRect.Right + 300, $boxRect.Top + 40)
                            Invoke-HotkeyPress
                            [void](Wait-Until { $W::IsWindowVisible($popup) } 1000)
                            Send-Key 0x09 1
                        }
                        function Close-Popup {
                            Invoke-HotkeyRelease
                            [void](Wait-Until { -not $W::IsWindowVisible($popup) } 1000)
                        }
                        function Get-Slots($Group) { return (@($Group.Slots | ForEach-Object { if ($null -eq $_) { '-' } elseif ($_.IsGroup) { '[' + $_.Name + ']' } else { $_.Text } }) -join ',') }


                        # 入れ替え: A を B の上へ
                        Open-Phrases
                        $written = (Get-PhrasesWritten)
                        Invoke-Drag (Get-RowPoint 0) (Get-RowPoint 3)
                        [void](Wait-Pumping { (Get-PhrasesWritten) -ne $written } 2000)
                        $items = Get-PopupItems $popup
                        $slots = Get-Slots (Read-Phrases)
                        Check 'ドラッグ: 定型文を別の定型文の上に落とすと入れ替わり、保存される' ($slots -ceq 'ドラッグB,[箱],-,ドラッグA,[満杯],-,-,-,-,-') "slots=$slots"
                        Check 'ドラッグ: 入れ替えた結果が、小窓にもすぐ出る' ($items[0] -ceq 'ドラッグB' -and $items[3] -ceq 'ドラッグA') ("items=" + (Format-Items $items))
                        Check 'ドラッグ: 小窓は出たまま' ($W::IsWindowVisible($popup))

                        # 空きに移す: A (4 行目) を空き (3 行目) へ
                        $written = (Get-PhrasesWritten)
                        Invoke-Drag (Get-RowPoint 3) (Get-RowPoint 2)
                        [void](Wait-Pumping { (Get-PhrasesWritten) -ne $written } 2000)
                        $slots = Get-Slots (Read-Phrases)
                        Check 'ドラッグ: 空きの枠に落とすと移る' ($slots -ceq 'ドラッグB,[箱],ドラッグA,-,[満杯],-,-,-,-,-') "slots=$slots"

                        # グループに入れる: A をグループ「箱」の行の中央へ
                        $written = (Get-PhrasesWritten)
                        Invoke-Drag (Get-RowPoint 2) (Get-RowPoint 1 0.5)
                        [void](Wait-Pumping { (Get-PhrasesWritten) -ne $written } 2000)
                        $r = Read-Phrases
                        Check 'ドラッグ: グループの行の中央に落とすと、グループの最初の空きに入る' ((Get-Slots $r) -ceq 'ドラッグB,[箱],-,-,[満杯],-,-,-,-,-' -and (Get-Slots $r.Slots[1]) -ceq '中1,ドラッグA,-,-,-,-,-,-,-,-') ("root=" + (Get-Slots $r) + " 箱=" + (Get-Slots $r.Slots[1]))
                        Check 'ドラッグ: グループに入れても、今の階層のまま' (Test-Title (Get-PopupLabels) '定型文') "labels=[$(Get-PopupLabels)]"

                        # グループと入れ替える: B をグループ「箱」の行の上端へ
                        $written = (Get-PhrasesWritten)
                        Invoke-Drag (Get-RowPoint 0) (Get-RowPoint 1 0.1)
                        [void](Wait-Pumping { (Get-PhrasesWritten) -ne $written } 2000)
                        $slots = Get-Slots (Read-Phrases)
                        Check 'ドラッグ: グループの行の上端に落とすと、グループと入れ替わる' ($slots -ceq '[箱],ドラッグB,-,-,[満杯],-,-,-,-,-') "slots=$slots"

                        # 落とせない先: 空きの無いグループ、自分自身
                        $before = [System.IO.File]::ReadAllText($phrasesPath)
                        Invoke-Drag (Get-RowPoint 1) (Get-RowPoint 4 0.5)
                        Check 'ドラッグ: 空きの無いグループには入れない (何も変わらない)' ([System.IO.File]::ReadAllText($phrasesPath) -ceq $before -and (Get-Slots (Read-Phrases)) -ceq '[箱],ドラッグB,-,-,[満杯],-,-,-,-,-')
                        Invoke-Drag (Get-RowPoint 0) (Get-RowPoint 0 0.5)
                        Check 'ドラッグ: グループを自分自身に落としても何も変わらない' ([System.IO.File]::ReadAllText($phrasesPath) -ceq $before)
                        Check 'ドラッグ: ドラッグしても入力はしない' ($box.Text -ceq '前:') ("text=[" + $box.Text + "]")

                        # Esc で取り消す
                        Invoke-Drag (Get-RowPoint 1) (Get-RowPoint 3) -NoRelease
                        Send-Key 0x1B 1
                        $W::LeftUp()
                        [void](Wait-Pumping { $false } 300)
                        Check 'ドラッグ: Esc で取り消すと、落としても何も変わらない' ([System.IO.File]::ReadAllText($phrasesPath) -ceq $before)
                        Check 'ドラッグ: Esc はドラッグを取り消すだけで、階層は変わらない' (Test-Title (Get-PopupLabels) '定型文') "labels=[$(Get-PopupLabels)]"

                        # 止めて開く: B をグループ「箱」の上で止めると箱が開き、中の空きに落とせる
                        $written = (Get-PhrasesWritten)
                        Invoke-Drag (Get-RowPoint 1) (Get-RowPoint 0 0.5) -HoldMs 1400 -NoRelease
                        $labels = Get-PopupLabels
                        $W::MoveMouse((Get-RowPoint 5).X, (Get-RowPoint 5).Y)
                        [void](Wait-Pumping { $false } 100)
                        $W::MoveMouse((Get-RowPoint 5).X, (Get-RowPoint 5).Y + 1)
                        [void](Wait-Pumping { $false } 100)
                        $W::LeftUp()
                        [void](Wait-Pumping { (Get-PhrasesWritten) -ne $written } 2000)
                        $r = Read-Phrases
                        Check 'ドラッグ: グループの上で止めると、そのグループが開く' (Test-Title $labels '定型文 > 箱') "labels=[$labels]"
                        Check 'ドラッグ: 開いたグループの中の枠に落とせる (元の枠は空きになる)' ((Get-Slots $r) -ceq '[箱],-,-,-,[満杯],-,-,-,-,-' -and (Get-Slots $r.Slots[0]) -ceq '中1,ドラッグA,-,-,-,ドラッグB,-,-,-,-') ("root=" + (Get-Slots $r) + " 箱=" + (Get-Slots $r.Slots[0]))

                        # 上の階層に出す: 箱の中の「中1」を見出しの「定型文」へ
                        $written = (Get-PhrasesWritten)
                        Invoke-Drag (Get-RowPoint 0) (Get-TopLevelPoint)
                        [void](Wait-Pumping { (Get-PhrasesWritten) -ne $written } 2000)
                        $r = Read-Phrases
                        Check 'ドラッグ: 見出しの階層名に落とすと、その階層の最初の空きに出る' ((Get-Slots $r) -ceq '[箱],中1,-,-,[満杯],-,-,-,-,-' -and (Get-Slots $r.Slots[0]) -ceq '-,ドラッグA,-,-,-,ドラッグB,-,-,-,-') ("root=" + (Get-Slots $r) + " 箱=" + (Get-Slots $r.Slots[0]))

                        # ホットキーを離すと取り消す
                        $before = [System.IO.File]::ReadAllText($phrasesPath)
                        Invoke-Drag (Get-RowPoint 1) (Get-RowPoint 5) -NoRelease
                        Close-Popup
                        $W::LeftUp()
                        [void](Wait-Pumping { $false } 300)
                        Check 'ドラッグ: 途中でホットキーを離すと、小窓が消えてドラッグも取り消される' ([System.IO.File]::ReadAllText($phrasesPath) -ceq $before)

                        # ドラッグの後も、数字キーでの入力は普通にできる
                        Reset-Target
                        Open-Phrases
                        Send-Key 0x31 1   # 1: 箱 (グループ) に入る
                        $afterDragLabels = Get-PopupLabels
                        Send-Key 0x36 1   # 6: ドラッグB
                        $afterDragMs = Wait-Pumping { $box.Text -ceq '前:ドラッグB' } 3000
                        $afterDragFg = $W::GetForegroundWindow()
                        Close-Popup
                        Check 'ドラッグ: 並べ替えた後も、数字キーでグループに入って入力できる' ($afterDragMs -ge 0) ("text=[" + $box.Text + "]" + " labels=[$afterDragLabels] 前面=" + $W::GetClass($afterDragFg) + " [" + $W::GetText($afterDragFg) + "]" + " 入力先=" + ($afterDragFg -eq $target.Handle))

                        # ---- 定型文: 小窓の外 (他のアプリ) へドラッグ＆ドロップする ----
                        # 一番上: [文字, ファイル, グループ]
                        $dragOutFile = Join-Path $tempDir 'dragout.txt'
                        [System.IO.File]::WriteAllText($dragOutFile, 'dragout')
                        $dragOutJson = '{"Slots":[' +
                            '{"Kind":"Phrase","Text":"外へ定型文 1行目\r\n2行目"},' +
                            '{"Kind":"Phrase","Path":' + ($dragOutFile | ConvertTo-Json) + '},' +
                            '{"Kind":"Group","Name":"外へ箱","Slots":[]}]}'
                        [System.IO.File]::WriteAllText($phrasesPath, $dragOutJson, (New-Object System.Text.UTF8Encoding $false))
                        Show-DropTarget
                        Reset-Target
                        Open-Phrases
                        $before = [System.IO.File]::ReadAllText($phrasesPath)
                        $dropped = Invoke-DragOut 0
                        Check '外へドラッグ: (準備) ドロップ先が小窓に隠れていない' $script:dropUnder
                        Check '外へドラッグ: 定型文の文字は、文字としてほかのアプリに落とせる (改行も含めて全文)' ($dropped -ceq "TEXT:外へ定型文 1行目`r`n2行目") "dropped=[$dropped]"
                        Check '外へドラッグ: コピーとして渡す (移動はさせない)' ($script:droppedEffects -eq [System.Windows.Forms.DragDropEffects]::Copy) "effects=$script:droppedEffects"
                        Check '外へドラッグ: ANSI のテキストしか読まないアプリ向けに、ANSI のテキストも渡す' ($script:droppedAnsi -eq $true) "ansi=$script:droppedAnsi"
                        $dropped = Invoke-DragOut 1
                        Check '外へドラッグ: ファイルのパスの定型文は、ファイルそのものとして落とせる' ($dropped -ceq "FILE:$dragOutFile") "dropped=[$dropped]"
                        Check '外へドラッグ: ファイルはコピーとして渡す (移動はさせない)' ($script:droppedEffects -eq [System.Windows.Forms.DragDropEffects]::Copy) "effects=$script:droppedEffects"
                        $dropped = Invoke-DragOut 2 -TimeoutMs 300
                        Check '外へドラッグ: グループは外へ出せない (何も落ちない)' ($null -eq $dropped) "dropped=[$dropped]"
                        Check '外へドラッグ: 外へ落としても、定型文は変わらない' ([System.IO.File]::ReadAllText($phrasesPath) -ceq $before)
                        Check '外へドラッグ: 外へ落としても、入力先には何も入力しない' ($box.Text -ceq '前:') ("text=[" + $box.Text + "]")
                        Check '外へドラッグ: 落とした後も小窓は出たまま' ($W::IsWindowVisible($popup))
                        Send-Key 0x31 1   # 1: 外へ定型文
                        $afterOutMs = Wait-Pumping { $box.Text -ceq "前:外へ定型文 1行目`r`n2行目" } 3000
                        Check '外へドラッグ: 落とした後も、数字キーで入力できる' ($afterOutMs -ge 0) ("text=[" + $box.Text + "]")
                        Close-Popup
                        $dropTarget.Hide()


                        # ---- 定型文: 見出しの階層名 (パンくずリスト) をクリックして移動する ----
                        $crumbJson = '{"Slots":[{"Kind":"Group","Name":"外側","Slots":[{"Kind":"Group","Name":"内側","Slots":[{"Kind":"Phrase","Text":"一番奥"}]}]}]}'
                        [System.IO.File]::WriteAllText($phrasesPath, $crumbJson, (New-Object System.Text.UTF8Encoding $false))
                        # 見出しの Index 番目の階層名の真ん中 (小窓と同じフォント・描き方で幅を測る)
                        function Get-SegmentPoint([int]$Index) {
                            foreach ($child in $W::Children($popup)) {
                                if ($W::GetClass($child) -like '*STATIC*' -and $W::GetText($child) -like '定型文*') {
                                    $r = $W::GetRect($child)
                                    $segments = $W::GetText($child) -split ' > '
                                    $font = New-Object System.Drawing.Font([System.Drawing.SystemFonts]::MessageBoxFont, [System.Drawing.FontStyle]::Bold)
                                    $flags = [System.Windows.Forms.TextFormatFlags]'NoPrefix, SingleLine, VerticalCenter, NoPadding'
                                    # Copipe と同じく、画面の Graphics を渡して測る (渡さないと NoPadding が効かず、余白の分だけ幅が大きくなる。実測)
                                    $screen = [System.Drawing.Graphics]::FromHwnd([IntPtr]::Zero)
                                    $measure = { param($s) [System.Windows.Forms.TextRenderer]::MeasureText($screen, $s, $font, [System.Drawing.Size]::Empty, $flags).Width }
                                    $x = $r.Left
                                    for ($i = 0; $i -lt $Index; $i++) { $x += (& $measure $segments[$i]) + (& $measure ' > ') }
                                    $x += [int]((& $measure $segments[$Index]) / 2)
                                    $font.Dispose()
                                    $screen.Dispose()
                                    return (Pt $x ($r.Top + [int]($r.Height / 2)))
                                }
                            }
                            return $null
                        }
                        Reset-Target
                        Open-Phrases
                        Send-Key 0x31 1   # 外側
                        Send-Key 0x31 1   # 内側
                        $labels = Get-PopupLabels
                        Check 'パンくず: (準備) 2 階層下まで入れた' (Test-Title $labels '定型文 > 外側 > 内側') "labels=[$labels]"
                        $pt = Get-SegmentPoint 2
                        [void]$W::SetCursorPos($pt.X, $pt.Y)
                        $W::LeftClick()
                        [void](Wait-Pumping { $false } 300)
                        Check 'パンくず: 今いる階層 (右端) をクリックしても何も変わらない' (Test-Title (Get-PopupLabels) '定型文 > 外側 > 内側') "labels=[$(Get-PopupLabels)]"
                        $pt = Get-SegmentPoint 1
                        [void]$W::SetCursorPos($pt.X, $pt.Y)
                        $W::LeftClick()
                        [void](Wait-Pumping { Test-Title (Get-PopupLabels) '定型文 > 外側' } 1000)
                        $items = Get-PopupItems $popup
                        Check 'パンくず: 途中の階層名をクリックすると、その階層へ移る' ((Test-Title (Get-PopupLabels) '定型文 > 外側') -and $items[0] -ceq '├ 内側') ("labels=[$(Get-PopupLabels)] items=" + (Format-Items $items))
                        Send-Key 0x31 1   # また内側へ
                        $pt = Get-SegmentPoint 0
                        [void]$W::SetCursorPos($pt.X, $pt.Y)
                        $W::LeftClick()
                        [void](Wait-Pumping { Test-Title (Get-PopupLabels) '定型文' } 1000)
                        Check 'パンくず: 「定型文」をクリックすると、一気に一番上へ戻る' (Test-Title (Get-PopupLabels) '定型文') "labels=[$(Get-PopupLabels)]"
                        Check 'パンくず: クリックしても小窓は出たまま、入力もしない' ($W::IsWindowVisible($popup) -and $box.Text -ceq '前:') ("text=[" + $box.Text + "]")
                        [void]$W::SetCursorPos($boxRect.Right + 300, $boxRect.Top + 40)
                        Close-Popup
                        Check 'パンくず: クリックの後も、元のアプリが前面のまま' ($W::GetForegroundWindow() -eq $target.Handle)

                        }
                        $box.AcceptsTab = $false

                        # ---- 履歴のピン止め (右クリックでピン止め、📌 のクリックで外す) ----
                        # 履歴の並びを変えるので、定型文の検証 (モードキーの検証が履歴の並びを見る) の後に置く
                        if ($runHistory) {
                        function Open-History {
                            [void]$W::SetCursorPos($boxRect.Right + 300, $boxRect.Top + 40)
                            Invoke-HotkeyPress
                            [void](Wait-Until { $W::IsWindowVisible($popup) } 1000)
                        }
                        function Close-History {
                            [void]$W::SetCursorPos($boxRect.Right + 300, $boxRect.Top + 40)
                            Invoke-HotkeyRelease
                            [void](Wait-Until { -not $W::IsWindowVisible($popup) } 1000)
                        }
                        # 一覧の Index 行目の右端 (📌 の上)
                        function Get-PinIconPoint([int]$Index) {
                            $center = Get-ItemCenter $popup $Index
                            foreach ($child in $W::Children($popup)) {
                                if ($W::GetClass($child) -like '*LISTBOX*') { return (Pt ($W::GetRect($child).Right - 10) $center.Y) }
                            }
                            return $null
                        }
                        # 前の検証で入力した定型文も履歴に入っているので、履歴を新しい順に「数字キー検証 11」〜「数字キー検証 02」の 10 件にして起動し直す
                        Stop-Copipe $copipe
                        $pinItems = @(11..2 | ForEach-Object { '"数字キー検証 {0:D2}"' -f $_ }) -join ','
                        [System.IO.File]::WriteAllText($historyPath, '{"Items":[' + $pinItems + '],"Pinned":[]}', (New-Object System.Text.UTF8Encoding $false))
                        # クリップボードに前の検証で入力した定型文が残っていると、小窓を出したときに履歴へ拾われるので空にする
                        Invoke-ClipboardOpen { [void][CopipeVerify.Native]::EmptyClipboard() }
                        $copipe = Start-Copipe
                        $app = $copipe.Process
                        $popup = $copipe.Popup
                        [void]$W::SetCursorPos($boxRect.Left + 40, $boxRect.Top + 20)
                        $W::LeftClick()
                        [void](Wait-Pumping { $W::GetForegroundWindow() -eq $target.Handle } 2000)
                        Reset-Target
                        $menu = Open-RowMenu 2
                        $names = if ($menu) { Get-MenuNames $menu } else { '' }
                        Check 'ピン止め: 履歴の行を右クリックすると「ピン止め」と「削除」が出る' ($names -ceq 'ピン止め | - | 削除') "names=[$names]"
                        if ($menu) { [void](Invoke-MenuItem $menu 'ピン止め') }
                        [void](Wait-Pumping { (Get-PopupItems $popup)[0] -ceq '📌 数字キー検証 09' } 2000)
                        $items = Get-PopupItems $popup
                        Check 'ピン止め: ピン止めした項目が一番上に 📌 付きで移る (空いた番号は空きの枠)' ($items.Count -eq 11 -and $items[10] -ceq '' -and $items[0] -ceq '📌 数字キー検証 09' -and $items[1] -ceq '数字キー検証 11' -and $items[2] -ceq '数字キー検証 10' -and $items[3] -ceq '数字キー検証 08') ("items=" + (Format-Items $items))
                        # 元のアプリへ戻すのは、メニューが閉じてから少し後になる
                        $fgMs = Wait-Pumping { $W::GetForegroundWindow() -eq $target.Handle } 1000
                        Check 'ピン止め: メニューで選んだ後も、元のアプリが前面のまま' ($fgMs -ge 0)
                        Check 'ピン止め: 小窓は出たまま' ($W::IsWindowVisible($popup))
                        Send-Key 0x31 1   # 1
                        $pinNumMs = Wait-Pumping { $box.Text -ceq '前:数字キー検証 11' } 3000
                        Check 'ピン止め: ピン止めは数字なし (a〜z)。数字キー 1 は普通の履歴の 1 件目を入力する' ($pinNumMs -ge 0) ("text=[" + $box.Text + "]")
                        $pt = Get-ItemCenter $popup 0
                        [void]$W::SetCursorPos($pt.X, $pt.Y)
                        $W::DoubleClick()
                        $pinDblMs = Wait-Pumping { $box.Text -ceq '前:数字キー検証 11数字キー検証 09' } 3000
                        Check 'ピン止め: ピン止めした行の本文をダブルクリックすると入力される' ($pinDblMs -ge 0) ("text=[" + $box.Text + "]")
                        Close-History

                        $menu = Open-RowMenu 0
                        $names = if ($menu) { Get-MenuNames $menu } else { '' }
                        Check 'ピン止め: ピン止めした行を右クリックすると「ピン止めを外す」と「削除」が出る' ($names -ceq 'ピン止めを外す | - | 削除') "names=[$names]"
                        # 小窓はフォーカスを奪わないので、メニューには直接キーが届かない。Esc・モードキーは Copipe が受け取って閉じる
                        Send-Key 0x1B 1   # Esc
                        $escMs = Wait-Pumping { $null -eq (Find-RowMenu) } 1500
                        Check '右クリック: Esc でメニューを閉じる (小窓は出たまま)' ($escMs -ge 0 -and $W::IsWindowVisible($popup))
                        $pt = Get-ItemCenter $popup 0
                        [void]$W::SetCursorPos($pt.X, $pt.Y)
                        $W::RightClick()
                        [void](Wait-Pumping { $null -ne (Find-RowMenu) } 1500)
                        Send-Key 0x09 1   # Tab (モードキー)
                        $modeMs = Wait-Pumping { $null -eq (Find-RowMenu) -and (Test-Title (Get-PopupLabels) '定型文') } 1500
                        Check '右クリック: モードキーでメニューを閉じて、定型文モードに切り替わる' ($modeMs -ge 0) "labels=[$(Get-PopupLabels)]"
                        Close-History
                        [void](Wait-Pumping { $null -eq (Find-RowMenu) } 1500)

                        # 新しくコピーしても、ピン止めは一番上に残る
                        Set-ClipboardText ('ピン止め検証 新しいコピー')
                        Start-Sleep -Milliseconds 400
                        Open-History
                        $items = Get-PopupItems $popup
                        Close-History
                        Check 'ピン止め: 新しくコピーしても、ピン止めは一番上に残る' ($items[0] -ceq '📌 数字キー検証 09' -and $items[1] -ceq 'ピン止め検証 新しいコピー') ("items=" + (Format-Items $items))

                        # 起動し直しても残る
                        Stop-Copipe $copipe
                        $copipe = Start-Copipe
                        $app = $copipe.Process
                        $popup = $copipe.Popup
                        [void]$W::SetCursorPos($boxRect.Left + 40, $boxRect.Top + 20)
                        $W::LeftClick()
                        [void](Wait-Pumping { $W::GetForegroundWindow() -eq $target.Handle } 2000)
                        Reset-Target
                        Open-History
                        $items = Get-PopupItems $popup
                        Check 'ピン止め: Copipe を起動し直しても残る' ($items[0] -ceq '📌 数字キー検証 09') ("items=" + (Format-Items $items))

                        # 📌 をクリックすると外れる (入力はしない)
                        $pt = Get-PinIconPoint 0
                        [void]$W::SetCursorPos($pt.X, $pt.Y)
                        $W::LeftClick()
                        [void](Wait-Pumping { (Get-PopupItems $popup)[0] -ceq '数字キー検証 09' } 2000)
                        $items = Get-PopupItems $popup
                        Check 'ピン止めを外す: 📌 をクリックすると外れ、普通の履歴の先頭に戻る' ($items[0] -ceq '数字キー検証 09' -and $items[1] -ceq 'ピン止め検証 新しいコピー' -and @($items | Where-Object { $_ -like '📌*' }).Count -eq 0) ("items=" + (Format-Items $items))
                        # ダブルクリックの設定なので、続けてもう 1 回クリックしても (ダブルクリック扱い) 入力しない
                        $W::LeftClick()
                        [void](Wait-Pumping { $false } 700)
                        Check 'ピン止めを外す: 📌 のクリックでは入力しない (続けてクリックしても)' ($box.Text -ceq '前:') ("text=[" + $box.Text + "]")
                        Check 'ピン止めを外す: 小窓は出たまま、元のアプリが前面のまま' ($W::IsWindowVisible($popup) -and $W::GetForegroundWindow() -eq $target.Handle)
                        Close-History

                        # 右クリックの「削除」で履歴から消す (確認はしない。後ろが詰まり、空いた番号は空きの枠)
                        $menu = Open-RowMenu 1
                        $names = if ($menu) { Get-MenuNames $menu } else { '' }
                        Check '削除: 履歴の行のメニューに「削除」がある (まだ消さない)' ($names -ceq 'ピン止め | - | 削除' -and (Get-PopupItems $popup)[1] -ceq 'ピン止め検証 新しいコピー') "names=[$names]"
                        $writtenBefore = [System.IO.File]::GetLastWriteTimeUtc($historyPath)
                        $chose = $menu -and (Invoke-MenuItem $menu '削除')
                        [void](Wait-Pumping { [System.IO.File]::GetLastWriteTimeUtc($historyPath) -ne $writtenBefore } 2000)
                        [void](Wait-Pumping { (Get-PopupItems $popup)[1] -ceq '数字キー検証 11' } 2000)
                        $items = Get-PopupItems $popup
                        Check '削除: 選ぶと確認せずに履歴から消え、後ろが詰まる' ($chose -and $null -eq (Find-RowMenu) -and $items[0] -ceq '数字キー検証 09' -and $items[1] -ceq '数字キー検証 11' -and $items -notcontains 'ピン止め検証 新しいコピー') ("items=" + (Format-Items $items))
                        $saved = [Copipe.Services.ClipboardHistory]::Load($historyPath, 10)
                        Check '削除: 消したことが保存される' ($saved.Items -notcontains 'ピン止め検証 新しいコピー' -and $saved.Items.Count -eq 9) "items=$($saved.Items -join ',')"
                        $fgMs = Wait-Pumping { $W::GetForegroundWindow() -eq $target.Handle } 1000
                        Check '削除: 小窓は出たまま、元のアプリが前面のまま、入力はしない' ($fgMs -ge 0 -and $W::IsWindowVisible($popup) -and $box.Text -ceq '前:') ("text=[" + $box.Text + "]")
                        Close-History

                        # ---- 履歴モード: ドラッグ＆ドロップで並べ替える (ピン止めはピン止めの中、普通の履歴は普通の履歴の中) ----
                        # 決まった中身の履歴で起動し直す。終わったら元の履歴に戻す (後の検証は今の履歴を前提にしている)
                        $historyBeforeDrag = [System.IO.File]::ReadAllBytes($historyPath)
                        function Restart-WithHistory([byte[]]$Bytes) {
                            Stop-Copipe $script:dragCopipe
                            [System.IO.File]::WriteAllBytes($historyPath, $Bytes)
                            $script:dragCopipe = Start-Copipe
                            [void]$W::SetCursorPos($boxRect.Left + 40, $boxRect.Top + 20)
                            $W::LeftClick()
                            [void](Wait-Pumping { $W::GetForegroundWindow() -eq $target.Handle } 2000)
                            [void]$W::SetCursorPos($boxRect.Right + 300, $boxRect.Top + 40)
                        }
                        $dragHistory = New-Object Copipe.Services.ClipboardHistory 10
                        foreach ($t in '並べ替えI4', '並べ替えI3', '並べ替えI2', '並べ替えI1') { [void]$dragHistory.Add($t) }
                        foreach ($t in '並べ替えP3', '並べ替えP2', '並べ替えP1') { [void]$dragHistory.PinText($t) }
                        $dragHistory.Save($historyPath)
                        # Copipe は起動時に今のクリップボードも拾うので、空にしておく
                        Invoke-ClipboardOpen { [void][CopipeVerify.Native]::EmptyClipboard() }
                        $script:dragCopipe = $copipe
                        Restart-WithHistory ([System.IO.File]::ReadAllBytes($historyPath))
                        $copipe = $script:dragCopipe
                        $app = $copipe.Process
                        $popup = $copipe.Popup
                        function Read-SavedHistory { return [Copipe.Services.ClipboardHistory]::Load($historyPath, 10) }
                        # 行: 0 P1、1 P2、2 P3 (ピン止め)、3 I1、4 I2、5 I3、6 I4
                        Reset-Target
                        Open-History
                        Invoke-Drag (Get-RowPoint 2) (Get-RowPoint 0)
                        [void](Wait-Pumping { ((Read-SavedHistory).Pinned -join ',') -ceq '並べ替えP3,並べ替えP1,並べ替えP2' } 2000)
                        $items = Get-PopupItems $popup
                        Check '並べ替え: ピン止めの 3 行目を 1 行目に落とすと、1 行目に移り、間のものは 1 つずつ下がる' ($items[0] -ceq '📌 並べ替えP3' -and $items[1] -ceq '📌 並べ替えP1' -and $items[2] -ceq '📌 並べ替えP2') ("items=" + (Format-Items $items))
                        Check '並べ替え: ピン止めの並びが保存される' (((Read-SavedHistory).Pinned -join ',') -ceq '並べ替えP3,並べ替えP1,並べ替えP2') "pinned=$((Read-SavedHistory).Pinned -join ',')"
                        Check '並べ替え: 落とした後も小窓は出たまま、元のアプリが前面のまま' ($W::IsWindowVisible($popup) -and $W::GetForegroundWindow() -eq $target.Handle)
                        Check '並べ替え: ドラッグでは入力しない' ($box.Text -ceq '前:') ("text=[" + $box.Text + "]")

                        Invoke-Drag (Get-RowPoint 6) (Get-RowPoint 3)
                        [void](Wait-Pumping { ((Read-SavedHistory).Items -join ',') -ceq '並べ替えI4,並べ替えI1,並べ替えI2,並べ替えI3' } 2000)
                        $items = Get-PopupItems $popup
                        Check '並べ替え: 普通の履歴の最後の行を先頭に落とすと、先頭に移る' ($items[3] -ceq '並べ替えI4' -and $items[4] -ceq '並べ替えI1' -and $items[6] -ceq '並べ替えI3') ("items=" + (Format-Items $items))
                        Check '並べ替え: 普通の履歴の並びが保存される' (((Read-SavedHistory).Items -join ',') -ceq '並べ替えI4,並べ替えI1,並べ替えI2,並べ替えI3') "items=$((Read-SavedHistory).Items -join ',')"
                        Send-Key 0x31 1
                        $moveNumMs = Wait-Pumping { $box.Text -ceq '前:並べ替えI4' } 3000
                        Check '並べ替え: 番号は新しい並びで振られる (数字キー 1 で移した項目が入る)' ($moveNumMs -ge 0) ("text=[" + $box.Text + "]")

                        Invoke-Drag (Get-RowPoint 3) (Get-RowPoint 5)
                        [void](Wait-Pumping { ((Read-SavedHistory).Items -join ',') -ceq '並べ替えI1,並べ替えI2,並べ替えI4,並べ替えI3' } 2000)
                        $items = Get-PopupItems $popup
                        Check '並べ替え: 普通の履歴を下の行に落とすと、その行の位置に移る' ($items[3] -ceq '並べ替えI1' -and $items[5] -ceq '並べ替えI4' -and $items[6] -ceq '並べ替えI3') ("items=" + (Format-Items $items))

                        $before = Format-Items (Get-PopupItems $popup)
                        Invoke-Drag (Get-RowPoint 0) (Get-RowPoint 4)
                        Invoke-Drag (Get-RowPoint 5) (Get-RowPoint 1)
                        [void](Wait-Pumping { $false } 300)
                        $after = Format-Items (Get-PopupItems $popup)
                        Check '並べ替え: ピン止めと普通の履歴の間では移さない (両方向とも)' ($after -ceq $before) "before=$before after=$after"

                        # ---- 履歴モード: ドラッグ中にモードキーで定型文に切り替え、空きの枠に落とすと定型文として登録する ----
                        # 登録した定型文は、後で phrases.json ごと元に戻す (後の検証は今の定型文を前提にしている)
                        $phrasesBeforeHistoryDrop = if ([System.IO.File]::Exists($phrasesPath)) { [System.IO.File]::ReadAllBytes($phrasesPath) } else { $null }
                        # 履歴の Row 行をつかみ、Tab で定型文モードにして、一番上の階層の ToSlot 行 (省略すると最初の空きの枠) に落とす。落とした行の位置を返す
                        function Invoke-HistoryToPhrase([int]$Row, [int]$ToSlot = -1) {
                            $slots = (Read-Phrases).Slots
                            $slot = 0
                            while ($slot -lt $slots.Length -and $null -ne $slots[$slot]) { $slot++ }
                            if ($ToSlot -ge 0) { $slot = $ToSlot }
                            Invoke-Drag (Get-RowPoint $Row) (Get-RowPoint $Row 0.95) -NoRelease
                            Send-Key 0x09 1   # Tab (モードキー)
                            $script:historyDropLabels = Get-PopupLabels
                            $to = Get-RowPoint $slot
                            $W::MoveMouse($to.X, $to.Y - 3)
                            [void](Wait-Pumping { $false } 50)
                            $W::MoveMouse($to.X, $to.Y)
                            [void](Wait-Pumping { $false } 100)
                            $W::LeftUp()
                            [void](Wait-Pumping { $false } 300)
                            return $slot
                        }
                        # 入力先には、前の検証 (数字キー 1) で入力した文字が残っている。ドラッグで増えないことを見る
                        $textBeforeHistoryDrop = $box.Text
                        $slot = Invoke-HistoryToPhrase 3
                        Check '履歴→定型文: ドラッグ中でもモードキーで定型文モードに切り替わる' (Test-Title $script:historyDropLabels '定型文') "labels=[$script:historyDropLabels]"
                        $node = (Read-Phrases).Slots[$slot]
                        Check '履歴→定型文: 空きの枠に落とすと、その文字の定型文として登録される' ($null -ne $node -and -not $node.IsGroup -and $node.Text -ceq '並べ替えI1' -and $node.Path -ceq '') "slot=$slot text=[$($node.Text)] path=[$($node.Path)]"
                        Check '履歴→定型文: 落とした後も定型文モードのまま、一覧に出る' ((Test-Title (Get-PopupLabels) '定型文') -and (Get-PopupItems $popup)[$slot] -like '*並べ替えI1') ("items=" + (Format-Items (Get-PopupItems $popup)))
                        Check '履歴→定型文: 履歴からは消えない' (@((Read-SavedHistory).Items) -contains '並べ替えI1') "items=$((Read-SavedHistory).Items -join ',')"
                        Check '履歴→定型文: ドラッグでは入力しない' ($box.Text -ceq $textBeforeHistoryDrop) ("text=[" + $box.Text + "] before=[" + $textBeforeHistoryDrop + "]")
                        Close-History

                        # グループの行 (中央) に落とすと、そのグループの最初の空きに入る (Copipe は定型文モードに入るとき読み直す)
                        $book = [Copipe.Services.PhraseBook]::Load($phrasesPath)
                        $groupSlot = 0
                        while ($null -ne $book.Root.Slots[$groupSlot]) { $groupSlot++ }
                        $book.Root.Slots[$groupSlot] = [Copipe.Model.PhraseNode]::CreateGroup('履歴の落とし先')
                        $book.Save($phrasesPath)
                        Open-History
                        [void](Invoke-HistoryToPhrase 4 $groupSlot)
                        $group = (Read-Phrases).Slots[$groupSlot]
                        Check '履歴→定型文: グループの行に落とすと、そのグループの最初の空きに登録される' ($null -ne $group -and $group.IsGroup -and $null -ne $group.Slots[0] -and $group.Slots[0].Text -ceq '並べ替えI2') "group=[$($group.Name)] first=[$(if ($group -and $group.Slots[0]) { $group.Slots[0].Text })]"
                        Close-History

                        # ---- 履歴モード: 小窓の外 (他のアプリ) へドラッグ＆ドロップする ----
                        # 行: 0 文字 (URI)、1 URL を開く項目 (定型文のパスに URL を登録して開いたもの)、2 ファイル (開く項目)
                        $dragOutFile = Join-Path $tempDir 'dragout-history.txt'
                        [System.IO.File]::WriteAllText($dragOutFile, 'dragout')
                        $outHistory = New-Object Copipe.Services.ClipboardHistory 10
                        [void]$outHistory.Add([Copipe.Services.ClipboardHistory]::LaunchEntry($dragOutFile, ''))
                        [void]$outHistory.Add([Copipe.Services.ClipboardHistory]::LaunchEntry('https://example.com/開く', 'URLを開く項目'))
                        [void]$outHistory.Add('https://example.com/外へ?a=1')
                        $outHistory.Save($historyPath)
                        # 並べ替えの検証で入力した文字がクリップボードに残っていると、起動し直したときに履歴へ拾われる
                        Invoke-ClipboardOpen { [void][CopipeVerify.Native]::EmptyClipboard() }
                        Restart-WithHistory ([System.IO.File]::ReadAllBytes($historyPath))
                        $copipe = $script:dragCopipe
                        $app = $copipe.Process
                        $popup = $copipe.Popup
                        Show-DropTarget
                        Reset-Target
                        Open-History
                        $before = Format-Items (Get-PopupItems $popup)
                        $dropped = Invoke-DragOut 0
                        Check '外へドラッグ: 履歴の URI は、文字としてほかのアプリに落とせる' ($dropped -ceq 'TEXT:https://example.com/外へ?a=1') "dropped=[$dropped] under=$script:dropUnder"
                        Check '外へドラッグ: 履歴の文字も、ANSI のテキストを一緒に渡す' ($script:droppedAnsi -eq $true) "ansi=$script:droppedAnsi"
                        $dropped = Invoke-DragOut 1
                        Check '外へドラッグ: URL を開く項目は、ファイルではなく文字として落とす (ブラウザーがローカルのファイルとして開かない)' ($dropped -ceq 'TEXT:https://example.com/開く') "dropped=[$dropped]"
                        $dropped = Invoke-DragOut 2
                        Check '外へドラッグ: 履歴のファイル (開く項目) は、ファイルそのものとして落とせる' ($dropped -ceq "FILE:$dragOutFile") "dropped=[$dropped]"
                        $after = Format-Items (Get-PopupItems $popup)
                        Check '外へドラッグ: 履歴の並びは変わらず、ピン止めにもならない' ($after -ceq $before) "before=$before after=$after"
                        Check '外へドラッグ: 履歴から落としても、入力先には何も入力しない' ($box.Text -ceq '前:') ("text=[" + $box.Text + "]")
                        Close-History
                        $dropTarget.Hide()

                        # 履歴のファイル (開く項目) を定型文に落とすと、そのファイルを開く定型文 (ランチャー) になる
                        Open-History
                        $slot = Invoke-HistoryToPhrase 2
                        $node = (Read-Phrases).Slots[$slot]
                        Check '履歴→定型文: ファイルは、それを開く定型文 (パス付き) として登録される' ($null -ne $node -and $node.Path -ceq $dragOutFile -and $node.Text -ceq '') "slot=$slot text=[$($node.Text)] path=[$($node.Path)]"
                        Close-History
                        if ($null -ne $phrasesBeforeHistoryDrop) { [System.IO.File]::WriteAllBytes($phrasesPath, $phrasesBeforeHistoryDrop) } else { Remove-Item -LiteralPath $phrasesPath -ErrorAction SilentlyContinue }

                        Restart-WithHistory $historyBeforeDrag
                        $copipe = $script:dragCopipe
                        $app = $copipe.Process
                        $popup = $copipe.Popup
                        Reset-Target

                        # 設定のホットキーを変えて Copipe を起動し直し、入力先を前面にする
                        function Restart-Copipe([System.Windows.Forms.Keys]$Hotkey) {
                            Stop-Copipe $script:copipeRef
                            Use-Hotkey $Hotkey
                            $script:copipeRef = Start-Copipe
                            [void]$W::SetCursorPos($boxRect.Left + 40, $boxRect.Top + 20)
                            $W::LeftClick()
                            [void](Wait-Pumping { $W::GetForegroundWindow() -eq $target.Handle } 2000)
                            [void]$W::SetCursorPos($boxRect.Right + 300, $boxRect.Top + 40)
                        }
                        $script:copipeRef = $copipe

                        # ---- CapsLock をホットキーにする (低レベルのキーボードフックで受け取り、握りつぶす) ----
                        # 起動時に CapsLock がオンなら、Copipe がオフに戻す (握りつぶしている間は CapsLock だけでは戻せないため)
                        if (-not $W::IsToggled(0x14)) { $W::KeyDown(0x14); $W::KeyUp(0x14) }
                        [void](Wait-Pumping { $W::IsToggled(0x14) } 1000)
                        $capsOnBeforeStart = $W::IsToggled(0x14)
                        # Copipe より先にフックを付け、Copipe が通した CapsLock だけを数える (後から付けた Copipe のフックが先に呼ばれる)
                        $keyWatchOn = $W::StartKeyWatch(0x14)
                        Check 'CapsLock: 検証のフックを付けられる (Copipe が通したキーを数えるため)' $keyWatchOn
                        Restart-Copipe $K::Capital
                        $popup = $script:copipeRef.Popup
                        Check 'CapsLock: 起動できる (登録の失敗を知らせない)' ($popup -ne [IntPtr]::Zero -and (Find-Dialog $script:copipeRef.Process.Id) -eq [IntPtr]::Zero)
                        $capsOffMs = Wait-Pumping { -not $W::IsToggled(0x14) } 2000
                        Check 'CapsLock: 起動時にオンだった CapsLock をオフに戻す' ($capsOnBeforeStart -and $capsOffMs -ge 0) "before=$capsOnBeforeStart after=$($W::IsToggled(0x14))"
                        Check 'CapsLock: Copipe が登録している (二重起動・他のアプリとの取り合いを今までどおり知らせる)' (-not $W::CanRegisterHotkey($owner, [uint32]0, [uint32]0x14))
                        Reset-Target
                        $W::ResetKeyWatch()
                        $W::KeyDown(0x14)
                        $capsShowMs = Wait-Until { $W::IsWindowVisible($popup) } 1000
                        # 押し続けたときの繰り返し (押し下げが重ねて届く) でも、出たままで切り替わらない
                        foreach ($i in 1..3) { [void](Wait-Pumping { $false } 100); $W::KeyDown(0x14) }
                        $capsStillShown = $W::IsWindowVisible($popup)
                        $W::KeyUp(0x14)
                        $capsHideMs = Wait-Until { -not $W::IsWindowVisible($popup) } 1000
                        [void](Wait-Pumping { $false } 200)
                        Check 'CapsLock: 押し続けている間だけ小窓が出て、離すと消える' ($capsShowMs -ge 0 -and $capsStillShown -and $capsHideMs -ge 0) "show=$capsShowMs held=$capsStillShown hide=$capsHideMs"
                        Check 'CapsLock: 押して離しても CapsLock はオフのまま (握りつぶしている)' (-not $W::IsToggled(0x14) -and $W::KeyWatchDowns -eq 0) "toggled=$($W::IsToggled(0x14)) passed=$($W::KeyWatchDowns)"
                        Check 'CapsLock: 入力先に何も入力しない' ($box.Text -ceq '前:') ("text=[" + $box.Text + "]")
                        # 修飾キーと一緒でも受け取って握りつぶす。Shift では確かめない: 日本語入力を使っていると、Shift+CapsLock は
                        # 低レベルのフックに届く前に日本語入力が受け取る (Copipe を止めていても検証のフックに届かなかった。実測)
                        $W::ResetKeyWatch()
                        $W::KeyDown(0x11); $W::KeyDown(0x14)
                        $ctrlCapsShowMs = Wait-Until { $W::IsWindowVisible($popup) } 1000
                        $W::KeyUp(0x14)
                        $ctrlCapsHideMs = Wait-Until { -not $W::IsWindowVisible($popup) } 1000
                        $W::KeyUp(0x11)
                        [void](Wait-Pumping { $false } 200)
                        Check 'CapsLock: Ctrl+CapsLock でも小窓が出て、離すと消える' ($ctrlCapsShowMs -ge 0 -and $ctrlCapsHideMs -ge 0) "show=$ctrlCapsShowMs hide=$ctrlCapsHideMs"
                        Check 'CapsLock: Ctrl+CapsLock も握りつぶす (CapsLock はオフのまま)' (-not $W::IsToggled(0x14) -and $W::KeyWatchDowns -eq 0) "toggled=$($W::IsToggled(0x14)) passed=$($W::KeyWatchDowns)"
                        $W::StopKeyWatch()

                        $copipe = $script:copipeRef
                        $app = $copipe.Process
                        $popup = $copipe.Popup
                        }
                    }
                } finally {
                    if ($dropTarget) { $dropTarget.Close(); $dropTarget.Dispose() }
                    $target.Close()
                    $target.Dispose()
                }
            }
            if ($app -and -not $app.HasExited) { Stop-Copipe $copipe }
        } finally {
            if ($script:hotkeyDown) { Invoke-HotkeyRelease }
            if ($helper) { Stop-ClipboardHelper $helper }
            if ($second -and -not $second.HasExited) { $second.Kill(); [void]$second.WaitForExit(5000) }
            # 残っていれば正常終了を依頼する (強制終了はトレイにアイコンの抜け殻を残すため最後の手段)
            & $stopScript -ExePath $exe
            $W::StopKeyWatch()
            # CapsLock を元のオン・オフに戻す (Copipe を止めた後なので、握りつぶされない)
            [System.Windows.Forms.Application]::DoEvents()
            if ($W::IsToggled(0x14) -ne $savedCapsLock) { $W::KeyDown(0x14); $W::KeyUp(0x14) }
            [void]$W::SetCursorPos($savedCursor.X, $savedCursor.Y)
            # 最小化したウインドウを元に戻す
            $shell.UndoMinimizeALL()
            # 利用者の設定と履歴を元に戻す
            if ($settingsBackup) {
                Copy-Item -LiteralPath $settingsBackup -Destination $settingsPath -Force
            } elseif (Test-Path -LiteralPath $settingsPath) {
                Remove-Item -LiteralPath $settingsPath -Force
            }
            $userHistory = [Copipe.Services.ClipboardHistory]::DefaultPath
            if ($historyBackup) {
                Copy-Item -LiteralPath $historyBackup -Destination $userHistory -Force
            } elseif (Test-Path -LiteralPath $userHistory) {
                Remove-Item -LiteralPath $userHistory -Force
            }
            if ($phrasesBackup) {
                Copy-Item -LiteralPath $phrasesBackup -Destination $phrasesPath -Force
            } elseif (Test-Path -LiteralPath $phrasesPath) {
                Remove-Item -LiteralPath $phrasesPath -Force
            }
            # 戻し終えたので、印を消す
            Remove-Item -LiteralPath $pendingMarker -Force -ErrorAction SilentlyContinue
        }
    }
} finally {
    # ==========================================================================
    # 後片付け
    # ==========================================================================
    try {
        if ($null -ne $clipBackup -and $clipBackup.Length -gt 0) {
            Set-ClipboardText ($clipBackup)
        }
    } catch {
        Write-Host ("クリップボードを戻せませんでした: " + $_.Exception.Message) -ForegroundColor Yellow
    }
    $ownerForm.Dispose()
    if (Test-Path -LiteralPath $tempDir) { Remove-Item -LiteralPath $tempDir -Recurse -Force }

    # 検証の前に Copipe が起動していたなら、起動し直しておく (利用者の設定と履歴は元に戻してある)
    if ($script:copipeWasRunning) {
        Start-Process -FilePath $exe
        Write-Host '検証の前に起動していた Copipe を、起動し直しました。' -ForegroundColor DarkCyan
    }
}

# ==============================================================================
Write-Host ''
if ($script:Fail -eq 0) {
    Write-Host ("結果: すべて成功 ({0} 件)" -f $script:Pass) -ForegroundColor Green
    exit 0
} else {
    Write-Host ("結果: 失敗 {0} 件 / 成功 {1} 件" -f $script:Fail, $script:Pass) -ForegroundColor Red
    exit 1
}
