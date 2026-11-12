/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : PhysicsDelivery3D.cs
수정일 : 2026-11-02

# 설명
PhysicsQuery3D의 raw Collider/Hit를 게임 도메인과 독립된 DeliveryContact3D로 정규화한다.
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
    public interface IProjectileDeliveryQuery3D
    {
        // ------------------------------------------------------------
        /// <summary>
        /// 겹치는 유효 대상의 존재와 검색 완전성을 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        DeliveryContactQueryResult3D QueryAnyOverlap
        (
            in PhysicsVolume3D volume,
            LayerMask layerMask,
            PhysicsDeliveryWorkspace3D workspace,
            IDeliveryColliderFilter3D filter = null
        );

        // ------------------------------------------------------------
        /// <summary>
        /// 이동 중 가장 가까운 Contact와 검색 완전성을 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        DeliveryContactQueryResult3D QueryCast
        (
            in PhysicsVolume3D volume,
            Vector3 displacement,
            LayerMask layerMask,
            PhysicsDeliveryWorkspace3D workspace,
            IDeliveryColliderFilter3D filter = null
        );
    }

    // ============================================================
    /// <summary>
    /// 차원별 Unity 물리 후보 형식을 지정한 재사용 Workspace.
    /// </summary>
    // ============================================================
    public sealed class PhysicsDeliveryWorkspace3D :
        PhysicsDeliveryWorkspaceBase<Collider, RaycastHit, DeliveryContact3D>
    {

    #region 생성자

        // ------------------------------------------------------------
        /// <summary>
        /// 초기 및 최대 버퍼 용량을 지정하여 Workspace를 생성한다.
        /// </summary>
        // ------------------------------------------------------------
        public PhysicsDeliveryWorkspace3D(int capacity = 256, int maximumCapacity = 256)
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
    public sealed class PhysicsDelivery3D : IProjectileDeliveryQuery3D
    {

    #region 필드

        private readonly QueryTriggerInteraction triggerInteraction;

    #endregion

    #region 생성자

        // ------------------------------------------------------------
        /// <summary>
        /// Collider의 Trigger 포함 정책을 가진 질의기를 생성한다.
        /// </summary>
        // ------------------------------------------------------------
        public PhysicsDelivery3D(QueryTriggerInteraction triggerInteraction = QueryTriggerInteraction.Collide)
        {
            if (!Enum.IsDefined(typeof(QueryTriggerInteraction), triggerInteraction))
            {
                throw new ArgumentOutOfRangeException(nameof(triggerInteraction));
            }

            this.triggerInteraction = triggerInteraction;
        }

    #endregion

    #region Volume 후보 수집

        // ------------------------------------------------------------
        /// <summary>
        /// <br/>호출자가 소유한 Workspace에 Volume 후보를 수집한다.
        /// <br/>포화 상태에서는 반환된 Contact 목록이 일부일 수 있다.
        /// </summary>
        // ------------------------------------------------------------
        public DeliveryBatchResult QueryVolume
        (
            in PhysicsVolume3D volume,
            LayerMask layerMask,
            PhysicsDeliveryWorkspace3D queryWorkspace,
            IDeliveryColliderFilter3D filter = null
        )
        {
            if (queryWorkspace == null)
            {
                throw new ArgumentNullException(nameof(queryWorkspace));
            }

            queryWorkspace.Begin();

            try
            {
                var count = ReadOverlap(volume, layerMask, queryWorkspace, triggerInteraction);

                // Filter는 물리 후보 추출이 끝난 다음에 실행한다.
                for (var index = 0; index < count; index++)
                {
                    var collider = queryWorkspace.Overlap[index];
                    if (!IsAccepted(collider, filter))
                    {
                        continue;
                    }

                    queryWorkspace.AddContact(CreateOverlapContact(volume, collider, Vector3.zero));
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
        public DeliveryContactQueryResult3D QueryTrace
        (
            Vector3 origin,
            Vector3 direction,
            float distance,
            LayerMask layerMask,
            PhysicsDeliveryWorkspace3D queryWorkspace,
            IDeliveryColliderFilter3D filter = null
        )
        {
            if (queryWorkspace == null)
            {
                throw new ArgumentNullException(nameof(queryWorkspace));
            }

            queryWorkspace.Begin();

            try
            {
                var count = ReadRaycast(origin, direction, distance, layerMask, queryWorkspace, triggerInteraction);
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
        public DeliveryContactQueryResult3D QueryAnyOverlap
        (
            in PhysicsVolume3D volume,
            LayerMask layerMask,
            PhysicsDeliveryWorkspace3D queryWorkspace,
            IDeliveryColliderFilter3D filter = null
        )
        {
            if (queryWorkspace == null)
            {
                throw new ArgumentNullException(nameof(queryWorkspace));
            }

            queryWorkspace.Begin();

            try
            {
                var count = ReadOverlap(volume, layerMask, queryWorkspace, triggerInteraction);
                Collider selected = null;

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
                return new DeliveryContactQueryResult3D
                (
                    selected != null,
                    isComplete,
                    selected != null
                        ? CreateOverlapContact(volume, selected, Vector3.zero)
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
        public DeliveryContactQueryResult3D QueryCast
        (
            in PhysicsVolume3D volume,
            Vector3 displacement,
            LayerMask layerMask,
            PhysicsDeliveryWorkspace3D queryWorkspace,
            IDeliveryColliderFilter3D filter = null
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
                    return new DeliveryContactQueryResult3D(false, true);
                }

                var direction = displacement / distance;
                var count = ReadCast(volume, direction, distance, layerMask, queryWorkspace, triggerInteraction);
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
            in PhysicsVolume3D volume,
            LayerMask layerMask,
            PhysicsDeliveryWorkspace3D queryWorkspace,
            QueryTriggerInteraction triggerInteraction
        )
        {
            int count;

            do
            {
                count = PhysicsQuery3D.Overlap
                (
                    volume,
                    queryWorkspace.Overlap,
                    layerMask,
                    triggerInteraction
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
            Vector3 origin,
            Vector3 direction,
            float distance,
            LayerMask layerMask,
            PhysicsDeliveryWorkspace3D queryWorkspace,
            QueryTriggerInteraction triggerInteraction
        )
        {
            int count;

            do
            {
                count = PhysicsQuery3D.Raycast
                (
                    origin,
                    direction,
                    distance,
                    queryWorkspace.Cast,
                    layerMask,
                    triggerInteraction
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
            in PhysicsVolume3D volume,
            Vector3 direction,
            float distance,
            LayerMask layerMask,
            PhysicsDeliveryWorkspace3D queryWorkspace,
            QueryTriggerInteraction triggerInteraction
        )
        {
            int count;

            do
            {
                count = PhysicsQuery3D.Cast
                (
                    volume,
                    direction,
                    distance,
                    queryWorkspace.Cast,
                    layerMask,
                    triggerInteraction
                );
            }
            while (count >= queryWorkspace.Capacity && queryWorkspace.TryGrow());

            return count;
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/>받은 후보에서 최소 거리와 동거리 Collider 식별 해시로 Contact를 고른다.
        /// <br/>식별 해시 동점 규칙은 Unity 실행 간 결정론을 보장하지 않는다.
        /// </summary>
        // ----------------------------------------------------------------------
        private static DeliveryContactQueryResult3D FindNearest
        (
            int count,
            Vector3 direction,
            IDeliveryColliderFilter3D filter,
            PhysicsDeliveryWorkspace3D queryWorkspace
        )
        {
            var selected = default(RaycastHit);
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
                return new DeliveryContactQueryResult3D(false, isComplete);
            }

            var contact = new DeliveryContact3D
            (
                selected.collider,
                selected.point,
                selected.normal,
                direction.sqrMagnitude > Mathf.Epsilon ? direction.normalized : Vector3.zero,
                selected.distance
            );
            return new DeliveryContactQueryResult3D(true, isComplete, contact);
        }

    #endregion

    #region Contact 변환

        // ----------------------------------------------------------------------
        /// <summary>
        /// 겹침 위치를 근사 Contact로 변환한다. 법선은 실제 충돌면 법선이 아니다.
        /// </summary>
        // ----------------------------------------------------------------------
        private static DeliveryContact3D CreateOverlapContact
        (
            in PhysicsVolume3D volume,
            Collider collider,
            Vector3 direction
        )
        {
            var position = collider.ClosestPoint(volume.Position);
            var normal = position - volume.Position;

            if (normal.sqrMagnitude <= Mathf.Epsilon)
            {
                normal = -(volume.Rotation * Vector3.forward);
            }
            else
            {
                normal.Normalize();
            }

            return new DeliveryContact3D
            (
                collider,
                position,
                normal,
                direction,
                0f,
                hasSurfaceGeometry: false
            );
        }

        private static bool IsAccepted(Collider collider, IDeliveryColliderFilter3D filter) => collider != null && (filter == null || filter.IsAccepted(collider));

    #endregion

    }
}
