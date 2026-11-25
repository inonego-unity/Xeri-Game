/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : TEST_NavigationController.cs
수정일 : 2026-11-21

# 설명
Navigation 목표·Planner 요청·Stale 결과·Guidance·Owned Snapshot 및 Motion/Motor 연결을 검증한다.

# 특이사항, 제약사항
실제 Planner 또는 게임 이동 정책을 포함하지 않는다. Physics World Step은 실행하지 않는다.

# 테스트 구성
 G: 목표와 Guidance 진행 (Goal)
 S: 지연 요청·Guidance 차단 (Stale)
 P: Planner 결과 처리 (Plan)
 R: Snapshot 복원 (Restore)
 C: 공개 계약과 연결 (Contract)
========================================================================= BLOCK_HEADER_END */

using System;
using System.Collections;
using System.Collections.Generic;

using UnityEngine;

using NUnit;
using NUnit.Framework;

using inonego;
using inonego.Xeri;
using inonego.Xeri.Game;
using inonego.Xeri.Game.Motor;
using inonego.Xeri.Game.Motion;
using inonego.Xeri.Game.Navigation;

namespace inonego.Xeri.Game.TEST.EDIT._Navigation
{
    // ============================================================
    /// <summary>
    /// Goal, Path 요청과 Owned State의 Actor 격리를 검증합니다.
    /// </summary>
    // ============================================================
    public sealed class TEST_NavigationController
    {

    #region 헬퍼

        private static readonly MotionActorKey actor = new(71, 1, Revision.FromUInt64(1));

        private static int[] Points(params int[] values) => values;

        private static Vector2[] Points(params Vector2[] values) => values;

        private sealed class PlannerFixture : INavigationPlanner<int>
        {
            internal NavigationPlanRequest<int> LastRequest;

            // ------------------------------------------------------------
            /// <summary>
            /// 외부 Planner가 받은 요청을 기록합니다.
            /// </summary>
            // ------------------------------------------------------------
            public void Request(in NavigationPlanRequest<int> request)
            {
                LastRequest = request;
            }
        }

    #endregion

    #region G-1: 목표와 Guidance 진행

        // ------------------------------------------------------------
        /// <summary>
        /// Goal → Request → Guidance → Arrived를 검증합니다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_Navigation_목표와Waypoint진행()
        {
            var navigation = new NavigationController<int>(actor);
            Assert.That(navigation.Status, Is.EqualTo(NavigationStatus.Idle));
            Assert.That(navigation.TryGetGuidance(out _), Is.False);

            navigation.SetGoal(30);
            Assert.That(navigation.Status, Is.EqualTo(NavigationStatus.NeedsPlan));
            Assert.That(navigation.Goal, Is.EqualTo(30));
            var request = navigation.BeginPlan(0, Revision.FromUInt64(12));
            Assert.That(request.Actor, Is.EqualTo(actor));
            Assert.That(request.GoalGeneration, Is.EqualTo(navigation.GoalGeneration));
            Assert.That(request.WorldRevision, Is.EqualTo(Revision.FromUInt64(12)));
            Assert.That(navigation.HasPendingRequest, Is.True);

            var path = Points(10, 20, 30);
            Assert.That
            (
                navigation.TryAcceptPath(request, path),
                Is.EqualTo(NavigationPlanApplyStatus.Accepted)
            );
            path[0] = 999;
            Assert.That(navigation.HasPendingRequest, Is.False);
            Assert.That(navigation.TryGetGuidance(out var guidance), Is.True);
            Assert.That(guidance.Target, Is.EqualTo(10));
            Assert.That(guidance.GoalGeneration, Is.EqualTo(request.GoalGeneration));
            Assert.That(guidance.Actor, Is.EqualTo(actor));

            Assert.That(navigation.AdvanceWaypoint(), Is.True);
            Assert.That(navigation.TryGetGuidance(out guidance), Is.True);
            Assert.That(guidance.WaypointIndex, Is.EqualTo(1));
            Assert.That(guidance.Target, Is.EqualTo(20));
            Assert.That(navigation.AdvanceWaypoint(), Is.True);
            Assert.That(navigation.AdvanceWaypoint(), Is.True);
            Assert.That(navigation.Status, Is.EqualTo(NavigationStatus.Arrived));
            Assert.That(navigation.TryGetGuidance(out _), Is.False);
            Assert.That(navigation.AdvanceWaypoint(), Is.False);
            Assert.That(navigation.HasGoal, Is.True);
        }

