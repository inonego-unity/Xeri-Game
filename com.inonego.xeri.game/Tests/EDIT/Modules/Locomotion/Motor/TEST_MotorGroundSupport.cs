/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : TEST_MotorGroundSupport.cs
수정일 : 2026-11-21

# 설명
Motor 관측 수명과 IGroundChecker의 선택적 접지·지면 지점 속도 연결을 검증한다.

# 특이사항, 제약사항
Fake Checker로 Point Velocity 재사용·Correction·Checker Owner 변경과 재사용 방지를 검증한다.

# 테스트 구성
 O: 표본 수명과 Owner (Observation)
 R: 재진입 거절 (Reentrancy)
========================================================================= BLOCK_HEADER_END */

using System;

using UnityEngine;

using NUnit;
using NUnit.Framework;

using inonego;
using inonego.Xeri;
using inonego.Xeri.Game;
using inonego.Xeri.Game.Controller;
using inonego.Xeri.Game.Motor;
using inonego.Xeri.Game.Motor.Adapter;
using inonego.Xeri.Game.Motion;

namespace inonego.Xeri.Game.TEST.EDIT._Motor
{
    // ============================================================
    /// <summary>
    /// GroundChecker의 재사용·관측 수명과 Groundless 지원을 검증합니다.
    /// </summary>
    // ============================================================
    public sealed class TEST_MotorGroundSupport
    {

    #region 헬퍼

        private static readonly MotionActorKey actor = new(1, 1, 1);

        private sealed class CheckFixture : IGroundChecker
        {
            internal GameObject Owner;
            internal bool NextGrounded;
            internal Vector3 PointVelocity;
            internal int CheckCalls;
            internal int PointCalls;
            internal Action OnCheck;
            internal Action OnOwnerRead;

            // ------------------------------------------------------------
            /// <summary>
            /// 직전 검사에서 승인한 접지 상태입니다.
            /// </summary>
            // ------------------------------------------------------------
            public bool IsOnGround { get; private set; }

            // ------------------------------------------------------------
            /// <summary>
            /// 고정된 테스트 Body 속도입니다.
            /// </summary>
            // ------------------------------------------------------------
            public Vector3 Velocity => new(10f, 20f, 0f);

            // ------------------------------------------------------------
            /// <summary>
            /// Point Velocity와 구별할 선속도입니다.
            /// </summary>
            // ------------------------------------------------------------
            public Vector3 GroundLinearVelocity => new(100f, 100f, 0f);

            // ------------------------------------------------------------
            /// <summary>
            /// Point Velocity와 구별할 각속도입니다.
            /// </summary>
            // ------------------------------------------------------------
            public Vector3 GroundAngularVelocity => new(0f, 0f, 80f);

            // ------------------------------------------------------------
            /// <summary>
            /// 테스트에 사용하는 중력 방향입니다.
            /// </summary>
            // ------------------------------------------------------------
            public Vector3 Gravity => Vector3.down;

            // ------------------------------------------------------------
            /// <summary>
            /// 테스트 Checker에 연결된 오브젝트입니다.
            /// </summary>
            // ------------------------------------------------------------
            public GameObject GameObject
            {
                get
                {
                    OnOwnerRead?.Invoke();
                    return Owner;
                }
            }

            // ------------------------------------------------------------
            /// <summary>
            /// 테스트 접지 진입 이벤트입니다.
            /// </summary>
            // ------------------------------------------------------------
            public event ValueChangeEventHandler<GameObject> OnLand;

            // ------------------------------------------------------------
            /// <summary>
            /// 테스트 접지 이탈 이벤트입니다.
            /// </summary>
            // ------------------------------------------------------------
            public event ValueChangeEventHandler<GameObject> OnLeave;

            // ------------------------------------------------------------
            /// <summary>
            /// 다음 접지 상태로 전이합니다.
            /// </summary>
            // ------------------------------------------------------------
            public void Check(float deltaTime)
            {
                CheckCalls++;
                var wasGrounded = IsOnGround;
                IsOnGround = NextGrounded;

                if (!wasGrounded && IsOnGround)
                {
                    OnLand?.Invoke(this, new(null, Owner));
                }
                else if (wasGrounded && !IsOnGround)
                {
                    OnLeave?.Invoke(this, new(Owner, null));
                }

                OnCheck?.Invoke();
            }

