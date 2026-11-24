/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : TEST_EntityActorRuntime.cs
수정일 : 2026-11-16

# 설명
EntityActorRuntime이 ActorController와 PresentationCoordinator를 생성·반환·Rebind transaction으로 조립하는 계약을 검증한다.
정상 lifecycle, Entity Rebind, Presentation Rebind rollback과 복합 실패 집계를 대상으로 한다.
Unity Test Runner (Play Mode)에서 실행한다.

# 테스트 구성
 L: 정상 Actor + Presentation lifecycle
 T: Entity·Presentation Owner Rebind와 Runtime 소유권·재진입 보호
 C: Actor 단위 수명 재진입, Registry 연쇄 반환과 Actor 풀 재사용
 R: commit 전 explicit rollback
 X: 생성·반환 실패 rollback과 오류 집계
 B: Registry 자동 Binding, 수동 Actor 소유권과 Local 구성
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

        [Serializable]
        private sealed class EntitySample : EntityBase
        {
            // ------------------------------------------------------------
            /// <summary>
            /// 공개된 현재 상태의 테스트 값.
            /// </summary>
            // ------------------------------------------------------------
            public int CurrentValue;
        }

        private sealed class ActorProbe : EntityActorBase<EntitySample>
        {
            internal Action OnBindAction;

            protected override void OnBindEntity(EntitySample entity)
            {
                OnBindAction?.Invoke();
            }
        }

        [Serializable]
        private sealed class RegistryHarness : EntitySpawnRegistry<EntitySample>
        {
            protected override EntitySample Acquire() => new();

            // ------------------------------------------------------------
            /// <summary>
            /// Registry의 로컬 Spawn을 테스트에 노출한다.
            /// </summary>
            // ------------------------------------------------------------
            public new bool TrySpawn(out EntitySample entity) => base.TrySpawn(out entity);
        }

        private sealed class ActorFactoryProbe :
            EntityActorFactory<ActorProbe, EntitySample, EntityActorNoContext>
        {
            private readonly IGameObjectProvider provider;

            internal Action<ActorProbe> OnPrepare;

            // ------------------------------------------------------------
            /// <summary>
            /// Actor Cleanup 실패를 주입한다.
            /// </summary>
            // ------------------------------------------------------------
            public bool FailOnCleanup { get; set; }

            // ------------------------------------------------------------
            /// <summary>
            /// Actor 반환 콜백 호출 여부를 반환한다.
            /// </summary>
            // ------------------------------------------------------------
            public bool HasReleased { get; private set; }

            // ------------------------------------------------------------
            /// <summary>
            /// Actor에 적용된 마지막 반환 사유를 반환한다.
            /// </summary>
            // ------------------------------------------------------------
            public DespawnReason LastReleaseReason { get; private set; }

            // ------------------------------------------------------------
            /// <summary>
            /// Actor 반환 콜백이 호출된 횟수를 반환한다.
            /// </summary>
            // ------------------------------------------------------------
            public int ReleaseCount { get; private set; }

            // ------------------------------------------------------------
            /// <summary>
            /// 지정한 GameObject Provider로 테스트 Factory를 생성한다.
            /// </summary>
            // ------------------------------------------------------------
            public ActorFactoryProbe(IGameObjectProvider provider)
            {
                this.provider = provider;
            }

            protected override IGameObjectProvider ResolveProvider
            (
                EntitySample entity,
                in EntityActorNoContext context
            )
            {
                return provider;
            }

            protected override void PrepareActor
            (
                ActorProbe actor,
                EntitySample entity,
                in EntityActorNoContext context
            )
            {
                OnPrepare?.Invoke(actor);
            }

            protected override void CleanupActor
            (
                ActorProbe actor,
                DespawnReason reason
            )
            {
                HasReleased = true;
                ReleaseCount++;
                LastReleaseReason = reason;

                if (FailOnCleanup)
                {
                    throw new InvalidOperationException("actor cleanup failure");
                }
            }
        }

        private sealed class GameObjectPoolProvider : IGameObjectProvider
        {
            private readonly GameObject source;
            private readonly Stack<GameObject> pool = new();
            private readonly List<GameObject> instances = new();

            // ------------------------------------------------------------
            /// <summary>
            /// 테스트 Pool에서 사용하지 않는 부모 Transform.
            /// </summary>
            // ------------------------------------------------------------
            public Transform Parent { get; set; }

            // ------------------------------------------------------------
            /// <summary>
            /// 복제 원본을 지정해 Pool을 생성한다.
            /// </summary>
            // ------------------------------------------------------------
            public GameObjectPoolProvider(GameObject source)
            {
                this.source = source;
            }

            // ------------------------------------------------------------
            /// <summary>
            /// 반환된 인스턴스를 우선 재사용하고 없으면 새로 복제한다.
            /// </summary>
            // ------------------------------------------------------------
            public GameObject Acquire(bool worldPositionStays = true)
            {
                if (pool.Count > 0)
                {
                    return pool.Pop();
                }

                var instance = UnityEngine.Object.Instantiate(source);
                instances.Add(instance);
                return instance;
            }

            // ------------------------------------------------------------
            /// <summary>
            /// 동기 Pool은 비동기 획득을 지원하지 않는다.
            /// </summary>
            // ------------------------------------------------------------
            public Awaitable<GameObject> AcquireAsync(bool worldPositionStays = true)
            {
                throw new NotSupportedException();
            }

            // ------------------------------------------------------------
            /// <summary>
            /// 반환된 인스턴스를 재사용 대기열에 둔다.
            /// </summary>
            // ------------------------------------------------------------
            public void Release(GameObject go, bool worldPositionStays = true)
            {
                pool.Push(go);
            }

            // ------------------------------------------------------------
            /// <summary>
            /// Pool이 복제한 모든 인스턴스를 폐기한다.
            /// </summary>
            // ------------------------------------------------------------
            public void DestroyAll()
            {
                foreach (var instance in instances)
                {
                    UnityEngine.Object.DestroyImmediate(instance);
                }

                instances.Clear();
                pool.Clear();
            }
        }

        private sealed class PresentationProviderProbe :
            IEntityPresentationProvider<ActorProbe, EntitySample>
        {
            private readonly string name;
            private readonly List<string> trace;

            // ------------------------------------------------------------
            /// <summary>
            /// Presentation 생성 실패를 주입한다.
            /// </summary>
            // ------------------------------------------------------------
            public bool FailOnCreate { get; set; }

            // ------------------------------------------------------------
            /// <summary>
            /// Actor 생성 시 공개 상태를 검사한다.
            /// </summary>
            // ------------------------------------------------------------
            public Action<EntitySample> InspectOnCreate { get; set; }

            // ------------------------------------------------------------
            /// <summary>
            /// Actor 반환 콜백 중 수행할 검사 또는 다른 수명 작업.
            /// </summary>
            // ------------------------------------------------------------
            public Action<EntitySample, ActorProbe> OnReleaseAction { get; set; }

            // ------------------------------------------------------------
            /// <summary>
            /// Actor 이전 콜백 중 수행할 검사 또는 다른 수명 작업.
            /// </summary>
            // ------------------------------------------------------------
            public Action<ActorProbe> OnReboundAction { get; set; }

            // ------------------------------------------------------------
            /// <summary>
            /// Presentation 반환 실패를 주입한다.
            /// </summary>
            // ------------------------------------------------------------
            public bool FailOnRelease { get; set; }

            // ------------------------------------------------------------
            /// <summary>
            /// Presentation 이전 실패를 주입한다.
            /// </summary>
            // ------------------------------------------------------------
            public bool FailOnRebind { get; set; }

            // ------------------------------------------------------------
            /// <summary>
            /// Presentation 이전 롤백 실패를 주입한다.
            /// </summary>
            // ------------------------------------------------------------
            public bool FailOnRebindRollback { get; set; }

            // ------------------------------------------------------------
            /// <summary>
            /// 마지막 Presentation 반환 사유를 반환한다.
            /// </summary>
            // ------------------------------------------------------------
            public DespawnReason LastReleaseReason { get; private set; }

            // ------------------------------------------------------------
            /// <summary>
            /// Provider 이름과 실행 기록을 설정한다.
            /// </summary>
            // ------------------------------------------------------------
            public PresentationProviderProbe(string name, List<string> trace)
            {
                this.name = name;
                this.trace = trace;
            }

            // ------------------------------------------------------------
            /// <summary>
            /// Actor 생성 순서를 기록하고 실패를 주입한다.
            /// </summary>
            // ------------------------------------------------------------
            public void OnActorCreated(EntitySample entity, ActorProbe actor)
            {
                InspectOnCreate?.Invoke(entity);
                trace.Add($"{name}.Create");

                if (FailOnCreate)
                {
                    throw new InvalidOperationException($"{name} create failure");
                }
            }

            // ------------------------------------------------------------
            /// <summary>
            /// Actor 반환 사유를 기록하고 실패를 주입한다.
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
                OnReleaseAction?.Invoke(entity, actor);

                if (FailOnRelease)
                {
                    throw new InvalidOperationException($"{name} release failure");
                }
            }

            // ------------------------------------------------------------
            /// <summary>
            /// Actor 이전을 기록하고 실패를 주입한다.
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
                OnReboundAction?.Invoke(actor);

                if (FailOnRebind)
                {
                    throw new InvalidOperationException($"{name} transfer failure");
                }
            }

            // ------------------------------------------------------------
            /// <summary>
            /// Actor 이전 롤백을 기록하고 실패를 주입한다.
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
            /// Presentation Owner 이전 콜백을 제공한다.
            /// </summary>
            // ------------------------------------------------------------
            public void OnPresentationOwnerRebound
            (
                ActorProbe previousOwner,
                ActorProbe nextOwner
            )
            {
                // NONE
            }

            // ------------------------------------------------------------
            /// <summary>
            /// Presentation Owner 이전 롤백 콜백을 제공한다.
            /// </summary>
            // ------------------------------------------------------------
            public void OnPresentationOwnerRebindRollback
            (
                ActorProbe previousOwner,
                ActorProbe nextOwner
            )
            {
                // NONE
            }
        }

        private
        (
            EntityActorRuntime<ActorProbe, EntitySample, EntityActorNoContext> runtime,
            EntityActorController<ActorProbe, EntitySample, EntityActorNoContext> controller,
            EntityPresentationCoordinator<ActorProbe, EntitySample> coordinator,
            ActorFactoryProbe factory
        ) CreateRuntime()
        {
            prefab = new GameObject("TestActorPrefab");
            prefab.AddComponent<ActorProbe>();
            prefab.SetActive(false);

            var provider = new PrefabGameObjectProvider(prefab, null);
            var factory = new ActorFactoryProbe(provider);
            var controller =
                new EntityActorController<ActorProbe, EntitySample, EntityActorNoContext>(factory);
            var coordinator =
                new EntityPresentationCoordinator<ActorProbe, EntitySample>();
            var runtime =
                new EntityActorRuntime<ActorProbe, EntitySample, EntityActorNoContext>
                (
                    controller,
                    coordinator
                );

            return (runtime, controller, coordinator, factory);
        }

    #endregion

    #region 픽스처

        private GameObject prefab = null;

        // ------------------------------------------------------------
        /// <summary>
        /// 테스트에서 생성한 Actor Prefab을 정리한다.
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

    #region L-1: 정상 Actor + Presentation lifecycle

        // ------------------------------------------------------------
        /// <summary>
        /// Actor와 Presentation이 함께 생성·반환되는지 검증한다.
        /// </summary>
        // ------------------------------------------------------------
        [UnityTest]
        public IEnumerator TEST_EntityActorRuntime_Create와Release가_Actor와Presentation수명을_함께완료()
        {
            var (runtime, controller, coordinator, _) = CreateRuntime();
            var trace = new List<string>();
            var presentation = new PresentationProviderProbe("Presentation", trace);
            var entity = new EntitySample();
            var context = default(EntityActorNoContext);
            coordinator.AddProvider(presentation);

            var actor = runtime.Create(entity, context);

            Assert.IsTrue(controller.Contains(actor));
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

            Assert.IsFalse(controller.Contains(actor));
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

    #region T-1: Actor + Presentation Rebind

        // ------------------------------------------------------------
        /// <summary>
        /// 동일 Actor의 Entity 관계 이전을 검증한다.
        /// </summary>
        // ------------------------------------------------------------
        [UnityTest]
        public IEnumerator TEST_EntityActorRuntime_Rebind가_같은Actor를_새Entity로_이전()
        {
            var (runtime, controller, coordinator, _) = CreateRuntime();
            var trace = new List<string>();
            var presentation = new PresentationProviderProbe("Presentation", trace);
            var previous = new EntitySample();
            var next = new EntitySample();
            var context = default(EntityActorNoContext);
            coordinator.AddProvider(presentation);

            var actor = runtime.Create(previous, context);

            Assert.IsTrue(runtime.Rebind(actor, next));
            Assert.AreSame(next, actor.Entity);
            Assert.AreEqual(0, controller.FindAll(previous).Count);
            Assert.IsTrue(controller.TryFindSingle(next, out var transferred));
            Assert.AreSame(actor, transferred);
            CollectionAssert.AreEqual
            (
                new[]
                {
                    "Presentation.Create",
                    "Presentation.Rebind"
                },
                trace
            );

            runtime.Release(actor, DespawnReason.Removed);

            yield return null;
        }

    #endregion

    #region T-2: Presentation Rebind 실패 rollback

        // ------------------------------------------------------------
        /// <summary>
        /// Rebind 실패 시 Actor와 Provider 롤백을 검증한다.
        /// </summary>
        // ------------------------------------------------------------
        [UnityTest]
        public IEnumerator TEST_EntityActorRuntime_PresentationRebind실패시_Actor와완료Provider를_Rollback()
        {
            var (runtime, controller, coordinator, _) = CreateRuntime();
            var trace = new List<string>();
            var first = new PresentationProviderProbe("First", trace);
            var second = new PresentationProviderProbe("Second", trace)
            {
                FailOnRebind = true
            };
            var previous = new EntitySample();
            var next = new EntitySample();
            var context = default(EntityActorNoContext);
            coordinator.AddProvider(first);
            coordinator.AddProvider(second);

            var actor = runtime.Create(previous, context);

            Assert.Throws<InvalidOperationException>
            (
                () => runtime.Rebind(actor, next)
            );

            Assert.AreSame(previous, actor.Entity);
            Assert.IsTrue(controller.TryFindSingle(previous, out var restored));
            Assert.AreSame(actor, restored);
            Assert.AreEqual(0, controller.FindAll(next).Count);
            CollectionAssert.AreEqual
            (
                new[]
                {
                    "First.Create",
                    "Second.Create",
                    "First.Rebind",
                    "Second.Rebind",
                    "Second.RebindRollback",
                    "First.RebindRollback"
                },
                trace
            );

            runtime.Release(actor, DespawnReason.Removed);

            yield return null;
        }

    #endregion

    #region T-3: Runtime 소유 Presentation Owner 보호

        // ----------------------------------------------------------------------
        /// <summary>
        /// Runtime이 생성한 Actor의 Presentation Owner를 독립적으로 이전하지 않는다.
        /// </summary>
        // ----------------------------------------------------------------------
        [UnityTest]
        public IEnumerator TEST_EntityActorRuntime_소유Actor의PresentationOwner이전을거절하고_정상반환()
        {
            var (runtime, controller, coordinator, _) = CreateRuntime();
            var trace = new List<string>();
            coordinator.AddProvider(new PresentationProviderProbe("Visual", trace));
            var entity = new EntitySample();

            var actor = runtime.Create(entity, default);

            Assert.Throws<InvalidOperationException>
            (
                () => coordinator.RebindOwner(actor, actor)
            );
            Assert.Throws<InvalidOperationException>
            (
                () => coordinator.Release(actor, entity, DespawnReason.Removed)
            );
            Assert.Throws<InvalidOperationException>
            (
                () => coordinator.RebindEntity(actor, entity, new EntitySample())
            );

            Assert.AreSame(entity, actor.Entity);
            Assert.IsTrue(controller.Contains(actor));
            Assert.IsTrue(runtime.Release(actor, DespawnReason.Removed));
            Assert.AreEqual(1, trace.FindAll(item => item == "Visual.Release").Count);

            yield return null;
        }

    #endregion

    #region T-4: 결합 Runtime의 단일 변경 권한

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> 결합 Controller와 Coordinator를 다른 Runtime에 중복 할당할 수 없다.
        /// <br/> 결합 실행기의 직접 수명 변경도 차단한다.
        /// </summary>
        // ----------------------------------------------------------------------
        [UnityTest]
        public IEnumerator TEST_EntityActorRuntime_결합실행기의중복소유와직접변경을거절()
        {
            var (runtime, controller, coordinator, factory) = CreateRuntime();
            var independentCoordinator =
                new EntityPresentationCoordinator<ActorProbe, EntitySample>();

            Assert.Throws<InvalidOperationException>
            (
                () => new EntityActorRuntime<ActorProbe, EntitySample, EntityActorNoContext>
                (
                    controller,
                    independentCoordinator
                )
            );

            var independentController =
                new EntityActorController<ActorProbe, EntitySample, EntityActorNoContext>(factory);

            Assert.Throws<InvalidOperationException>
            (
                () => new EntityActorRuntime<ActorProbe, EntitySample, EntityActorNoContext>
                (
                    independentController,
                    coordinator
                )
            );

            // 결합 실패로 반환된 독립 Controller는 재사용 가능하다.
            var availableCoordinator =
                new EntityPresentationCoordinator<ActorProbe, EntitySample>();
            var nextRuntime =
                new EntityActorRuntime<ActorProbe, EntitySample, EntityActorNoContext>
                (
                    independentController,
                    availableCoordinator
                );

            var entity = new EntitySample();
            var actor = runtime.Create(entity, default);

            Assert.Throws<InvalidOperationException>
            (
                () => controller.Release(actor, DespawnReason.Removed)
            );
            Assert.Throws<InvalidOperationException>
            (
                () => controller.Rebind(actor, new EntitySample())
            );
            Assert.Throws<InvalidOperationException>
            (
                () => coordinator.Create(actor, entity)
            );

            Assert.IsTrue(runtime.Release(actor, DespawnReason.Removed));
            Assert.IsFalse(controller.Contains(actor));
            Assert.IsFalse(nextRuntime.Release(actor, DespawnReason.Removed));

            yield return null;
        }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/> Bind 훅과 Provider 생성 콜백은 같은 Actor의 수명 변경에 재진입할 수 없다.
        /// <br/> 거절된 호출은 정상 진행 중인 Actor 수명에 영향을 주지 않는다.
        /// </summary>
        // --------------------------------------------------------------------------------
        [UnityTest]
        public IEnumerator TEST_EntityActorRuntime_ActorBind와Provider콜백의_같은Actor재진입을거절()
        {
            var (runtime, controller, coordinator, factory) = CreateRuntime();
            var entity = new EntitySample();
            var trace = new List<string>();
            var nextEntity = new EntitySample();

            factory.OnPrepare = actor =>
            {
                actor.OnBindAction = () =>
                {
                    Assert.Throws<InvalidOperationException>
                    (
                        () => runtime.Release(actor, DespawnReason.Removed)
                    );
                    Assert.Throws<InvalidOperationException>
                    (
                        () => controller.Release(actor, DespawnReason.Removed)
                    );
                };
            };

            var provider = new PresentationProviderProbe("Visual", trace)
            {
                InspectOnCreate = _ =>
                {
                    var actor = controller.FindAll(entity)[0];

                    Assert.Throws<InvalidOperationException>
                    (
                        () => runtime.Release(actor, DespawnReason.Removed)
                    );
                    Assert.Throws<InvalidOperationException>
                    (
                        () => runtime.Rebind(actor, nextEntity)
                    );
                }
            };

            coordinator.AddProvider(provider);

            var created = runtime.Create(entity, default);

            Assert.IsTrue(controller.Contains(created));
            Assert.AreSame(entity, created.Entity);
            Assert.IsTrue(runtime.Release(created, DespawnReason.Removed));
            CollectionAssert.AreEqual
            (
                new[]
                {
                    "Visual.Create",
                    "Visual.Release"
                },
                trace
            );

            yield return null;
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> 주입한 Registry는 하나의 Controller만 소유한다.
        /// <br/> 소유자 외에는 조회로만 Runtime의 Actor 수명을 관찰할 수 있다.
        /// </summary>
        // ----------------------------------------------------------------------
        [UnityTest]
        public IEnumerator TEST_EntityActorRuntime_주입한Registry는_하나의Controller만_소유하고_조회로관찰()
        {
            var (_, _, _, factory) = CreateRuntime();
            var actorRegistry = new EntityActorRegistry<ActorProbe, EntitySample>();
            var controller =
                new EntityActorController<ActorProbe, EntitySample, EntityActorNoContext>
                (
                    factory,
                    actorRegistry
                );

            Assert.Throws<InvalidOperationException>
            (
                () => new EntityActorController<ActorProbe, EntitySample, EntityActorNoContext>
                (
                    factory,
                    actorRegistry
                )
            );

            var coordinator = new EntityPresentationCoordinator<ActorProbe, EntitySample>();
            var runtime =
                new EntityActorRuntime<ActorProbe, EntitySample, EntityActorNoContext>
                (
                    controller,
                    coordinator
                );

            var entity = new EntitySample();
            var actor = runtime.Create(entity, default);

            Assert.IsTrue(actorRegistry.Contains(actor));
            Assert.IsTrue(actorRegistry.IsBound(actor));
            Assert.AreEqual(1, actorRegistry.Count);
            Assert.AreEqual(1, actorRegistry.BoundCount);
            Assert.AreSame(entity, actor.Entity);

            Assert.IsTrue(runtime.Release(actor, DespawnReason.Removed));

            Assert.IsFalse(actorRegistry.Contains(actor));
            Assert.AreEqual(0, actorRegistry.Count);

            yield return null;
        }

    #endregion

    #region C-1: Actor 단위 수명 재진입

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/> Actor 생성 중 같은 Actor의 수명 변경은 거절하고 독립 Actor 생성은 허용한다.
        /// <br/> 두 Actor는 Presentation과 Factory를 각각 한 번씩 반환한다.
        /// </summary>
        // --------------------------------------------------------------------------------
        [UnityTest]
        public IEnumerator TEST_EntityActorRuntime_Actor생성중_독립Actor생성을허용()
        {
            var (runtime, controller, coordinator, factory) = CreateRuntime();
            var trace = new List<string>();
            coordinator.AddProvider(new PresentationProviderProbe("Visual", trace));
            ActorProbe independent = null;
            var isFirst = true;

            factory.OnPrepare = actor =>
            {
                if (!isFirst)
                {
                    return;
                }

                isFirst = false;
                actor.OnBindAction = () =>
                {
                    Assert.Throws<InvalidOperationException>
                    (
                        () => runtime.Release(actor, DespawnReason.Removed)
                    );
                    independent = runtime.Create(new EntitySample(), default);
                };
            };

            var created = runtime.Create(new EntitySample(), default);

            Assert.IsNotNull(independent);
            Assert.AreNotSame(created, independent);
            Assert.IsTrue(controller.Contains(created));
            Assert.IsTrue(controller.Contains(independent));

            Assert.IsTrue(runtime.Release(independent, DespawnReason.Removed));
            Assert.IsTrue(runtime.Release(created, DespawnReason.Removed));

            Assert.IsFalse(controller.Contains(created));
            Assert.IsFalse(controller.Contains(independent));
            Assert.AreEqual(2, factory.ReleaseCount);
            Assert.AreEqual(2, trace.FindAll(item => item == "Visual.Create").Count);
            Assert.AreEqual(2, trace.FindAll(item => item == "Visual.Release").Count);

            yield return null;
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> 생성 중 만든 독립 Actor는 첫 Actor의 Provider 실패 Rollback에
        /// <br/> 영향받지 않는다. 첫 Actor의 부분 생성 자원만 역순으로 반환한다.
        /// </summary>
        // ----------------------------------------------------------------------
        [UnityTest]
        public IEnumerator TEST_EntityActorRuntime_Actor생성실패는_중첩생성한독립Actor를_보존()
        {
            var (runtime, controller, coordinator, factory) = CreateRuntime();
            var trace = new List<string>();
            var second = new PresentationProviderProbe("Second", trace);
            var created = false;
            ActorProbe independent = null;
            var first = new PresentationProviderProbe("First", trace)
            {
                InspectOnCreate = _ =>
                {
                    if (created)
                    {
                        return;
                    }

                    // 독립 Actor 생성이 끝난 뒤에만 첫 Actor의 두 번째 Provider를 실패시킨다.
                    created = true;
                    independent = runtime.Create(new EntitySample(), default);
                    second.FailOnCreate = true;
                }
            };
            coordinator.AddProvider(first);
            coordinator.AddProvider(second);
            var failedEntity = new EntitySample();

            Assert.Throws<InvalidOperationException>(() => runtime.Create(failedEntity, default));

            Assert.IsNotNull(independent);
            Assert.IsTrue(controller.Contains(independent));
            Assert.AreEqual(0, controller.FindAll(failedEntity).Count);
            Assert.AreEqual(1, factory.ReleaseCount);
            Assert.AreEqual(1, trace.FindAll(item => item == "First.Release").Count);
            Assert.AreEqual(1, trace.FindAll(item => item == "Second.Release").Count);

            second.FailOnCreate = false;
            Assert.IsTrue(runtime.Release(independent, DespawnReason.Removed));

            Assert.AreEqual(2, factory.ReleaseCount);
            Assert.AreEqual(2, trace.FindAll(item => item == "First.Release").Count);
            Assert.AreEqual(2, trace.FindAll(item => item == "Second.Release").Count);

            yield return null;
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> Provider 반환 콜백에서 같은 Actor의 반환·Rebind는 거절하고
        /// <br/> 다른 Actor 반환은 허용한다. 두 Actor 모두 한 번씩 반환된다.
        /// </summary>
        // ----------------------------------------------------------------------
        [UnityTest]
        public IEnumerator TEST_EntityActorRuntime_Provider반환콜백중_다른Actor를_반환()
        {
            var (runtime, controller, coordinator, factory) = CreateRuntime();
            var trace = new List<string>();
            var provider = new PresentationProviderProbe("Visual", trace);
            coordinator.AddProvider(provider);
            var first = runtime.Create(new EntitySample(), default);
            var second = runtime.Create(new EntitySample(), default);
            var secondReleased = false;

            provider.OnReleaseAction = (_, actor) =>
            {
                if (!ReferenceEquals(actor, first))
                {
                    return;
                }

                Assert.Throws<InvalidOperationException>
                (
                    () => runtime.Release(first, DespawnReason.Removed)
                );
                Assert.Throws<InvalidOperationException>
                (
                    () => runtime.Rebind(first, new EntitySample())
                );
                secondReleased = runtime.Release(second, DespawnReason.Removed);
            };

            Assert.IsTrue(runtime.Release(first, DespawnReason.Removed));

            Assert.IsTrue(secondReleased);
            Assert.IsFalse(controller.Contains(first));
            Assert.IsFalse(controller.Contains(second));
            Assert.IsFalse(runtime.Release(first, DespawnReason.Removed));
            Assert.IsFalse(runtime.Release(second, DespawnReason.Removed));
            Assert.AreEqual(2, factory.ReleaseCount);
            Assert.AreEqual(2, trace.FindAll(item => item == "Visual.Release").Count);

            // 모든 Presentation 수명이 종료되어 Provider 구성을 바꿀 수 있다.
            Assert.IsTrue(coordinator.RemoveProvider(provider));

            yield return null;
        }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/> A 반환 중 B를 반환하고 B 반환 중 A를 다시 반환하면 두 번째 A 작업만 거절한다.
        /// <br/> A와 B의 수명은 각각 한 번씩 끝난다.
        /// </summary>
        // --------------------------------------------------------------------------------
        [UnityTest]
        public IEnumerator TEST_EntityActorRuntime_A_B_A순환반환은_두번째A만거절()
        {
            var (runtime, controller, coordinator, factory) = CreateRuntime();
            var trace = new List<string>();
            var provider = new PresentationProviderProbe("Visual", trace);
            coordinator.AddProvider(provider);
            var first = runtime.Create(new EntitySample(), default);
            var second = runtime.Create(new EntitySample(), default);
            var rejected = 0;

            provider.OnReleaseAction = (_, actor) =>
            {
                if (ReferenceEquals(actor, first))
                {
                    Assert.IsTrue(runtime.Release(second, DespawnReason.Removed));
                    return;
                }

                Assert.Throws<InvalidOperationException>
                (
                    () => runtime.Release(first, DespawnReason.Removed)
                );
                rejected++;
            };

            Assert.IsTrue(runtime.Release(first, DespawnReason.Removed));

            Assert.AreEqual(1, rejected);
            Assert.IsFalse(controller.Contains(first));
            Assert.IsFalse(controller.Contains(second));
            Assert.AreEqual(2, factory.ReleaseCount);
            Assert.AreEqual(2, trace.FindAll(item => item == "Visual.Release").Count);

            yield return null;
        }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/> A Rebind 중 같은 Actor의 반환·Rebind는 거절하고 B 반환은 독립적으로 완료한다.
        /// <br/> A는 다음 Entity에 연결된 채 유지된다.
        /// </summary>
        // --------------------------------------------------------------------------------
        [UnityTest]
        public IEnumerator TEST_EntityActorRuntime_Rebind중_다른Actor를_독립적으로_반환()
        {
            var (runtime, controller, coordinator, factory) = CreateRuntime();
            var trace = new List<string>();
            var provider = new PresentationProviderProbe("Visual", trace);
            coordinator.AddProvider(provider);
            var nextEntity = new EntitySample();
            var first = runtime.Create(new EntitySample(), default);
            var second = runtime.Create(new EntitySample(), default);
            var secondReleased = false;

            provider.OnReboundAction = actor =>
            {
                Assert.Throws<InvalidOperationException>
                (
                    () => runtime.Release(actor, DespawnReason.Removed)
                );
                Assert.Throws<InvalidOperationException>
                (
                    () => runtime.Rebind(actor, new EntitySample())
                );
                secondReleased = runtime.Release(second, DespawnReason.Removed);
            };

            Assert.IsTrue(runtime.Rebind(first, nextEntity));

            Assert.IsTrue(secondReleased);
            Assert.AreSame(nextEntity, first.Entity);
            Assert.IsTrue(controller.Contains(first));
            Assert.IsFalse(controller.Contains(second));
            Assert.AreEqual(1, factory.ReleaseCount);

            Assert.IsTrue(runtime.Release(first, DespawnReason.Removed));
            Assert.AreEqual(2, factory.ReleaseCount);
            Assert.AreEqual(2, trace.FindAll(item => item == "Visual.Release").Count);

            yield return null;
        }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// 다른 Runtime이 소유한 Actor의 반환과 Rebind는 거절하고 소유 Runtime의 수명을 유지한다.
        /// </summary>
        // --------------------------------------------------------------------------------
        [UnityTest]
        public IEnumerator TEST_EntityActorRuntime_다른Runtime의_Actor변경을_거절()
        {
            var (runtime, controller, _, factory) = CreateRuntime();
            var otherController =
                new EntityActorController<ActorProbe, EntitySample, EntityActorNoContext>(factory);
            var otherRuntime =
                new EntityActorRuntime<ActorProbe, EntitySample, EntityActorNoContext>
                (
                    otherController,
                    new EntityPresentationCoordinator<ActorProbe, EntitySample>()
                );
            var entity = new EntitySample();
            var actor = runtime.Create(entity, default);

            Assert.IsFalse(otherRuntime.Release(actor, DespawnReason.Removed));
            Assert.IsFalse(otherRuntime.Rebind(actor, new EntitySample()));

            Assert.IsTrue(controller.Contains(actor));
            Assert.IsFalse(otherController.Contains(actor));
            Assert.AreSame(entity, actor.Entity);
            Assert.AreEqual(0, factory.ReleaseCount);

            Assert.IsTrue(runtime.Release(actor, DespawnReason.Removed));
            Assert.AreEqual(1, factory.ReleaseCount);

            yield return null;
        }

    #endregion

    #region C-2: Registry 연쇄 반환과 Actor 풀 재사용

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> Entity A의 Provider 반환 콜백에서 Entity B를 Despawn하면
        /// <br/> B의 Registry·Actor·Presentation이 함께 종료된다.
        /// <br/> B 반환 중 A를 다시 Despawn하는 순환은 거절한다.
        /// </summary>
        // ----------------------------------------------------------------------
        [UnityTest]
        public IEnumerator TEST_EntityActorRuntimeBinding_Provider콜백중_다른Entity_Despawn이_연쇄되고_순환은_거절()
        {
            var (runtime, controller, coordinator, factory) = CreateRuntime();
            var trace = new List<string>();
            var provider = new PresentationProviderProbe("Visual", trace);
            coordinator.AddProvider(provider);
            var registry = new RegistryHarness();
            Assert.IsTrue(registry.TrySpawn(out var first));
            Assert.IsTrue(registry.TrySpawn(out var second));
            var binding = new EntityActorRuntimeBinding<ActorProbe, EntitySample>(runtime);
            binding.Bind(registry);
            Assert.IsTrue(binding.TryFind(first, out var firstActor));
            Assert.IsTrue(binding.TryFind(second, out var secondActor));
            var rejected = 0;

            provider.OnReleaseAction = (entity, _) =>
            {
                if (ReferenceEquals(entity, first))
                {
                    second.Despawn();
                    return;
                }

                Assert.Throws<InvalidOperationException>(() => first.Despawn());
                rejected++;
            };

            first.Despawn();

            Assert.AreEqual(1, rejected);
            Assert.AreEqual(0, registry.Registered.Count);
            Assert.IsFalse(binding.TryFind(first, out _));
            Assert.IsFalse(binding.TryFind(second, out _));
            Assert.IsFalse(controller.Contains(firstActor));
            Assert.IsFalse(controller.Contains(secondActor));
            Assert.AreEqual(2, factory.ReleaseCount);
            Assert.AreEqual(2, trace.FindAll(item => item == "Visual.Release").Count);

            binding.Unbind();
            yield return null;
        }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/> Actor 반환 중 Binding 전체 Unbind는 시작 전에 거절하며 Registry 연결을 유지한다.
        /// <br/> 반환이 끝난 뒤에는 남은 Actor가 정상 해제된다.
        /// </summary>
        // --------------------------------------------------------------------------------
        [UnityTest]
        public IEnumerator TEST_EntityActorRuntimeBinding_Actor반환중_Unbind는_시작전에_거절()
        {
            var (runtime, controller, coordinator, factory) = CreateRuntime();
            var trace = new List<string>();
            var provider = new PresentationProviderProbe("Visual", trace);
            coordinator.AddProvider(provider);
            var registry = new RegistryHarness();
            Assert.IsTrue(registry.TrySpawn(out var first));
            Assert.IsTrue(registry.TrySpawn(out var second));
            var binding = new EntityActorRuntimeBinding<ActorProbe, EntitySample>(runtime);
            binding.Bind(registry);
            Assert.IsTrue(binding.TryFind(second, out var secondActor));

            provider.OnReleaseAction = (entity, _) =>
            {
                if (!ReferenceEquals(entity, first))
                {
                    return;
                }

                Assert.Throws<InvalidOperationException>(() => binding.Unbind());
                Assert.AreSame(registry, binding.BoundRegistry);
            };

            first.Despawn();

            Assert.AreSame(registry, binding.BoundRegistry);
            Assert.IsFalse(binding.TryFind(first, out _));
            Assert.IsTrue(binding.TryFind(second, out _));
            Assert.IsTrue(controller.Contains(secondActor));
            Assert.AreEqual(1, factory.ReleaseCount);

            binding.Unbind();

            Assert.IsNull(binding.BoundRegistry);
            Assert.IsFalse(controller.Contains(secondActor));
            Assert.AreEqual(2, factory.ReleaseCount);
            Assert.AreEqual(1, registry.Registered.Count);

            second.Despawn();
            yield return null;
        }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/> 풀에서 같은 Actor 인스턴스를 다시 받아도 이전 수명 기록은 새 수명에 이어지지 않는다.
        /// <br/> 새 수명의 Entity·Presentation·반환은 독립적으로 한 번씩 완료된다.
        /// </summary>
        // --------------------------------------------------------------------------------
        [UnityTest]
        public IEnumerator TEST_EntityActorRuntime_풀링으로_재사용한_Actor의_수명기록을_분리()
        {
            prefab = new GameObject("TestActorPrefab");
            prefab.AddComponent<ActorProbe>();
            prefab.SetActive(false);

            var pool = new GameObjectPoolProvider(prefab);
            var factory = new ActorFactoryProbe(pool);
            var controller =
                new EntityActorController<ActorProbe, EntitySample, EntityActorNoContext>(factory);
            var coordinator = new EntityPresentationCoordinator<ActorProbe, EntitySample>();
            var runtime =
                new EntityActorRuntime<ActorProbe, EntitySample, EntityActorNoContext>
                (
                    controller,
                    coordinator
                );
            var trace = new List<string>();
            coordinator.AddProvider(new PresentationProviderProbe("Visual", trace));
            var firstEntity = new EntitySample();
            var secondEntity = new EntitySample();

            var first = runtime.Create(firstEntity, default);
            Assert.IsTrue(runtime.Release(first, DespawnReason.Removed));

            var second = runtime.Create(secondEntity, default);

            Assert.AreSame(first, second);
            Assert.AreSame(secondEntity, second.Entity);
            Assert.IsTrue(controller.Contains(second));

            // 이전 수명 기준의 Rebind·반환은 새 수명 기록에 영향을 주지 않고 새 수명만 반환된다.
            Assert.IsTrue(runtime.Release(second, DespawnReason.Removed));
            Assert.IsFalse(runtime.Release(second, DespawnReason.Removed));
            Assert.IsFalse(controller.Contains(second));
            Assert.AreEqual(2, factory.ReleaseCount);
            Assert.AreEqual(2, trace.FindAll(item => item == "Visual.Create").Count);
            Assert.AreEqual(2, trace.FindAll(item => item == "Visual.Release").Count);

            pool.DestroyAll();
            yield return null;
        }

    #endregion

    #region R-1: commit 전 explicit rollback

        // ------------------------------------------------------------
        /// <summary>
        /// 명시적 Rollback의 반환 사유와 정리를 검증한다.
        /// </summary>
        // ------------------------------------------------------------
        [UnityTest]
        public IEnumerator TEST_EntityActorRuntime_Rollback이_SpawnRollback사유로_전체Transaction을정리()
        {
            var (runtime, controller, coordinator, factory) = CreateRuntime();
            var trace = new List<string>();
            var presentation = new PresentationProviderProbe("Presentation", trace);
            var entity = new EntitySample();
            var context = default(EntityActorNoContext);
            coordinator.AddProvider(presentation);

            var actor = runtime.Create(entity, context);
            Assert.IsTrue(runtime.Rollback(actor));

            Assert.IsFalse(controller.Contains(actor));
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

        // ------------------------------------------------------------
        /// <summary>
        /// Presentation 생성 실패 시 Actor 반환을 검증한다.
        /// </summary>
        // ------------------------------------------------------------
        [UnityTest]
        public IEnumerator TEST_EntityActorRuntime_PresentationCreate실패시_Actor까지Rollback()
        {
            var (runtime, controller, coordinator, factory) = CreateRuntime();
            var trace = new List<string>();
            var first = new PresentationProviderProbe("First", trace);
            var second = new PresentationProviderProbe("Second", trace)
            {
                FailOnCreate = true
            };
            var entity = new EntitySample();
            var context = default(EntityActorNoContext);
            coordinator.AddProvider(first);
            coordinator.AddProvider(second);

            Assert.Throws<InvalidOperationException>
            (
                () => runtime.Create(entity, context)
            );

            Assert.IsFalse(controller.TryFindSingle(entity, out _));
            Assert.AreEqual(0, controller.FindAll(entity).Count);
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
                    "Second.Release",
                    "First.Release"
                },
                trace
            );

            yield return null;
        }

    #endregion

    #region X-2: Presentation Create + Actor rollback 복합 실패

        // ------------------------------------------------------------
        /// <summary>
        /// 생성·Actor 반환 실패의 복합 예외를 검증한다.
        /// </summary>
        // ------------------------------------------------------------
        [UnityTest]
        public IEnumerator TEST_EntityActorRuntime_PresentationCreate과ActorRollback실패를_Aggregate()
        {
            var (runtime, controller, coordinator, factory) = CreateRuntime();
            var trace = new List<string>();
            var first = new PresentationProviderProbe("First", trace);
            var second = new PresentationProviderProbe("Second", trace)
            {
                FailOnCreate = true
            };
            var entity = new EntitySample();
            var context = default(EntityActorNoContext);
            coordinator.AddProvider(first);
            coordinator.AddProvider(second);
            factory.FailOnCleanup = true;

            var exception = Assert.Throws<AggregateException>
            (
                () => runtime.Create(entity, context)
            );

            Assert.AreEqual(2, exception.InnerExceptions.Count);
            Assert.IsFalse(controller.TryFindSingle(entity, out _));
            Assert.AreEqual(0, controller.FindAll(entity).Count);
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

        // ------------------------------------------------------------
        /// <summary>
        /// 표현 반환 실패 후에도 Actor 반환을 검증한다.
        /// </summary>
        // ------------------------------------------------------------
        [UnityTest]
        public IEnumerator TEST_EntityActorRuntime_PresentationRelease실패에도_Actor반환을완료()
        {
            var (runtime, controller, coordinator, factory) = CreateRuntime();
            var trace = new List<string>();
            var presentation = new PresentationProviderProbe("Presentation", trace);
            var entity = new EntitySample();
            var context = default(EntityActorNoContext);
            coordinator.AddProvider(presentation);

            var actor = runtime.Create(entity, context);
            presentation.FailOnRelease = true;

            Assert.Throws<InvalidOperationException>
            (
                () => runtime.Release(actor, DespawnReason.Removed)
            );

            Assert.IsFalse(controller.Contains(actor));
            Assert.IsNull(actor.Entity);
            Assert.IsTrue(factory.HasReleased);
            Assert.AreEqual(DespawnReason.Removed, factory.LastReleaseReason);

            yield return null;
        }

    #endregion

    #region X-4: Presentation + Actor 반환 복합 실패

        // ------------------------------------------------------------
        /// <summary>
        /// 표현과 Actor 반환 오류의 집계를 검증한다.
        /// </summary>
        // ------------------------------------------------------------
        [UnityTest]
        public IEnumerator TEST_EntityActorRuntime_Presentation과Actor반환실패를_Aggregate()
        {
            var (runtime, controller, coordinator, factory) = CreateRuntime();
            var trace = new List<string>();
            var presentation = new PresentationProviderProbe("Presentation", trace);
            var entity = new EntitySample();
            var context = default(EntityActorNoContext);
            coordinator.AddProvider(presentation);

            var actor = runtime.Create(entity, context);
            presentation.FailOnRelease = true;
            factory.FailOnCleanup = true;

            var exception = Assert.Throws<AggregateException>
            (
                () => runtime.Release(actor, DespawnReason.Removed)
            );

            Assert.AreEqual(2, exception.InnerExceptions.Count);
            Assert.IsFalse(controller.Contains(actor));
            Assert.IsNull(actor.Entity);
            Assert.IsTrue(factory.HasReleased);
            Assert.AreEqual(DespawnReason.Removed, factory.LastReleaseReason);

            yield return null;
        }

    #endregion

    #region B-1: Registry 자동 Actor + Presentation 조합

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> 자동 Actor는 독립 Rebind·Release를 거절한다.
        /// <br/> Entity의 등록 종료 경로에서만 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        [UnityTest]
        public IEnumerator TEST_EntityActorRuntimeBinding_자동Actor의독립Rebind와Release를거절()
        {
            var (runtime, controller, coordinator, _) = CreateRuntime();
            var trace = new List<string>();
            coordinator.AddProvider(new PresentationProviderProbe("Visual", trace));
            var registry = new RegistryHarness();
            var binding = new EntityActorRuntimeBinding<ActorProbe, EntitySample>(runtime);

            Assert.IsTrue(registry.TrySpawn(out var entity));
            binding.Bind(registry);
            Assert.IsTrue(binding.TryFind(entity, out var actor));

            var next = new EntitySample();

            Assert.IsTrue(runtime.Rebind(actor, entity));
            Assert.Throws<InvalidOperationException>
            (
                () => runtime.Rebind(actor, next)
            );
            Assert.Throws<InvalidOperationException>
            (
                () => runtime.Release(actor, DespawnReason.Removed)
            );

            Assert.AreSame(entity, actor.Entity);
            Assert.IsTrue(controller.Contains(actor));
            Assert.IsTrue(binding.TryFind(entity, out var found));
            Assert.AreSame(actor, found);
            Assert.AreEqual(1, trace.FindAll(item => item == "Visual.Create").Count);
            Assert.AreEqual(0, trace.FindAll(item => item == "Visual.Release").Count);

            // 자동 Binding 중에도 수동 Actor의 독립적인 Rebind는 유지한다.
            var manualEntity = new EntitySample();
            var manual = runtime.Create(manualEntity, default);
            Assert.IsTrue(runtime.Rebind(manual, next));
            Assert.IsTrue(runtime.Release(manual, DespawnReason.Removed));

            entity.Despawn();

            Assert.IsFalse(binding.TryFind(entity, out _));
            Assert.IsFalse(controller.Contains(actor));
            Assert.AreEqual(2, trace.FindAll(item => item == "Visual.Release").Count);

            binding.Unbind();
            yield return null;
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// 기존·신규 Entity의 Actor와 Presentation을 자동으로 조립하고 정리한다.
        /// </summary>
        // ----------------------------------------------------------------------
        [UnityTest]
        public IEnumerator TEST_EntityActorRuntimeBinding_기존과_신규_Entity를_표현과함께_동기화()
        {
            var (runtime, controller, coordinator, _) = CreateRuntime();
            var trace = new List<string>();
            var presentation = new PresentationProviderProbe("Visual", trace);
            var registry = new RegistryHarness();
            var binding = new EntityActorRuntimeBinding<ActorProbe, EntitySample>(runtime);
            coordinator.AddProvider(presentation);

            Assert.IsTrue(registry.TrySpawn(out var existing));
            binding.Bind(registry);

            Assert.AreSame(registry, binding.BoundRegistry);
            Assert.IsTrue(binding.TryFind(existing, out var firstActor));
            Assert.IsTrue(controller.Contains(firstActor));
            Assert.AreSame(existing, firstActor.Entity);

            Assert.IsTrue(registry.TrySpawn(out var added));
            Assert.IsTrue(binding.TryFind(added, out var nextActor));
            Assert.AreSame(added, nextActor.Entity);
            Assert.AreEqual(2, trace.Count);

            existing.Despawn();

            Assert.IsFalse(binding.TryFind(existing, out _));
            Assert.IsFalse(controller.Contains(firstActor));
            Assert.IsTrue(binding.TryFind(added, out _));

            binding.Unbind();

            Assert.IsNull(binding.BoundRegistry);
            Assert.IsFalse(binding.TryFind(added, out _));
            Assert.IsFalse(controller.Contains(nextActor));
            Assert.AreEqual(1, registry.Registered.Count);
            Assert.AreEqual(4, trace.Count);

            added.Despawn();
            yield return null;
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// 자동 Registry 수명은 다른 사용자가 수동 생성한 Actor를 반환하지 않는다.
        /// </summary>
        // ----------------------------------------------------------------------
        [UnityTest]
        public IEnumerator TEST_EntityActorRuntimeBinding_수동_Actor의_소유권을_유지()
        {
            var (runtime, controller, coordinator, _) = CreateRuntime();
            var trace = new List<string>();
            coordinator.AddProvider(new PresentationProviderProbe("Visual", trace));
            var registry = new RegistryHarness();
            var binding = new EntityActorRuntimeBinding<ActorProbe, EntitySample>(runtime);
            var manualEntity = new EntitySample();
            var manualActor = runtime.Create(manualEntity, default);

            Assert.IsTrue(registry.TrySpawn(out var automaticEntity));
            binding.Bind(registry);

            Assert.IsTrue(binding.TryFind(automaticEntity, out var automaticActor));
            Assert.IsTrue(controller.Contains(manualActor));
            Assert.IsTrue(controller.Contains(automaticActor));

            binding.Unbind();

            Assert.IsFalse(controller.Contains(automaticActor));
            Assert.IsTrue(controller.Contains(manualActor));
            Assert.AreSame(manualEntity, manualActor.Entity);
            Assert.IsTrue(runtime.Release(manualActor, DespawnReason.Removed));
            automaticEntity.Despawn();

            Assert.AreEqual(2, trace.FindAll(item => item == "Visual.Create").Count);
            Assert.AreEqual(2, trace.FindAll(item => item == "Visual.Release").Count);

            yield return null;
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// Presentation 실패로 기존 Registry 동기화가 중단되면 Actor를 롤백한다.
        /// </summary>
        // ----------------------------------------------------------------------
        [UnityTest]
        public IEnumerator TEST_EntityActorRuntimeBinding_동기화실패는_Registry를_보존하고_Actor를_롤백()
        {
            var (runtime, controller, coordinator, _) = CreateRuntime();
            var trace = new List<string>();
            var first = new PresentationProviderProbe("First", trace);
            var second = new PresentationProviderProbe("Second", trace)
            {
                FailOnCreate = true
            };
            coordinator.AddProvider(first);
            coordinator.AddProvider(second);
            var registry = new RegistryHarness();
            Assert.IsTrue(registry.TrySpawn(out var existing));
            var binding = new EntityActorRuntimeBinding<ActorProbe, EntitySample>(runtime);

            Assert.Throws<InvalidOperationException>(() => binding.Bind(registry));

            Assert.IsNull(binding.BoundRegistry);
            Assert.IsFalse(binding.TryFind(existing, out _));
            Assert.AreEqual(0, controller.FindAll(existing).Count);
            Assert.AreEqual(1, registry.Registered.Count);
            Assert.AreSame(existing, registry.Find(existing.Key));
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

            second.FailOnCreate = false;
            binding.Bind(registry);

            Assert.IsTrue(binding.TryFind(existing, out _));

            binding.Unbind();
            existing.Despawn();
            yield return null;
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// Provider 반환이 실패해도 Registry Despawn과 Actor 소유 수명을 종료한다.
        /// </summary>
        // ----------------------------------------------------------------------
        [UnityTest]
        public IEnumerator TEST_EntityActorRuntimeBinding_Presentation반환실패는_terminal로_정리()
        {
            var (runtime, controller, coordinator, _) = CreateRuntime();
            var trace = new List<string>();
            var presentation = new PresentationProviderProbe("Visual", trace);
            coordinator.AddProvider(presentation);
            var registry = new RegistryHarness();
            var binding = new EntityActorRuntimeBinding<ActorProbe, EntitySample>(runtime);
            Assert.IsTrue(registry.TrySpawn(out var entity));
            binding.Bind(registry);

            Assert.IsTrue(binding.TryFind(entity, out var actor));
            presentation.FailOnRelease = true;

            Assert.Throws<InvalidOperationException>(() => entity.Despawn());

            Assert.IsFalse(binding.TryFind(entity, out _));
            Assert.IsFalse(controller.Contains(actor));
            Assert.AreEqual(0, registry.Registered.Count);
            Assert.IsFalse(entity.HasKey);

            binding.Unbind();

            Assert.IsNull(binding.BoundRegistry);
            Assert.AreEqual(1, trace.FindAll(item => item == "Visual.Release").Count);
            yield return null;
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> 복수 Actor의 Provider 반환이 실패해도 모두 terminal 정리하고
        /// <br/> Registry 수명은 유지한다.
        /// </summary>
        // ----------------------------------------------------------------------
        [UnityTest]
        public IEnumerator TEST_EntityActorRuntimeBinding_다중Provider반환실패도_모든Actor정리()
        {
            var (runtime, controller, coordinator, _) = CreateRuntime();
            var trace = new List<string>();
            var provider = new PresentationProviderProbe("Visual", trace);
            coordinator.AddProvider(provider);

            var registry = new RegistryHarness();
            Assert.IsTrue(registry.TrySpawn(out var first));
            Assert.IsTrue(registry.TrySpawn(out var second));

            var binding = new EntityActorRuntimeBinding<ActorProbe, EntitySample>(runtime);
            binding.Bind(registry);

            Assert.IsTrue(binding.TryFind(first, out var firstActor));
            Assert.IsTrue(binding.TryFind(second, out var secondActor));

            provider.FailOnRelease = true;
            var exception = Assert.Throws<AggregateException>(() => binding.Unbind());

            Assert.AreEqual(2, exception.InnerExceptions.Count);
            Assert.AreEqual(2, trace.FindAll(item => item == "Visual.Release").Count);
            Assert.IsFalse(controller.Contains(firstActor));
            Assert.IsFalse(controller.Contains(secondActor));
            Assert.IsFalse(binding.TryFind(first, out _));
            Assert.IsFalse(binding.TryFind(second, out _));
            Assert.IsNull(binding.BoundRegistry);
            Assert.AreEqual(2, registry.Registered.Count);

            binding.Unbind();
            Assert.AreEqual(2, trace.FindAll(item => item == "Visual.Release").Count);

            provider.FailOnRelease = false;
            binding.Bind(registry);

            Assert.IsTrue(binding.TryFind(first, out _));
            Assert.IsTrue(binding.TryFind(second, out _));

            binding.Unbind();
            first.Despawn();
            second.Despawn();
            yield return null;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> 역직렬화된 Registry에 Binder를 연결해 현재 표현만 생성한다.
        /// <br/> 과거 Entity의 Spawn 알림은 재생하지 않는다.
        /// </summary>
        // ------------------------------------------------------------
        [UnityTest]
        public IEnumerator TEST_EntityActorRuntimeBinding_복원된Registry는_현재표현만_생성()
        {
            var (runtime, controller, coordinator, _) = CreateRuntime();
            var trace = new List<string>();
            coordinator.AddProvider(new PresentationProviderProbe("Visual", trace));
            var registry = new RegistryHarness();
            Assert.IsTrue(registry.TrySpawn(out var original));

            var restored = JsonUtility.FromJson<RegistryHarness>(JsonUtility.ToJson(registry));
            var entity = restored.Find(original.Key);
            var spawnNotifications = 0;
            restored.OnSpawned += (_, _) => spawnNotifications++;

            var binding = new EntityActorRuntimeBinding<ActorProbe, EntitySample>(runtime);
            binding.Bind(restored);

            Assert.IsNotNull(entity);
            Assert.AreEqual(SpawnState.Spawned, entity.SpawnState);
            Assert.AreEqual(0, spawnNotifications);
            Assert.IsTrue(binding.TryFind(entity, out var actor));
            Assert.AreSame(entity, actor.Entity);
            CollectionAssert.AreEqual
            (
                new[]
                {
                    "Visual.Create"
                },
                trace
            );

            binding.Unbind();

            Assert.IsFalse(controller.Contains(actor));
            Assert.AreEqual(1, restored.Registered.Count);
            Assert.AreEqual(0, spawnNotifications);
            CollectionAssert.AreEqual
            (
                new[]
                {
                    "Visual.Create",
                    "Visual.Release"
                },
                trace
            );

            entity.Despawn();
            yield return null;
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> 로컬 기본 조합을 한 번에 연결하고, Unbind에서 표현만 정리한다.
        /// <br/> Entity Registry와 외부 Factory의 소유권은 보존한다.
        /// </summary>
        // ----------------------------------------------------------------------
        [UnityTest]
        public IEnumerator TEST_EntityActorRuntimeBinding_로컬기본조합은_Registry를_보존()
        {
            var (_, _, _, factory) = CreateRuntime();
            var registry = new RegistryHarness();
            var trace = new List<string>();
            var visual = new PresentationProviderProbe("Visual", trace);

            Assert.IsTrue(registry.TrySpawn(out var existing));

            var binding = EntityActorRuntimeBinding<ActorProbe, EntitySample>.CreateAndBind
            (
                registry,
                factory,
                visual
            );

            Assert.AreSame(registry, binding.BoundRegistry);
            Assert.IsTrue(binding.TryFind(existing, out var first));
            Assert.AreSame(existing, first.Entity);

            Assert.IsTrue(registry.TrySpawn(out var added));
            Assert.IsTrue(binding.TryFind(added, out var second));
            Assert.AreEqual(2, trace.FindAll(item => item == "Visual.Create").Count);

            binding.Unbind();

            Assert.IsNull(binding.BoundRegistry);
            Assert.IsFalse(binding.TryFind(existing, out _));
            Assert.IsFalse(binding.TryFind(added, out _));
            Assert.AreEqual(2, trace.FindAll(item => item == "Visual.Release").Count);
            Assert.AreEqual(2, registry.Registered.Count);

            existing.Despawn();
            added.Despawn();
            yield return null;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Provider를 사용하지 않는 로컬 자동 Actor 조합도 지원한다.
        /// </summary>
        // ------------------------------------------------------------
        [UnityTest]
        public IEnumerator TEST_EntityActorRuntimeBinding_Provider없는_로컬Actor구성()
        {
            var (_, _, _, factory) = CreateRuntime();
            var registry = new RegistryHarness();

            var binding = EntityActorRuntimeBinding<ActorProbe, EntitySample>.CreateAndBind
            (
                registry,
                factory
            );

            Assert.IsTrue(registry.TrySpawn(out var entity));
            Assert.IsTrue(binding.TryFind(entity, out var actor));
            Assert.AreSame(entity, actor.Entity);

            entity.Despawn();

            Assert.IsFalse(binding.TryFind(entity, out _));
            binding.Unbind();
            Assert.IsNull(binding.BoundRegistry);
            yield return null;
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> 권한 Registry와 공개 Client Registry는 독립된 Entity 수명을 가진다.
        /// <br/> 클라이언트에서 관측이 끝나도 권한 World의 Entity는 제거되지 않는다.
        /// </summary>
        // ----------------------------------------------------------------------
        [UnityTest]
        public IEnumerator TEST_EntityActorRuntimeBinding_공개대상만_클라이언트Registry에_구성()
        {
            var (_, _, _, factory) = CreateRuntime();
            var authority = new RegistryHarness();
            var client = new RegistryHarness();

            Assert.IsTrue(authority.TrySpawn(out var visible));
            Assert.IsTrue(authority.TrySpawn(out var hidden));

            // 어떤 대상이 공개되는지는 외부 Host의 판단이며 Foundation은 그 결정만 구성한다.
            var replica = client.RegisterWithKey(new EntitySample(), visible.Key);
            var binding = EntityActorRuntimeBinding<ActorProbe, EntitySample>.CreateAndBind
            (
                client,
                factory
            );

            Assert.AreEqual(2, authority.Registered.Count);
            Assert.AreEqual(1, client.Registered.Count);
            Assert.IsNull(client.Find(hidden.Key));
            Assert.IsFalse(binding.TryFind(hidden, out _));
            Assert.IsTrue(binding.TryFind(replica, out var actor));
            Assert.AreSame(replica, actor.Entity);

            // Observer의 Replica 제거는 권한 Entity의 Despawn이 아니다.
            replica.Despawn();

            Assert.IsFalse(binding.TryFind(replica, out _));
            Assert.AreSame(visible, authority.Find(visible.Key));
            Assert.AreSame(hidden, authority.Find(hidden.Key));

            // 재발견에서는 동일 World Key로 새 로컬 등록 수명과 표현을 구성한다.
            var restoredReplica = client.RegisterWithKey(new EntitySample(), visible.Key);
            Assert.IsTrue(binding.TryFind(restoredReplica, out var restoredActor));
            Assert.AreSame(restoredReplica, restoredActor.Entity);

            binding.Unbind();
            restoredReplica.Despawn();
            visible.Despawn();
            hidden.Despawn();
            yield return null;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> 이미 준비한 현재 상태를 Actor가 읽으며
        /// <br/> 게임 Spawn 이벤트를 재생하지 않는다.
        /// </summary>
        // ------------------------------------------------------------
        [UnityTest]
        public IEnumerator TEST_EntityActorRuntimeBinding_현재상태구성은_Actor생성보다_먼저적용()
        {
            var (_, _, _, factory) = CreateRuntime();
            var registry = new RegistryHarness();
            var trace = new List<string>();
            var provider = new PresentationProviderProbe("Visual", trace)
            {
                InspectOnCreate = entity => Assert.AreEqual(77, entity.CurrentValue)
            };

            var binding = EntityActorRuntimeBinding<ActorProbe, EntitySample>.CreateAndBind
            (
                registry,
                factory,
                provider
            );

            var spawnEvents = 0;
            registry.OnSpawning += (_, _) => spawnEvents++;
            registry.OnSpawned += (_, _) => spawnEvents++;

            var prepared = new EntitySample
            {
                CurrentValue = 77
            };

            var entity = registry.RegisterWithKey(prepared, 200UL);

            Assert.IsTrue(binding.TryFind(entity, out var actor));
            Assert.AreSame(entity, actor.Entity);
            Assert.AreEqual(0, spawnEvents);
            CollectionAssert.AreEqual
            (
                new[]
                {
                    "Visual.Create"
                },
                trace
            );

            entity.Despawn();

            Assert.IsFalse(binding.TryFind(entity, out _));
            Assert.AreEqual(0, registry.Registered.Count);
            Assert.AreEqual(0, spawnEvents);
            Assert.AreEqual(1, trace.FindAll(item => item == "Visual.Release").Count);

            binding.Unbind();
            yield return null;
        }

    #endregion

    }
}
