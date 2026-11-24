/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : PhysicsDelivery2D.cs
수정일 : 2026-11-02

# 설명
PhysicsQuery2D의 raw Collider2D/Hit를 게임 도메인과 독립된 DeliveryContact2D로 정규화한다.
제한형 재사용 Workspace가 버퍼 수명과 포화·재진입을 관리하며 결과 완전성을 노출한다.
========================================================================= BLOCK_HEADER_END */

using System;

using UnityEngine;

namespace inonego.Xeri.Game
{
    // ============================================================
    /// <summary>
    /// Projectile이 사용할 현재 Volume 겹침과 이동 Cast 질의 계약.
    /// </summary>
    // ============================================================
    public interface IProjectileDeliveryQuery2D
    {
        // ------------------------------------------------------------
        /// <summary>
        /// 겹치는 유효 대상의 존재와 검색 완전성을 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        DeliveryContactQueryResult2D QueryAnyOverlap
        (
            in PhysicsVolume2D volume,
            ContactFilter2D contactFilter,
            PhysicsDeliveryWorkspace2D workspace,
            IDeliveryColliderFilter2D filter = null
        );

        // ------------------------------------------------------------
        /// <summary>
        /// 이동 중 가장 가까운 Contact와 검색 완전성을 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        DeliveryContactQueryResult2D QueryCast
        (
            in PhysicsVolume2D volume,
            Vector2 displacement,
            ContactFilter2D contactFilter,
            PhysicsDeliveryWorkspace2D workspace,
            IDeliveryColliderFilter2D filter = null
        );
    }

    // ============================================================
    /// <summary>
    /// 차원별 Unity 물리 후보 형식을 지정한 재사용 Workspace.
    /// </summary>
    // ============================================================
    public sealed class PhysicsDeliveryWorkspace2D :
        PhysicsDeliveryWorkspaceBase<Collider2D, RaycastHit2D, DeliveryContact2D>
    {

    #region 생성자

        // ------------------------------------------------------------
        /// <summary>
        /// 초기 및 최대 버퍼 용량을 지정하여 Workspace를 생성한다.
        /// </summary>
        // ------------------------------------------------------------
        public PhysicsDeliveryWorkspace2D(int capacity = 256, int maximumCapacity = 256)
            : base(capacity, maximumCapacity)
        {
            // NONE
        }

    #endregion

    }

    // ============================================================
    /// <summary>
    /// <br/>Unity Physics 결과를 Contact로 정규화한다.
    /// <br/>검색 완전성을 Contact 존재 여부와 별도로 보고한다.
    /// </summary>
    // ============================================================
    public sealed class PhysicsDelivery2D : IProjectileDeliveryQuery2D
    {

    #region Volume 후보 수집

        // ------------------------------------------------------------
        /// <summary>
        /// <br/>호출자가 소유한 Workspace에 Volume 후보를 수집한다.
        /// <br/>포화 상태에서는 반환된 Contact 목록이 일부일 수 있다.
        /// </summary>
        // ------------------------------------------------------------
        public DeliveryBatchResult QueryVolume
        (
            in PhysicsVolume2D volume,
            ContactFilter2D contactFilter,
            PhysicsDeliveryWorkspace2D queryWorkspace,
            IDeliveryColliderFilter2D filter = null
        )
        {
            if (queryWorkspace == null)
            {
                throw new ArgumentNullException(nameof(queryWorkspace));
            }

            queryWorkspace.Begin();

            try
            {
                var count = ReadOverlap(volume, contactFilter, queryWorkspace);

                // Filter는 물리 후보 추출이 끝난 다음에 실행한다.
                for (var index = 0; index < count; index++)
                {
                    var collider = queryWorkspace.Overlap[index];
                    if (!IsAccepted(collider, filter))
                    {
                        continue;
                    }

                    queryWorkspace.AddContact(CreateOverlapContact(volume, collider, Vector2.zero));
                }

                return new DeliveryBatchResult(queryWorkspace.ContactCount, count >= queryWorkspace.Capacity);
            }
            catch
            {
                queryWorkspace.ResetContacts();
                throw;
            }
            finally
            {
                queryWorkspace.End();
            }
        }

    #endregion

    #region Ray Trace

