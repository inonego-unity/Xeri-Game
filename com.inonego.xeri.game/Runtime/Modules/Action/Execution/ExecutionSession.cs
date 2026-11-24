/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : ExecutionSession.cs
수정일 : 2026-11-19

# 설명
Execution의 요청·적용 시간, Flow/Behavior, 독립 Signal·Command, Commit·종료 수명을 소유한다.
시작·진행·Command·Signal·확정 단계의 실패는 Cancelled 종료로 수렴시키고, 종료 통지 실패는 모아서 전달한다.
세션별 ValueRegistry를 격리한다.
수명은 시작 전·시작 중·활성·종료 단계 하나로 관리하며 종료가 확정되면 시작 중이어도 종료 단계가 우선한다.
========================================================================= BLOCK_HEADER_END */

using System;
using System.Collections;
using System.Collections.Generic;

namespace inonego.Xeri.Game
{
    using Xeri;
    using Xeri.Serializable;

    // ============================================================
    /// <summary>
    /// 한 번 시작된 Execution의 독립 Runtime 수명.
    /// </summary>
    // ============================================================
    public sealed class ExecutionSession
    {

    #region 실행 구성

        // ------------------------------------------------------------
        /// <summary>
        /// 실행 세션의 ID.
        /// </summary>
        // ------------------------------------------------------------
        public ulong ID { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 실행의 Owner와 Instigator 문맥.
        /// </summary>
        // ------------------------------------------------------------
        public ExecutionContext Context { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 세션이 소유하는 실행 진행 Flow.
        /// </summary>
        // ------------------------------------------------------------
        public ExecutionFlowRuntime Flow => flow;
        private readonly ExecutionFlowRuntime flow;

        // ------------------------------------------------------------
        /// <summary>
        /// 세션에 귀속된 Behavior 실행 인스턴스 목록.
        /// </summary>
        // ------------------------------------------------------------
        public IReadOnlyList<ExecutionBehaviorRuntime> Behaviors => behaviorView;
        private readonly IReadOnlyList<ExecutionBehaviorRuntime> behaviorView;
        private readonly ExecutionBehaviorRuntime[] behaviors;

        // ------------------------------------------------------------
        /// <summary>
        /// 이 Execution에서만 사용하는 primitive 변수를 보관한다.
        /// </summary>
        // ------------------------------------------------------------
        public ValueRegistry Variables { get; } = new();

    #endregion

    #region 실행 시간

        // ------------------------------------------------------------
        /// <summary>
        /// 마지막 Advance 호출 직전의 실행 시간.
        /// </summary>
        // ------------------------------------------------------------
        public float PreviousTime => previousTime;
        private float previousTime = 0f;

        // ------------------------------------------------------------
        /// <summary>
        /// 마지막 Advance 호출에서 전달받은 원본 Delta.
        /// </summary>
        // ------------------------------------------------------------
        public float RequestedDelta => requestedDelta;
        private float requestedDelta = 0f;

        // ------------------------------------------------------------
        /// <summary>
        /// 마지막 Advance에서 실제 반영한 시간 증분.
        /// </summary>
        // ------------------------------------------------------------
        public float AppliedDelta => time - previousTime;

        // ------------------------------------------------------------
        /// <summary>
        /// 누적된 현재 실행 시간.
        /// </summary>
        // ------------------------------------------------------------
        public float Time => time;
        private float time = 0f;

        // ------------------------------------------------------------
        /// <summary>
        /// 세션 진행 시간의 최대 상한.
        /// </summary>
        // ------------------------------------------------------------
        public float MaximumTime => maximumTime;
        private readonly float maximumTime;

    #endregion

    #region 수명 상태

        // ============================================================
        /// <summary>
        /// Session 수명의 단계.
        /// </summary>
        // ============================================================
        private enum Phase
        {
            NotBegun,
            Beginning,
            Active,
            Ended,
        }

        private Phase phase = Phase.NotBegun;

        // 연산별 중첩 금지 가드. 서로 동시에 켜질 수 있어 단계와 분리한다.
        private bool isAdvancing = false;
        private bool isPublishingSignal = false;
        private bool isCommanding = false;

        // ------------------------------------------------------------
        /// <summary>
        /// Begin 호출이 수행되었는지 나타낸다.
        /// </summary>
        // ------------------------------------------------------------
        public bool HasBegun => phase != Phase.NotBegun;

        // ------------------------------------------------------------
        /// <summary>
        /// Begin이 호출되었고 아직 종료되지 않은 실행 상태.
        /// </summary>
        // ------------------------------------------------------------
        public bool IsActive => phase is Phase.Beginning or Phase.Active;

        // ------------------------------------------------------------
        /// <summary>
        /// 종료 처리가 확정되었는지 나타낸다.
        /// </summary>
        // ------------------------------------------------------------
        public bool HasEnded => phase == Phase.Ended;

        // ------------------------------------------------------------
        /// <summary>
        /// 실행의 Commit 여부를 나타낸다.
        /// </summary>
        // ------------------------------------------------------------
        public bool IsCommitted => isCommitted;
        private bool isCommitted = false;

        // ------------------------------------------------------------
        /// <summary>
        /// Flow 완료 또는 최대 시간 상한에 도달했는지 확인한다.
        /// </summary>
        // ------------------------------------------------------------
        public bool IsComplete => IsActive && (flow.IsComplete || time >= maximumTime);

        // ------------------------------------------------------------
        /// <summary>
        /// 확정된 종료 사유를 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        public ExecutionEndReason EndReason => endReason;
        private ExecutionEndReason endReason = ExecutionEndReason.None;

        private ExecutionSession attachedParent = null;
        private readonly List<ExecutionSession> attachedChildren = new();

    #endregion

    #region 수명 이벤트

        // ------------------------------------------------------------
        /// <summary>
        /// Flow와 Behavior의 Begin이 모두 완료된 뒤 한 번 발생한다.
        /// </summary>
        // ------------------------------------------------------------
        public event Action<ExecutionSession> OnBegun = null;

        // ------------------------------------------------------------
        /// <summary>
        /// Active Session이 최초 Commit에 성공하면 한 번 발생한다.
        /// </summary>
        // ------------------------------------------------------------
        public event Action<ExecutionSession> OnCommitted = null;

        // ------------------------------------------------------------
        /// <summary>
        /// 수명 종료 처리 후 종료 사유와 함께 한 번 발생한다.
        /// </summary>
        // ------------------------------------------------------------
        public event Action<ExecutionSession, ExecutionEndReason> OnEnded = null;

    #endregion

    #region Session 생성·Runtime 귀속

        // ------------------------------------------------------------
        /// <summary>
        /// Flow와 Behavior의 실행별 인스턴스를 받아 Session을 구성한다.
        /// </summary>
        // ------------------------------------------------------------
        public ExecutionSession
        (
            ulong id,
            in ExecutionContext context,
            ExecutionFlowRuntime flow,
            IReadOnlyList<ExecutionBehaviorRuntime> behaviors = null,
            float maximumTime = float.PositiveInfinity
        )
        {
            if (flow == null)
            {
                throw new ArgumentNullException(nameof(flow));
            }

            if (float.IsNaN(maximumTime) || maximumTime <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumTime));
            }

            var runtimeBehaviors = CopyBehavior(behaviors);

            if (flow.Session != null)
            {
                throw new InvalidOperationException("Execution Flow Runtime은 이미 다른 Session에 귀속되어 있습니다.");
            }

            for (var index = 0; index < runtimeBehaviors.Length; index++)
            {
                if (runtimeBehaviors[index].Session != null)
                {
                    throw new InvalidOperationException("Execution Behavior Runtime은 이미 다른 Session에 귀속되어 있습니다.");
                }
            }

            ID = id;
            Context = context;
            this.flow = flow;
            this.maximumTime = maximumTime;
            this.behaviors = runtimeBehaviors;
            behaviorView = Array.AsReadOnly(runtimeBehaviors);

            flow.Bind(this);

            for (var index = 0; index < runtimeBehaviors.Length; index++)
            {
                runtimeBehaviors[index].Bind(this);
            }
        }

