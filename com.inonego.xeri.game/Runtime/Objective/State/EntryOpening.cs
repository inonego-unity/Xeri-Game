/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : EntryOpening.cs
수정일 : 2026-11-24

# 설명
시간을 재는 Entry가 참여를 시작한 순간을 기록하는 영속 이력 항목.

# 특이사항, 제약사항
OpenTime은 그 순간 ObjectiveInstance의 ElapsedTime이다. Entry가 참여를 멈추면 Engine이 기록을 지운다.
========================================================================= BLOCK_HEADER_END */

using System;

using UnityEngine;

using inonego;
using inonego.Xeri;

namespace inonego.Xeri.Game.Objective
{
    // ============================================================
    /// <summary>
    /// 한 Entry가 참여를 시작한 실행 시각.
    /// </summary>
    // ============================================================
    [Serializable]
    public sealed class EntryOpening : IDeepCloneable<EntryOpening>
    {

    #region 프로퍼티

        // ------------------------------------------------------------
        /// <summary>
        /// 시간을 재는 ChildEntry의 안정 ID.
        /// </summary>
        // ------------------------------------------------------------
        public string EntryID => entryID;
        [SerializeField] private string entryID;

        // ------------------------------------------------------------
        /// <summary>
        /// Entry가 참여를 시작한 순간의 누적 경과 시간.
        /// </summary>
        // ------------------------------------------------------------
        public double OpenTime => openTime;
        [SerializeField] private double openTime;

    #endregion

    #region 생성자

        internal EntryOpening(string entryID, double openTime)
        {
            this.entryID = entryID;
            this.openTime = openTime;
        }

    #endregion

    #region 복제

        // Core 컬렉션 CloneFrom이 요소를 복제할 때 쓴다.
        EntryOpening IDeepCloneable<EntryOpening>.@new() => new(null, 0);

        void IDeepCloneableFrom<EntryOpening>.CloneFrom(EntryOpening source)
        {
            entryID = source.entryID;
            openTime = source.openTime;
        }

    #endregion

    }
}
