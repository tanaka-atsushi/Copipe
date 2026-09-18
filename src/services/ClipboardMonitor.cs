using System;
using System.Windows.Forms;
using Copipe.Interop;

namespace Copipe.Services
{
    /// <summary>
    /// クリップボードが変わったことを知らせる。ポーリングではなく Windows からの通知
    /// (WM_CLIPBOARDUPDATE) を受けるので、待っている間の負荷はほぼ無い。
    /// UI スレッドで作ること (Changed も UI スレッドで発生する)。
    /// </summary>
    internal sealed class ClipboardMonitor : NativeWindow, IDisposable
    {
        private bool _listening;

        public ClipboardMonitor()
        {
            // 画面に出ないメッセージ専用ウインドウで通知を受ける
            CreateParams cp = new CreateParams();
            cp.Parent = NativeMethods.HWND_MESSAGE;
            CreateHandle(cp);
        }

        /// <summary>クリップボードが変わったとき。</summary>
        public event EventHandler Changed;

        /// <summary>通知の受け取りを始める。失敗したら false (履歴が貯まらなくなる)。</summary>
        public bool Start()
        {
            _listening = NativeMethods.AddClipboardFormatListener(Handle);
            return _listening;
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == NativeMethods.WM_CLIPBOARDUPDATE)
            {
                EventHandler handler = Changed;
                if (handler != null)
                {
                    handler(this, EventArgs.Empty);
                }
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
            if (_listening)
            {
                NativeMethods.RemoveClipboardFormatListener(Handle);
                _listening = false;
            }
            DestroyHandle();
        }
    }
}
