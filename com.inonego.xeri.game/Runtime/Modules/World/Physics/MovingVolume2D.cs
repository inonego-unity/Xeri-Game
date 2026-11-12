/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : MovingVolume2D.cs
수정일 : 2026-11-02

# 설명
2D Physics Volume의 위치·속도·수명을 함께 보존하는 중립 이동 상태를 정의한다.
충돌과 Target 의미를 소유하지 않고 이동 예측·속도 변경·상태 적용만 수행한다.
========================================================================= BLOCK_HEADER_END */

using System;

using UnityEngine;

using inonego;
using inonego.Xeri;
using inonego.Xeri.Primitive;

namespace inonego.Xeri.Game
{
    // ============================================================
    /// <summary>
    /// 2D Physics Volume의 이동과 수명 상태를 보존하는 값.
    /// </summary>
    // ============================================================
    [Serializable]
    public struct MovingVolume2D
    {

    #region 필드

        // ------------------------------------------------------------
        /// <summary>
        /// 현재 월드 공간 Physics Volume.
        /// </summary>
        // ------------------------------------------------------------
        public PhysicsVolume2D Volume { get; private set; }

        // ------------------------------------------------------------
        /// <summary>
        /// 현재 월드 선속도.
        /// </summary>
        // ------------------------------------------------------------
        public Vector2 Velocity { get; private set; }

        // ------------------------------------------------------------
        /// <summary>
        /// 전체 수명. PositiveInfinity이면 자동 만료하지 않는다.
        /// </summary>
        // ------------------------------------------------------------
        public float Lifetime { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 지금까지 진행한 수명 시간.
        /// </summary>
        // ------------------------------------------------------------
        public float ElapsedTime { get; private set; }

        // ------------------------------------------------------------
        /// <summary>
        /// 현재 수명이 종료되었는지 여부.
        /// </summary>
        // ------------------------------------------------------------
        public bool IsExpired =>
            !float.IsPositiveInfinity(Lifetime) && ElapsedTime >= Lifetime;

    #endregion

    #region 생성자

        // ------------------------------------------------------------
        /// <summary>
        /// Physics Volume과 초기 속도·수명으로 이동 상태를 생성한다.
        /// </summary>
        // ------------------------------------------------------------
        public MovingVolume2D
        (
            in PhysicsVolume2D volume,
            Vector2 velocity,
            float lifetime = float.PositiveInfinity
        ) : this()
        {
            ValidateFinite(velocity, nameof(velocity));
            ValidateLifetime(lifetime);

            Volume = volume;
            Velocity = velocity;
            Lifetime = lifetime;
            ElapsedTime = 0f;
        }

    #endregion

    #region 예측

        // ------------------------------------------------------------
        /// <summary>
        /// 현재 속도와 일정 Acceleration으로 이번 Step의 변위를 예측한다.
        /// </summary>
        // ------------------------------------------------------------
        public Vector2 PredictDisplacement
        (
            Vector2 acceleration,
            float deltaTime
        )
        {
            ValidateFinite(acceleration, nameof(acceleration));
            ValidateDeltaTime(deltaTime);

            return
                Velocity * deltaTime +
                acceleration * (0.5f * deltaTime * deltaTime);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 현재 속도와 일정 Acceleration으로 이번 Step 종료 속도를 예측한다.
        /// </summary>
        // ------------------------------------------------------------
        public Vector2 PredictVelocity
        (
            Vector2 acceleration,
            float deltaTime
        )
        {
            ValidateFinite(acceleration, nameof(acceleration));
            ValidateDeltaTime(deltaTime);

            return Velocity + acceleration * deltaTime;
        }

    #endregion

    #region 상태 적용

        // ------------------------------------------------------------
        /// <summary>
        /// 위치와 수명을 유지하면서 현재 속도를 설정한다.
        /// </summary>
        // ------------------------------------------------------------
        public void SetVelocity(Vector2 velocity)
        {
            ValidateFinite(velocity, nameof(velocity));
            Velocity = velocity;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 외부 Query가 확정한 Volume·속도와 경과 시간을 현재 상태에 적용한다.
        /// </summary>
        // ------------------------------------------------------------
        public void Commit
        (
            in PhysicsVolume2D volume,
            Vector2 velocity,
            float deltaTime
        )
        {
            ValidateFinite(velocity, nameof(velocity));
            ValidateDeltaTime(deltaTime);

            Volume = volume;
            Velocity = velocity;

            // 무한 수명은 경과 시간만 계속 누적하고 유한 수명은 상한에서 멈춘다.
            ElapsedTime = float.IsPositiveInfinity(Lifetime)
                ? ElapsedTime + deltaTime
                : Mathf.Min(Lifetime, ElapsedTime + deltaTime);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 충돌 보정이 필요 없는 Step을 현재 상태에 바로 적용한다.
        /// </summary>
        // ------------------------------------------------------------
        public void Advance
        (
            Vector2 acceleration,
            float deltaTime
        )
        {
            var displacement = PredictDisplacement(acceleration, deltaTime);
            var velocity = PredictVelocity(acceleration, deltaTime);
            var volume = Volume.WithPosition(Volume.Position + displacement);

            Commit(volume, velocity, deltaTime);
        }

    #endregion

    #region 검증

        // ------------------------------------------------------------
        /// <summary>
        /// 유한한 2D 벡터를 요구한다.
        /// </summary>
        // ------------------------------------------------------------
        private static void ValidateFinite
        (
            Vector2 value,
            string parameterName
        )
        {
            if (!value.IsFinite())
            {
                throw new ArgumentOutOfRangeException
                (
                    parameterName,
                    "Moving Volume 벡터는 유한한 값이어야 합니다."
                );
            }
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Step 시간은 유한한 0 이상의 값이어야 한다.
        /// </summary>
        // ------------------------------------------------------------
        private static void ValidateDeltaTime(float deltaTime)
        {
            if
            (
                !deltaTime.IsFinite() ||
                deltaTime < 0f
            )
            {
                throw new ArgumentOutOfRangeException
                (
                    nameof(deltaTime),
                    "deltaTime은 유한한 0 이상의 값이어야 합니다."
                );
            }
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 수명은 0보다 크거나 PositiveInfinity여야 한다.
        /// </summary>
        // ------------------------------------------------------------
        private static void ValidateLifetime(float lifetime)
        {
            if
            (
                lifetime <= 0f ||
                float.IsNaN(lifetime) ||
                float.IsNegativeInfinity(lifetime)
            )
            {
                throw new ArgumentOutOfRangeException
                (
                    nameof(lifetime),
                    "Lifetime은 0보다 크거나 PositiveInfinity여야 합니다."
                );
            }
        }

    #endregion

    }
}
