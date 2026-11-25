/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : NavigationController.cs
수정일 : 2026-11-25

# 설명
Goal Generation·Planner 요청·Waypoint Guidance와 Owned State 수명을 관리한다.
진행 상태(NavigationStatus)와 요청·복원 판정 열거형을 함께 둔다.

# 특이사항, 제약사항
외부 Planner/Host가 시간·World Revision·경로 완료 및 Motor 적용을 소유한다.
경로는 ReadOnlySpan으로 받아 수락 도중 외부 코드를 호출하지 않는다.
Goal Generation·요청 ID·경로 ID는 단조 증가하는 수명 토큰이며 Restore에서도 되돌리지 않는다.
========================================================================= BLOCK_HEADER_END */

using System;
using System.Collections;
using System.Collections.Generic;

using inonego;
using inonego.Xeri;
using inonego.Xeri.Game;
using inonego.Xeri.Game.Motion;

namespace inonego.Xeri.Game.Navigation
{
    // ============================================================
    /// <summary>
    /// Actor 목표와 현재 경로의 진행 상태입니다.
    /// </summary>
    // ============================================================
    public enum NavigationStatus
    {
        Idle,
        NeedsPlan,
        Planning,
        Following,
        Arrived,
        Failed
    }

    // ============================================================
    /// <summary>
    /// 외부 Planner 결과 적용의 판정입니다.
    /// </summary>
    // ============================================================
    public enum NavigationPlanApplyStatus
    {
        Accepted,
        Stale,
        InvalidPath
    }

    // ============================================================
    /// <summary>
    /// Owned Navigation Snapshot 복원의 판정입니다.
    /// </summary>
    // ============================================================
    public enum NavigationRestoreStatus
    {
        Restored,
        ActorMismatch,
        WorldMismatch
    }

    // ============================================================
    /// <summary>
    /// <br/> 하나의 Actor가 소유한 목표와 경로 수명을 관리합니다.
    /// <br/> Planner 계산과 Motor Body 적용은 수행하지 않습니다.
    /// </summary>
    // ============================================================
    public sealed class NavigationController<TPoint> where TPoint : unmanaged
    {

    #region 필드

        private long nextRequestID;
        private long pendingRequestID;
        private Revision pathGeneration = Revision.None;
        private Revision nextPathGeneration = Revision.None;
        private TPoint[] path = Array.Empty<TPoint>();

