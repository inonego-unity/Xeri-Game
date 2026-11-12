/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : TEST_EntityActorController.cs
수정일 : 2026-11-12

# 설명
EntityActorController의 Detached ↔ Bound Actor 관계, Rebind와 선택적 Entity Registry 연동을 검증한다.
객체 Create/Release와 Entity Attach/Detach를 분리하고 동일 Actor instance의 owner migration을 주 계약으로 검증한다.
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

        private sealed class TestEntity : EntityBase
        {
            // NONE
        }

        private sealed class TestEntityActor : EntityActorBase<TestEntity>
        {
            private readonly Dictionary<TestEntity, string> bindFailures = new();
            private string nextUnbindFailure = null;

            public void FailBind(TestEntity entity, string message)
            {
                bindFailures[entity] = message;
            }

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
                if (nextUnbindFailure == null)
                {
                    return;
                }

                var message = nextUnbindFailure;
                nextUnbindFailure = null;
                throw new InvalidOperationException(message);
            }
        }

        private sealed class TestEntityRegistry : EntitySpawnRegistry<TestEntity>
        {
            protected override TestEntity Acquire()
            {
                return new TestEntity();
            }

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

        [UnityTest]
        public IEnumerator TEST_EntityActorController_Detached_관계와_Rebind를_독립적으로_관리()
        {
            var (controller, registry) = CreateRuntime();
            registry.TrySpawn(out var firstEntity);
            registry.TrySpawn(out var secondEntity);

            var first = controller.Create(firstEntity);
            var second = controller.Create(firstEntity);

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

    #endregion

    }
}
