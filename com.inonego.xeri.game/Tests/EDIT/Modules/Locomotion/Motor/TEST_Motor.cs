/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : TEST_Motor.cs
수정일 : 2026-11-21

# 설명
Motor의 합성 결과 적용·상태 교정·과거 복원·Contact 수명·실제 Rigidbody Backend 계약을 검증한다.

# 특이사항, 제약사항
EditMode 테스트로 Physics World Step과 충돌 접촉까지 검증하지 않는다.

# 테스트 구성
 A: 합성 결과 적용 (Apply)
 B: Rigidbody Backend (2D, 3D)
 C: 현재 상태 교정 (Correction)
 H: 과거 상태 캡처와 복원 (History)
 P: Physics Step 관측 수명과 재진입
 S: 센서 관측 세대 (Sensor)
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

namespace inonego.Xeri.Game.TEST.EDIT._Motor
{
    // ============================================================
    /// <summary>
    /// Motor의 현재 상태 적용과 선택적 History를 검증합니다.
    /// </summary>
    // ============================================================
    public sealed class TEST_Motor
    {

    #region 헬퍼

        private static readonly MotionActorKey actor = new(1, 1, 1);

        private sealed class HistoricalBody :
            IMotorBackend<MotorState2D>,
            IMotorHistoricalBackend<MotorState2D>
        {
            internal MotorState2D Current;
            internal bool AllowRestore = true;
            internal Action DuringCapture;
            internal Action DuringRestore;

            // ------------------------------------------------------------
            /// <summary>
            /// 현재 상태를 반환합니다.
            /// </summary>
            // ------------------------------------------------------------
            public MotorState2D ReadCurrentState() => Current;

            // ------------------------------------------------------------
            /// <summary>
            /// 권한 상태를 저장합니다.
            /// </summary>
            // ------------------------------------------------------------
            public void ApplyCurrentState(in MotorState2D state)
            {
                Current = state;
            }

            // ------------------------------------------------------------
            /// <summary>
            /// Planar가 Set인 합성 결과만 적용합니다.
            /// </summary>
            // ------------------------------------------------------------
            public bool TryApply(in MotionFrame frame)
            {
                if (frame.PlanarMode != MotionChannelMode.Set)
                {
                    return false;
                }

                var current = new Vector3(Current.LinearVelocity.x, Current.LinearVelocity.y, 0f);
                var linear = frame.ComposeLinear(current);
                Current = new MotorState2D
                (
                    Current.Position, Current.Rotation,
                    new Vector2(linear.x, linear.y), Current.AngularVelocity
                );
                return true;
            }

            // ------------------------------------------------------------
            /// <summary>
            /// 현재 Owned State를 저장합니다.
            /// </summary>
            // ------------------------------------------------------------
            public MotorState2D CaptureHistoricalState()
            {
                var captured = Current;
                DuringCapture?.Invoke();
                return captured;
            }

            // ------------------------------------------------------------
            /// <summary>
            /// 복원이 허용된 경우에만 상태를 되돌립니다.
            /// </summary>
            // ------------------------------------------------------------
            public bool TryRestoreHistoricalState(long step, in MotorState2D state)
            {
                if (!AllowRestore)
                {
                    return false;
                }

                Current = state;
                DuringRestore?.Invoke();
                return true;
            }
        }

        private sealed class FailingContactBeginBody :
            IMotorBackend<MotorState2D>,
            IMotorContactBackend<ContactPoint2D>
        {
            internal bool FailNextBegin = true;

            // ------------------------------------------------------------
            /// <summary>
            /// 임시 테스트 Body의 현재 상태를 반환합니다.
            /// </summary>
            // ------------------------------------------------------------
            public MotorState2D ReadCurrentState() => default;

            // ------------------------------------------------------------
            /// <summary>
            /// 임시 테스트 Body의 상태 적용을 흉내 냅니다.
            /// </summary>
            // ------------------------------------------------------------
            public void ApplyCurrentState(in MotorState2D state)
            {
                // NONE
            }

            // ------------------------------------------------------------
            /// <summary>
            /// 임시 테스트 Body는 운동 적용을 지원하지 않습니다.
            /// </summary>
            // ------------------------------------------------------------
            public bool TryApply(in MotionFrame frame) => false;

            // ------------------------------------------------------------
            /// <summary>
            /// 실제 접촉 관측 기능이 있는 Backend로 동작합니다.
            /// </summary>
            // ------------------------------------------------------------
            public bool SupportsContactObservation => true;

            // ------------------------------------------------------------
            /// <summary>
            /// 첫 번째 Begin에서 오류를 발생시키고 다음에는 성공합니다.
            /// </summary>
            // ------------------------------------------------------------
            public void BeginContactStep()
            {
                if (FailNextBegin)
                {
                    FailNextBegin = false;
                    throw new InvalidOperationException("테스트용 Contact Begin 실패");
                }
            }

            // ------------------------------------------------------------
            /// <summary>
            /// 임시 테스트 Backend의 관측을 초기화합니다.
            /// </summary>
            // ------------------------------------------------------------
            public void ClearContactObservation()
            {
                // NONE
            }

            // ------------------------------------------------------------
            /// <summary>
            /// 이 테스트 Backend의 Contact는 빈 배열입니다.
            /// </summary>
            // ------------------------------------------------------------
            public int ReadContacts(ContactPoint2D[] results) => 0;
        }

