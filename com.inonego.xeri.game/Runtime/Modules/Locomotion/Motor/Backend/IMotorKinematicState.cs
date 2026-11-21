/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : IMotorKinematicState.cs
수정일 : 2026-11-21

# 설명
Body 상태 형식과 관계없이 월드 선속도·회전을 읽는 계약을 정의한다.

# 특이사항, 제약사항
MovementState 계산에 쓴다. MotorState2D·MotorState3D가 구현한다.
========================================================================= BLOCK_HEADER_END */

using System;

using UnityEngine;

using inonego;
using inonego.Xeri;
using inonego.Xeri.Game;
using inonego.Xeri.Game.Motion;

namespace inonego.Xeri.Game.Motor
{
    // ============================================================
    /// <summary>
    /// <br/> Body 상태 형식과 관계없이 월드 선속도·회전을 읽는 계약입니다.
    /// <br/> MovementState 계산에 사용합니다.
    /// </summary>
    // ============================================================
    public interface IMotorKinematicState
    {
        // ------------------------------------------------------------
        /// <summary>
        /// 월드 선속도입니다.
        /// </summary>
        // ------------------------------------------------------------
        Vector3 WorldLinearVelocity { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 월드 회전입니다.
        /// </summary>
        // ------------------------------------------------------------
        Quaternion WorldRotation { get; }
    }
}
