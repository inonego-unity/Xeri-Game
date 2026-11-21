/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : MotorState2D.cs
수정일 : 2026-11-21

# 설명
2D Rigidbody의 현재 물리 상태(위치·회전·선속도·각속도) 값을 정의한다.

# 특이사항, 제약사항
회전과 각속도는 Z축 기준이며 단위는 도, 도/초다. 네임스페이스는 폴더와 달리 Motor 하나를 쓴다.
========================================================================= BLOCK_HEADER_END */

using UnityEngine;

using inonego;
using inonego.Xeri;
using inonego.Xeri.Game;

namespace inonego.Xeri.Game.Motor
{
    // ============================================================
    /// <summary>
    /// 2D Rigidbody의 현재 물리 상태입니다.
    /// </summary>
    // ============================================================
    public readonly struct MotorState2D : IMotorKinematicState
    {

    #region 필드

        // ------------------------------------------------------------
        /// <summary>
        /// World Position입니다.
        /// </summary>
        // ------------------------------------------------------------
        public Vector2 Position { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// Z축 Rotation이며 단위는 도입니다.
        /// </summary>
        // ------------------------------------------------------------
        public float Rotation { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// World Linear Velocity입니다.
        /// </summary>
        // ------------------------------------------------------------
        public Vector2 LinearVelocity { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// Z축 Angular Velocity이며 단위는 도/초입니다.
        /// </summary>
        // ------------------------------------------------------------
        public float AngularVelocity { get; }

    #endregion

    #region 생성자

        // ------------------------------------------------------------
        /// <summary>
        /// 2D Body의 현재 상태를 구성합니다.
        /// </summary>
        // ------------------------------------------------------------
        public MotorState2D
        (
            Vector2 position, float rotation,
            Vector2 linearVelocity, float angularVelocity
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
        /// 선속도를 Z가 0인 월드 벡터로 읽습니다.
        /// </summary>
        // ------------------------------------------------------------
        public Vector3 WorldLinearVelocity => new(LinearVelocity.x, LinearVelocity.y, 0f);

        // ------------------------------------------------------------
        /// <summary>
        /// Z축 회전을 월드 회전으로 읽습니다.
        /// </summary>
        // ------------------------------------------------------------
        public Quaternion WorldRotation => Quaternion.Euler(0f, 0f, Rotation);

    #endregion

    }
}