    #endregion

    #region Begin과 Rollback

        // ------------------------------------------------------------
        /// <summary>
        /// Flow와 Behavior를 시작하고 실패 시 시작된 요소를 역순 정리한다.
        /// </summary>
        // ------------------------------------------------------------
        public void Begin()
        {
            if (phase != Phase.NotBegun)
            {
                throw new InvalidOperationException("ExecutionSession은 한 번만 시작할 수 있습니다.");
            }

            phase = Phase.Beginning;
            var flowBegan = false;
            var behaviorBeginCount = 0;

            try
            {
                flowBegan = true;
                flow.Begin(this);

                for (var index = 0; index < behaviors.Length; index++)
                {
                    behaviorBeginCount = index + 1;
                    behaviors[index].Begin(this);
                }
            }
            catch (Exception exception)
            {
                if (phase != Phase.Ended)
                {
                    RollbackBegin(flowBegan, behaviorBeginCount, exception);
                }

                throw;
            }

            phase = Phase.Active;

            try
            {
                InvokeWhileActive(OnBegun);
            }
            catch (Exception exception)
            {
                if (phase != Phase.Ended)
                {
                    RollbackBegin(flowBegan, behaviorBeginCount, exception);
                }

                throw;
            }
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 일부 시작된 요소를 역순으로 정리하고 실패를 보존한다.
        /// </summary>
        // ------------------------------------------------------------
        private void RollbackBegin
        (
            bool flowBegan,
            int behaviorBeginCount,
            Exception beginException
        )
        {
            // rollback 중에는 Session이 다시 활성 상태로 관찰되지 않도록 종료를 먼저 확정한다.
            phase = Phase.Ended;
            endReason = ExecutionEndReason.Cancelled;
            var failures = new ExceptionBuffer();

            for (var index = behaviorBeginCount - 1; index >= 0; index--)
            {
                try
                {
                    behaviors[index].End(this, ExecutionEndReason.Cancelled);
                }
                catch (Exception exception)
                {
                    failures.Add(exception);
                }
            }

            if (flowBegan)
            {
                try
                {
                    flow.End(this, ExecutionEndReason.Cancelled);
                }
                catch (Exception exception)
                {
                    failures.Add(exception);
                }
            }

            EndAttachedChildren(ref failures);
            NotifyEnded(ExecutionEndReason.Cancelled, ref failures);

            if (failures.Count == 0)
            {
                return;
            }

            failures.Prepend(beginException);
            throw failures.CreateAggregateException("Execution 시작 실패와 rollback 정리 실패가 함께 발생했습니다.");
        }

    #endregion

    #region 시간 진행

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> 요청 Delta와 실제 적용 Delta를 구분한다.
        /// <br/> Flow와 Behavior는 같은 시간 구간을 사용한다.
        /// </summary>
        // ------------------------------------------------------------
        public void Advance(float deltaTime)
        {
            RequireActive();
            if (phase == Phase.Beginning || isAdvancing)
            {
                throw new InvalidOperationException("Execution 진행 중 Advance를 재진입할 수 없습니다.");
            }

            if (float.IsNaN(deltaTime) || float.IsInfinity(deltaTime))
            {
                throw new ArgumentOutOfRangeException(nameof(deltaTime));
            }

            isAdvancing = true;
            requestedDelta = deltaTime;
            previousTime = time;

            // 음수 요청은 적용하지 않고, double 계산으로 float 덧셈 오버플로를 방지한다.
            var nonnegativeDelta = Math.Max(0f, requestedDelta);
            var desiredTime = Math.Min((double)maximumTime, (double)previousTime + nonnegativeDelta);
            time = (float)Math.Min(desiredTime, float.MaxValue);
            var appliedDelta = AppliedDelta;

            try
            {
                flow.Advance(this, appliedDelta);
                if (!IsActive)
                {
                    return;
                }

                for (var index = 0; index < behaviors.Length; index++)
                {
                    behaviors[index].Advance(this, appliedDelta);
                    if (!IsActive)
                    {
                        return;
                    }
                }
            }
            catch (Exception exception)
            {
                AbortOnFailure(exception);
                throw;
            }
            finally
            {
                isAdvancing = false;
            }
        }

    #endregion

    #region Command·Signal 전달

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> Signal 알림을 Behavior에 발행한다.
        /// <br/> Command의 판정과 Flow 상태 전이는 발생시키지 않는다.
        /// <br/> Signal 전달 중의 Signal 재발행은 거부한다.
        /// </summary>
        // ------------------------------------------------------------
        public void PublishSignal(in ExecutionSignal signal)
        {
            RequireActive();

            if (phase == Phase.Beginning)
            {
                throw new InvalidOperationException("Begin 중에는 Signal을 발행할 수 없습니다.");
            }

            if (!signal.IsValid)
            {
                throw new ArgumentException("유효한 Execution Signal이 필요합니다.", nameof(signal));
            }

            if (isPublishingSignal)
            {
                throw new InvalidOperationException("Signal 전달 중에는 Signal을 다시 발행할 수 없습니다.");
            }

            isPublishingSignal = true;
            try
            {
                NotifyBehaviors(signal);
            }
            catch (Exception exception)
            {
                AbortOnFailure(exception);
                throw;
            }
            finally
            {
                isPublishingSignal = false;
            }
        }

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> Command의 수락·거부·지연은 Flow가 판정한다.
        /// <br/> Signal 알림은 별도 호출로 전달한다.
        /// <br/> Command 처리 중의 Command 재요청은 거부한다.
        /// </summary>
        // ------------------------------------------------------------
        public ExecutionCommandResult Command(in ExecutionCommand command)
        {
            if (phase != Phase.Active || !command.IsValid)
            {
                return ExecutionCommandResult.Rejected;
            }

            if (isCommanding)
            {
                throw new InvalidOperationException("Command 처리 중에는 Command를 다시 요청할 수 없습니다.");
            }

            isCommanding = true;
            try
            {
                return flow.Command(this, command);
            }
            catch (Exception exception)
            {
                AbortOnFailure(exception);
                throw;
            }
            finally
            {
                isCommanding = false;
            }
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 활성 Behavior에 동일한 Signal을 순서대로 전달한다.
        /// </summary>
        // ------------------------------------------------------------
        private void NotifyBehaviors(in ExecutionSignal signal)
        {
            for (var index = 0; index < behaviors.Length && IsActive; index++)
            {
                behaviors[index].Signal(this, signal);
            }
        }

    #endregion

    #region Child 수명

        // ----------------------------------------------------------------------
        /// <summary>
        /// Active Child를 단일 Parent에 연결하며 순환과 중복 소유를 방지한다.
        /// </summary>
        // ----------------------------------------------------------------------
        public bool AttachChild(ExecutionSession child)
        {
            RequireActive();

            if (child == null)
            {
                throw new ArgumentNullException(nameof(child));
            }

            if (ReferenceEquals(child, this))
            {
                throw new InvalidOperationException
                (
                    "ExecutionSession은 자기 자신을 Child로 연결할 수 없습니다."
                );
            }

            if (!child.IsActive)
            {
                throw new InvalidOperationException
                (
                    "Active Child ExecutionSession만 연결할 수 있습니다."
                );
            }

            if (ReferenceEquals(child.attachedParent, this))
            {
                return false;
            }

            if (child.attachedParent != null)
            {
                throw new InvalidOperationException
                (
                    "ParentBound Child Execution은 하나의 Parent만 가질 수 있습니다."
                );
            }

            for
            (
                var ancestor = this;
                ancestor != null;
                ancestor = ancestor.attachedParent
            )
            {
                if (ReferenceEquals(ancestor, child))
                {
                    throw new InvalidOperationException
                    (
                        "Execution Child ownership에 순환을 만들 수 없습니다."
                    );
                }
            }

            child.attachedParent = this;
            attachedChildren.Add(child);
            child.OnEnded += OnChildEnded;
            return true;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Child 수명 연결을 해제하며 Child 실행 자체는 종료하지 않는다.
        /// </summary>
        // ------------------------------------------------------------
        public bool DetachChild(ExecutionSession child)
        {
            if (child == null)
            {
                return false;
            }

            var index = attachedChildren.IndexOf(child);
            if (index < 0)
            {
                return false;
            }

            attachedChildren.RemoveAt(index);
            child.OnEnded -= OnChildEnded;

            if (ReferenceEquals(child.attachedParent, this))
            {
                child.attachedParent = null;
            }

            return true;
        }

        private void OnChildEnded(ExecutionSession child, ExecutionEndReason reason)
        {
            DetachChild(child);
        }

    #endregion

    #region Commit·완료 판정

        // ------------------------------------------------------------
        /// <summary>
        /// Active 실행을 한 번만 확정하고 Commit 이벤트를 통지한다.
        /// </summary>
        // ------------------------------------------------------------
        public bool TryCommit()
        {
            if (phase != Phase.Active || isCommitted)
            {
                return false;
            }

            isCommitted = true;

            try
            {
                InvokeWhileActive(OnCommitted);
            }
            catch (Exception exception)
            {
                AbortOnFailure(exception);
                throw;
            }

            return true;
        }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/> 구독자를 순서대로 호출하되 구독자가 Session을 종료하면 남은 구독자에게는
        /// <br/> 알리지 않는다. 종료된 Session의 시작·확정 통지가 종료 통지보다 늦게 가지 않는다.
        /// </summary>
        // --------------------------------------------------------------------------------
        private void InvokeWhileActive(Action<ExecutionSession> observers)
        {
            if (observers == null)
            {
                return;
            }

            foreach (Action<ExecutionSession> observer in observers.GetInvocationList())
            {
                if (!IsActive)
                {
                    return;
                }

                observer(this);
            }
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> Flow가 완료된 Active Session만 Completed 사유로 종료한다.
        /// <br/> 완료 판정이 예외를 던지면 Cancelled로 종료하고 예외를 다시 던진다.
        /// </summary>
        // ----------------------------------------------------------------------
        public bool TryComplete()
        {
            if (phase == Phase.Beginning)
            {
                return false;
            }

            bool complete;
            try
            {
                complete = IsComplete;
            }
            catch (Exception exception)
            {
                AbortOnFailure(exception);
                throw;
            }

            if (!complete)
            {
                return false;
            }

            return End(ExecutionEndReason.Completed);
        }

    #endregion

    #region 종료·실패 처리

        // ----------------------------------------------------------------------
        /// <summary>
        /// Behavior와 Flow 및 종속 Child를 정리하고 종료 이벤트를 한 번만 발행한다.
        /// </summary>
        // ----------------------------------------------------------------------
        public bool End(ExecutionEndReason reason)
        {
            if (phase == Phase.Beginning)
            {
                throw new InvalidOperationException("Begin 중에는 Execution을 종료할 수 없습니다.");
            }

            if (!IsActive)
            {
                return false;
            }

            if (reason == ExecutionEndReason.None)
            {
                throw new ArgumentOutOfRangeException(nameof(reason));
            }

            phase = Phase.Ended;
            endReason = reason;
            var failures = new ExceptionBuffer();

            for (var index = behaviors.Length - 1; index >= 0; index--)
            {
                try
                {
                    behaviors[index].End(this, reason);
                }
                catch (Exception exception)
                {
                    failures.Add(exception);
                }
            }

            try
            {
                flow.End(this, reason);
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }

            EndAttachedChildren(ref failures);

            NotifyEnded(reason, ref failures);

            failures.ThrowWhenAny("Execution 종료 중 여러 정리 단계가 실패했습니다.");

            return true;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> 실행 중 실패하면 Cancelled로 종료한다.
        /// <br/> 정리 실패는 원래 실패와 함께 전달한다.
        /// </summary>
        // ------------------------------------------------------------
        private void AbortOnFailure(Exception failure)
        {
            if (!IsActive)
            {
                return;
            }

            try
            {
                End(ExecutionEndReason.Cancelled);
            }
            catch (Exception cleanupFailure)
            {
                throw new AggregateException
                (
                    "Execution 처리 실패 후 종료 정리까지 실패했습니다.",
                    failure, cleanupFailure
                );
            }
        }

        private void RequireActive()
        {
            if (!IsActive)
            {
                throw new InvalidOperationException
                (
                    "Active ExecutionSession에서만 수행할 수 있습니다."
                );
            }
        }

    #endregion

    #region 종료 통지·종속 실행 정리

        // ------------------------------------------------------------
        /// <summary>
        /// 종료 observer 하나가 실패해도 나머지를 통지한다.
        /// </summary>
        // ------------------------------------------------------------
        private void NotifyEnded(ExecutionEndReason reason, ref ExceptionBuffer failures)
        {
            var observers = OnEnded;
            if (observers == null)
            {
                return;
            }

            foreach (Action<ExecutionSession, ExecutionEndReason> observer in observers.GetInvocationList())
            {
                try
                {
                    observer(this, reason);
                }
                catch (Exception exception)
                {
                    failures.Add(exception);
                }
            }
        }

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> 소유한 Child를 분리하고 활성 Child의 종료를 시도한다.
        /// <br/> 종료 콜백이 목록을 바꿔도 남은 Child만 이어서 정리한다.
        /// </summary>
        // ------------------------------------------------------------
        private void EndAttachedChildren(ref ExceptionBuffer failures)
        {
            while (attachedChildren.Count > 0)
            {
                var child = attachedChildren[attachedChildren.Count - 1];
                attachedChildren.RemoveAt(attachedChildren.Count - 1);
                child.OnEnded -= OnChildEnded;

                if (ReferenceEquals(child.attachedParent, this))
                {
                    child.attachedParent = null;
                }

                if (!child.IsActive)
                {
                    continue;
                }

                try
                {
                    child.End(ExecutionEndReason.Cancelled);
                }
                catch (Exception exception)
                {
                    failures.Add(exception);
                }
            }
        }

    #endregion

    #region Behavior 목록 검증

        // ------------------------------------------------------------
        /// <summary>
        /// Behavior 실행 인스턴스를 복사하고 null 항목을 검증한다.
        /// </summary>
        // ------------------------------------------------------------
        private static ExecutionBehaviorRuntime[] CopyBehavior
        (
            IReadOnlyList<ExecutionBehaviorRuntime> source
        )
        {
            if (source == null || source.Count == 0)
            {
                return Array.Empty<ExecutionBehaviorRuntime>();
            }

            var result = new ExecutionBehaviorRuntime[source.Count];

            for (var index = 0; index < source.Count; index++)
            {
                var behavior = source[index] ??
                    throw new ArgumentException
                    (
                        "Execution Behavior Runtime 목록에 null을 사용할 수 없습니다.",
                        nameof(source)
                    );

                for (var previous = 0; previous < index; previous++)
                {
                    if (ReferenceEquals(result[previous], behavior))
                    {
                        throw new ArgumentException
                        (
                            "같은 Execution Behavior Runtime을 중복 등록할 수 없습니다.",
                            nameof(source)
                        );
                    }
                }

                result[index] = behavior;
            }

            return result;
        }

    #endregion

    }
}