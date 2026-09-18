using System;
using System.Windows.Forms;
using Copipe.Interop;
using Copipe.UI;

namespace Copipe.Services
{
    /// <summary>
    /// 小窓を出している間だけ、数字キー (上段・テンキー)・モードキー・Esc (定型文モードの間) をホットキーとして登録して受け取る。
    /// 小窓はフォーカスを奪わないので、登録しないとキーは入力中のアプリに届いてしまう。
    /// 登録している間はこれらのキーがどのアプリにも届かないので、小窓を消すときに必ず Disable すること。
    /// (Copipe が落ちた場合は、ウインドウが消えるときに Windows が登録を解除する)
    /// UI スレッドで作り、UI スレッドで使うこと。
    /// </summary>
    internal sealed class PopupKeys : NativeWindow, IDisposable
    {
        // ホットキーの ID。HoldHotkey (1) と重ならないようにする
        private const int FirstId = 100;
        private const int ModeKeyId = 200;
        private const int EscapeId = 201;

        private bool _enabled;
        private bool _escapeEnabled;
        private uint _mod;

        public PopupKeys()
        {
            CreateParams cp = new CreateParams();
            cp.Parent = NativeMethods.HWND_MESSAGE;
            CreateHandle(cp);
        }

        /// <summary>数字キーが押されたとき。引数は一覧の位置 (0 始まり。1 のキーなら 0、0 のキーなら 9)。</summary>
        public event Action<int> NumberPressed;

        /// <summary>モードキーが押されたとき。押し続けても 1 回だけ届く。</summary>
        public event Action ModePressed;

        /// <summary>Esc が押されたとき (SetEscapeEnabled(true) にしている間だけ)。</summary>
        public event Action EscapePressed;

        /// <summary>
        /// 数字キーとモードキーを登録する。modifiers には、押し続けているホットキーの修飾キーを渡す
        /// (Ctrl+Space を押したままなら、数字は Ctrl+1 として届くため)。
        /// 他のアプリが使っているキーは登録できないので、そのキーだけ効かない。
        /// modeKey が Keys.None なら、モードキーは登録しない。
        /// </summary>
        public void Enable(Keys modifiers, Keys modeKey)
        {
            Disable();

            uint mod = NativeMethods.MOD_NOREPEAT;
            if ((modifiers & Keys.Control) == Keys.Control) { mod |= NativeMethods.MOD_CONTROL; }
            if ((modifiers & Keys.Shift) == Keys.Shift) { mod |= NativeMethods.MOD_SHIFT; }
            if ((modifiers & Keys.Alt) == Keys.Alt) { mod |= NativeMethods.MOD_ALT; }

            for (int i = 0; i < ItemNumber.AllKeys.Count; i++)
            {
                NativeMethods.RegisterHotKey(Handle, FirstId + i, mod, (uint)ItemNumber.AllKeys[i]);
            }
            if ((modeKey & Keys.KeyCode) != Keys.None)
            {
                NativeMethods.RegisterHotKey(Handle, ModeKeyId, mod, (uint)(modeKey & Keys.KeyCode));
            }
            _mod = mod;
            _enabled = true;
        }

        /// <summary>
        /// Esc も受け取るかを切り替える (定型文モードで 1 つ上の階層に戻るため)。
        /// 履歴モードの間は受け取らず、入力中のアプリに届くようにする。Enable の後で呼ぶこと。
        /// </summary>
        public void SetEscapeEnabled(bool enabled)
        {
            if (!_enabled || enabled == _escapeEnabled)
            {
                return;
            }
            if (enabled)
            {
                NativeMethods.RegisterHotKey(Handle, EscapeId, _mod, (uint)Keys.Escape);
            }
            else
            {
                NativeMethods.UnregisterHotKey(Handle, EscapeId);
            }
            _escapeEnabled = enabled;
        }

        /// <summary>登録を解除する。登録していなくても呼んでよい。</summary>
        public void Disable()
        {
            if (!_enabled)
            {
                return;
            }
            for (int i = 0; i < ItemNumber.AllKeys.Count; i++)
            {
                NativeMethods.UnregisterHotKey(Handle, FirstId + i);
            }
            NativeMethods.UnregisterHotKey(Handle, ModeKeyId);
            NativeMethods.UnregisterHotKey(Handle, EscapeId);
            _escapeEnabled = false;
            _enabled = false;
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == NativeMethods.WM_HOTKEY)
            {
                int id = m.WParam.ToInt32();
                if (id == ModeKeyId)
                {
                    Action mode = ModePressed;
                    if (mode != null)
                    {
                        mode();
                    }
                    return;
                }
                if (id == EscapeId)
                {
                    Action escape = EscapePressed;
                    if (escape != null)
                    {
                        escape();
                    }
                    return;
                }
                int i = id - FirstId;
                if (i >= 0 && i < ItemNumber.AllKeys.Count)
                {
                    Action<int> handler = NumberPressed;
                    if (handler != null)
                    {
                        handler(ItemNumber.IndexFromKey(ItemNumber.AllKeys[i]));
                    }
                    return;
                }
            }
            base.WndProc(ref m);
        }

        /// <summary>
        /// NativeWindow の既定はウインドウプロシージャ内の例外を握りつぶすので、
        /// アプリの例外ハンドラー (Program の OnThreadException) に渡す。
        /// </summary>
        protected override void OnThreadException(Exception e)
        {
            Application.OnThreadException(e);
        }

        public void Dispose()
        {
            Disable();
            DestroyHandle();
        }
    }
}
