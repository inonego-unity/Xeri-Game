/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : PhysicsQuery2D.cs
수정일 : 2026-08-21

# 설명
2D Raycast와 Box·Circle·Capsule Overlap/Cast의 NonAlloc 호출 조립을 공통화한다.
Collider2D와 RaycastHit2D 결과, ContactFilter2D 정책은 Unity 계약을 그대로 사용한다.
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
    /// 2D Physics의 Raycast·Overlap·Cast를 공통 Volume 계약으로 실행한다.
    /// </summary>
    // ======================================================================
    public static class PhysicsQuery2D
    {

    #region Raycast

        // ----------------------------------------------------------------------
        /// <summary>
        /// 지정한 선 구간의 Raycast 결과를 재사용 Buffer에 기록한다.
        /// </summary>
        // ----------------------------------------------------------------------
        public static int Raycast
        (
            Vector2 origin,
            Vector2 direction,
            float distance,
            ContactFilter2D filter,
            RaycastHit2D[] results
        )
        {
            ValidateResults(results);
            ValidateDistance(distance);

            if (!TryNormalizeDirection(direction, out var normalized))
            {
                return 0;
            }

            return Physics2D.Raycast
            (
                origin,
                normalized,
                filter,
                results,
                distance
            );
        }

    #endregion

    #region Overlap

        // ----------------------------------------------------------------------
        /// <summary>
        /// 지정한 2D Volume과 현재 겹치는 Collider2D를 재사용 Buffer에 기록한다.
        /// </summary>
        // ----------------------------------------------------------------------
        public static int Overlap
        (
            in PhysicsVolume2D volume,
            ContactFilter2D filter,
            Collider2D[] results
        )
        {
            ValidateResults(results);

            return volume.Shape switch
            {
                PhysicsShape2D.Box => Physics2D.OverlapBox
                (
                    volume.Position,
                    volume.Size,
                    volume.Angle,
                    filter,
                    results
                ),
                PhysicsShape2D.Circle => Physics2D.OverlapCircle
                (
                    volume.Position,
                    volume.Radius,
                    filter,
                    results
                ),
                PhysicsShape2D.Capsule => Physics2D.OverlapCapsule
                (
                    volume.Position,
                    volume.Size,
                    volume.CapsuleDirection,
                    volume.Angle,
                    filter,
                    results
                ),
                _ => throw new ArgumentOutOfRangeException(nameof(volume)),
            };
        }

    #endregion

    #region Cast

        // ----------------------------------------------------------------------
        /// <summary>
        /// 지정한 2D Volume을 방향과 거리만큼 Cast하고 결과를 재사용 Buffer에 기록한다.
        /// </summary>
        // ----------------------------------------------------------------------
        public static int Cast
        (
            in PhysicsVolume2D volume,
            Vector2 direction,
            float distance,
            ContactFilter2D filter,
            RaycastHit2D[] results
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
                PhysicsShape2D.Box => Physics2D.BoxCast
                (
                    volume.Position,
                    volume.Size,
                    volume.Angle,
                    normalized,
                    filter,
                    results,
                    distance
                ),
                PhysicsShape2D.Circle => Physics2D.CircleCast
                (
                    volume.Position,
                    volume.Radius,
                    normalized,
                    filter,
                    results,
                    distance
                ),
                PhysicsShape2D.Capsule => Physics2D.CapsuleCast
                (
                    volume.Position,
                    volume.Size,
                    volume.CapsuleDirection,
                    volume.Angle,
                    normalized,
                    filter,
                    results,
                    distance
                ),
                _ => throw new ArgumentOutOfRangeException(nameof(volume)),
            };
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
            Vector2 direction,
            out Vector2 normalized
        )
        {
            if (!direction.IsFinite())
            {
                throw new ArgumentOutOfRangeException
                (
                    nameof(direction),
                    "Physics Query 방향은 유한한 Vector2여야 합니다."
                );
            }

            if (direction.sqrMagnitude <= Mathf.Epsilon)
            {
                normalized = Vector2.zero;
                return false;
            }

            normalized = direction.normalized;
            return true;
        }

    #endregion

    }
}
