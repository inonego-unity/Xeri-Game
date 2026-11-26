/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : TaskNodeSpec.cs
수정일 : 2026-11-25

# 설명
Objective의 단일 Task와 재귀 TaskGroup 정적 실행 정의를 제공한다.

# 특이사항, 제약사항
Condition은 외부에서 제공받는 읽기 전용 ICond이며 실행 상태는 저장하지 않는다.
========================================================================= BLOCK_HEADER_END */

using System;
using System.Collections;
using System.Collections.Generic;

using inonego;
using inonego.Xeri;

namespace inonego.Xeri.Game.Objective
{
    // ============================================================
    /// <summary>
    /// 하나의 Root 또는 Group 자식이 될 수 있는 정적 Task Node.
    /// </summary>
    // ============================================================
    public abstract class TaskNodeSpec
    {

    #region 프로퍼티

        // ------------------------------------------------------------
        /// <summary>
        /// Spec 트리 내에서 고유한 Node ID.
        /// </summary>
        // ------------------------------------------------------------
        public string NodeID { get; }

    #endregion

    #region 생성자

        protected TaskNodeSpec(string nodeID)
        {
            NodeID = nodeID;
        }

    #endregion

    }

    // ============================================================
    /// <summary>
    /// 하나의 Condition으로 판정되는 단일 Task의 정적 정의.
    /// </summary>
    // ============================================================
    public sealed class TaskSpec : TaskNodeSpec
    {

    #region 프로퍼티

        // ------------------------------------------------------------
        /// <summary>
        /// Task의 완료 조건.
        /// </summary>
        // ------------------------------------------------------------
        public ICond<ObjectiveContext> SuccessCond { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 선택적인 실패 조건.
        /// </summary>
        // ------------------------------------------------------------
        public ICond<ObjectiveContext> FailureCond { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 후퇴 시 독점 소유로 초기화할 수 있는 로컬 Progress ID.
        /// </summary>
        // ------------------------------------------------------------
        public ReadOnlyListView<string> LocalProgressIDs => new(localProgressIDs);
        private readonly string[] localProgressIDs;

    #endregion

    #region 생성자

        // ------------------------------------------------------------
        /// <summary>
        /// Task의 Condition과 독점 로컬 Progress 선언을 복사해 보존한다.
        /// </summary>
        // ------------------------------------------------------------
        public TaskSpec
        (
            string nodeID, ICond<ObjectiveContext> successCond,
            ICond<ObjectiveContext> failureCond = null,
            IEnumerable<string> localProgressIDs = null
        ) : base(nodeID)
        {
            SuccessCond = successCond;
            FailureCond = failureCond;
            this.localProgressIDs = localProgressIDs == null
                ? Array.Empty<string>() : new List<string>(localProgressIDs).ToArray();
        }

    #endregion

    }

    // ============================================================
    /// <summary>
    /// 여러 ChildEntry를 Condition으로 조합하는 재귀 TaskGroup 정의.
    /// </summary>
    // ============================================================
    public sealed class TaskGroupSpec : TaskNodeSpec
    {

    #region 프로퍼티

        // ------------------------------------------------------------
        /// <summary>
        /// 하위 Entry의 개방 순서.
        /// </summary>
        // ------------------------------------------------------------
        public TaskExecutionMode ExecutionMode { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// Group 완료 후 하위 입력을 지속할지 지정한다.
        /// </summary>
        // ------------------------------------------------------------
        public TaskContinuationMode ContinuationMode { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// Group의 명시적 완료 Condition.
        /// </summary>
        // ------------------------------------------------------------
        public ICond<TaskGroupContext> SuccessCond { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// Group의 선택적 실패 Condition.
        /// </summary>
        // ------------------------------------------------------------
        public ICond<TaskGroupContext> FailureCond { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 정의한 순서를 보존한 직접 자식 Entry 목록.
        /// </summary>
        // ------------------------------------------------------------
        public ReadOnlyListView<TaskChildEntry> Entries => new(entries);
        private readonly TaskChildEntry[] entries;

    #endregion

    #region 생성자

        // ------------------------------------------------------------
        /// <summary>
        /// Group 정책과 직접 자식 목록을 안정적으로 복사한다.
        /// </summary>
        // ------------------------------------------------------------
        public TaskGroupSpec
        (
            string nodeID, TaskExecutionMode executionMode,
            ICond<TaskGroupContext> successCond, IEnumerable<TaskChildEntry> entries,
            TaskContinuationMode continuationMode = TaskContinuationMode.StopOnSucceeded,
            ICond<TaskGroupContext> failureCond = null
        ) : base(nodeID)
        {
            ExecutionMode = executionMode;
            ContinuationMode = continuationMode;
            SuccessCond = successCond;
            FailureCond = failureCond;
            this.entries = entries == null
                ? Array.Empty<TaskChildEntry>() : new List<TaskChildEntry>(entries).ToArray();
        }

    #endregion

    }
}