        private sealed class RevisionContactBody :
            IMotorBackend<MotorState2D>,
            IMotorContactBackend<ContactPoint2D>,
            IMotorContactObservationRevision
        {
            // ------------------------------------------------------------
            /// <summary>
            /// 일시적으로 변경할 수 있는 Contact 지원 상태입니다.
            /// </summary>
            // ------------------------------------------------------------
            public bool SupportsContactObservation => isAvailable;
            private bool isAvailable = true;

            // ------------------------------------------------------------
            /// <summary>
            /// 센서 연결 수명을 나타내는 세대입니다.
            /// </summary>
            // ------------------------------------------------------------
            public long ContactObservationRevision { get; private set; }

            internal Action DuringContactRead;
            internal Action DuringContactBegin;
            internal Action DuringContactClear;

            internal void SetAvailable(bool available)
            {
                isAvailable = available;
                ContactObservationRevision++;
            }

            // ------------------------------------------------------------
            /// <summary>
            /// 테스트용 Body의 현재 상태입니다.
            /// </summary>
            // ------------------------------------------------------------
            public MotorState2D ReadCurrentState() => default;

            // ------------------------------------------------------------
            /// <summary>
            /// 테스트용 Body의 현재 상태 적용입니다.
            /// </summary>
            // ------------------------------------------------------------
            public void ApplyCurrentState(in MotorState2D state)
            {
                // NONE
            }

            // ------------------------------------------------------------
            /// <summary>
            /// 테스트용 Body는 운동 명령을 적용하지 않습니다.
            /// </summary>
            // ------------------------------------------------------------
            public bool TryApply(in MotionFrame frame) => false;

            // ------------------------------------------------------------
            /// <summary>
            /// 관측 Step의 시작을 허용합니다.
            /// </summary>
            // ------------------------------------------------------------
            public void BeginContactStep()
            {
                DuringContactBegin?.Invoke();
            }

            // ------------------------------------------------------------
            /// <summary>
            /// 테스트용 Contact 상태를 초기화합니다.
            /// </summary>
            // ------------------------------------------------------------
            public void ClearContactObservation()
            {
                DuringContactClear?.Invoke();
            }

            // ------------------------------------------------------------
            /// <summary>
            /// 빈 Contact를 반환합니다.
            /// </summary>
            // ------------------------------------------------------------
            public int ReadContacts(ContactPoint2D[] results)
            {
                DuringContactRead?.Invoke();
                return 0;
            }
        }

        private static MotionFrame CreateFrame
        (
            MotionActorKey frameActor, Vector3 up,
            MotionChannelMode planarMode, Vector3 planar,
            MotionChannelMode elevationMode = MotionChannelMode.Free, float elevation = 0f,
            MotionChannelMode rotationMode = MotionChannelMode.Free, Vector3 rotation = default,
            Vector3 impulse = default
        ) => new
        (
            frameActor, new MotionTick(0, 0.02d), 1, up,
            planarMode, planar, elevationMode, elevation, rotationMode, rotation,
            impulse, 0, -1
        );

    #endregion

