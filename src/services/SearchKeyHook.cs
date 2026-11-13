using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Copipe.Interop;

namespace Copipe.Services
{
    /// <summary>
    /// 押している間だけ出す設定の小窓で、ホットキーを押している間のキーを、低レベルのキーボードフック (WH_KEYBOARD_LL) で
    /// 先に受け取って握りつぶす (どのアプリが前面でも。小窓はフォーカスを取らないので、そのままではキーが入力中のアプリに入る)。
    /// 英字・数字・記号・空白・Backspace は target のウインドウに PostMessage (TypedMessage) で知らせ、そちらで検索窓に入れる
    /// (CharFor で文字にする)。Ctrl・Alt・Win と一緒のとき (Ctrl+A など) は知らせずに握りつぶすだけ。
    /// 修飾キーと PopupKeys のキー、ignored のキー (ホットキー・モードキー) は通す。
    ///
    /// 低レベルのフックは、後から付けたものが先に呼ばれるので、小窓を出すたびに付け、隠したら外す。
    /// フックは専用のスレッドで動かす (KeyboardHook と同じ。UI スレッドが止まっても、キー入力を遅らせないため)。
    /// </summary>
    internal sealed class SearchKeyHook : IDisposable
    {
        /// <summary>
        /// 文字のキーが押されたとき。英字・空白・Backspace は wParam が仮想キー、lParam は Shift を押していれば 1。
        /// 数字・記号は wParam が文字そのもので、lParam が LiteralChar。CharFor で文字にする。
        /// </summary>
        public const int TypedMessage = NativeMethods.WM_APP + 3;

        private const int ReadyTimeoutMs = 3000;
        private const int StopTimeoutMs = 1000;
        private const int VK_SHIFT = 0x10;
        private const int VK_MENU = 0x12;
        private const int VK_LWIN = 0x5B;
        private const int VK_RWIN = 0x5C;
        private const uint VK_SPACE = 0x20;
        private const uint VK_BACK = 0x08;
        // TypedMessage の lParam がこれなら、wParam は仮想キーではなく文字そのもの (数字・記号)
        private const int LiteralChar = 2;

        private readonly IntPtr _target;
        // 受け取らないキー (ホットキー・モードキー)。仮想キーの位置が true
        private readonly bool[] _ignored = new bool[256];
        // GC に回収されないよう、フックに渡したデリゲートを持っておく
        private readonly NativeMethods.LowLevelKeyboardProc _proc;
        private readonly Thread _thread;
        private readonly ManualResetEvent _ready = new ManualResetEvent(false);
        // フックのスレッドだけが読み書きする。押し下げを握りつぶしたキー (それなら押し上げも握りつぶす)
        private readonly bool[] _swallowed = new bool[256];
        private IntPtr _hook;
        private uint _threadId;
        private volatile bool _disposed;

        private SearchKeyHook(IntPtr target, Keys[] ignored)
        {
            _target = target;
            if (ignored != null)
            {
                foreach (Keys key in ignored)
                {
                    _ignored[(int)(key & Keys.KeyCode) & 0xFF] = true;
                }
            }
            _proc = HookCallback;
            _thread = new Thread(Run);
            _thread.IsBackground = true;
            _thread.Name = "Copipe search key hook";
        }

        /// <summary>フックを付ける。押されたキーを target に知らせる。ignored のキーは受け取らない。付けられなければ null。</summary>
        public static SearchKeyHook Start(IntPtr target, Keys[] ignored = null)
        {
            SearchKeyHook hook = new SearchKeyHook(target, ignored);
            hook._thread.Start();
            if (!hook._ready.WaitOne(ReadyTimeoutMs) || hook._hook == IntPtr.Zero)
            {
                hook.Dispose();
                return null;
            }
            return hook;
        }

        private void Run()
        {
            _threadId = NativeMethods.GetCurrentThreadId();
            // メッセージキューを先に作っておく (無いと、終わらせるための WM_QUIT を送れない)
            NativeMethods.MSG msg;
            NativeMethods.PeekMessage(out msg, IntPtr.Zero, 0, 0, NativeMethods.PM_NOREMOVE);
            _hook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, _proc, NativeMethods.GetModuleHandle(null), 0);
            _ready.Set();
            if (_hook == IntPtr.Zero)
            {
                return;
            }
            if (_disposed)
            {
                // 付け終わる前に、待ちきれずに Dispose された
                NativeMethods.UnhookWindowsHookEx(_hook);
                return;
            }

