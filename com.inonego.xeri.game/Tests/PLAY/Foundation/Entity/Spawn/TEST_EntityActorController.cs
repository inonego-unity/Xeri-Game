/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : TEST_EntityActorController.cs
수정일 : 2026-11-14

# 설명
EntityActorController의 Detached ↔ Bound Actor 관계, Rebind와 Entity Registry 연결 트랜잭션을 검증한다.
객체 Create/Release와 Entity Attach/Detach, 연결·해제 중 재진입의 상태 정합성을 확인한다.
Unity Test Runner (Play Mode)에서 실행한다.

# 테스트 구성
 E: 명시적 0..N Actor 수명
 X: Actor binding failure boundary
 B: 선택적 Entity Registry 연동
========================================================================= BLOCK_HEADER_END */

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

using UnityEngine;
using UnityEngine.TestTools;

using NUnit;
using NUnit.Framework;

namespace inonego.Xeri.Game.TEST._EntitySpawn
{
    using Xeri;
    using Xeri.Game;

    // ================================================================================
    /// <summary>
    /// EntityActorController의 명시적 0..N 수명과 선택적 Registry 연동을 검증한다.
    /// </summary>
    // ================================================================================
    public class TEST_EntityActorController
    {

    #region 헬퍼

        [Serializable]
        private sealed class TestEntity : EntityBase
        {
            // NONE
        }

        private sealed class TestEntityActor : EntityActorBase<TestEntity>
        {
            private readonly Dictionary<TestEntity, string> bindFailures = new();
            private string nextUnbindFailure = null;

            // ------------------------------------------------------------
            /// <summary>
            /// Entity Unbind 훅에서 호출할 검증 작업.
            /// </summary>
            // ------------------------------------------------------------
            public Action OnUnbindAction { get; set; }

            // ------------------------------------------------------------
            /// <summary>
            /// Entity Bind 실패를 예약한다.
            /// </summary>
            // ------------------------------------------------------------
            public void FailBind(TestEntity entity, string message)
            {
                bindFailures[entity] = message;
            }

            // ------------------------------------------------------------
            /// <summary>
            /// 다음 Unbind 실패를 예약한다.
            /// </summary>
            // ------------------------------------------------------------
            public void FailNextUnbind(string message)
            {
                nextUnbindFailure = message;
            }

            protected override void OnBindEntity(TestEntity entity)
            {
                if (bindFailures.Remove(entity, out var message))
                {
                    throw new InvalidOperationException(message);
                }
            }

            protected override void OnUnbindEntity(TestEntity previousEntity)
            {
                OnUnbindAction?.Invoke();

                if (nextUnbindFailure == null)
                {
                    return;
                }

                var message = nextUnbindFailure;
                nextUnbindFailure = null;
                throw new InvalidOperationException(message);
            }
        }

        [Serializable]
        private sealed class TestEntityRegistry : EntitySpawnRegistry<TestEntity>
        {
            protected override TestEntity Acquire()
            {
                return new TestEntity();
            }

            // ------------------------------------------------------------
            /// <summary>
            /// Registry Spawn을 테스트에 노출한다.
            /// </summary>
            // ------------------------------------------------------------
            public new bool TrySpawn(out TestEntity spawned)
            {
                return base.TrySpawn(out spawned);
            }
        }

        private
        (
            EntityActorController<TestEntityActor, TestEntity> controller,
            TestEntityRegistry registry
        ) CreateRuntime()
        {
            prefab = new GameObject("TestPrefab");
            prefab.AddComponent<TestEntityActor>();
            prefab.SetActive(false);

            var provider = new PrefabGameObjectProvider(prefab, null);
            var factory = new EntityActorFactory<TestEntityActor, TestEntity>(provider);
            var controller = new EntityActorController<TestEntityActor, TestEntity>(factory);
            var registry = new TestEntityRegistry();

            return (controller, registry);
        }

    #endregion

    #region 픽스처

        private GameObject prefab = null;

        // ------------------------------------------------------------
        /// <summary>
        /// Unity 테스트 Prefab을 정리한다.
        /// </summary>
        // ------------------------------------------------------------
        [TearDown]
        public void TearDown()
        {
            if (prefab != null)
            {
                UnityEngine.Object.DestroyImmediate(prefab);
                prefab = null;
            }
        }

    #endregion

    #region E-1: Detached / Attach / Detach / Rebind

