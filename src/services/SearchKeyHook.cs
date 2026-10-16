using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Copipe.Interop;

namespace Copipe.Services
{
    /// <summary>
    /// 英字の名前を打つ欄 (小窓の検索窓、定型文・グループの名前の欄) に打つ英字 (A〜Z) と空白を、
    /// 低レベルのキーボードフック (WH_KEYBOARD_LL) で先に受け取って握りつぶす。
    /// 受け取ったキーは target のウインドウに PostMessage (TypedMessage) で知らせ、そちらで欄に入れる (CharFor で文字にする)。
    ///
    /// 日本語入力 (IME) や、Meltype のようにキーボードフックでキーを受け取ってかなにする入力ソフトに、
    /// キーが届く前に受け取るため (届くと、かなが入る。ユーザーの報告)。低レベルのフックは、後から付けたものが先に呼ばれるので、
    /// 小窓やダイアログを出すたびに付け、閉じたら外す。
    /// window が前面のときだけ (focus を渡したときは、さらにその部品に入力カーソルがあるときだけ) 受け取る。
    /// Ctrl・Alt・Win と一緒のとき (Ctrl+A など) は受け取らない。
    ///
    /// 日本語を打ちたいときのため、半角/全角 キーで「英字に固定」と「普通の入力」(PassThrough。英字を受け取らず、
    /// IME や入力ソフトに届ける) を切り替える。切り替えたら target に ModeMessage で知らせる。
    /// 半角/全角 キーも握りつぶす (IME に届くと IME のオン・オフも変わり、普通の入力にしたのに英字になることがあるため)。
    /// Ctrl などと一緒のとき (Meltype の一時停止の Ctrl+半角/全角 など) は受け取らない。
    ///
    /// フックは専用のスレッドで動かす (KeyboardHook と同じ。UI スレッドが止まっても、キー入力を遅らせないため)。
    /// </summary>
    internal sealed class SearchKeyHook : IDisposable
    {
        /// <summary>英字か空白が押されたとき。wParam は仮想キー、lParam は Shift を押していれば 1。</summary>
        public const int TypedMessage = NativeMethods.WM_APP + 3;

        /// <summary>半角/全角 キーで切り替えたとき。lParam は普通の入力 (PassThrough) にしたら 1、英字に固定したら 0。</summary>
        public const int ModeMessage = NativeMethods.WM_APP + 4;

        private const int ReadyTimeoutMs = 3000;
        private const int StopTimeoutMs = 1000;
        private const int VK_SHIFT = 0x10;
        private const int VK_MENU = 0x12;
        private const int VK_LWIN = 0x5B;
        private const int VK_RWIN = 0x5C;
        private const uint VK_SPACE = 0x20;
        // 半角/全角 キー。日本語キーボードでは押すたびに 0xF3 と 0xF4 が交互に届く。VK_KANJI (0x19) も同じに扱う
        private const uint VK_OEM_AUTO = 0xF3;
        private const uint VK_OEM_ENLW = 0xF4;
        private const uint VK_KANJI = 0x19;

        private readonly IntPtr _target;
        private readonly IntPtr _window;
        private readonly IntPtr _focus;
        // GC に回収されないよう、フックに渡したデリゲートを持っておく
        private readonly NativeMethods.LowLevelKeyboardProc _proc;
        private readonly Thread _thread;
        private readonly ManualResetEvent _ready = new ManualResetEvent(false);
        // フックのスレッドだけが読み書きする。押し下げを握りつぶしたキー (それなら押し上げも握りつぶす)
        private readonly bool[] _swallowed = new bool[256];
        private IntPtr _hook;
        private uint _threadId;
        private volatile bool _disposed;
        private volatile bool _passThrough;

        private SearchKeyHook(IntPtr target, IntPtr window, IntPtr focus)
        {
            _target = target;
            _window = window;
            _focus = focus;
            _proc = HookCallback;
            _thread = new Thread(Run);
            _thread.IsBackground = true;
            _thread.Name = "Copipe search key hook";
        }

