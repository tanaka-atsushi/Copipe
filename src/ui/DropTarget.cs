namespace Copipe.UI
{
    /// <summary>ドラッグした項目を落とす先の種類。</summary>
    internal enum DropKind
    {
        /// <summary>落とせる先ではない (小窓の外など)。</summary>
        None,
        /// <summary>一覧の行と入れ替える (空きなら移動)。Index は行の位置。</summary>
        Swap,
        /// <summary>グループの行の中央。そのグループに入れる。Index は行の位置。</summary>
        Into,
        /// <summary>見出しの階層名。その階層に出す。Index は階層 (0 が一番上)。</summary>
        Level
    }

    /// <summary>ドラッグした項目を落とす先。</summary>
    internal struct DropTarget
    {
        public static readonly DropTarget Nowhere = new DropTarget(DropKind.None, -1);

        public DropTarget(DropKind kind, int index)
        {
            _kind = kind;
            _index = index;
        }

        private readonly DropKind _kind;
        private readonly int _index;

        public DropKind Kind
        {
            get { return _kind; }
        }

        public int Index
        {
            get { return _index; }
        }

        public bool Equals(DropTarget other)
        {
            return _kind == other._kind && _index == other._index;
        }
    }
}
