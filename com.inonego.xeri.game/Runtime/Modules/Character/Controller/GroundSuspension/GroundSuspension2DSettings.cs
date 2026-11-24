/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : GroundSuspension2DSettings.cs
수정일 : 2026-08-02

# 설명
2D Floating Capsule 지지에서 사용하는 차원별 설정을 정의한다.
Physics2D의 접촉 여유를 반영한 GroundChecker2D 최소 감지 깊이를 제공한다.
========================================================================= BLOCK_HEADER_END */

using System;

using UnityEngine;

namespace inonego.Xeri.Game.Controller
{
    // ============================================================
    /// <summary>
    /// 2D Floating Capsule 지면 지지 조정값.
    /// </summary>
    // ============================================================
    [Serializable]
    public sealed class GroundSuspension2DSettings : GroundSuspensionSettings
    {
        // ------------------------------------------------------------
        /// <summary>
        /// GroundChecker2D가 확보해야 하는 최소 감지 깊이.
        /// </summary>
        // ------------------------------------------------------------
        public override float RequiredDetectionDepth => GetRequiredDetectionDepth
        (
            Physics2D.defaultContactOffset
        );
    }
}
