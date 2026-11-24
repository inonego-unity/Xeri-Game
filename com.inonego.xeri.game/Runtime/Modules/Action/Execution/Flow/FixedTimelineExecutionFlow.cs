/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : FixedTimelineExecutionFlow.cs
수정일 : 2026-11-19

# 설명
지정 Duration까지 Session 적용 시간으로 진행한 뒤 완료되는 고정 Timeline Flow를 제공한다.
========================================================================= BLOCK_HEADER_END */

using System;

using UnityEngine;

namespace inonego.Xeri.Game
{
    // ============================================================
    /// <summary>
    /// 지정 Duration까지 진행한 뒤 완료되는 Timeline Flow Spec.
    /// </summary>
    // ============================================================
    [Serializable]
    public sealed class FixedTimelineExecutionFlowSpec : ExecutionFlowSpec
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
        /// Unity 직렬화용 기본 Timeline Spec을 만든다.
        /// </summary>
        // ------------------------------------------------------------
        public FixedTimelineExecutionFlowSpec()
        {
            // NONE
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 지속시간을 정규화한 Timeline Spec을 생성한다.
        /// </summary>
        // ------------------------------------------------------------
        public FixedTimelineExecutionFlowSpec(float duration)
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
        internal FixedTimelineExecutionFlowRuntime(FixedTimelineExecutionFlowSpec spec)
        {
            if (spec == null)
            {
                throw new ArgumentNullException(nameof(spec));
            }

            duration = spec.Duration;
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
}