        // ------------------------------------------------------------
        /// <summary>
        /// 지정된 Workspace로 Trace의 최소 거리 Contact를 검색한다.
        /// </summary>
        // ------------------------------------------------------------
        public DeliveryContactQueryResult2D QueryTrace
        (
            Vector2 origin,
            Vector2 direction,
            float distance,
            ContactFilter2D contactFilter,
            PhysicsDeliveryWorkspace2D queryWorkspace,
            IDeliveryColliderFilter2D filter = null
        )
        {
            if (queryWorkspace == null)
            {
                throw new ArgumentNullException(nameof(queryWorkspace));
            }

            queryWorkspace.Begin();

            try
            {
                var count = ReadRaycast(origin, direction, distance, contactFilter, queryWorkspace);
                return FindNearest(count, direction, filter, queryWorkspace);
            }
            finally
            {
                queryWorkspace.End();
            }
        }

    #endregion

    #region 겹침 존재 조회

        // ------------------------------------------------------------
        /// <summary>
        /// 겹침 후보 하나의 존재와 버퍼 완전성을 판정한다.
        /// </summary>
        // ------------------------------------------------------------
        public DeliveryContactQueryResult2D QueryAnyOverlap
        (
            in PhysicsVolume2D volume,
            ContactFilter2D contactFilter,
            PhysicsDeliveryWorkspace2D queryWorkspace,
            IDeliveryColliderFilter2D filter = null
        )
        {
            if (queryWorkspace == null)
            {
                throw new ArgumentNullException(nameof(queryWorkspace));
            }

            queryWorkspace.Begin();

            try
            {
                var count = ReadOverlap(volume, contactFilter, queryWorkspace);
                Collider2D selected = null;

                // 물리 반환 순서를 사용하지 않고 현재 후보의 식별자 기준으로 선택한다.
                for (var index = 0; index < count; index++)
                {
                    var candidate = queryWorkspace.Overlap[index];
                    if (!IsAccepted(candidate, filter))
                    {
                        continue;
                    }

                    if (selected == null || candidate.GetHashCode() < selected.GetHashCode())
                    {
                        selected = candidate;
                    }
                }

                var isComplete = count < queryWorkspace.Capacity;
                return new DeliveryContactQueryResult2D
                (
                    selected != null,
                    isComplete,
                    selected != null
                        ? CreateOverlapContact(volume, selected, Vector2.zero)
                        : default
                );
            }
            finally
            {
                queryWorkspace.End();
            }
        }

    #endregion

    #region 이동 Cast

        // ------------------------------------------------------------
        /// <summary>
        /// 이동 구간에서 가장 가까운 유효 접촉을 판정한다.
        /// </summary>
        // ------------------------------------------------------------
        public DeliveryContactQueryResult2D QueryCast
        (
            in PhysicsVolume2D volume,
            Vector2 displacement,
            ContactFilter2D contactFilter,
            PhysicsDeliveryWorkspace2D queryWorkspace,
            IDeliveryColliderFilter2D filter = null
        )
        {
            if (queryWorkspace == null)
            {
                throw new ArgumentNullException(nameof(queryWorkspace));
            }

            queryWorkspace.Begin();

            try
            {
                var distance = displacement.magnitude;
                if (distance <= Mathf.Epsilon)
                {
                    return new DeliveryContactQueryResult2D(false, true);
                }

                var direction = displacement / distance;
                var count = ReadCast(volume, direction, distance, contactFilter, queryWorkspace);
                return FindNearest(count, direction, filter, queryWorkspace);
            }
            finally
            {
                queryWorkspace.End();
            }
        }

    #endregion

    #region 포화 시 제한형 재조회

