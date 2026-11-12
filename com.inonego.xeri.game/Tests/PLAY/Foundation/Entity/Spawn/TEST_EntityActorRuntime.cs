/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : TEST_EntityActorRuntime.cs
수정일 : 2026-11-12

# 설명
EntityActorRuntime이 ActorController와 PresentationCoordinator를 생성·반환·Transfer transaction으로 조립하는 계약을 검증한다.
정상 lifecycle, Entity Rebind, Presentation Transfer rollback과 복합 실패 집계를 대상으로 한다.
Unity Test Runner (Play Mode)에서 실행한다.

# 테스트 구성
 L: 정상 Actor + Presentation lifecycle
 R: commit 전 explicit rollback
 X: 생성·반환 실패 rollback과 오류 집계
========================================================================= BLOCK_HEADER_END */

using System;
using System.Collections;
using System.Collections.Generic;

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
    /// EntityActorRuntime의 Actor + Presentation transaction 경계를 검증한다.
    /// </summary>
    // ================================================================================
    public class TEST_EntityActorRuntime
    {

    #region 헬퍼

        private sealed class TestEntity : EntityBase
        {
            // NONE
        }

        private sealed class TestActor : EntityActorBase<TestEntity>
        {
            // NONE
        }

        private sealed class TestFactory :
            EntityActorFactory<TestActor, TestEntity, EntityActorNoContext>
        {
            private readonly IGameObjectProvider provider;

            public bool FailOnCleanup { get; set; }
            public bool HasReleased { get; private set; }
            public DespawnReason LastReleaseReason { get; private set; }

            public TestFactory(IGameObjectProvider provider)
            {
                this.provider = provider;
            }

            protected override IGameObjectProvider ResolveProvider
            (
                TestEntity entity,
                in EntityActorNoContext context
            )
            {
                return provider;
            }

            protected override void CleanupActor
            (
                TestActor actor,
                DespawnReason reason
            )
            {
                HasReleased = true;
                LastReleaseReason = reason;

                if (FailOnCleanup)
                {
                    throw new InvalidOperationException("actor cleanup failure");
                }
            }
        }

        private sealed class TestPresentationProvider :
            IEntityPresentationProvider<TestActor, TestEntity>
        {
            private readonly string name;
            private readonly List<string> trace;

            public bool FailOnCreate { get; set; }
            public bool FailOnRelease { get; set; }
            public bool FailOnTransfer { get; set; }
            public bool FailOnTransferRollback { get; set; }
            public DespawnReason LastReleaseReason { get; private set; }

            public TestPresentationProvider(string name, List<string> trace)
            {
                this.name = name;
                this.trace = trace;
            }

            public void OnActorCreated(TestEntity entity, TestActor actor)
            {
                trace.Add($"{name}.Create");

                if (FailOnCreate)
                {
                    throw new InvalidOperationException($"{name} create failure");
                }
            }

            public void OnActorReleasing
            (
                TestEntity entity,
                TestActor actor,
                DespawnReason reason
            )
            {
                LastReleaseReason = reason;
                trace.Add($"{name}.Release");

                if (FailOnRelease)
                {
                    throw new InvalidOperationException($"{name} release failure");
                }
            }

            public void OnActorTransferred
            (
                TestEntity previousEntity,
                TestEntity nextEntity,
                TestActor actor
            )
            {
                trace.Add($"{name}.Transfer");

                if (FailOnTransfer)
                {
                    throw new InvalidOperationException($"{name} transfer failure");
                }
            }

            public void OnActorTransferRollback
            (
                TestEntity previousEntity,
                TestEntity nextEntity,
                TestActor actor
            )
            {
                trace.Add($"{name}.TransferRollback");

                if (FailOnTransferRollback)
                {
                    throw new InvalidOperationException($"{name} transfer rollback failure");
                }
            }

            public void OnPresentationOwnerTransferred
            (
                TestActor previousOwner,
                TestActor nextOwner
            )
            {
                // NONE
            }

            public void OnPresentationOwnerTransferRollback
            (
                TestActor previousOwner,
                TestActor nextOwner
            )
            {
                // NONE
            }
        }

        private
        (
            EntityActorRuntime<TestActor, TestEntity, EntityActorNoContext> runtime,
            EntityActorController<TestActor, TestEntity, EntityActorNoContext> controller,
            EntityPresentationCoordinator<TestActor, TestEntity> coordinator,
            TestFactory factory
        ) CreateRuntime()
        {
            prefab = new GameObject("TestActorPrefab");
            prefab.AddComponent<TestActor>();
            prefab.SetActive(false);

            var provider = new PrefabGameObjectProvider(prefab, null);
            var factory = new TestFactory(provider);
            var controller =
                new EntityActorController<TestActor, TestEntity, EntityActorNoContext>(factory);
            var coordinator =
                new EntityPresentationCoordinator<TestActor, TestEntity>();
            var runtime =
                new EntityActorRuntime<TestActor, TestEntity, EntityActorNoContext>
                (
                    controller,
                    coordinator
                );

            return (runtime, controller, coordinator, factory);
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

    #region L-1: 정상 Actor + Presentation lifecycle

        [UnityTest]
        public IEnumerator TEST_EntityActorRuntime_Create와Release가_Actor와Presentation수명을_함께완료()
        {
            var (runtime, controller, coordinator, _) = CreateRuntime();
            var trace = new List<string>();
            var presentation = new TestPresentationProvider("Presentation", trace);
            var entity = new TestEntity();
            var context = default(EntityActorNoContext);
            coordinator.Register(presentation);

            var actor = runtime.Create(entity, context);

            Assert.IsTrue(controller.ContainsActor(actor));
            Assert.AreSame(entity, actor.Entity);
            CollectionAssert.AreEqual
            (
                new[]
                {
                    "Presentation.Create"
                },
                trace
            );

            Assert.IsTrue(runtime.Release(actor, DespawnReason.Removed));

            Assert.IsFalse(controller.ContainsActor(actor));
            Assert.IsNull(actor.Entity);
            CollectionAssert.AreEqual
            (
                new[]
                {
                    "Presentation.Create",
                    "Presentation.Release"
                },
                trace
            );

            yield return null;
        }

    #endregion

    #region T-1: Actor + Presentation Transfer

        [UnityTest]
        public IEnumerator TEST_EntityActorRuntime_Transfer가_같은Actor를_새Entity로_이전()
        {
            var (runtime, controller, coordinator, _) = CreateRuntime();
            var trace = new List<string>();
            var presentation = new TestPresentationProvider("Presentation", trace);
            var previous = new TestEntity();
            var next = new TestEntity();
            var context = default(EntityActorNoContext);
            coordinator.Register(presentation);

            var actor = runtime.Create(previous, context);

            Assert.IsTrue(runtime.Transfer(actor, next));
            Assert.AreSame(next, actor.Entity);
            Assert.AreEqual(0, controller.FindActors(previous).Count);
            Assert.IsTrue(controller.TryFindSingleActor(next, out var transferred));
            Assert.AreSame(actor, transferred);
            CollectionAssert.AreEqual
            (
                new[]
                {
                    "Presentation.Create",
                    "Presentation.Transfer"
                },
                trace
            );

            runtime.Release(actor, DespawnReason.Removed);

            yield return null;
        }

    #endregion

    #region T-2: Presentation Transfer 실패 rollback

        [UnityTest]
        public IEnumerator TEST_EntityActorRuntime_PresentationTransfer실패시_Actor와완료Provider를_Rollback()
        {
            var (runtime, controller, coordinator, _) = CreateRuntime();
            var trace = new List<string>();
            var first = new TestPresentationProvider("First", trace);
            var second = new TestPresentationProvider("Second", trace)
            {
                FailOnTransfer = true
            };
            var previous = new TestEntity();
            var next = new TestEntity();
            var context = default(EntityActorNoContext);
            coordinator.Register(first);
            coordinator.Register(second);

            var actor = runtime.Create(previous, context);

            Assert.Throws<InvalidOperationException>
            (
                () => runtime.Transfer(actor, next)
            );

            Assert.AreSame(previous, actor.Entity);
            Assert.IsTrue(controller.TryFindSingleActor(previous, out var restored));
            Assert.AreSame(actor, restored);
            Assert.AreEqual(0, controller.FindActors(next).Count);
            CollectionAssert.AreEqual
            (
                new[]
                {
                    "First.Create",
                    "Second.Create",
                    "First.Transfer",
                    "Second.Transfer",
                    "First.TransferRollback"
                },
                trace
            );

            runtime.Release(actor, DespawnReason.Removed);

            yield return null;
        }

    #endregion

    #region R-1: commit 전 explicit rollback

        [UnityTest]
        public IEnumerator TEST_EntityActorRuntime_Rollback이_SpawnRollback사유로_전체Transaction을정리()
        {
            var (runtime, controller, coordinator, factory) = CreateRuntime();
            var trace = new List<string>();
            var presentation = new TestPresentationProvider("Presentation", trace);
            var entity = new TestEntity();
            var context = default(EntityActorNoContext);
            coordinator.Register(presentation);

            var actor = runtime.Create(entity, context);
            Assert.IsTrue(runtime.Rollback(actor));

            Assert.IsFalse(controller.ContainsActor(actor));
            Assert.IsNull(actor.Entity);
            Assert.AreEqual
            (
                DespawnReason.SpawnRollback,
                presentation.LastReleaseReason
            );
            Assert.IsTrue(factory.HasReleased);
            Assert.AreEqual
            (
                DespawnReason.SpawnRollback,
                factory.LastReleaseReason
            );

            yield return null;
        }

    #endregion

    #region X-1: Presentation Create 실패 rollback

        [UnityTest]
        public IEnumerator TEST_EntityActorRuntime_PresentationCreate실패시_Actor까지Rollback()
        {
            var (runtime, controller, coordinator, factory) = CreateRuntime();
            var trace = new List<string>();
            var first = new TestPresentationProvider("First", trace);
            var second = new TestPresentationProvider("Second", trace)
            {
                FailOnCreate = true
            };
            var entity = new TestEntity();
            var context = default(EntityActorNoContext);
            coordinator.Register(first);
            coordinator.Register(second);

            Assert.Throws<InvalidOperationException>
            (
                () => runtime.Create(entity, context)
            );

            Assert.IsFalse(controller.TryFindSingleActor(entity, out _));
            Assert.AreEqual(0, controller.FindActors(entity).Count);
            Assert.AreEqual
            (
                DespawnReason.SpawnRollback,
                first.LastReleaseReason
            );
            Assert.IsTrue(factory.HasReleased);
            Assert.AreEqual
            (
                DespawnReason.SpawnRollback,
                factory.LastReleaseReason
            );
            CollectionAssert.AreEqual
            (
                new[]
                {
                    "First.Create",
                    "Second.Create",
                    "First.Release"
                },
                trace
            );

            yield return null;
        }

    #endregion

    #region X-2: Presentation Create + Actor rollback 복합 실패

        [UnityTest]
        public IEnumerator TEST_EntityActorRuntime_PresentationCreate과ActorRollback실패를_Aggregate()
        {
            var (runtime, controller, coordinator, factory) = CreateRuntime();
            var trace = new List<string>();
            var first = new TestPresentationProvider("First", trace);
            var second = new TestPresentationProvider("Second", trace)
            {
                FailOnCreate = true
            };
            var entity = new TestEntity();
            var context = default(EntityActorNoContext);
            coordinator.Register(first);
            coordinator.Register(second);
            factory.FailOnCleanup = true;

            var exception = Assert.Throws<AggregateException>
            (
                () => runtime.Create(entity, context)
            );

            Assert.AreEqual(2, exception.InnerExceptions.Count);
            Assert.IsFalse(controller.TryFindSingleActor(entity, out _));
            Assert.AreEqual(0, controller.FindActors(entity).Count);
            Assert.AreEqual
            (
                DespawnReason.SpawnRollback,
                first.LastReleaseReason
            );
            Assert.IsTrue(factory.HasReleased);
            Assert.AreEqual
            (
                DespawnReason.SpawnRollback,
                factory.LastReleaseReason
            );

            yield return null;
        }

    #endregion

    #region X-3: Presentation Release 실패 격리

        [UnityTest]
        public IEnumerator TEST_EntityActorRuntime_PresentationRelease실패에도_Actor반환을완료()
        {
            var (runtime, controller, coordinator, factory) = CreateRuntime();
            var trace = new List<string>();
            var presentation = new TestPresentationProvider("Presentation", trace);
            var entity = new TestEntity();
            var context = default(EntityActorNoContext);
            coordinator.Register(presentation);

            var actor = runtime.Create(entity, context);
            presentation.FailOnRelease = true;

            Assert.Throws<InvalidOperationException>
            (
                () => runtime.Release(actor, DespawnReason.Removed)
            );

            Assert.IsFalse(controller.ContainsActor(actor));
            Assert.IsNull(actor.Entity);
            Assert.IsTrue(factory.HasReleased);
            Assert.AreEqual(DespawnReason.Removed, factory.LastReleaseReason);

            yield return null;
        }

    #endregion

    #region X-4: Presentation + Actor 반환 복합 실패

        [UnityTest]
        public IEnumerator TEST_EntityActorRuntime_Presentation과Actor반환실패를_Aggregate()
        {
            var (runtime, controller, coordinator, factory) = CreateRuntime();
            var trace = new List<string>();
            var presentation = new TestPresentationProvider("Presentation", trace);
            var entity = new TestEntity();
            var context = default(EntityActorNoContext);
            coordinator.Register(presentation);

            var actor = runtime.Create(entity, context);
            presentation.FailOnRelease = true;
            factory.FailOnCleanup = true;

            var exception = Assert.Throws<AggregateException>
            (
                () => runtime.Release(actor, DespawnReason.Removed)
            );

            Assert.AreEqual(2, exception.InnerExceptions.Count);
            Assert.IsFalse(controller.ContainsActor(actor));
            Assert.IsNull(actor.Entity);
            Assert.IsTrue(factory.HasReleased);
            Assert.AreEqual(DespawnReason.Removed, factory.LastReleaseReason);

            yield return null;
        }

    #endregion

    }
}
