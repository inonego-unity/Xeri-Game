/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : MotorState3D.cs
수정일 : 2026-11-21

# 설명
3D Rigidbody의 현재 물리 상태(위치·회전·선속도·각속도) 값을 정의한다.

# 특이사항, 제약사항
각속도 단위는 라디안/초다. 네임스페이스는 폴더와 달리 Motor 하나를 쓴다.
========================================================================= BLOCK_HEADER_END */

using UnityEngine;

using inonego;
using inonego.Xeri;
using inonego.Xeri.Game;

namespace inonego.Xeri.Game.Motor
{
    // ============================================================
    /// <summary>
    /// 3D Rigidbody의 현재 물리 상태입니다.
    /// </summary>
    // ============================================================
    public readonly struct MotorState3D : IMotorKinematicState
    {

    #region 필드

        // ------------------------------------------------------------
        /// <summary>
        /// World Position입니다.
        /// </summary>
        // ------------------------------------------------------------
        public Vector3 Position { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// World Rotation입니다.
        /// </summary>
        // ------------------------------------------------------------
        public Quaternion Rotation { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// World Linear Velocity입니다.
        /// </summary>
        // ------------------------------------------------------------
        public Vector3 LinearVelocity { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// World Angular Velocity이며 단위는 라디안/초입니다.
        /// </summary>
        // ------------------------------------------------------------
        public Vector3 AngularVelocity { get; }

    #endregion

    #region 생성자

        // ------------------------------------------------------------
        /// <summary>
        /// 3D Body의 현재 상태를 구성합니다.
        /// </summary>
        // ------------------------------------------------------------
        public MotorState3D
        (
            Vector3 position, Quaternion rotation,
            Vector3 linearVelocity, Vector3 angularVelocity
        )
        {
            Position = position;
            Rotation = rotation;
            LinearVelocity = linearVelocity;
            AngularVelocity = angularVelocity;
        }

    #endregion

    #region 인터페이스 구현

        // ------------------------------------------------------------
        /// <summary>
        /// 월드 선속도입니다.
        /// </summary>
        // ------------------------------------------------------------
        public Vector3 WorldLinearVelocity => LinearVelocity;

        // ------------------------------------------------------------
        /// <summary>
        /// 월드 회전입니다.
        /// </summary>
        // ------------------------------------------------------------
        public Quaternion WorldRotation => Rotation;

    #endregion

    }
}