        /// <summary>
        /// フックを付ける。window が前面の間に押された英字・空白を target に知らせる。
        /// focus が IntPtr.Zero でなければ、その部品に入力カーソルがあるときだけ受け取る。付けられなければ null。
        /// </summary>
        public static SearchKeyHook Start(IntPtr target, IntPtr window, IntPtr focus)
        {
            SearchKeyHook hook = new SearchKeyHook(target, window, focus);
            hook._thread.Start();
            if (!hook._ready.WaitOne(ReadyTimeoutMs) || hook._hook == IntPtr.Zero)
            {
                hook.Dispose();
                return null;
            }
            return hook;
        }

        /// <summary>
        /// 普通の入力にしているか (英字を受け取らず、IME や入力ソフトに届ける)。false なら英字に固定。
        /// UI のスレッドから変えてよい (フックのスレッドは読むだけ。半角/全角 キーのときは書き換えて知らせる)。
        /// </summary>
        public bool PassThrough
        {
            get { return _passThrough; }
            set { _passThrough = value; }
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
                if ((vk == VK_OEM_AUTO || vk == VK_OEM_ENLW || vk == VK_KANJI) && (k.flags & NativeMethods.LLKHF_UP) == 0 &&
                    k.dwExtraInfo != KeyboardHook.OwnInputMarker && !IsDown(NativeMethods.VK_CONTROL) && !IsDown(VK_SHIFT) &&
                    !IsDown(VK_MENU) && !IsDown(VK_LWIN) && !IsDown(VK_RWIN) && IsTargetFocused())
                {
                    _passThrough = !_passThrough;
                    NativeMethods.PostMessage(_target, ModeMessage, IntPtr.Zero, new IntPtr(_passThrough ? 1 : 0));
                    return new IntPtr(1);
                }
                if (((vk >= 'A' && vk <= 'Z') || vk == VK_SPACE) && !_passThrough)
                {
                    if ((k.flags & NativeMethods.LLKHF_UP) == 0)
                    {
                        // Copipe 自身が送った入力 (入力する定型文など) は通す
                        if (k.dwExtraInfo != KeyboardHook.OwnInputMarker && !IsDown(NativeMethods.VK_CONTROL) &&
                            !IsDown(VK_MENU) && !IsDown(VK_LWIN) && !IsDown(VK_RWIN) && IsTargetFocused())
                        {
                            // 押し続けたときの繰り返しも、そのたびに 1 文字として知らせる
                            _swallowed[vk] = true;
                            NativeMethods.PostMessage(_target, TypedMessage, new IntPtr((int)vk), new IntPtr(IsDown(VK_SHIFT) ? 1 : 0));
                            return new IntPtr(1);
                        }
                    }
                    else if (_swallowed[vk])
                    {
                        // 押し下げを握りつぶしたときだけ、押し上げも握りつぶす (片方だけ届くと Windows のキーの状態がずれる)
                        _swallowed[vk] = false;
                        return new IntPtr(1);
                    }
                }
            }
            return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        private static bool IsDown(int vk)
        {
            return (NativeMethods.GetAsyncKeyState(vk) & 0x8000) != 0;
        }

        /// <summary>window が前面で、focus を渡したときは入力カーソルがその部品にあるか。</summary>
        private bool IsTargetFocused()
        {
            if (NativeMethods.GetForegroundWindow() != _window)
            {
                return false;
            }
            if (_focus == IntPtr.Zero)
            {
                return true;
            }
            NativeMethods.GUITHREADINFO info = new NativeMethods.GUITHREADINFO();
            info.cbSize = Marshal.SizeOf(typeof(NativeMethods.GUITHREADINFO));
            return NativeMethods.GetGUIThreadInfo(0, ref info) && info.hwndFocus == _focus;
        }

        /// <summary>
        /// TypedMessage の wParam・lParam から、打った文字を決める。Shift と CapsLock のどちらか一方なら大文字。
        /// CapsLock の状態を読むので、知らせを受けた UI スレッドで呼ぶこと。
        /// </summary>
        public static char CharFor(IntPtr wParam, IntPtr lParam)
        {
            int vk = wParam.ToInt32();
            if (vk == (int)VK_SPACE)
            {
                return ' ';
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
