/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : PhysicsQuery3D.cs
수정일 : 2026-08-21

# 설명
3D Raycast와 Box·Sphere·Capsule Overlap/Cast의 NonAlloc 호출 조립을 공통화한다.
Collider와 RaycastHit 결과, LayerMask와 Trigger 정책은 Unity 계약을 그대로 사용한다.
========================================================================= BLOCK_HEADER_END */

using System;

using UnityEngine;

using inonego;
using inonego.Xeri;
using inonego.Xeri.Primitive;

namespace inonego.Xeri.Game
{
    // ======================================================================
    /// <summary>
    /// 3D Physics의 Raycast·Overlap·Cast를 공통 Volume 계약으로 실행한다.
    /// </summary>
    // ======================================================================
    public static class PhysicsQuery3D
    {

    #region Raycast

        // ----------------------------------------------------------------------
        /// <summary>
        /// 지정한 선 구간의 Raycast 결과를 재사용 Buffer에 기록한다.
        /// </summary>
        // ----------------------------------------------------------------------
        public static int Raycast
        (
            Vector3 origin,
            Vector3 direction,
            float distance,
            RaycastHit[] results,
            LayerMask layerMask,
            QueryTriggerInteraction triggerInteraction = QueryTriggerInteraction.UseGlobal
        )
        {
            ValidateResults(results);
            ValidateDistance(distance);

            if (!TryNormalizeDirection(direction, out var normalized))
            {
                return 0;
            }

            return Physics.RaycastNonAlloc
            (
                origin,
                normalized,
                results,
                distance,
                layerMask,
                triggerInteraction
            );
        }

    #endregion

    #region Overlap

        // ----------------------------------------------------------------------
        /// <summary>
        /// 지정한 3D Volume과 현재 겹치는 Collider를 재사용 Buffer에 기록한다.
        /// </summary>
        // ----------------------------------------------------------------------
        public static int Overlap
        (
            in PhysicsVolume3D volume,
            Collider[] results,
            LayerMask layerMask,
            QueryTriggerInteraction triggerInteraction = QueryTriggerInteraction.UseGlobal
        )
        {
            ValidateResults(results);

            return volume.Shape switch
            {
                PhysicsShape3D.Box => Physics.OverlapBoxNonAlloc
                (
                    volume.Position,
                    volume.Size * 0.5f,
                    results,
                    volume.Rotation,
                    layerMask,
                    triggerInteraction
                ),
                PhysicsShape3D.Sphere => Physics.OverlapSphereNonAlloc
                (
                    volume.Position,
                    volume.Radius,
                    results,
                    layerMask,
                    triggerInteraction
                ),
                PhysicsShape3D.Capsule => OverlapCapsule
                (
                    volume,
                    results,
                    layerMask,
                    triggerInteraction
                ),
                _ => throw new ArgumentOutOfRangeException(nameof(volume)),
            };
        }

    #endregion

    #region Cast

        // ----------------------------------------------------------------------
        /// <summary>
        /// 지정한 3D Volume을 방향과 거리만큼 Cast하고 결과를 재사용 Buffer에 기록한다.
        /// </summary>
        // ----------------------------------------------------------------------
        public static int Cast
        (
            in PhysicsVolume3D volume,
            Vector3 direction,
            float distance,
            RaycastHit[] results,
            LayerMask layerMask,
            QueryTriggerInteraction triggerInteraction = QueryTriggerInteraction.UseGlobal
        )
        {
            ValidateResults(results);
            ValidateDistance(distance);

            if (distance <= 0f || !TryNormalizeDirection(direction, out var normalized))
            {
                return 0;
            }

            return volume.Shape switch
            {
                PhysicsShape3D.Box => Physics.BoxCastNonAlloc
                (
                    volume.Position,
                    volume.Size * 0.5f,
                    normalized,
                    results,
                    volume.Rotation,
                    distance,
                    layerMask,
                    triggerInteraction
                ),
                PhysicsShape3D.Sphere => Physics.SphereCastNonAlloc
                (
                    volume.Position,
                    volume.Radius,
                    normalized,
                    results,
                    distance,
                    layerMask,
                    triggerInteraction
                ),
                PhysicsShape3D.Capsule => CastCapsule
                (
                    volume,
                    normalized,
                    distance,
                    results,
                    layerMask,
                    triggerInteraction
                ),
                _ => throw new ArgumentOutOfRangeException(nameof(volume)),
            };
        }

    #endregion

    #region Capsule

        // ----------------------------------------------------------------------
        /// <summary>
        /// Capsule Volume의 현재 Overlap을 실행한다.
        /// </summary>
        // ----------------------------------------------------------------------
        private static int OverlapCapsule
        (
            in PhysicsVolume3D volume,
            Collider[] results,
            LayerMask layerMask,
            QueryTriggerInteraction triggerInteraction
        )
        {
            GetCapsulePoints(volume, out var point1, out var point2);

            return Physics.OverlapCapsuleNonAlloc
            (
                point1,
                point2,
                volume.Radius,
                results,
                layerMask,
                triggerInteraction
            );
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// Capsule Volume의 Cast를 실행한다.
        /// </summary>
        // ----------------------------------------------------------------------
        private static int CastCapsule
        (
            in PhysicsVolume3D volume,
            Vector3 direction,
            float distance,
            RaycastHit[] results,
            LayerMask layerMask,
            QueryTriggerInteraction triggerInteraction
        )
        {
            GetCapsulePoints(volume, out var point1, out var point2);

            return Physics.CapsuleCastNonAlloc
            (
                point1,
                point2,
                volume.Radius,
                direction,
                results,
                distance,
                layerMask,
                triggerInteraction
            );
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// 로컬 Y축 Capsule의 두 반구 중심을 월드 공간에서 계산한다.
        /// </summary>
        // ----------------------------------------------------------------------
        private static void GetCapsulePoints
        (
            in PhysicsVolume3D volume,
            out Vector3 point1,
            out Vector3 point2
        )
        {
            var halfSegment = Mathf.Max
            (
                0f,
                volume.Height * 0.5f - volume.Radius
            );
            var axis = volume.Rotation * Vector3.up * halfSegment;

            point1 = volume.Position + axis;
            point2 = volume.Position - axis;
        }

    #endregion

    #region 검증

        // ------------------------------------------------------------
        /// <summary>
        /// 결과 Buffer가 존재하는지 확인한다.
        /// </summary>
        // ------------------------------------------------------------
        private static void ValidateResults(Array results)
        {
            if (results == null)
            {
                throw new ArgumentNullException(nameof(results));
            }
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Physics Query 거리가 0 이상이며 NaN이 아닌지 확인한다.
        /// </summary>
        // ------------------------------------------------------------
        private static void ValidateDistance(float distance)
        {
            if (distance < 0f || float.IsNaN(distance))
            {
                throw new ArgumentOutOfRangeException
                (
                    nameof(distance),
                    "Physics Query 거리는 0 이상의 값이어야 합니다."
                );
            }
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// 유한한 방향 벡터를 정규화하고 영벡터 여부를 반환한다.
        /// </summary>
        // ----------------------------------------------------------------------
        private static bool TryNormalizeDirection
        (
            Vector3 direction,
            out Vector3 normalized
        )
        {
            if (!direction.IsFinite())
            {
                throw new ArgumentOutOfRangeException
                (
                    nameof(direction),
                    "Physics Query 방향은 유한한 Vector3여야 합니다."
                );
            }

            if (direction.sqrMagnitude <= Mathf.Epsilon)
            {
                normalized = Vector3.zero;
                return false;
            }

            normalized = direction.normalized;
            return true;
        }

    #endregion

    }
}
