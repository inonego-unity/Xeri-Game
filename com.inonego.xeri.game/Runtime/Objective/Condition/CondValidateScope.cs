/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : CondValidateScope.cs
수정일 : 2026-11-24

# 설명
Condition 하나를 정적으로 검사할 때 그 Condition이 놓인 위치와 Spec 전체의 Entry 정보를 제공한다.

# 특이사항, 제약사항
Spec 트리를 모두 순회한 뒤에 만들어지므로 뒤에 선언된 Entry도 참조할 수 있다.
========================================================================= BLOCK_HEADER_END */

using System;
using System.Collections;
using System.Collections.Generic;

namespace inonego.Xeri.Game.Objective
{
    // ============================================================
    /// <summary>
    /// 정적 검사를 받는 Condition의 위치와 Entry 정보.
    /// </summary>
    // ============================================================
    internal sealed class CondValidateScope
    {

    #region 프로퍼티

        // ------------------------------------------------------------
        /// <summary>
        /// Spec 전체의 모든 EntryID.
        /// </summary>
        // ------------------------------------------------------------
        internal HashSet<string> EntryIDs { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 참여 시작 시각을 기록하도록 선언한 EntryID.
        /// </summary>
        // ------------------------------------------------------------
        internal HashSet<string> TimedEntryIDs { get; }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/>Condition이 Group의 Success/Failure이면 그 Group의 직접 자식 EntryID.
        /// <br/>아니면 비어 있다.
        /// </summary>
        // --------------------------------------------------------------------------------
        internal HashSet<string> ChildEntryIDs { get; }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/>Condition이 Group의 Success/Failure이면 그 Group의 직접 자식 Entry 수.
        /// <br/>ID가 겹치는 Entry도 센다.
        /// </summary>
        // --------------------------------------------------------------------------------
        internal int ChildCount { get; }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/>Condition이 Entry의 Eligibility이면 그 Entry와 모든 하위 Entry의 EntryID.
        /// <br/>아니면 비어 있다.
        /// </summary>
        // --------------------------------------------------------------------------------
        internal HashSet<string> OwnerEntryIDs { get; }

        private readonly string path;
        private readonly Action<string, string> report;

    #endregion

    #region 생성자

        internal CondValidateScope
        (
            string path, HashSet<string> entryIDs, HashSet<string> timedEntryIDs,
            HashSet<string> childEntryIDs, int childCount, HashSet<string> ownerEntryIDs, Action<string, string> report
        )
        {
            this.path = path;
            EntryIDs = entryIDs;
            TimedEntryIDs = timedEntryIDs;
            ChildEntryIDs = childEntryIDs;
            ChildCount = childCount;
            OwnerEntryIDs = ownerEntryIDs;
            this.report = report;
        }

    #endregion

    #region 오류 알림

        // ------------------------------------------------------------
        /// <summary>
        /// 이 위치의 경로를 붙여 오류를 알린다.
        /// </summary>
        // ------------------------------------------------------------
        internal void Add(string message) => report(path, message);

    #endregion

    }
}