            // フックの呼び出しは、このスレッドがメッセージを待っている間に届く
            while (NativeMethods.GetMessage(out msg, IntPtr.Zero, 0, 0) > 0)
            {
            }
            NativeMethods.UnhookWindowsHookEx(_hook);
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                NativeMethods.KBDLLHOOKSTRUCT k = (NativeMethods.KBDLLHOOKSTRUCT)Marshal.PtrToStructure(
                    lParam, typeof(NativeMethods.KBDLLHOOKSTRUCT));
                uint vk = k.vkCode;
                // Copipe 自身が送った入力 (入力する定型文など) は通す
                if (vk >= 256 || _ignored[vk] || Passes(vk) || k.dwExtraInfo == KeyboardHook.OwnInputMarker)
                {
                    return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
                }
                if ((k.flags & NativeMethods.LLKHF_UP) == 0)
                {
                    // 押し続けたときの繰り返しも、そのたびに 1 文字として知らせる
                    _swallowed[vk] = true;
                    if (!IsDown(NativeMethods.VK_CONTROL) && !IsDown(VK_MENU) && !IsDown(VK_LWIN) && !IsDown(VK_RWIN))
                    {
                        Notify(vk, k.scanCode);
                    }
                    return new IntPtr(1);
                }
                if (_swallowed[vk])
                {
                    // 押し下げを握りつぶしたときだけ、押し上げも握りつぶす (片方だけ届くと Windows のキーの状態がずれる)
                    _swallowed[vk] = false;
                    return new IntPtr(1);
                }
            }
            return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        /// <summary>文字のキーなら target に知らせる。数字・記号は、前面のアプリのキーボード配列で文字にする。</summary>
        private void Notify(uint vk, uint scanCode)
        {
            bool shift = IsDown(VK_SHIFT);
            if ((vk >= 'A' && vk <= 'Z') || vk == VK_SPACE || vk == VK_BACK)
            {
                NativeMethods.PostMessage(_target, TypedMessage, new IntPtr((int)vk), new IntPtr(shift ? 1 : 0));
                return;
            }
            char c = PrintableChar(vk, scanCode, shift);
            if (c != '\0')
            {
                NativeMethods.PostMessage(_target, TypedMessage, new IntPtr(c), new IntPtr(LiteralChar));
            }
        }

        /// <summary>
        /// 握りつぶさずに通すキー。修飾キー (握りつぶすと、ホットキーを離したかが分からなくなる) と、
        /// PopupKeys がホットキーで受け取るキー (矢印・Enter・F2・Esc。フックで握りつぶすとホットキーにも届かない)。
        /// </summary>
        private static bool Passes(uint vk)
        {
            return (vk >= 0x10 && vk <= 0x12) || (vk >= 0xA0 && vk <= 0xA5) || vk == VK_LWIN || vk == VK_RWIN ||
                   (vk >= 0x25 && vk <= 0x28) || vk == 0x0D || vk == 0x71 || vk == 0x1B;
        }

        /// <summary>
        /// キーを、前面のアプリのキーボード配列と Shift の状態で文字にする。英数字・記号 (! から ~) でなければ '\0'。
        /// Windows のキーの状態は変えない (フラグ 4。日本語入力や dead key の途中の状態を崩さない)。
        /// </summary>
        private static char PrintableChar(uint vk, uint scanCode, bool shift)
        {
            byte[] state = new byte[256];
            if (shift)
            {
                state[VK_SHIFT] = 0x80;
            }
            uint thread = NativeMethods.GetWindowThreadProcessId(NativeMethods.GetForegroundWindow(), IntPtr.Zero);
            char[] buffer = new char[4];
            int count = NativeMethods.ToUnicodeEx(vk, scanCode, state, buffer, buffer.Length, 4, NativeMethods.GetKeyboardLayout(thread));
            return count == 1 && buffer[0] > ' ' && buffer[0] <= '~' ? buffer[0] : '\0';
        }

        private static bool IsDown(int vk)
        {
            return (NativeMethods.GetAsyncKeyState(vk) & 0x8000) != 0;
        }

        /// <summary>
        /// TypedMessage の wParam・lParam から、打った文字を決める。英字は Shift と CapsLock のどちらか一方なら大文字。
        /// CapsLock の状態を読むので、知らせを受けた UI スレッドで呼ぶこと。
        /// </summary>
        public static char CharFor(IntPtr wParam, IntPtr lParam)
        {
            int vk = wParam.ToInt32();
            if (lParam.ToInt32() == LiteralChar)
            {
                return (char)vk;
            }
            if (vk == (int)VK_SPACE)
            {
                return ' ';
            }
            if (vk == (int)VK_BACK)
            {
                return '\b';
            }
            char letter = (char)('a' + (vk - 'A'));
            bool shift = lParam.ToInt32() != 0;
            return shift != Control.IsKeyLocked(Keys.CapsLock) ? char.ToUpperInvariant(letter) : letter;
        }

        /// <summary>フックを外し、スレッドを終える。</summary>
        public void Dispose()
        {
            _disposed = true;
            if (_thread.IsAlive && _ready.WaitOne(0))
            {
                NativeMethods.PostThreadMessage(_threadId, NativeMethods.WM_QUIT, IntPtr.Zero, IntPtr.Zero);
                _thread.Join(StopTimeoutMs);
            }
            // 付け終わる前に待ちきれなかったときは、スレッドがまだ Set するので閉じない
            if (!_thread.IsAlive)
            {
                _ready.Close();
            }
        }
    }
}
