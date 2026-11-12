/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : GroundSuspensionSettings.cs
수정일 : 2026-08-03

# 설명
2D/3D Floating Capsule 지면 지지에 공통으로 사용하는 조정값을 정의한다.
각 차원별 설정은 물리 엔진의 접촉 여유를 반영한 최소 감지 깊이를 제공한다.
========================================================================= BLOCK_HEADER_END */

using System;

using UnityEngine;

namespace inonego.Xeri.Game.Controller
{
    // ============================================================
    /// <summary>
    /// 2D/3D 지면 지지에 공통으로 사용하는 조정값.
    /// </summary>
    // ============================================================
    [Serializable]
    public abstract class GroundSuspensionSettings
    {

    #region 필드

        // ------------------------------------------------------------
        /// <summary>
        /// GroundChecker가 확보해야 하는 최소 감지 깊이.
        /// </summary>
        // ------------------------------------------------------------
        public abstract float RequiredDetectionDepth { get; }

        [Min(0f)]
        public float TargetHeight = 0.2f;

        [Min(0f)]
        public float MaximumDistance = 0.55f;

        [Range(0f, 89f)]
        public float MaximumSlopeAngle = 60f;

        // ------------------------------------------------------------
        /// <summary>
        /// 최대 지지 경사에서 허용할 최소 지면 법선 정렬도.
        /// </summary>
        // ------------------------------------------------------------
        public float MinimumGroundAlignment => Mathf.Cos
        (
            Mathf.Clamp(MaximumSlopeAngle, 0f, 89f) * Mathf.Deg2Rad
        );

        [Min(0f)]
        public float Strength = 400f;

        [Min(0f)]
        public float Damping = 40f;

        [Min(0f)]
        public float MaxAcceleration = 100f;

    #endregion

    #region 메서드

        // ------------------------------------------------------------
        /// <summary>
        /// 물리 엔진의 접촉 여유를 반영한 최소 감지 깊이를 계산한다.
        /// </summary>
        // ------------------------------------------------------------
        protected float GetRequiredDetectionDepth(float contactOffset) => Mathf.Max
        (
            MaximumDistance,
            TargetHeight + contactOffset
        );

    #endregion

    }
}
