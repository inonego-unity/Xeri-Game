/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : TEST_EntityActorController.cs
수정일 : 2026-11-16

# 설명
EntityActorController의 Detached ↔ Bound Actor 관계와 Rebind 트랜잭션을 검증한다.
객체 Create/Release와 Entity Attach/Detach, 수명 훅 중 재진입의 상태 정합성을 확인한다.
Unity Test Runner (Play Mode)에서 실행한다.

# 테스트 구성
 E: 명시적 0..N Actor 수명
 X: Actor binding failure boundary
 C: Actor 단위 수명 재진입, Runtime 소유권 이전 사전 거절과 ReleaseAll 수명 식별
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

    // ============================================================
    /// <summary>
    /// EntityActorController의 명시적 0..N 수명을 검증한다.
    /// </summary>
    // ============================================================
    public class TEST_EntityActorController
    {

    #region 헬퍼

        [Serializable]
        private sealed class EntitySample : EntityBase
        {
            // NONE
        }

        private sealed class ActorProbe : EntityActorBase<EntitySample>
        {
            private readonly Dictionary<EntitySample, string> bindFailures = new();
            private string nextUnbindFailure = null;

            // ------------------------------------------------------------
            /// <summary>
            /// Entity Bind 훅에서 호출할 검증 작업.
            /// </summary>
            // ------------------------------------------------------------
            public Action OnBindAction { get; set; }

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
            public void FailBind(EntitySample entity, string message)
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

            protected override void OnBindEntity(EntitySample entity)
            {
                OnBindAction?.Invoke();

                if (bindFailures.Remove(entity, out var message))
                {
                    throw new InvalidOperationException(message);
                }
            }

            protected override void OnUnbindEntity(EntitySample previousEntity)
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
        private sealed class EntityRegistryHarness : EntitySpawnRegistry<EntitySample>
        {
            protected override EntitySample Acquire()
            {
                return new EntitySample();
            }

            // ------------------------------------------------------------
            /// <summary>
            /// Registry Spawn을 테스트에 노출한다.
            /// </summary>
            // ------------------------------------------------------------
            public new bool TrySpawn(out EntitySample spawned)
            {
                return base.TrySpawn(out spawned);
            }
        }

        private sealed class ActorFactoryProbe : EntityActorFactory<ActorProbe, EntitySample>
        {
            // ------------------------------------------------------------
            /// <summary>
            /// Actor 생성 직전에 수행할 검증 작업.
            /// </summary>
            // ------------------------------------------------------------
            public Action OnCreate { get; set; }

            // ------------------------------------------------------------
            /// <summary>
            /// Actor 반환 직전에 수행할 검증 작업.
            /// </summary>
            // ------------------------------------------------------------
            public Action<ActorProbe> OnRelease { get; set; }

            // ------------------------------------------------------------
            /// <summary>
            /// Provider 반환을 완료한 뒤 반환 훅을 호출할지 여부.
            /// </summary>
            // ------------------------------------------------------------
            public bool InvokeReleaseHookAfterBase { get; set; }

            // ------------------------------------------------------------
            /// <summary>
            /// Actor 반환 콜백이 호출된 횟수.
            /// </summary>
            // ------------------------------------------------------------
            public int ReleaseCount { get; private set; }

            // ------------------------------------------------------------
            /// <summary>
            /// GameObject Provider로 검증용 Factory를 생성한다.
            /// </summary>
            // ------------------------------------------------------------
            public ActorFactoryProbe(IGameObjectProvider provider) : base(provider)
            {
                // NONE
            }

            // ------------------------------------------------------------
            /// <summary>
            /// 생성 훅을 수행한 뒤 Actor를 생성한다.
            /// </summary>
            // ------------------------------------------------------------
            public override ActorProbe Create(EntitySample entity, in EntityActorNoContext context)
            {
                OnCreate?.Invoke();
                return base.Create(entity, context);
            }

            // ------------------------------------------------------------
            /// <summary>
            /// 반환 횟수를 기록하고 반환 훅을 수행한 뒤 Actor를 반환한다.
            /// </summary>
            // ------------------------------------------------------------
            public override void Release(ActorProbe actor, DespawnReason reason)
            {
                ReleaseCount++;

                if (InvokeReleaseHookAfterBase)
                {
                    base.Release(actor, reason);
                    OnRelease?.Invoke(actor);
                    return;
                }

                OnRelease?.Invoke(actor);
                base.Release(actor, reason);
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

        private ActorFactoryProbe factory = null;

        private
        (
            EntityActorController<ActorProbe, EntitySample> controller,
            EntityRegistryHarness registry
        ) CreateRuntime()
        {
            prefab = new GameObject("TestPrefab");
            prefab.AddComponent<ActorProbe>();
            prefab.SetActive(false);

            var provider = new PrefabGameObjectProvider(prefab, null);
            factory = new ActorFactoryProbe(provider);
            var controller = new EntityActorController<ActorProbe, EntitySample>(factory);
            var registry = new EntityRegistryHarness();

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

            Assert.AreEqual(2, controller.Actors.Count());
            Assert.AreEqual(2, controller.DetachedActors.Count());
            Assert.AreEqual(0, controller.FindAll(firstEntity).Count);
            Assert.IsFalse(first.IsBound);
            Assert.IsNull(first.Entity);

            Assert.IsTrue(controller.Attach(first, firstEntity));
            Assert.IsTrue(controller.Attach(second, firstEntity));
            Assert.AreEqual(2, controller.FindAll(firstEntity).Count);
            Assert.IsFalse(controller.TryFindSingle(firstEntity, out _));

            Assert.IsTrue(controller.Detach(first));
            Assert.IsTrue(controller.Contains(first));
            Assert.IsFalse(controller.IsBound(first));
            Assert.IsNull(first.Entity);
            Assert.AreEqual(1, controller.DetachedActors.Count());

            Assert.IsTrue(controller.Rebind(second, secondEntity));
            Assert.AreSame(secondEntity, second.Entity);
            Assert.AreEqual(0, controller.FindAll(firstEntity).Count);
            Assert.IsTrue(controller.TryFindSingle(secondEntity, out var rebound));
            Assert.AreSame(second, rebound);

            Assert.IsTrue(controller.Release(first, DespawnReason.Removed));
            Assert.IsTrue(controller.Release(second, DespawnReason.Removed));
            Assert.AreEqual(0, controller.Actors.Count());

            firstEntity.Despawn();
            secondEntity.Despawn();

            yield return null;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> Actor 집합과 Entity별 목록의 View 재사용을 검증한다.
        /// <br/> 외부 변경을 차단하고 실제 연결 변경을 반영한다.
        /// </summary>
        // ------------------------------------------------------------
        [UnityTest]
        public IEnumerator TEST_EntityActorController_조회View는_재사용되고_내부수정이차단됨()
        {
            var (controller, registry) = CreateRuntime();
            registry.TrySpawn(out var entity);

            var all = controller.Actors;
            Assert.AreSame(all, controller.Actors);
            Assert.IsFalse(((object)all) is HashSet<ActorProbe>);

            var first = controller.Create(entity);
            var second = controller.Create(entity);
            Assert.AreEqual(2, all.Count());

            Assert.IsTrue(controller.Attach(first, entity));
            var entityView = controller.FindAll(entity);

            Assert.AreSame(entityView, controller.FindAll(entity));
            Assert.IsFalse(entityView is List<ActorProbe>);
            Assert.Throws<NotSupportedException>
            (
                () => ((IList<ActorProbe>)entityView).Clear()
            );

            Assert.IsTrue(controller.Attach(second, entity));
            Assert.AreSame(entityView, controller.FindAll(entity));
            Assert.AreEqual(2, entityView.Count);

            Assert.IsTrue(controller.Detach(first));
            Assert.AreEqual(1, entityView.Count);
            Assert.AreSame(second, entityView[0]);

            Assert.IsTrue(controller.Release(first, DespawnReason.Removed));
            Assert.IsTrue(controller.Release(second, DespawnReason.Removed));
            Assert.AreEqual(0, all.Count());
            Assert.AreEqual(0, entityView.Count);

            var third = controller.Create(entity);
            Assert.IsTrue(controller.Attach(third, entity));
            Assert.AreNotSame(entityView, controller.FindAll(entity));
            Assert.AreEqual(0, entityView.Count);

            Assert.IsTrue(controller.Release(third, DespawnReason.Removed));
            entity.Despawn();
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

            Assert.IsTrue(controller.Contains(actor));
            Assert.IsFalse(controller.IsBound(actor));
            Assert.IsNull(actor.Entity);
            Assert.AreEqual(0, controller.FindAll(entity).Count);
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

            Assert.IsTrue(controller.Contains(actor));
            Assert.IsFalse(controller.IsBound(actor));
            Assert.IsNull(actor.Entity);
            Assert.AreEqual(0, controller.FindAll(entity).Count);
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
            Assert.IsTrue(controller.IsBound(actor));
            Assert.AreEqual(0, controller.FindAll(next).Count);
            Assert.IsTrue(controller.TryFindSingle(previous, out var restored));
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
            Assert.IsFalse(controller.IsBound(actor));
            Assert.AreEqual(0, controller.FindAll(previous).Count);
            Assert.AreEqual(0, controller.FindAll(next).Count);

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

            Assert.IsFalse(controller.Contains(actor));
            Assert.AreEqual(0, controller.Actors.Count());
            Assert.AreEqual(0, controller.DetachedActors.Count());

            entity.Despawn();

            yield return null;
        }

    #endregion

    #region C-1: Actor 단위 수명 재진입

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/> 단독 Controller의 Actor Bind·Unbind 콜백에서 같은 Actor의 수명 작업을 거절한다.
        /// <br/> 독립 Actor 생성은 허용하며 재진입 거절 뒤에도 객체 반환은 원래 순서대로 수행된다.
        /// </summary>
        // --------------------------------------------------------------------------------
        [UnityTest]
        public IEnumerator TEST_EntityActorController_Actor훅중_같은Actor재진입만_거절()
        {
            var (controller, registry) = CreateRuntime();
            Assert.IsTrue(registry.TrySpawn(out var entity));
            var actor = controller.Create(entity);
            var attempts = 0;
            ActorProbe independent = null;

            actor.OnBindAction = () =>
            {
                attempts++;
                Assert.Throws<InvalidOperationException>
                (
                    () => controller.Release(actor, DespawnReason.Removed)
                );
                independent = controller.Create(new EntitySample());
            };

            Assert.IsTrue(controller.Attach(actor, entity));
            Assert.IsTrue(controller.Contains(actor));
            Assert.IsNotNull(independent);
            Assert.IsTrue(controller.Contains(independent));
            Assert.IsFalse(independent.IsBound);

            actor.OnUnbindAction = () =>
            {
                attempts++;
                Assert.Throws<InvalidOperationException>
                (
                    () => controller.Rebind(actor, new EntitySample())
                );
                Assert.Throws<InvalidOperationException>
                (
                    () => controller.Release(actor, DespawnReason.Removed)
                );
            };

            Assert.IsTrue(controller.Detach(actor));
            Assert.IsFalse(actor.IsBound);
            Assert.AreEqual(2, attempts);
            Assert.IsTrue(controller.Release(actor, DespawnReason.Removed));
            Assert.IsTrue(controller.Release(independent, DespawnReason.Removed));
            Assert.IsFalse(controller.Contains(actor));
            Assert.IsFalse(controller.Contains(independent));

            entity.Despawn();
            yield return null;
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> 단독 Controller의 Actor 반환 콜백에서 다른 Actor를 반환할 수 있다.
        /// <br/> 같은 Actor의 반환·Rebind와 A → B → A 순환 반환은 거절한다.
        /// </summary>
        // ----------------------------------------------------------------------
        [UnityTest]
        public IEnumerator TEST_EntityActorController_Actor반환중_다른Actor반환을_허용하고_순환은_거절()
        {
            var (controller, registry) = CreateRuntime();
            Assert.IsTrue(registry.TrySpawn(out var entity));
            var first = controller.Create(entity);
            var second = controller.Create(entity);
            Assert.IsTrue(controller.Attach(first, entity));
            Assert.IsTrue(controller.Attach(second, entity));
            var secondReleased = false;
            var rejected = 0;

            first.OnUnbindAction = () =>
            {
                Assert.Throws<InvalidOperationException>
                (
                    () => controller.Release(first, DespawnReason.Removed)
                );
                Assert.Throws<InvalidOperationException>
                (
                    () => controller.Rebind(first, new EntitySample())
                );
                secondReleased = controller.Release(second, DespawnReason.Removed);
            };
            second.OnUnbindAction = () =>
            {
                Assert.Throws<InvalidOperationException>
                (
                    () => controller.Release(first, DespawnReason.Removed)
                );
                rejected++;
            };

            Assert.IsTrue(controller.Release(first, DespawnReason.Removed));

            Assert.IsTrue(secondReleased);
            Assert.AreEqual(1, rejected);
            Assert.IsFalse(controller.Contains(first));
            Assert.IsFalse(controller.Contains(second));
            Assert.AreEqual(0, controller.FindAll(entity).Count);
            Assert.AreEqual(2, factory.ReleaseCount);

            entity.Despawn();
            yield return null;
        }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/> 반환 콜백 중 중첩된 ReleaseAll은 진행 중인 Actor만 거절하고 나머지를 반환한다.
        /// <br/> 바깥 ReleaseAll은 이미 반환된 Actor를 다시 반환하지 않는다.
        /// </summary>
        // --------------------------------------------------------------------------------
        [UnityTest]
        public IEnumerator TEST_EntityActorController_중첩ReleaseAll은_누락도_중복도없음()
        {
            var (controller, registry) = CreateRuntime();
            Assert.IsTrue(registry.TrySpawn(out var entity));
            var first = controller.Create(entity);
            var second = controller.Create(entity);
            var third = controller.Create(entity);
            Assert.IsTrue(controller.Attach(first, entity));
            Assert.IsTrue(controller.Attach(second, entity));
            Assert.IsTrue(controller.Attach(third, entity));
            var nestedAttempts = 0;

            first.OnUnbindAction = () =>
            {
                nestedAttempts++;
                Assert.Throws<InvalidOperationException>
                (
                    () => controller.ReleaseAll(entity, DespawnReason.Removed)
                );
            };

            controller.ReleaseAll(DespawnReason.Removed);

            Assert.AreEqual(1, nestedAttempts);
            Assert.AreEqual(0, controller.Actors.Count());
            Assert.AreEqual(0, controller.FindAll(entity).Count);
            Assert.AreEqual(3, factory.ReleaseCount);

            entity.Despawn();
            yield return null;
        }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/> Actor 생성 중에는 Controller의 Runtime 소유권 이전을 시작 전에 거절한다.
        /// <br/> 원래 생성은 standalone 소유권으로 완료되고 정상 반환할 수 있다.
        /// </summary>
        // --------------------------------------------------------------------------------
        [UnityTest]
        public IEnumerator TEST_EntityActorController_Actor생성중_Runtime소유권이전을_거절()
        {
            var (controller, registry) = CreateRuntime();
            Assert.IsTrue(registry.TrySpawn(out var entity));
            var coordinator = new EntityPresentationCoordinator<ActorProbe, EntitySample>();
            var rejected = 0;

            factory.OnCreate = () =>
            {
                Assert.Throws<InvalidOperationException>
                (
                    () => new EntityActorRuntime<ActorProbe, EntitySample, EntityActorNoContext>
                    (
                        controller,
                        coordinator
                    )
                );
                rejected++;
            };

            var actor = controller.Create(entity);
            factory.OnCreate = null;

            Assert.AreEqual(1, rejected);
            Assert.IsTrue(controller.Contains(actor));

            factory.OnRelease = _ =>
            {
                Assert.Throws<InvalidOperationException>
                (
                    () => new EntityActorRuntime<ActorProbe, EntitySample, EntityActorNoContext>
                    (
                        controller,
                        coordinator
                    )
                );
                rejected++;
            };

            Assert.IsTrue(controller.Release(actor, DespawnReason.Removed));
            factory.OnRelease = null;

            Assert.AreEqual(2, rejected);
            Assert.IsFalse(controller.Contains(actor));

            // 생성·반환 transaction이 끝나고 Controller가 비면 Runtime 소유권 이전은 가능하다.
            var runtime =
                new EntityActorRuntime<ActorProbe, EntitySample, EntityActorNoContext>
                (
                    controller,
                    coordinator
                );

            Assert.IsNotNull(runtime);

            entity.Despawn();
            yield return null;
        }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/> Factory Release가 끝나기 전에 같은 Actor 인스턴스를 Create 결과로 재발급하면
        /// <br/> Controller는 새 등록을 시작하지 않고 즉시 거절한다.
        /// </summary>
        // --------------------------------------------------------------------------------
        [UnityTest]
        public IEnumerator TEST_EntityActorController_반환중_같은Actor재발급을_등록전에_거절()
        {
            prefab = new GameObject("TestPrefab");
            prefab.AddComponent<ActorProbe>();
            prefab.SetActive(false);

            var pool = new GameObjectPoolProvider(prefab);
            factory = new ActorFactoryProbe(pool)
            {
                InvokeReleaseHookAfterBase = true
            };
            var controller = new EntityActorController<ActorProbe, EntitySample>(factory);
            var actor = controller.Create(new EntitySample());
            ActorProbe reacquired = null;

            factory.OnRelease = released =>
            {
                Assert.AreSame(actor, released);

                var exception = Assert.Throws<InvalidOperationException>
                (
                    () => reacquired = controller.Create(new EntitySample())
                );

                StringAssert.Contains("반환 중인 Entity Actor", exception.Message);
            };

            Assert.IsTrue(controller.Release(actor, DespawnReason.Removed));
            factory.OnRelease = null;

            Assert.IsNull(reacquired);
            Assert.IsFalse(controller.Contains(actor));
            Assert.AreEqual(1, factory.ReleaseCount);

            // 계약 위반 Factory가 내부적으로 재획득한 자원은 Controller가 중첩 반환하지 않는다.
            factory.Release(actor, DespawnReason.SpawnRollback);
            Assert.AreEqual(2, factory.ReleaseCount);

            pool.DestroyAll();
            yield return null;
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> ReleaseAll 중 먼저 반환한 Actor를 풀이 같은 인스턴스로 다시 내줘도
        /// <br/> 새 수명은 이번 ReleaseAll의 대상이 아니므로 반환하지 않는다.
        /// </summary>
        // ----------------------------------------------------------------------
        [UnityTest]
        public IEnumerator TEST_EntityActorController_ReleaseAll중_풀에서_재사용한_새수명은_반환하지않음()
        {
            prefab = new GameObject("TestPrefab");
            prefab.AddComponent<ActorProbe>();
            prefab.SetActive(false);

            var pool = new GameObjectPoolProvider(prefab);
            factory = new ActorFactoryProbe(pool);
            var controller = new EntityActorController<ActorProbe, EntitySample>(factory);
            var first = controller.Create(new EntitySample());
            var second = controller.Create(new EntitySample());
            ActorProbe other = null;
            ActorProbe reused = null;

            factory.OnRelease = actor =>
            {
                if (other != null)
                {
                    return;
                }

                // 첫 반환 콜백에서 남은 Actor를 먼저 반환하고, 풀이 같은 인스턴스를 새 수명으로 돌려주게 한다.
                other = ReferenceEquals(actor, first) ? second : first;
                Assert.IsTrue(controller.Release(other, DespawnReason.Removed));
                reused = controller.Create(new EntitySample());
            };

            controller.ReleaseAll(DespawnReason.Removed);
            factory.OnRelease = null;

            Assert.AreSame(other, reused);
            Assert.IsTrue(controller.Contains(reused));
            Assert.AreEqual(2, factory.ReleaseCount);

            Assert.IsTrue(controller.Release(reused, DespawnReason.Removed));
            Assert.AreEqual(3, factory.ReleaseCount);

            pool.DestroyAll();
            yield return null;
        }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/> Entity 단위 ReleaseAll도 반환 중 같은 Entity에 다시 연결된 재사용 인스턴스의
        /// <br/> 새 수명은 반환하지 않는다.
        /// </summary>
        // --------------------------------------------------------------------------------
        [UnityTest]
        public IEnumerator TEST_EntityActorController_Entity단위_ReleaseAll중_재사용한_새수명은_반환하지않음()
        {
            prefab = new GameObject("TestPrefab");
            prefab.AddComponent<ActorProbe>();
            prefab.SetActive(false);

            var pool = new GameObjectPoolProvider(prefab);
            factory = new ActorFactoryProbe(pool);
            var controller = new EntityActorController<ActorProbe, EntitySample>(factory);
            var entity = new EntitySample();
            var first = controller.Create(entity);
            var second = controller.Create(entity);
            Assert.IsTrue(controller.Attach(first, entity));
            Assert.IsTrue(controller.Attach(second, entity));
            ActorProbe other = null;
            ActorProbe reused = null;

            factory.OnRelease = actor =>
            {
                if (other != null)
                {
                    return;
                }

                other = ReferenceEquals(actor, first) ? second : first;
                Assert.IsTrue(controller.Release(other, DespawnReason.Removed));
                reused = controller.Create(entity);
                Assert.IsTrue(controller.Attach(reused, entity));
            };

            controller.ReleaseAll(entity, DespawnReason.Removed);
            factory.OnRelease = null;

            Assert.AreSame(other, reused);
            Assert.IsTrue(controller.Contains(reused));
            Assert.IsTrue(controller.IsBound(reused));
            Assert.AreEqual(2, factory.ReleaseCount);

            Assert.IsTrue(controller.Release(reused, DespawnReason.Removed));
            Assert.AreEqual(3, factory.ReleaseCount);

            pool.DestroyAll();
            yield return null;
        }

    #endregion

    }
}
