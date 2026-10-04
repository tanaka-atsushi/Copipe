using System;
using System.Runtime.InteropServices;
using System.Threading;
using Copipe.Interop;

namespace Copipe.Services
{
    /// <summary>
    /// 決まったキー (CapsLock) だけを、低レベルのキーボードフック (WH_KEYBOARD_LL) で受け取って握りつぶす。
    /// RegisterHotKey で受け取っても、CapsLock は切り替わってしまう。
    /// (半角/全角はフックでも押し上げを感知できなかった (実機で確認) ので、対象にしない。)
    /// 押した・離したは、target のウインドウに PostMessage (PressedMessage・ReleasedMessage) で知らせる。
    ///
    /// フックは専用のスレッドで動かす。フックは全アプリのキー入力をこちらの返事まで待たせるので、
    /// UI スレッドが重い処理 (大量のファイルの読み取りなど) で止まっても、キー入力を遅らせないため。
    /// 返事が遅すぎると、Windows はフックを黙って外してしまう。
    /// </summary>
    internal sealed class KeyboardHook : IDisposable
    {
        /// <summary>キーを押したとき (押し続けても 1 回だけ)。</summary>
        public const int PressedMessage = NativeMethods.WM_APP + 1;

        /// <summary>PressedMessage の後、キーを離したとき。</summary>
        public const int ReleasedMessage = NativeMethods.WM_APP + 2;

        /// <summary>
        /// Copipe 自身が送ったキー入力の印 (dwExtraInfo)。この印の付いた入力は握りつぶさずに通す
        /// (CapsLock をオフに戻すため)。検証の擬似入力は印が無いので、本物のキーと同じに扱う。
        /// </summary>
        public static readonly IntPtr OwnInputMarker = new IntPtr(0x43504950); // "CPIP"

        private const int ReadyTimeoutMs = 3000;
        private const int StopTimeoutMs = 1000;
        private const int OwnInputTimeoutMs = 500;

        private readonly IntPtr _target;
        private readonly int[] _vks;
        // GC に回収されないよう、フックに渡したデリゲートを持っておく
        private readonly NativeMethods.LowLevelKeyboardProc _proc;
        private readonly Thread _thread;
        private readonly ManualResetEvent _ready = new ManualResetEvent(false);
        private readonly ManualResetEvent _ownKeyUpPassed = new ManualResetEvent(false);
        private IntPtr _hook;
        private uint _threadId;
        private volatile bool _disposed;
        // フックのスレッドだけが読み書きする。押し下げを握りつぶしたか (それなら押し上げも握りつぶす)
        private bool _swallowing;

        private KeyboardHook(IntPtr target, int[] vks)
        {
            _target = target;
            _vks = vks;
            _proc = HookCallback;
            _thread = new Thread(Run);
            _thread.IsBackground = true;
            _thread.Name = "Copipe keyboard hook";
        }

        /// <summary>
        /// フックを付ける。vks のどれかを押す・離すと target に知らせる。vks は 1 つのキーとして扱う。
        /// 付けられなければ null。
        /// </summary>
        public static KeyboardHook Start(IntPtr target, int[] vks)
        {
            KeyboardHook hook = new KeyboardHook(target, vks);
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
                if (IsTarget(k.vkCode) && k.dwExtraInfo == OwnInputMarker)
                {
                    // 自分で送った入力は通す。押し上げまで通したら、送り終えたと知らせる (TurnOffCapsLock)
                    if ((k.flags & NativeMethods.LLKHF_UP) != 0)
                    {
                        _ownKeyUpPassed.Set();
                    }
                }
                else if (IsTarget(k.vkCode))
                {
                    bool up = (k.flags & NativeMethods.LLKHF_UP) != 0;
                    if (!up)
                    {
                        if (!_swallowing)
                        {
                            // Ctrl・Shift・Alt・Windows キーと一緒なら通す (Shift+CapsLock (英語配列なら CapsLock、日本語入力を使っていると入力の切り替え) など。F1 のホットキーでも Ctrl+F1 は他のアプリに届くのと同じ)
                            if (IsModifierHeld())
                            {
                                return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
                            }
                            _swallowing = true;
                            NativeMethods.PostMessage(_target, PressedMessage, IntPtr.Zero, IntPtr.Zero);
                        }
                        // 押し続けたときの繰り返しも握りつぶす
                        return new IntPtr(1);
                    }
                    if (_swallowing)
                    {
                        // 押し下げを握りつぶしたときだけ、押し上げも握りつぶす (片方だけ届くと Windows のキーの状態がずれる)
                        _swallowing = false;
                        NativeMethods.PostMessage(_target, ReleasedMessage, IntPtr.Zero, IntPtr.Zero);
                        return new IntPtr(1);
                    }
                }
            }
            return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        private bool IsTarget(uint vk)
        {
            foreach (int target in _vks)
            {
                if (vk == (uint)target)
                {
                    return true;
                }
            }
            return false;
        }

        private static bool IsModifierHeld()
        {
            return IsDown(NativeMethods.VK_CONTROL) || IsDown(NativeMethods.VK_SHIFT) || IsDown(NativeMethods.VK_MENU) ||
                   IsDown(NativeMethods.VK_LWIN) || IsDown(NativeMethods.VK_RWIN);
        }

        private static bool IsDown(int vk)
        {
            return (NativeMethods.GetAsyncKeyState(vk) & 0x8000) != 0;
        }

        /// <summary>
        /// CapsLock がオンならオフに戻し、送った入力をこのフックが通し終えるまで待つ (最大 OwnInputTimeoutMs)。
        /// 握りつぶしている間は、CapsLock だけではオフに戻せないため。
        /// RegisterHotKey で CapsLock を登録する前に呼ぶこと。登録した後に送ると、入力がホットキーとして吸い込まれ、
        /// 入力先のアプリに届かないことがある (CapsLock がかかったままになった)。
        /// </summary>
        public void TurnOffCapsLock()
        {
            if ((NativeMethods.GetKeyState(NativeMethods.VK_CAPITAL) & 1) == 0)
            {
                return;
            }
            _ownKeyUpPassed.Reset();
            ushort scan = (ushort)NativeMethods.MapVirtualKey(NativeMethods.VK_CAPITAL, NativeMethods.MAPVK_VK_TO_VSC);
            NativeMethods.INPUT[] inputs = new NativeMethods.INPUT[2];
            for (int i = 0; i < inputs.Length; i++)
            {
                inputs[i].type = NativeMethods.INPUT_KEYBOARD;
                inputs[i].u.ki.wVk = (ushort)NativeMethods.VK_CAPITAL;
                inputs[i].u.ki.wScan = scan;
                inputs[i].u.ki.dwFlags = i == 0 ? 0 : NativeMethods.KEYEVENTF_KEYUP;
                inputs[i].u.ki.dwExtraInfo = OwnInputMarker;
            }
            if (NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(NativeMethods.INPUT))) == inputs.Length)
            {
                // SendInput は入力を Windows に預けるだけで、処理されるのは少し後。フックが押し上げを通すまで待つ
                _ownKeyUpPassed.WaitOne(OwnInputTimeoutMs);
            }
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
                _ownKeyUpPassed.Close();
            }
        }
    }
}
