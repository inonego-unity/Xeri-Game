/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : HoldReleaseExecutionFlow.cs
수정일 : 2026-11-19

# 설명
지정 Command 또는 최대 Hold 시간에서 Release되고 회복 구간 뒤 완료되는 Flow를 제공한다.
제어 키는 project-defined 문자열이며 Release enum을 Core에 고정하지 않는다.
========================================================================= BLOCK_HEADER_END */

using System;

using UnityEngine;

namespace inonego.Xeri.Game
{
    // ============================================================
    /// <summary>
    /// 지정 Command 또는 최대 Hold 시간에서 Release되는 Flow Spec.
    /// </summary>
    // ============================================================
    [Serializable]
    public sealed class HoldReleaseExecutionFlowSpec : ExecutionFlowSpec
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
        /// Unity 직렬화용 기본 HoldRelease Spec을 만든다.
        /// </summary>
        // ------------------------------------------------------------
        public HoldReleaseExecutionFlowSpec()
        {
            // NONE
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 유지 시간과 회복 시간 및 해제 명령을 지정한다.
        /// </summary>
        // ------------------------------------------------------------
        public HoldReleaseExecutionFlowSpec
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
        internal HoldReleaseExecutionFlowRuntime(HoldReleaseExecutionFlowSpec spec)
        {
            if (spec == null)
            {
                throw new ArgumentNullException(nameof(spec));
            }

            releaseCommandKey = spec.ReleaseCommandKey;
            minHoldTime = spec.MinHoldTime;
            maxHoldTime = spec.MaxHoldTime;
            releaseRecoveryDuration = spec.ReleaseRecoveryDuration;
            maximumDuration = spec.MaximumDuration;
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
