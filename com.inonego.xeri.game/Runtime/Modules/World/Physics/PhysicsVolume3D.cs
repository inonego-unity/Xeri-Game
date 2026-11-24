/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : PhysicsVolume3D.cs
수정일 : 2026-08-21

# 설명
3D Physics Overlap과 Cast가 공유하는 월드 공간 Box·Sphere·Capsule Volume 값을 정의한다.
Volume은 Query 정책, Layer, Trigger, 결과 Buffer를 소유하지 않는다.
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
    /// 3D Physics Volume의 형상 종류.
    /// </summary>
    // ============================================================
    public enum PhysicsShape3D
    {
        Box = 0,
        Sphere = 1,
        Capsule = 2,
    }

    // ======================================================================
    /// <summary>
    /// 3D Physics Query에 전달할 월드 공간 형상과 Pose를 보존하는 값.
    /// </summary>
    // ======================================================================
    [Serializable]
    public readonly struct PhysicsVolume3D
    {

    #region 필드

        // ------------------------------------------------------------
        /// <summary>
        /// Volume 형상 종류.
        /// </summary>
        // ------------------------------------------------------------
        public PhysicsShape3D Shape { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// Volume 중심의 월드 위치.
        /// </summary>
        // ------------------------------------------------------------
        public Vector3 Position { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// Box와 Capsule의 월드 회전.
        /// </summary>
        // ------------------------------------------------------------
        public Quaternion Rotation { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// Box의 전체 월드 크기.
        /// </summary>
        // ------------------------------------------------------------
        public Vector3 Size { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// Sphere와 Capsule의 월드 반지름.
        /// </summary>
        // ------------------------------------------------------------
        public float Radius { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// Capsule의 로컬 Y축 기준 전체 월드 높이.
        /// </summary>
        // ------------------------------------------------------------
        public float Height { get; }

    #endregion

    #region 생성자

        // ----------------------------------------------------------------------
        /// <summary>
        /// 검증된 3D Physics Volume 값을 생성한다.
        /// </summary>
        // ----------------------------------------------------------------------
        private PhysicsVolume3D
        (
            PhysicsShape3D shape,
            Vector3 position,
            Quaternion rotation,
            Vector3 size,
            float radius,
            float height
        )
        {
            Shape = shape;
            Position = position;
            Rotation = rotation;
            Size = size;
            Radius = radius;
            Height = height;
        }

    #endregion

    #region 생성

        // ------------------------------------------------------------
        /// <summary>
        /// 월드 공간 Box Volume을 생성한다.
        /// </summary>
        // ------------------------------------------------------------
        public static PhysicsVolume3D CreateBox
        (
            Vector3 position,
            Vector3 size,
            Quaternion rotation
        )
        {
            ValidatePositive(size, nameof(size));

            return new PhysicsVolume3D
            (
                PhysicsShape3D.Box,
                position,
                rotation,
                size,
                0f,
                0f
            );
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 월드 공간 Sphere Volume을 생성한다.
        /// </summary>
        // ------------------------------------------------------------
        public static PhysicsVolume3D CreateSphere
        (
            Vector3 position,
            float radius
        )
        {
            ValidatePositive(radius, nameof(radius));

            return new PhysicsVolume3D
            (
                PhysicsShape3D.Sphere,
                position,
                Quaternion.identity,
                Vector3.zero,
                radius,
                radius * 2f
            );
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// 로컬 Y축을 길이 방향으로 사용하는 월드 공간 Capsule Volume을 생성한다.
        /// </summary>
        // ----------------------------------------------------------------------
        public static PhysicsVolume3D CreateCapsule
        (
            Vector3 position,
            float radius,
            float height,
            Quaternion rotation
        )
        {
            ValidatePositive(radius, nameof(radius));
            ValidatePositive(height, nameof(height));

            if (height < radius * 2f)
            {
                throw new ArgumentOutOfRangeException
                (
                    nameof(height),
                    "Capsule height는 diameter 이상이어야 합니다."
                );
            }

            return new PhysicsVolume3D
            (
                PhysicsShape3D.Capsule,
                position,
                rotation,
                Vector3.zero,
                radius,
                height
            );
        }

    #endregion

    #region 복제

        // ------------------------------------------------------------
        /// <summary>
        /// 형상을 유지하고 월드 위치만 교체한 Volume을 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        public PhysicsVolume3D WithPosition(Vector3 position)
        {
            return new PhysicsVolume3D
            (
                Shape,
                position,
                Rotation,
                Size,
                Radius,
                Height
            );
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 형상을 유지하고 월드 Pose를 교체한 Volume을 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        public PhysicsVolume3D WithPose
        (
            Vector3 position,
            Quaternion rotation
        )
        {
            return new PhysicsVolume3D
            (
                Shape,
                position,
                Shape == PhysicsShape3D.Sphere ? Quaternion.identity : rotation,
                Size,
                Radius,
                Height
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
        /// 모든 축이 유한한 양수인 Box 크기를 요구한다.
        /// </summary>
        // ------------------------------------------------------------
        private static void ValidatePositive
        (
            Vector3 value,
            string parameterName
        )
        {
            ValidatePositive(value.x, parameterName);
            ValidatePositive(value.y, parameterName);
            ValidatePositive(value.z, parameterName);
        }

    #endregion

    }
}
