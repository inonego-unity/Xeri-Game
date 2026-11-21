/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : MotionScaleCommand.cs
수정일 : 2026-11-21

# 설명
채널·층별 제어 배율 항목의 추가·제거 명령과 배율을 적용할 층(MotionLayer)을 정의한다.

# 특이사항, 제약사항
연산 규칙은 Core NumericFOperation을 쓰며 NumericFModifier.Modify와 같다.
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
    /// 채널·층별 제어 배율 항목의 추가·제거 명령.
    /// </summary>
    // ============================================================
    public readonly struct MotionScaleCommand
    {

    #region 필드

        // ------------------------------------------------------------
        /// <summary>
        /// 추가(Set) 또는 제거(Remove).
        /// </summary>
        // ------------------------------------------------------------
        public MotionCommandAction Action { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 배율 항목 식별자.
        /// </summary>
        // ------------------------------------------------------------
        public MotionSourceKey Source { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 배율을 적용할 채널.
        /// </summary>
        // ------------------------------------------------------------
        public MotionChannel Channels { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 배율을 적용할 층.
        /// </summary>
        // ------------------------------------------------------------
        public MotionLayer Targets { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 적용 순서. 낮을수록 먼저 적용한다.
        /// </summary>
        // ------------------------------------------------------------
        public int Order { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 연산. 규칙은 Core NumericFModifier와 같다.
        /// </summary>
        // ------------------------------------------------------------
        public NumericFOperation Operation { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 연산 값.
        /// </summary>
        // ------------------------------------------------------------
        public float Value { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 지속 시간(초). 0이면 제거 명령까지 유지한다.
        /// </summary>
        // ------------------------------------------------------------
        public float Duration { get; }

    #endregion

    #region 생성

        private MotionScaleCommand
        (
            MotionCommandAction action, MotionSourceKey source, MotionChannel channels,
            MotionLayer targets, int order, NumericFOperation operation, float value, float duration
        )
        {
            Action = action;
            Source = source;
            Channels = channels;
            Targets = targets;
            Order = order;
            Operation = operation;
            Value = value;
            Duration = duration;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 배율 항목을 추가한다.
        /// </summary>
        // ------------------------------------------------------------
        public static MotionScaleCommand Add
        (
            MotionSourceKey source, MotionChannel channels, MotionLayer targets,
            int order, NumericFOperation operation, float value, float duration = 0f
        ) => new(MotionCommandAction.Set, source, channels, targets, order, operation, value, duration);

        // ------------------------------------------------------------
        /// <summary>
        /// 배율 항목을 제거한다.
        /// </summary>
        // ------------------------------------------------------------
        public static MotionScaleCommand Remove(MotionSourceKey source) =>
            new(MotionCommandAction.Remove, source, MotionChannel.None, MotionLayer.None, 0, NumericFOperation.SET, 0f, 0f);

    #endregion

    }

    // ============================================================
    /// <summary>
    /// 제어 배율을 적용할 대상 층.
    /// </summary>
    // ============================================================
    [Flags]
    public enum MotionLayer
    {
        None = 0,
        Intent = 1,
        Override = 2,
        All = Intent | Override
    }
}
