using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Copipe.Model
{
    /// <summary>クリップボードから読み取った内容の種類。</summary>
    public enum ClipboardKind
    {
        Empty,
        Text,
        Files,
        Image,
        /// <summary>何かは入っているが、テキスト・ファイル・画像のどれでもない。</summary>
        Other,
        /// <summary>他のアプリが開いたままなどの理由で読み取れなかった。</summary>
        Unavailable
    }

    /// <summary>
    /// ある時点のクリップボードの内容。読み取った後は変更されない。
    /// Text は Kind == Text のときだけ、Files は Kind == Files のときだけ null 以外になる。
    /// </summary>
    public sealed class ClipboardSnapshot
    {
        public static readonly ClipboardSnapshot Empty = new ClipboardSnapshot(ClipboardKind.Empty, null, null);
        public static readonly ClipboardSnapshot Image = new ClipboardSnapshot(ClipboardKind.Image, null, null);
        public static readonly ClipboardSnapshot Other = new ClipboardSnapshot(ClipboardKind.Other, null, null);
        public static readonly ClipboardSnapshot Unavailable = new ClipboardSnapshot(ClipboardKind.Unavailable, null, null);

        private ClipboardSnapshot(ClipboardKind kind, string text, ReadOnlyCollection<string> files)
        {
            Kind = kind;
            Text = text;
            Files = files;
        }

        public ClipboardKind Kind { get; private set; }

        public string Text { get; private set; }

        public ReadOnlyCollection<string> Files { get; private set; }

        public static ClipboardSnapshot FromText(string text)
        {
            if (text == null)
            {
                throw new ArgumentNullException("text");
            }
            return new ClipboardSnapshot(ClipboardKind.Text, text, null);
        }

        public static ClipboardSnapshot FromFiles(IEnumerable<string> files)
        {
            if (files == null)
            {
                throw new ArgumentNullException("files");
            }
            return new ClipboardSnapshot(ClipboardKind.Files, null, new List<string>(files).AsReadOnly());
        }
    }
}
