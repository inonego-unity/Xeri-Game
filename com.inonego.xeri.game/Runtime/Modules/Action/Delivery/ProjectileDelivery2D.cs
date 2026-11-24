/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : ProjectileDelivery2D.cs
수정일 : 2026-11-02

# 설명
일정 속도의 선형 Sweep로 Projectile 접촉·이동·수명을 처리한다.
검색 완전성이 부족하면 이동 상태를 확정하지 않고 결과로 보고한다.
비선형 경로는 외부 운동 정책이 여러 Sweep으로 분할해 실행한다.
========================================================================= BLOCK_HEADER_END */

using System;

using UnityEngine;

namespace inonego.Xeri.Game
{
    // ============================================================
    /// <summary>
    /// 선형 Sweep의 확정 상태와 이번 호출의 시간 소비를 보존한다.
    /// </summary>
    // ============================================================
    public readonly struct ProjectileDeliveryStepResult2D
    {

    #region 상태

        // ------------------------------------------------------------
        /// <summary>
        /// Projectile 진행 결과 상태.
        /// </summary>
        // ------------------------------------------------------------
        public ProjectileDeliveryStepStatus Status { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 접촉 상태일 때 확정된 2D Contact.
        /// </summary>
        // ------------------------------------------------------------
        public DeliveryContact2D Contact { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 이번 Step에서 접촉을 확정했는지 나타낸다.
        /// </summary>
        // ------------------------------------------------------------
        public bool HasContact => Status == ProjectileDeliveryStepStatus.Contact;

        // ------------------------------------------------------------
        /// <summary>
        /// 이번 Sweep을 신뢰할 수 있게 판정했는지 나타낸다.
        /// </summary>
        // ------------------------------------------------------------
        public bool IsComplete => Status != ProjectileDeliveryStepStatus.Incomplete;

        // ------------------------------------------------------------
        /// <summary>
        /// 접촉과 독립적으로 수명이 만료됐는지 나타낸다.
        /// </summary>
        // ------------------------------------------------------------
        public bool IsExpired { get; }

    #endregion

    #region 시간

        // ------------------------------------------------------------
        /// <summary>
        /// 현재 Projectile에 실제 적용한 경과 시간.
        /// </summary>
        // ------------------------------------------------------------
        public float ConsumedTime { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 요청 시간에서 아직 소비하지 않은 시간.
        /// </summary>
        // ------------------------------------------------------------
        public float RemainingTime { get; }

    #endregion

    #region 생성자

        // ------------------------------------------------------------
        /// <summary>
        /// 이번 선형 Sweep의 결과와 시간 소비를 보존한다.
        /// </summary>
        // ------------------------------------------------------------
        public ProjectileDeliveryStepResult2D
        (
            ProjectileDeliveryStepStatus status,
            float consumedTime,
            float remainingTime,
            in DeliveryContact2D contact = default,
            bool isExpired = false
        )
        {
            Status = status;
            Contact = status == ProjectileDeliveryStepStatus.Contact ? contact : default;
            IsExpired = isExpired || status == ProjectileDeliveryStepStatus.Expired;
            ConsumedTime = consumedTime;
            RemainingTime = remainingTime;
        }

    #endregion

    }

    // ============================================================
    /// <summary>
    /// <br/>현재 속도로 일정 시간 직선 이동하는 Projectile을 진행한다.
    /// <br/>Contact 이후의 반응과 남은 시간 소비는 호출자가 결정한다.
    /// </summary>
    // ============================================================
    [Serializable]
    public struct ProjectileDelivery2D
    {

    #region 진행 상태

        // ------------------------------------------------------------
        /// <summary>
        /// 현재 Volume, 속도와 수명을 보존하는 이동 상태.
        /// </summary>
        // ------------------------------------------------------------
        public MovingVolume2D MovingVolume { get; private set; }

        // ------------------------------------------------------------
        /// <summary>
        /// Unity 2D Layer·Depth·Trigger 검색 정책.
        /// </summary>
        // ------------------------------------------------------------
        public ContactFilter2D ContactFilter { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 현재 Projectile 수명 종료 여부를 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        public bool IsExpired => MovingVolume.IsExpired;

    #endregion

    #region 생성자

        // ------------------------------------------------------------
        /// <summary>
        /// 2D 이동 상태와 질의 필터를 결합한다.
        /// </summary>
        // ------------------------------------------------------------
        public ProjectileDelivery2D(in MovingVolume2D movingVolume, ContactFilter2D contactFilter)
        {
            MovingVolume = movingVolume;
            ContactFilter = contactFilter;
        }

    #endregion

    #region 이동 방향 갱신

        // ------------------------------------------------------------
        /// <summary>
        /// 시간과 위치는 유지하고 다음 Sweep에서 사용할 선속도를 교체한다.
        /// </summary>
        // ------------------------------------------------------------
        public void SetVelocity(Vector2 velocity)
        {
            var movingVolume = MovingVolume;
            movingVolume.SetVelocity(velocity);
            MovingVolume = movingVolume;
        }

    #endregion

    #region 시작 접촉

        // ------------------------------------------------------------
        /// <summary>
        /// <br/>이동 전 Volume의 겹침을 검색하고 현재 진행 방향을 부여한다.
        /// <br/>결과가 불완전하고 접촉도 없다면 시작 여부를 확정하지 않는다.
        /// </summary>
        // ------------------------------------------------------------
        public readonly DeliveryContactQueryResult2D QueryInitialOverlap
        (
            IProjectileDeliveryQuery2D query,
            PhysicsDeliveryWorkspace2D workspace,
            IDeliveryColliderFilter2D filter = null
        )
        {
            if (query == null)
            {
                throw new ArgumentNullException(nameof(query));
            }

            var result = query.QueryAnyOverlap
            (
                MovingVolume.Volume,
                ContactFilter,
                workspace,
                filter
            );
            return result.HasContact
                ? new DeliveryContactQueryResult2D
                (
                    true,
                    result.IsComplete,
                    result.Contact.WithDirection(ResolveDirection(MovingVolume.Velocity))
                )
                : result;
        }

    #endregion

    #region 선형 Sweep

        // ------------------------------------------------------------
        /// <summary>
        /// <br/>현재 속도의 직선 구간을 Cast하고 접촉 시각까지 Commit한다.
        /// <br/>검색이 불완전하면 이동을 적용하지 않고 Incomplete로 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        public ProjectileDeliveryStepResult2D Advance
        (
            float deltaTime,
            IProjectileDeliveryQuery2D query,
            PhysicsDeliveryWorkspace2D workspace,
            IDeliveryColliderFilter2D filter = null
        )
        {
            if (query == null)
            {
                throw new ArgumentNullException(nameof(query));
            }

            if (float.IsNaN(deltaTime) || float.IsInfinity(deltaTime) || deltaTime < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(deltaTime));
            }

            var movingVolume = MovingVolume;
            if (movingVolume.IsExpired)
            {
                return new ProjectileDeliveryStepResult2D
                (
                    ProjectileDeliveryStepStatus.Expired,
                    0f,
                    deltaTime,
                    isExpired: true
                );
            }

            var remainingLifetime = movingVolume.Lifetime - movingVolume.ElapsedTime;
            var stepTime = float.IsPositiveInfinity(movingVolume.Lifetime)
                ? deltaTime
                : Mathf.Min(deltaTime, Mathf.Max(0f, remainingLifetime));

            if (stepTime <= 0f)
            {
                return new ProjectileDeliveryStepResult2D
                (
                    ProjectileDeliveryStepStatus.Active,
                    0f,
                    deltaTime
                );
            }

            // 매 Sweep 시작에서 신규 겹침을 조회한다.
            var initial = QueryInitialOverlap(query, workspace, filter);
            if (initial.HasContact)
            {
                return new ProjectileDeliveryStepResult2D
                (
                    ProjectileDeliveryStepStatus.Contact,
                    0f,
                    deltaTime,
                    initial.Contact
                );
            }

            if (!initial.IsComplete)
            {
                return new ProjectileDeliveryStepResult2D
                (
                    ProjectileDeliveryStepStatus.Incomplete,
                    0f,
                    deltaTime
                );
            }

            // 한 번의 Step은 현재 Velocity로 일정하게 진행하는 선분이다.
            var displacement = movingVolume.Velocity * stepTime;
            if (!IsFinite(displacement))
            {
                throw new ArgumentOutOfRangeException
                (
                    nameof(deltaTime),
                    "Sweep 변위가 유한 범위를 초과했습니다."
                );
            }

            var cast = query.QueryCast
            (
                movingVolume.Volume,
                displacement,
                ContactFilter,
                workspace,
                filter
            );
            if (!cast.IsComplete)
            {
                return new ProjectileDeliveryStepResult2D
                (
                    ProjectileDeliveryStepStatus.Incomplete,
                    0f,
                    deltaTime
                );
            }

            if (cast.HasContact)
            {
                return CommitContact
                (
                    ref movingVolume,
                    displacement,
                    stepTime,
                    deltaTime,
                    cast.Contact
                );
            }

            var nextVolume = movingVolume.Volume.WithPosition
            (
                movingVolume.Volume.Position + displacement
            );
            movingVolume.Commit(nextVolume, movingVolume.Velocity, stepTime);
            MovingVolume = movingVolume;
            return new ProjectileDeliveryStepResult2D
            (
                movingVolume.IsExpired
                    ? ProjectileDeliveryStepStatus.Expired
                    : ProjectileDeliveryStepStatus.Active,
                stepTime,
                deltaTime - stepTime,
                isExpired: movingVolume.IsExpired
            );
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 접촉한 Sweep 지점에 Volume 중심을 확정하고 시간을 소비한다.
        /// </summary>
        // ------------------------------------------------------------
        private ProjectileDeliveryStepResult2D CommitContact
        (
            ref MovingVolume2D movingVolume,
            Vector2 displacement,
            float stepTime,
            float requestedTime,
            in DeliveryContact2D contact
        )
        {
            var distance = displacement.magnitude;
            if
            (
                float.IsNaN(contact.Distance) ||
                float.IsInfinity(contact.Distance) ||
                contact.Distance > distance
            )
            {
                throw new InvalidOperationException
                (
                    "Cast Contact의 Collider2D 또는 거리가 유효하지 않습니다."
                );
            }

            var fraction = distance > Mathf.Epsilon
                ? contact.Distance / distance
                : 0f;
            var consumedTime = stepTime * fraction;
            var nextVolume = movingVolume.Volume.WithPosition
            (
                movingVolume.Volume.Position + displacement * fraction
            );
            movingVolume.Commit(nextVolume, movingVolume.Velocity, consumedTime);
            MovingVolume = movingVolume;

            return new ProjectileDeliveryStepResult2D
            (
                ProjectileDeliveryStepStatus.Contact,
                consumedTime,
                requestedTime - consumedTime,
                contact.WithDirection(ResolveDirection(movingVolume.Velocity)),
                movingVolume.IsExpired
            );
        }

    #endregion

    #region 기하 입력 검증

        private static bool IsFinite(Vector2 value) =>
            !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
            !float.IsNaN(value.y) && !float.IsInfinity(value.y);

        private static Vector2 ResolveDirection(Vector2 velocity) =>
            velocity.sqrMagnitude > Mathf.Epsilon
                ? velocity.normalized
                : Vector2.zero;

    #endregion

    }
}
