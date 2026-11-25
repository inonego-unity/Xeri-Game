/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : TEST_MotorGroundSupport.cs
수정일 : 2026-11-21

# 설명
실제 GroundChecker2D/3D에서 지면 속도·Correction·2D 이동 지면 A/B 전환을 검증한다.

# 특이사항, 제약사항
Physics Step은 테스트 Host가 수동 실행한다. 테스트 종료 시 전역 SimulationMode를 복원한다.

# 테스트 구성
 P: 이동·회전 지면의 지점 속도 (Point velocity)
 S: 지면 전환 (Switch)
========================================================================= BLOCK_HEADER_END */

using System;
using System.Collections;

using UnityEngine;
using UnityEngine.TestTools;

using NUnit;
using NUnit.Framework;

using inonego;
using inonego.Xeri;
using inonego.Xeri.Game;
using inonego.Xeri.Game.Controller;
using inonego.Xeri.Game.Motor;
using inonego.Xeri.Game.Motor.Adapter;
using inonego.Xeri.Game.Motion;

namespace inonego.Xeri.Game.TEST.Motor._GroundSupport
{
    // ============================================================
    /// <summary>
    /// <br/> 실제 2D·3D 지면의 속도를 Motor의 현재 Step에 연결합니다.
    /// <br/> Physics World와 Game별 이동 정책은 생성하지 않습니다.
    /// </summary>
    // ============================================================
    public sealed class TEST_MotorGroundSupport
    {

    #region P-1: 2D 지점 속도

        // ------------------------------------------------------------
        /// <summary>
        /// 2D 회전 바닥의 속도를 중복 계산 없이 전달합니다.
        /// </summary>
        // ------------------------------------------------------------
        [UnityTest]
        public IEnumerator TEST_MotorGroundSupport_2D이동회전지면의지점속도()
        {
            var previousMode = Physics2D.simulationMode;
            GameObject ground = null;
            GameObject player = null;
            GroundChecker2D checker = null;

            try
            {
                Physics2D.simulationMode = SimulationMode2D.Script;

                ground = new GameObject("TEST_SupportMoving2D");
                ground.layer = 8;
                ground.transform.position = new Vector3(0f, -0.5f, 0f);
                ground.AddComponent<BoxCollider2D>().size = new Vector2(8f, 1f);
                var groundBody = ground.AddComponent<Rigidbody2D>();
                groundBody.bodyType = RigidbodyType2D.Dynamic;
                groundBody.gravityScale = 0f;
                groundBody.mass = 1000f;
                groundBody.linearDamping = 0f;
                groundBody.angularDamping = 0f;
                groundBody.linearVelocity = new Vector2(0.3f, 0f);
                groundBody.angularVelocity = 15f;

                player = new GameObject("TEST_SupportPlayer2D");
                player.transform.position = new Vector3(0f, 0.55f, 0f);
                var playerBody = player.AddComponent<Rigidbody2D>();
                playerBody.bodyType = RigidbodyType2D.Kinematic;
                playerBody.linearVelocity = Vector2.down;
                player.AddComponent<CircleCollider2D>().radius = 0.5f;

                checker = new GroundChecker2D
                {
                    Config = new GroundCheckerConfig
                    {
                        Layer = 1 << 8,
                        Depth = 0.25f
                    }
                };
                checker.Init(player);

                var actor = new MotionActorKey(112, 1, Revision.FromUInt64(1));
                var motor = new Motor<MotorState2D>
                (
                    actor, new RigidbodyMotorBackend2D(playerBody)
                );
                var support = new MotorGroundSupport<MotorState2D>
                (
                    motor, checker
                );
                var worldPoint = new Vector3(1f, 0f, 0f);

                Physics2D.SyncTransforms();
                motor.BeginPhysicsStep();
                Physics2D.Simulate(0.02f);
                motor.CompletePhysicsStep(0);

                Assert.That(support.CheckAndObserve(0, 0.02f, worldPoint), Is.True);
                Assert.That(support.TryRead(out var observed), Is.True);
                Assert.That(observed.HasChecker, Is.True);
                Assert.That(observed.IsOnGround, Is.True);

                var expectedVelocity = groundBody.GetPointVelocity(worldPoint);
                Assert.That(expectedVelocity.sqrMagnitude, Is.GreaterThan(0.01f));
                Assert.That(Vector2.Distance(observed.GroundPointVelocity, expectedVelocity), Is.LessThan(0.001f));

                motor.ApplyCurrentState
                (
                    new MotorState2D
                    (
                        new Vector2(0f, 5f), 0f,
                        Vector2.zero, 0f
                    )
                );

                Assert.That(support.TryRead(out _), Is.False);
                motor.BeginPhysicsStep();
                Physics2D.SyncTransforms();
                Physics2D.Simulate(0.02f);
                motor.CompletePhysicsStep(1);
                Assert.That(support.CheckAndObserve(1, 0.02f, worldPoint), Is.True);
                Assert.That(support.TryRead(out var departed), Is.True);
                Assert.That(departed.IsOnGround, Is.False);
                Assert.That(departed.GroundPointVelocity, Is.EqualTo(Vector3.zero));
            }
            finally
            {
                checker?.Release();

                if (player != null)
                {
                    UnityEngine.Object.DestroyImmediate(player);
                }

                if (ground != null)
                {
                    UnityEngine.Object.DestroyImmediate(ground);
                }

                Physics2D.simulationMode = previousMode;
            }

            yield return null;
        }

