/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : ExecutionTimePointTrigger.cs
수정일 : 2026-10-31

# 설명
지정 시점을 처음 통과할 때 한 번 발동하는 조건.
========================================================================= BLOCK_HEADER_END */

using System;

using UnityEngine;

namespace inonego.Xeri.Game
{
    // ============================================================
    /// <summary>
    /// 지정 시간 지점의 Trigger 조건을 표현하는 정의.
    /// </summary>
    // ============================================================
    [Serializable]
    public sealed class ExecutionTimePointTriggerDefinition : ExecutionTriggerDefinition
    {

    #region 설정

        // ------------------------------------------------------------
        /// <summary>
        /// 한 번 감지할 실행 시점.
        /// </summary>
        // ------------------------------------------------------------
        public float Time => time;

        [SerializeField, Min(0f)]
        private float time = 0f;

    #endregion

    #region 생성자

        // ------------------------------------------------------------
        /// <summary>
        /// Unity 직렬화용 기본 시간 지점 정의를 생성한다.
        /// </summary>
        // ------------------------------------------------------------
        public ExecutionTimePointTriggerDefinition()
        {
            // NONE
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 지정한 시간으로 지점 Trigger를 구성한다.
        /// </summary>
        // ------------------------------------------------------------
        public ExecutionTimePointTriggerDefinition(float time)
        {
            this.time = Mathf.Max(0f, time);
        }

    #endregion

    #region 생성·정규화

        // ------------------------------------------------------------
        /// <summary>
        /// 현재 정의의 시간값을 가진 실행별 Trigger를 생성한다.
        /// </summary>
        // ------------------------------------------------------------
        public override ExecutionTrigger CreateTrigger() => new ExecutionTimePointTrigger(this);

        // ------------------------------------------------------------
        /// <summary>
        /// 지점 시각을 실행의 유효 범위로 제한한다.
        /// </summary>
        // ------------------------------------------------------------
        public override void Normalize(float maximumDuration)
        {
            time = Mathf.Clamp(time, 0f, Mathf.Max(0f, maximumDuration));
        }

    #endregion

    }

    // ============================================================
    /// <summary>
    /// 지정 시간 통과를 한 번 감지하는 Trigger 실행 상태.
    /// </summary>
    // ============================================================
    internal sealed class ExecutionTimePointTrigger : ExecutionTrigger
    {

    #region 실행별 설정·상태

        private readonly float time;
        private bool hasFired = false;

    #endregion

    #region 생성자

        // ------------------------------------------------------------
        /// <summary>
        /// 실행별 시간 설정값을 복사하여 생성한다.
        /// </summary>
        // ------------------------------------------------------------
        internal ExecutionTimePointTrigger(ExecutionTimePointTriggerDefinition definition)
        {
            time = definition?.Time ?? throw new ArgumentNullException(nameof(definition));
        }

    #endregion

    #region 실행 수명과 평가

        // ------------------------------------------------------------
        /// <summary>
        /// 지정 시점 감지에 필요한 이전 상태를 초기화한다.
        /// </summary>
        // ------------------------------------------------------------
        public override void Begin()
        {
            hasFired = false;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 실행 시간이 지정 시점을 통과했는지 한 번 판정한다.
        /// </summary>
        // ------------------------------------------------------------
        public override bool TryEvaluate
        (
            float previousTime,
            float currentTime,
            out float effectiveTime
        )
        {
            effectiveTime = time;
            if (hasFired || currentTime <= previousTime) return false;

            var crossed =
                time <= currentTime &&
                (time > previousTime ||
                 (previousTime <= 0f && time <= 0f));

            if (crossed)
            {
                hasFired = true;
            }

            return crossed;
        }

    #endregion

    }
}
