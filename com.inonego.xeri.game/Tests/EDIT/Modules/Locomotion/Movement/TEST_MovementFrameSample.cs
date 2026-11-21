/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : TEST_MovementFrameSample.cs
수정일 : 2026-11-21

# 설명
합성 결과·Body 현재 상태·이동 상태를 묶는 Step Sample과 MovementState 재구성을 검증한다.

# 특이사항, 제약사항
Authority/Confirmed 분류는 외부 Host 입력이고 Network·Recorder를 구현하지 않는다.

# 테스트 구성
 C: 캡처와 확정 (Capture)
 S: 출처·수명 검증 (Source)
 R: 재진입과 복합 전환 (Reentrancy)
 M: MovementState 값 (Movement)
========================================================================= BLOCK_HEADER_END */

using System;

using UnityEngine;

using NUnit;
using NUnit.Framework;

using inonego;
using inonego.Xeri;
using inonego.Xeri.Game;
using inonego.Xeri.Game.Motor;
using inonego.Xeri.Game.Motion;
using inonego.Xeri.Game.Movement;
using inonego.Xeri.Game.Navigation;

namespace inonego.Xeri.Game.TEST.EDIT._Movement
{
    // ============================================================
    /// <summary>
    /// 공개 Step Sample의 출처 검증과 이동 상태 값을 검증합니다.
    /// </summary>
    // ============================================================
    public sealed class TEST_MovementFrameSample
    {

    #region 헬퍼

        private static readonly MotionActorKey actor = new(81, 1, 1);

        private static readonly MotionIntent[] walk =
        {
            new(MotionChannel.Planar, new Vector3(2f, 3f, 0f))
        };

        private sealed class StateReadCallbackBackend : IMotorBackend<MotorState2D>
        {
            internal Action DuringRead;
            internal MotorState2D State;

            // ------------------------------------------------------------
            /// <summary>
            /// 조회 중 Host의 상태 변경 호출을 재현합니다.
            /// </summary>
            // ------------------------------------------------------------
            public MotorState2D ReadCurrentState()
            {
                var captured = State;
                DuringRead?.Invoke();
                return captured;
            }

            // ------------------------------------------------------------
            /// <summary>
            /// 현재 Body 상태를 갱신합니다.
            /// </summary>
            // ------------------------------------------------------------
            public void ApplyCurrentState(in MotorState2D state)
            {
                State = state;
            }

            // ------------------------------------------------------------
            /// <summary>
            /// 이 Fixture는 합성 결과를 Body에 반영하지 않습니다.
            /// </summary>
            // ------------------------------------------------------------
            public bool TryApply(in MotionFrame frame) => true;
        }

        private static MotionFrame StepAndObserve(MotionModel model, Motor<MotorState2D> motor)
        {
            var frame = model.StepLocal(0.02d, new MotionStepInput(walk), MotionFeedback.None);
            Assert.That(motor.TryApply(frame), Is.True);
            motor.BeginPhysicsStep();
            motor.CompletePhysicsStep(frame.Tick.Index);
            return frame;
        }

    #endregion

    #region C-1: 캡처와 확정