    #endregion

    #region P-2: 3D 지점 속도

        // ------------------------------------------------------------
        /// <summary>
        /// 회전·병진 지면의 Point Velocity를 그대로 전달합니다.
        /// </summary>
        // ------------------------------------------------------------
        [UnityTest]
        public IEnumerator TEST_MotorGroundSupport_3D이동회전지면의지점속도()
        {
            var previousMode = Physics.simulationMode;
            GameObject ground = null;
            GameObject player = null;
            GroundChecker3D checker = null;

            try
            {
                Physics.simulationMode = SimulationMode.Script;

                ground = new GameObject("TEST_SupportMoving3D");
                ground.layer = 8;
                ground.transform.position = new Vector3(0f, -0.5f, 0f);
                ground.AddComponent<BoxCollider>().size = new Vector3(8f, 1f, 8f);
                var groundBody = ground.AddComponent<Rigidbody>();
                groundBody.useGravity = false;
                groundBody.mass = 1000f;
                groundBody.linearDamping = 0f;
                groundBody.angularDamping = 0f;
                groundBody.linearVelocity = new Vector3(1f, 0f, 0f);
                groundBody.angularVelocity = new Vector3(0f, 0.5f, 0f);

                player = new GameObject("TEST_SupportPlayer3D");
                player.transform.position = new Vector3(0f, 0.55f, 0f);
                var playerBody = player.AddComponent<Rigidbody>();
                playerBody.isKinematic = true;
                player.AddComponent<SphereCollider>().radius = 0.5f;

                checker = new GroundChecker3D
                {
                    Config = new GroundCheckerConfig
                    {
                        Layer = 1 << 8,
                        Depth = 0.25f
                    }
                };
                checker.Init(player);

                var actor = new MotionActorKey(111, 1, Revision.FromUInt64(1));
                var motor = new Motor<MotorState3D>
                (
                    actor, new RigidbodyMotorBackend3D(playerBody)
                );
                var support = new MotorGroundSupport<MotorState3D>
                (
                    motor, checker
                );
                var worldPoint = new Vector3(1f, 0f, 0f);

                Physics.SyncTransforms();
                motor.BeginPhysicsStep();
                Physics.Simulate(0.02f);
                motor.CompletePhysicsStep(0);

                Assert.That(support.CheckAndObserve(0, 0.02f, worldPoint), Is.True);
                Assert.That(support.TryRead(out var observed), Is.True);
                Assert.That(observed.HasChecker, Is.True);
                Assert.That(observed.IsOnGround, Is.True);

                var expectedVelocity = groundBody.GetPointVelocity(worldPoint);
                Assert.That(expectedVelocity.sqrMagnitude, Is.GreaterThan(0.01f));
                Assert.That(Vector3.Distance(observed.GroundPointVelocity, expectedVelocity), Is.LessThan(0.001f));
                Assert.That(observed.Actor, Is.EqualTo(actor));

                motor.ApplyCurrentState
                (
                    new MotorState3D
                    (
                        new Vector3(0f, 5f, 0f), Quaternion.identity,
                        Vector3.zero, Vector3.zero
                    )
                );
                Assert.That(support.TryRead(out _), Is.False);

                motor.BeginPhysicsStep();
                Physics.SyncTransforms();
                Physics.Simulate(0.02f);
                motor.CompletePhysicsStep(1);
                Assert.That(support.CheckAndObserve(1, 0.02f, worldPoint), Is.True);
                Assert.That(support.TryRead(out var departed), Is.True);
                Assert.That(departed.IsOnGround, Is.False);
                Assert.That(departed.GroundPointVelocity, Is.EqualTo(Vector3.zero));
            }
            finally
            {
                checker?.Release();

                if (player != null)
                {
                    UnityEngine.Object.DestroyImmediate(player);
                }

                if (ground != null)
                {
                    UnityEngine.Object.DestroyImmediate(ground);
                }

                Physics.simulationMode = previousMode;
            }

            yield return null;
        }

    #endregion

    #region S-1: 2D 지면 전환