        // ------------------------------------------------------------
        /// <summary>
        /// 이 Controller의 실행 인스턴스와 Actor 수명입니다.
        /// </summary>
        // ------------------------------------------------------------
        public MotionActorKey Actor { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 현재 Goal의 수명 Generation이며 Restore에서도 줄어들지 않습니다.
        /// </summary>
        // ------------------------------------------------------------
        public Revision GoalGeneration => goalGeneration;
        private Revision goalGeneration = Revision.None;

        // ------------------------------------------------------------
        /// <summary>
        /// 현재 경로와 Planner 요청 상태입니다.
        /// </summary>
        // ------------------------------------------------------------
        public NavigationStatus Status => status;
        private NavigationStatus status;

        // ------------------------------------------------------------
        /// <summary>
        /// 현재 활성 목표가 존재하는지 표시합니다.
        /// </summary>
        // ------------------------------------------------------------
        public bool HasGoal => hasGoal;
        private bool hasGoal;

        // ------------------------------------------------------------
        /// <summary>
        /// 현재 목표 위치이며 목표가 없다면 기본값입니다.
        /// </summary>
        // ------------------------------------------------------------
        public TPoint Goal => goal;
        private TPoint goal;

        // ------------------------------------------------------------
        /// <summary>
        /// 경로 요청에 사용한 World Revision입니다.
        /// </summary>
        // ------------------------------------------------------------
        public Revision WorldRevision => worldRevision;
        private Revision worldRevision = Revision.None;

        // ------------------------------------------------------------
        /// <summary>
        /// 요청이 진행 중인지 알려줍니다.
        /// </summary>
        // ------------------------------------------------------------
        public bool HasPendingRequest => status == NavigationStatus.Planning;

        // ------------------------------------------------------------
        /// <summary>
        /// 마지막으로 승인된 경로의 읽기 전용 Waypoint입니다.
        /// </summary>
        // ------------------------------------------------------------
        public IReadOnlyList<TPoint> Waypoints => readonlyPath;
        private IReadOnlyList<TPoint> readonlyPath =
            Array.AsReadOnly(Array.Empty<TPoint>());

        // ------------------------------------------------------------
        /// <summary>
        /// 다음에 소비할 Waypoint의 인덱스입니다.
        /// </summary>
        // ------------------------------------------------------------
        public int WaypointIndex => waypointIndex;
        private int waypointIndex;

    #endregion

    #region 생성자

        // ------------------------------------------------------------
        /// <summary>
        /// Actor 하나의 독립적인 Navigation 수명을 구성합니다.
        /// </summary>
        // ------------------------------------------------------------
        public NavigationController(MotionActorKey actor)
        {
            if (actor.ScopeID <= 0 || actor.ActorID <= 0 || actor.Generation.IsNone)
            {
                throw new ArgumentOutOfRangeException(nameof(actor));
            }

            Actor = actor;
        }

    #endregion

    #region 목표와 Planner 요청

        // ------------------------------------------------------------
        /// <summary>
        /// 목표를 교체하고 이전 Planner 요청을 취소합니다.
        /// </summary>
        // ------------------------------------------------------------
        public void SetGoal(in TPoint target)
        {
            goalGeneration = goalGeneration.Inc();
            hasGoal = true;
            goal = target;
            status = NavigationStatus.NeedsPlan;
            worldRevision = Revision.None;
            ClearPath();
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 현재 목표·경로·지연 Planner 요청을 취소합니다.
        /// </summary>
        // ------------------------------------------------------------
        public void Cancel()
        {
            goalGeneration = goalGeneration.Inc();
            hasGoal = false;
            goal = default;
            status = NavigationStatus.Idle;
            worldRevision = Revision.None;
            ClearPath();
        }

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> Correction 또는 World 변경 뒤 기존 경로를 폐기합니다.
        /// <br/> 목표는 유지하므로 외부 Host가 재계획할 수 있습니다.
        /// </summary>
        // ------------------------------------------------------------
        public void Invalidate()
        {
            goalGeneration = goalGeneration.Inc();
            status = hasGoal ? NavigationStatus.NeedsPlan : NavigationStatus.Idle;
            worldRevision = Revision.None;
            ClearPath();
        }

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> 새 경로 요청을 발급합니다.
        /// <br/> 요청 ID는 해당 Controller에서 재사용하지 않습니다.
        /// </summary>
        // ------------------------------------------------------------
        public NavigationPlanRequest<TPoint> BeginPlan(in TPoint start, Revision currentWorldRevision)
        {
            if (!hasGoal)
            {
                throw new InvalidOperationException("목표가 없는 Actor는 경로를 요청할 수 없습니다.");
            }

            if (currentWorldRevision.IsNone)
            {
                throw new ArgumentOutOfRangeException(nameof(currentWorldRevision));
            }

            nextRequestID = checked(nextRequestID + 1);
            worldRevision = currentWorldRevision;
            status = NavigationStatus.Planning;
            ClearPath();
            pendingRequestID = nextRequestID;

            return new NavigationPlanRequest<TPoint>
            (
                Actor, goalGeneration, pendingRequestID,
                worldRevision, start, goal
            );
        }

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> 현재 요청의 경로만 승인하고 Waypoint를 독립 복사합니다.
        /// <br/> 지연된 결과나 중복 제출은 적용하지 않습니다.
        /// </summary>
        // ------------------------------------------------------------
        public NavigationPlanApplyStatus TryAcceptPath
        (
            in NavigationPlanRequest<TPoint> request,
            ReadOnlySpan<TPoint> waypoints
        )
        {
            if (!IsCurrentRequest(request))
            {
                return NavigationPlanApplyStatus.Stale;
            }

            if (waypoints.IsEmpty)
            {
                return NavigationPlanApplyStatus.InvalidPath;
            }

            SetPath(waypoints.ToArray());
            pathGeneration = IssuePathGeneration();
            status = NavigationStatus.Following;
            return NavigationPlanApplyStatus.Accepted;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 현재 Planner 요청의 실패를 기록합니다.
        /// </summary>
        // ------------------------------------------------------------
        public NavigationPlanApplyStatus TryRejectPath(in NavigationPlanRequest<TPoint> request)
        {
            if (!IsCurrentRequest(request))
            {
                return NavigationPlanApplyStatus.Stale;
            }

            pendingRequestID = 0;
            status = NavigationStatus.Failed;
            ClearPath();
            return NavigationPlanApplyStatus.Accepted;
        }

        private bool IsCurrentRequest(in NavigationPlanRequest<TPoint> request) =>
            hasGoal && status == NavigationStatus.Planning &&
            request.Actor.Equals(Actor) && request.GoalGeneration == goalGeneration &&
            request.RequestID == pendingRequestID &&
            request.WorldRevision == worldRevision;

    #endregion

    #region Guidance

        // ------------------------------------------------------------
        /// <summary>
        /// 현재 Waypoint를 소비자가 읽을 수 있도록 반환합니다.
        /// </summary>
        // ------------------------------------------------------------
        public bool TryGetGuidance(out NavigationGuidance<TPoint> guidance)
        {
            if (status != NavigationStatus.Following || waypointIndex >= path.Length)
            {
                guidance = default;
                return false;
            }

            guidance = new NavigationGuidance<TPoint>
            (
                Actor, goalGeneration, pathGeneration, waypointIndex, path[waypointIndex]
            );
            return true;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> 현재 경로의 즉시 도착 판정을 반영합니다.
        /// <br/> 지연 통지는 TryAdvanceWaypoint로 검증합니다.
        /// </summary>
        // ------------------------------------------------------------
        public bool AdvanceWaypoint()
        {
            if (status != NavigationStatus.Following)
            {
                return false;
            }

            waypointIndex++;

            if (waypointIndex >= path.Length)
            {
                status = NavigationStatus.Arrived;
            }

            return true;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> 현재 경로·Waypoint의 Guidance만 처리합니다.
        /// <br/> 늦은 도착과 중복 완료를 거절합니다.
        /// </summary>
        // ------------------------------------------------------------
        public bool TryAdvanceWaypoint(in NavigationGuidance<TPoint> guidance)
        {
            if (status != NavigationStatus.Following ||
                !guidance.Actor.Equals(Actor) ||
                guidance.GoalGeneration != goalGeneration ||
                guidance.PathGeneration != pathGeneration ||
                guidance.WaypointIndex != waypointIndex)
            {
                return false;
            }

            return AdvanceWaypoint();
        }

    #endregion

    #region Owned Snapshot

        // ------------------------------------------------------------
        /// <summary>
        /// 현재 Navigation Goal·Path의 독립 Snapshot을 만듭니다.
        /// </summary>
        // ------------------------------------------------------------
        public NavigationSnapshot<TPoint> Capture()
        {
            var owned = new TPoint[path.Length];
            Array.Copy(path, owned, path.Length);

            return new NavigationSnapshot<TPoint>
            (
                Actor, goalGeneration, worldRevision, status, hasGoal,
                goal, owned, waypointIndex
            );
        }

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> 같은 Actor·World의 Owned State만 복원합니다.
        /// <br/> Goal Generation·경로 ID는 새로 발급합니다.
        /// </summary>
        // ------------------------------------------------------------
        public NavigationRestoreStatus Restore
        (
            NavigationSnapshot<TPoint> snapshot,
            Revision currentWorldRevision
        )
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            if (currentWorldRevision.IsNone)
            {
                throw new ArgumentOutOfRangeException(nameof(currentWorldRevision));
            }

            if (!snapshot.Actor.Equals(Actor))
            {
                return NavigationRestoreStatus.ActorMismatch;
            }

            if (!snapshot.WorldRevision.IsNone &&
                snapshot.WorldRevision != currentWorldRevision)
            {
                return NavigationRestoreStatus.WorldMismatch;
            }

            // 수명 토큰은 되돌리지 않는다. 복원 전에 발급한 요청·Guidance와 같은 값이 다시 나오지 않도록 새로 발급한다.
            nextRequestID = checked(nextRequestID + 1);
            pendingRequestID = 0;
            goalGeneration = goalGeneration.Inc();
            worldRevision = snapshot.WorldRevision;
            hasGoal = snapshot.HasGoal;
            goal = snapshot.Goal;
            var restored = new TPoint[snapshot.Waypoints.Count];

            for (var index = 0; index < restored.Length; index++)
            {
                restored[index] = snapshot.Waypoints[index];
            }

            SetPath(restored);
            pathGeneration = snapshot.Status == NavigationStatus.Following ||
                snapshot.Status == NavigationStatus.Arrived ? IssuePathGeneration() : Revision.None;
            waypointIndex = snapshot.WaypointIndex;
            status = snapshot.Status == NavigationStatus.Planning
                ? NavigationStatus.NeedsPlan : snapshot.Status;
            return NavigationRestoreStatus.Restored;
        }

    #endregion

    #region 유틸

        private void ClearPath() => SetPath(Array.Empty<TPoint>());

        private void SetPath(TPoint[] owned)
        {
            path = owned;
            readonlyPath = Array.AsReadOnly(owned);
            waypointIndex = 0;
            pendingRequestID = 0;
            pathGeneration = Revision.None;
        }

        private Revision IssuePathGeneration()
        {
            nextPathGeneration = nextPathGeneration.Inc();
            return nextPathGeneration;
        }

    #endregion

    }
}
