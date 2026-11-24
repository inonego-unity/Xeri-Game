/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : MotionModifierState.cs
수정일 : 2026-11-22

# 설명
Model이 가진 Modifier 항목 하나의 현재 상태를 읽기 전용 값으로 정의한다.

# 특이사항, 제약사항
Model 내부 Modifier 인스턴스를 노출하지 않으려고 값만 복사해 돌려준다.
보임 여부는 마지막 Step의 합성 결과 기준이다. 가려진 동안 시간을 멈출지는 사용처가 이 값으로 정한다.
========================================================================= BLOCK_HEADER_END */

using System;

using UnityEngine;

using inonego;
using inonego.Xeri;
using inonego.Xeri.Serializable;

namespace inonego.Xeri.Game.Motion
{
    // ============================================================
    /// <summary>
    /// Modifier 항목 하나의 현재 상태.
    /// </summary>
    // ============================================================
    public readonly struct MotionModifierState
    {

    #region 필드

        // ------------------------------------------------------------
        /// <summary>
        /// 항목 키와 수명 세대.
        /// </summary>
        // ------------------------------------------------------------
        public MotionSourceKey Source { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 적용 순서.
        /// </summary>
        // ------------------------------------------------------------
        public int Order { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 채널 값에 적용하는 연산.
        /// </summary>
        // ------------------------------------------------------------
        public NumericFOperation Operation { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 적용 채널과 채널별 값.
        /// </summary>
        // ------------------------------------------------------------
        public MotionChannelValues Values { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 사용처가 해석하는 동작 식별자.
        /// </summary>
        // ------------------------------------------------------------
        public int ActionID { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 항목이 시작된 Tick.
        /// </summary>
        // ------------------------------------------------------------
        public long StartTick { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 한 Tick만 적용하는 충격 여부.
        /// </summary>
        // ------------------------------------------------------------
        public bool IsImpulse { get; }

        // ----------------------------------------------------------------------
        /// <summary>
        /// 마지막 Step에서 하나 이상의 채널에서 뒤의 SET에 가려지지 않았는지 나타낸다.
        /// </summary>
        // ----------------------------------------------------------------------
        public bool IsVisible { get; }

    #endregion

    #region 생성자

        internal MotionModifierState
        (
            MotionSourceKey source, int order, NumericFOperation operation, in MotionChannelValues values,
            int actionID, long startTick, bool isImpulse, bool isVisible
        )
        {
            Source = source;
            Order = order;
            Operation = operation;
            Values = values;
            ActionID = actionID;
            StartTick = startTick;
            IsImpulse = isImpulse;
            IsVisible = isVisible;
        }

    #endregion

    }
}
