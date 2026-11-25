/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : ExecutionTimeIntervalTrigger.cs
수정일 : 2026-11-24

# 설명
시간창 안에서 일정한 간격마다 발동하는 조건.
발동 시각은 beginTime + n × interval(n = 0, 1, 2 ...)이며 endTime 이후와 maxCount번째 이후는 발동하지 않는다.

# 특이사항, 제약사항
한 번의 TryEvaluate는 발동 하나만 반환하므로 큰 시간 간격에 여러 주기가 들어 있으면 false가 나올 때까지 같은 구간으로 반복 호출해
놓친 주기를 모두 받는다. 각 호출의 effectiveTime은 그 주기의 발동 시각이며 오름차순이다.
이번 구간 앞에서 끝난 주기(호출자가 평가하지 못한 구간)는 발동 없이 건너뛴다.
간격은 최소 MinimumInterval이며 그보다 작은 값은 정규화한다.
========================================================================= BLOCK_HEADER_END */

using System;

using UnityEngine;

namespace inonego.Xeri.Game
{
    // ============================================================
    /// <summary>
    /// <br/> 시간창 안에서 간격마다 발동하는 조건의 Spec.
    /// <br/> 한 Tick에 여러 주기가 들어 있으면 반복 호출로 받는다.
    /// </summary>
    // ============================================================
    [Serializable]
    public sealed class ExecutionTimeIntervalTriggerSpec : ExecutionTriggerSpec
    {

    #region 설정

        // ------------------------------------------------------------
        /// <summary>
        /// 허용하는 가장 짧은 발동 간격이다.
        /// </summary>
        // ------------------------------------------------------------
        public const float MinimumInterval = 0.001f;

        // ------------------------------------------------------------
        /// <summary>
        /// 첫 발동 시각이자 시간창의 시작 시각이다.
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

        // ------------------------------------------------------------
        /// <summary>
        /// 발동 사이의 시간 간격이다.
        /// </summary>
        // ------------------------------------------------------------
        public float Interval => interval;

        [SerializeField, Min(MinimumInterval)]
        private float interval = 1f;

        // ------------------------------------------------------------
        /// <summary>
        /// 발동 횟수 한도이며 0이면 시간창 끝까지 제한이 없다.
        /// </summary>
        // ------------------------------------------------------------
        public int MaxCount => maxCount;

        [SerializeField, Min(0)]
        private int maxCount = 0;

    #endregion

    #region 생성자

        // ------------------------------------------------------------
        /// <summary>
        /// Unity 직렬화용 기본 값을 구성한다.
        /// </summary>
        // ------------------------------------------------------------
        public ExecutionTimeIntervalTriggerSpec()
        {
            // NONE
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 시간창과 간격, 횟수 한도를 정규화하여 정의한다.
        /// </summary>
        // ------------------------------------------------------------
        public ExecutionTimeIntervalTriggerSpec(float beginTime, float endTime, float interval, int maxCount = 0)
        {
            this.beginTime = beginTime;
            this.endTime = endTime;
            this.interval = interval;
            this.maxCount = maxCount;
            Normalize(float.PositiveInfinity);
        }

    #endregion

    #region 생성·정규화

        // ------------------------------------------------------------
        /// <summary>
        /// 현재 Spec의 설정을 가진 실행별 Trigger를 생성한다.
        /// </summary>
        // ------------------------------------------------------------
        public override ExecutionTrigger CreateTrigger() => new ExecutionTimeIntervalTrigger(this);

        // ------------------------------------------------------------
        /// <summary>
        /// 시간창을 유효 범위로, 간격과 횟수 한도를 허용 범위로 정규화한다.
        /// </summary>
        // ------------------------------------------------------------
        public override void Normalize(float maximumDuration)
        {
            var max = float.IsPositiveInfinity(maximumDuration)
                ? float.MaxValue
                : Mathf.Max(0f, maximumDuration);
            beginTime = Mathf.Clamp(beginTime, 0f, max);
            endTime = Mathf.Clamp(endTime, beginTime, max);
            interval = float.IsNaN(interval) ? MinimumInterval : Mathf.Max(interval, MinimumInterval);
            maxCount = Mathf.Max(0, maxCount);
        }

    #endregion

    }

    // ============================================================
    /// <summary>
    /// 실행 시간이 주기 발동 시각을 지날 때마다 판정하는 Trigger 실행 상태.
    /// </summary>
    // ============================================================
    internal sealed class ExecutionTimeIntervalTrigger : ExecutionTrigger
    {

    #region 실행별 설정·상태

        private readonly float beginTime;
        private readonly float endTime;
        private readonly float interval;
        private readonly int maxCount;

        // 다음에 판정할 발동 번호(0부터)이다.
        private int nextIndex = 0;

    #endregion

    #region 생성자

        // ------------------------------------------------------------
        /// <summary>
        /// 실행별 설정값을 복사하여 생성한다.
        /// </summary>
        // ------------------------------------------------------------
        internal ExecutionTimeIntervalTrigger(ExecutionTimeIntervalTriggerSpec spec)
        {
            if (spec == null)
            {
                throw new ArgumentNullException(nameof(spec));
            }

            beginTime = spec.BeginTime;
            endTime = spec.EndTime;
            interval = Mathf.Max(spec.Interval, ExecutionTimeIntervalTriggerSpec.MinimumInterval);
            maxCount = Mathf.Max(0, spec.MaxCount);
        }

    #endregion

    #region 실행 수명과 평가

        // ------------------------------------------------------------
        /// <summary>
        /// 발동 번호를 처음으로 되돌린다.
        /// </summary>
        // ------------------------------------------------------------
        public override void Begin()
        {
            nextIndex = 0;
        }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/> 이번 구간에 들어 있는 다음 주기 발동을 판정한다.
        /// <br/> 한 번에 하나만 반환하므로 한 구간에 주기가 여럿이면 false가 나올 때까지 반복 호출한다.
        /// <br/> 구간 앞에서 이미 끝난 주기는 발동 없이 건너뛴다.
        /// </summary>
        // --------------------------------------------------------------------------------
        public override bool TryEvaluate
        (
            float previousTime,
            float currentTime,
            out float effectiveTime
        )
        {
            effectiveTime = 0f;
            if (!(previousTime < currentTime))
            {
                return false;
            }

            while (maxCount == 0 || nextIndex < maxCount)
            {
                var time = beginTime + nextIndex * interval;

                // 시간창은 반열린 [begin, end)이고 아직 오지 않은 주기는 다음 호출에서 판정한다.
                if (!(time < endTime) || time > currentTime)
                {
                    return false;
                }

                nextIndex++;

                // 구간 시작 시각의 주기는 이전 구간이 이미 소유하며, 0초 주기만 첫 구간이 소유한다.
                if (time > previousTime || (previousTime <= 0f && time <= 0f))
                {
                    effectiveTime = time;
                    return true;
                }
            }

            return false;
        }

    #endregion

    }
}
