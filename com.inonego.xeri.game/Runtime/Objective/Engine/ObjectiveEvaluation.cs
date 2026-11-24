/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : ObjectiveEvaluation.cs
수정일 : 2026-11-23

# 설명
한 번의 Composite 평가에서 계산된 Node 결과와 Entry 참여·입력 허용·닫힌 사유를 노출한다.

# 특이사항, 제약사항
영속 원본이 아니라 호출마다 생성되는 읽기 전용 결과이며 별도 Snapshot DTO가 아니다.
========================================================================= BLOCK_HEADER_END */

using System;
using System.Collections;
using System.Collections.Generic;

namespace inonego.Xeri.Game.Objective
{
    // ============================================================
    /// <summary>
    /// Entry가 부모 집계에 참여하지 못하는 사유.
    /// </summary>
    // ============================================================
    public enum TaskClosedBy { None, Parent, Predecessor, Eligibility }

    // ============================================================
    /// <summary>
    /// 한 Node의 참여·원시 결과와 새 입력 수락 가능 여부.
    /// </summary>
    // ============================================================
    public readonly struct TaskNodeEvaluation
    {

    #region 프로퍼티

        // ------------------------------------------------------------
        /// <summary>
        /// Spec 트리 내의 NodeID.
        /// </summary>
        // ------------------------------------------------------------
        public string NodeID { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 상위 Eligibility와 구조상 집계 참여 여부.
        /// </summary>
        // ------------------------------------------------------------
        public bool Participating { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 참여 여부와 독립적으로 계산한 Node Condition 결과.
        /// </summary>
        // ------------------------------------------------------------
        public TaskResult RawResult { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// Objective Active·조상 완료 입력 정책을 통과하는지 여부.
        /// </summary>
        // ------------------------------------------------------------
        public bool CanAcceptInput { get; }

    #endregion

    #region 생성자

        internal TaskNodeEvaluation
        (
            string nodeID, bool participating, TaskResult rawResult, bool canAcceptInput
        )
        {
            NodeID = nodeID;
            Participating = participating;
            RawResult = rawResult;
            CanAcceptInput = canAcceptInput;
        }

    #endregion

    }

    // ============================================================
    /// <summary>
    /// 부모가 집계하는 Entry의 유효 결과 및 입력 허용 여부.
    /// </summary>
    // ============================================================
    public readonly struct TaskEntryEvaluation
    {

    #region 프로퍼티

        // ------------------------------------------------------------
        /// <summary>
        /// 부모→자식 관계의 안정 EntryID.
        /// </summary>
        // ------------------------------------------------------------
        public string EntryID { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 이 Entry가 가리키는 ChildNode의 ID.
        /// </summary>
        // ------------------------------------------------------------
        public string NodeID { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 부모 집계 참여 여부.
        /// </summary>
        // ------------------------------------------------------------
        public bool Participating { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 부모가 집계하는 결과. 비참여 Entry는 Pending.
        /// </summary>
        // ------------------------------------------------------------
        public TaskResult EffectiveResult { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 이 Entry 아래로 새로운 Progress 사건을 전달할 수 있는지 여부.
        /// </summary>
        // ------------------------------------------------------------
        public bool CanAcceptInput { get; }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/>참여하지 못한다면 그 사유(부모가 닫힘, 앞 Entry 미통과, Eligibility 거짓).
        /// <br/>참여 중이면 None이다.
        /// </summary>
        // --------------------------------------------------------------------------------
        public TaskClosedBy ClosedBy { get; }

    #endregion

    #region 생성자

        internal TaskEntryEvaluation
        (
            string entryID, string nodeID, bool participating,
            TaskResult effectiveResult, bool canAcceptInput, TaskClosedBy closedBy
        )
        {
            EntryID = entryID;
            NodeID = nodeID;
            Participating = participating;
            EffectiveResult = effectiveResult;
            CanAcceptInput = canAcceptInput;
            ClosedBy = closedBy;
        }

    #endregion

    }

    // ======================================================================
    /// <summary>
    /// 한 확정에서 Entry의 참여·유효 결과·닫힌 사유가 바뀐 이전과 이후 평가.
    /// </summary>
    // ======================================================================
    public readonly struct TaskEntryTransition
    {

    #region 프로퍼티

        // ------------------------------------------------------------
        /// <summary>
        /// 바뀐 Entry의 ID.
        /// </summary>
        // ------------------------------------------------------------
        public string EntryID { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 확정 전 평가.
        /// </summary>
        // ------------------------------------------------------------
        public TaskEntryEvaluation Previous { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 확정 뒤 평가.
        /// </summary>
        // ------------------------------------------------------------
        public TaskEntryEvaluation Next { get; }

    #endregion

    #region 생성자

        internal TaskEntryTransition(string entryID, TaskEntryEvaluation previous, TaskEntryEvaluation next)
        {
            EntryID = entryID;
            Previous = previous;
            Next = next;
        }

    #endregion

    }

    // ============================================================
    /// <summary>
    /// 평가를 한 번 마친 후 Node/Entry별 계산 결과를 조회하는 비영속 결과.
    /// </summary>
    // ============================================================
    public sealed class ObjectiveEvaluation
    {

    #region 필드

        private readonly Dictionary<string, TaskNodeEvaluation> nodes = new(StringComparer.Ordinal);
        private readonly Dictionary<string, TaskEntryEvaluation> entries = new(StringComparer.Ordinal);
        private readonly List<string> entryOrder = new();

    #endregion

    #region 조회

        // ------------------------------------------------------------
        /// <summary>
        /// Root Node의 최종 RawResult.
        /// </summary>
        // ------------------------------------------------------------
        public TaskResult RootResult { get; internal set; }

        // 이 평가에서 Condition이 읽은 다른 Objective와 Fact 소스. Directory가 변경 전파에 쓴다.
        internal HashSet<(string Scope, string Name)> Reads { get; set; }

        // ------------------------------------------------------------
        /// <summary>
        /// 안정 NodeID로 해당 Node의 이번 평가 결과를 읽는다.
        /// </summary>
        // ------------------------------------------------------------
        public bool TryGetNode(string nodeID, out TaskNodeEvaluation result)
        {
            if (nodeID != null)
            {
                return nodes.TryGetValue(nodeID, out result);
            }

            result = default;
            return false;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 안정 EntryID로 해당 Entry의 이번 평가 결과를 읽는다.
        /// </summary>
        // ------------------------------------------------------------
        public bool TryGetEntry(string entryID, out TaskEntryEvaluation result)
        {
            if (entryID != null)
            {
                return entries.TryGetValue(entryID, out result);
            }

            result = default;
            return false;
        }

    #endregion

    #region 내부 평가 작성

        internal void SetNode(string nodeID, bool participating, TaskResult result)
        {
            nodes[nodeID] = new TaskNodeEvaluation(nodeID, participating, result, false);
        }

        internal void SetEntry
        (
            string entryID, string nodeID, bool participating, TaskResult result, TaskClosedBy closedBy
        )
        {
            if (!entries.ContainsKey(entryID))
            {
                entryOrder.Add(entryID);
            }

            entries[entryID] = new TaskEntryEvaluation(entryID, nodeID, participating, result, false, closedBy);
        }

        // 이전 평가와 비교해 참여·유효 결과·닫힌 사유가 바뀐 Entry를 평가 순서대로 돌려준다. 입력 허용만 바뀐 Entry는 제외한다.
        internal List<TaskEntryTransition> DiffEntries(ObjectiveEvaluation previous)
        {
            var result = new List<TaskEntryTransition>();
            if (previous == null)
            {
                return result;
            }

            foreach (var entryID in entryOrder)
            {
                if (previous.entries.TryGetValue(entryID, out var before))
                {
                    var after = entries[entryID];
                    if (before.Participating != after.Participating
                        || before.EffectiveResult != after.EffectiveResult
                        || before.ClosedBy != after.ClosedBy)
                    {
                        result.Add(new TaskEntryTransition(entryID, before, after));
                    }
                }
            }

            return result;
        }

        internal void SetNodeInput(string nodeID, bool allowed)
        {
            var current = nodes[nodeID];
            nodes[nodeID] = new TaskNodeEvaluation
            (
                current.NodeID, current.Participating, current.RawResult, allowed
            );
        }

        internal void SetEntryInput(string entryID, bool allowed)
        {
            var current = entries[entryID];
            entries[entryID] = new TaskEntryEvaluation
            (
                current.EntryID, current.NodeID, current.Participating,
                current.EffectiveResult, allowed, current.ClosedBy
            );
        }

        internal bool IsEquivalentTo(ObjectiveEvaluation other)
        {
            if (other == null || RootResult != other.RootResult
                || nodes.Count != other.nodes.Count || entries.Count != other.entries.Count)
            {
                return false;
            }

            foreach (var item in nodes)
            {
                if (!other.nodes.TryGetValue(item.Key, out var value)
                    || item.Value.Participating != value.Participating
                    || item.Value.RawResult != value.RawResult
                    || item.Value.CanAcceptInput != value.CanAcceptInput)
                {
                    return false;
                }
            }

            foreach (var item in entries)
            {
                if (!other.entries.TryGetValue(item.Key, out var value)
                    || item.Value.Participating != value.Participating
                    || item.Value.EffectiveResult != value.EffectiveResult
                    || item.Value.CanAcceptInput != value.CanAcceptInput
                    || item.Value.ClosedBy != value.ClosedBy)
                {
                    return false;
                }
            }

            return true;
        }

    #endregion

    }
}