        // ------------------------------------------------------------
        /// <summary>
        /// 확정 Sample은 합성 결과·현재 State·이동 상태를 보관합니다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_MovementFrameSample_확정State와이동상태()
        {
            var go = new GameObject("TEST_MovementConfirmed");

            try
            {
                var body = go.AddComponent<Rigidbody2D>();
                body.gravityScale = 0f;
                var model = new MotionModel(actor, MotionModel.TopDown2DUp);
                var motor = new Motor<MotorState2D>(actor, new RigidbodyMotorBackend2D(body));
                var frame = StepAndObserve(model, motor);

                var sample = MovementFrameSample<MotorState2D>.Capture
                (
                    model, motor, frame, true, MovementSampleConfirmation.Confirmed
                );

                Assert.That(sample.Actor, Is.EqualTo(actor));
                Assert.That(sample.Tick.Index, Is.Zero);
                Assert.That(sample.Tick.Delta, Is.EqualTo(0.02d));
                Assert.That(sample.Confirmation, Is.EqualTo(MovementSampleConfirmation.Confirmed));
                Assert.That(sample.Frame.PlanarMode, Is.EqualTo(MotionChannelMode.Set));
                Assert.That(sample.CurrentState.LinearVelocity, Is.EqualTo(new Vector2(2f, 3f)));
                Assert.That(sample.State.PlanarSpeed, Is.EqualTo(new Vector2(2f, 3f).magnitude).Within(1e-4f));
                Assert.That(sample.State.IsGrounded, Is.True);
                Assert.That(sample.State.ActionID, Is.Zero);

                // 값 형식이므로 이후 Correction이 보관한 Sample을 바꾸지 않는다.
                motor.ApplyCurrentState(new MotorState2D(new Vector2(4f, 5f), 0f, Vector2.zero, 0f));
                Assert.That(sample.CurrentState.LinearVelocity, Is.EqualTo(new Vector2(2f, 3f)));
                Assert.That(sample.DiscontinuityVersion, Is.Zero);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 확정 분류 변경은 원본을 바꾸지 않은 새 값을 만듭니다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_MovementFrameSample_과거Sample불변확정전환()
        {
            var backend = new StateReadCallbackBackend();
            var model = new MotionModel(actor, MotionModel.TopDown2DUp);
            var motor = new Motor<MotorState2D>(actor, backend);
            var frame = StepAndObserve(model, motor);
            var original = MovementFrameSample<MotorState2D>.Capture
            (
                model, motor, frame, false, MovementSampleConfirmation.Unconfirmed
            );

            // 다음 Tick이 진행된 뒤에도 보관한 과거 Sample만으로 확정 사본을 만든다.
            StepAndObserve(model, motor);
            var confirmed = original.WithConfirmation(MovementSampleConfirmation.Confirmed);

            Assert.That(original.Confirmation, Is.EqualTo(MovementSampleConfirmation.Unconfirmed));
            Assert.That(confirmed.Confirmation, Is.EqualTo(MovementSampleConfirmation.Confirmed));
            Assert.That(confirmed.Tick.Index, Is.Zero);
            Assert.That(confirmed.Frame.StepVersion, Is.EqualTo(original.Frame.StepVersion));
            Assert.Throws<ArgumentOutOfRangeException>
            (
                () => original.WithConfirmation((MovementSampleConfirmation)9)
            );
        }

    #endregion

    #region S-1: 출처·수명 검증

        // ------------------------------------------------------------
        /// <summary>
        /// 다른 Actor의 Model·Motor·결과를 묶지 않습니다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_MovementFrameSample_Actor출처검증()
        {
            var otherActor = new MotionActorKey(81, 2, 1);
            var model = new MotionModel(actor, MotionModel.TopDown2DUp);
            var otherModel = new MotionModel(otherActor, MotionModel.TopDown2DUp);
            var motor = new Motor<MotorState2D>(actor, new StateReadCallbackBackend());
            var otherMotor = new Motor<MotorState2D>(otherActor, new StateReadCallbackBackend());
            var frame = StepAndObserve(model, motor);
            var otherFrame = otherModel.StepLocal(0.02d, new MotionStepInput(walk), MotionFeedback.None);

            Assert.Throws<InvalidOperationException>
            (
                () => MovementFrameSample<MotorState2D>.Capture
                (
                    model, otherMotor, frame, false, MovementSampleConfirmation.Confirmed
                )
            );
            Assert.Throws<InvalidOperationException>
            (
                () => MovementFrameSample<MotorState2D>.Capture
                (
                    model, motor, otherFrame, false, MovementSampleConfirmation.Confirmed
                )
            );
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Restore 후 같은 Tick을 재실행하면 이전 결과는 캡처할 수 없습니다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_MovementFrameSample_Restore후동일Tick의오래된결과거절()
        {
            var model = new MotionModel(actor, MotionModel.TopDown2DUp);
            var motor = new Motor<MotorState2D>(actor, new StateReadCallbackBackend());
            var snapshot = model.Capture();
            var obsolete = StepAndObserve(model, motor);

            model.Restore(snapshot);
            var replayed = StepAndObserve(model, motor);

            Assert.That(replayed.Tick.Index, Is.EqualTo(obsolete.Tick.Index));
            Assert.That(replayed.StepVersion, Is.GreaterThan(obsolete.StepVersion));
            Assert.Throws<InvalidOperationException>
            (
                () => MovementFrameSample<MotorState2D>.Capture
                (
                    model, motor, obsolete, false, MovementSampleConfirmation.Unconfirmed
                )
            );
            Assert.DoesNotThrow
            (
                () => MovementFrameSample<MotorState2D>.Capture
                (
                    model, motor, replayed, false, MovementSampleConfirmation.Unconfirmed
                )
            );
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Tick과 다른 번호로 완료한 Physics Step은 별도 메시지로 거절합니다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_MovementFrameSample_PhysicsStep번호불일치거절()
        {
            var model = new MotionModel(actor, MotionModel.TopDown2DUp);
            var motor = new Motor<MotorState2D>(actor, new StateReadCallbackBackend());
            var frame = model.Step(new MotionTick(3, 0.02d), new MotionStepInput(walk), MotionFeedback.None);

            Assert.That(motor.TryApply(frame), Is.True);
            motor.BeginPhysicsStep();
            motor.CompletePhysicsStep(4);

            var exception = Assert.Throws<InvalidOperationException>
            (
                () => MovementFrameSample<MotorState2D>.Capture
                (
                    model, motor, frame, false, MovementSampleConfirmation.Unconfirmed
                )
            );
            Assert.That(exception.Message, Does.Contain("CompletePhysicsStep"));

            motor.BeginPhysicsStep();
            motor.CompletePhysicsStep(3);
            Assert.DoesNotThrow
            (
                () => MovementFrameSample<MotorState2D>.Capture
                (
                    model, motor, frame, false, MovementSampleConfirmation.Unconfirmed
                )
            );
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Physics 관측 전이나 Correction 직후에는 캡처하지 않습니다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_MovementFrameSample_Physics관측전거절()
        {
            var model = new MotionModel(actor, MotionModel.TopDown2DUp);
            var motor = new Motor<MotorState2D>(actor, new StateReadCallbackBackend());
            var frame = model.StepLocal(0.02d, new MotionStepInput(walk), MotionFeedback.None);

            Assert.Throws<InvalidOperationException>
            (
                () => MovementFrameSample<MotorState2D>.Capture
                (
                    model, motor, frame, false, MovementSampleConfirmation.Confirmed
                )
            );

            motor.BeginPhysicsStep();
            motor.CompletePhysicsStep(0);
            motor.ApplyCurrentState(default);
            Assert.Throws<InvalidOperationException>
            (
                () => MovementFrameSample<MotorState2D>.Capture
                (
                    model, motor, frame, false, MovementSampleConfirmation.Confirmed
                )
            );
        }

    #endregion

    #region R-1: 재진입과 복합 전환

        // ------------------------------------------------------------
        /// <summary>
        /// Body 조회 콜백의 Motor Correction은 재진입으로 거절합니다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_MovementFrameSample_State읽기중MotorCorrection재진입거절()
        {
            var backend = new StateReadCallbackBackend();
            var model = new MotionModel(actor, MotionModel.TopDown2DUp);
            var motor = new Motor<MotorState2D>(actor, backend);
            var frame = StepAndObserve(model, motor);

            backend.DuringRead = () => motor.ApplyCurrentState(default);

            Assert.Throws<InvalidOperationException>
            (
                () => MovementFrameSample<MotorState2D>.Capture
                (
                    model, motor, frame, false, MovementSampleConfirmation.Unconfirmed
                )
            );
            Assert.That(motor.DiscontinuityVersion, Is.Zero);
            Assert.That(motor.ObservedPhysicsStep, Is.Zero);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Body 조회 중 Model Restore가 일어나면 수명 검증에서 거절합니다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_MovementFrameSample_State읽기중MotionRestore거절()
        {
            var backend = new StateReadCallbackBackend();
            var model = new MotionModel(actor, MotionModel.TopDown2DUp);
            var motor = new Motor<MotorState2D>(actor, backend);
            var snapshot = model.Capture();
            var frame = StepAndObserve(model, motor);

            backend.DuringRead = () => model.Restore(snapshot);

            Assert.Throws<InvalidOperationException>
            (
                () => MovementFrameSample<MotorState2D>.Capture
                (
                    model, motor, frame, false, MovementSampleConfirmation.Unconfirmed
                )
            );
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> Model Restore·Motor Correction·Navigation 무효화를 함께 거친 뒤
        /// <br/> 이전 결과는 거절하고 새 관측의 Sample만 만듭니다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_MovementFrameSample_MotionRestoreMotorCorrectionNavigation무효화()
        {
            var go = new GameObject("TEST_MovementCombinedTransition");

            try
            {
                var body = go.AddComponent<Rigidbody2D>();
                body.gravityScale = 0f;
                var model = new MotionModel(actor, MotionModel.TopDown2DUp);
                var motor = new Motor<MotorState2D>(actor, new RigidbodyMotorBackend2D(body));
                var navigation = new NavigationController<Vector2>(actor);
                navigation.SetGoal(new Vector2(10f, 0f));
                var plan = navigation.BeginPlan(Vector2.zero, 7);
                var planWaypoints = new[]
                {
                    new Vector2(10f, 0f)
                };
                navigation.TryAcceptPath(plan, planWaypoints);
                Assert.That(navigation.TryGetGuidance(out var oldGuidance), Is.True);

                var initial = StepAndObserve(model, motor);
                var original = MovementFrameSample<MotorState2D>.Capture
                (
                    model, motor, initial, false, MovementSampleConfirmation.Unconfirmed
                );
                var snapshot = model.Capture();

                // 다음 Tick 결과와 Planner 응답이 대기하는 동안 Host가 세 모듈을 각각 교정한다.
                var obsolete = StepAndObserve(model, motor);
                var pendingPlan = navigation.BeginPlan(Vector2.zero, 7);

                model.Restore(snapshot);
                motor.ApplyCurrentState(new MotorState2D(new Vector2(4f, 5f), 0f, new Vector2(9f, 0f), 0f));
                navigation.Invalidate();

                Assert.That(motor.ObservedPhysicsStep, Is.EqualTo(-1));
                Assert.That(navigation.TryAdvanceWaypoint(oldGuidance), Is.False);
                var pendingWaypoints = new[]
                {
                    new Vector2(10f, 0f)
                };
                Assert.That
                (
                    navigation.TryAcceptPath(pendingPlan, pendingWaypoints),
                    Is.EqualTo(NavigationPlanApplyStatus.Stale)
                );
                Assert.Throws<InvalidOperationException>
                (
                    () => MovementFrameSample<MotorState2D>.Capture
                    (
                        model, motor, obsolete, false, MovementSampleConfirmation.Unconfirmed
                    )
                );

                // 재실행한 Tick과 새 Physics 관측으로만 Sample을 만든다.
                var current = model.StepLocal(0.02d, default, MotionFeedback.None);
                Assert.Throws<InvalidOperationException>
                (
                    () => MovementFrameSample<MotorState2D>.Capture
                    (
                        model, motor, current, false, MovementSampleConfirmation.Unconfirmed
                    )
                );

                Assert.That(motor.TryApply(current), Is.True);
                motor.BeginPhysicsStep();
                motor.CompletePhysicsStep(current.Tick.Index);
                var sample = MovementFrameSample<MotorState2D>.Capture
                (
                    model, motor, current, false, MovementSampleConfirmation.Unconfirmed
                );

                Assert.That(sample.CurrentState.Position, Is.EqualTo(new Vector2(4f, 5f)));
                Assert.That(sample.CurrentState.LinearVelocity, Is.EqualTo(new Vector2(9f, 0f)));
                Assert.That(sample.DiscontinuityVersion, Is.GreaterThan(original.DiscontinuityVersion));
                Assert.That(sample.State.LastDiscontinuity, Is.EqualTo(MotorDiscontinuityKind.CurrentStateApply));
                Assert.That(original.CurrentState.LinearVelocity, Is.EqualTo(new Vector2(2f, 3f)));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

    #endregion

    #region M-1: MovementState 값

        // ------------------------------------------------------------
        /// <summary>
        /// Override의 동작 ID와 시작 Tick이 이동 상태로 전달됩니다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_MovementFrameSample_동작ID와시작Tick전달()
        {
            var model = new MotionModel(actor, MotionModel.TopDown2DUp);
            var motor = new Motor<MotorState2D>(actor, new StateReadCallbackBackend());
            var dash = new MotionSourceKey(5, 1);

            model.StepLocal(0.02d, new MotionStepInput(walk), MotionFeedback.None);
            var dashInputs = new[]
            {
                MotionOverrideCommand.Begin(dash, MotionChannel.Planar, 10, new Vector3(9f, 0f, 0f), actionID: 7)
            };
            model.StepLocal(0.02d, new MotionStepInput(walk, dashInputs), MotionFeedback.None);
            model.StepLocal(0.02d, new MotionStepInput(walk), MotionFeedback.None);
            var frame = model.StepLocal(0.02d, new MotionStepInput(walk), MotionFeedback.None);

            Assert.That(motor.TryApply(frame), Is.True);
            motor.BeginPhysicsStep();
            motor.CompletePhysicsStep(frame.Tick.Index);
            var sample = MovementFrameSample<MotorState2D>.Capture
            (
                model, motor, frame, false, MovementSampleConfirmation.Unconfirmed
            );

            Assert.That(sample.State.ActionID, Is.EqualTo(7));
            Assert.That(sample.State.ActionStartTick, Is.EqualTo(1));
            Assert.That(sample.State.ActionElapsedTicks, Is.EqualTo(2));
        }

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> 원격 Actor는 복제 값만으로 같은 이동 상태를 재구성하고
        /// <br/> 로컬 속도는 Body 회전 기준으로 계산됩니다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_MovementFrameSample_원격재구성과로컬속도()
        {
            var rotation = Quaternion.Euler(0f, 90f, 0f);
            var velocity = new Vector3(1f, 2f, 0f);

            var server = MovementState.Create
            (
                actor, 12, Vector3.up, velocity, rotation, false, 3, 10,
                MotionChannel.Planar, MotorDiscontinuityKind.None
            );
            var remote = MovementState.Create
            (
                new MotionActorKey(82, 1, 1), 12, Vector3.up, velocity, rotation, false, 3, 10,
                MotionChannel.Planar, MotorDiscontinuityKind.None
            );

            // 월드 +X는 Y축 90도 회전한 Body의 전방이다.
            Assert.That((server.LocalVelocity - new Vector3(0f, 2f, 1f)).sqrMagnitude, Is.LessThan(1e-8f));
            Assert.That(server.PlanarSpeed, Is.EqualTo(1f).Within(1e-5f));
            Assert.That(server.VerticalSpeed, Is.EqualTo(2f).Within(1e-5f));
            Assert.That(server.ActionElapsedTicks, Is.EqualTo(2));
            Assert.That(server.BlockedChannels, Is.EqualTo(MotionChannel.Planar));

            Assert.That(remote.LocalVelocity, Is.EqualTo(server.LocalVelocity));
            Assert.That(remote.PlanarSpeed, Is.EqualTo(server.PlanarSpeed));
            Assert.That(remote.VerticalSpeed, Is.EqualTo(server.VerticalSpeed));
            Assert.That(remote.ActionID, Is.EqualTo(server.ActionID));
            Assert.Throws<ArgumentOutOfRangeException>
            (
                () => MovementState.Create
                (
                    actor, 0, Vector3.zero, velocity, rotation, false, 0, -1,
                    MotionChannel.None, MotorDiscontinuityKind.None
                )
            );
        }

    #endregion

    }
}
