/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : ExecutionTimeWindowTrigger.cs
수정일 : 2026-10-31

# 설명
시간창과 진행 구간의 교차를 Tick마다 평가하는 조건.
========================================================================= BLOCK_HEADER_END */

using System;

using UnityEngine;

namespace inonego.Xeri.Game
{
    // ============================================================
    /// <summary>
    /// 지정된 시간 구간에 유효한 Trigger 조건을 표현하는 정의.
    /// </summary>
    // ============================================================
    [Serializable]
    public sealed class ExecutionTimeWindowTriggerDefinition : ExecutionTriggerDefinition
    {

    #region 설정

        // ------------------------------------------------------------
        /// <summary>
        /// 시간창의 시작 시각이다.
        /// </summary>
        // ------------------------------------------------------------
        public float BeginTime => beginTime;

        [SerializeField, Min(0f)]
        private float beginTime = 0f;

        // ------------------------------------------------------------
        /// <summary>
        /// 시간창의 끝 시각이며 이 시점은 제외한다.
        /// </summary>
        // ------------------------------------------------------------
        public float EndTime => endTime;

        [SerializeField, Min(0f)]
        private float endTime = 0f;

    #endregion

    #region 생성자

        // ------------------------------------------------------------
        /// <summary>
        /// Unity 직렬화용 기본 시간창을 구성한다.
        /// </summary>
        // ------------------------------------------------------------
        public ExecutionTimeWindowTriggerDefinition()
        {
            // NONE
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 시작과 종료 시각을 정규화하여 시간창을 정의한다.
        /// </summary>
        // ------------------------------------------------------------
        public ExecutionTimeWindowTriggerDefinition(float beginTime, float endTime)
        {
            this.beginTime = beginTime;
            this.endTime = endTime;
            Normalize(float.PositiveInfinity);
        }

    #endregion

    #region 생성·정규화

        // ------------------------------------------------------------
        /// <summary>
        /// 현재 정의의 시간창을 가진 실행별 Trigger를 생성한다.
        /// </summary>
        // ------------------------------------------------------------
        public override ExecutionTrigger CreateTrigger() => new ExecutionTimeWindowTrigger(this);

        // ------------------------------------------------------------
        /// <summary>
        /// 시간창의 시작과 끝을 유효 범위로 정규화한다.
        /// </summary>
        // ------------------------------------------------------------
        public override void Normalize(float maximumDuration)
        {
            var max = float.IsPositiveInfinity(maximumDuration)
                ? float.MaxValue
                : Mathf.Max(0f, maximumDuration);
            beginTime = Mathf.Clamp(beginTime, 0f, max);
            endTime = Mathf.Clamp(endTime, beginTime, max);
        }

    #endregion

    }

    // ============================================================
    /// <summary>
    /// 실행 Tick과 시간창의 교차 여부를 반복 평가하는 Trigger.
    /// </summary>
    // ============================================================
    internal sealed class ExecutionTimeWindowTrigger : ExecutionTrigger
    {

    #region 실행별 설정

        private readonly float beginTime;
        private readonly float endTime;

    #endregion

    #region 생성자

        // ------------------------------------------------------------
        /// <summary>
        /// 실행별 시간창 설정값을 복사하여 생성한다.
        /// </summary>
        // ------------------------------------------------------------
        internal ExecutionTimeWindowTrigger(ExecutionTimeWindowTriggerDefinition definition)
        {
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            beginTime = definition.BeginTime;
            endTime = definition.EndTime;
        }

    #endregion

    #region 시간창 판정

        // ------------------------------------------------------------
        /// <summary>
        /// 진행 Tick과 시간창의 교차 여부를 판정한다.
        /// </summary>
        // ------------------------------------------------------------
        public override bool TryEvaluate
        (
            float previousTime,
            float currentTime,
            out float effectiveTime
        )
        {
            effectiveTime = 0f;
            if
            (
                !(previousTime < currentTime) ||
                !(beginTime < endTime) ||
                !(previousTime < endTime) ||
                !(currentTime >= beginTime)
            )
            {
                return false;
            }

            effectiveTime = Mathf.Max(previousTime, beginTime);
            return true;
        }

    #endregion

    }
}