            // ------------------------------------------------------------
            /// <summary>
            /// 미리 지정한 지점 속도를 그대로 반환합니다.
            /// </summary>
            // ------------------------------------------------------------
            public Vector3 GetGroundPointVelocity(Vector3 worldPoint)
            {
                PointCalls++;
                return PointVelocity;
            }
        }

    #endregion

    #region O-1: 표본 수명과 Owner

        // ------------------------------------------------------------
        /// <summary>
        /// Checker 없는 Actor도 유효한 Groundless 표본을 반환합니다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_MotorGroundSupport_Groundless와같은Step재사용무효화()
        {
            var go = new GameObject("TEST_MotorSupportGroundless");

            try
            {
                var body = go.AddComponent<Rigidbody2D>();
                var motor = new Motor<MotorState2D>
                (
                    actor, new RigidbodyMotorBackend2D(body)
                );
                var support = new MotorGroundSupport<MotorState2D>(motor);

                Assert.That(support.TryRead(out _), Is.False);
                motor.BeginPhysicsStep();
                Assert.That(support.CheckAndObserve(1, 0.02f, Vector3.zero), Is.False);
                motor.CompletePhysicsStep(1);
                Assert.That(support.CheckAndObserve(1, 0.02f, Vector3.zero), Is.True);

                Assert.That(support.TryRead(out var sample), Is.True);
                Assert.That(sample.Actor, Is.EqualTo(actor));
                Assert.That(sample.Step, Is.EqualTo(1));
                Assert.That(sample.HasChecker, Is.False);
                Assert.That(sample.IsOnGround, Is.False);
                Assert.That(sample.GroundPointVelocity, Is.EqualTo(Vector3.zero));

                motor.BeginPhysicsStep();
                Assert.That(support.TryRead(out _), Is.False);
                motor.CompletePhysicsStep(1);

                // 같은 Step Index의 반복 실행도 이전 지면 표본을 재사용하지 않는다.
                Assert.That(support.TryRead(out _), Is.False);
                Assert.That(support.CheckAndObserve(1, 0.02f, Vector3.zero), Is.True);

                motor.ApplyCurrentState
                (
                    new MotorState2D(Vector2.zero, 0f, Vector2.zero, 0f)
                );
                Assert.That(support.TryRead(out _), Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 회전 발판의 Point Velocity를 재합산하지 않습니다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_MotorGroundSupport_지점속도직접전달과GroundLeave()
        {
            var go = new GameObject("TEST_MotorSupportGroundChecker");

            try
            {
                var body = go.AddComponent<Rigidbody2D>();
                var motor = new Motor<MotorState2D>
                (
                    actor, new RigidbodyMotorBackend2D(body)
                );
                var checker = new CheckFixture
                {
                    Owner = go,
                    NextGrounded = true,
                    PointVelocity = new Vector3(4f, 6f, 0f)
                };
                var support = new MotorGroundSupport<MotorState2D>
                (
                    motor, checker
                );
                var worldPoint = new Vector3(2f, 3f, 0f);

                motor.BeginPhysicsStep();
                motor.CompletePhysicsStep(2);
                Assert.That(support.CheckAndObserve(2, 0.02f, worldPoint), Is.True);
                Assert.That(support.TryRead(out var landed), Is.True);
                Assert.That(landed.IsOnGround, Is.True);
                Assert.That(landed.HasChecker, Is.True);
                Assert.That(landed.WorldPoint, Is.EqualTo(worldPoint));
                Assert.That(landed.GroundPointVelocity, Is.EqualTo(checker.PointVelocity));
                Assert.That(checker.PointCalls, Is.EqualTo(1));
                Assert.That(checker.CheckCalls, Is.EqualTo(1));

                Assert.That(support.CheckAndObserve(2, 0.02f, worldPoint), Is.True);
                Assert.That(support.CheckAndObserve(2, 0.02f, worldPoint + Vector3.right), Is.False);
                Assert.That(checker.CheckCalls, Is.EqualTo(1));
                Assert.That(checker.PointCalls, Is.EqualTo(1));
                Assert.That(support.TryRead(out var retained), Is.True);
                Assert.That(retained.WorldPoint, Is.EqualTo(worldPoint));

                checker.NextGrounded = false;
                motor.BeginPhysicsStep();
                motor.CompletePhysicsStep(3);
                Assert.That(support.TryRead(out _), Is.False);
                Assert.That(support.CheckAndObserve(3, 0.02f, worldPoint), Is.True);
                Assert.That(support.TryRead(out var leftGround), Is.True);
                Assert.That(leftGround.IsOnGround, Is.False);
                Assert.That(leftGround.GroundPointVelocity, Is.EqualTo(Vector3.zero));
                Assert.That(checker.PointCalls, Is.EqualTo(1));
                Assert.That(checker.CheckCalls, Is.EqualTo(2));

                UnityEngine.Object.DestroyImmediate(go);
                Assert.That(support.TryRead(out _), Is.False);
            }
            finally
            {
                if (go != null)
                {
                    UnityEngine.Object.DestroyImmediate(go);
                }
            }
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Checker Owner 교체 시 이전 Support를 무효화합니다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_MotorGroundSupport_CheckerOwner교체시표본무효화()
        {
            var first = new GameObject("TEST_SupportOwnerFirst");
            var second = new GameObject("TEST_SupportOwnerSecond");

            try
            {
                var body = first.AddComponent<Rigidbody2D>();
                var motor = new Motor<MotorState2D>
                (
                    actor, new RigidbodyMotorBackend2D(body)
                );
                var checker = new CheckFixture
                {
                    Owner = first,
                    NextGrounded = true,
                    PointVelocity = Vector3.right
                };
                var support = new MotorGroundSupport<MotorState2D>
                (
                    motor, checker
                );

                motor.BeginPhysicsStep();
                motor.CompletePhysicsStep(5);
                Assert.That(support.CheckAndObserve(5, 0.02f, Vector3.zero), Is.True);
                Assert.That(support.TryRead(out _), Is.True);

                checker.Owner = second;
                checker.PointVelocity = Vector3.up;

                Assert.That(support.TryRead(out _), Is.False);
                Assert.That(support.CheckAndObserve(5, 0.02f, Vector3.zero), Is.False);
                Assert.That(checker.CheckCalls, Is.EqualTo(1));

                // Owner가 원래 객체로 돌아와도 동일 Step의 옛 Sample은 부활하면 안 된다.
                checker.Owner = first;
                checker.PointVelocity = Vector3.left;
                Assert.That(support.TryRead(out _), Is.False);
                Assert.That(support.CheckAndObserve(5, 0.02f, Vector3.zero), Is.False);
                Assert.That(checker.CheckCalls, Is.EqualTo(1));

                checker.Owner = second;
                checker.PointVelocity = Vector3.up;
                motor.BeginPhysicsStep();
                motor.CompletePhysicsStep(6);
                Assert.That(support.CheckAndObserve(6, 0.02f, Vector3.zero), Is.True);
                Assert.That(support.TryRead(out var latest), Is.True);
                Assert.That(latest.GroundPointVelocity, Is.EqualTo(Vector3.up));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(second);
                UnityEngine.Object.DestroyImmediate(first);
            }
        }

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> Check 중 파괴된 Owner의 표본은 확정하지 않습니다.
        /// <br/> 다음 Step은 Groundless 표본으로 관측합니다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_MotorGroundSupport_Check중Owner파괴시표본거절()
        {
            var go = new GameObject("TEST_MotorSupportOwnerDestroyBody");
            var owner = new GameObject("TEST_MotorSupportOwnerDestroyOwner");

            try
            {
                var body = go.AddComponent<Rigidbody2D>();
                var motor = new Motor<MotorState2D>
                (
                    actor, new RigidbodyMotorBackend2D(body)
                );
                var checker = new CheckFixture
                {
                    Owner = owner,
                    NextGrounded = true,
                    PointVelocity = Vector3.right
                };
                var support = new MotorGroundSupport<MotorState2D>
                (
                    motor, checker
                );

                // 실제 GroundChecker처럼 파괴된 Owner의 관리 참조를 계속 반환한다.
                checker.OnCheck = () => UnityEngine.Object.DestroyImmediate(owner);

                motor.BeginPhysicsStep();
                motor.CompletePhysicsStep(7);
                Assert.That(support.CheckAndObserve(7, 0.02f, Vector3.zero), Is.False);
                Assert.That(support.TryRead(out _), Is.False);
                Assert.That(support.HasChecker, Is.False);

                checker.OnCheck = null;
                motor.BeginPhysicsStep();
                motor.CompletePhysicsStep(8);
                Assert.That(support.CheckAndObserve(8, 0.02f, Vector3.zero), Is.True);
                Assert.That(support.TryRead(out var sample), Is.True);
                Assert.That(sample.HasChecker, Is.False);
                Assert.That(sample.IsOnGround, Is.False);
                Assert.That(checker.CheckCalls, Is.EqualTo(1));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);

                if (owner != null)
                {
                    UnityEngine.Object.DestroyImmediate(owner);
                }
            }
        }

    #endregion

    #region R-1: 재진입 거절

        // ------------------------------------------------------------
        /// <summary>
        /// Checker 이벤트의 중첩 관측은 재진입으로 거절합니다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_MotorGroundSupport_Check중재진입거절()
        {
            var go = new GameObject("TEST_MotorSupportReentrantCheck");

            try
            {
                var body = go.AddComponent<Rigidbody2D>();
                var motor = new Motor<MotorState2D>
                (
                    actor, new RigidbodyMotorBackend2D(body)
                );
                var checker = new CheckFixture
                {
                    Owner = go,
                    NextGrounded = true,
                    PointVelocity = Vector3.right
                };
                var support = new MotorGroundSupport<MotorState2D>
                (
                    motor, checker
                );
                var nestedAttempted = false;
                var nestedRejected = false;

                checker.OnCheck = () =>
                {
                    if (nestedAttempted)
                    {
                        return;
                    }

                    nestedAttempted = true;

                    try
                    {
                        support.CheckAndObserve(2, 0.02f, Vector3.zero);
                    }
                    catch (InvalidOperationException)
                    {
                        nestedRejected = true;
                    }
                };

                motor.BeginPhysicsStep();
                motor.CompletePhysicsStep(2);
                Assert.That(support.CheckAndObserve(2, 0.02f, Vector3.zero), Is.True);
                Assert.That(nestedAttempted, Is.True);
                Assert.That(nestedRejected, Is.True);
                Assert.That(checker.CheckCalls, Is.EqualTo(1));
                Assert.That(checker.PointCalls, Is.EqualTo(1));
                Assert.That(support.TryRead(out var sample), Is.True);
                Assert.That(sample.GroundPointVelocity, Is.EqualTo(Vector3.right));

                checker.OnCheck = null;
                motor.BeginPhysicsStep();
                motor.CompletePhysicsStep(3);
                Assert.That(support.CheckAndObserve(3, 0.02f, Vector3.zero), Is.True);
                Assert.That(checker.CheckCalls, Is.EqualTo(2));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> 캐시 소유권 조회 콜백의 Correction은 재진입으로 거절합니다.
        /// <br/> 기존 표본은 그대로 유효합니다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_MotorGroundSupport_캐시검증중Correction재진입거절()
        {
            var go = new GameObject("TEST_SupportCachedOwnerCorrection");

            try
            {
                var body = go.AddComponent<Rigidbody2D>();
                var motor = new Motor<MotorState2D>
                (
                    actor, new RigidbodyMotorBackend2D(body)
                );
                var checker = new CheckFixture
                {
                    Owner = go,
                    NextGrounded = true,
                    PointVelocity = Vector3.right
                };
                var support = new MotorGroundSupport<MotorState2D>
                (
                    motor, checker
                );

                motor.BeginPhysicsStep();
                motor.CompletePhysicsStep(11);
                Assert.That(support.CheckAndObserve(11, 0.02f, Vector3.zero), Is.True);

                var corrected = false;
                checker.OnOwnerRead = () =>
                {
                    if (corrected)
                    {
                        return;
                    }

                    corrected = true;
                    motor.ApplyCurrentState(default);
                };

                Assert.Throws<InvalidOperationException>
                (
                    () => support.CheckAndObserve(11, 0.02f, Vector3.zero)
                );
                Assert.That(corrected, Is.True);
                Assert.That(motor.ObservedPhysicsStep, Is.EqualTo(11));
                Assert.That(support.TryRead(out _), Is.True);

                checker.OnOwnerRead = null;
                motor.BeginPhysicsStep();
                motor.CompletePhysicsStep(12);
                Assert.That(support.CheckAndObserve(12, 0.02f, Vector3.zero), Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 초기 Checker 소유권 조회 중 재진입은 중복 검사를 막습니다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_MotorGroundSupport_초기소유권조회중재진입거절()
        {
            var go = new GameObject("TEST_SupportInitialOwnerReentry");

            try
            {
                var body = go.AddComponent<Rigidbody2D>();
                var motor = new Motor<MotorState2D>
                (
                    actor, new RigidbodyMotorBackend2D(body)
                );
                var checker = new CheckFixture
                {
                    Owner = go,
                    NextGrounded = true,
                    PointVelocity = Vector3.up
                };
                var support = new MotorGroundSupport<MotorState2D>
                (
                    motor, checker
                );
                var attempted = false;
                var rejected = false;
                checker.OnOwnerRead = () =>
                {
                    if (attempted)
                    {
                        return;
                    }

                    attempted = true;

                    try
                    {
                        support.CheckAndObserve(13, 0.02f, Vector3.zero);
                    }
                    catch (InvalidOperationException)
                    {
                        rejected = true;
                    }
                };

                motor.BeginPhysicsStep();
                motor.CompletePhysicsStep(13);
                Assert.That(support.CheckAndObserve(13, 0.02f, Vector3.zero), Is.True);
                Assert.That(attempted, Is.True);
                Assert.That(rejected, Is.True);
                Assert.That(checker.CheckCalls, Is.EqualTo(1));
                Assert.That(checker.PointCalls, Is.EqualTo(1));
                Assert.That(support.TryRead(out var sample), Is.True);
                Assert.That(sample.GroundPointVelocity, Is.EqualTo(Vector3.up));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 마지막 소유권 확인 중 재진입은 중복 검사를 막습니다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_MotorGroundSupport_확정소유권조회중재진입거절()
        {
            var go = new GameObject("TEST_SupportFinalOwnerReentry");

            try
            {
                var body = go.AddComponent<Rigidbody2D>();
                var motor = new Motor<MotorState2D>
                (
                    actor, new RigidbodyMotorBackend2D(body)
                );
                var checker = new CheckFixture
                {
                    Owner = go,
                    NextGrounded = true,
                    PointVelocity = Vector3.right
                };
                var support = new MotorGroundSupport<MotorState2D>
                (
                    motor, checker
                );
                var attempted = false;
                var rejected = false;
                checker.OnCheck = () =>
                {
                    checker.OnOwnerRead = () =>
                    {
                        if (attempted)
                        {
                            return;
                        }

                        attempted = true;

                        try
                        {
                            support.CheckAndObserve(14, 0.02f, Vector3.zero);
                        }
                        catch (InvalidOperationException)
                        {
                            rejected = true;
                        }
                    };
                };

                motor.BeginPhysicsStep();
                motor.CompletePhysicsStep(14);
                Assert.That(support.CheckAndObserve(14, 0.02f, Vector3.zero), Is.True);
                Assert.That(attempted, Is.True);
                Assert.That(rejected, Is.True);
                Assert.That(checker.CheckCalls, Is.EqualTo(1));
                Assert.That(checker.PointCalls, Is.EqualTo(1));
                Assert.That(support.TryRead(out var sample), Is.True);
                Assert.That(sample.GroundPointVelocity, Is.EqualTo(Vector3.right));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        // ------------------------------------------------------------
        /// <summary>
        /// TryRead 소유자 조회 콜백의 Correction은 재진입으로 거절합니다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_MotorGroundSupport_소유자조회중Correction재진입거절()
        {
            var go = new GameObject("TEST_MotorSupportOwnerReadCorrection");

            try
            {
                var body = go.AddComponent<Rigidbody2D>();
                var motor = new Motor<MotorState2D>
                (
                    actor, new RigidbodyMotorBackend2D(body)
                );
                var checker = new CheckFixture
                {
                    Owner = go,
                    NextGrounded = true,
                    PointVelocity = Vector3.right
                };
                var support = new MotorGroundSupport<MotorState2D>
                (
                    motor, checker
                );

                motor.BeginPhysicsStep();
                motor.CompletePhysicsStep(3);
                Assert.That(support.CheckAndObserve(3, 0.02f, Vector3.zero), Is.True);
                Assert.That(support.TryRead(out _), Is.True);

                var corrected = false;
                checker.OnOwnerRead = () =>
                {
                    if (corrected)
                    {
                        return;
                    }

                    corrected = true;
                    motor.ApplyCurrentState
                    (
                        new MotorState2D
                        (
                            new Vector2(4f, 5f), 0f, Vector2.zero, 0f
                        )
                    );
                };

                Assert.Throws<InvalidOperationException>(() => support.TryRead(out _));
                Assert.That(corrected, Is.True);
                Assert.That(motor.ObservedPhysicsStep, Is.EqualTo(3));
                Assert.That(support.TryRead(out _), Is.True);

                checker.OnOwnerRead = null;
                motor.BeginPhysicsStep();
                motor.CompletePhysicsStep(4);
                Assert.That(support.CheckAndObserve(4, 0.02f, Vector3.zero), Is.True);
                Assert.That(support.TryRead(out var current), Is.True);
                Assert.That(current.Step, Is.EqualTo(4));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 확정 직전 소유자 확인 콜백의 Correction은 재진입으로 거절합니다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_MotorGroundSupport_소유자확인중Correction재진입거절()
        {
            var go = new GameObject("TEST_SupportCommitCorrection");

            try
            {
                var body = go.AddComponent<Rigidbody2D>();
                var motor = new Motor<MotorState2D>
                (
                    actor, new RigidbodyMotorBackend2D(body)
                );
                var checker = new CheckFixture
                {
                    Owner = go,
                    NextGrounded = true,
                    PointVelocity = Vector3.right
                };
                var support = new MotorGroundSupport<MotorState2D>
                (
                    motor, checker
                );
                var corrected = false;
                checker.OnCheck = () =>
                {
                    checker.OnOwnerRead = () =>
                    {
                        if (corrected)
                        {
                            return;
                        }

                        corrected = true;
                        motor.ApplyCurrentState
                        (
                            new MotorState2D
                            (
                                new Vector2(1f, 2f), 0f, Vector2.zero, 0f
                            )
                        );
                    };
                };

                motor.BeginPhysicsStep();
                motor.CompletePhysicsStep(5);
                Assert.Throws<InvalidOperationException>
                (
                    () => support.CheckAndObserve(5, 0.02f, Vector3.zero)
                );
                Assert.That(corrected, Is.True);
                Assert.That(support.TryRead(out _), Is.False);
                Assert.That(motor.ObservedPhysicsStep, Is.EqualTo(5));
                Assert.That(motor.DiscontinuityVersion, Is.Zero);

                checker.OnCheck = null;
                checker.OnOwnerRead = null;
                motor.BeginPhysicsStep();
                motor.CompletePhysicsStep(6);
                Assert.That(support.CheckAndObserve(6, 0.02f, Vector3.zero), Is.True);
                Assert.That(support.TryRead(out var current), Is.True);
                Assert.That(current.Step, Is.EqualTo(6));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Checker 이벤트 중 Correction은 재진입으로 거절합니다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_MotorGroundSupport_Check중Correction재진입거절()
        {
            var go = new GameObject("TEST_MotorSupportCorrection");

            try
            {
                var body = go.AddComponent<Rigidbody2D>();
                var motor = new Motor<MotorState2D>
                (
                    actor, new RigidbodyMotorBackend2D(body)
                );
                var checker = new CheckFixture
                {
                    Owner = go,
                    NextGrounded = true,
                    PointVelocity = Vector3.right
                };
                var support = new MotorGroundSupport<MotorState2D>
                (
                    motor, checker
                );

                motor.BeginPhysicsStep();
                motor.CompletePhysicsStep(4);
                checker.OnCheck = () => motor.ApplyCurrentState
                (
                    new MotorState2D(Vector2.zero, 0f, Vector2.zero, 0f)
                );

                Assert.Throws<InvalidOperationException>
                (
                    () => support.CheckAndObserve(4, 0.02f, Vector3.zero)
                );
                Assert.That(support.TryRead(out _), Is.False);
                Assert.That(motor.ObservedPhysicsStep, Is.EqualTo(4));
                Assert.That(motor.DiscontinuityVersion, Is.Zero);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

    #endregion

    }
}