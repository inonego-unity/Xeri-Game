/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : ObjectiveChangeQueue.cs
수정일 : 2026-11-26

# 설명
Engine이 확정한 변경을 대기열에 두었다가 순서대로 구독자에게 발행한다.

# 특이사항, 제약사항
변경은 구독자와 인자를 그대로 담아 클로저를 만들지 않는다. 구독자는 대기열에 둘 때의 스냅샷이다.
발행 중 변경에서 새 명령이 실행돼도 새 변경은 대기열 끝에 붙어 순서대로 발행된다.
Directory가 하나를 소유해 그 Directory의 Engine들이 함께 쓰며, 일괄 처리는 BeginBatch로 발행을 미뤄 모든 확정이 끝난 뒤 알린다.
구독자 하나가 실패해도 같은 이벤트의 나머지 구독자와 뒤의 변경을 모두 발행하고, 실패는 모아 마지막에 던진다.
========================================================================= BLOCK_HEADER_END */

using System;
using System.Collections;
using System.Collections.Generic;

using inonego;
using inonego.Xeri;
using inonego.Xeri.Game;

namespace inonego.Xeri.Game.Objective
{
    // ============================================================
    /// <summary>
    /// Engine의 확정 변경을 순서대로 발행하는 대기열.
    /// </summary>
    // ============================================================
    internal sealed class ObjectiveChangeQueue
    {

    #region 필드

        private readonly Queue<PendingChange> changes = new();

        // 발행 중이면 새로 들어온 변경은 이미 도는 발행 루프가 이어서 발행한다.
        private bool raising;

        // 발행을 미룬 깊이. 0보다 크면 Flush가 아무것도 발행하지 않는다.
        private int batchDepth;

    #endregion

    #region 대기열

        internal void Enqueue(Action handler)
        {
            changes.Enqueue(new PendingChange(handler));
        }

        internal void Enqueue(Action<ObjectiveStatus, ObjectiveStatus> handler, ObjectiveStatus before, ObjectiveStatus after)
        {
            changes.Enqueue(new PendingChange(handler, before, after));
        }

        internal void Enqueue(Action<TaskEntryTransition> handler, TaskEntryTransition transition)
        {
            changes.Enqueue(new PendingChange(handler, transition));
        }

        internal void Enqueue(Action<ObjectiveEvaluation> handler, ObjectiveEvaluation evaluation)
        {
            changes.Enqueue(new PendingChange(handler, evaluation));
        }

    #endregion

    #region 발행

        // 일괄 처리 동안 발행을 미룬다. 여러 Engine이 모두 확정된 뒤 같은 상태를 보고 변경을 받게 한다.
        // 반드시 EndBatch와 짝으로 쓴다. 겹쳐 쓸 수 있으며 가장 바깥 EndBatch에서 발행한다.
        internal void BeginBatch()
        {
            batchDepth++;
        }

        // 미룬 발행을 푼다. 가장 바깥 BeginBatch가 끝나면 쌓인 변경을 발행하고 실패는 던지지 않고 failures에 모은다.
        internal void EndBatch(ref ExceptionBuffer failures)
        {
            batchDepth--;
            if (batchDepth == 0)
            {
                RaisePending(ref failures);
            }
        }

        // 쌓인 변경을 순서대로 발행한다. 발행을 미뤘거나 이미 발행 중이면 바로 돌아온다.
        // 모은 실패가 있으면 발행을 마친 뒤 한 번에 던진다.
        internal void Flush()
        {
            if (batchDepth > 0)
            {
                return;
            }

            var failures = new ExceptionBuffer();
            RaisePending(ref failures);
            failures.ThrowWhenAny("Objective change handlers failed.");
        }

        private void RaisePending(ref ExceptionBuffer failures)
        {
            if (raising)
            {
                return;
            }

            raising = true;
            try
            {
                while (changes.Count > 0)
                {
                    changes.Dequeue().Raise(ref failures);
                }
            }
            finally
            {
                raising = false;
            }
        }

    #endregion

    #region 변경 항목

        private enum ChangeKind
        {
            Changed,
            StatusChanged,
            EntryEvaluationChanged,
            EvaluationChanged
        }

        // ================================================================================
        /// <summary>
        /// <br/>대기열에 둘 변경 하나. 구독자와 인자를 그대로 담아 변경마다 클로저를 만들지 않는다.
        /// <br/>구독자는 대기열에 둘 때의 스냅샷이며, 발행할 때 구독자를 하나씩 격리해 호출한다.
        /// </summary>
        // ================================================================================
        private readonly struct PendingChange
        {
            private readonly ChangeKind kind;
            private readonly Delegate handler;
            private readonly ObjectiveStatus before;
            private readonly ObjectiveStatus after;
            private readonly TaskEntryTransition transition;
            private readonly ObjectiveEvaluation evaluation;

            internal PendingChange(Action handler)
            {
                kind = ChangeKind.Changed;
                this.handler = handler;
                before = default;
                after = default;
                transition = default;
                evaluation = null;
            }

            internal PendingChange(Action<ObjectiveStatus, ObjectiveStatus> handler, ObjectiveStatus before, ObjectiveStatus after)
            {
                kind = ChangeKind.StatusChanged;
                this.handler = handler;
                this.before = before;
                this.after = after;
                transition = default;
                evaluation = null;
            }

            internal PendingChange(Action<TaskEntryTransition> handler, TaskEntryTransition transition)
            {
                kind = ChangeKind.EntryEvaluationChanged;
                this.handler = handler;
                before = default;
                after = default;
                this.transition = transition;
                evaluation = null;
            }

            internal PendingChange(Action<ObjectiveEvaluation> handler, ObjectiveEvaluation evaluation)
            {
                kind = ChangeKind.EvaluationChanged;
                this.handler = handler;
                before = default;
                after = default;
                transition = default;
                this.evaluation = evaluation;
            }

            // 구독자를 등록 순서대로 하나씩 호출한다. 하나가 실패해도 나머지를 호출하고 실패는 failures에 모은다.
            internal void Raise(ref ExceptionBuffer failures)
            {
                switch (kind)
                {
                    case ChangeKind.Changed:
                        ((Action)handler).InvokeEach(ref failures);
                        break;
                    case ChangeKind.StatusChanged:
                        ((Action<ObjectiveStatus, ObjectiveStatus>)handler).InvokeEach(before, after, ref failures);
                        break;
                    case ChangeKind.EntryEvaluationChanged:
                        ((Action<TaskEntryTransition>)handler).InvokeEach(transition, ref failures);
                        break;
                    default:
                        ((Action<ObjectiveEvaluation>)handler).InvokeEach(evaluation, ref failures);
                        break;
                }
            }
        }

    #endregion

    }
}
