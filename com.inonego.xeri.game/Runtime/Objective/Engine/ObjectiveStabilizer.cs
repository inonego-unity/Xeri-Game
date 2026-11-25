/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : ObjectiveStabilizer.cs
수정일 : 2026-11-25

# 설명
후보 Instance를 순차 개방·참여 시각 기록과 맞춰 더는 바뀌지 않을 때까지 안정화한다.

# 특이사항, 제약사항
후보만 바꾸며 영속 Instance는 건드리지 않는다. 호출자(Engine)가 안정된 평가를 계획 평가로 쓴다.
복원 직후의 조회·바인딩은 안정화를 실행하지 않는다.
========================================================================= BLOCK_HEADER_END */

using System;
using System.Collections;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.Pool;

using inonego;
using inonego.Xeri;
using inonego.Xeri.Game;
using inonego.Xeri.Game.Progress;
using inonego.Xeri.Serializable;

namespace inonego.Xeri.Game.Objective
{
    // ============================================================
    /// <summary>
    /// 후보 Instance의 순차 개방 경계와 Entry 참여 시각을 안정화한다.
    /// </summary>
    // ============================================================
    internal sealed class ObjectiveStabilizer
    {

    #region 생성자

        private readonly ObjectiveSpec spec;
        private readonly ObjectiveEvaluator evaluator;

        internal ObjectiveStabilizer(ObjectiveSpec spec, ObjectiveEvaluator evaluator)
        {
            this.spec = spec;
            this.evaluator = evaluator;
        }

    #endregion

    #region Sequential 통과·후퇴

        // 후보가 더는 바뀌지 않을 때까지 맞추고, 그 상태의 평가를 돌려준다.
        // 호출자는 같은 후보를 다시 평가하지 않고 이 결과를 쓴다.
        internal ObjectiveEvaluation Stabilize(ObjectiveInstance candidate)
        {
            // 복원 직후의 조회·바인딩은 이 메서드를 실행하지 않는다.
            // Active 명령/명시 Refresh에서만 영속 개방 이력을 확정한다.
            int maximum = spec.Index.EntryCount * 4 + 8;
            for (int round = 0; round < maximum; round++)
            {
                var evaluation = evaluator.Evaluate(candidate);
                if (candidate.Status != ObjectiveStatus.Active)
                {
                    return evaluation;
                }

                // 순차 개방이 바뀌면 참여 여부가 달라지므로 시각 기록은 개방이 안정된 평가에서만 맞춘다.
                if (ReconcileSequential(spec.Root, evaluation, candidate)
                    || ReconcileOpenings(spec.Root, evaluation, candidate))
                {
                    continue;
                }

                return evaluation;
            }

            throw new InvalidOperationException("Objective state did not converge.");
        }

        private static bool ReconcileSequential
        (
            TaskNodeSpec node, ObjectiveEvaluation evaluation, ObjectiveInstance candidate
        )
        {
            if (node is not TaskGroupSpec group)
            {
                return false;
            }

            evaluation.TryGetNode(node.NodeID, out var groupState);
            if (!groupState.Participating)
            {
                return false;
            }

            if (group.ExecutionMode == TaskExecutionMode.Sequential)
            {
                int previous = -1;
                if (candidate.TryGetBoundary(group.NodeID, out string boundaryID))
                {
                    previous = FindEntryIndex(group, boundaryID);
                }

                int reached = -1;
                for (int index = 0; index < group.Entries.Count; index++)
                {
                    var entry = group.Entries[index];
                    evaluation.TryGetEntry(entry.EntryID, out var current);
                    if (!current.Participating)
                    {
                        // Eligibility=false는 입력만 정지한다. 통과·경계·Progress는 보존한다.
                        break;
                    }

                    reached = index;
                    if (index < previous && current.EffectiveResult != TaskResult.Succeeded)
                    {
                        // 실제 참여 중인 선행 Entry가 유효 통과를 잃었을 때만 후퇴한다.
                        for (int later = previous; later > index; later--)
                        {
                            var rolledBack = group.Entries[later];
                            if (rolledBack.RollbackMode == TaskRollbackMode.ResetOnRollback)
                            {
                                ResetEntryTree(rolledBack, candidate);
                            }
                        }

                        candidate.SetBoundary(group.NodeID, entry.EntryID);
                        return true;
                    }

                    evaluation.TryGetNode(entry.Node.NodeID, out var child);
                    if (entry.RevalidationMode == TaskRevalidationMode.OncePassed
                        && child.RawResult == TaskResult.Succeeded
                        && !candidate.HasPassed(entry.EntryID))
                    {
                        candidate.MarkPassed(entry.EntryID);
                        return true;
                    }
                }

                if (reached > previous)
                {
                    candidate.SetBoundary(group.NodeID, group.Entries[reached].EntryID);
                    return true;
                }
            }

            foreach (var entry in group.Entries)
            {
                evaluation.TryGetEntry(entry.EntryID, out var state);
                if (state.Participating && ReconcileSequential(entry.Node, evaluation, candidate))
                {
                    return true;
                }
            }

            return false;
        }

        private static void ResetEntryTree(TaskChildEntry entry, ObjectiveInstance candidate)
        {
            // 후퇴하는 Entry 자체의 통과도 subtree Reset 대상에 포함한다.
            candidate.ClearPassed(entry.EntryID);
            ResetNodeTree(entry.Node, candidate);
        }

        private static void ResetNodeTree(TaskNodeSpec node, ObjectiveInstance candidate)
        {
            if (node is TaskSpec task)
            {
                foreach (var progressID in task.LocalProgressIDs)
                {
                    candidate.ResetProgress(progressID);
                }

                return;
            }

            var group = (TaskGroupSpec)node;
            if (group.ExecutionMode == TaskExecutionMode.Sequential)
            {
                candidate.ClearBoundary(group.NodeID);
            }

            foreach (var entry in group.Entries)
            {
                ResetEntryTree(entry, candidate);
            }
        }

        internal static int FindEntryIndex(TaskGroupSpec group, string entryID)
        {
            for (int index = 0; index < group.Entries.Count; index++)
            {
                if (string.Equals(group.Entries[index].EntryID, entryID, StringComparison.Ordinal))
                {
                    return index;
                }
            }

            return -1;
        }

    #endregion

    #region 참여 시각 기록

        // 시간을 재는 Entry의 참여 시작 시각을 참여 여부에 맞춘다. 참여를 멈추면 지워서 다시 열릴 때 새로 잰다.
        private static bool ReconcileOpenings
        (
            TaskNodeSpec node, ObjectiveEvaluation evaluation, ObjectiveInstance candidate
        )
        {
            if (node is not TaskGroupSpec group)
            {
                return false;
            }

            bool changed = false;
            foreach (var entry in group.Entries)
            {
                if (entry.TracksOpenTime)
                {
                    evaluation.TryGetEntry(entry.EntryID, out var state);
                    changed |= state.Participating
                        ? candidate.MarkOpened(entry.EntryID)
                        : candidate.ClearOpened(entry.EntryID);
                }

                changed |= ReconcileOpenings(entry.Node, evaluation, candidate);
            }

            return changed;
        }

    #endregion

    }
}
