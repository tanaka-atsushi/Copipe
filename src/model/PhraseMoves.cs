namespace Copipe.Model
{
    /// <summary>
    /// 定型文のドラッグ＆ドロップでの並べ替え (入れ替え・グループに入れる)。
    /// グループが自分の中に入って入れ子が輪になる動きは、できないようにする。
    /// </summary>
    public static class PhraseMoves
    {
        /// <summary>node が target 自身か、target を中に含むグループなら true。</summary>
        public static bool Contains(PhraseNode node, PhraseNode target)
        {
            if (node == null || target == null)
            {
                return false;
            }
            if (node == target)
            {
                return true;
            }
            if (!node.IsGroup)
            {
                return false;
            }
            foreach (PhraseNode child in node.Slots)
            {
                if (child != null && Contains(child, target))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>グループの最初の空きの枠。空きが無ければ -1。</summary>
        public static int FirstEmptySlot(PhraseNode group)
        {
            for (int i = 0; i < group.Slots.Length; i++)
            {
                if (group.Slots[i] == null)
                {
                    return i;
                }
            }
            return -1;
        }

        /// <summary>
        /// fromGroup の fromIndex 番目と、toGroup の toIndex 番目を入れ替えられるか (相手が空きなら移動)。
        /// 別の階層どうしでもよい。
        /// </summary>
        public static bool CanSwap(PhraseNode fromGroup, int fromIndex, PhraseNode toGroup, int toIndex)
        {
            PhraseNode node = SlotAt(fromGroup, fromIndex);
            if (node == null || !IsSlot(toGroup, toIndex))
            {
                return false;
            }
            if (fromGroup == toGroup && fromIndex == toIndex)
            {
                return false;
            }
            // 動かす項目が、移り先の階層を中に含むなら輪になる
            if (Contains(node, toGroup))
            {
                return false;
            }
            // 入れ替えの相手が、元の階層を中に含むなら輪になる
            PhraseNode other = toGroup.Slots[toIndex];
            return !Contains(other, fromGroup);
        }

        /// <summary>入れ替える (相手が空きなら移動)。できなければ何もせず false。</summary>
        public static bool Swap(PhraseNode fromGroup, int fromIndex, PhraseNode toGroup, int toIndex)
        {
            if (!CanSwap(fromGroup, fromIndex, toGroup, toIndex))
            {
                return false;
            }
            PhraseNode node = fromGroup.Slots[fromIndex];
            fromGroup.Slots[fromIndex] = toGroup.Slots[toIndex];
            toGroup.Slots[toIndex] = node;
            return true;
        }

        /// <summary>
        /// fromGroup の fromIndex 番目を、グループ target の最初の空きに入れられるか。
        /// 今いる階層・自分自身・自分の中のグループ・空きの無いグループには入れない。
        /// </summary>
        public static bool CanMoveInto(PhraseNode fromGroup, int fromIndex, PhraseNode target)
        {
            PhraseNode node = SlotAt(fromGroup, fromIndex);
            if (node == null || target == null || !target.IsGroup || target == fromGroup)
            {
                return false;
            }
            if (Contains(node, target))
            {
                return false;
            }
            return FirstEmptySlot(target) >= 0;
        }

        /// <summary>グループ target の最初の空きに入れる。できなければ何もせず false。</summary>
        public static bool MoveInto(PhraseNode fromGroup, int fromIndex, PhraseNode target)
        {
            if (!CanMoveInto(fromGroup, fromIndex, target))
            {
                return false;
            }
            target.Slots[FirstEmptySlot(target)] = fromGroup.Slots[fromIndex];
            fromGroup.Slots[fromIndex] = null;
            return true;
        }

        private static bool IsSlot(PhraseNode group, int index)
        {
            return group != null && group.IsGroup && index >= 0 && index < group.Slots.Length;
        }

        private static PhraseNode SlotAt(PhraseNode group, int index)
        {
            return IsSlot(group, index) ? group.Slots[index] : null;
        }
    }
}
