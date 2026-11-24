/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : TaskChildEntry.cs
수정일 : 2026-11-23

# 설명
TaskGroup과 하위 Node 관계에서만 의미 있는 실행 정책을 보관한다.

# 특이사항, 제약사항
OncePassed는 Node 자체가 아닌 부모→자식 Entry의 영속 이력을 사용한다.
참여 시각 기록도 Entry 단위이며, 시간을 재도록 선언한 Entry만 영속 기록을 남긴다.
========================================================================= BLOCK_HEADER_END */

using inonego;
using inonego.Xeri;

namespace inonego.Xeri.Game.Objective
{
    // ============================================================
    /// <summary>
    /// Group 내부의 안정 식별자와 하위 Node 실행 조건을 정의한다.
    /// </summary>
    // ============================================================
    public sealed class TaskChildEntry
    {

    #region 프로퍼티

        // ------------------------------------------------------------
        /// <summary>
        /// 부모와 자식 관계의 고유 ID.
        /// </summary>
        // ------------------------------------------------------------
        public string EntryID { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// Entry가 가리키는 하나의 ChildNode.
        /// </summary>
        // ------------------------------------------------------------
        public TaskNodeSpec Node { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 참일 때만 Entry가 참여할 수 있는 선택 조건.
        /// </summary>
        // ------------------------------------------------------------
        public ICond<ObjectiveContext> EligibilityCond { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// Sequential 부모가 과거 통과를 인정하는 방식.
        /// </summary>
        // ------------------------------------------------------------
        public TaskRevalidationMode RevalidationMode { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// Sequential 후퇴 시 독점 로컬 Progress의 보존 여부.
        /// </summary>
        // ------------------------------------------------------------
        public TaskRollbackMode RollbackMode { get; }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/>참여를 시작한 시각을 영속 기록할지 지정한다.
        /// <br/>켜면 Condition이 ObjectiveContext.TryGetOpenElapsed로 Entry 단위 경과
        /// <br/>시간을 읽는다.
        /// <br/>시각은 Entry가 참여한 뒤에야 기록되므로 그 Entry와 하위 Entry의 시각은 그 Entry
        /// <br/>자신의 Eligibility에서 읽을 수 없다(Validator가 거부한다).
        /// <br/>하위 Node의 Condition이나 다른 Entry의 Eligibility에서 읽는다.
        /// </summary>
        // --------------------------------------------------------------------------------
        public bool TracksOpenTime { get; }

    #endregion

    #region 생성자

        // ------------------------------------------------------------
        /// <summary>
        /// 부모 Sequential에 필요한 재검증·후퇴 정책을 별도로 설정한다.
        /// </summary>
        // ------------------------------------------------------------
        public TaskChildEntry
        (
            string entryID, TaskNodeSpec node,
            ICond<ObjectiveContext> eligibilityCond = null,
            TaskRevalidationMode revalidationMode = TaskRevalidationMode.WhileValid,
            TaskRollbackMode rollbackMode = TaskRollbackMode.Preserve,
            bool tracksOpenTime = false
        )
        {
            EntryID = entryID;
            Node = node;
            EligibilityCond = eligibilityCond;
            RevalidationMode = revalidationMode;
            RollbackMode = rollbackMode;
            TracksOpenTime = tracksOpenTime;
        }

    #endregion

    }
}
