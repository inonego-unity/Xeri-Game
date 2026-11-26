/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : ObjectiveEvaluation.cs
수정일 : 2026-11-26

# 설명
한 번의 Composite 평가에서 계산된 Node 결과와 Entry 참여·입력 허용·닫힌 사유를 노출한다.

# 특이사항, 제약사항
영속 원본이 아니라 평가 결과를 읽기 전용으로 보여 주며 별도 Snapshot DTO가 아니다.
Engine이 알림·조회로 내보낸 결과는 이후 바뀌지 않는다. 내부 재사용 결과는 Engine 밖으로 나가지 않는다.
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

        // Spec의 번호표. Node와 Entry의 자리를 정하며 같은 Spec의 모든 평가가 공유한다.
        private readonly SpecIndex index;
        private readonly TaskNodeEvaluation[] nodes;
        private readonly TaskEntryEvaluation[] entries;

    #endregion

    #region 생성자

        // 번호표 크기에 맞춘 배열 둘만 만든다. 평가마다 딕셔너리나 목록을 늘리지 않아 쓰레기가 적다.
        internal ObjectiveEvaluation(SpecIndex index)
        {
            this.index = index;
            nodes = new TaskNodeEvaluation[index.NodeCount];
            entries = new TaskEntryEvaluation[index.EntryCount];
        }

    #endregion

    #region 조회

        // ------------------------------------------------------------
        /// <summary>
        /// Root Node의 최종 RawResult.
        /// </summary>
        // ------------------------------------------------------------
        public TaskResult RootResult { get; internal set; }

        // ------------------------------------------------------------
        /// <summary>
        /// 안정 NodeID로 해당 Node의 이번 평가 결과를 읽는다.
        /// </summary>
        // ------------------------------------------------------------
        public bool TryGetNode(string nodeID, out TaskNodeEvaluation result)
        {
            if (nodeID != null && index.TryGetNodeNumber(nodeID, out int number) && nodes[number].NodeID != null)
            {
                result = nodes[number];
                return true;
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
            if (entryID != null && index.TryGetEntryNumber(entryID, out int number) && entries[number].EntryID != null)
            {
                result = entries[number];
                return true;
            }

            result = default;
            return false;
        }

    #endregion

    #region 내부 평가 작성

        // 같은 Spec의 평가에 다시 쓸 수 있도록 결과를 비운다. 배열은 그대로 두므로 할당이 없다.
        internal void Clear()
        {
            Array.Clear(nodes, 0, nodes.Length);
            Array.Clear(entries, 0, entries.Length);
            RootResult = default;
        }

        // 같은 Spec의 새 결과로 복사해 재사용 결과와 분리한다.
        internal ObjectiveEvaluation Copy()
        {
            var copy = new ObjectiveEvaluation(index);
            Array.Copy(nodes, copy.nodes, nodes.Length);
            Array.Copy(entries, copy.entries, entries.Length);
            copy.RootResult = RootResult;
            return copy;
        }

        // 이 평가가 그 Spec의 번호표로 만들어졌는지 확인한다.
        internal bool IsFor(SpecIndex other) => ReferenceEquals(index, other);

        internal void SetNode(string nodeID, bool participating, TaskResult result)
        {
            nodes[NodeNumber(nodeID)] = new TaskNodeEvaluation(nodeID, participating, result, false);
        }

        internal void SetEntry
        (
            string entryID, string nodeID, bool participating, TaskResult result, TaskClosedBy closedBy
        )
        {
            entries[EntryNumber(entryID)] = new TaskEntryEvaluation(entryID, nodeID, participating, result, false, closedBy);
        }

        internal void SetNodeInput(string nodeID, bool allowed)
        {
            int number = NodeNumber(nodeID);
            var current = nodes[number];
            nodes[number] = new TaskNodeEvaluation
            (
                current.NodeID, current.Participating, current.RawResult, allowed
            );
        }

        internal void SetEntryInput(string entryID, bool allowed)
        {
            int number = EntryNumber(entryID);
            var current = entries[number];
            entries[number] = new TaskEntryEvaluation
            (
                current.EntryID, current.NodeID, current.Participating,
                current.EffectiveResult, allowed, current.ClosedBy
            );
        }

        private int NodeNumber(string nodeID)
        {
            if (!index.TryGetNodeNumber(nodeID, out int number))
            {
                throw new KeyNotFoundException($"Unknown Node in this Spec: '{nodeID}'.");
            }

            return number;
        }

        private int EntryNumber(string entryID)
        {
            if (!index.TryGetEntryNumber(entryID, out int number))
            {
                throw new KeyNotFoundException($"Unknown Entry in this Spec: '{entryID}'.");
            }

            return number;
        }

    #endregion

    #region 비교

        // 이전 평가와 비교해 참여·유효 결과·닫힌 사유가 바뀐 Entry를 평가 순서대로 호출자의 목록에 더한다.
        // 입력 허용만 바뀐 Entry는 제외한다. 목록을 비우지 않으므로 호출자가 비워서 넘긴다.
        internal void DiffEntries(ObjectiveEvaluation previous, List<TaskEntryTransition> result)
        {
            if (previous == null || !ReferenceEquals(previous.index, index))
            {
                return;
            }

            for (int number = 0; number < entries.Length; number++)
            {
                var before = previous.entries[number];
                var after = entries[number];
                if (before.EntryID != null && after.EntryID != null
                    && (before.Participating != after.Participating
                        || before.EffectiveResult != after.EffectiveResult
                        || before.ClosedBy != after.ClosedBy))
                {
                    result.Add(new TaskEntryTransition(after.EntryID, before, after));
                }
            }
        }

        internal bool IsEquivalentTo(ObjectiveEvaluation other)
        {
            if (other == null || !ReferenceEquals(index, other.index) || RootResult != other.RootResult)
            {
                return false;
            }

            for (int number = 0; number < nodes.Length; number++)
            {
                var left = nodes[number];
                var right = other.nodes[number];
                if ((left.NodeID == null) != (right.NodeID == null)
                    || left.Participating != right.Participating
                    || left.RawResult != right.RawResult
                    || left.CanAcceptInput != right.CanAcceptInput)
                {
                    return false;
                }
            }

            for (int number = 0; number < entries.Length; number++)
            {
                var left = entries[number];
                var right = other.entries[number];
                if ((left.EntryID == null) != (right.EntryID == null)
                    || left.Participating != right.Participating
                    || left.EffectiveResult != right.EffectiveResult
                    || left.CanAcceptInput != right.CanAcceptInput
                    || left.ClosedBy != right.ClosedBy)
                {
                    return false;
                }
            }

            return true;
        }

    #endregion

    }
}
