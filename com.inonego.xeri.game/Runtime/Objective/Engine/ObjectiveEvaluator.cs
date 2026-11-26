/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : ObjectiveEvaluator.cs
수정일 : 2026-11-26

# 설명
한 Spec의 Composite 평가를 읽기 전용으로 계산한다.

# 특이사항, 제약사항
Instance와 평가 결과는 호출자가 소유하며 이 클래스는 아무것도 바꾸지 않는다. Fact Scope와 재진입 가드는 Engine이 맡는다.
평가 결과·읽은 대상 집합·Objective·Group Context는 재사용하므로 한 Engine이 한 번에 하나만 평가한다.
EvaluateScratch는 재사용 결과를 돌려주므로 다음 호출 전까지만 유효하다. 보관할 결과는 호출자가 Copy한다.
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
    /// 한 Spec의 Composite 평가와 입력 Gate를 계산한다.
    /// </summary>
    // ============================================================
    internal sealed class ObjectiveEvaluator
    {

    #region 필드·생성자

        private readonly ObjectiveSpec spec;
        private readonly ObjectiveDirectory directory;

        // 평가마다 다시 채워 쓰는 Context.
        // Group 문맥은 자식 평가가 모두 끝난 뒤 그 Group의 판정 동안만 채우므로 중첩 Group이 하나를 함께 쓴다.
        private readonly ObjectiveContext context = new();
        private readonly TaskGroupContext groupContext = new();

        // 재사용 평가 결과와 그 읽기 집합. Engine이 재진입을 막으므로 동시에 둘이 필요한 순간이 없다.
        private ObjectiveEvaluation scratch;

        // 마지막 EvaluateScratch에서 Condition이 읽은 대상. Directory가 없으면 기록하지 않는다(default).
        // 다음 EvaluateScratch에서 덮이므로 보관하려는 호출자는 Copy한다.
        internal ObjectiveReads ScratchReads => scratchReads;
        private ObjectiveReads scratchReads;

        internal ObjectiveEvaluator(ObjectiveSpec spec, ObjectiveDirectory directory)
        {
            this.spec = spec;
            this.directory = directory;
        }

    #endregion

    #region 평가 진입

        // 경과 시간을 evaluatedTime으로 본 평가를 재사용 결과에 채워 돌려준다. 읽은 대상은 ScratchReads에 기록한다.
        // 결과는 다음 EvaluateScratch에서 덮이므로 보관하려는 호출자는 Copy한다.
        internal ObjectiveEvaluation EvaluateScratch(ObjectiveInstance source, double evaluatedTime)
        {
            scratch ??= new ObjectiveEvaluation(spec.Index);

            ObjectiveReads reads = default;
            if (directory != null)
            {
                if (!scratchReads.IsTracking)
                {
                    scratchReads = ObjectiveReads.Create();
                }

                reads = scratchReads;
            }

            Fill(source, scratch, evaluatedTime, reads);
            return scratch;
        }

        // 경과 시간을 evaluatedTime으로 본 평가로 target을 비우고 다시 채운다. 읽은 대상은 reads에 기록하며 기록하지 않으면 default를 넘긴다.
        internal void Fill
        (
            ObjectiveInstance source, ObjectiveEvaluation target, double evaluatedTime,
            ObjectiveReads reads
        )
        {
            target.Clear();
            reads.Clear();
            source.PrepareContext(context, directory, reads, evaluatedTime);
            target.RootResult = EvaluateNode(spec.Root, source, true, target);

            bool active = source.Status == ObjectiveStatus.Active;
            SetInputGates(spec.Root, target, active);
        }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/>현재 상태로 채운 재사용 Context를 돌려준다. Flow의 시작 조건 확인처럼 평가 밖의
        /// <br/>단일 판정에 쓰며, 다음 평가나 판정 전까지만 유효하다.
        /// </summary>
        // --------------------------------------------------------------------------------
        internal ObjectiveContext PrepareContext(ObjectiveInstance source, ObjectiveReads reads)
        {
            source.PrepareContext(context, directory, reads, source.ElapsedTime);
            return context;
        }

    #endregion

    #region 노드 평가

        private TaskResult EvaluateNode
        (
            TaskNodeSpec node, ObjectiveInstance source,
            bool participating, ObjectiveEvaluation snapshot
        )
        {
            TaskResult result;
            if (node is TaskSpec task)
            {
                if (task.FailureCond?.Check(context) == true)
                {
                    result = TaskResult.Failed;
                }
                else if (task.SuccessCond.Check(context))
                {
                    result = TaskResult.Succeeded;
                }
                else
                {
                    result = TaskResult.Pending;
                }
            }
            else
            {
                result = EvaluateGroup((TaskGroupSpec)node, source, participating, snapshot);
            }

            snapshot.SetNode(node.NodeID, participating, result);
            return result;
        }

        // Group의 Entry를 평가하고 자식 결과를 집계한다. 자식 결과 목록은 평가 동안만 쓰므로 풀에서 빌린다.
        private TaskResult EvaluateGroup
        (
            TaskGroupSpec group, ObjectiveInstance source,
            bool participating, ObjectiveEvaluation snapshot
        )
        {
            var children = ListPool<TaskEntryResult>.Get();
            try
            {
                bool predecessorPassed = participating;
                foreach (var entry in group.Entries)
                {
                    // 닫힌 사유는 부모, 앞 Entry, Eligibility 순서로 첫 번째만 기록한다. 앞이 닫히면 뒤 조건은 호출하지 않는다.
                    var closedBy = TaskClosedBy.None;
                    if (!participating)
                    {
                        closedBy = TaskClosedBy.Parent;
                    }
                    else if (group.ExecutionMode == TaskExecutionMode.Sequential && !predecessorPassed)
                    {
                        closedBy = TaskClosedBy.Predecessor;
                    }
                    else if (!(entry.EligibilityCond?.Check(context) ?? true))
                    {
                        closedBy = TaskClosedBy.Eligibility;
                    }

                    bool opened = closedBy == TaskClosedBy.None;
                    TaskResult childRaw = EvaluateNode(entry.Node, source, opened, snapshot);
                    TaskResult effective = opened ? childRaw : TaskResult.Pending;
                    if (opened && group.ExecutionMode == TaskExecutionMode.Sequential
                        && entry.RevalidationMode == TaskRevalidationMode.OncePassed
                        && childRaw != TaskResult.Failed && source.HasPassed(entry.EntryID))
                    {
                        effective = TaskResult.Succeeded;
                    }

                    snapshot.SetEntry(entry.EntryID, entry.Node.NodeID, opened, effective, closedBy);
                    children.Add(new TaskEntryResult(entry.EntryID, opened, effective));
                    if (group.ExecutionMode == TaskExecutionMode.Sequential)
                    {
                        predecessorPassed = opened && effective == TaskResult.Succeeded;
                    }
                }

                // 자식 평가가 모두 끝난 뒤에 이 Group의 문맥을 채운다. 판정 동안 다른 Group 평가가 끼지 않는다.
                groupContext.Prepare(context, children);
                if (group.FailureCond?.Check(groupContext) == true)
                {
                    return TaskResult.Failed;
                }

                if (group.SuccessCond.Check(groupContext))
                {
                    return TaskResult.Succeeded;
                }

                return TaskResult.Pending;
            }
            finally
            {
                // 자식 결과 목록은 풀에 돌려주므로 문맥이 그 목록을 계속 가리키지 않게 먼저 놓는다.
                groupContext.Clear();
                ListPool<TaskEntryResult>.Release(children);
            }
        }

        private static void SetInputGates(TaskNodeSpec node, ObjectiveEvaluation snapshot, bool ancestorAllows)
        {
            snapshot.TryGetNode(node.NodeID, out var current);
            bool allowed = current.Participating && ancestorAllows;
            if (node is TaskGroupSpec group)
            {
                if (current.RawResult == TaskResult.Succeeded
                    && group.ContinuationMode == TaskContinuationMode.StopOnSucceeded)
                {
                    allowed = false;
                }

                snapshot.SetNodeInput(node.NodeID, allowed);
                foreach (var entry in group.Entries)
                {
                    snapshot.TryGetEntry(entry.EntryID, out var child);
                    SetInputGates(entry.Node, snapshot, allowed && child.Participating);
                    snapshot.TryGetNode(entry.Node.NodeID, out var childNode);
                    snapshot.SetEntryInput(entry.EntryID, childNode.CanAcceptInput);
                }
            }
            else
            {
                snapshot.SetNodeInput(node.NodeID, allowed);
            }
        }

    #endregion

    }
}
