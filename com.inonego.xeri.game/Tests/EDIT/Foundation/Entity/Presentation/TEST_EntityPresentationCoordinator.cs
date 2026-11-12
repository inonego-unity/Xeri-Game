/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : TEST_EntityPresentationCoordinator.cs
수정일 : 2026-11-12

# 설명
EntityPresentationCoordinator의 Provider 정순 생성·Transfer, 역순 release·rollback 계약을 검증한다.
단순 Registry CRUD가 아니라 여러 Provider의 수명 transaction과 실패 격리를 대상으로 한다.

# 테스트 구성
 R: Registry 관계와 committed observer 알림
 L: 정상 Provider lifecycle
 T: Provider Transfer transaction
 X: Create·Release 실패 rollback과 격리
========================================================================= BLOCK_HEADER_END */

using System;
using System.Collections;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.TestTools;

using NUnit;
using NUnit.Framework;

namespace inonego.Xeri.Game.TEST._EntityPresentation
{
    using Xeri;
    using Xeri.Game;

    // ======================================================================
    /// <summary>
    /// EntityPresentationCoordinator의 Provider transaction 계약을 검증한다.
    /// </summary>
    // ======================================================================
    public class TEST_EntityPresentationCoordinator
    {

    #region 헬퍼

        // ============================================================
        /// <summary>
        /// Provider transaction에 사용하는 논리 Entity.
        /// </summary>
        // ============================================================
        private sealed class TestEntity : EntityBase
        {
            // NONE
        }

        // ============================================================
        /// <summary>
        /// Provider transaction에 사용하는 Unity Actor.
        /// </summary>
        // ============================================================
        private sealed class TestActor : EntityActorBase<TestEntity>
        {
            // NONE
        }

        // ============================================================
        /// <summary>
        /// Provider 호출 순서와 지정 단계의 실패를 관찰한다.
        /// </summary>
        // ============================================================
        private sealed class TestProvider :
            IEntityPresentationProvider<TestActor, TestEntity>
        {
            private readonly string name;
            private readonly List<string> trace;

            // ------------------------------------------------------------
            /// <summary>
            /// 생성 단계의 실패 여부.
            /// </summary>
            // ------------------------------------------------------------
            public bool FailOnCreate { get; set; }

            // ------------------------------------------------------------
            /// <summary>
            /// 반환 단계의 실패 여부.
            /// </summary>
            // ------------------------------------------------------------
            public bool FailOnRelease { get; set; }

            // ------------------------------------------------------------
            /// <summary>
            /// Entity 이전 단계의 실패 여부.
            /// </summary>
            // ------------------------------------------------------------
            public bool FailOnTransfer { get; set; }

            // ------------------------------------------------------------
            /// <summary>
            /// Entity 이전 rollback 단계의 실패 여부.
            /// </summary>
            // ------------------------------------------------------------
            public bool FailOnTransferRollback { get; set; }

            // ------------------------------------------------------------
            /// <summary>
            /// Presentation owner 이전 단계의 실패 여부.
            /// </summary>
            // ------------------------------------------------------------
            public bool FailOnOwnerTransfer { get; set; }

            // ------------------------------------------------------------
            /// <summary>
            /// Presentation owner rollback 단계의 실패 여부.
            /// </summary>
            // ------------------------------------------------------------
            public bool FailOnOwnerTransferRollback { get; set; }

            // ------------------------------------------------------------
            /// <summary>
            /// 마지막 반환에 전달된 실제 사유.
            /// </summary>
            // ------------------------------------------------------------
            public DespawnReason LastReleaseReason { get; private set; }

            // ------------------------------------------------------------
            /// <summary>
            /// Provider 호출 순서와 지정 단계의 실패를 관찰한다.
            /// </summary>
            // ------------------------------------------------------------
            public TestProvider(string name, List<string> trace)
            {
                this.name = name;
                this.trace = trace;
            }

            // ------------------------------------------------------------
            /// <summary>
            /// 생성 호출을 기록하고 지정된 실패를 전달한다.
            /// </summary>
            // ------------------------------------------------------------
            public void OnActorCreated(TestEntity entity, TestActor actor)
            {
                trace.Add($"{name}.Create");

                if (FailOnCreate)
                {
                    throw new InvalidOperationException($"{name} create failure");
                }
            }

            // ------------------------------------------------------------
            /// <summary>
            /// 반환 순서와 사유를 기록하고 지정된 실패를 전달한다.
            /// </summary>
            // ------------------------------------------------------------
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

            // ------------------------------------------------------------
            /// <summary>
            /// Entity 이전 호출을 기록하고 지정된 실패를 전달한다.
            /// </summary>
            // ------------------------------------------------------------
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

            // ------------------------------------------------------------
            /// <summary>
            /// 완료된 Entity 이전의 rollback 순서를 기록한다.
            /// </summary>
            // ------------------------------------------------------------
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

            // ------------------------------------------------------------
            /// <summary>
            /// Presentation owner 이전 순서를 기록한다.
            /// </summary>
            // ------------------------------------------------------------
            public void OnPresentationOwnerTransferred
            (
                TestActor previousOwner,
                TestActor nextOwner
            )
            {
                trace.Add($"{name}.OwnerTransfer");

                if (FailOnOwnerTransfer)
                {
                    throw new InvalidOperationException($"{name} owner transfer failure");
                }
            }

            // ------------------------------------------------------------
            /// <summary>
            /// 완료된 Presentation owner 이전의 rollback 순서를 기록한다.
            /// </summary>
            // ------------------------------------------------------------
            public void OnPresentationOwnerTransferRollback
            (
                TestActor previousOwner,
                TestActor nextOwner
            )
            {
                trace.Add($"{name}.OwnerTransferRollback");

                if (FailOnOwnerTransferRollback)
                {
                    throw new InvalidOperationException($"{name} owner transfer rollback failure");
                }
            }
        }

