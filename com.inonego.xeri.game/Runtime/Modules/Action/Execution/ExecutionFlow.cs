/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : ExecutionFlow.cs
수정일 : 2026-11-01

# 설명
Session의 적용 시간과 실행별 고정 설정으로 Flow 완료, Command 판정과 HoldRelease 해제 경계를 제공한다.
제어 키는 project-defined 문자열이며 Release enum을 Core에 고정하지 않는다.
========================================================================= BLOCK_HEADER_END */

using System;

using UnityEngine;

namespace inonego.Xeri.Game
{
    // ============================================================
    /// <summary>
    /// Execution 진행 규칙의 공유 가능한 authoring base.
    /// </summary>
    // ============================================================
    [Serializable]
    public abstract class ExecutionFlowDefinition
    {

    #region 시간 설정·생성

        // ------------------------------------------------------------
        /// <summary>
        /// Flow가 소유하는 최대 실행 시간을 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        public abstract float MaximumDuration { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 설정한 Flow 정의로부터 실행별 Runtime 객체를 생성한다.
        /// </summary>
        // ------------------------------------------------------------
        public abstract ExecutionFlowRuntime CreateRuntime();

        // ------------------------------------------------------------
        /// <summary>
        /// Flow의 시간 관련 입력을 유효한 상태로 정규화한다.
        /// </summary>
        // ------------------------------------------------------------
        public virtual void Normalize()
        {
            // NONE
        }

    #endregion

    }

    // ============================================================
    /// <summary>
    /// 한 ExecutionSession에 귀속되는 Flow 실행 상태 base.
    /// </summary>
    // ============================================================
    public abstract class ExecutionFlowRuntime
    {

    #region Session 소유권

        // ------------------------------------------------------------
        /// <summary>
        /// 이 Runtime을 단독 소유하는 ExecutionSession.
        /// </summary>
        // ------------------------------------------------------------
        internal ExecutionSession Session { get; private set; }

        // ------------------------------------------------------------
        /// <summary>
        /// 실행별 Runtime을 한 Session에 한 번만 귀속시킨다.
        /// </summary>
        // ------------------------------------------------------------
        internal void Bind(ExecutionSession session)
        {
            if (Session != null)
            {
                throw new InvalidOperationException("Execution Flow Runtime은 여러 Session에 연결할 수 없습니다.");
            }

            Session = session ?? throw new ArgumentNullException(nameof(session));
        }

    #endregion

    #region 진행 상태

        // ------------------------------------------------------------
        /// <summary>
        /// 진행이 종료 조건을 만족했는지 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        public abstract bool IsComplete { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 유지 구간에 머무는 Flow인지 나타낸다.
        /// </summary>
        // ------------------------------------------------------------
        public virtual bool IsHolding => false;

        // ------------------------------------------------------------
        /// <summary>
        /// 유지 상태에서 해제되었는지 여부.
        /// </summary>
        // ------------------------------------------------------------
        public virtual bool HasReleased => false;

        // ------------------------------------------------------------
        /// <summary>
        /// 유지 해제가 확정된 실행 시각.
        /// </summary>
        // ------------------------------------------------------------
        public virtual float ReleaseTime => 0f;

        // ------------------------------------------------------------
        /// <summary>
        /// 최대 유지 시간 대비 해제 진행 비율.
        /// </summary>
        // ------------------------------------------------------------
        public virtual float ReleaseProgress => 0f;

    #endregion

    #region 실행 수명과 Command