        // ------------------------------------------------------------
        /// <summary>
        /// Detached Actor와 Rebind 수명을 확인한다.
        /// </summary>
        // ------------------------------------------------------------
        [UnityTest]
        public IEnumerator TEST_EntityActorController_Detached_관계와_Rebind를_독립적으로_관리()
        {
            var (controller, registry) = CreateRuntime();
            registry.TrySpawn(out var firstEntity);
            registry.TrySpawn(out var secondEntity);

            var first = controller.Create(firstEntity);
            var second = controller.Create(firstEntity);

            // Registry에 자동 연결되지 않았다면 Unbind는 명시적 Actor 수명을 종료하지 않는다.
            controller.Unbind();

            Assert.AreEqual(2, controller.Actors.Count());
            Assert.AreEqual(2, controller.DetachedActors.Count());
            Assert.AreEqual(0, controller.FindActors(firstEntity).Count);
            Assert.IsFalse(first.IsBound);
            Assert.IsNull(first.Entity);

            Assert.IsTrue(controller.Attach(first, firstEntity));
            Assert.IsTrue(controller.Attach(second, firstEntity));
            Assert.AreEqual(2, controller.FindActors(firstEntity).Count);
            Assert.IsFalse(controller.TryFindSingleActor(firstEntity, out _));

            Assert.IsTrue(controller.Detach(first));
            Assert.IsTrue(controller.ContainsActor(first));
            Assert.IsFalse(controller.IsActorBound(first));
            Assert.IsNull(first.Entity);
            Assert.AreEqual(1, controller.DetachedActors.Count());

            Assert.IsTrue(controller.Rebind(second, secondEntity));
            Assert.AreSame(secondEntity, second.Entity);
            Assert.AreEqual(0, controller.FindActors(firstEntity).Count);
            Assert.IsTrue(controller.TryFindSingleActor(secondEntity, out var rebound));
            Assert.AreSame(second, rebound);

            Assert.IsTrue(controller.Release(first, DespawnReason.Removed));
            Assert.IsTrue(controller.Release(second, DespawnReason.Removed));
            Assert.AreEqual(0, controller.Actors.Count());

            firstEntity.Despawn();
            secondEntity.Despawn();

            yield return null;
        }

    #endregion

    #region X-1: Actor binding failure boundary

