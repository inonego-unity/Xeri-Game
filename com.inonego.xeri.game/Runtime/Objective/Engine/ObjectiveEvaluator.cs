/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : ObjectiveEvaluator.cs
수정일 : 2026-11-25

# 설명
한 Spec의 Composite 평가를 읽기 전용으로 계산한다.

# 특이사항, 제약사항
Instance와 평가 결과는 호출자가 소유하며 이 클래스는 아무것도 바꾸지 않는다. Fact Scope와 재진입 가드는 Engine이 맡는다.
읽은 대상 집합은 재사용하므로 한 Engine이 한 번에 하나만 평가한다.
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

    #region 생성자

        private readonly ObjectiveSpec spec;
        private readonly ObjectiveDirectory directory;

        // 평가가 읽은 대상을 모으는 재사용 집합. 읽은 대상이 있으면 결과에 넘기고 새로 만든다.
        private HashSet<(string Scope, string Name)> readsBuffer;

        internal ObjectiveEvaluator(ObjectiveSpec spec, ObjectiveDirectory directory)
        {
            this.spec = spec;
            this.directory = directory;
        }

    #endregion

    #region 평가

        // 새 결과를 만들어 평가를 채운다. 조건이 읽은 대상도 결과에 기록한다.
        internal ObjectiveEvaluation Evaluate(ObjectiveInstance source)
        {
            var snapshot = new ObjectiveEvaluation(spec.Index);
            Fill(source, snapshot, true);
            return snapshot;
        }

        // 평가를 주어진 결과에 채운다. trackReads가 참이면 조건이 읽은 대상을 결과에 기록한다.
        internal void Fill(ObjectiveInstance source, ObjectiveEvaluation snapshot, bool trackReads)
        {
            // 읽은 대상이 없으면 집합을 다음 평가에 다시 쓰고, 있으면 결과(와 Directory)에 넘겨 새로 만든다.
            HashSet<(string Scope, string Name)> reads = null;
            if (trackReads && directory != null)
            {
                reads = readsBuffer ??= new HashSet<(string Scope, string Name)>();
                reads.Clear();
            }

            var context = source.CreateContext(directory, reads);
            snapshot.RootResult = EvaluateNode(spec.Root, context, source, true, snapshot);
            if (reads != null && reads.Count > 0)
            {
                snapshot.Reads = reads;
                readsBuffer = null;
            }

            bool active = source.Status == ObjectiveStatus.Active;
            SetInputGates(spec.Root, snapshot, active);
        }

        private static TaskResult EvaluateNode
        (
            TaskNodeSpec node, ObjectiveContext context, ObjectiveInstance source,
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
                result = EvaluateGroup((TaskGroupSpec)node, context, source, participating, snapshot);
            }

            snapshot.SetNode(node.NodeID, participating, result);
            return result;
        }

        // Group의 Entry를 평가하고 자식 결과를 집계한다. 자식 결과 목록은 평가 동안만 쓰므로 풀에서 빌린다.
        private static TaskResult EvaluateGroup
        (
            TaskGroupSpec group, ObjectiveContext context, ObjectiveInstance source,
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
                    TaskResult childRaw = EvaluateNode(entry.Node, context, source, opened, snapshot);
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

                var groupContext = TaskGroupContext.ForEvaluation(context, children);
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
