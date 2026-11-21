/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : TEST_MovementScenario.cs
수정일 : 2026-11-21

# 설명
Motion Model → Motor → 실제 Physics Step → 관측 Feedback을 여러 Tick 연속으로 돌려 실사용 이동 시나리오를 검증한다.

# 특이사항, 제약사항
테스트가 Host 역할을 하며 수동 Physics Simulation으로 Step 순서를 고정한다. 전역 SimulationMode를 복원한다.

# 테스트 구성
 W: 걷는 중 수직 속도 (Walk)
 K: 넉백과 복귀·차단 (Knockback)
========================================================================= BLOCK_HEADER_END */

using System;
using System.Collections;

using UnityEngine;
using UnityEngine.TestTools;

using NUnit;
using NUnit.Framework;

using inonego;
using inonego.Xeri;
using inonego.Xeri.Serializable;
using inonego.Xeri.Game;
using inonego.Xeri.Game.Motion;
using inonego.Xeri.Game.Motor;

namespace inonego.Xeri.Game.TEST.Movement._Scenario
{
    // ============================================================
    /// <summary>
    /// 합성 모델과 실제 Physics를 잇는 이동 시나리오를 검증합니다.
    /// </summary>
    // ============================================================
    public sealed class TEST_MovementScenario
    {

    #region 헬퍼

        private const float DELTA = 0.02f;

        private static void Tick2D
        (
            MotionModel model, Motor<MotorState2D> motor, MotionIntent[] intents,
            MotionOverrideCommand[] overrides = null, MotionExternalCommand[] externals = null
        )
        {
            // UnityTest 반복기 안에서는 ref struct 입력 묶음을 만들 수 없어 이 도우미에서 만든다.
            var input = new MotionStepInput(intents, overrides, externals: externals);
            var frame = model.StepLocal(DELTA, input, MotionFeedback.None);
            Assert.That(motor.TryApply(frame), Is.True);
            motor.BeginPhysicsStep();
            Physics2D.Simulate(DELTA);
            motor.CompletePhysicsStep(frame.Tick.Index);
        }

        private static MotionFrame Step3D
        (
            MotionModel model, MotionIntent[] intents, in MotionFeedback feedback,
            MotionScaleCommand[] scales = null, MotionExternalCommand[] externals = null
        )
        {
            var input = new MotionStepInput(intents, scales: scales, externals: externals);
            return model.StepLocal(DELTA, input, feedback);
        }

    #endregion

    #region W-1: 2D 걷는 중 중력

        // ------------------------------------------------------------
        /// <summary>
        /// 옆에서 보는 2D에서 걷는 동안에도 중력이 수직 속도를 유지합니다.
        /// </summary>
        // ------------------------------------------------------------
        [UnityTest]
        public IEnumerator TEST_MovementScenario_2D옆보기걷는중중력유지()
        {
            var previousMode = Physics2D.simulationMode;
            GameObject actorObject = null;

            try
            {
                Physics2D.simulationMode = SimulationMode2D.Script;
                actorObject = new GameObject("TEST_ScenarioSideView");
                var body = actorObject.AddComponent<Rigidbody2D>();
                body.gravityScale = 1f;
                actorObject.AddComponent<CircleCollider2D>().radius = 0.5f;

                var actor = new MotionActorKey(41, 1, 1);
                var model = new MotionModel(actor, MotionModel.WorldUp);
                var motor = new Motor<MotorState2D>(actor, new RigidbodyMotorBackend2D(body));
                var walk = new[]
                {
                    new MotionIntent(MotionChannel.Planar, new Vector3(3f, 0f, 0f))
                };

                for (var tick = 0; tick < 10; tick++)
                {
                    Tick2D(model, motor, walk);
                }

                var state = motor.ReadCurrentState();
                Assert.That(state.LinearVelocity.x, Is.EqualTo(3f).Within(1e-3f));
                Assert.That(state.LinearVelocity.y, Is.LessThan(-1f), "Planar 이동이 중력 속도를 지웠습니다.");
            }
            finally
            {
                if (actorObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(actorObject);
                }

                Physics2D.simulationMode = previousMode;
            }

            yield return null;
        }

