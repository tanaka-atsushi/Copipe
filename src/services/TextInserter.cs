using System;
using System.Collections.Generic;
using System.Media;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Copipe.Interop;

namespace Copipe.Services
{
    /// <summary>
    /// 前面のアプリのテキストカーソルの位置に、テキストを入力する。
    /// クリップボードに置いてから Ctrl+V を送る (CopyQ・Clibor と同じ方式)。
    /// 1 文字ずつ送る方式は、改行が Enter として届いてチャットが送信されてしまうなどの問題があるので使わない。
    /// 続けて頼まれたときは順番に 1 件ずつ入力する (数字キーを素早く続けて押したときなど)。
    /// UI スレッドで作り、UI スレッドで使うこと。
    /// </summary>
    internal sealed class TextInserter : IDisposable
    {
        /// <summary>
        /// クリップボードに置いてから Ctrl+V を送るまでの最初の待ち。
        /// 置いた直後は CopyQ などが読みに来てクリップボードを開くので (実測)、その間に Ctrl+V を送ると
        /// 貼り付け先のアプリが読めずに失敗することがある。
        /// </summary>
        private const int FirstWaitMs = 100;

        /// <summary>まだ他のアプリが開いていたときに、調べ直す間隔と、待つ上限。</summary>
        private const int PollIntervalMs = 30;
        private const int MaxWaitMs = 1000;

        /// <summary>
        /// Ctrl+V を送ってから、次の項目をクリップボードに置くまでの待ち。
        /// 貼り付け先は Ctrl+V を処理する時点でクリップボードを読むので、すぐに次の項目に置き換えると、
        /// 前の項目の代わりに次の項目が貼られてしまう。
        /// </summary>
        private const int AfterPasteMs = 200;

        private enum State
        {
            Idle,
            WaitingToPaste,
            AfterPaste
        }

        private readonly IntPtr _window;
        private readonly Timer _timer;
        private readonly Queue<string> _pending = new Queue<string>();
        private State _state = State.Idle;
        private int _waitedMs;

        /// <param name="window">クリップボードを開くときに使うこのプロセスのウインドウ。</param>
        public TextInserter(IntPtr window)
        {
            _window = window;
            _timer = new Timer();
            _timer.Tick += OnTimerTick;
        }

        /// <summary>入力を頼む。前の入力が終わっていなければ、その後に入力する。</summary>
        public void Insert(string text)
        {
            _pending.Enqueue(text);
            if (_state == State.Idle)
            {
                StartNext();
            }
        }

        private void StartNext()
        {
            while (_pending.Count > 0)
            {
                string text = _pending.Dequeue();
                if (ClipboardWriter.SetText(_window, text))
                {
                    _state = State.WaitingToPaste;
                    _waitedMs = 0;
                    _timer.Interval = FirstWaitMs;
                    _timer.Start();
                    return;
                }
                // クリップボードに置けなかった (他のアプリが開いたまま)。音で知らせて次へ
                SystemSounds.Beep.Play();
            }
            _state = State.Idle;
        }

        private void OnTimerTick(object sender, EventArgs e)
        {
            if (_state == State.AfterPaste)
            {
                _timer.Stop();
                StartNext();
                return;
            }

            _waitedMs += _timer.Interval;
            _timer.Interval = PollIntervalMs;

            // 他のアプリがまだ開いていれば少し待つ。上限を過ぎたら、そのまま送ってみる
            if (!IsClipboardFree() && _waitedMs < MaxWaitMs)
            {
                return;
            }

            SendPaste();
            _state = State.AfterPaste;
            _timer.Interval = AfterPasteMs;
        }

        private bool IsClipboardFree()
        {
            if (!NativeMethods.OpenClipboard(_window))
            {
                return false;
            }
            NativeMethods.CloseClipboard();
            return true;
        }

        /// <summary>
        /// Ctrl+V を送る。入力をまとめて送るので、利用者の入力が間に割り込まない。
        /// Shift が押されたままだと Ctrl+Shift+V として届いて貼り付けにならないので、先に Shift を離す
        /// (日本語キーボードの Shift+英数 は CapsLock として届くので、そのホットキーでは Shift を押したままになる)。
        /// 押し直しはしない。送る間に利用者が離していると、Shift が押されたままになってしまうため。
        /// </summary>
        private static void SendPaste()
        {
            List<NativeMethods.INPUT> inputs = new List<NativeMethods.INPUT>();
            foreach (ushort shift in new ushort[] { NativeMethods.VK_LSHIFT, NativeMethods.VK_RSHIFT })
            {
                if ((NativeMethods.GetAsyncKeyState(shift) & 0x8000) != 0)
                {
                    inputs.Add(Key(shift, true));
                }
            }
            inputs.Add(Key(NativeMethods.VK_CONTROL, false));
            inputs.Add(Key(NativeMethods.VK_V, false));
            inputs.Add(Key(NativeMethods.VK_V, true));
            inputs.Add(Key(NativeMethods.VK_CONTROL, true));
            uint sent = NativeMethods.SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf(typeof(NativeMethods.INPUT)));
            if (sent != inputs.Count)
            {
                // 管理者として実行中のアプリが前面にあると、UIPI で送れない
                SystemSounds.Beep.Play();
            }
        }

        private static NativeMethods.INPUT Key(ushort vk, bool up)
        {
            NativeMethods.INPUT input = new NativeMethods.INPUT();
            input.type = NativeMethods.INPUT_KEYBOARD;
            input.u.ki.wVk = vk;
            input.u.ki.wScan = (ushort)NativeMethods.MapVirtualKey(vk, NativeMethods.MAPVK_VK_TO_VSC);
            input.u.ki.dwFlags = up ? NativeMethods.KEYEVENTF_KEYUP : 0;
            return input;
        }

        public void Dispose()
        {
            _timer.Dispose();
        }
    }
}