        // ------------------------------------------------------------
        /// <summary>
        /// Bind 실패의 Detached 복구를 확인한다.
        /// </summary>
        // ------------------------------------------------------------
        [UnityTest]
        public IEnumerator TEST_EntityActorController_OnBindEntity실패후_Detached상태를복구()
        {
            var (controller, registry) = CreateRuntime();
            registry.TrySpawn(out var entity);
            var actor = controller.Create(entity);
            actor.FailBind(entity, "bind failure");

            Assert.Throws<InvalidOperationException>
            (
                () => controller.Attach(actor, entity)
            );

            Assert.IsTrue(controller.ContainsActor(actor));
            Assert.IsFalse(controller.IsActorBound(actor));
            Assert.IsNull(actor.Entity);
            Assert.AreEqual(0, controller.FindActors(entity).Count);
            Assert.AreEqual(1, controller.DetachedActors.Count());

            Assert.IsTrue(controller.Release(actor, DespawnReason.Removed));
            entity.Despawn();

            yield return null;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Unbind 실패 후 Detached 상태를 확인한다.
        /// </summary>
        // ------------------------------------------------------------
        [UnityTest]
        public IEnumerator TEST_EntityActorController_OnUnbindEntity실패에도_Detached관계를Commit()
        {
            var (controller, registry) = CreateRuntime();
            registry.TrySpawn(out var entity);
            var actor = controller.Create(entity);
            Assert.IsTrue(controller.Attach(actor, entity));
            actor.FailNextUnbind("unbind failure");

            Assert.Throws<InvalidOperationException>
            (
                () => controller.Detach(actor)
            );

            Assert.IsTrue(controller.ContainsActor(actor));
            Assert.IsFalse(controller.IsActorBound(actor));
            Assert.IsNull(actor.Entity);
            Assert.AreEqual(0, controller.FindActors(entity).Count);
            Assert.AreEqual(1, controller.DetachedActors.Count());

            Assert.IsTrue(controller.Release(actor, DespawnReason.Removed));
            entity.Despawn();

            yield return null;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Rebind 실패 시 이전 소유자를 복원한다.
        /// </summary>
        // ------------------------------------------------------------
        [UnityTest]
        public IEnumerator TEST_EntityActorController_RebindNextAttach실패시_PreviousOwner를복구()
        {
            var (controller, registry) = CreateRuntime();
            registry.TrySpawn(out var previous);
            registry.TrySpawn(out var next);
            var actor = controller.Create(previous);
            Assert.IsTrue(controller.Attach(actor, previous));
            actor.FailBind(next, "next bind failure");

            Assert.Throws<InvalidOperationException>
            (
                () => controller.Rebind(actor, next)
            );

            Assert.AreSame(previous, actor.Entity);
            Assert.IsTrue(controller.IsActorBound(actor));
            Assert.AreEqual(0, controller.FindActors(next).Count);
            Assert.IsTrue(controller.TryFindSingleActor(previous, out var restored));
            Assert.AreSame(actor, restored);

            Assert.IsTrue(controller.Release(actor, DespawnReason.Removed));
            previous.Despawn();
            next.Despawn();

            yield return null;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 재연결과 롤백 실패를 함께 전달한다.
        /// </summary>
        // ------------------------------------------------------------
        [UnityTest]
        public IEnumerator TEST_EntityActorController_RebindRollback도실패하면_두원인을Aggregate()
        {
            var (controller, registry) = CreateRuntime();
            registry.TrySpawn(out var previous);
            registry.TrySpawn(out var next);
            var actor = controller.Create(previous);
            Assert.IsTrue(controller.Attach(actor, previous));
            actor.FailBind(next, "next bind failure");
            actor.FailBind(previous, "previous rollback bind failure");

            var exception = Assert.Throws<AggregateException>
            (
                () => controller.Rebind(actor, next)
            );

            Assert.AreEqual(2, exception.InnerExceptions.Count);
            Assert.AreEqual("next bind failure", exception.InnerExceptions[0].Message);
            Assert.AreEqual("previous rollback bind failure", exception.InnerExceptions[1].Message);
            Assert.IsNull(actor.Entity);
            Assert.IsFalse(controller.IsActorBound(actor));
            Assert.AreEqual(0, controller.FindActors(previous).Count);
            Assert.AreEqual(0, controller.FindActors(next).Count);

            Assert.IsTrue(controller.Release(actor, DespawnReason.Removed));
            previous.Despawn();
            next.Despawn();

            yield return null;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Detached Actor의 terminal 반환을 확인한다.
        /// </summary>
        // ------------------------------------------------------------
        [UnityTest]
        public IEnumerator TEST_EntityActorController_DetachedActor도_TerminalRelease가능()
        {
            var (controller, registry) = CreateRuntime();
            registry.TrySpawn(out var entity);
            var actor = controller.Create(entity);

            Assert.IsFalse(actor.IsBound);
            Assert.IsTrue(controller.Release(actor, DespawnReason.Removed));

            Assert.IsFalse(controller.ContainsActor(actor));
            Assert.AreEqual(0, controller.Actors.Count());
            Assert.AreEqual(0, controller.DetachedActors.Count());

            entity.Despawn();

            yield return null;
        }

    #endregion

    #region B-1: 선택적 Entity Registry 연동

        // ------------------------------------------------------------
        /// <summary>
        /// 기존·신규 Entity의 Actor 자동 연결을 확인한다.
        /// </summary>
        // ------------------------------------------------------------
        [UnityTest]
        public IEnumerator TEST_EntityActorController_Bind시_기존과_신규Entity를_동기화하고_Unbind에서_정리()
        {
            var (controller, registry) = CreateRuntime();

            registry.TrySpawn(out var existing);
            controller.Bind(registry);

            Assert.IsTrue(controller.TryFindSingleActor(existing, out var existingActor));
            Assert.AreSame(existing, existingActor.Entity);

            registry.TrySpawn(out var added);

            Assert.IsTrue(controller.TryFindSingleActor(added, out var addedActor));
            Assert.AreSame(added, addedActor.Entity);

            existing.Despawn();

            Assert.AreEqual(0, controller.FindActors(existing).Count);
            Assert.IsTrue(controller.TryFindSingleActor(added, out _));

            controller.Unbind();

            Assert.AreEqual(0, controller.Actors.Count());

            added.Despawn();

            yield return null;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 복원된 Entity에 Actor만 재연결한다.
        /// </summary>
        // ------------------------------------------------------------
        [UnityTest]
        public IEnumerator TEST_EntityActorController_복원된_Registry_Bind는_기존_Actor만_동기화()
        {
            var (controller, registry) = CreateRuntime();
            registry.TrySpawn(out var original);

            var restored = JsonUtility.FromJson<TestEntityRegistry>(JsonUtility.ToJson(registry));
            var entity = restored.Find(original.Key);
            var liveNotifications = 0;

            restored.OnSpawned += (_, _) => liveNotifications++;
            controller.Bind(restored);

            Assert.IsNotNull(entity);
            Assert.AreEqual(SpawnState.Spawned, entity.SpawnState);
            Assert.AreEqual(0, liveNotifications);
            Assert.IsTrue(controller.TryFindSingleActor(entity, out var actor));
            Assert.AreSame(entity, actor.Entity);
            Assert.AreEqual(1, controller.Actors.Count());

            controller.Unbind();

            Assert.AreEqual(0, controller.Actors.Count());
            Assert.AreEqual(1, restored.Spawned.Count);
            Assert.AreEqual(0, liveNotifications);

            entity.Despawn();
            yield return null;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> Binding 중 중첩 Unbind를 거절한다.
        /// <br/> Registry와 Actor 연결은 유지된다.
        /// </summary>
        // ------------------------------------------------------------
        [UnityTest]
        public IEnumerator TEST_EntityActorController_Bind동기화중_Unbind재진입은_연결을훼손하지않음()
        {
            var (controller, registry) = CreateRuntime();
            registry.TrySpawn(out var existing);
            var resolverCalls = 0;

            controller.Bind
            (
                registry,
                (_, _) =>
                {
                    resolverCalls++;

                    if (resolverCalls == 1)
                    {
                        Assert.IsNull(controller.BoundRegistry);
                        Assert.Throws<InvalidOperationException>(() => controller.Unbind());
                    }
                    else
                    {
                        Assert.AreSame(registry, controller.BoundRegistry);
                    }

                    return default;
                }
            );

            Assert.AreEqual(1, resolverCalls);
            Assert.AreSame(registry, controller.BoundRegistry);
            Assert.IsTrue(controller.TryFindSingleActor(existing, out _));

            registry.TrySpawn(out var added);

            Assert.AreEqual(2, resolverCalls);
            Assert.IsTrue(controller.TryFindSingleActor(added, out _));
            Assert.AreEqual(2, controller.Actors.Count());

            controller.Unbind();

            Assert.IsNull(controller.BoundRegistry);
            Assert.AreEqual(0, controller.Actors.Count());
            Assert.AreEqual(2, registry.Spawned.Count);

            registry.DespawnAll();
            yield return null;
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> Bind 중 기존 객체 동기화가 실패하면 Registry Binding과 Actor를
        /// <br/> 정리하고 다음 Bind를 시작할 수 있다.
        /// </summary>
        // ----------------------------------------------------------------------
        [UnityTest]
        public IEnumerator TEST_EntityActorController_Bind동기화실패후_연결수명을재시작()
        {
            var (controller, registry) = CreateRuntime();
            registry.TrySpawn(out _);
            registry.TrySpawn(out _);
            var resolverCalls = 0;

            Assert.Throws<InvalidOperationException>
            (
                () => controller.Bind
                (
                    registry,
                    (_, _) =>
                    {
                        resolverCalls++;

                        if (resolverCalls == 2)
                        {
                            throw new InvalidOperationException("context failure");
                        }

                        return default;
                    }
                )
            );

            Assert.AreEqual(2, resolverCalls);
            Assert.IsNull(controller.BoundRegistry);
            Assert.AreEqual(0, controller.Actors.Count());
            Assert.AreEqual(2, registry.Spawned.Count);

            controller.Bind(registry);

            Assert.AreSame(registry, controller.BoundRegistry);
            Assert.AreEqual(2, controller.Actors.Count());

            controller.Unbind();

            Assert.AreEqual(0, controller.Actors.Count());
            registry.DespawnAll();
            yield return null;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 해제 중 중첩 Unbind를 거절한다.
        /// </summary>
        // ------------------------------------------------------------
        [UnityTest]
        public IEnumerator TEST_EntityActorController_Unbind정리중_재진입은_중복반환하지않음()
        {
            var (controller, registry) = CreateRuntime();
            registry.TrySpawn(out var entity);
            controller.Bind(registry);

            Assert.IsTrue(controller.TryFindSingleActor(entity, out var actor));

            actor.OnUnbindAction = () =>
            {
                Assert.IsNull(controller.BoundRegistry);
                Assert.Throws<InvalidOperationException>(() => controller.Unbind());
            };

            controller.Unbind();

            Assert.IsNull(controller.BoundRegistry);
            Assert.AreEqual(0, controller.Actors.Count());
            Assert.AreEqual(1, registry.Spawned.Count);

            registry.TrySpawn(out _);
            Assert.AreEqual(0, controller.Actors.Count());

            registry.DespawnAll();
            yield return null;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Actor Unbind 실패에도 Registry Binding은 해제한다.
        /// </summary>
        // ------------------------------------------------------------
        [UnityTest]
        public IEnumerator TEST_EntityActorController_Registry_Unbind실패에도_Actor를_정리()
        {
            var (controller, registry) = CreateRuntime();
            registry.TrySpawn(out var entity);
            controller.Bind(registry);

            Assert.IsTrue(controller.TryFindSingleActor(entity, out var actor));
            actor.FailNextUnbind("unbind failure");

            Assert.Throws<AggregateException>(() => controller.Unbind());

            Assert.IsNull(controller.BoundRegistry);
            Assert.AreEqual(0, controller.Actors.Count());
            Assert.AreEqual(1, registry.Spawned.Count);
            Assert.AreEqual(SpawnState.Spawned, entity.SpawnState);

            registry.TrySpawn(out _);
            Assert.AreEqual(0, controller.Actors.Count());

            registry.DespawnAll();
            yield return null;
        }

    #endregion

    }
}