        // ------------------------------------------------------------
        /// <summary>
        /// 포화된 Overlap은 최대 용량까지 재질의한다.
        /// </summary>
        // ------------------------------------------------------------
        private static int ReadOverlap
        (
            in PhysicsVolume2D volume,
            ContactFilter2D contactFilter,
            PhysicsDeliveryWorkspace2D queryWorkspace
        )
        {
            int count;

            do
            {
                count = PhysicsQuery2D.Overlap
                (
                    volume,
                    contactFilter,
                    queryWorkspace.Overlap
                );
            }
            while (count >= queryWorkspace.Capacity && queryWorkspace.TryGrow());

            return count;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 포화된 Raycast는 최대 용량까지 재질의한다.
        /// </summary>
        // ------------------------------------------------------------
        private static int ReadRaycast
        (
            Vector2 origin,
            Vector2 direction,
            float distance,
            ContactFilter2D contactFilter,
            PhysicsDeliveryWorkspace2D queryWorkspace
        )
        {
            int count;

            do
            {
                count = PhysicsQuery2D.Raycast
                (
                    origin,
                    direction,
                    distance,
                    contactFilter,
                    queryWorkspace.Cast
                );
            }
            while (count >= queryWorkspace.Capacity && queryWorkspace.TryGrow());

            return count;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 포화된 Cast는 최대 용량까지 재질의한다.
        /// </summary>
        // ------------------------------------------------------------
        private static int ReadCast
        (
            in PhysicsVolume2D volume,
            Vector2 direction,
            float distance,
            ContactFilter2D contactFilter,
            PhysicsDeliveryWorkspace2D queryWorkspace
        )
        {
            int count;

            do
            {
                count = PhysicsQuery2D.Cast
                (
                    volume,
                    direction,
                    distance,
                    contactFilter,
                    queryWorkspace.Cast
                );
            }
            while (count >= queryWorkspace.Capacity && queryWorkspace.TryGrow());

            return count;
        }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/>받은 후보에서 최소 거리와 동거리 Collider2D 식별 해시로 Contact를 고른다.
        /// <br/>식별 해시 동점 규칙은 Unity 실행 간 결정론을 보장하지 않는다.
        /// </summary>
        // --------------------------------------------------------------------------------
        private static DeliveryContactQueryResult2D FindNearest
        (
            int count,
            Vector2 direction,
            IDeliveryColliderFilter2D filter,
            PhysicsDeliveryWorkspace2D queryWorkspace
        )
        {
            var selected = default(RaycastHit2D);
            var selectedDistance = float.PositiveInfinity;
            var selectedHash = int.MaxValue;
            var found = false;

            for (var index = 0; index < count; index++)
            {
                var candidate = queryWorkspace.Cast[index];
                if (!IsAccepted(candidate.collider, filter) || float.IsNaN(candidate.distance))
                {
                    continue;
                }

                var candidateHash = candidate.collider.GetHashCode();
                if
                (
                    candidate.distance > selectedDistance ||
                    (candidate.distance == selectedDistance && candidateHash >= selectedHash)
                )
                {
                    continue;
                }

                selected = candidate;
                selectedDistance = candidate.distance;
                selectedHash = candidateHash;
                found = true;
            }

            var isComplete = count < queryWorkspace.Capacity;
            if (!found)
            {
                return new DeliveryContactQueryResult2D(false, isComplete);
            }

            var contact = new DeliveryContact2D
            (
                selected.collider,
                selected.point,
                selected.normal,
                direction.sqrMagnitude > Mathf.Epsilon ? direction.normalized : Vector2.zero,
                selected.distance
            );
            return new DeliveryContactQueryResult2D(true, isComplete, contact);
        }

    #endregion

    #region Contact 변환

        // ----------------------------------------------------------------------
        /// <summary>
        /// 겹침 위치를 근사 Contact로 변환한다. 법선은 실제 충돌면 법선이 아니다.
        /// </summary>
        // ----------------------------------------------------------------------
        private static DeliveryContact2D CreateOverlapContact
        (
            in PhysicsVolume2D volume,
            Collider2D collider,
            Vector2 direction
        )
        {
            var position = collider.ClosestPoint(volume.Position);
            var normal = position - volume.Position;

            if (normal.sqrMagnitude <= Mathf.Epsilon)
            {
                var radians = volume.Angle * Mathf.Deg2Rad;
                normal = new Vector2(Mathf.Sin(radians), -Mathf.Cos(radians));
            }
            else
            {
                normal.Normalize();
            }

            return new DeliveryContact2D
            (
                collider,
                position,
                normal,
                direction,
                0f,
                hasSurfaceGeometry: false
            );
        }

        private static bool IsAccepted(Collider2D collider, IDeliveryColliderFilter2D filter) => collider != null && (filter == null || filter.IsAccepted(collider));

    #endregion

    }
}
