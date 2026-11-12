/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : PhysicsVolume2D.cs
수정일 : 2026-08-21

# 설명
2D Physics Overlap과 Cast가 공유하는 월드 공간 Box·Circle·Capsule Volume 값을 정의한다.
Volume은 Query 정책, ContactFilter, 결과 Buffer를 소유하지 않는다.
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
    /// 2D Physics Volume의 형상 종류.
    /// </summary>
    // ============================================================
    public enum PhysicsShape2D
    {
        Box = 0,
        Circle = 1,
        Capsule = 2,
    }

    // ======================================================================
    /// <summary>
    /// 2D Physics Query에 전달할 월드 공간 형상과 Pose를 보존하는 값.
    /// </summary>
    // ======================================================================
    [Serializable]
    public readonly struct PhysicsVolume2D
    {

    #region 필드

        // ------------------------------------------------------------
        /// <summary>
        /// Volume 형상 종류.
        /// </summary>
        // ------------------------------------------------------------
        public PhysicsShape2D Shape { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// Volume 중심의 월드 위치.
        /// </summary>
        // ------------------------------------------------------------
        public Vector2 Position { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// Box와 Capsule의 Z축 월드 회전 각도.
        /// </summary>
        // ------------------------------------------------------------
        public float Angle { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// Box와 Capsule의 전체 월드 크기.
        /// </summary>
        // ------------------------------------------------------------
        public Vector2 Size { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// Circle의 월드 반지름.
        /// </summary>
        // ------------------------------------------------------------
        public float Radius { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// Capsule의 장축 방향.
        /// </summary>
        // ------------------------------------------------------------
        public CapsuleDirection2D CapsuleDirection { get; }

    #endregion

    #region 생성자

        // ----------------------------------------------------------------------
        /// <summary>
        /// 검증된 2D Physics Volume 값을 생성한다.
        /// </summary>
        // ----------------------------------------------------------------------
        private PhysicsVolume2D
        (
            PhysicsShape2D shape,
            Vector2 position,
            float angle,
            Vector2 size,
            float radius,
            CapsuleDirection2D capsuleDirection
        )
        {
            Shape = shape;
            Position = position;
            Angle = angle;
            Size = size;
            Radius = radius;
            CapsuleDirection = capsuleDirection;
        }

    #endregion

    #region 생성

        // ------------------------------------------------------------
        /// <summary>
        /// 월드 공간 Box Volume을 생성한다.
        /// </summary>
        // ------------------------------------------------------------
        public static PhysicsVolume2D CreateBox
        (
            Vector2 position,
            Vector2 size,
            float angle
        )
        {
            ValidatePositive(size, nameof(size));

            return new PhysicsVolume2D
            (
                PhysicsShape2D.Box,
                position,
                angle,
                size,
                0f,
                CapsuleDirection2D.Vertical
            );
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 월드 공간 Circle Volume을 생성한다.
        /// </summary>
        // ------------------------------------------------------------
        public static PhysicsVolume2D CreateCircle
        (
            Vector2 position,
            float radius
        )
        {
            ValidatePositive(radius, nameof(radius));

            return new PhysicsVolume2D
            (
                PhysicsShape2D.Circle,
                position,
                0f,
                Vector2.zero,
                radius,
                CapsuleDirection2D.Vertical
            );
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 월드 공간 Capsule Volume을 생성한다.
        /// </summary>
        // ------------------------------------------------------------
        public static PhysicsVolume2D CreateCapsule
        (
            Vector2 position,
            Vector2 size,
            CapsuleDirection2D direction,
            float angle
        )
        {
            ValidatePositive(size, nameof(size));

            var diameter = direction == CapsuleDirection2D.Vertical
                ? size.x
                : size.y;
            var length = direction == CapsuleDirection2D.Vertical
                ? size.y
                : size.x;

            if (length < diameter)
            {
                throw new ArgumentOutOfRangeException
                (
                    nameof(size),
                    "Capsule 장축 길이는 단축 길이 이상이어야 합니다."
                );
            }

            return new PhysicsVolume2D
            (
                PhysicsShape2D.Capsule,
                position,
                angle,
                size,
                0f,
                direction
            );
        }

    #endregion

    #region 복제

        // ------------------------------------------------------------
        /// <summary>
        /// 형상을 유지하고 월드 위치만 교체한 Volume을 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        public PhysicsVolume2D WithPosition(Vector2 position)
        {
            return new PhysicsVolume2D
            (
                Shape,
                position,
                Angle,
                Size,
                Radius,
                CapsuleDirection
            );
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 형상을 유지하고 월드 Pose를 교체한 Volume을 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        public PhysicsVolume2D WithPose
        (
            Vector2 position,
            float angle
        )
        {
            return new PhysicsVolume2D
            (
                Shape,
                position,
                Shape == PhysicsShape2D.Circle ? 0f : angle,
                Size,
                Radius,
                CapsuleDirection
            );
        }

    #endregion

    #region 검증

        // ------------------------------------------------------------
        /// <summary>
        /// 유한한 양수 형상 값을 요구한다.
        /// </summary>
        // ------------------------------------------------------------
        private static void ValidatePositive
        (
            float value,
            string parameterName
        )
        {
            if
            (
                !value.IsFinite() ||
                value <= 0f
            )
            {
                throw new ArgumentOutOfRangeException
                (
                    parameterName,
                    "Physics Volume 크기는 유한한 양수여야 합니다."
                );
            }
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 모든 축이 유한한 양수인 2D 크기를 요구한다.
        /// </summary>
        // ------------------------------------------------------------
        private static void ValidatePositive
        (
            Vector2 value,
            string parameterName
        )
        {
            ValidatePositive(value.x, parameterName);
            ValidatePositive(value.y, parameterName);
        }

    #endregion

    }
}
