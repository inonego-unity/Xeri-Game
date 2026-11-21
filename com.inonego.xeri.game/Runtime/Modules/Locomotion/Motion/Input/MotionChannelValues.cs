/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : MotionChannelValues.cs
수정일 : 2026-11-22

# 설명
Modifier 항목이 채널별 MValue에 넣는 값(Planar·Elevation·Rotation)과 적용 채널을 정의한다.

# 특이사항, 제약사항
값은 연산과 무관하게 그대로 채널 Modifier의 피연산자가 된다. 의미(속도, 배율 등)는 연산이 정한다.
Planar 값은 투영하지 않고 넣는다. Model이 Planar 채널을 접은 뒤 결과만 Up에 수직인 평면에 투영한다.
Linear로 만든 값은 월드 선속도를 그대로 보관하고 Model이 매 Tick 현재 Up으로 Elevation을 나눈다.
그래서 Up이 바뀌어도 월드 방향이 유지된다. 직접 지정한 Elevation은 Up 방향 크기라 Up을 따라 돈다.
========================================================================= BLOCK_HEADER_END */

using System;

using UnityEngine;

using inonego;
using inonego.Xeri;

namespace inonego.Xeri.Game.Motion
{
    // ============================================================
    /// <summary>
    /// Modifier 항목의 채널별 값.
    /// </summary>
    // ============================================================
    public readonly struct MotionChannelValues
    {

    #region 필드

        // ------------------------------------------------------------
        /// <summary>
        /// 값을 넣을 채널.
        /// </summary>
        // ------------------------------------------------------------
        public MotionChannel Channels { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// Planar 채널 값(월드 벡터). IsWorldLinear면 월드 선속도 전체다.
        /// </summary>
        // ------------------------------------------------------------
        public Vector3 Planar { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// Elevation 채널 값(Up 방향 스칼라).
        /// </summary>
        // ------------------------------------------------------------
        public float Elevation { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// Rotation 채널 값.
        /// </summary>
        // ------------------------------------------------------------
        public Vector3 Rotation { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> Planar에 월드 선속도를 담는다.
        /// <br/> Elevation은 Model이 현재 Up으로 나눈다.
        /// <br/> Linear로 만든 값이 이 형태다.
        /// </summary>
        // ------------------------------------------------------------
        public bool IsWorldLinear { get; }

    #endregion

    #region 생성

        // ----------------------------------------------------------------------
        /// <summary>
        /// 채널과 채널별 값으로 만든다. 켜지 않은 채널의 값은 Model이 0으로 바꿔 보관한다.
        /// </summary>
        // ----------------------------------------------------------------------
        public MotionChannelValues(MotionChannel channels, Vector3 planar = default, float elevation = 0f, Vector3 rotation = default)
            : this(channels, planar, elevation, rotation, false)
        {
            // NONE
        }

        internal MotionChannelValues(MotionChannel channels, Vector3 planar, float elevation, Vector3 rotation, bool isWorldLinear)
        {
            Channels = channels;
            Planar = planar;
            Elevation = elevation;
            Rotation = rotation;
            IsWorldLinear = isWorldLinear;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> 월드 선속도와 각속도로 만든다.
        /// <br/> Model이 매 Tick 현재 Up으로 Planar·Elevation을 나누므로
        /// <br/> Up이 바뀌는 Tick에도 바뀐 뒤에도 월드 방향이 유지된다.
        /// </summary>
        // ------------------------------------------------------------
        public static MotionChannelValues Linear(MotionChannel channels, Vector3 linear, Vector3 angular = default) =>
            new(channels, linear, 0f, angular, true);

        // ------------------------------------------------------------
        /// <summary>
        /// 모든 채널·성분에 같은 값을 넣는다(MUL 배율 등).
        /// </summary>
        // ------------------------------------------------------------
        public static MotionChannelValues Uniform(MotionChannel channels, float value)
        {
            var vector = new Vector3(value, value, value);

            return new MotionChannelValues(channels, vector, value, vector);
        }

    #endregion

    }
}
