/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : MotionIntent.cs
수정일 : 2026-11-21

# 설명
한 Tick 동안만 유지되는 기본 의도 속도를 정의한다.

# 특이사항, 제약사항
같은 Tick의 여러 의도는 채널별로 나중 값이 이긴다.
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
    /// <br/> 한 Tick 동안만 유지되는 기본 의도 속도.
    /// <br/> 같은 Tick의 여러 의도는 채널별로 나중 값이 이긴다.
    /// </summary>
    // ============================================================
    public readonly struct MotionIntent
    {

    #region 필드

        // ------------------------------------------------------------
        /// <summary>
        /// 의도가 다루는 채널.
        /// </summary>
        // ------------------------------------------------------------
        public MotionChannel Channels { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 선속도. Model의 Up 축으로 Planar·Elevation을 나눈다.
        /// </summary>
        // ------------------------------------------------------------
        public Vector3 Linear { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 각속도(rad/s).
        /// </summary>
        // ------------------------------------------------------------
        public Vector3 Angular { get; }

    #endregion

    #region 생성자

        // ------------------------------------------------------------
        /// <summary>
        /// 채널과 속도로 의도를 만든다.
        /// </summary>
        // ------------------------------------------------------------
        public MotionIntent(MotionChannel channels, Vector3 linear, Vector3 angular = default)
        {
            Channels = channels;
            Linear = linear;
            Angular = angular;
        }

    #endregion

    }
}
