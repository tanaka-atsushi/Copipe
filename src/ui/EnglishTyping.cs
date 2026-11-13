using System;
using System.Drawing;
using System.Windows.Forms;
using Copipe.Interop;
using Copipe.Services;

namespace Copipe.UI
{
    /// <summary>
    /// 英字の名前を打つ欄 (小窓の検索窓、定型文の表示名・グループ名) で、日本語入力や Meltype などの入力ソフトが
    /// オンでも英字が入るようにする。キーボードフック (SearchKeyHook) で英字・空白を先に受け取り、Typed で欄に入れてもらう。
    ///
    /// 日本語を打ちたいときは、半角/全角 キーか、欄の右の印 (Indicator) のクリックで「普通の入力」に切り替える
    /// (印は、英字に固定なら A、普通の入力なら あ)。開くたびに英字に固定から始める (Start)。
    /// フックの知らせを受けるため、メッセージ専用のウインドウを持つ。
    /// </summary>
    internal sealed class EnglishTyping : NativeWindow, IDisposable
    {
        private readonly TextBox _box;
        private readonly Label _indicator;
        private readonly ToolTip _toolTip = new ToolTip();
        private SearchKeyHook _hook;
        private bool _japanese;

        public EnglishTyping(TextBox box)
        {
            _box = box;
            CreateParams cp = new CreateParams();
            cp.Parent = NativeMethods.HWND_MESSAGE;
            CreateHandle(cp);

            _indicator = new Label();
            _indicator.Name = "inputModeIndicator";
            _indicator.AutoSize = false;
            _indicator.TextAlign = ContentAlignment.MiddleCenter;
            _indicator.BorderStyle = BorderStyle.FixedSingle;
            _indicator.Cursor = Cursors.Hand;
            _indicator.UseMnemonic = false;
            _indicator.Click += delegate { Japanese = !Japanese; };
            _toolTip.SetToolTip(_indicator, Lang.T("半角/全角 キー (またはクリック) で、英字 (A) と日本語 (あ) を切り替えます",
                                                   "Press Hankaku/Zenkaku (or click) to switch between letters (A) and Japanese input (あ)"));
            ShowMode();
        }

        /// <summary>今の入力のしかたを示す印 (A・あ)。欄の右に置いてもらう。クリックでも切り替わる。</summary>
        public Label Indicator
        {
            get { return _indicator; }
        }

        /// <summary>
        /// フックが受け取った英字・空白 (Shift か CapsLock なら大文字) を欄に入れる処理。
        /// 置かなければ、欄に入力カーソルがあるときに、選んでいる部分を置き換えて入れる。
        /// </summary>
        public Action<char> Typed { get; set; }

        /// <summary>普通の入力 (日本語も打てる) にしているか。false なら英字に固定。</summary>
        public bool Japanese
        {
            get { return _japanese; }
            set
            {
                _japanese = value;
                if (_hook != null)
                {
                    _hook.PassThrough = value;
                }
                ShowMode();
                EventHandler handler = ModeChanged;
                if (handler != null)
                {
                    handler(this, EventArgs.Empty);
                }
            }
        }

        /// <summary>英字に固定 ⇔ 普通の入力 を切り替えたとき。</summary>
        public event EventHandler ModeChanged;

        /// <summary>
        /// 受け取り始める (英字に固定から)。window が前面の間だけ受け取る。focus を渡したら、その部品に入力カーソルがあるときだけ。
        /// フックを付けられなかったときは、キーは今までどおり欄に届く。
        /// </summary>
        public void Start(IntPtr window, IntPtr focus)
        {
            Stop();
            _hook = SearchKeyHook.Start(Handle, window, focus);
            Japanese = false;
        }

        /// <summary>受け取りをやめる (フックを外す)。</summary>
        public void Stop()
        {
            if (_hook != null)
            {
                _hook.Dispose();
                _hook = null;
            }
        }

        private void ShowMode()
        {
            _indicator.Text = _japanese ? "あ" : "A";
            _indicator.ForeColor = _japanese ? SystemColors.HotTrack : SystemColors.GrayText;
            // 英字に固定の間は IME も止める (英字・空白以外のキーで、かなの入力が始まらないように)。
            // 普通の入力では IME をオンにして、すぐ日本語を打てるようにする
            _box.ImeMode = _japanese ? ImeMode.Hiragana : ImeMode.Disable;
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == SearchKeyHook.TypedMessage)
            {
                char c = SearchKeyHook.CharFor(m.WParam, m.LParam);
                Action<char> typed = Typed;
                if (typed != null)
                {
                    typed(c);
                }
                else if (_box.Focused)
                {
                    _box.SelectedText = c.ToString();
                }
                return;
            }
            if (m.Msg == SearchKeyHook.ModeMessage)
            {
                // フックは自分の側を切り替え済み。印などをそろえる
                Japanese = m.LParam.ToInt32() != 0;
                return;
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
            Stop();
            _toolTip.Dispose();
            DestroyHandle();
        }
    }
}
