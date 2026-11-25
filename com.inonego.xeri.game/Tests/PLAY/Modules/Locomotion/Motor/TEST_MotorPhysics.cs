/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : TEST_MotorPhysics.cs
수정일 : 2026-11-21

# 설명
실제 Unity Physics2D/Physics3D Step으로 Motor Contact·Blocking·Slide 관측과 Correction 무효화를 검증한다.

# 특이사항, 제약사항
수동 Physics Simulation과 Unity 자동 FixedUpdate 관측을 검증한다. 전역 SimulationMode를 복원한다.

# 테스트 구성
 C: 접촉 읽기와 Correction 무효화 (Contact)
 H: 자동 Physics Step의 관측 경계 (Host)
 A: 다중 Actor 관측 격리 (Actor)
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
using inonego.Xeri.Game.Motor;
using inonego.Xeri.Game.Motion;

namespace inonego.Xeri.Game.TEST.Motor._Physics
{
    // ============================================================
    /// <summary>
    /// 실제 Physics Step 뒤 Body 접촉과 Blocking/Slide를 검증합니다.
    /// </summary>
    // ============================================================
    public sealed class TEST_MotorPhysics
    {

    #region 헬퍼

        private static MotionFrame StepIntent(MotionModel model, Vector3 velocity)
        {
            // UnityTest 반복기 안에서는 ref struct 입력 묶음을 만들 수 없어 이 도우미에서 만든다.
            var intents = new[]
            {
                new MotionIntent(MotionChannel.Planar, velocity)
            };

            return model.StepLocal(0.02d, new MotionStepInput(intents));
        }

    #endregion