    #endregion

    #region K-1: 2D 넉백과 대시 복귀

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> 위에서 보는 2D에서 걷는 동안 넉백이 Tick마다 유지되고,
        /// <br/> 대시가 끝나면 걷기가 다시 획득 없이 이어집니다.
        /// </summary>
        // ------------------------------------------------------------
        [UnityTest]
        public IEnumerator TEST_MovementScenario_2D걷는중넉백유지와대시복귀()
        {
            var previousMode = Physics2D.simulationMode;
            GameObject actorObject = null;

            try
            {
                Physics2D.simulationMode = SimulationMode2D.Script;
                actorObject = new GameObject("TEST_ScenarioTopDown");
                var body = actorObject.AddComponent<Rigidbody2D>();
                body.gravityScale = 0f;
                body.linearDamping = 0f;

                var actor = new MotionActorKey(42, 1, 1);
                var model = new MotionModel(actor, MotionModel.TopDown2DUp);
                var motor = new Motor<MotorState2D>(actor, new RigidbodyMotorBackend2D(body));
                var walkOnly = new[]
                {
                    new MotionIntent(MotionChannel.Planar, new Vector3(2f, 0f, 0f))
                };
                const float decay = 2f;

                var knockback = new[]
                {
                    MotionExternalCommand.Set(new MotionSourceKey(1, 1), 0, NumericFOperation.ADD, new Vector3(0f, 6f, 0f), decay)
                };
                Tick2D(model, motor, walkOnly, externals: knockback);

                for (var tick = 1; tick < 5; tick++)
                {
                    Tick2D(model, motor, walkOnly);
                }

                // 다섯 번째 Tick의 넉백은 네 번 감쇠한 값이며 걷기 속도도 그대로다.
                var knocked = motor.ReadCurrentState().LinearVelocity;
                Assert.That(knocked.x, Is.EqualTo(2f).Within(1e-3f));
                Assert.That(knocked.y, Is.EqualTo(6f * Mathf.Exp(-decay * DELTA * 4f)).Within(0.02f));

                // 대시 동안은 대시 속도에 넉백이 더해지고, 끝나면 걷기로 돌아온다.
                var dash = new[]
                {
                    MotionOverrideCommand.Begin(new MotionSourceKey(2, 1), MotionChannel.Planar, 10, new Vector3(9f, 0f, 0f), duration: 0.05f)
                };
                Tick2D(model, motor, walkOnly, dash);
                Assert.That(motor.ReadCurrentState().LinearVelocity.x, Is.EqualTo(9f).Within(1e-3f));

                for (var tick = 0; tick < 3; tick++)
                {
                    Tick2D(model, motor, walkOnly);
                }

                var resumed = motor.ReadCurrentState().LinearVelocity;
                Assert.That(resumed.x, Is.EqualTo(2f).Within(1e-3f));
                Assert.That(resumed.y, Is.GreaterThan(0f));
            }
            finally
            {
                if (actorObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(actorObject);
                }

                Physics2D.simulationMode = previousMode;
            }

            yield return null;
        }

    #endregion

