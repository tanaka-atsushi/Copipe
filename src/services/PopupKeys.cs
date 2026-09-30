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
        private static readonly Keys[] ArrowKeys = { Keys.Up, Keys.Down, Keys.Left, Keys.Right, Keys.Return };
        private const int ArrowId = 210; // ArrowId + ArrowKeys の位置
        // ダブルタップのキーを押したまま (Ctrl+1 など) でも受け取るための登録は、ID にこれを足す
        private const int HeldOffset = 1000;

        private bool _enabled;
        private bool _escapeEnabled;
        private uint _heldMod;

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

        /// <summary>矢印キー (Up・Down・Left・Right) と Enter (Return) が押されたとき。</summary>
        public event Action<Keys> ArrowPressed;

        /// <summary>
        /// 数字キーとモードキーを登録する (修飾キーなし)。
        /// ダブルタップで小窓を出したときは、そのキー (heldModifier: ControlKey・ShiftKey・Menu) を押したままなので、
        /// 数字は Ctrl+1 などとして届く。そのキー付きでも登録する。
        /// 他のアプリや Windows が使っているキー (Alt+Tab など) は登録できないので、そのキーだけ効かない。
        /// modeKey が Keys.None なら、モードキーは登録しない。
        /// </summary>
        public void Enable(Keys modeKey, Keys heldModifier)
        {
            Disable();
            _enabled = true;
            _heldMod = ModifierFlag(heldModifier);

            for (int i = 0; i < ItemNumber.AllKeys.Count; i++)
            {
                Register(FirstId + i, (uint)ItemNumber.AllKeys[i]);
            }
            if ((modeKey & Keys.KeyCode) != Keys.None)
            {
                Register(ModeKeyId, (uint)(modeKey & Keys.KeyCode));
            }
            for (int i = 0; i < ArrowKeys.Length; i++)
            {
                Register(ArrowId + i, (uint)ArrowKeys[i]);
            }
        }

        private static uint ModifierFlag(Keys heldModifier)
        {
            switch (HotkeyText.NormalizeModifier(heldModifier))
            {
                case Keys.ControlKey:
                    return NativeMethods.MOD_CONTROL;
                case Keys.ShiftKey:
                    return NativeMethods.MOD_SHIFT;
                case Keys.Menu:
                    return NativeMethods.MOD_ALT;
                default:
                    return 0;
            }
        }

        private void Register(int id, uint vk)
        {
            NativeMethods.RegisterHotKey(Handle, id, NativeMethods.MOD_NOREPEAT, vk);
            if (_heldMod != 0)
            {
                NativeMethods.RegisterHotKey(Handle, id + HeldOffset, NativeMethods.MOD_NOREPEAT | _heldMod, vk);
            }
        }

        private void Unregister(int id)
        {
            NativeMethods.UnregisterHotKey(Handle, id);
            NativeMethods.UnregisterHotKey(Handle, id + HeldOffset);
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
                Register(EscapeId, (uint)Keys.Escape);
            }
            else
            {
                Unregister(EscapeId);
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
                Unregister(FirstId + i);
            }
            Unregister(ModeKeyId);
            Unregister(EscapeId);
            for (int i = 0; i < ArrowKeys.Length; i++)
            {
                Unregister(ArrowId + i);
            }
            _escapeEnabled = false;
            _enabled = false;
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == NativeMethods.WM_HOTKEY)
            {
                int id = m.WParam.ToInt32();
                if (id >= HeldOffset)
                {
                    // ダブルタップのキーを押したまま押された。修飾キーなしと同じに扱う
                    id -= HeldOffset;
                }
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
                int arrow = id - ArrowId;
                if (arrow >= 0 && arrow < ArrowKeys.Length)
                {
                    Action<Keys> arrowHandler = ArrowPressed;
                    if (arrowHandler != null)
                    {
                        arrowHandler(ArrowKeys[arrow]);
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