    #region C-1: 2D 접촉과 Correction

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> 2D 낙하가 바닥과 충돌한 뒤 접촉·속도를 관측합니다.
        /// <br/> 원격 Correction 직후에는 이전 접촉을 읽지 않습니다.
        /// </summary>
        // ------------------------------------------------------------
        [UnityTest]
        public IEnumerator TEST_MotorPhysics_2D접촉과Correction무효화()
        {
            var previousMode = Physics2D.simulationMode;
            GameObject ground = null;
            GameObject actorObject = null;

            try
            {
                Physics2D.simulationMode = SimulationMode2D.Script;

                ground = new GameObject("TEST_MotorGround2D");
                var groundCollider = ground.AddComponent<BoxCollider2D>();
                groundCollider.size = new Vector2(10f, 1f);
                ground.transform.position = new Vector3(0f, -0.5f, 0f);

                actorObject = new GameObject("TEST_MotorActor2D");
                actorObject.transform.position = new Vector3(0f, 2f, 0f);
                var rigidbody = actorObject.AddComponent<Rigidbody2D>();
                rigidbody.gravityScale = 2f;
                actorObject.AddComponent<CircleCollider2D>().radius = 0.5f;

                var actor = new MotionActorKey(31, 1, Revision.FromUInt64(1));
                var motor = new Motor<MotorState2D>
                (
                    actor, new RigidbodyMotorBackend2D(rigidbody)
                );
                var contacts = new ContactPoint2D[16];
                var reader = new MotorContactReader<MotorState2D, ContactPoint2D>(motor);

                Assert.That(reader.IsSupported, Is.True);
                Assert.That(reader.Read(contacts, out _), Is.EqualTo(MotorContactReadStatus.NotObserved));

                var collided = false;

                Physics2D.SyncTransforms();

                // 실제 World Step은 테스트 Host가 수행한다.
                for (var step = 0; step < 120; step++)
                {
                    motor.BeginPhysicsStep();
                    Assert.That(reader.Read(contacts, out _), Is.EqualTo(MotorContactReadStatus.NotObserved));
                    Physics2D.Simulate(0.02f);
                    motor.CompletePhysicsStep(step);

                    Assert.That(reader.Read(contacts, out var count), Is.EqualTo(MotorContactReadStatus.Ready));

                    if (count == 0)
                    {
                        continue;
                    }

                    collided = true;
                    var feedback = MotorContactFeedbackEvaluator.Evaluate
                    (
                        Vector2.down * 4f, motor.ReadCurrentState().LinearVelocity,
                        contacts, count
                    );

                    Assert.That(feedback.IsBlocked, Is.True);
                    Assert.That(feedback.BlockingNormal.y, Is.GreaterThan(0.7f));
                    Assert.That(motor.ObservedPhysicsStep, Is.EqualTo(step));
                    break;
                }

                Assert.That(collided, Is.True, "Physics2D 접촉을 관측하지 못했습니다.");

                motor.ApplyCurrentState
                (
                    new MotorState2D
                    (
                        new Vector2(0f, 5f), 0f,
                        Vector2.zero, 0f
                    )
                );

                Assert.That(reader.Read(contacts, out _), Is.EqualTo(MotorContactReadStatus.NotObserved));
                Assert.That(motor.ObservedPhysicsStep, Is.EqualTo(-1));

                motor.BeginPhysicsStep();
                Physics2D.Simulate(0.02f);
                motor.CompletePhysicsStep(121);

                Assert.That(reader.Read(contacts, out var afterCorrection), Is.EqualTo(MotorContactReadStatus.Ready));
                Assert.That(afterCorrection, Is.Zero);
            }
            finally
            {
                if (actorObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(actorObject);
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

    #region C-2: 3D 접촉과 Correction

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> 3D 벽의 실제 접촉과 Slide를 관측합니다.
        /// <br/> Correction 뒤 센서 표본은 무효화합니다.
        /// </summary>
        // ------------------------------------------------------------
        [UnityTest]
        public IEnumerator TEST_MotorPhysics_3D접촉차단Slide와Correction()
        {
            var previousMode = Physics.simulationMode;
            GameObject wall = null;
            GameObject actorObject = null;
            PhysicsMaterial frictionless = null;

            try
            {
                Physics.simulationMode = SimulationMode.Script;

                // 기본 Physics Material의 마찰이 접선 이동을 감쇠시키지 않게 한다.
                frictionless = new PhysicsMaterial("TEST_MotorNoFriction")
                {
                    dynamicFriction = 0f,
                    staticFriction = 0f,
                    frictionCombine = PhysicsMaterialCombine.Minimum,
                    bounciness = 0f
                };

                wall = new GameObject("TEST_MotorWall3D");
                wall.transform.position = new Vector3(1f, 0f, 0f);
                var wallCollider = wall.AddComponent<BoxCollider>();
                wallCollider.size = new Vector3(1f, 10f, 20f);
                wallCollider.sharedMaterial = frictionless;

                actorObject = new GameObject("TEST_MotorActor3D");
                actorObject.transform.position = new Vector3(-1f, 0f, 0f);
                var rigidbody = actorObject.AddComponent<Rigidbody>();
                rigidbody.useGravity = false;
                rigidbody.linearDamping = 0f;
                rigidbody.angularDamping = 0f;
                rigidbody.constraints = RigidbodyConstraints.FreezeRotation;
                var sphere = actorObject.AddComponent<SphereCollider>();
                sphere.radius = 0.5f;
                sphere.sharedMaterial = frictionless;
                var sensor = actorObject.AddComponent<MotorCollisionProbe3D>();

                var actor = new MotionActorKey(32, 1, Revision.FromUInt64(1));
                var backend = new RigidbodyMotorBackend3D(rigidbody, sensor);
                var motor = new Motor<MotorState3D>(actor, backend);
                var contacts = new ContactPoint[32];
                var reader = new MotorContactReader<MotorState3D, ContactPoint>(motor);

                Assert.That(reader.IsSupported, Is.True);
                Assert.That(reader.Read(contacts, out _), Is.EqualTo(MotorContactReadStatus.NotObserved));

                var requestedVelocity = new Vector3(4f, 0f, 2f);
                var model = new MotionModel(actor, Vector3.up);
                var frame = StepIntent(model, requestedVelocity);
                Assert.That(motor.TryApply(frame), Is.True);

                Physics.SyncTransforms();
                var collided = false;

                for (var step = 0; step < 100; step++)
                {
                    motor.BeginPhysicsStep();
                    Physics.Simulate(0.02f);
                    motor.CompletePhysicsStep(step);

                    Assert.That(reader.Read(contacts, out var count), Is.EqualTo(MotorContactReadStatus.Ready));

                    if (count == 0)
                    {
                        continue;
                    }

                    var feedback = MotorContactFeedbackEvaluator.Evaluate
                    (
                        requestedVelocity, motor.ReadCurrentState().LinearVelocity,
                        contacts, count
                    );

                    if (!feedback.IsBlocked)
                    {
                        continue;
                    }

                    collided = true;
                    Assert.That(feedback.BlockingNormal.x, Is.LessThan(-0.6f));
                    Assert.That(feedback.SlideVelocity.z, Is.GreaterThan(0.2f));
                    Assert.That(feedback.SlideVelocity.x, Is.LessThan(0.5f));
                    Assert.That(sensor.ContactCount, Is.GreaterThan(0));
                    break;
                }

                Assert.That(collided, Is.True, "Physics3D Blocking/Slide 관측을 얻지 못했습니다.");

                motor.ApplyCurrentState
                (
                    new MotorState3D
                    (
                        new Vector3(-5f, 0f, 0f), Quaternion.identity,
                        Vector3.zero, Vector3.zero
                    )
                );

                Assert.That(sensor.ContactCount, Is.Zero);
                Assert.That(reader.Read(contacts, out _), Is.EqualTo(MotorContactReadStatus.NotObserved));
                Assert.That(motor.ObservedPhysicsStep, Is.EqualTo(-1));

                motor.BeginPhysicsStep();
                Physics.SyncTransforms();
                Physics.Simulate(0.02f);
                motor.CompletePhysicsStep(101);

                Assert.That(reader.Read(contacts, out var afterCorrection), Is.EqualTo(MotorContactReadStatus.Ready));
                Assert.That(afterCorrection, Is.Zero);
            }
            finally
            {
                if (actorObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(actorObject);
                }

                if (wall != null)
                {
                    UnityEngine.Object.DestroyImmediate(wall);
                }

                if (frictionless != null)
                {
                    UnityEngine.Object.DestroyImmediate(frictionless);
                }

                Physics.simulationMode = previousMode;
            }

            yield return null;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 실제 다중 충돌에서 Probe 상한과 출력 버퍼 절단을 확인합니다.
        /// </summary>
        // ------------------------------------------------------------
        [UnityTest]
        public IEnumerator TEST_MotorPhysics_3D다중접촉버퍼상한과절단()
        {
            var previousMode = Physics.simulationMode;
            var obstacles = new GameObject[48];
            GameObject actorObject = null;

            try
            {
                Physics.simulationMode = SimulationMode.Script;

                actorObject = new GameObject("TEST_MotorMultiContactActor");
                var body = actorObject.AddComponent<Rigidbody>();
                body.useGravity = false;
                body.constraints = RigidbodyConstraints.FreezeAll;
                actorObject.AddComponent<SphereCollider>().radius = 0.5f;
                var sensor = actorObject.AddComponent<MotorCollisionProbe3D>();

                // 독립적인 고정 Collider를 다수 배치해 Contact 배열 용량을 초과한다.
                for (var index = 0; index < obstacles.Length; index++)
                {
                    var angle = (index % 24) * (Mathf.PI * 2f / 24f);
                    var height = index < 24 ? 0.22f : -0.22f;
                    var position = new Vector3
                    (
                        Mathf.Cos(angle) * 0.61f, height,
                        Mathf.Sin(angle) * 0.61f
                    );
                    var obstacle = new GameObject("TEST_MotorContactObstacle");
                    obstacle.transform.position = position;
                    obstacle.AddComponent<SphereCollider>().radius = 0.25f;
                    obstacles[index] = obstacle;
                }

                var actor = new MotionActorKey(33, 1, Revision.FromUInt64(1));
                var motor = new Motor<MotorState3D>
                (
                    actor, new RigidbodyMotorBackend3D(body, sensor)
                );

                Physics.SyncTransforms();
                motor.BeginPhysicsStep();
                Physics.Simulate(0.02f);
                motor.CompletePhysicsStep(0);

                var allContacts = new ContactPoint[32];
                var smallBuffer = new ContactPoint[5];
                var reader = new MotorContactReader<MotorState3D, ContactPoint>(motor);

                Assert.That(sensor.ContactCount, Is.EqualTo(32));
                Assert.That(sensor.Truncated, Is.True);
                var status = reader.Read(allContacts, out var fullCount);
                Assert.That(status, Is.EqualTo(MotorContactReadStatus.Ready));
                Assert.That(fullCount, Is.EqualTo(32));

                status = reader.Read(smallBuffer, out var smallCount);
                Assert.That(status, Is.EqualTo(MotorContactReadStatus.Ready));
                Assert.That(smallCount, Is.EqualTo(5));
                Assert.That(sensor.ContactCount, Is.EqualTo(32));

                motor.BeginPhysicsStep();
                Assert.That(sensor.ContactCount, Is.Zero);
                Assert.That(sensor.Truncated, Is.False);
                status = reader.Read(smallBuffer, out _);
                Assert.That(status, Is.EqualTo(MotorContactReadStatus.NotObserved));
                Physics.Simulate(0.02f);
                motor.CompletePhysicsStep(1);
            }
            finally
            {
                if (actorObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(actorObject);
                }

                for (var index = 0; index < obstacles.Length; index++)
                {
                    if (obstacles[index] != null)
                    {
                        UnityEngine.Object.DestroyImmediate(obstacles[index]);
                    }
                }

                Physics.simulationMode = previousMode;
            }

            yield return null;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Probe 비활성 구간에 이전 접촉이 다시 나타나지 않습니다.
        /// </summary>
        // ------------------------------------------------------------
        [UnityTest]
        public IEnumerator TEST_MotorPhysics_3DProbe비활성재연결시이전접촉무효화()
        {
            var previousMode = Physics.simulationMode;
            GameObject floor = null;
            GameObject actorObject = null;

            try
            {
                Physics.simulationMode = SimulationMode.Script;

                floor = new GameObject("TEST_MotorProbeSwitchFloor");
                floor.transform.position = new Vector3(0f, -0.5f, 0f);
                floor.AddComponent<BoxCollider>().size = new Vector3(10f, 1f, 10f);

                actorObject = new GameObject("TEST_MotorProbeSwitchActor");
                actorObject.transform.position = new Vector3(0f, 0.45f, 0f);
                var body = actorObject.AddComponent<Rigidbody>();
                body.useGravity = false;
                body.constraints = RigidbodyConstraints.FreezeAll;
                actorObject.AddComponent<SphereCollider>().radius = 0.5f;
                var probe = actorObject.AddComponent<MotorCollisionProbe3D>();

                var actor = new MotionActorKey(42, 1, Revision.FromUInt64(1));
                var motor = new Motor<MotorState3D>
                (
                    actor, new RigidbodyMotorBackend3D(body, probe)
                );
                var contacts = new ContactPoint[32];
                var reader = new MotorContactReader<MotorState3D, ContactPoint>(motor);

                Physics.SyncTransforms();
                motor.BeginPhysicsStep();
                Physics.Simulate(0.02f);
                motor.CompletePhysicsStep(0);

                var status = reader.Read(contacts, out var firstCount);
                Assert.That(status, Is.EqualTo(MotorContactReadStatus.Ready));
                Assert.That(firstCount, Is.GreaterThan(0));

                probe.enabled = false;
                Assert.That(reader.IsSupported, Is.False);
                probe.enabled = true;

                // 이미 완료된 Step의 관측은 Probe 재활성화로 부활하지 않는다.
                status = reader.Read(contacts, out _);
                Assert.That(status, Is.EqualTo(MotorContactReadStatus.NotObserved));

                probe.enabled = false;
                motor.BeginPhysicsStep();
                Physics.Simulate(0.02f);
                motor.CompletePhysicsStep(1);

                // Probe가 참여하지 않은 Step은 빈 Contact가 아닌 미관측 상태다.
                probe.enabled = true;
                status = reader.Read(contacts, out var staleCount);
                Assert.That(status, Is.EqualTo(MotorContactReadStatus.NotObserved));
                Assert.That(staleCount, Is.Zero);
                Assert.That(probe.ContactCount, Is.Zero);

                motor.BeginPhysicsStep();
                Physics.Simulate(0.02f);
                motor.CompletePhysicsStep(2);
                status = reader.Read(contacts, out var restoredCount);
                Assert.That(status, Is.EqualTo(MotorContactReadStatus.Ready));
                Assert.That(restoredCount, Is.GreaterThan(0));
            }
            finally
            {
                if (actorObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(actorObject);
                }

                if (floor != null)
                {
                    UnityEngine.Object.DestroyImmediate(floor);
                }

                Physics.simulationMode = previousMode;
            }

            yield return null;
        }

    #endregion

    #region H-1: 2D 자동 Step 관측 경계

        // ------------------------------------------------------------
        /// <summary>
        /// Unity 자동 2D FixedUpdate 뒤에만 Contact 관측을 완료합니다.
        /// </summary>
        // ------------------------------------------------------------
        [UnityTest]
        public IEnumerator TEST_MotorPhysics_2D자동PhysicsStep의관측경계()
        {
            var previousMode = Physics2D.simulationMode;
            GameObject floor = null;
            GameObject actorObject = null;

            try
            {
                Physics2D.simulationMode = SimulationMode2D.FixedUpdate;

                floor = new GameObject("TEST_MotorAutoPhysicsFloor2D");
                floor.transform.position = new Vector3(0f, -0.5f, 0f);
                floor.AddComponent<BoxCollider2D>().size = new Vector2(10f, 1f);

                actorObject = new GameObject("TEST_MotorAutoPhysicsActor2D");
                actorObject.transform.position = new Vector3(0f, 0.45f, 0f);
                var body = actorObject.AddComponent<Rigidbody2D>();
                body.gravityScale = 0f;
                body.constraints = RigidbodyConstraints2D.FreezeAll;
                actorObject.AddComponent<CircleCollider2D>().radius = 0.5f;

                var motor = new Motor<MotorState2D>
                (
                    new MotionActorKey(31, 2, Revision.FromUInt64(1)),
                    new RigidbodyMotorBackend2D(body)
                );
                var contacts = new ContactPoint2D[16];
                var reader = new MotorContactReader<MotorState2D, ContactPoint2D>(motor);

                Physics2D.SyncTransforms();
                motor.BeginPhysicsStep();

                var before = reader.Read(contacts, out _);
                Assert.That(before, Is.EqualTo(MotorContactReadStatus.NotObserved));

                // Unity의 자동 2D Physics가 한 번 실행되도록 기다린다.
                yield return new WaitForFixedUpdate();

                motor.CompletePhysicsStep(0);
                var after = reader.Read(contacts, out var count);
                Assert.That(after, Is.EqualTo(MotorContactReadStatus.Ready));
                Assert.That(count, Is.GreaterThan(0));
                Assert.That(motor.ObservedPhysicsStep, Is.Zero);
            }
            finally
            {
                if (actorObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(actorObject);
                }

                if (floor != null)
                {
                    UnityEngine.Object.DestroyImmediate(floor);
                }

                Physics2D.simulationMode = previousMode;
            }
        }

    #endregion

    #region H-2: 3D 자동 Step 관측 경계

        // ------------------------------------------------------------
        /// <summary>
        /// Unity 자동 FixedUpdate Physics가 끝난 뒤 Contact를 읽습니다.
        /// </summary>
        // ------------------------------------------------------------
        [UnityTest]
        public IEnumerator TEST_MotorPhysics_3D자동PhysicsStep과Host관측경계()
        {
            var previousMode = Physics.simulationMode;
            GameObject floor = null;
            GameObject actorObject = null;

            try
            {
                Physics.simulationMode = SimulationMode.FixedUpdate;

                floor = new GameObject("TEST_MotorAutoPhysicsFloor");
                floor.transform.position = new Vector3(0f, -0.5f, 0f);
                floor.AddComponent<BoxCollider>().size = new Vector3(10f, 1f, 10f);

                actorObject = new GameObject("TEST_MotorAutoPhysicsActor");
                actorObject.transform.position = new Vector3(0f, 0.45f, 0f);
                var body = actorObject.AddComponent<Rigidbody>();
                body.useGravity = false;
                body.constraints = RigidbodyConstraints.FreezeAll;
                actorObject.AddComponent<SphereCollider>().radius = 0.5f;
                var probe = actorObject.AddComponent<MotorCollisionProbe3D>();

                var actor = new MotionActorKey(34, 1, Revision.FromUInt64(1));
                var motor = new Motor<MotorState3D>
                (
                    actor, new RigidbodyMotorBackend3D(body, probe)
                );
                var contacts = new ContactPoint[32];
                var reader = new MotorContactReader<MotorState3D, ContactPoint>(motor);

                Physics.SyncTransforms();
                motor.BeginPhysicsStep();
                var pending = reader.Read(contacts, out _);
                Assert.That(pending, Is.EqualTo(MotorContactReadStatus.NotObserved));

                // Unity가 소유한 FixedUpdate Physics를 실행하도록 직접 Simulate하지 않는다.
                yield return new WaitForFixedUpdate();

                Assert.That(probe.ContactCount, Is.GreaterThan(0));
                motor.CompletePhysicsStep(0);
                var ready = reader.Read(contacts, out var count);
                Assert.That(ready, Is.EqualTo(MotorContactReadStatus.Ready));
                Assert.That(count, Is.GreaterThan(0));
                Assert.That(motor.ObservedPhysicsStep, Is.Zero);
            }
            finally
            {
                if (actorObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(actorObject);
                }

                if (floor != null)
                {
                    UnityEngine.Object.DestroyImmediate(floor);
                }

                Physics.simulationMode = previousMode;
            }
        }

    #endregion

    #region A-1: 2D 다중 Actor 격리

        // ------------------------------------------------------------
        /// <summary>
        /// 2D World의 Actor별 Contact·Correction 격리를 확인합니다.
        /// </summary>
        // ------------------------------------------------------------
        [UnityTest]
        public IEnumerator TEST_MotorPhysics_2D다중Actor관측격리와Correction()
        {
            var previousMode = Physics2D.simulationMode;
            GameObject floor = null;
            GameObject firstObject = null;
            GameObject secondObject = null;

            try
            {
                Physics2D.simulationMode = SimulationMode2D.Script;

                floor = new GameObject("TEST_MotorMultiActorFloor2D");
                floor.transform.position = new Vector3(0f, -0.5f, 0f);
                floor.AddComponent<BoxCollider2D>().size = new Vector2(30f, 1f);

                firstObject = new GameObject("TEST_MotorMultiActorFirst2D");
                firstObject.transform.position = new Vector3(-4f, 0.45f, 0f);
                var firstBody = firstObject.AddComponent<Rigidbody2D>();
                firstBody.gravityScale = 0f;
                firstBody.constraints = RigidbodyConstraints2D.FreezeAll;
                firstObject.AddComponent<CircleCollider2D>().radius = 0.5f;

                secondObject = new GameObject("TEST_MotorMultiActorSecond2D");
                secondObject.transform.position = new Vector3(4f, 0.45f, 0f);
                var secondBody = secondObject.AddComponent<Rigidbody2D>();
                secondBody.gravityScale = 0f;
                secondBody.constraints = RigidbodyConstraints2D.FreezeAll;
                secondObject.AddComponent<CircleCollider2D>().radius = 0.5f;

                var first = new Motor<MotorState2D>
                (
                    new MotionActorKey(31, 3, Revision.FromUInt64(1)),
                    new RigidbodyMotorBackend2D(firstBody)
                );
                var second = new Motor<MotorState2D>
                (
                    new MotionActorKey(31, 4, Revision.FromUInt64(1)),
                    new RigidbodyMotorBackend2D(secondBody)
                );

                var firstContacts = new ContactPoint2D[16];
                var secondContacts = new ContactPoint2D[16];
                var firstReader = new MotorContactReader<MotorState2D, ContactPoint2D>(first);
                var secondReader = new MotorContactReader<MotorState2D, ContactPoint2D>(second);

                Physics2D.SyncTransforms();
                first.BeginPhysicsStep();
                second.BeginPhysicsStep();
                Physics2D.Simulate(0.02f);
                first.CompletePhysicsStep(0);
                second.CompletePhysicsStep(0);

                var firstStatus = firstReader.Read(firstContacts, out var firstCount);
                var secondStatus = secondReader.Read(secondContacts, out var secondCount);
                Assert.That(firstStatus, Is.EqualTo(MotorContactReadStatus.Ready));
                Assert.That(secondStatus, Is.EqualTo(MotorContactReadStatus.Ready));
                Assert.That(firstCount, Is.GreaterThan(0));
                Assert.That(secondCount, Is.GreaterThan(0));

                var secondVersion = second.PhysicsObservationGeneration;
                first.ApplyCurrentState
                (
                    new MotorState2D
                    (
                        new Vector2(-4f, 5f), 0f,
                        Vector2.zero, 0f
                    )
                );

                firstStatus = firstReader.Read(firstContacts, out _);
                Assert.That(firstStatus, Is.EqualTo(MotorContactReadStatus.NotObserved));
                Assert.That(second.PhysicsObservationGeneration, Is.EqualTo(secondVersion));
                Assert.That(second.ObservedPhysicsStep, Is.Zero);
                secondStatus = secondReader.Read(secondContacts, out var preservedCount);
                Assert.That(secondStatus, Is.EqualTo(MotorContactReadStatus.Ready));
                Assert.That(preservedCount, Is.EqualTo(secondCount));

                first.BeginPhysicsStep();
                second.BeginPhysicsStep();
                Physics2D.SyncTransforms();
                Physics2D.Simulate(0.02f);
                first.CompletePhysicsStep(1);
                second.CompletePhysicsStep(1);

                firstStatus = firstReader.Read(firstContacts, out var separatedCount);
                secondStatus = secondReader.Read(secondContacts, out var groundedCount);
                Assert.That(firstStatus, Is.EqualTo(MotorContactReadStatus.Ready));
                Assert.That(secondStatus, Is.EqualTo(MotorContactReadStatus.Ready));
                Assert.That(separatedCount, Is.Zero);
                Assert.That(groundedCount, Is.GreaterThan(0));
            }
            finally
            {
                if (secondObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(secondObject);
                }

                if (firstObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(firstObject);
                }

                if (floor != null)
                {
                    UnityEngine.Object.DestroyImmediate(floor);
                }

                Physics2D.simulationMode = previousMode;
            }

            yield return null;
        }

    #endregion

    #region A-2: 3D 다중 Actor 격리

        // ------------------------------------------------------------
        /// <summary>
        /// 한 World의 두 Actor 중 하나를 교정해도 다른 관측은 유지됩니다.
        /// </summary>
        // ------------------------------------------------------------
        [UnityTest]
        public IEnumerator TEST_MotorPhysics_3D다중Actor관측격리와Correction()
        {
            var previousMode = Physics.simulationMode;
            GameObject floor = null;
            GameObject firstObject = null;
            GameObject secondObject = null;

            try
            {
                Physics.simulationMode = SimulationMode.Script;

                floor = new GameObject("TEST_MotorMultiActorFloor");
                floor.transform.position = new Vector3(0f, -0.5f, 0f);
                floor.AddComponent<BoxCollider>().size = new Vector3(30f, 1f, 10f);

                firstObject = new GameObject("TEST_MotorMultiActorFirst");
                firstObject.transform.position = new Vector3(-4f, 0.45f, 0f);
                var firstBody = firstObject.AddComponent<Rigidbody>();
                firstBody.useGravity = false;
                firstBody.constraints = RigidbodyConstraints.FreezeAll;
                firstObject.AddComponent<SphereCollider>().radius = 0.5f;
                var firstProbe = firstObject.AddComponent<MotorCollisionProbe3D>();

                secondObject = new GameObject("TEST_MotorMultiActorSecond");
                secondObject.transform.position = new Vector3(4f, 0.45f, 0f);
                var secondBody = secondObject.AddComponent<Rigidbody>();
                secondBody.useGravity = false;
                secondBody.constraints = RigidbodyConstraints.FreezeAll;
                secondObject.AddComponent<SphereCollider>().radius = 0.5f;
                var secondProbe = secondObject.AddComponent<MotorCollisionProbe3D>();

                var first = new Motor<MotorState3D>
                (
                    new MotionActorKey(41, 1, Revision.FromUInt64(1)),
                    new RigidbodyMotorBackend3D(firstBody, firstProbe)
                );
                var second = new Motor<MotorState3D>
                (
                    new MotionActorKey(41, 2, Revision.FromUInt64(1)),
                    new RigidbodyMotorBackend3D(secondBody, secondProbe)
                );

                var firstContacts = new ContactPoint[32];
                var secondContacts = new ContactPoint[32];
                var firstReader = new MotorContactReader<MotorState3D, ContactPoint>(first);
                var secondReader = new MotorContactReader<MotorState3D, ContactPoint>(second);

                Physics.SyncTransforms();
                first.BeginPhysicsStep();
                second.BeginPhysicsStep();
                Physics.Simulate(0.02f);
                first.CompletePhysicsStep(0);
                second.CompletePhysicsStep(0);

                var firstStatus = firstReader.Read(firstContacts, out var firstCount);
                var secondStatus = secondReader.Read(secondContacts, out var secondCount);
                Assert.That(firstStatus, Is.EqualTo(MotorContactReadStatus.Ready));
                Assert.That(secondStatus, Is.EqualTo(MotorContactReadStatus.Ready));
                Assert.That(firstCount, Is.GreaterThan(0));
                Assert.That(secondCount, Is.GreaterThan(0));

                var secondVersion = second.PhysicsObservationGeneration;
                first.ApplyCurrentState
                (
                    new MotorState3D
                    (
                        new Vector3(-4f, 5f, 0f), Quaternion.identity,
                        Vector3.zero, Vector3.zero
                    )
                );

                firstStatus = firstReader.Read(firstContacts, out _);
                Assert.That(firstStatus, Is.EqualTo(MotorContactReadStatus.NotObserved));
                Assert.That(firstProbe.ContactCount, Is.Zero);
                Assert.That(second.PhysicsObservationGeneration, Is.EqualTo(secondVersion));
                Assert.That(second.ObservedPhysicsStep, Is.Zero);
                secondStatus = secondReader.Read(secondContacts, out var preservedCount);
                Assert.That(secondStatus, Is.EqualTo(MotorContactReadStatus.Ready));
                Assert.That(preservedCount, Is.EqualTo(secondCount));
                Assert.That(secondProbe.ContactCount, Is.EqualTo(secondCount));

                first.BeginPhysicsStep();
                second.BeginPhysicsStep();
                Physics.SyncTransforms();
                Physics.Simulate(0.02f);
                first.CompletePhysicsStep(1);
                second.CompletePhysicsStep(1);

                firstStatus = firstReader.Read(firstContacts, out var separatedCount);
                secondStatus = secondReader.Read(secondContacts, out var groundedCount);
                Assert.That(firstStatus, Is.EqualTo(MotorContactReadStatus.Ready));
                Assert.That(secondStatus, Is.EqualTo(MotorContactReadStatus.Ready));
                Assert.That(separatedCount, Is.Zero);
                Assert.That(groundedCount, Is.GreaterThan(0));
            }
            finally
            {
                if (secondObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(secondObject);
                }

                if (firstObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(firstObject);
                }

                if (floor != null)
                {
                    UnityEngine.Object.DestroyImmediate(floor);
                }

                Physics.simulationMode = previousMode;
            }

            yield return null;
        }

    #endregion

    }
}