    #region K-2: 3D 스턴 중 넉백과 벽 차단

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> 스턴 중에도 넉백으로 밀리고, 벽에 막힌 성분은 제거되어
        /// <br/> 벽 관측이 끊겨도 되살아나지 않습니다.
        /// </summary>
        // ------------------------------------------------------------
        [UnityTest]
        public IEnumerator TEST_MovementScenario_3D스턴중넉백벽차단()
        {
            var previousMode = Physics.simulationMode;
            GameObject wall = null;
            GameObject actorObject = null;
            PhysicsMaterial frictionless = null;

            try
            {
                Physics.simulationMode = SimulationMode.Script;
                frictionless = new PhysicsMaterial("TEST_ScenarioNoFriction")
                {
                    dynamicFriction = 0f,
                    staticFriction = 0f,
                    frictionCombine = PhysicsMaterialCombine.Minimum,
                    bounciness = 0f
                };

                wall = new GameObject("TEST_ScenarioWall");
                wall.transform.position = new Vector3(1f, 0f, 0f);
                var wallCollider = wall.AddComponent<BoxCollider>();
                wallCollider.size = new Vector3(1f, 10f, 20f);
                wallCollider.sharedMaterial = frictionless;

                actorObject = new GameObject("TEST_ScenarioActor3D");
                actorObject.transform.position = new Vector3(-1f, 0f, 0f);
                var body = actorObject.AddComponent<Rigidbody>();
                body.useGravity = false;
                body.linearDamping = 0f;
                body.constraints = RigidbodyConstraints.FreezeRotation;
                var sphere = actorObject.AddComponent<SphereCollider>();
                sphere.radius = 0.5f;
                sphere.sharedMaterial = frictionless;
                var probe = actorObject.AddComponent<MotorCollisionProbe3D>();

                var actor = new MotionActorKey(43, 1, 1);
                var model = new MotionModel(actor, Vector3.up);
                var motor = new Motor<MotorState3D>(actor, new RigidbodyMotorBackend3D(body, probe));
                var reader = new MotorContactReader<MotorState3D, ContactPoint>(motor);
                var contacts = new ContactPoint[32];
                var hold = new[]
                {
                    new MotionIntent(MotionChannel.Planar, Vector3.zero)
                };
                var stun = new[]
                {
                    MotionScaleCommand.Add(new MotionSourceKey(1, 1), MotionChannel.All, MotionLayer.All, 100, NumericFOperation.SET, 0f)
                };
                var knockback = new[]
                {
                    MotionExternalCommand.Set(new MotionSourceKey(2, 1), 0, NumericFOperation.ADD, new Vector3(5f, 0f, 2f))
                };
                var feedback = MotionFeedback.None;
                var intended = Vector3.zero;
                var blockedTicks = 0;

                Physics.SyncTransforms();

                // Host 루프: 직전 관측으로 Feedback을 만들고 합성 → 적용 → Physics → 관측 순서로 돈다.
                for (var tick = 0; tick < 120 && blockedTicks < 5; tick++)
                {
                    var frame = tick == 0
                        ? Step3D(model, hold, feedback, stun, knockback)
                        : Step3D(model, hold, feedback);
                    intended = frame.Planar;
                    Assert.That(frame.BlockedChannels, Is.EqualTo(MotionChannel.All));
                    Assert.That(motor.TryApply(frame), Is.True);

                    motor.BeginPhysicsStep();
                    Physics.Simulate(DELTA);
                    motor.CompletePhysicsStep(frame.Tick.Index);

                    feedback = MotionFeedback.None;

                    if (reader.Read(contacts, out var count) == MotorContactReadStatus.Ready && count > 0)
                    {
                        var evaluated = MotorContactFeedbackEvaluator.Evaluate(intended, motor.ReadCurrentState().LinearVelocity, contacts, count);

                        if (evaluated.IsBlocked)
                        {
                            feedback = feedback.WithBlockingNormal(evaluated.BlockingNormal);
                            blockedTicks++;
                        }
                    }
                }

                Assert.That(blockedTicks, Is.GreaterThan(0), "벽 차단을 관측하지 못했습니다.");

                // 벽 방향 성분이 제거된 뒤에는 관측 없이 몇 Tick 진행해도 다시 벽으로 밀지 않는다.
                for (var tick = 0; tick < 5; tick++)
                {
                    var frame = Step3D(model, hold, MotionFeedback.None);
                    Assert.That(motor.TryApply(frame), Is.True);
                    motor.BeginPhysicsStep();
                    Physics.Simulate(DELTA);
                    motor.CompletePhysicsStep(frame.Tick.Index);
                    Assert.That(frame.Planar.x, Is.LessThan(0.01f));
                }

                var velocity = motor.ReadCurrentState().LinearVelocity;
                Assert.That(Mathf.Abs(velocity.x), Is.LessThan(0.2f));
                Assert.That(velocity.z, Is.EqualTo(2f).Within(0.2f));
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

    #endregion

    }
}