    #endregion

    #region 픽스처

        private readonly List<GameObject> actorObjects = new();

        // ------------------------------------------------------------
        /// <summary>
        /// 각 테스트에서 생성한 Unity Actor를 폐기한다.
        /// </summary>
        // ------------------------------------------------------------
        [TearDown]
        public void TearDown()
        {
            for (var index = 0; index < actorObjects.Count; index++)
            {
                if (actorObjects[index] != null)
                {
                    UnityEngine.Object.DestroyImmediate(actorObjects[index]);
                }
            }

            actorObjects.Clear();
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Provider 호출을 검증할 테스트 Actor를 생성한다.
        /// </summary>
        // ------------------------------------------------------------
        private TestActor CreateActor()
        {
            var actorObject = new GameObject("TestActor");
            actorObjects.Add(actorObject);
            return actorObject.AddComponent<TestActor>();
        }

    #endregion

    #region R-1: Presentation Registry Detached / Bound

        // ------------------------------------------------------------
        /// <summary>
        /// 객체 등록과 owner 관계의 독립적인 전이를 검증한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_EntityPresentationRegistry_Detached_Attach_Detach_Rebind_수명을_분리()
        {
            var registry = new EntityPresentationRegistry<object, object>();
            var firstOwner = new object();
            var secondOwner = new object();
            var presentation = new object();
            var trace = new List<string>();

            registry.OnAttach += (_, e) =>
                trace.Add(ReferenceEquals(e.Owner, firstOwner) ? "Attach.First" : "Attach.Second");
            registry.OnDetach += (_, e) =>
                trace.Add(ReferenceEquals(e.Owner, firstOwner) ? "Detach.First" : "Detach.Second");

            registry.Register(presentation);

            Assert.IsTrue(registry.Contains(presentation));
            Assert.IsFalse(registry.IsBound(presentation));
            Assert.AreEqual(1, registry.Count);
            Assert.AreEqual(1, new List<object>(registry.DetachedPresentations).Count);

            registry.Attach(firstOwner, presentation);

            Assert.IsTrue(registry.IsBound(presentation));
            Assert.AreEqual(1, registry.FindAll(firstOwner).Count);
            Assert.AreEqual(0, new List<object>(registry.DetachedPresentations).Count);

            Assert.IsTrue(registry.Detach(presentation));
            Assert.IsTrue(registry.Contains(presentation));
            Assert.IsFalse(registry.IsBound(presentation));

            registry.Attach(firstOwner, presentation);
            Assert.IsTrue(registry.Rebind(presentation, secondOwner));

            Assert.AreEqual(0, registry.FindAll(firstOwner).Count);
            Assert.AreEqual(1, registry.FindAll(secondOwner).Count);
            Assert.IsTrue(registry.TryGetOwner(presentation, out var owner));
            Assert.AreSame(secondOwner, owner);

            Assert.IsTrue(registry.Detach(presentation));
            Assert.IsTrue(registry.Unregister(presentation));
            Assert.IsFalse(registry.Contains(presentation));

            CollectionAssert.AreEqual
            (
                new[]
                {
                    "Attach.First",
                    "Detach.First",
                    "Attach.First",
                    "Detach.First",
                    "Attach.Second",
                    "Detach.Second"
                },
                trace
            );
        }

    #endregion

    #region R-2: Registry observer failure 격리

        // ------------------------------------------------------------
        /// <summary>
        /// Clear observer 실패가 다른 소비자의 완료 정리를 막지 않게 한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_EntityPresentationRegistry_ClearObserver실패에도_정리와후속Observer를유지()
        {
            var registry = new EntityPresentationRegistry<object, object>();
            var owner = new object();
            var bound = new object();
            var detached = new object();
            registry.Register(bound);
            registry.Register(detached);
            registry.Attach(owner, bound);
            var observed = 0;
            registry.OnClear += (_, _) => throw new InvalidOperationException("clear observer failure");
            registry.OnClear += (_, _) => observed++;

            LogAssert.Expect(LogType.Exception, "InvalidOperationException: clear observer failure");
            registry.Clear();

            Assert.IsFalse(registry.Contains(bound));
            Assert.IsFalse(registry.Contains(detached));
            Assert.IsFalse(registry.TryGetOwner(bound, out _));
            Assert.AreEqual(0, registry.FindAll(owner).Count);
            Assert.AreEqual(1, observed);
            registry.Clear();
            Assert.AreEqual(1, observed);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Attach observer 실패에도 확정된 관계와 후속 알림을 유지한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_EntityPresentationRegistry_AttachObserver실패에도_BoundCommit과후속Observer를유지()
        {
            var registry = new EntityPresentationRegistry<object, object>();
            var owner = new object();
            var presentation = new object();
            var observed = 0;

            registry.Register(presentation);
            registry.OnAttach += (_, _) =>
                throw new InvalidOperationException("attach observer failure");
            registry.OnAttach += (_, _) => observed++;

            LogAssert.Expect(LogType.Exception, "InvalidOperationException: attach observer failure");
            registry.Attach(owner, presentation);

            Assert.IsTrue(registry.IsBound(presentation));
            Assert.IsTrue(registry.TryGetOwner(presentation, out var currentOwner));
            Assert.AreSame(owner, currentOwner);
            Assert.AreEqual(1, observed);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Detach observer 실패에도 관계 종료와 후속 알림을 유지한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_EntityPresentationRegistry_DetachObserver실패에도_DetachedCommit과후속Observer를유지()
        {
            var registry = new EntityPresentationRegistry<object, object>();
            var owner = new object();
            var presentation = new object();
            var observed = 0;

            registry.Register(presentation);
            registry.Attach(owner, presentation);
            registry.OnDetach += (_, _) =>
                throw new InvalidOperationException("detach observer failure");
            registry.OnDetach += (_, _) => observed++;

            LogAssert.Expect(LogType.Exception, "InvalidOperationException: detach observer failure");
            Assert.IsTrue(registry.Detach(presentation));

            Assert.IsFalse(registry.IsBound(presentation));
            Assert.IsFalse(registry.TryGetOwner(presentation, out _));
            Assert.AreEqual(1, observed);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Rebind observer 실패에도 새 owner 관계와 후속 알림을 유지한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_EntityPresentationRegistry_RebindObserver실패에도_NextOwnerCommit과후속Observer를유지()
        {
            var registry = new EntityPresentationRegistry<object, object>();
            var previousOwner = new object();
            var nextOwner = new object();
            var presentation = new object();
            var detachObserved = 0;
            var attachObserved = 0;

            registry.Register(presentation);
            registry.Attach(previousOwner, presentation);
            registry.OnDetach += (_, _) =>
                throw new InvalidOperationException("rebind detach observer failure");
            registry.OnDetach += (_, _) => detachObserved++;
            registry.OnAttach += (_, _) =>
                throw new InvalidOperationException("rebind attach observer failure");
            registry.OnAttach += (_, _) => attachObserved++;

            LogAssert.Expect(LogType.Exception, "InvalidOperationException: rebind detach observer failure");
            LogAssert.Expect(LogType.Exception, "InvalidOperationException: rebind attach observer failure");
            Assert.IsTrue(registry.Rebind(presentation, nextOwner));

            Assert.AreEqual(0, registry.FindAll(previousOwner).Count);
            Assert.AreEqual(1, registry.FindAll(nextOwner).Count);
            Assert.IsTrue(registry.TryGetOwner(presentation, out var currentOwner));
            Assert.AreSame(nextOwner, currentOwner);
            Assert.AreEqual(1, detachObserved);
            Assert.AreEqual(1, attachObserved);
        }

    #endregion

    #region L-1: 정상 Provider lifecycle

        // ------------------------------------------------------------
        /// <summary>
        /// Provider 생성과 반환의 대칭 실행 순서를 검증한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_EntityPresentationCoordinator_Provider를_정순Create하고_역순Release()
        {
            var trace = new List<string>();
            var coordinator =
                new EntityPresentationCoordinator<TestActor, TestEntity>();
            var entity = new TestEntity();
            var actor = CreateActor();

            coordinator.Register(new TestProvider("First", trace));
            coordinator.Register(new TestProvider("Second", trace));

            coordinator.CreateForActor(entity, actor);
            coordinator.Release(entity, actor, DespawnReason.Removed);

            CollectionAssert.AreEqual
            (
                new[]
                {
                    "First.Create",
                    "Second.Create",
                    "Second.Release",
                    "First.Release"
                },
                trace
            );
        }

    #endregion

    #region T-1: Transfer 실패 rollback

        // ------------------------------------------------------------
        /// <summary>
        /// Entity 이전 실패 시 완료 Provider의 역순 복구를 검증한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_EntityPresentationCoordinator_Transfer실패시_완료Provider를_역순Rollback()
        {
            var trace = new List<string>();
            var coordinator =
                new EntityPresentationCoordinator<TestActor, TestEntity>();
            var previous = new TestEntity();
            var next = new TestEntity();
            var actor = CreateActor();
            var first = new TestProvider("First", trace);
            var second = new TestProvider("Second", trace)
            {
                FailOnTransfer = true
            };
            var third = new TestProvider("Third", trace);

            coordinator.Register(first);
            coordinator.Register(second);
            coordinator.Register(third);

            Assert.Throws<InvalidOperationException>
            (
                () => coordinator.Transfer(previous, next, actor)
            );

            CollectionAssert.AreEqual
            (
                new[]
                {
                    "First.Transfer",
                    "Second.Transfer",
                    "First.TransferRollback"
                },
                trace
            );
        }

    #endregion

    #region T-2: Presentation Owner Transfer 실패 rollback

        // ----------------------------------------------------------------------
        /// <summary>
        /// Presentation owner 이전 실패 시 완료 Provider의 역순 복구를 검증한다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_EntityPresentationCoordinator_OwnerTransfer실패시_완료Provider를_역순Rollback()
        {
            var trace = new List<string>();
            var coordinator =
                new EntityPresentationCoordinator<TestActor, TestEntity>();
            var previousOwner = CreateActor();
            var nextOwner = CreateActor();
            var first = new TestProvider("First", trace);
            var second = new TestProvider("Second", trace)
            {
                FailOnOwnerTransfer = true
            };
            var third = new TestProvider("Third", trace);

            coordinator.Register(first);
            coordinator.Register(second);
            coordinator.Register(third);

            Assert.Throws<InvalidOperationException>
            (
                () => coordinator.TransferOwner(previousOwner, nextOwner)
            );

            CollectionAssert.AreEqual
            (
                new[]
                {
                    "First.OwnerTransfer",
                    "Second.OwnerTransfer",
                    "First.OwnerTransferRollback"
                },
                trace
            );
        }

    #endregion

    #region X-1: Create 실패 rollback

        // ------------------------------------------------------------
        /// <summary>
        /// 생성 실패 시 완료된 Provider만 반환하는 계약을 검증한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_EntityPresentationCoordinator_Create실패시_완료Provider만_역순Rollback()
        {
            var trace = new List<string>();
            var coordinator =
                new EntityPresentationCoordinator<TestActor, TestEntity>();
            var entity = new TestEntity();
            var actor = CreateActor();
            var first = new TestProvider("First", trace);
            var second = new TestProvider("Second", trace)
            {
                FailOnCreate = true
            };
            var third = new TestProvider("Third", trace);

            coordinator.Register(first);
            coordinator.Register(second);
            coordinator.Register(third);

            Assert.Throws<InvalidOperationException>
            (
                () => coordinator.CreateForActor(entity, actor)
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
            Assert.AreEqual
            (
                DespawnReason.SpawnRollback,
                first.LastReleaseReason
            );
        }

    #endregion

    #region X-2: Release 실패 격리

        // ------------------------------------------------------------
        /// <summary>
        /// Provider 반환 실패에도 나머지 소유권 정리를 계속한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_EntityPresentationCoordinator_Release실패에도_나머지Provider정리를_계속()
        {
            var trace = new List<string>();
            var coordinator =
                new EntityPresentationCoordinator<TestActor, TestEntity>();
            var entity = new TestEntity();
            var actor = CreateActor();
            var first = new TestProvider("First", trace);
            var second = new TestProvider("Second", trace)
            {
                FailOnRelease = true
            };
            var third = new TestProvider("Third", trace);

            coordinator.Register(first);
            coordinator.Register(second);
            coordinator.Register(third);
            coordinator.CreateForActor(entity, actor);
            trace.Clear();

            Assert.Throws<InvalidOperationException>
            (
                () => coordinator.Release(entity, actor, DespawnReason.Removed)
            );

            CollectionAssert.AreEqual
            (
                new[]
                {
                    "Third.Release",
                    "Second.Release",
                    "First.Release"
                },
                trace
            );
        }

    #endregion

    }
}
