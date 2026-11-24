/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : GroundSuspension3DSettings.cs
수정일 : 2026-08-02

# 설명
3D Floating Capsule 지지에서 사용하는 차원별 설정을 정의한다.
Physics의 접촉 여유를 반영한 GroundChecker3D 최소 감지 깊이를 제공한다.
========================================================================= BLOCK_HEADER_END */

using System;

using UnityEngine;

namespace inonego.Xeri.Game.Controller
{
    // ============================================================
    /// <summary>
    /// 3D Floating Capsule 지면 지지 조정값.
    /// </summary>
    // ============================================================
    [Serializable]
    public sealed class GroundSuspension3DSettings : GroundSuspensionSettings
    {
        // ------------------------------------------------------------
        /// <summary>
        /// GroundChecker3D가 확보해야 하는 최소 감지 깊이.
        /// </summary>
        // ------------------------------------------------------------
        public override float RequiredDetectionDepth => GetRequiredDetectionDepth
        (
            Physics.defaultContactOffset
        );
    }
}
