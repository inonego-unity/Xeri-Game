/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : MotionExternalCommand.cs
수정일 : 2026-11-21

# 설명
외부 속도 항목의 설정·제거·전체 제거 명령을 정의한다.

# 특이사항, 제약사항
연산은 SET·ADD·SUB만 쓰며 같은 키를 다시 설정하면 갱신이다.
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
    /// 외부 속도 항목의 설정·제거·전체 제거 명령.
    /// </summary>
    // ============================================================
    public readonly struct MotionExternalCommand
    {

    #region 필드

        // ------------------------------------------------------------
        /// <summary>
        /// 설정(Set), 제거(Remove), 전체 제거(Clear).
        /// </summary>
        // ------------------------------------------------------------
        public MotionCommandAction Action { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 외부 속도 항목 식별자.
        /// </summary>
        // ------------------------------------------------------------
        public MotionSourceKey Source { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 적용 순서. 낮을수록 먼저 적용한다.
        /// </summary>
        // ------------------------------------------------------------
        public int Order { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 연산. SET·ADD·SUB만 사용한다.
        /// </summary>
        // ------------------------------------------------------------
        public NumericFOperation Operation { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 선속도 값.
        /// </summary>
        // ------------------------------------------------------------
        public Vector3 Velocity { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 지수 감쇠율(1/초). 0이면 감쇠하지 않는다.
        /// </summary>
        // ------------------------------------------------------------
        public float Decay { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 지속 시간(초). 0이면 제거 명령이나 감쇠 종료까지 유지한다.
        /// </summary>
        // ------------------------------------------------------------
        public float Duration { get; }

    #endregion

    #region 생성

        private MotionExternalCommand
        (
            MotionCommandAction action, MotionSourceKey source, int order,
            NumericFOperation operation, Vector3 velocity, float decay, float duration
        )
        {
            Action = action;
            Source = source;
            Order = order;
            Operation = operation;
            Velocity = velocity;
            Decay = decay;
            Duration = duration;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> 외부 속도 항목을 추가하거나 같은 식별자의 항목을 갱신한다.
        /// <br/> 갱신은 시작 순서를 유지하고 남은 시간을 다시 채운다.
        /// </summary>
        // ------------------------------------------------------------
        public static MotionExternalCommand Set
        (
            MotionSourceKey source, int order, NumericFOperation operation,
            Vector3 velocity, float decay = 0f, float duration = 0f
        ) => new(MotionCommandAction.Set, source, order, operation, velocity, decay, duration);

        // ------------------------------------------------------------
        /// <summary>
        /// 외부 속도 항목을 제거한다.
        /// </summary>
        // ------------------------------------------------------------
        public static MotionExternalCommand Remove(MotionSourceKey source) =>
            new(MotionCommandAction.Remove, source, 0, NumericFOperation.SET, Vector3.zero, 0f, 0f);

        // ------------------------------------------------------------
        /// <summary>
        /// 모든 외부 속도 항목을 제거한다.
        /// </summary>
        // ------------------------------------------------------------
        public static MotionExternalCommand Clear() =>
            new(MotionCommandAction.Clear, default, 0, NumericFOperation.SET, Vector3.zero, 0f, 0f);

    #endregion

    }
}
