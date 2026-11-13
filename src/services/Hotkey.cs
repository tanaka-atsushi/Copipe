using System;
using System.Windows.Forms;
using Copipe.Interop;
using Copipe.UI;

namespace Copipe.Services
{
    /// <summary>
    /// 小窓を出すホットキー。押したことを RegisterHotKey の WM_HOTKEY で受け取る。
    /// ただし CapsLock だけは、RegisterHotKey で受け取っても CapsLock が切り替わってしまうので、
    /// そのキーだけを低レベルのキーボードフック (KeyboardHook) で受け取って握りつぶす。
    /// UI スレッドで作り、UI スレッドで使うこと (イベントも UI スレッドで発生する)。
    /// </summary>
    internal sealed class Hotkey : NativeWindow, IDisposable
    {
        private const int HotkeyId = 1;

        private bool _registered;
        // CapsLock のときだけ。押したことはフックから届くので、WM_HOTKEY は使わない
        private KeyboardHook _keyHook;

        public Hotkey()
        {
            // 画面に出ないメッセージ専用ウインドウで WM_HOTKEY を受け取る
            CreateParams cp = new CreateParams();
            cp.Parent = NativeMethods.HWND_MESSAGE;
            CreateHandle(cp);
        }

        /// <summary>キーが押されたとき。押し続けても 1 回だけ発生する。</summary>
        public event EventHandler Pressed;

        /// <summary>
        /// 指定したキーで登録し直す。他のアプリ (または起動済みの Copipe) が同じキーを
        /// 登録していると失敗し、false を返す (このとき未登録の状態になる)。
        /// </summary>
        /// <param name="keys">キーと修飾キーの組み合わせ。例: Keys.F1、Keys.Control | Keys.Space。</param>
        public bool TryRegister(Keys keys)
        {
            Unregister();
            if ((keys & Keys.KeyCode) == Keys.None)
            {
                // キーが無ければ何も登録しない
                return true;
            }

            uint modifiers = NativeMethods.MOD_NOREPEAT;
            if ((keys & Keys.Control) == Keys.Control) { modifiers |= NativeMethods.MOD_CONTROL; }
            if ((keys & Keys.Shift) == Keys.Shift) { modifiers |= NativeMethods.MOD_SHIFT; }
            if ((keys & Keys.Alt) == Keys.Alt) { modifiers |= NativeMethods.MOD_ALT; }

            if (HotkeyText.UsesKeyboardHook(keys))
            {
                _keyHook = KeyboardHook.Start(Handle, HotkeyText.HookVirtualKeys(keys));
                if (_keyHook == null)
                {
                    return false;
                }
                if ((keys & Keys.KeyCode) == Keys.Capital)
                {
                    // 握りつぶしている間は CapsLock だけではオフに戻せないので、オンならオフにしておく
                    // (起動したとき・設定画面で CapsLock を押して取り込んだときなど)。
                    // 下の RegisterHotKey より先にする。登録した後だとオフにする入力がホットキーとして吸い込まれ、
                    // 入力先のアプリに届かずに CapsLock がかかったままになることがあった
                    _keyHook.TurnOffCapsLock();
                }
            }

            // フックで受け取るキーも登録はしておく。他のアプリが使っているキーを
            // 今までどおり「登録できなかった」と知らせるため (WM_HOTKEY はフックが先に握りつぶすので届かない)
            _registered = NativeMethods.RegisterHotKey(Handle, HotkeyId, modifiers, (uint)(keys & Keys.KeyCode));
            if (!_registered && _keyHook != null)
            {
                _keyHook.Dispose();
                _keyHook = null;
            }
            return _registered;
        }

        /// <summary>登録を解除する。</summary>
        public void Unregister()
        {
            if (_keyHook != null)
            {
                _keyHook.Dispose();
                _keyHook = null;
            }
            if (_registered)
            {
                NativeMethods.UnregisterHotKey(Handle, HotkeyId);
                _registered = false;
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == NativeMethods.WM_HOTKEY && m.WParam.ToInt32() == HotkeyId)
            {
                // フックで受け取っているときは、フックからの知らせだけを使う
                if (_keyHook == null)
                {
                    OnHotkey();
                }
                return;
            }
            if (m.Msg == KeyboardHook.PressedMessage)
            {
                // 解除した後に届いた古い知らせは無視する
                if (_keyHook != null)
                {
                    OnHotkey();
                }
                return;
            }
            base.WndProc(ref m);
        }

        /// <summary>
        /// NativeWindow の既定はウインドウプロシージャ内の例外を握りつぶす。
        /// それだと「押しても何も起きない」だけになって原因が分からないので、
        /// アプリの例外ハンドラー (Program の OnThreadException) に渡す。
        /// </summary>
        protected override void OnThreadException(Exception e)
        {
            Application.OnThreadException(e);
        }

        private void OnHotkey()
        {
            // Shift+英数 (CapsLock として届く) で押したとき、Shift を押したままにしない
            TextInserter.ReleaseShift();

            EventHandler handler = Pressed;
            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
        }

        public void Dispose()
        {
            Unregister();
            DestroyHandle();
        }
    }
}
