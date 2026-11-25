/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : TEST_EntityPresentationCoordinator.cs
수정일 : 2026-11-16

# 설명
EntityPresentationCoordinator의 Provider 정순 생성·Rebind, 역순 release·rollback 계약을 검증한다.
단순 Registry CRUD가 아니라 여러 Provider의 수명 transaction과 실패 격리를 대상으로 한다.

# 테스트 구성
 R: Registry 관계와 committed handler 알림
 L: 정상 Provider lifecycle
 T: Provider Rebind transaction
 X: Create·Release 실패 rollback과 격리
 P: 활성 Actor의 Provider 조립 불변성
 C: Actor 단위 Provider 수명 재진입과 다른 Actor 수명 허용
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
        private sealed class EntitySample : EntityBase
        {
            // NONE
        }

        // ============================================================
        /// <summary>
        /// Provider transaction에 사용하는 Unity Actor.
        /// </summary>
        // ============================================================
        private sealed class ActorProbe : EntityActorBase<EntitySample>
        {
            // NONE
        }

        // ============================================================
        /// <summary>
        /// Provider 호출 순서와 지정 단계의 실패를 관찰한다.
        /// </summary>
        // ============================================================
        private sealed class PresentationProviderProbe :
            IEntityPresentationProvider<ActorProbe, EntitySample>
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
            /// 생성 Provider 호출 시 수행할 검사.
            /// </summary>
            // ------------------------------------------------------------
            public Action OnCreateAction { get; set; }

            // ------------------------------------------------------------
            /// <summary>
            /// 반환 단계의 실패 여부.
            /// </summary>
            // ------------------------------------------------------------
            public bool FailOnRelease { get; set; }

            // ------------------------------------------------------------
            /// <summary>
            /// 반환 Provider 호출 시 수행할 검사.
            /// </summary>
            // ------------------------------------------------------------
            public Action OnReleaseAction { get; set; }

            // ------------------------------------------------------------
            /// <summary>
            /// Entity 이전 단계의 실패 여부.
            /// </summary>
            // ------------------------------------------------------------
            public bool FailOnRebind { get; set; }

            // ------------------------------------------------------------
            /// <summary>
            /// Entity 이전 rollback 단계의 실패 여부.
            /// </summary>
            // ------------------------------------------------------------
            public bool FailOnRebindRollback { get; set; }

            // ------------------------------------------------------------
            /// <summary>
            /// Presentation owner 이전 단계의 실패 여부.
            /// </summary>
            // ------------------------------------------------------------
            public bool FailOnOwnerRebind { get; set; }

            // ------------------------------------------------------------
            /// <summary>
            /// Presentation owner rollback 단계의 실패 여부.
            /// </summary>
            // ------------------------------------------------------------
            public bool FailOnOwnerRebindRollback { get; set; }

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
            public PresentationProviderProbe(string name, List<string> trace)
            {
                this.name = name;
                this.trace = trace;
            }

            // ------------------------------------------------------------
            /// <summary>
            /// 생성 호출을 기록하고 지정된 실패를 전달한다.
            /// </summary>
            // ------------------------------------------------------------
            public void OnActorCreated(EntitySample entity, ActorProbe actor)
            {
                trace.Add($"{name}.Create");
                OnCreateAction?.Invoke();

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
                EntitySample entity,
                ActorProbe actor,
                DespawnReason reason
            )
            {
                LastReleaseReason = reason;
                trace.Add($"{name}.Release");
                OnReleaseAction?.Invoke();

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
            public void OnActorRebound
            (
                EntitySample previousEntity,
                EntitySample nextEntity,
                ActorProbe actor
            )
            {
                trace.Add($"{name}.Rebind");

                if (FailOnRebind)
                {
                    throw new InvalidOperationException($"{name} transfer failure");
                }
            }

            // ------------------------------------------------------------
            /// <summary>
            /// 완료된 Entity 이전의 rollback 순서를 기록한다.
            /// </summary>
            // ------------------------------------------------------------
            public void OnActorRebindRollback
            (
                EntitySample previousEntity,
                EntitySample nextEntity,
                ActorProbe actor
            )
            {
                trace.Add($"{name}.RebindRollback");

                if (FailOnRebindRollback)
                {
                    throw new InvalidOperationException($"{name} transfer rollback failure");
                }
            }

            // ------------------------------------------------------------
            /// <summary>
            /// Presentation owner 이전 순서를 기록한다.
            /// </summary>
            // ------------------------------------------------------------
            public void OnPresentationOwnerRebound
            (
                ActorProbe previousOwner,
                ActorProbe nextOwner
            )
            {
                trace.Add($"{name}.OwnerRebind");

                if (FailOnOwnerRebind)
                {
                    throw new InvalidOperationException($"{name} owner transfer failure");
                }
            }

            // ------------------------------------------------------------
            /// <summary>
            /// 완료된 Presentation owner 이전의 rollback 순서를 기록한다.
            /// </summary>
            // ------------------------------------------------------------
            public void OnPresentationOwnerRebindRollback
            (
                ActorProbe previousOwner,
                ActorProbe nextOwner
            )
            {
                trace.Add($"{name}.OwnerRebindRollback");

                if (FailOnOwnerRebindRollback)
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
        private ActorProbe CreateActor()
        {
            var actorObject = new GameObject("ActorProbe");
            actorObjects.Add(actorObject);
            return actorObject.AddComponent<ActorProbe>();
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

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> Owner별 목록과 전체 Presentation View를 복사 없이 재사용하며
        /// <br/> 외부 수정과 종료된 Owner View의 재사용을 차단한다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_EntityPresentationRegistry_조회View는_재사용되고_내부수정이차단됨()
        {
            var registry = new EntityPresentationRegistry<object, object>();
            var owner = new object();
            var nextOwner = new object();
            var first = new object();
            var second = new object();

            // 전체 View는 값 타입이라 접근마다 새로 만들지만 같은 원본을 그대로 보여 준다.
            var all = registry.Presentations;
            Assert.IsFalse(((object)all) is HashSet<object>);

            registry.Register(first);
            registry.Register(second);

            Assert.AreEqual(2, all.Count);

            registry.Attach(owner, first);
            var ownerView = registry.FindAll(owner);

            Assert.AreSame(ownerView, registry.FindAll(owner));
            Assert.IsFalse(ownerView is List<object>);
            Assert.Throws<NotSupportedException>
            (
                () => ((IList<object>)ownerView).Clear()
            );

            registry.Attach(owner, second);

            Assert.AreSame(ownerView, registry.FindAll(owner));
            Assert.AreEqual(2, ownerView.Count);
            Assert.AreSame(first, ownerView[0]);
            Assert.AreSame(second, ownerView[1]);

            Assert.IsTrue(registry.Detach(first));
            Assert.AreEqual(1, ownerView.Count);

            Assert.IsTrue(registry.Rebind(second, nextOwner));
            Assert.AreEqual(0, ownerView.Count);

            var nextView = registry.FindAll(nextOwner);
            Assert.AreSame(nextView, registry.FindAll(nextOwner));
            Assert.AreEqual(1, nextView.Count);

            registry.Clear();

            Assert.AreEqual(0, all.Count);
            Assert.AreEqual(0, ownerView.Count);
            Assert.AreEqual(0, nextView.Count);

            registry.Register(first);
            registry.Attach(owner, first);

            Assert.AreEqual(1, all.Count);
            Assert.AreEqual(0, ownerView.Count);
            Assert.AreNotSame(ownerView, registry.FindAll(owner));
        }

    #endregion

    #region R-2: Registry handler failure 격리

        // ------------------------------------------------------------
        /// <summary>
        /// Clear handler 실패가 다른 소비자의 완료 정리를 막지 않게 한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_EntityPresentationRegistry_ClearHandler실패에도_정리와후속Handler를유지()
        {
            var registry = new EntityPresentationRegistry<object, object>();
            var owner = new object();
            var bound = new object();
            var detached = new object();
            registry.Register(bound);
            registry.Register(detached);
            registry.Attach(owner, bound);
            var observed = 0;
            registry.OnClear += (_, _) => throw new InvalidOperationException("clear handler failure");
            registry.OnClear += (_, _) => observed++;

            LogAssert.Expect(LogType.Exception, "InvalidOperationException: clear handler failure");
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
        /// Attach handler 실패에도 확정된 관계와 후속 알림을 유지한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_EntityPresentationRegistry_AttachHandler실패에도_BoundCommit과후속Handler를유지()
        {
            var registry = new EntityPresentationRegistry<object, object>();
            var owner = new object();
            var presentation = new object();
            var observed = 0;

            registry.Register(presentation);
            registry.OnAttach += (_, _) =>
                throw new InvalidOperationException("attach handler failure");
            registry.OnAttach += (_, _) => observed++;

            LogAssert.Expect(LogType.Exception, "InvalidOperationException: attach handler failure");
            registry.Attach(owner, presentation);

            Assert.IsTrue(registry.IsBound(presentation));
            Assert.IsTrue(registry.TryGetOwner(presentation, out var currentOwner));
            Assert.AreSame(owner, currentOwner);
            Assert.AreEqual(1, observed);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Detach handler 실패에도 관계 종료와 후속 알림을 유지한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_EntityPresentationRegistry_DetachHandler실패에도_DetachedCommit과후속Handler를유지()
        {
            var registry = new EntityPresentationRegistry<object, object>();
            var owner = new object();
            var presentation = new object();
            var observed = 0;

            registry.Register(presentation);
            registry.Attach(owner, presentation);
            registry.OnDetach += (_, _) =>
                throw new InvalidOperationException("detach handler failure");
            registry.OnDetach += (_, _) => observed++;

            LogAssert.Expect(LogType.Exception, "InvalidOperationException: detach handler failure");
            Assert.IsTrue(registry.Detach(presentation));

            Assert.IsFalse(registry.IsBound(presentation));
            Assert.IsFalse(registry.TryGetOwner(presentation, out _));
            Assert.AreEqual(1, observed);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Rebind handler 실패에도 새 owner 관계와 후속 알림을 유지한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_EntityPresentationRegistry_RebindHandler실패에도_NextOwnerCommit과후속Handler를유지()
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
                throw new InvalidOperationException("rebind detach handler failure");
            registry.OnDetach += (_, _) => detachObserved++;
            registry.OnAttach += (_, _) =>
                throw new InvalidOperationException("rebind attach handler failure");
            registry.OnAttach += (_, _) => attachObserved++;

            LogAssert.Expect(LogType.Exception, "InvalidOperationException: rebind detach handler failure");
            LogAssert.Expect(LogType.Exception, "InvalidOperationException: rebind attach handler failure");
            Assert.IsTrue(registry.Rebind(presentation, nextOwner));

            Assert.AreEqual(0, registry.FindAll(previousOwner).Count);
            Assert.AreEqual(1, registry.FindAll(nextOwner).Count);
            Assert.IsTrue(registry.TryGetOwner(presentation, out var currentOwner));
            Assert.AreSame(nextOwner, currentOwner);
            Assert.AreEqual(1, detachObserved);
            Assert.AreEqual(1, attachObserved);
        }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/> Rebind handler가 같은 Registry 상태를 변경해 알림을 무효화하지 못한다.
        /// <br/> Detach 다음 Attach 알림까지 확정된 next Owner 관계를 유지한다.
        /// </summary>
        // --------------------------------------------------------------------------------
        [Test]
        public void TEST_EntityPresentationRegistry_Rebind알림중_재진입변경을거절()
        {
            var registry = new EntityPresentationRegistry<object, object>();
            var previousOwner = new object();
            var nextOwner = new object();
            var presentation = new object();
            var trace = new List<string>();

            registry.Register(presentation);
            registry.Attach(previousOwner, presentation);

            registry.OnDetach += (_, _) =>
            {
                trace.Add("Detach");

                Assert.Throws<InvalidOperationException>
                (
                    () => registry.Detach(presentation)
                );
                Assert.Throws<InvalidOperationException>
                (
                    () => registry.Rebind(presentation, previousOwner)
                );
                Assert.Throws<InvalidOperationException>
                (
                    () => registry.Clear()
                );
            };

            registry.OnAttach += (_, _) =>
            {
                trace.Add("Attach");
                Assert.IsTrue(registry.TryGetOwner(presentation, out var owner));
                Assert.AreSame(nextOwner, owner);

                Assert.Throws<InvalidOperationException>
                (
                    () => registry.Unregister(presentation)
                );
            };

            Assert.IsTrue(registry.Rebind(presentation, nextOwner));

            CollectionAssert.AreEqual
            (
                new[]
                {
                    "Detach",
                    "Attach"
                },
                trace
            );

            Assert.AreEqual(0, registry.FindAll(previousOwner).Count);
            Assert.AreEqual(1, registry.FindAll(nextOwner).Count);
            Assert.IsTrue(registry.Detach(presentation));
            Assert.IsTrue(registry.Unregister(presentation));
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
                new EntityPresentationCoordinator<ActorProbe, EntitySample>();
            var entity = new EntitySample();
            var actor = CreateActor();

            coordinator.AddProvider(new PresentationProviderProbe("First", trace));
            coordinator.AddProvider(new PresentationProviderProbe("Second", trace));

            coordinator.Create(actor, entity);
            coordinator.Release(actor, entity, DespawnReason.Removed);

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

    #region T-1: Rebind 실패 rollback

        // ------------------------------------------------------------
        /// <summary>
        /// Entity 이전 실패 시 완료 Provider의 역순 복구를 검증한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_EntityPresentationCoordinator_Rebind실패시_완료Provider를_역순Rollback()
        {
            var trace = new List<string>();
            var coordinator =
                new EntityPresentationCoordinator<ActorProbe, EntitySample>();
            var previous = new EntitySample();
            var next = new EntitySample();
            var actor = CreateActor();
            var first = new PresentationProviderProbe("First", trace);
            var second = new PresentationProviderProbe("Second", trace)
            {
                FailOnRebind = true
            };
            var third = new PresentationProviderProbe("Third", trace);

            coordinator.AddProvider(first);
            coordinator.AddProvider(second);
            coordinator.AddProvider(third);
            coordinator.Create(actor, previous);
            trace.Clear();

            Assert.Throws<InvalidOperationException>
            (
                () => coordinator.RebindEntity(actor, previous, next)
            );

            CollectionAssert.AreEqual
            (
                new[]
                {
                    "First.Rebind",
                    "Second.Rebind",
                    "Second.RebindRollback",
                    "First.RebindRollback"
                },
                trace
            );
        }

    #endregion

    #region T-2: Presentation Owner Rebind 실패 rollback

        // ----------------------------------------------------------------------
        /// <summary>
        /// Presentation owner 이전 실패 시 완료 Provider의 역순 복구를 검증한다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_EntityPresentationCoordinator_OwnerRebind실패시_완료Provider를_역순Rollback()
        {
            var trace = new List<string>();
            var coordinator =
                new EntityPresentationCoordinator<ActorProbe, EntitySample>();
            var previousOwner = CreateActor();
            var nextOwner = CreateActor();
            var first = new PresentationProviderProbe("First", trace);
            var second = new PresentationProviderProbe("Second", trace)
            {
                FailOnOwnerRebind = true
            };
            var third = new PresentationProviderProbe("Third", trace);

            coordinator.AddProvider(first);
            coordinator.AddProvider(second);
            coordinator.AddProvider(third);
            var entity = new EntitySample();
            coordinator.Create(previousOwner, entity);
            trace.Clear();

            Assert.Throws<InvalidOperationException>
            (
                () => coordinator.RebindOwner(previousOwner, nextOwner)
            );

            CollectionAssert.AreEqual
            (
                new[]
                {
                    "First.OwnerRebind",
                    "Second.OwnerRebind",
                    "Second.OwnerRebindRollback",
                    "First.OwnerRebindRollback"
                },
                trace
            );

            // 실패한 Rebind는 활성 소유자를 옮기지 않는다.
            trace.Clear();
            coordinator.Release(nextOwner, entity, DespawnReason.Removed);
            Assert.AreEqual(0, trace.Count);
            coordinator.Release(previousOwner, entity, DespawnReason.Removed);
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

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> 실패한 Provider 자신의 Rebind Rollback도 실행하고
        /// <br/> 최초 오류 및 모든 Rollback 오류를 함께 전달한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_EntityPresentationCoordinator_Rebind자체실패와_Rollback오류를_집계()
        {
            var trace = new List<string>();
            var coordinator = new EntityPresentationCoordinator<ActorProbe, EntitySample>();
            var actor = CreateActor();
            var first = new PresentationProviderProbe("First", trace)
            {
                FailOnRebindRollback = true
            };
            var second = new PresentationProviderProbe("Second", trace)
            {
                FailOnRebind = true,
                FailOnRebindRollback = true
            };
            coordinator.AddProvider(first);
            coordinator.AddProvider(second);
            coordinator.Create(actor, new EntitySample());
            trace.Clear();

            var error = Assert.Throws<AggregateException>
            (
                () => coordinator.RebindEntity(actor, new EntitySample(), new EntitySample())
            );

            Assert.AreEqual(3, error.InnerExceptions.Count);
            CollectionAssert.AreEqual
            (
                new[]
                {
                    "First.Rebind",
                    "Second.Rebind",
                    "Second.RebindRollback",
                    "First.RebindRollback"
                },
                trace
            );
        }

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> Owner Rebind 실패에도 실패한 Provider부터 역순 복구하고
        /// <br/> 원본 오류와 Rollback 오류를 집계한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_EntityPresentationCoordinator_OwnerRebind실패와_Rollback오류를_집계()
        {
            var trace = new List<string>();
            var coordinator = new EntityPresentationCoordinator<ActorProbe, EntitySample>();
            var first = new PresentationProviderProbe("First", trace);
            var second = new PresentationProviderProbe("Second", trace)
            {
                FailOnOwnerRebind = true,
                FailOnOwnerRebindRollback = true
            };
            coordinator.AddProvider(first);
            coordinator.AddProvider(second);
            var previousOwner = CreateActor();
            var nextOwner = CreateActor();
            coordinator.Create(previousOwner, new EntitySample());
            trace.Clear();

            var error = Assert.Throws<AggregateException>
            (
                () => coordinator.RebindOwner(previousOwner, nextOwner)
            );

            Assert.AreEqual(2, error.InnerExceptions.Count);
            CollectionAssert.AreEqual
            (
                new[]
                {
                    "First.OwnerRebind",
                    "Second.OwnerRebind",
                    "Second.OwnerRebindRollback",
                    "First.OwnerRebindRollback"
                },
                trace
            );
        }

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> 소유자 Rebind가 성공하면 등록 수명을 새 Owner로 이동시키고,
        /// <br/> 새 Owner 반환에서만 Presentation을 정리한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_EntityPresentationCoordinator_OwnerRebind완료후_새Owner만반환()
        {
            var trace = new List<string>();
            var coordinator = new EntityPresentationCoordinator<ActorProbe, EntitySample>();
            var entity = new EntitySample();
            var previousOwner = CreateActor();
            var nextOwner = CreateActor();
            var first = new PresentationProviderProbe("First", trace);
            var second = new PresentationProviderProbe("Second", trace);
            coordinator.AddProvider(first);
            coordinator.AddProvider(second);

            coordinator.Create(previousOwner, entity);
            trace.Clear();

            coordinator.RebindOwner(previousOwner, nextOwner);

            CollectionAssert.AreEqual
            (
                new[]
                {
                    "First.OwnerRebind",
                    "Second.OwnerRebind"
                },
                trace
            );

            trace.Clear();
            coordinator.Release(previousOwner, entity, DespawnReason.Removed);
            Assert.AreEqual(0, trace.Count);

            coordinator.Release(nextOwner, entity, DespawnReason.Removed);

            CollectionAssert.AreEqual
            (
                new[]
                {
                    "Second.Release",
                    "First.Release"
                },
                trace
            );
            Assert.IsTrue(coordinator.RemoveProvider(first));
            Assert.IsTrue(coordinator.RemoveProvider(second));
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// 활성 수명 없는 Actor나 이미 소유 중인 Actor의 Owner Rebind를 거절한다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_EntityPresentationCoordinator_Rebind는_유효한Owner수명만허용()
        {
            var trace = new List<string>();
            var coordinator = new EntityPresentationCoordinator<ActorProbe, EntitySample>();
            var entity = new EntitySample();
            var previousOwner = CreateActor();
            var nextOwner = CreateActor();
            var unrelatedOwner = CreateActor();
            coordinator.AddProvider(new PresentationProviderProbe("Visual", trace));

            Assert.Throws<InvalidOperationException>
            (
                () => coordinator.RebindOwner(previousOwner, nextOwner)
            );
            Assert.Throws<InvalidOperationException>
            (
                () => coordinator.RebindEntity(previousOwner, entity, new EntitySample())
            );

            coordinator.Create(previousOwner, entity);
            coordinator.Create(nextOwner, entity);
            trace.Clear();

            Assert.Throws<InvalidOperationException>
            (
                () => coordinator.RebindOwner(previousOwner, nextOwner)
            );

            coordinator.RebindOwner(previousOwner, previousOwner);
            Assert.AreEqual(0, trace.Count);

            coordinator.RebindOwner(previousOwner, unrelatedOwner);
            CollectionAssert.AreEqual
            (
                new[]
                {
                    "Visual.OwnerRebind"
                },
                trace
            );

            trace.Clear();
            coordinator.Release(previousOwner, entity, DespawnReason.Removed);
            Assert.AreEqual(0, trace.Count);
            coordinator.Release(unrelatedOwner, entity, DespawnReason.Removed);
            coordinator.Release(nextOwner, entity, DespawnReason.Removed);
            CollectionAssert.AreEqual
            (
                new[]
                {
                    "Visual.Release",
                    "Visual.Release"
                },
                trace
            );
        }

    #endregion

    #region X-1: Create 실패 rollback

        // ------------------------------------------------------------
        /// <summary>
        /// 생성에 실패한 Provider까지 역순 반환하는 계약을 검증한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_EntityPresentationCoordinator_Create실패시_실패Provider까지_역순Rollback()
        {
            var trace = new List<string>();
            var coordinator =
                new EntityPresentationCoordinator<ActorProbe, EntitySample>();
            var entity = new EntitySample();
            var actor = CreateActor();
            var first = new PresentationProviderProbe("First", trace);
            var second = new PresentationProviderProbe("Second", trace)
            {
                FailOnCreate = true
            };
            var third = new PresentationProviderProbe("Third", trace);

            coordinator.AddProvider(first);
            coordinator.AddProvider(second);
            coordinator.AddProvider(third);

            Assert.Throws<InvalidOperationException>
            (
                () => coordinator.Create(actor, entity)
            );

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
            Assert.AreEqual
            (
                DespawnReason.SpawnRollback,
                first.LastReleaseReason
            );
            Assert.AreEqual
            (
                DespawnReason.SpawnRollback,
                second.LastReleaseReason
            );
        }

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> Provider가 부분 생성에 실패해도
        /// <br/> Rollback으로 자원을 정리한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_EntityPresentationCoordinator_부분생성실패도_실패Provider를_정리()
        {
            var trace = new List<string>();
            var coordinator = new EntityPresentationCoordinator<ActorProbe, EntitySample>();
            var entity = new EntitySample();
            var actor = CreateActor();
            var hasResource = false;
            var failed = new PresentationProviderProbe("Partial", trace)
            {
                FailOnCreate = true,
                OnCreateAction = () => hasResource = true,
                OnReleaseAction = () =>
                {
                    Assert.IsTrue(hasResource);
                    hasResource = false;
                }
            };

            coordinator.AddProvider(failed);

            var error = Assert.Throws<InvalidOperationException>
            (
                () => coordinator.Create(actor, entity)
            );

            Assert.AreEqual("Partial create failure", error.Message);
            Assert.IsFalse(hasResource);
            CollectionAssert.AreEqual
            (
                new[]
                {
                    "Partial.Create",
                    "Partial.Release"
                },
                trace
            );
            Assert.AreEqual(DespawnReason.SpawnRollback, failed.LastReleaseReason);
            Assert.IsTrue(coordinator.RemoveProvider(failed));
        }

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> 생성·반환 오류를 함께 보존하고
        /// <br/> 다른 Provider의 반환도 계속한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_EntityPresentationCoordinator_부분생성과_반환실패를_모두집계()
        {
            var trace = new List<string>();
            var coordinator = new EntityPresentationCoordinator<ActorProbe, EntitySample>();
            var entity = new EntitySample();
            var actor = CreateActor();
            var first = new PresentationProviderProbe("First", trace)
            {
                FailOnRelease = true
            };
            var second = new PresentationProviderProbe("Second", trace)
            {
                FailOnCreate = true,
                FailOnRelease = true
            };
            var third = new PresentationProviderProbe("Third", trace);
            coordinator.AddProvider(first);
            coordinator.AddProvider(second);
            coordinator.AddProvider(third);

            var error = Assert.Throws<AggregateException>
            (
                () => coordinator.Create(actor, entity)
            );

            Assert.AreEqual(3, error.InnerExceptions.Count);
            Assert.AreEqual("Second create failure", error.InnerExceptions[0].Message);
            Assert.AreEqual("Second release failure", error.InnerExceptions[1].Message);
            Assert.AreEqual("First release failure", error.InnerExceptions[2].Message);

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

            coordinator.Release(actor, entity, DespawnReason.Removed);
            Assert.AreEqual(2, trace.FindAll(item => item.EndsWith(".Release")).Count);
            Assert.IsTrue(coordinator.RemoveProvider(first));
            Assert.IsTrue(coordinator.RemoveProvider(second));
            Assert.IsTrue(coordinator.RemoveProvider(third));
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
                new EntityPresentationCoordinator<ActorProbe, EntitySample>();
            var entity = new EntitySample();
            var actor = CreateActor();
            var first = new PresentationProviderProbe("First", trace);
            var second = new PresentationProviderProbe("Second", trace)
            {
                FailOnRelease = true
            };
            var third = new PresentationProviderProbe("Third", trace);

            coordinator.AddProvider(first);
            coordinator.AddProvider(second);
            coordinator.AddProvider(third);
            coordinator.Create(actor, entity);
            trace.Clear();

            Assert.Throws<InvalidOperationException>
            (
                () => coordinator.Release(actor, entity, DespawnReason.Removed)
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

    #region P-1: 활성 Actor의 Provider 구성 불변성

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> 활성 Actor의 Provider 구성은 유지하고 모든 Actor 반환 뒤에만
        /// <br/> Provider 변경을 허용한다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_EntityPresentationCoordinator_활성Actor동안_Provider구성을_고정()
        {
            var trace = new List<string>();
            var coordinator = new EntityPresentationCoordinator<ActorProbe, EntitySample>();
            var first = new PresentationProviderProbe("First", trace);
            var second = new PresentationProviderProbe("Second", trace);
            var entity = new EntitySample();
            var firstActor = CreateActor();
            var secondActor = CreateActor();

            coordinator.AddProvider(first);

            // IReadOnlyList를 List로 다운캐스팅해 생명주기 잠금을 우회할 수 없어야 한다.
            Assert.IsFalse(coordinator.Providers is List<IEntityPresentationProvider<ActorProbe, EntitySample>>);
            var view = coordinator.Providers as IList<IEntityPresentationProvider<ActorProbe, EntitySample>>;
            Assert.IsNotNull(view);
            Assert.Throws<NotSupportedException>(() => view.Add(second));

            coordinator.Create(firstActor, entity);
            coordinator.Create(secondActor, entity);

            Assert.Throws<InvalidOperationException>(() => coordinator.AddProvider(second));
            Assert.Throws<InvalidOperationException>(() => coordinator.RemoveProvider(first));
            Assert.Throws<InvalidOperationException>(() => coordinator.Create(firstActor, entity));

            coordinator.Release(firstActor, entity, DespawnReason.Removed);
            Assert.Throws<InvalidOperationException>(() => coordinator.RemoveProvider(first));

            coordinator.Release(secondActor, entity, DespawnReason.Removed);

            Assert.IsTrue(coordinator.RemoveProvider(first));
            coordinator.AddProvider(second);
            var thirdActor = CreateActor();
            coordinator.Create(thirdActor, entity);
            coordinator.Release(thirdActor, entity, DespawnReason.Removed);

            CollectionAssert.AreEqual
            (
                new[]
                {
                    "First.Create", "First.Create",
                    "First.Release", "First.Release",
                    "Second.Create", "Second.Release"
                },
                trace
            );
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 마지막 Actor가 구성 또는 반환되는 순간에도 재진입 구성을 차단한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_EntityPresentationCoordinator_Provider콜백중_구성변경을_거절()
        {
            var trace = new List<string>();
            var coordinator = new EntityPresentationCoordinator<ActorProbe, EntitySample>();
            var first = new PresentationProviderProbe("First", trace);
            var second = new PresentationProviderProbe("Second", trace);
            var entity = new EntitySample();
            var actor = CreateActor();

            first.OnCreateAction = () =>
                Assert.Throws<InvalidOperationException>(() => coordinator.AddProvider(second));
            first.OnReleaseAction = () =>
                Assert.Throws<InvalidOperationException>(() => coordinator.RemoveProvider(first));

            coordinator.AddProvider(first);
            coordinator.Create(actor, entity);
            coordinator.Release(actor, entity, DespawnReason.Removed);

            Assert.IsTrue(coordinator.RemoveProvider(first));
            coordinator.AddProvider(second);
            Assert.AreEqual(1, coordinator.Providers.Count);
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// Create 실패로 rollback한 Actor는 Provider 재구성을 막지 않는다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_EntityPresentationCoordinator_Create실패후_Provider재구성이_가능()
        {
            var trace = new List<string>();
            var coordinator = new EntityPresentationCoordinator<ActorProbe, EntitySample>();
            var provider = new PresentationProviderProbe("Failed", trace)
            {
                FailOnCreate = true
            };
            var entity = new EntitySample();
            var actor = CreateActor();
            coordinator.AddProvider(provider);

            Assert.Throws<InvalidOperationException>(() => coordinator.Create(actor, entity));
            Assert.IsTrue(coordinator.RemoveProvider(provider));

            provider.FailOnCreate = false;
            coordinator.AddProvider(provider);
            coordinator.Create(actor, entity);
            coordinator.Release(actor, entity, DespawnReason.Removed);

            CollectionAssert.AreEqual
            (
                new[]
                {
                    "Failed.Create",
                    "Failed.Release",
                    "Failed.Create",
                    "Failed.Release"
                },
                trace
            );
        }

    #endregion

    #region C-1: Actor 단위 수명 재진입

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> Provider 생성 콜백에서는 같은 Actor의 수명 작업만 거절한다.
        /// <br/> 다른 Actor의 생성은 허용하며 각 Actor의 구성은 한 번씩 완료된다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_EntityPresentationCoordinator_생성콜백중_같은Actor수명재진입만_차단()
        {
            var trace = new List<string>();
            var coordinator = new EntityPresentationCoordinator<ActorProbe, EntitySample>();
            var provider = new PresentationProviderProbe("Visual", trace);
            var entity = new EntitySample();
            var actor = CreateActor();
            var otherActor = CreateActor();
            var isNested = false;

            provider.OnCreateAction = () =>
            {
                if (isNested)
                {
                    return;
                }

                isNested = true;
                Assert.Throws<InvalidOperationException>
                (
                    () => coordinator.Create(actor, entity)
                );
                Assert.Throws<InvalidOperationException>
                (
                    () => coordinator.Release(actor, entity, DespawnReason.Removed)
                );
                Assert.Throws<InvalidOperationException>
                (
                    () => coordinator.RebindOwner(actor, otherActor)
                );
                Assert.Throws<InvalidOperationException>
                (
                    () => coordinator.RebindEntity(actor, entity, new EntitySample())
                );
                coordinator.Create(otherActor, entity);
            };

            coordinator.AddProvider(provider);
            coordinator.Create(actor, entity);
            coordinator.Release(otherActor, entity, DespawnReason.Removed);
            coordinator.Release(actor, entity, DespawnReason.Removed);

            CollectionAssert.AreEqual
            (
                new[]
                {
                    "Visual.Create",
                    "Visual.Create",
                    "Visual.Release",
                    "Visual.Release"
                },
                trace
            );
            Assert.IsTrue(coordinator.RemoveProvider(provider));
        }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/> Provider 반환 콜백에서 다른 Actor를 반환할 수 있고 순환 반환은 거절한다.
        /// <br/> 중첩 반환 중에도 Provider 구성 변경은 거절하며 두 Actor는 한 번씩 반환된다.
        /// </summary>
        // --------------------------------------------------------------------------------
        [Test]
        public void TEST_EntityPresentationCoordinator_반환콜백중_다른Actor를_반환하고_순환은_거절()
        {
            var trace = new List<string>();
            var coordinator = new EntityPresentationCoordinator<ActorProbe, EntitySample>();
            var provider = new PresentationProviderProbe("Visual", trace);
            var entity = new EntitySample();
            var first = CreateActor();
            var second = CreateActor();
            var depth = 0;
            var rejected = 0;

            coordinator.AddProvider(provider);
            coordinator.Create(first, entity);
            coordinator.Create(second, entity);

            provider.OnReleaseAction = () =>
            {
                depth++;
                Assert.Throws<InvalidOperationException>(() => coordinator.RemoveProvider(provider));

                if (depth == 1)
                {
                    coordinator.Release(second, entity, DespawnReason.Removed);
                    return;
                }

                Assert.Throws<InvalidOperationException>
                (
                    () => coordinator.Release(first, entity, DespawnReason.Removed)
                );
                rejected++;
            };

            coordinator.Release(first, entity, DespawnReason.Removed);

            Assert.AreEqual(1, rejected);
            Assert.AreEqual(2, trace.FindAll(item => item == "Visual.Release").Count);
            Assert.IsTrue(coordinator.RemoveProvider(provider));
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> 마지막 Actor 반환 콜백이 동일 Actor의 새 생성 또는 중복 반환을
        /// <br/> 시도해도 terminal 수명만 한 번 완료된다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_EntityPresentationCoordinator_반환콜백중_중복수명을_차단()
        {
            var trace = new List<string>();
            var coordinator = new EntityPresentationCoordinator<ActorProbe, EntitySample>();
            var provider = new PresentationProviderProbe("Visual", trace);
            var entity = new EntitySample();
            var actor = CreateActor();

            provider.OnReleaseAction = () =>
            {
                Assert.Throws<InvalidOperationException>
                (
                    () => coordinator.Create(actor, entity)
                );
                Assert.Throws<InvalidOperationException>
                (
                    () => coordinator.Release(actor, entity, DespawnReason.Removed)
                );
            };

            coordinator.AddProvider(provider);
            coordinator.Create(actor, entity);
            coordinator.Release(actor, entity, DespawnReason.Removed);

            CollectionAssert.AreEqual
            (
                new[]
                {
                    "Visual.Create",
                    "Visual.Release"
                },
                trace
            );
            Assert.IsTrue(coordinator.RemoveProvider(provider));
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> terminal 반환이 실패해도 같은 Actor의 Provider Release는
        /// <br/> 재호출되지 않으며 다음 수명에만 다시 생성할 수 있다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_EntityPresentationCoordinator_반환실패도_terminal_중복반환없음()
        {
            var trace = new List<string>();
            var coordinator = new EntityPresentationCoordinator<ActorProbe, EntitySample>();
            var provider = new PresentationProviderProbe("Visual", trace)
            {
                FailOnRelease = true
            };
            var entity = new EntitySample();
            var actor = CreateActor();

            coordinator.AddProvider(provider);
            coordinator.Create(actor, entity);

            Assert.Throws<InvalidOperationException>
            (
                () => coordinator.Release(actor, entity, DespawnReason.Removed)
            );

            coordinator.Release(actor, entity, DespawnReason.Removed);
            Assert.AreEqual(1, trace.FindAll(item => item == "Visual.Release").Count);

            Assert.IsTrue(coordinator.RemoveProvider(provider));
            provider.FailOnRelease = false;
            coordinator.AddProvider(provider);
            coordinator.Create(actor, entity);
            coordinator.Release(actor, entity, DespawnReason.Removed);

            Assert.AreEqual(2, trace.FindAll(item => item == "Visual.Create").Count);
            Assert.AreEqual(2, trace.FindAll(item => item == "Visual.Release").Count);
        }

    #endregion

    }
}
