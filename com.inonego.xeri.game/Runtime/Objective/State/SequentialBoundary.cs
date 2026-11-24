/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : SequentialBoundary.cs
수정일 : 2026-11-24

# 설명
Sequential Group별 이전 개방 Entry를 기록하는 영속 이력 항목.

# 특이사항, 제약사항
Group/Entry 안정 ID의 마지막 실제 개방 지점이다. 순차 후퇴는 Engine이 명시 Refresh에서 판정한다.
========================================================================= BLOCK_HEADER_END */

using System;

using UnityEngine;

using inonego;
using inonego.Xeri;

namespace inonego.Xeri.Game.Objective
{
    // ============================================================
    /// <summary>
    /// 특정 Sequential Group에서 마지막으로 기록한 개방 Entry.
    /// </summary>
    // ============================================================
    [Serializable]
    public sealed class SequentialBoundary : IDeepCloneable<SequentialBoundary>
    {

    #region 프로퍼티

        // ------------------------------------------------------------
        /// <summary>
        /// Sequential Group의 안정 ID.
        /// </summary>
        // ------------------------------------------------------------
        public string GroupID => groupID;
        [SerializeField] private string groupID;

        // ------------------------------------------------------------
        /// <summary>
        /// 기록된 개방 ChildEntry의 안정 ID.
        /// </summary>
        // ------------------------------------------------------------
        public string EntryID => entryID;
        [SerializeField] private string entryID;

    #endregion

    #region 생성자

        internal SequentialBoundary(string groupID, string entryID)
        {
            this.groupID = groupID;
            this.entryID = entryID;
        }

    #endregion

    #region 변경

        internal void SetEntry(string value)
        {
            entryID = value;
        }

    #endregion

    #region 복제

        // Core 컬렉션 CloneFrom이 요소를 복제할 때 쓴다.
        SequentialBoundary IDeepCloneable<SequentialBoundary>.@new() => new(null, null);

        void IDeepCloneableFrom<SequentialBoundary>.CloneFrom(SequentialBoundary source)
        {
            groupID = source.groupID;
            entryID = source.entryID;
        }

    #endregion

    }
}