        // ------------------------------------------------------------
        /// <summary>
        /// Execution 시작 시 Flow의 시간 진행 상태를 초기화한다.
        /// </summary>
        // ------------------------------------------------------------
        public virtual void Begin(ExecutionSession session)
        {
            // NONE
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 경과 시간에 따라 Flow가 완료 조건으로 진행하게 한다.
        /// </summary>
        // ------------------------------------------------------------
        public abstract void Advance(ExecutionSession session, float deltaTime);

        // ------------------------------------------------------------
        /// <summary>
        /// 실행 명령을 판정하며 지원하지 않는 명령은 거부한다.
        /// </summary>
        // ------------------------------------------------------------
        public virtual ExecutionCommandResult Command(ExecutionSession session, in ExecutionCommand command)
        {
            return ExecutionCommandResult.Rejected;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Execution이 종료될 때 Flow의 수명을 함께 마무리한다.
        /// </summary>
        // ------------------------------------------------------------
        public virtual void End(ExecutionSession session, ExecutionEndReason reason)
        {
            // NONE
        }

    #endregion

    }

    // ======================================================================
    /// <summary>
    /// 지정 Duration까지 진행한 뒤 완료되는 Timeline Flow Definition.
    /// </summary>
    // ======================================================================
    [Serializable]
    public sealed class FixedTimelineExecutionFlowDefinition : ExecutionFlowDefinition
    {

    #region 설정

        // ------------------------------------------------------------
        /// <summary>
        /// 고정 Timeline의 설정된 지속 시간.
        /// </summary>
        // ------------------------------------------------------------
        public float Duration => duration;

        // ------------------------------------------------------------
        /// <summary>
        /// 설정된 Timeline의 최대 실행 시간.
        /// </summary>
        // ------------------------------------------------------------
        public override float MaximumDuration => duration;

        [SerializeField, Min(0.01f)]
        private float duration = 0.6f;

    #endregion

    #region 생성자

        // ------------------------------------------------------------
        /// <summary>
        /// Unity 직렬화용 기본 Timeline 정의를 만든다.
        /// </summary>
        // ------------------------------------------------------------
        public FixedTimelineExecutionFlowDefinition()
        {
            // NONE
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 지속시간을 정규화한 Timeline 정의를 생성한다.
        /// </summary>
        // ------------------------------------------------------------
        public FixedTimelineExecutionFlowDefinition(float duration)
        {
            this.duration = duration;
            Normalize();
        }

    #endregion

    #region 생성·정규화

        // ------------------------------------------------------------
        /// <summary>
        /// Timeline 진행 상태를 위한 Runtime을 생성한다.
        /// </summary>
        // ------------------------------------------------------------
        public override ExecutionFlowRuntime CreateRuntime() => new FixedTimelineExecutionFlowRuntime(this);

        // ------------------------------------------------------------
        /// <summary>
        /// Flow 설정값을 실행 가능한 범위로 정규화한다.
        /// </summary>
        // ------------------------------------------------------------
        public override void Normalize()
        {
            duration = Mathf.Max(0.01f, duration);
        }

    #endregion

    }

    // ============================================================
    /// <summary>
    /// 지정 시간까지 진행하면 완료되는 고정 시간형 실행 흐름.
    /// </summary>
    // ============================================================
    internal sealed class FixedTimelineExecutionFlowRuntime : ExecutionFlowRuntime
    {

    #region 실행별 상태

        private readonly float duration;
        private float elapsedTime = 0f;

        // ------------------------------------------------------------
        /// <summary>
        /// 설정한 Timeline 지속시간이 지난 상태이다.
        /// </summary>
        // ------------------------------------------------------------
        public override bool IsComplete => elapsedTime >= duration;

    #endregion

    #region 생성자

        // ------------------------------------------------------------
        /// <summary>
        /// Timeline 지속 시간을 실행별 불변 값으로 복사한다.
        /// </summary>
        // ------------------------------------------------------------
        internal FixedTimelineExecutionFlowRuntime(FixedTimelineExecutionFlowDefinition definition)
        {
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            duration = definition.Duration;
        }

    #endregion

    #region 시간 진행

        // ------------------------------------------------------------
        /// <summary>
        /// 현재 실행의 Flow 상태를 처음 값으로 초기화한다.
        /// </summary>
        // ------------------------------------------------------------
        public override void Begin(ExecutionSession session)
        {
            elapsedTime = 0f;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Session의 적용된 실행 시간으로 Timeline 완료 상태를 판정한다.
        /// </summary>
        // ------------------------------------------------------------
        public override void Advance(ExecutionSession session, float deltaTime)
        {
            elapsedTime = Mathf.Min(duration, session.Time);
        }

    #endregion

    }

    // ======================================================================
    /// <summary>
    /// 지정 Command 또는 최대 Hold 시간에서 Release되는 Flow Definition.
    /// </summary>
    // ======================================================================
    [Serializable]
    public sealed class HoldReleaseExecutionFlowDefinition : ExecutionFlowDefinition
    {

    #region 설정

        // ------------------------------------------------------------
        /// <summary>
        /// 유지 해제를 요청하는 Command의 키.
        /// </summary>
        // ------------------------------------------------------------
        public string ReleaseCommandKey => releaseCommandKey;

        [SerializeField]
        private string releaseCommandKey = "Release";

        // ------------------------------------------------------------
        /// <summary>
        /// 해제 명령이 처리될 수 있는 최소 유지 시간.
        /// </summary>
        // ------------------------------------------------------------
        public float MinHoldTime => minHoldTime;

        [SerializeField, Min(0f)]
        private float minHoldTime = 0f;

        // ------------------------------------------------------------
        /// <summary>
        /// 자동 해제가 발생하는 최대 유지 시간.
        /// </summary>
        // ------------------------------------------------------------
        public float MaxHoldTime => maxHoldTime;

        [SerializeField, Min(0.01f)]
        private float maxHoldTime = 1f;

        // ------------------------------------------------------------
        /// <summary>
        /// 해제 후 회복 구간의 길이.
        /// </summary>
        // ------------------------------------------------------------
        public float ReleaseRecoveryDuration => releaseRecoveryDuration;

        [SerializeField, Min(0f)]
        private float releaseRecoveryDuration = 0f;

        // ------------------------------------------------------------
        /// <summary>
        /// 최대 유지와 회복 시간을 합친 실행 상한.
        /// </summary>
        // ------------------------------------------------------------
        public override float MaximumDuration => maxHoldTime + releaseRecoveryDuration;

    #endregion

    #region 생성자

        // ------------------------------------------------------------
        /// <summary>
        /// Unity 직렬화용 기본 HoldRelease 정의를 만든다.
        /// </summary>
        // ------------------------------------------------------------
        public HoldReleaseExecutionFlowDefinition()
        {
            // NONE
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 유지 시간과 회복 시간 및 해제 명령을 지정한다.
        /// </summary>
        // ------------------------------------------------------------
        public HoldReleaseExecutionFlowDefinition
        (
            string releaseCommandKey,
            float minHoldTime,
            float maxHoldTime,
            float releaseRecoveryDuration
        )
        {
            this.releaseCommandKey = releaseCommandKey;
            this.minHoldTime = minHoldTime;
            this.maxHoldTime = maxHoldTime;
            this.releaseRecoveryDuration = releaseRecoveryDuration;
            Normalize();
        }

    #endregion

    #region 생성·정규화

        // ------------------------------------------------------------
        /// <summary>
        /// HoldRelease 진행 상태를 위한 Runtime을 생성한다.
        /// </summary>
        // ------------------------------------------------------------
        public override ExecutionFlowRuntime CreateRuntime() => new HoldReleaseExecutionFlowRuntime(this);

        // ------------------------------------------------------------
        /// <summary>
        /// Flow 설정값을 실행 가능한 범위로 정규화한다.
        /// </summary>
        // ------------------------------------------------------------
        public override void Normalize()
        {
            releaseCommandKey = string.IsNullOrWhiteSpace(releaseCommandKey)
                ? "Release"
                : releaseCommandKey.Trim();
            minHoldTime = Mathf.Max(0f, minHoldTime);
            maxHoldTime = Mathf.Max(0.01f, maxHoldTime);
            minHoldTime = Mathf.Min(minHoldTime, maxHoldTime);
            releaseRecoveryDuration = Mathf.Max(0f, releaseRecoveryDuration);
        }

    #endregion

    }

    // ============================================================
    /// <summary>
    /// 최소 유지 시간과 강제 해제 및 회복 구간을 관리하는 실행 흐름.
    /// </summary>
    // ============================================================
    internal sealed class HoldReleaseExecutionFlowRuntime : ExecutionFlowRuntime
    {

    #region 실행별 상태

        private readonly string releaseCommandKey;
        private readonly float minHoldTime;
        private readonly float maxHoldTime;
        private readonly float releaseRecoveryDuration;
        private readonly float maximumDuration;
        private float elapsedTime = 0f;
        private bool releaseRequested = false;

        // ------------------------------------------------------------
        /// <summary>
        /// 해제 이전 유지 구간에 있는 상태이다.
        /// </summary>
        // ------------------------------------------------------------
        public override bool IsHolding => !released;

        // ------------------------------------------------------------
        /// <summary>
        /// 유지 해제가 발생했는지 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        public override bool HasReleased => released;
        private bool released = false;

        // ------------------------------------------------------------
        /// <summary>
        /// 해제 시점의 실행 경과 시간이다.
        /// </summary>
        // ------------------------------------------------------------
        public override float ReleaseTime => releaseTime;
        private float releaseTime = 0f;

        // ------------------------------------------------------------
        /// <summary>
        /// 해제 후 회복 기간까지 지났는지 판정한다.
        /// </summary>
        // ------------------------------------------------------------
        public override bool IsComplete => released && elapsedTime >= releaseTime + releaseRecoveryDuration;

        // ------------------------------------------------------------
        /// <summary>
        /// 최대 유지 시간에 대한 해제 비율을 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        public override float ReleaseProgress => maxHoldTime <= Mathf.Epsilon
            ? 1f
            : Mathf.Clamp01(releaseTime / maxHoldTime);

    #endregion

    #region 생성자

        // ------------------------------------------------------------
        /// <summary>
        /// HoldRelease 설정을 실행별 불변 값으로 복사한다.
        /// </summary>
        // ------------------------------------------------------------
        internal HoldReleaseExecutionFlowRuntime(HoldReleaseExecutionFlowDefinition definition)
        {
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            releaseCommandKey = definition.ReleaseCommandKey;
            minHoldTime = definition.MinHoldTime;
            maxHoldTime = definition.MaxHoldTime;
            releaseRecoveryDuration = definition.ReleaseRecoveryDuration;
            maximumDuration = definition.MaximumDuration;
        }

    #endregion

    #region 시간 진행

        // ------------------------------------------------------------
        /// <summary>
        /// 현재 실행의 Flow 상태를 처음 값으로 초기화한다.
        /// </summary>
        // ------------------------------------------------------------
        public override void Begin(ExecutionSession session)
        {
            elapsedTime = 0f;
            releaseTime = 0f;
            releaseRequested = false;
            released = false;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Session 적용 시간으로 Hold 해제와 회복을 판정한다.
        /// </summary>
        // ------------------------------------------------------------
        public override void Advance(ExecutionSession session, float deltaTime)
        {
            elapsedTime = Mathf.Min(maximumDuration, session.Time);

            if (released)
            {
                return;
            }

            if (releaseRequested && elapsedTime >= minHoldTime)
            {
                Release(minHoldTime);
            }
            else if (elapsedTime >= maxHoldTime)
            {
                Release(maxHoldTime);
            }
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Release 명령을 수락하거나 최소 유지 시간까지 처리를 유예한다.
        /// </summary>
        // ------------------------------------------------------------
        public override ExecutionCommandResult Command(ExecutionSession session, in ExecutionCommand command)
        {
            if (!command.IsValid || !string.Equals(command.Key, releaseCommandKey, StringComparison.Ordinal) || released)
            {
                return ExecutionCommandResult.Rejected;
            }

            RequestRelease();
            return released ? ExecutionCommandResult.Accepted : ExecutionCommandResult.Deferred;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 현재 시간에서 해제가 가능한지 판단하고 대기 중인 요청을 보존한다.
        /// </summary>
        // ------------------------------------------------------------
        private void RequestRelease()
        {
            if (released)
            {
                return;
            }

            releaseRequested = true;
            if (elapsedTime >= minHoldTime)
            {
                Release(elapsedTime);
            }
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Hold를 종료하고 실제 시간 경계에 해제 시각을 고정한다.
        /// </summary>
        // ------------------------------------------------------------
        private void Release(float effectiveTime)
        {
            if (released)
            {
                return;
            }

            released = true;
            releaseTime = Mathf.Min(effectiveTime, maxHoldTime);
        }

    #endregion

    }
}