    #endregion

    #region S-1: 지연 요청·Guidance 차단

        // ------------------------------------------------------------
        /// <summary>
        /// 비동기 Planner의 늦은 결과·중복 응답을 차단합니다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_Navigation_동일Goal의이전Planner요청거절()
        {
            var navigation = new NavigationController<int>(actor);
            navigation.SetGoal(8);
            var first = navigation.BeginPlan(0, Revision.FromUInt64(1));
            var latest = navigation.BeginPlan(2, Revision.FromUInt64(1));
            Assert.That(latest.RequestID, Is.GreaterThan(first.RequestID));
            Assert.That
            (
                navigation.TryAcceptPath(first, Points(8)),
                Is.EqualTo(NavigationPlanApplyStatus.Stale)
            );
            Assert.That
            (
                navigation.TryRejectPath(first),
                Is.EqualTo(NavigationPlanApplyStatus.Stale)
            );
            Assert.That
            (
                navigation.TryAcceptPath(latest, Points(8)),
                Is.EqualTo(NavigationPlanApplyStatus.Accepted)
            );
            Assert.That
            (
                navigation.TryAcceptPath(latest, Points(99)),
                Is.EqualTo(NavigationPlanApplyStatus.Stale)
            );
            Assert.That(navigation.Waypoints[0], Is.EqualTo(8));
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 새 Goal과 Cancel은 기존 Planner 응답을 폐기합니다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_Navigation_목표변경및취소후지연응답()
        {
            var navigation = new NavigationController<int>(actor);
            navigation.SetGoal(10);
            var first = navigation.BeginPlan(0, Revision.FromUInt64(1));
            navigation.SetGoal(20);
            Assert.That
            (
                navigation.TryAcceptPath(first, Points(10)),
                Is.EqualTo(NavigationPlanApplyStatus.Stale)
            );
            var second = navigation.BeginPlan(0, Revision.FromUInt64(1));
            navigation.Cancel();
            Assert.That
            (
                navigation.TryAcceptPath(second, Points(20)),
                Is.EqualTo(NavigationPlanApplyStatus.Stale)
            );
            Assert.That(navigation.Status, Is.EqualTo(NavigationStatus.Idle));
            Assert.That(navigation.HasGoal, Is.False);
            Assert.That(navigation.TryGetGuidance(out _), Is.False);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Motor Correction 뒤 경로를 폐기하고 재계획합니다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_Navigation_Correction후재계획()
        {
            var navigation = new NavigationController<int>(actor);
            navigation.SetGoal(80);
            var pending = navigation.BeginPlan(0, Revision.FromUInt64(5));
            navigation.Invalidate();

            Assert.That(navigation.Status, Is.EqualTo(NavigationStatus.NeedsPlan));
            Assert.That(navigation.HasGoal, Is.True);
            Assert.That(navigation.Goal, Is.EqualTo(80));
            Assert.That
            (
                navigation.TryAcceptPath(pending, Points(80)),
                Is.EqualTo(NavigationPlanApplyStatus.Stale)
            );
            var retry = navigation.BeginPlan(40, Revision.FromUInt64(6));
            Assert.That(retry.Goal, Is.EqualTo(80));
            Assert.That
            (
                navigation.TryAcceptPath(retry, Points(80)),
                Is.EqualTo(NavigationPlanApplyStatus.Accepted)
            );
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 동일 Goal 재계획 시 이전 경로의 도착 통지를 거절합니다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_Navigation_동일Goal재계획에서오래된Guidance차단()
        {
            var navigation = new NavigationController<int>(actor);
            navigation.SetGoal(100);

            var firstRequest = navigation.BeginPlan(0, Revision.FromUInt64(7));
            navigation.TryAcceptPath(firstRequest, Points(10, 100));
            Assert.That(navigation.TryGetGuidance(out var oldGuidance), Is.True);
            Assert.That(oldGuidance.Target, Is.EqualTo(10));

            // 목표는 같지만 현재 경로는 새 Planner 결과로 교체되었다.
            var newRequest = navigation.BeginPlan(5, Revision.FromUInt64(7));
            navigation.TryAcceptPath(newRequest, Points(20, 100));
            Assert.That(navigation.TryGetGuidance(out var currentGuidance), Is.True);
            Assert.That(currentGuidance.GoalGeneration, Is.EqualTo(oldGuidance.GoalGeneration));
            Assert.That(currentGuidance.PathGeneration, Is.Not.EqualTo(oldGuidance.PathGeneration));

            Assert.That(navigation.TryAdvanceWaypoint(oldGuidance), Is.False);
            Assert.That(navigation.WaypointIndex, Is.Zero);
            Assert.That(navigation.TryAdvanceWaypoint(currentGuidance), Is.True);
            Assert.That(navigation.WaypointIndex, Is.EqualTo(1));
            Assert.That(navigation.TryAdvanceWaypoint(currentGuidance), Is.False);
            Assert.That(navigation.TryGetGuidance(out var nextGuidance), Is.True);
            Assert.That(nextGuidance.Target, Is.EqualTo(100));
        }

    #endregion

    #region P-1: Planner 결과 처리

        // ------------------------------------------------------------
        /// <summary>
        /// 빈 경로는 거절하되 같은 요청을 다시 완료할 수 있습니다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_Navigation_무효경로와재제출()
        {
            var navigation = new NavigationController<int>(actor);
            navigation.SetGoal(3);
            var request = navigation.BeginPlan(0, Revision.FromUInt64(1));
            Assert.That
            (
                navigation.TryAcceptPath(request, Array.Empty<int>()),
                Is.EqualTo(NavigationPlanApplyStatus.InvalidPath)
            );
            Assert.That(navigation.Status, Is.EqualTo(NavigationStatus.Planning));
            Assert.That
            (
                navigation.TryAcceptPath(request, null),
                Is.EqualTo(NavigationPlanApplyStatus.InvalidPath)
            );
            Assert.That
            (
                navigation.TryAcceptPath(request, Points(3)),
                Is.EqualTo(NavigationPlanApplyStatus.Accepted)
            );
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Planner 실패 뒤에도 같은 Goal을 다시 계획할 수 있습니다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_Navigation_Planner실패후재요청()
        {
            var navigation = new NavigationController<int>(actor);
            navigation.SetGoal(4);
            var request = navigation.BeginPlan(0, Revision.FromUInt64(2));
            Assert.That
            (
                navigation.TryRejectPath(request),
                Is.EqualTo(NavigationPlanApplyStatus.Accepted)
            );
            Assert.That(navigation.Status, Is.EqualTo(NavigationStatus.Failed));
            Assert.That(navigation.TryGetGuidance(out _), Is.False);
            var retry = navigation.BeginPlan(1, Revision.FromUInt64(2));
            Assert.That(retry.RequestID, Is.GreaterThan(request.RequestID));
            Assert.That
            (
                navigation.TryAcceptPath(retry, Points(4)),
                Is.EqualTo(NavigationPlanApplyStatus.Accepted)
            );
        }

    #endregion

    #region R-1: Snapshot 복원

        // ------------------------------------------------------------
        /// <summary>
        /// Owned State 복원 후 과거 Guidance 재사용을 차단합니다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_Navigation_Snapshot복원후과거Guidance차단()
        {
            var navigation = new NavigationController<int>(actor);
            navigation.SetGoal(30);
            var request = navigation.BeginPlan(0, Revision.FromUInt64(9));
            navigation.TryAcceptPath(request, Points(10, 30));
            Assert.That(navigation.TryGetGuidance(out var original), Is.True);
            var snapshot = navigation.Capture();

            Assert.That
            (
                navigation.Restore(snapshot, Revision.FromUInt64(9)),
                Is.EqualTo(NavigationRestoreStatus.Restored)
            );
            Assert.That(navigation.TryGetGuidance(out var restored), Is.True);
            Assert.That(restored.PathGeneration, Is.Not.EqualTo(original.PathGeneration));
            Assert.That(navigation.TryAdvanceWaypoint(original), Is.False);
            Assert.That(navigation.TryAdvanceWaypoint(restored), Is.True);

            var other = new NavigationController<int>(new MotionActorKey(71, 2, Revision.FromUInt64(1)));
            other.SetGoal(30);

            // 경로를 한 번 더 승인해 다른 Controller의 경로 세대를 복원된 경로와 같게 맞춘다.
            other.TryAcceptPath(other.BeginPlan(0, Revision.FromUInt64(9)), Points(10, 30));
            var otherRequest = other.BeginPlan(0, Revision.FromUInt64(9));
            other.TryAcceptPath(otherRequest, Points(10, 30));
            Assert.That(other.TryGetGuidance(out var otherGuidance), Is.True);
            Assert.That(otherGuidance.PathGeneration, Is.EqualTo(restored.PathGeneration));
            Assert.That(other.TryAdvanceWaypoint(restored), Is.False);
            Assert.That(other.WaypointIndex, Is.Zero);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Snapshot은 이후 경로 수정과 독립되어 유지됩니다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_Navigation_OwnedSnapshot복제와진행복원()
        {
            var navigation = new NavigationController<int>(actor);
            navigation.SetGoal(30);
            var request = navigation.BeginPlan(0, Revision.FromUInt64(12));
            navigation.TryAcceptPath(request, Points(10, 20, 30));
            navigation.AdvanceWaypoint();
            var saved = navigation.Capture();

            navigation.Cancel();
            navigation.SetGoal(500);
            Assert.That(saved.Waypoints.Count, Is.EqualTo(3));
            Assert.That(saved.Waypoints[1], Is.EqualTo(20));
            Assert.That
            (
                navigation.Restore(saved, Revision.FromUInt64(12)),
                Is.EqualTo(NavigationRestoreStatus.Restored)
            );
            Assert.That(navigation.Status, Is.EqualTo(NavigationStatus.Following));
            Assert.That(navigation.TryGetGuidance(out var guidance), Is.True);
            Assert.That(guidance.Target, Is.EqualTo(20));
            Assert.That(navigation.Waypoints[2], Is.EqualTo(30));
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 다른 Actor·World의 Snapshot은 적용하지 않습니다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_Navigation_SnapshotActor및World검증()
        {
            var navigation = new NavigationController<int>(actor);
            navigation.SetGoal(9);
            var request = navigation.BeginPlan(0, Revision.FromUInt64(7));
            navigation.TryAcceptPath(request, Points(9));
            var snapshot = navigation.Capture();
            navigation.SetGoal(22);
            var revision = navigation.GoalGeneration;

            Assert.That
            (
                navigation.Restore(snapshot, Revision.FromUInt64(8)),
                Is.EqualTo(NavigationRestoreStatus.WorldMismatch)
            );
            Assert.That(navigation.GoalGeneration, Is.EqualTo(revision));
            var other = new NavigationController<int>(new MotionActorKey(71, 2, Revision.FromUInt64(1)));
            Assert.That
            (
                other.Restore(snapshot, Revision.FromUInt64(7)),
                Is.EqualTo(NavigationRestoreStatus.ActorMismatch)
            );
            Assert.That(other.Status, Is.EqualTo(NavigationStatus.Idle));
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Planning 시점 Snapshot 복원은 오래된 응답을 허용하지 않습니다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_Navigation_PlanningSnapshot복원은재계획()
        {
            var navigation = new NavigationController<int>(actor);
            navigation.SetGoal(55);
            var old = navigation.BeginPlan(0, Revision.FromUInt64(3));
            var snapshot = navigation.Capture();
            navigation.Invalidate();

            Assert.That
            (
                navigation.Restore(snapshot, Revision.FromUInt64(3)),
                Is.EqualTo(NavigationRestoreStatus.Restored)
            );
            Assert.That(navigation.Status, Is.EqualTo(NavigationStatus.NeedsPlan));
            Assert.That
            (
                navigation.TryAcceptPath(old, Points(55)),
                Is.EqualTo(NavigationPlanApplyStatus.Stale)
            );
            var retry = navigation.BeginPlan(1, Revision.FromUInt64(3));
            Assert.That(retry.RequestID, Is.GreaterThan(old.RequestID));
            Assert.That
            (
                navigation.TryAcceptPath(retry, Points(55)),
                Is.EqualTo(NavigationPlanApplyStatus.Accepted)
            );
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 복원 뒤에도 이미 발급한 Goal Generation을 다시 쓰지 않습니다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_Navigation_복원후GoalGeneration재사용금지()
        {
            var navigation = new NavigationController<int>(actor);
            navigation.SetGoal(30);
            var snapshot = navigation.Capture();
            navigation.SetGoal(40);
            var replaced = navigation.GoalGeneration;

            Assert.That
            (
                navigation.Restore(snapshot, Revision.FromUInt64(1)),
                Is.EqualTo(NavigationRestoreStatus.Restored)
            );
            Assert.That(navigation.Goal, Is.EqualTo(30));
            Assert.IsTrue(navigation.GoalGeneration > replaced);

            // 복원 뒤의 새 목표도 복원 전 목표의 Revision과 겹치지 않는다.
            var restored = navigation.GoalGeneration;
            navigation.SetGoal(50);
            Assert.IsTrue(navigation.GoalGeneration > restored);
        }

    #endregion

    #region C-1: 공개 계약과 연결

        // ------------------------------------------------------------
        /// <summary>
        /// Planner는 공개 요청만 전달받고 Core Body에 접근하지 않습니다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_Navigation_독립Planner계약()
        {
            var navigation = new NavigationController<int>(actor);
            var planner = new PlannerFixture();
            navigation.SetGoal(15);
            var request = navigation.BeginPlan(7, Revision.FromUInt64(4));
            planner.Request(request);
            Assert.That(planner.LastRequest.Goal, Is.EqualTo(15));
            Assert.That(planner.LastRequest.Start, Is.EqualTo(7));
            Assert.That
            (
                navigation.TryAcceptPath(planner.LastRequest, Points(15)),
                Is.EqualTo(NavigationPlanApplyStatus.Accepted)
            );
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 목표가 없는 경우와 잘못된 World 값을 거절합니다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_Navigation_요청값검증()
        {
            var navigation = new NavigationController<int>(actor);
            Assert.Throws<InvalidOperationException>(() => navigation.BeginPlan(0, Revision.FromUInt64(1)));
            navigation.SetGoal(5);
            Assert.Throws<ArgumentOutOfRangeException>(() => navigation.BeginPlan(0, Revision.None));
            Assert.Throws<ArgumentNullException>(() => navigation.Restore(null, Revision.FromUInt64(1)));
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// Navigation Guidance는 Motion 입력·Motor 적용 공개 계약으로 이어집니다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_Navigation_기존MotionMotor공개계약연결()
        {
            var go = new GameObject("TEST_NavigationMotionMotor");

            try
            {
                var body = go.AddComponent<Rigidbody2D>();
                body.gravityScale = 0f;
                var motor = new Motor<MotorState2D>(actor, new RigidbodyMotorBackend2D(body));
                var navigation = new NavigationController<Vector2>(actor);
                navigation.SetGoal(new Vector2(3f, 0f));
                var request = navigation.BeginPlan(Vector2.zero, Revision.FromUInt64(1));
                navigation.TryAcceptPath(request, Points(new Vector2(3f, 0f)));
                Assert.That(navigation.TryGetGuidance(out var guidance), Is.True);

                // 게임 정책의 Steering 값은 테스트 Host에서 생성한다.
                var velocity = guidance.Target.normalized * 2f;
                var model = new MotionModel(actor, MotionModel.TopDown2DUp);
                var steeringInputs = new[]
                {
                    new MotionIntent(MotionChannel.Planar, velocity)
                };
                var frame = model.StepLocal
                (
                    0.02d, new MotionStepInput(steeringInputs)
                );

                Assert.That(motor.TryApply(frame), Is.True);
                Assert.That(body.linearVelocity, Is.EqualTo(new Vector2(2f, 0f)));
                Assert.That(navigation.TryAdvanceWaypoint(guidance), Is.True);
                Assert.That(navigation.Status, Is.EqualTo(NavigationStatus.Arrived));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

    #endregion

    }
}