        // ------------------------------------------------------------
        /// <summary>
        /// 실제 2D 바닥 교체 후 이전 지면 속도를 재사용하지 않습니다.
        /// </summary>
        // ------------------------------------------------------------
        [UnityTest]
        public IEnumerator TEST_MotorGroundSupport_2D서로다른지면전환()
        {
            var previousMode = Physics2D.simulationMode;
            GameObject first = null;
            GameObject second = null;
            GameObject player = null;
            GroundChecker2D checker = null;

            try
            {
                Physics2D.simulationMode = SimulationMode2D.Script;

                first = new GameObject("TEST_GroundSwitchFirst");
                first.layer = 8;
                first.transform.position = new Vector3(0f, -0.5f, 0f);
                first.AddComponent<BoxCollider2D>().size = new Vector2(8f, 1f);
                var firstBody = first.AddComponent<Rigidbody2D>();
                firstBody.bodyType = RigidbodyType2D.Dynamic;
                firstBody.gravityScale = 0f;
                firstBody.mass = 1000f;
                firstBody.linearVelocity = new Vector2(0.3f, 0f);

                second = new GameObject("TEST_GroundSwitchSecond");
                second.layer = 8;
                second.transform.position = new Vector3(20f, -0.5f, 0f);
                second.AddComponent<BoxCollider2D>().size = new Vector2(8f, 1f);
                var secondBody = second.AddComponent<Rigidbody2D>();
                secondBody.bodyType = RigidbodyType2D.Dynamic;
                secondBody.gravityScale = 0f;
                secondBody.mass = 1000f;
                secondBody.linearVelocity = new Vector2(-0.5f, 0f);

                player = new GameObject("TEST_GroundSwitchActor");
                player.transform.position = new Vector3(0f, 0.55f, 0f);
                var playerBody = player.AddComponent<Rigidbody2D>();
                playerBody.bodyType = RigidbodyType2D.Kinematic;
                playerBody.linearVelocity = Vector2.down;
                player.AddComponent<CircleCollider2D>().radius = 0.5f;

                checker = new GroundChecker2D
                {
                    Config = new GroundCheckerConfig
                    {
                        Layer = 1 << 8,
                        Depth = 0.25f
                    }
                };
                checker.Init(player);

                var motor = new Motor<MotorState2D>
                (
                    new MotionActorKey(113, 1, Revision.FromUInt64(1)),
                    new RigidbodyMotorBackend2D(playerBody)
                );
                var support = new MotorGroundSupport<MotorState2D>
                (
                    motor, checker
                );

                Physics2D.SyncTransforms();
                motor.BeginPhysicsStep();
                Physics2D.Simulate(0.02f);
                motor.CompletePhysicsStep(0);

                Assert.That(support.CheckAndObserve(0, 0.02f, Vector3.zero), Is.True);
                Assert.That(support.TryRead(out var firstSample), Is.True);
                Assert.That(firstSample.IsOnGround, Is.True);
                var firstError = Vector2.Distance
                (
                    firstSample.GroundPointVelocity,
                    firstBody.GetPointVelocity(Vector2.zero)
                );
                Assert.That(firstError, Is.LessThan(0.001f));

                // 새 물리 Step에서는 기존 지면을 치우고 다른 Rigidbody 지면을 배치한다.
                firstBody.position = new Vector2(20f, -0.5f);
                secondBody.position = new Vector2(0f, -0.5f);
                Physics2D.SyncTransforms();
                motor.BeginPhysicsStep();
                Assert.That(support.TryRead(out _), Is.False);
                Physics2D.Simulate(0.02f);
                motor.CompletePhysicsStep(1);

                Assert.That(support.CheckAndObserve(1, 0.02f, Vector3.zero), Is.True);
                Assert.That(support.TryRead(out var secondSample), Is.True);
                Assert.That(secondSample.IsOnGround, Is.True);
                var secondError = Vector2.Distance
                (
                    secondSample.GroundPointVelocity,
                    secondBody.GetPointVelocity(Vector2.zero)
                );
                Assert.That(secondError, Is.LessThan(0.001f));
                var transition = Vector3.Distance
                (
                    firstSample.GroundPointVelocity,
                    secondSample.GroundPointVelocity
                );
                Assert.That(transition, Is.GreaterThan(0.2f));
            }
            finally
            {
                checker?.Release();

                if (player != null)
                {
                    UnityEngine.Object.DestroyImmediate(player);
                }

                if (first != null)
                {
                    UnityEngine.Object.DestroyImmediate(first);
                }

                if (second != null)
                {
                    UnityEngine.Object.DestroyImmediate(second);
                }

                Physics2D.simulationMode = previousMode;
            }

            yield return null;
        }

    #endregion

    }
}