    #region A-1: 합성 결과 적용

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> Model 합성 결과를 같은 Motor가 Tick당 한 번 적용합니다.
        /// <br/> 다음 Physics Step 전 재적용과 Step 중 적용은 거절합니다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_Motor_합성결과Tick당한번적용()
        {
            var model = new MotionModel(actor, MotionModel.TopDown2DUp);
            var body = new HistoricalBody();
            var motor = new Motor<MotorState2D>(actor, body);
            var inputs = new[]
            {
                new MotionIntent(MotionChannel.Planar, new Vector3(3f, 4f, 0f))
            };

            var frame = model.StepLocal(0.02d, new MotionStepInput(inputs));
            Assert.That(motor.TryApply(frame), Is.True);
            Assert.That(motor.ReadCurrentState().LinearVelocity, Is.EqualTo(new Vector2(3f, 4f)));
            Assert.That(motor.DiscontinuityVersion, Is.Zero);
            Assert.Throws<InvalidOperationException>(() => motor.TryApply(frame));

            // Physics Step 경계를 넘으면 다음 Tick 결과를 다시 받는다.
            motor.BeginPhysicsStep();
            var next = model.StepLocal(0.02d, new MotionStepInput(inputs));
            Assert.Throws<InvalidOperationException>(() => motor.TryApply(next));
            motor.CompletePhysicsStep(0);
            Assert.That(motor.TryApply(next), Is.True);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 다른 Actor의 합성 결과는 Body에 적용하지 않습니다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_Motor_다른ActorMotion적용거절()
        {
            var backend = new HistoricalBody();
            var motor = new Motor<MotorState2D>(actor, backend);
            var other = new MotionActorKey(1, 2, 1);
            var velocity = new Vector3(10f, 20f, 0f);

            Assert.That(motor.TryApply(CreateFrame(other, MotionModel.TopDown2DUp, MotionChannelMode.Set, velocity)), Is.False);
            Assert.That(motor.ReadCurrentState().LinearVelocity, Is.EqualTo(Vector2.zero));
            Assert.That(motor.TryApply(CreateFrame(actor, MotionModel.TopDown2DUp, MotionChannelMode.Set, velocity)), Is.True);
        }

    #endregion

    #region B-1: Rigidbody Backend

        // ------------------------------------------------------------
        /// <summary>
        /// 2D Rigidbody는 현재 상태와 채널 제어만 지원합니다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_Motor_Rigidbody2D현재상태와미지원기능()
        {
            var go = new GameObject("TEST_Motor2D");
            try
            {
                var rigidbody = go.AddComponent<Rigidbody2D>();
                rigidbody.gravityScale = 0f;
                var backend = new RigidbodyMotorBackend2D(rigidbody);
                var motor = new Motor<MotorState2D>(actor, backend);
                var state = new MotorState2D
                (
                    new Vector2(1f, 2f), 20f,
                    new Vector2(0f, 3f), 10f
                );

                motor.ApplyCurrentState(state);
                var read = motor.ReadCurrentState();
                Assert.That(read.Position.x, Is.EqualTo(1f).Within(0.01f));
                Assert.That(read.Position.y, Is.EqualTo(2f).Within(0.01f));
                Assert.That(read.LinearVelocity.y, Is.EqualTo(3f).Within(0.01f));
                Assert.That(motor.SupportsHistoricalRestore, Is.False);
                Assert.That(motor.TryCaptureHistorical(0, out _), Is.False);
                var historicalBody = new HistoricalBody();
                var historical = new Motor<MotorState2D>(actor, historicalBody);
                Assert.That(historical.TryCaptureHistorical(0, out var saved), Is.True);
                Assert.That(motor.RestoreHistorical(saved), Is.EqualTo(MotorRestoreStatus.Unsupported));

                // Planar만 Set이면 Free인 Rotation은 기존 각속도를 유지한다.
                var planar = CreateFrame(actor, MotionModel.TopDown2DUp, MotionChannelMode.Set, new Vector3(6f, 7f, 0f));
                Assert.That(motor.TryApply(planar), Is.True);
                Assert.That(motor.ReadCurrentState().LinearVelocity.x, Is.EqualTo(6f).Within(0.01f));
                Assert.That(motor.ReadCurrentState().AngularVelocity, Is.EqualTo(10f).Within(0.01f));

                // Rotation은 rad/s를 도/초로 바꿔 쓴다.
                var rotation = CreateFrame
                (
                    actor, MotionModel.TopDown2DUp, MotionChannelMode.Free, Vector3.zero,
                    rotationMode: MotionChannelMode.Set, rotation: new Vector3(0f, 0f, Mathf.PI)
                );
                Assert.That(backend.TryApply(rotation), Is.True);
                Assert.That(motor.ReadCurrentState().AngularVelocity, Is.EqualTo(180f).Within(0.01f));
                Assert.That(motor.ReadCurrentState().LinearVelocity.x, Is.EqualTo(6f).Within(0.01f));

                var invalid = CreateFrame(actor, MotionModel.TopDown2DUp, MotionChannelMode.Set, new Vector3(float.NaN, 0f, 0f));
                Assert.That(backend.TryApply(invalid), Is.False);
                Assert.That(motor.ReadCurrentState().LinearVelocity.x, Is.EqualTo(6f).Within(0.01f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 3D Rigidbody의 Planar/Elevation이 서로를 덮지 않습니다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_Motor_Rigidbody3D채널분리와CurrentApply()
        {
            var go = new GameObject("TEST_Motor3D");
            try
            {
                var rigidbody = go.AddComponent<Rigidbody>();
                rigidbody.useGravity = false;
                var backend = new RigidbodyMotorBackend3D(rigidbody);
                var motor = new Motor<MotorState3D>(actor, backend);
                motor.ApplyCurrentState
                (
                    new MotorState3D
                    (
                        new Vector3(1f, 2f, 3f), Quaternion.identity,
                        new Vector3(0f, 5f, 0f), new Vector3(0f, 1f, 0f)
                    )
                );

                var planar = CreateFrame(actor, Vector3.up, MotionChannelMode.Set, new Vector3(9f, 0f, 4f));
                Assert.That(motor.TryApply(planar), Is.True);
                Assert.That(motor.ReadCurrentState().LinearVelocity.y, Is.EqualTo(5f).Within(0.01f));
                Assert.That(motor.ReadCurrentState().LinearVelocity.x, Is.EqualTo(9f).Within(0.01f));

                var elevation = CreateFrame
                (
                    actor, Vector3.up, MotionChannelMode.Free, Vector3.zero, MotionChannelMode.Set, 7f
                );
                Assert.That(backend.TryApply(elevation), Is.True);
                Assert.That(motor.ReadCurrentState().LinearVelocity.x, Is.EqualTo(9f).Within(0.01f));
                Assert.That(motor.ReadCurrentState().LinearVelocity.y, Is.EqualTo(7f).Within(0.01f));
                Assert.That(motor.ReadCurrentState().LinearVelocity.z, Is.EqualTo(4f).Within(0.01f));

                Assert.That(motor.ReadCurrentState().AngularVelocity.y, Is.EqualTo(1f).Within(0.01f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> 유한 입력도 계산 중 오버플로하면 거절합니다.
        /// <br/> 거절한 명령과 상태는 Rigidbody를 바꾸지 않습니다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_Motor_Rigidbody3D오버플로입력거절()
        {
            var go = new GameObject("TEST_MotorRigidbody3DOverflow");

            try
            {
                var body = go.AddComponent<Rigidbody>();
                body.useGravity = false;
                var backend = new RigidbodyMotorBackend3D(body);
                var initial = new MotorState3D
                (
                    new Vector3(1f, 2f, 3f), Quaternion.identity,
                    new Vector3(4f, 5f, 6f), new Vector3(0f, 1f, 0f)
                );
                backend.ApplyCurrentState(initial);

                // 각 값은 유한하지만 Planar와 Impulse를 더하면 float 범위를 넘는다.
                var huge = new Vector3(3e38f, 3e38f, 3e38f);
                var overflow = CreateFrame
                (
                    actor, new Vector3(1f, 1f, 1f).normalized, MotionChannelMode.Set, huge,
                    rotationMode: MotionChannelMode.Set, rotation: new Vector3(0f, 2f, 0f), impulse: huge
                );

                Assert.That(backend.TryApply(overflow), Is.False);
                Assert.That((body.linearVelocity - initial.LinearVelocity).sqrMagnitude, Is.LessThan(1e-6f));
                Assert.That((body.angularVelocity - initial.AngularVelocity).sqrMagnitude, Is.LessThan(1e-6f));

                // 성분은 유한하지만 크기 제곱이 오버플로하는 회전은 거절한다.
                Assert.Throws<ArgumentOutOfRangeException>
                (
                    () => backend.ApplyCurrentState
                    (
                        new MotorState3D
                        (
                            Vector3.zero, new Quaternion(1e20f, 0f, 0f, 0f),
                            Vector3.zero, Vector3.zero
                        )
                    )
                );
                Assert.That((body.position - initial.Position).sqrMagnitude, Is.LessThan(1e-6f));
                Assert.That(Quaternion.Angle(body.rotation, initial.Rotation), Is.LessThan(0.01f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

    #endregion

    #region C-1: 현재 상태 교정

        // ------------------------------------------------------------
        /// <summary>
        /// 현재 상태 교정은 기존 History를 무효화합니다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_Motor_CurrentStateApply기존History무효화()
        {
            var body = new HistoricalBody();
            var motor = new Motor<MotorState2D>(actor, body);

            Assert.That(motor.TryCaptureHistorical(5, out var saved), Is.True);
            motor.ApplyCurrentState
            (
                new MotorState2D
                (
                    new Vector2(7f, 8f), 90f,
                    new Vector2(2f, 0f), 12f
                )
            );

            Assert.That(motor.RestoreHistorical(saved), Is.EqualTo(MotorRestoreStatus.StaleHistory));
            Assert.That(motor.ReadCurrentState().Position, Is.EqualTo(new Vector2(7f, 8f)));
            Assert.That(motor.DiscontinuityVersion, Is.EqualTo(1));
            Assert.That(motor.LastDiscontinuity, Is.EqualTo(MotorDiscontinuityKind.CurrentStateApply));
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 외부 Actor 매핑을 사용한 Authority State 교정을 검증합니다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_Motor_서버확정State를클라이언트에적용()
        {
            var serverActor = new MotionActorKey(1, 1, 1);
            var clientActor = new MotionActorKey(2, 1, 1);
            var server = new Motor<MotorState2D>
            (
                serverActor, new HistoricalBody()
            );
            var client = new Motor<MotorState2D>
            (
                clientActor, new HistoricalBody()
            );

            Assert.That(client.TryCaptureHistorical(4, out var predicted), Is.True);

            var authoritative = new MotorState2D
            (
                new Vector2(14f, 3f), 35f,
                new Vector2(5f, 1f), 0f
            );
            server.ApplyCurrentState(authoritative);
            client.ApplyCurrentState(server.ReadCurrentState());

            Assert.That(client.ReadCurrentState().Position, Is.EqualTo(authoritative.Position));
            Assert.That(client.RestoreHistorical(predicted), Is.EqualTo(MotorRestoreStatus.StaleHistory));
            Assert.That(client.LastDiscontinuity, Is.EqualTo(MotorDiscontinuityKind.CurrentStateApply));
            Assert.That(client.DiscontinuityVersion, Is.EqualTo(1));
        }

    #endregion

    #region H-1: 과거 상태 캡처와 복원

        // ------------------------------------------------------------
        /// <summary>
        /// Historical 조회 콜백의 Correction은 재진입으로 거절합니다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_Motor_HistoricalCapture중Correction재진입거절()
        {
            var body = new HistoricalBody
            {
                Current = new MotorState2D
                (
                    new Vector2(1f, 2f), 0f, Vector2.zero, 0f
                )
            };
            var motor = new Motor<MotorState2D>(actor, body);

            body.DuringCapture = () => motor.ApplyCurrentState
            (
                new MotorState2D
                (
                    new Vector2(8f, 9f), 0f, Vector2.zero, 0f
                )
            );

            Assert.Throws<InvalidOperationException>(() => motor.TryCaptureHistorical(5, out _));
            Assert.That(motor.ReadCurrentState().Position, Is.EqualTo(new Vector2(1f, 2f)));
            Assert.That(motor.DiscontinuityVersion, Is.Zero);

            body.DuringCapture = null;
            Assert.That(motor.TryCaptureHistorical(6, out var current), Is.True);
            Assert.That(current.State.Position, Is.EqualTo(new Vector2(1f, 2f)));
            Assert.That(motor.RestoreHistorical(current), Is.EqualTo(MotorRestoreStatus.Restored));
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 과거 Snapshot은 재시뮬레이션에서 반복 복원할 수 있습니다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_Motor_HistoricalRestore반복가능()
        {
            var body = new HistoricalBody();
            var motor = new Motor<MotorState2D>(actor, body);
            Assert.That(motor.TryCaptureHistorical(3, out var saved), Is.True);

            body.Current = new MotorState2D(new Vector2(4f, 5f), 0f, Vector2.zero, 0f);
            Assert.That(motor.RestoreHistorical(saved), Is.EqualTo(MotorRestoreStatus.Restored));
            Assert.That(motor.ReadCurrentState().Position, Is.EqualTo(Vector2.zero));

            body.Current = new MotorState2D(new Vector2(8f, 9f), 0f, Vector2.zero, 0f);
            Assert.That(motor.RestoreHistorical(saved), Is.EqualTo(MotorRestoreStatus.Restored));
            Assert.That(motor.ReadCurrentState().Position, Is.EqualTo(Vector2.zero));
            Assert.That(motor.DiscontinuityVersion, Is.EqualTo(2));
            Assert.That(motor.LastDiscontinuity, Is.EqualTo(MotorDiscontinuityKind.HistoricalRestore));
        }

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> 복원 콜백의 Correction은 재진입으로 거절합니다.
        /// <br/> 실패한 복원은 불연속을 기록하지 않습니다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_Motor_HistoricalRestore중Correction재진입거절()
        {
            var body = new HistoricalBody
            {
                Current = new MotorState2D
                (
                    new Vector2(1f, 2f), 0f, Vector2.zero, 0f
                )
            };
            var motor = new Motor<MotorState2D>(actor, body);
            Assert.That(motor.TryCaptureHistorical(5, out var snapshot), Is.True);

            body.Current = new MotorState2D
            (
                new Vector2(4f, 5f), 0f, Vector2.zero, 0f
            );
            body.DuringRestore = () => motor.ApplyCurrentState
            (
                new MotorState2D
                (
                    new Vector2(8f, 9f), 0f, Vector2.zero, 0f
                )
            );

            Assert.Throws<InvalidOperationException>(() => motor.RestoreHistorical(snapshot));
            Assert.That(motor.LastDiscontinuity, Is.EqualTo(MotorDiscontinuityKind.None));
            Assert.That(motor.DiscontinuityVersion, Is.Zero);

            body.DuringRestore = null;
            Assert.That(motor.RestoreHistorical(snapshot), Is.EqualTo(MotorRestoreStatus.Restored));
            Assert.That(motor.ReadCurrentState().Position, Is.EqualTo(new Vector2(1f, 2f)));
            Assert.That(motor.DiscontinuityVersion, Is.EqualTo(1));
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 다른 Actor의 Snapshot을 복원하지 않습니다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_Motor_HistoryActor격리와Backend거절()
        {
            var firstBody = new HistoricalBody();
            var secondBody = new HistoricalBody();
            var first = new Motor<MotorState2D>(actor, firstBody);
            var second = new Motor<MotorState2D>
            (
                new MotionActorKey(1, 2, 1), secondBody
            );

            Assert.That(first.TryCaptureHistorical(0, out var saved), Is.True);
            Assert.That(second.RestoreHistorical(saved), Is.EqualTo(MotorRestoreStatus.ActorMismatch));

            firstBody.AllowRestore = false;
            Assert.That(first.RestoreHistorical(saved), Is.EqualTo(MotorRestoreStatus.BackendRejected));
            Assert.That(first.DiscontinuityVersion, Is.Zero);
        }

    #endregion

    #region P-1: Step 수명

        // ------------------------------------------------------------
        /// <summary>
        /// Contact 관측은 완료된 Physics Step에만 접근할 수 있습니다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_Motor_접촉관측수명과Correction무효화()
        {
            var go = new GameObject("TEST_MotorContact2D");

            try
            {
                var rb = go.AddComponent<Rigidbody2D>();
                var motor = new Motor<MotorState2D>
                (
                    actor, new RigidbodyMotorBackend2D(rb)
                );
                var contacts = new ContactPoint2D[8];
                var reader = new MotorContactReader<MotorState2D, ContactPoint2D>(motor);

                Assert.That(reader.IsSupported, Is.True);
                Assert.That(motor.ObservedPhysicsStep, Is.EqualTo(-1));
                Assert.That(reader.Read(contacts, out _), Is.EqualTo(MotorContactReadStatus.NotObserved));
                Assert.Throws<InvalidOperationException>(() => motor.CompletePhysicsStep(0));
                Assert.Throws<ArgumentNullException>(() => reader.Read(null, out _));

                motor.BeginPhysicsStep();
                Assert.That(reader.Read(contacts, out _), Is.EqualTo(MotorContactReadStatus.NotObserved));
                motor.CompletePhysicsStep(0);

                Assert.That(reader.Read(contacts, out var count), Is.EqualTo(MotorContactReadStatus.Ready));
                Assert.That(count, Is.Zero);
                Assert.That(motor.ObservedPhysicsStep, Is.Zero);
                Assert.Throws<ArgumentException>
                (
                    () => new MotorContactReader<MotorState2D, ContactPoint>(motor)
                );

                motor.ApplyCurrentState
                (
                    new MotorState2D
                    (
                        new Vector2(5f, 4f), 0f, Vector2.zero, 0f
                    )
                );

                Assert.That(reader.Read(contacts, out _), Is.EqualTo(MotorContactReadStatus.NotObserved));
                Assert.That(motor.ObservedPhysicsStep, Is.EqualTo(-1));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 진행 중인 Physics Step을 다시 시작해 접촉을 잃지 않습니다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_Motor_중복BeginPhysicsStep거절()
        {
            var go = new GameObject("TEST_MotorDuplicateBegin");

            try
            {
                var body = go.AddComponent<Rigidbody2D>();
                var motor = new Motor<MotorState2D>
                (
                    actor, new RigidbodyMotorBackend2D(body)
                );
                motor.BeginPhysicsStep();
                var version = motor.PhysicsObservationVersion;

                Assert.Throws<InvalidOperationException>(() => motor.BeginPhysicsStep());
                Assert.That(motor.PhysicsObservationVersion, Is.EqualTo(version));
                motor.CompletePhysicsStep(7);
                Assert.That(motor.ObservedPhysicsStep, Is.EqualTo(7));

                motor.BeginPhysicsStep();
                motor.CompletePhysicsStep(8);
                Assert.That(motor.ObservedPhysicsStep, Is.EqualTo(8));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 진행 중인 Step의 Correction은 해당 관측을 취소합니다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_Motor_진행중Correction과반복보정의Step수명()
        {
            var go = new GameObject("TEST_MotorCorrectionDuringStep");

            try
            {
                var body = go.AddComponent<Rigidbody2D>();
                var motor = new Motor<MotorState2D>
                (
                    actor, new RigidbodyMotorBackend2D(body)
                );
                var contacts = new ContactPoint2D[4];
                var reader = new MotorContactReader<MotorState2D, ContactPoint2D>(motor);

                motor.BeginPhysicsStep();
                var firstVersion = motor.PhysicsObservationVersion;
                motor.ApplyCurrentState
                (
                    new MotorState2D
                    (
                        new Vector2(1f, 2f), 0f,
                        Vector2.zero, 0f
                    )
                );

                Assert.That(motor.ObservedPhysicsStep, Is.EqualTo(-1));
                Assert.That(motor.PhysicsObservationVersion, Is.GreaterThan(firstVersion));
                var status = reader.Read(contacts, out _);
                Assert.That(status, Is.EqualTo(MotorContactReadStatus.NotObserved));
                Assert.Throws<InvalidOperationException>(() => motor.CompletePhysicsStep(0));
                Assert.That(motor.DiscontinuityVersion, Is.EqualTo(1));

                motor.BeginPhysicsStep();
                motor.CompletePhysicsStep(1);
                Assert.That(motor.ObservedPhysicsStep, Is.EqualTo(1));

                motor.ApplyCurrentState
                (
                    new MotorState2D
                    (
                        new Vector2(3f, 4f), 0f,
                        Vector2.zero, 0f
                    )
                );

                status = reader.Read(contacts, out _);
                Assert.That(status, Is.EqualTo(MotorContactReadStatus.NotObserved));
                Assert.That(motor.DiscontinuityVersion, Is.EqualTo(2));
                Assert.That(motor.ObservedPhysicsStep, Is.EqualTo(-1));
                motor.BeginPhysicsStep();
                motor.CompletePhysicsStep(2);
                Assert.That(motor.ObservedPhysicsStep, Is.EqualTo(2));
                Assert.That(motor.ReadCurrentState().Position, Is.EqualTo(new Vector2(3f, 4f)));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Backend Begin 실패 뒤에는 새 Physics Step을 시작할 수 있습니다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_Motor_BackendBegin예외후재시작가능()
        {
            var body = new FailingContactBeginBody();
            var motor = new Motor<MotorState2D>(actor, body);
            var contacts = new ContactPoint2D[4];
            var reader = new MotorContactReader<MotorState2D, ContactPoint2D>(motor);

            Assert.Throws<InvalidOperationException>(() => motor.BeginPhysicsStep());
            Assert.That(motor.ObservedPhysicsStep, Is.EqualTo(-1));
            var status = reader.Read(contacts, out _);
            Assert.That(status, Is.EqualTo(MotorContactReadStatus.NotObserved));
            Assert.Throws<InvalidOperationException>(() => motor.CompletePhysicsStep(0));

            motor.BeginPhysicsStep();
            motor.CompletePhysicsStep(1);
            Assert.That(motor.ObservedPhysicsStep, Is.EqualTo(1));
            status = reader.Read(contacts, out var count);
            Assert.That(status, Is.EqualTo(MotorContactReadStatus.Ready));
            Assert.That(count, Is.Zero);
        }

    #endregion

    #region P-2: 콜백 재진입 거절

        // ------------------------------------------------------------
        /// <summary>
        /// Contact 시작 콜백의 Correction은 재진입으로 거절합니다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_Motor_ContactBegin중Correction재진입거절()
        {
            var body = new RevisionContactBody();
            var motor = new Motor<MotorState2D>(actor, body);
            var reader = new MotorContactReader<MotorState2D, ContactPoint2D>(motor);
            body.DuringContactBegin = () => motor.ApplyCurrentState
            (
                new MotorState2D
                (
                    new Vector2(3f, 4f), 0f, Vector2.zero, 0f
                )
            );

            Assert.Throws<InvalidOperationException>(() => motor.BeginPhysicsStep());
            Assert.That(motor.ObservedPhysicsStep, Is.EqualTo(-1));
            Assert.That(motor.LastDiscontinuity, Is.EqualTo(MotorDiscontinuityKind.None));
            Assert.That(motor.DiscontinuityVersion, Is.Zero);
            Assert.Throws<InvalidOperationException>(() => motor.CompletePhysicsStep(0));

            body.DuringContactBegin = null;
            motor.BeginPhysicsStep();
            motor.CompletePhysicsStep(1);
            Assert.That(motor.ObservedPhysicsStep, Is.EqualTo(1));
            Assert.That
            (
                reader.Read(new ContactPoint2D[4], out _),
                Is.EqualTo(MotorContactReadStatus.Ready)
            );
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Contact 초기화 중 중첩 Begin은 기존 시작을 방해하지 않습니다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_Motor_ContactBegin중중첩Begin거절()
        {
            var body = new RevisionContactBody();
            var motor = new Motor<MotorState2D>(actor, body);
            var beginCalls = 0;
            var nestedAccepted = false;

            body.DuringContactBegin = () =>
            {
                beginCalls++;

                if (beginCalls == 1)
                {
                    try
                    {
                        motor.BeginPhysicsStep();
                        nestedAccepted = true;
                    }
                    catch (InvalidOperationException)
                    {
                        // NONE
                    }
                }
            };

            Assert.DoesNotThrow(() => motor.BeginPhysicsStep());
            Assert.That(beginCalls, Is.EqualTo(1));
            Assert.That(nestedAccepted, Is.False);
            motor.CompletePhysicsStep(2);
            Assert.That(motor.ObservedPhysicsStep, Is.EqualTo(2));
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Contact 초기화 콜백의 Correction은 재진입으로 거절합니다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_Motor_ContactClear중Correction재진입거절()
        {
            var body = new RevisionContactBody();
            var motor = new Motor<MotorState2D>(actor, body);
            var reader = new MotorContactReader<MotorState2D, ContactPoint2D>(motor);
            var corrected = false;
            body.DuringContactClear = () =>
            {
                if (corrected)
                {
                    return;
                }

                corrected = true;
                motor.ApplyCurrentState(default);
            };

            Assert.Throws<InvalidOperationException>(() => motor.BeginPhysicsStep());
            Assert.That(corrected, Is.True);
            Assert.That(motor.ObservedPhysicsStep, Is.EqualTo(-1));
            Assert.That(motor.DiscontinuityVersion, Is.Zero);
            Assert.Throws<InvalidOperationException>(() => motor.CompletePhysicsStep(20));

            body.DuringContactClear = null;
            motor.BeginPhysicsStep();
            motor.CompletePhysicsStep(21);
            Assert.That(motor.ObservedPhysicsStep, Is.EqualTo(21));
            Assert.That
            (
                reader.Read(new ContactPoint2D[1], out _),
                Is.EqualTo(MotorContactReadStatus.Ready)
            );
        }

    #endregion

    #region S-1: 센서 관측 세대

        // ------------------------------------------------------------
        /// <summary>
        /// 센서 수명 변경 뒤에는 새 Step 전까지 관측을 허용하지 않습니다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_Motor_센서관측세대변경후새Step전까지무효()
        {
            var body = new RevisionContactBody();
            var motor = new Motor<MotorState2D>(actor, body);
            var contacts = new ContactPoint2D[4];
            var reader = new MotorContactReader<MotorState2D, ContactPoint2D>(motor);

            motor.BeginPhysicsStep();
            motor.CompletePhysicsStep(0);
            var status = reader.Read(contacts, out _);
            Assert.That(status, Is.EqualTo(MotorContactReadStatus.Ready));

            body.SetAvailable(false);
            status = reader.Read(contacts, out _);
            Assert.That(status, Is.EqualTo(MotorContactReadStatus.Unsupported));
            body.SetAvailable(true);

            // 실제 Physics Step을 다시 실행하지 않았으므로 Ready가 아니다.
            status = reader.Read(contacts, out _);
            Assert.That(status, Is.EqualTo(MotorContactReadStatus.NotObserved));

            motor.BeginPhysicsStep();
            motor.CompletePhysicsStep(1);
            status = reader.Read(contacts, out _);
            Assert.That(status, Is.EqualTo(MotorContactReadStatus.Ready));

            body.SetAvailable(false);
            motor.BeginPhysicsStep();
            motor.CompletePhysicsStep(2);
            body.SetAvailable(true);
            status = reader.Read(contacts, out _);
            Assert.That(status, Is.EqualTo(MotorContactReadStatus.NotObserved));

            motor.BeginPhysicsStep();
            motor.CompletePhysicsStep(3);
            status = reader.Read(contacts, out _);
            Assert.That(status, Is.EqualTo(MotorContactReadStatus.Ready));
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Contact를 복사하는 중 센서 세대가 바뀌면 Ready를 거절합니다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_Motor_Contact복사중센서수명변경은미관측()
        {
            var body = new RevisionContactBody();
            var motor = new Motor<MotorState2D>(actor, body);
            var contacts = new ContactPoint2D[4];
            var reader = new MotorContactReader<MotorState2D, ContactPoint2D>(motor);
            motor.BeginPhysicsStep();
            motor.CompletePhysicsStep(0);

            // 복사 도중 센서가 비활성·재활성되면 이전 결과를 유효한 관측으로 볼 수 없다.
            body.DuringContactRead = () =>
            {
                body.SetAvailable(false);
                body.SetAvailable(true);
            };

            Assert.That
            (
                reader.Read(contacts, out var count),
                Is.EqualTo(MotorContactReadStatus.NotObserved)
            );
            Assert.That(count, Is.Zero);

            body.DuringContactRead = null;
            Assert.That
            (
                reader.Read(contacts, out _),
                Is.EqualTo(MotorContactReadStatus.NotObserved)
            );

            motor.BeginPhysicsStep();
            motor.CompletePhysicsStep(1);
            Assert.That
            (
                reader.Read(contacts, out count),
                Is.EqualTo(MotorContactReadStatus.Ready)
            );
            Assert.That(count, Is.Zero);

            // 복사 콜백의 Body Correction은 재진입이므로 관측을 바꾸지 않고 거절한다.
            body.DuringContactRead = () => motor.ApplyCurrentState(default);
            Assert.Throws<InvalidOperationException>(() => reader.Read(contacts, out _));
            Assert.That(motor.ObservedPhysicsStep, Is.EqualTo(1));
            Assert.That(motor.DiscontinuityVersion, Is.Zero);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 센서가 연결되지 않은 3D Backend는 Contact 미지원을 반환합니다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_Motor_3D미연결센서는ContactUnsupported()
        {
            var go = new GameObject("TEST_MotorNoSensor3D");
            var other = new GameObject("TEST_MotorOtherSensor");

            try
            {
                var rigidbody = go.AddComponent<Rigidbody>();
                var backend = new RigidbodyMotorBackend3D(rigidbody);
                var motor = new Motor<MotorState3D>(actor, backend);
                var contacts = new ContactPoint[4];
                var reader = new MotorContactReader<MotorState3D, ContactPoint>(motor);

                Assert.That(reader.IsSupported, Is.False);
                Assert.That(reader.Read(contacts, out _), Is.EqualTo(MotorContactReadStatus.Unsupported));

                motor.BeginPhysicsStep();
                motor.CompletePhysicsStep(0);
                Assert.That(reader.Read(contacts, out var count), Is.EqualTo(MotorContactReadStatus.Unsupported));
                Assert.That(count, Is.Zero);

                var foreignSensor = other.AddComponent<MotorCollisionProbe3D>();
                Assert.Throws<ArgumentException>
                (
                    () => new RigidbodyMotorBackend3D(rigidbody, foreignSensor)
                );

                var ownSensor = go.AddComponent<MotorCollisionProbe3D>();
                var enabledBackend = new RigidbodyMotorBackend3D(rigidbody, ownSensor);
                var enabledMotor = new Motor<MotorState3D>(actor, enabledBackend);
                var enabledReader = new MotorContactReader<MotorState3D, ContactPoint>(enabledMotor);

                Assert.That(enabledReader.IsSupported, Is.True);
                ownSensor.enabled = false;
                Assert.That(enabledReader.IsSupported, Is.False);
                Assert.That(enabledReader.Read(contacts, out _), Is.EqualTo(MotorContactReadStatus.Unsupported));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(other);
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

    #endregion

    }
}
