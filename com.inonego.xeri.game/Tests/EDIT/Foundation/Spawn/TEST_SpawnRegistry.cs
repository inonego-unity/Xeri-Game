/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : TEST_SpawnRegistry.cs
수정일 : 2026-11-16

# 설명
SpawnRegistry의 lifecycle transaction, required binding, committed notification과 self-rehydrate 계약을 검증한다.
단순 Find/Count 동작보다 여러 hook·binding·rollback 경계가 결합된 상태 전이를 우선 검증한다.

# 테스트 구성
 L: 기본 lifecycle과 bulk cleanup
 T: required binding transaction과 commit 경계
 B: 기존 등록 집합에 대한 Binding 동기화와 rollback
 S: 직렬화 후 runtime self-rehydrate
 X: 등록 실패 rollback, 복합 오류 집계와 terminal 정리
========================================================================= BLOCK_HEADER_END */

using System;
using System.Collections;
using System.Collections.Generic;

using UnityEngine;

using NUnit;
using NUnit.Framework;

namespace inonego.Xeri.Game.TEST._Spawn
{
    using Xeri;
    using Xeri.Game;

    // ============================================================
    /// <summary>
    /// SpawnRegistry의 lifecycle transaction 계약을 검증한다.
    /// </summary>
    // ============================================================
    public class TEST_SpawnRegistry
    {

    #region 헬퍼

        // ============================================================
        /// <summary>
        /// Registry 상태 전이와 lifecycle 훅을 관찰하는 테스트 객체.
        /// </summary>
        // ============================================================
        [Serializable]
        private class TestObject : ISpawnRegistryObject<ulong>
        {
            // ------------------------------------------------------------
            /// <summary>
            /// 실제 객체 Spawn 완료 훅의 호출 횟수.
            /// </summary>
            // ------------------------------------------------------------
            public static int SpawnedHookCount { get; set; } = 0;

            // ------------------------------------------------------------
            /// <summary>
            /// 현재 테스트 객체의 Registry Key.
            /// </summary>
            // ------------------------------------------------------------
            public ulong Key => key;

            [SerializeField]
            private ulong key = 0UL;

            // ------------------------------------------------------------
            /// <summary>
            /// 현재 객체에 Registry Key가 설정되어 있는지 여부.
            /// </summary>
            // ------------------------------------------------------------
            public bool HasKey => hasKey;

            [SerializeField]
            private bool hasKey = false;

            // ------------------------------------------------------------
            /// <summary>
            /// Registry가 관리하는 실제 Spawn 상태.
            /// </summary>
            // ------------------------------------------------------------
            public SpawnState SpawnState => spawnState;

            [NonSerialized]
            private SpawnState spawnState = SpawnState.Despawned;

            // ------------------------------------------------------------
            /// <summary>
            /// 객체의 lifecycle 실행 순서를 관찰할 목록.
            /// </summary>
            // ------------------------------------------------------------
            [NonSerialized]
            public List<string> Trace = null;

            // ------------------------------------------------------------
            /// <summary>
            /// Registry가 관리하는 실제 Spawn 상태.
            /// </summary>
            // ------------------------------------------------------------
            SpawnState ISpawnRegistryObject<ulong>.SpawnState
            {
                get => spawnState;
                set => spawnState = value;
            }

            // ------------------------------------------------------------
            /// <summary>
            /// 객체의 Despawn 요청을 Registry에 전달하는 연결.
            /// </summary>
            // ------------------------------------------------------------
            Action<DespawnReason> IDespawnable.DespawnFromRegistry
            {
                get => despawnFromRegistry;
                set => despawnFromRegistry = value;
            }

            [NonSerialized]
            private Action<DespawnReason> despawnFromRegistry = null;

            // ------------------------------------------------------------
            /// <summary>
            /// 테스트 획득 객체에 Registry Key를 설정한다.
            /// </summary>
            // ------------------------------------------------------------
            public void SetKey(ulong value)
            {
                key = value;
                hasKey = true;
            }

            // ------------------------------------------------------------
            /// <summary>
            /// 현재 Registry Key의 유효성을 종료한다.
            /// </summary>
            // ------------------------------------------------------------
            public void ClearKey()
            {
                hasKey = false;
            }

            // ------------------------------------------------------------
            /// <summary>
            /// 객체 생성 시작 훅의 실행 순서를 기록한다.
            /// </summary>
            // ------------------------------------------------------------
            void ISpawnable.OnSpawning()
            {
                Trace?.Add("Object.OnSpawning");
            }

            // ------------------------------------------------------------
            /// <summary>
            /// 등록 완료 참여 순서를 기록하고 지정된 실패를 전달한다.
            /// </summary>
            // ------------------------------------------------------------
            void ISpawnable.OnSpawned()
            {
                SpawnedHookCount++;
                Trace?.Add("Object.OnSpawned");
            }

            // ------------------------------------------------------------
            /// <summary>
            /// 등록 해제 참여 순서를 기록하고 지정된 실패를 전달한다.
            /// </summary>
            // ------------------------------------------------------------
            void IDespawnable.OnDespawning(DespawnReason reason)
            {
                Trace?.Add("Object.OnDespawning");
            }

            // ------------------------------------------------------------
            /// <summary>
            /// 객체 Despawn 완료 훅의 실행 순서를 기록한다.
            /// </summary>
            // ------------------------------------------------------------
            void IDespawnable.OnDespawned(DespawnReason reason)
            {
                Trace?.Add("Object.OnDespawned");
            }
        }

        // ============================================================
        /// <summary>
        /// 필수 lifecycle Binding의 실행 순서와 실패를 관찰한다.
        /// </summary>
        // ============================================================
        private sealed class TestBinding : ISpawnRegistryBinding<ulong, TestObject>
        {
            private readonly string name;
            private readonly List<string> trace;

            // ------------------------------------------------------------
            /// <summary>
            /// 필수 등록 Binding 실패 여부.
            /// </summary>
            // ------------------------------------------------------------
            public bool FailOnRegistered { get; set; }

            // ------------------------------------------------------------
            /// <summary>
            /// N번째 등록 동기화에서 실패한다. 0은 비활성.
            /// </summary>
            // ------------------------------------------------------------
            public int FailOnRegisteredCall { get; set; }

            // ------------------------------------------------------------
            /// <summary>
            /// 등록 Binding에서 호출할 추가 검증 작업.
            /// </summary>
            // ------------------------------------------------------------
            public Action OnRegisteredAction { get; set; }

            private int registeredCallCount = 0;

            // ------------------------------------------------------------
            /// <summary>
            /// 등록 해제 Binding에서 호출할 추가 검증 작업.
            /// </summary>
            // ------------------------------------------------------------
            public Action OnUnregisteringAction { get; set; }

            // ------------------------------------------------------------
            /// <summary>
            /// 필수 등록 해제 Binding 실패 여부.
            /// </summary>
            // ------------------------------------------------------------
            public bool FailOnUnregistering { get; set; }

            // ------------------------------------------------------------
            /// <summary>
            /// 필수 lifecycle Binding의 실행 순서와 실패를 관찰한다.
            /// </summary>
            // ------------------------------------------------------------
            public TestBinding(string name, List<string> trace)
            {
                this.name = name;
                this.trace = trace;
            }

            // ------------------------------------------------------------
            /// <summary>
            /// 등록 완료 참여 순서를 기록하고 지정된 실패를 전달한다.
            /// </summary>
            // ------------------------------------------------------------
            public void OnRegistered(ulong key, TestObject spawnable)
            {
                trace.Add($"{name}.OnRegistered");
                registeredCallCount++;
                OnRegisteredAction?.Invoke();

                if (FailOnRegistered || (FailOnRegisteredCall > 0 && registeredCallCount == FailOnRegisteredCall))
                {
                    throw new InvalidOperationException($"{name} spawn failure");
                }
            }

            // ------------------------------------------------------------
            /// <summary>
            /// 등록 해제 참여 순서를 기록하고 지정된 실패를 전달한다.
            /// </summary>
            // ------------------------------------------------------------
            public void OnUnregistering
            (
                ulong key,
                TestObject despawnable,
                DespawnReason reason
            )
            {
                trace.Add($"{name}.OnUnregistering");
                OnUnregisteringAction?.Invoke();

                if (FailOnUnregistering)
                {
                    throw new InvalidOperationException($"{name} despawn failure");
                }
            }
        }

        // ============================================================
        /// <summary>
        /// 미리 준비한 후보 또는 새 객체를 획득하는 실제 Registry.
        /// </summary>
        // ============================================================
        [Serializable]
        private sealed class TestRegistry : SpawnRegistry<ulong, TestObject>
        {
            [SerializeField]
            private ulong nextKey = 0UL;

            // ------------------------------------------------------------
            /// <summary>
            /// 다음 Spawn에서 획득할 지정 후보.
            /// </summary>
            // ------------------------------------------------------------
            public TestObject NextObject { get; set; }

            // ------------------------------------------------------------
            /// <summary>
            /// 다음 Acquire에서 객체를 제공하지 않는다.
            /// </summary>
            // ------------------------------------------------------------
            public bool ReturnNullOnAcquire { get; set; }

            // ------------------------------------------------------------
            /// <summary>
            /// 획득 출처로 반환하는 시점의 재진입을 검증한다.
            /// </summary>
            // ------------------------------------------------------------
            public Action<TestObject> OnReleaseAction { get; set; }

            // ------------------------------------------------------------
            /// <summary>
            /// 지정 후보 또는 새로운 Key를 가진 객체를 공급한다.
            /// </summary>
            // ------------------------------------------------------------
            protected override TestObject Acquire()
            {
                if (ReturnNullOnAcquire)
                {
                    ReturnNullOnAcquire = false;
                    return null;
                }

                if (NextObject != null)
                {
                    var injected = NextObject;
                    NextObject = null;
                    return injected;
                }

                var obj = new TestObject();
                obj.SetKey(nextKey++);
                return obj;
            }

            // ------------------------------------------------------------
            /// <summary>
            /// 획득 출처 반환 시점의 후속 검증을 수행한다.
            /// </summary>
            // ------------------------------------------------------------
            protected override void Release(TestObject spawnable, DespawnReason reason)
            {
                OnReleaseAction?.Invoke(spawnable);
            }

            // ------------------------------------------------------------
            /// <summary>
            /// 공개 Spawn 계약을 테스트에 노출한다.
            /// </summary>
            // ------------------------------------------------------------
            public new bool TrySpawn(out TestObject spawned)
            {
                return base.TrySpawn(out spawned);
            }

            // ------------------------------------------------------------
            /// <summary>
            /// 지정 객체의 Registry terminal 종료를 실행한다.
            /// </summary>
            // ------------------------------------------------------------
            public new void Despawn(TestObject obj, DespawnReason reason)
            {
                base.Despawn(obj, reason);
            }
        }

        // ============================================================
        /// <summary>
        /// 파라미터 초기화 경로를 검증하는 스폰 대상.
        /// </summary>
        // ============================================================
        [Serializable]
        private sealed class TestParamObject : TestObject, INeedToInit<int>
        {
            // ------------------------------------------------------------
            /// <summary>
            /// 스폰 중 전달된 초기화 파라미터.
            /// </summary>
            // ------------------------------------------------------------
            public int InitializedWith { get; private set; } = -1;

            // ------------------------------------------------------------
            /// <summary>
            /// 파라미터 기반 스폰에서 초기화한다.
            /// </summary>
            // ------------------------------------------------------------
            public void Init(int param)
            {
                InitializedWith = param;
            }

            // ------------------------------------------------------------
            /// <summary>
            /// 초기화 수명을 종료한다.
            /// </summary>
            // ------------------------------------------------------------
            public void Release()
            {
                // NONE
            }
        }

        // ============================================================
        /// <summary>
        /// 파라미터 기반 Acquire 실패와 초기화 호출을 검증하는 Registry.
        /// </summary>
        // ============================================================
        [Serializable]
        private sealed class TestParamRegistry : SpawnRegistry<ulong, TestParamObject, int>
        {
            // ------------------------------------------------------------
            /// <summary>
            /// 다음 Acquire에서 객체를 제공하지 않는다.
            /// </summary>
            // ------------------------------------------------------------
            public bool ReturnNullOnAcquire { get; set; }

            // ------------------------------------------------------------
            /// <summary>
            /// 파라미터 기반 스폰에 사용할 후보를 획득한다.
            /// </summary>
            // ------------------------------------------------------------
            protected override TestParamObject Acquire(int param)
            {
                if (ReturnNullOnAcquire)
                {
                    ReturnNullOnAcquire = false;
                    return null;
                }

                var obj = new TestParamObject();
                obj.SetKey(0UL);
                return obj;
            }
        }

        // ------------------------------------------------------------
        /// <summary>
        /// lifecycle 실행 순서를 기록할 획득 후보를 준비한다.
        /// </summary>
        // ------------------------------------------------------------
        private static TestObject InjectTraceObject
        (
            TestRegistry registry,
            List<string> trace,
            ulong key = 100UL
        )
        {
            var obj = new TestObject
            {
                Trace = trace
            };
            obj.SetKey(key);
            registry.NextObject = obj;
            return obj;
        }

    #endregion

    #region 게임 Spawn·Despawn 수명

        // ------------------------------------------------------------
        /// <summary>
        /// SpawnDespawn에서 상태와 Membership을 전이 계약을 검증한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_SpawnRegistry_SpawnDespawn에서_상태와_Membership을_전이()
        {
            var registry = new TestRegistry();

            Assert.IsTrue(registry.TrySpawn(out var obj));
            Assert.AreEqual(SpawnState.Spawned, obj.SpawnState);
            Assert.IsTrue(obj.HasKey);
            Assert.AreSame(obj, registry.Registered[obj.Key]);

            registry.Despawn(obj, DespawnReason.Removed);

            Assert.AreEqual(SpawnState.Despawned, obj.SpawnState);
            Assert.AreEqual(0, registry.Registered.Count);
        }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/> 한 객체의 Spawn 완료 알림에서 다른 객체의 Spawn은 허용하지만,
        /// <br/> 중첩된 객체의 알림에서 아직 진행 중인 최초 객체로 되돌아가는 수명 작업은 거절한다.
        /// </summary>
        // --------------------------------------------------------------------------------
        [Test]
        public void TEST_SpawnRegistry_다른객체_중첩Spawn은_허용하고_순환재진입은_거절()
        {
            var registry = new TestRegistry();
            TestObject first = null;
            TestObject second = null;
            var nestedSpawnCount = 0;
            var cycleRejected = 0;

            registry.OnSpawned += (_, entity) =>
            {
                if (first == null)
                {
                    first = entity;
                    Assert.IsTrue(registry.TrySpawn(out second));
                    nestedSpawnCount++;
                    return;
                }

                if (ReferenceEquals(entity, first))
                {
                    return;
                }

                Assert.Throws<InvalidOperationException>
                (
                    () => registry.Despawn(first, DespawnReason.Removed)
                );
                cycleRejected++;
            };

            Assert.IsTrue(registry.TrySpawn(out var outer));

            Assert.AreSame(first, outer);
            Assert.IsNotNull(second);
            Assert.AreEqual(1, nestedSpawnCount);
            Assert.AreEqual(1, cycleRejected);
            Assert.AreEqual(2, registry.Registered.Count);
            Assert.AreEqual(SpawnState.Spawned, first.SpawnState);
            Assert.AreEqual(SpawnState.Spawned, second.SpawnState);

            registry.DespawnAll();
            Assert.AreEqual(0, registry.Registered.Count);
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> 두 SpawnRegistry 변형 모두 객체 미획득을 정상 실패로 반환하고,
        /// <br/> 다음 획득의 Spawn과 파라미터 초기화는 정상 실행한다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_SpawnRegistry_Acquire가_null이면_false를_반환()
        {
            var registry = new TestRegistry
            {
                ReturnNullOnAcquire = true
            };

            Assert.IsFalse(registry.TrySpawn(out var missing));
            Assert.IsNull(missing);
            Assert.AreEqual(0, registry.Registered.Count);

            Assert.IsTrue(registry.TrySpawn(out var spawned));
            Assert.AreEqual(0UL, spawned.Key);

            var paramRegistry = new TestParamRegistry
            {
                ReturnNullOnAcquire = true
            };

            Assert.IsFalse(paramRegistry.TrySpawn(27, out var noParamObject));
            Assert.IsNull(noParamObject);
            Assert.AreEqual(0, paramRegistry.Registered.Count);

            Assert.IsTrue(paramRegistry.TrySpawn(27, out var initialized));
            Assert.AreEqual(27, initialized.InitializedWith);
            Assert.AreSame(initialized, paramRegistry.Find(0UL));
        }

    #endregion

    #region 일괄 Despawn과 terminal 정리

        // ------------------------------------------------------------
        /// <summary>
        /// DespawnAll이 모든 객체를 terminal 정리 계약을 검증한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_SpawnRegistry_DespawnAll이_모든_객체를_terminal_정리()
        {
            var registry = new TestRegistry();

            registry.TrySpawn(out var first);
            registry.TrySpawn(out var second);
            registry.TrySpawn(out var third);

            var rejectedSpawns = 0;
            registry.OnDespawning += (_, _, _) =>
            {
                Assert.Throws<InvalidOperationException>(() => registry.TrySpawn(out _));
                rejectedSpawns++;
            };

            registry.DespawnAll();

            Assert.AreEqual(3, rejectedSpawns);
            Assert.AreEqual(0, registry.Registered.Count);
            Assert.AreEqual(SpawnState.Despawned, first.SpawnState);
            Assert.AreEqual(SpawnState.Despawned, second.SpawnState);
            Assert.AreEqual(SpawnState.Despawned, third.SpawnState);
        }

    #endregion

    #region 필수 Binding 순서와 커밋 경계

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/> RequiredBinding을 정순Spawn 역순Despawn하고 Observer는 Commit후 발행 계약을
        /// <br/> 검증한다.
        /// </summary>
        // --------------------------------------------------------------------------------
        [Test]
        public void TEST_SpawnRegistry_RequiredBinding을_정순Spawn_역순Despawn하고_Observer는_Commit후_발행()
        {
            var trace = new List<string>();
            var registry = new TestRegistry();
            var obj = InjectTraceObject(registry, trace);
            var first = new TestBinding("First", trace);
            var second = new TestBinding("Second", trace);

            registry.AttachBinding(first);
            registry.AttachBinding(second);
            registry.OnSpawned += (_, _) => trace.Add("Registry.OnSpawned");
            registry.OnDespawning += (_, _, _) => trace.Add("Registry.OnDespawning");
            registry.OnDespawned += (_, _, _) => trace.Add("Registry.OnDespawned");

            Assert.IsTrue(registry.TrySpawn(out _));

            CollectionAssert.AreEqual
            (
                new[]
                {
                    "Object.OnSpawning",
                    "First.OnRegistered",
                    "Second.OnRegistered",
                    "Object.OnSpawned",
                    "Registry.OnSpawned"
                },
                trace
            );

            trace.Clear();
            registry.Despawn(obj, DespawnReason.Removed);

            CollectionAssert.AreEqual
            (
                new[]
                {
                    "Object.OnDespawning",
                    "Registry.OnDespawning",
                    "Second.OnUnregistering",
                    "First.OnUnregistering",
                    "Object.OnDespawned",
                    "Registry.OnDespawned"
                },
                trace
            );
        }

    #endregion

    #region 필수 Binding 등록 실패 Rollback

        // ----------------------------------------------------------------------
        /// <summary>
        /// RequiredBinding Spawn실패시 실행된Binding만 역순Rollback 계약을 검증한다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_SpawnRegistry_RequiredBinding_Spawn실패시_실행된Binding만_역순Rollback()
        {
            var trace = new List<string>();
            var registry = new TestRegistry();
            var obj = InjectTraceObject(registry, trace);
            var first = new TestBinding("First", trace);
            var second = new TestBinding("Second", trace)
            {
                FailOnRegistered = true
            };
            var third = new TestBinding("Third", trace);

            registry.AttachBinding(first);
            registry.AttachBinding(second);
            registry.AttachBinding(third);

            Assert.Throws<InvalidOperationException>(() => registry.TrySpawn(out _));

            CollectionAssert.AreEqual
            (
                new[]
                {
                    "Object.OnSpawning",
                    "First.OnRegistered",
                    "Second.OnRegistered",
                    "Object.OnDespawning",
                    "Second.OnUnregistering",
                    "First.OnUnregistering",
                    "Object.OnDespawned"
                },
                trace
            );
            Assert.AreEqual(SpawnState.Despawned, obj.SpawnState);
            Assert.AreEqual(0, registry.Registered.Count);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> Spawn 등록 실패의 Rollback 도중 반환 훅에서 동일 객체를
        /// <br/> 다시 스폰하지 못하고 원래 실패를 보존한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_SpawnRegistry_Rollback반환중_동일객체재Spawn거절()
        {
            var trace = new List<string>();
            var registry = new TestRegistry();
            var entity = new TestObject();
            entity.SetKey(0UL);
            registry.NextObject = entity;

            var binding = new TestBinding("Failed", trace)
            {
                FailOnRegistered = true
            };
            registry.AttachBinding(binding);

            var releases = 0;
            registry.OnReleaseAction = released =>
            {
                releases++;
                Assert.AreSame(entity, released);
                Assert.AreEqual(SpawnState.Despawned, released.SpawnState);

                // 방어 실패로 실제 재스폰될 경우 재귀 Rollback은 방지한다.
                registry.OnReleaseAction = null;
                registry.NextObject = released;

                var reentryError = Assert.Throws<InvalidOperationException>
                (
                    () => registry.TrySpawn(out _)
                );
                StringAssert.Contains("동일 객체의 등록 작업", reentryError.Message);
            };

            var error = Assert.Throws<InvalidOperationException>
            (
                () => registry.TrySpawn(out _)
            );

            Assert.AreEqual("Failed spawn failure", error.Message);
            Assert.AreEqual(1, releases);
            Assert.AreEqual(0, registry.Registered.Count);
            Assert.AreEqual(SpawnState.Despawned, entity.SpawnState);
        }

    #endregion

    #region 등록과 Rollback의 복합 실패

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> Required Binding이 Spawn과 rollback에서 모두 실패해도
        /// <br/> 최초 Spawn 실패와 정리 오류를 순서대로 보존한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_SpawnRegistry_BindingSpawn과Rollback_동시실패를_모두전달()
        {
            var trace = new List<string>();
            var registry = new TestRegistry();
            var obj = InjectTraceObject(registry, trace);
            var failing = new TestBinding("Failed", trace)
            {
                FailOnRegistered = true,
                FailOnUnregistering = true
            };

            registry.AttachBinding(failing);

            var exception = Assert.Throws<AggregateException>
            (
                () => registry.TrySpawn(out _)
            );

            Assert.AreEqual(2, exception.InnerExceptions.Count);
            Assert.AreEqual("Failed spawn failure", exception.InnerExceptions[0].Message);
            Assert.AreEqual("Failed despawn failure", exception.InnerExceptions[1].Message);
            Assert.AreEqual(SpawnState.Despawned, obj.SpawnState);
            Assert.AreEqual(0, registry.Registered.Count);
            CollectionAssert.Contains(trace, "Failed.OnUnregistering");
        }

    #endregion

    #region 필수 Binding 해제 실패 격리

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/> RequiredBinding Despawn실패에도 나머지정리와 terminal전이를 계속 계약을
        /// <br/> 검증한다.
        /// </summary>
        // --------------------------------------------------------------------------------
        [Test]
        public void TEST_SpawnRegistry_RequiredBinding_Despawn실패에도_나머지정리와_terminal전이를_계속()
        {
            var trace = new List<string>();
            var registry = new TestRegistry();
            var obj = InjectTraceObject(registry, trace);
            var first = new TestBinding("First", trace);
            var second = new TestBinding("Second", trace)
            {
                FailOnUnregistering = true
            };
            var third = new TestBinding("Third", trace);

            registry.AttachBinding(first);
            registry.AttachBinding(second);
            registry.AttachBinding(third);
            registry.TrySpawn(out _);
            trace.Clear();

            Assert.Throws<InvalidOperationException>
            (
                () => registry.Despawn(obj, DespawnReason.Removed)
            );

            CollectionAssert.AreEqual
            (
                new[]
                {
                    "Object.OnDespawning",
                    "Third.OnUnregistering",
                    "Second.OnUnregistering",
                    "First.OnUnregistering",
                    "Object.OnDespawned"
                },
                trace
            );
            Assert.AreEqual(SpawnState.Despawned, obj.SpawnState);
            Assert.AreEqual(0, registry.Registered.Count);
        }

    #endregion

    #region 해제와 완료 알림의 복합 실패

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> Despawning Binding과 완료 Observer가 모두 실패해도
        /// <br/> 원인을 보존하고 객체의 terminal 제거를 마친다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_SpawnRegistry_Despawn과완료Observer실패를_모두전달()
        {
            var trace = new List<string>();
            var registry = new TestRegistry();
            var entity = InjectTraceObject(registry, trace);
            var binding = new TestBinding("Failed", trace)
            {
                FailOnUnregistering = true
            };
            registry.AttachBinding(binding);
            registry.TrySpawn(out _);
            registry.OnDespawned += (_, _, _) => throw new InvalidOperationException("commit notification failure");

            var exception = Assert.Throws<AggregateException>
            (
                () => registry.Despawn(entity, DespawnReason.Removed)
            );

            Assert.AreEqual(2, exception.InnerExceptions.Count);
            Assert.AreEqual("Failed despawn failure", exception.InnerExceptions[0].Message);
            Assert.AreEqual("commit notification failure", exception.InnerExceptions[1].Message);
            Assert.AreEqual(SpawnState.Despawned, entity.SpawnState);
            Assert.AreEqual(0, registry.Registered.Count);
            CollectionAssert.Contains(trace, "Object.OnDespawned");
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// 등록 전에 실패했어도 완료 알림 오류가 최초 Spawn 오류를 가리지 않는다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_SpawnRegistry_미등록SpawnRollback은_완료알림없이_원인을전달()
        {
            var registry = new TestRegistry();
            var trace = new List<string>();
            var entity = InjectTraceObject(registry, trace);
            var completedNotifications = 0;

            registry.OnSpawning += (_, _) => throw new InvalidOperationException("spawning observer failure");
            registry.OnRegistered += (_, _) => completedNotifications++;
            registry.OnUnregistered += (_, _, _) => completedNotifications++;
            registry.OnDespawned += (_, _, _) => completedNotifications++;

            var error = Assert.Throws<InvalidOperationException>
            (
                () => registry.TrySpawn(out _)
            );

            Assert.AreEqual("spawning observer failure", error.Message);
            Assert.AreEqual(0, completedNotifications);
            Assert.AreEqual(SpawnState.Despawned, entity.SpawnState);
            Assert.AreEqual(0, registry.Registered.Count);
        }

    #endregion

    #region 등록 완료 알림과 커밋 후 예외

        // --------------------------------------------------------------------------------
        /// <summary>
        /// CommittedSpawnObserver실패가 완료된Spawn을 Rollback하지않음 계약을 검증한다.
        /// </summary>
        // --------------------------------------------------------------------------------
        [Test]
        public void TEST_SpawnRegistry_CommittedSpawnObserver실패가_완료된Spawn을_Rollback하지않음()
        {
            // 예외가 전파되면 out 대입은 완료되지 않으므로 획득 후보의 동일성을 먼저 확보한다.
            var obj = new TestObject();
            obj.SetKey(0UL);
            var registry = new TestRegistry
            {
                NextObject = obj,
            };
            Action<ulong, TestObject> observer =
                (_, _) => throw new InvalidOperationException("observer failure");

            registry.OnSpawned += observer;

            Assert.Throws<InvalidOperationException>
            (
                () => registry.TrySpawn(out _)
            );

            Assert.IsNotNull(obj);
            Assert.AreEqual(SpawnState.Spawned, obj.SpawnState);
            Assert.AreEqual(1, registry.Registered.Count);
            Assert.AreSame(obj, registry.Registered[obj.Key]);

            registry.OnSpawned -= observer;
            registry.Despawn(obj, DespawnReason.Removed);
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// 완료 알림 구독자 예외가 하나면 원래 예외를 스택 추적과 함께 다시 전달한다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_SpawnRegistry_완료알림실패하나는_원래스택추적유지()
        {
            var obj = new TestObject();
            obj.SetKey(0UL);
            var registry = new TestRegistry
            {
                NextObject = obj,
            };

            registry.OnSpawned += ThrowFromSpawnedObserver;

            var thrown = Assert.Throws<InvalidOperationException>(() => registry.TrySpawn(out _));

            Assert.That(thrown.StackTrace, Does.Contain(nameof(ThrowFromSpawnedObserver)));

            registry.OnSpawned -= ThrowFromSpawnedObserver;
            registry.Despawn(obj, DespawnReason.Removed);
        }

        private static void ThrowFromSpawnedObserver(ulong key, TestObject spawnable)
        {
            throw new InvalidOperationException("observer failure");
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 완료 이벤트 오류를 모두 전달하고 Spawn 등록 상태를 유지한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_SpawnRegistry_등록완료알림실패에도_Spawn완료알림을_시도()
        {
            var obj = new TestObject();
            obj.SetKey(0UL);

            var registry = new TestRegistry
            {
                NextObject = obj
            };
            var trace = new List<string>();

            registry.OnRegistered += (_, _) =>
            {
                trace.Add("Registered");
                throw new InvalidOperationException("registration notification failure");
            };
            registry.OnSpawned += (_, _) =>
            {
                trace.Add("Spawned");
                throw new InvalidOperationException("spawn notification failure");
            };

            var exception = Assert.Throws<AggregateException>
            (
                () => registry.TrySpawn(out _)
            );

            CollectionAssert.AreEqual
            (
                new[]
                {
                    "Registered", "Spawned"
                },
                trace
            );
            Assert.AreEqual(2, exception.InnerExceptions.Count);
            Assert.AreEqual("registration notification failure", exception.InnerExceptions[0].Message);
            Assert.AreEqual("spawn notification failure", exception.InnerExceptions[1].Message);
            Assert.AreEqual(SpawnState.Spawned, obj.SpawnState);
            Assert.AreSame(obj, registry.Find(0UL));

            obj.Despawn();
            Assert.AreEqual(SpawnState.Despawned, obj.SpawnState);
            Assert.AreEqual(0, registry.Registered.Count);
        }

    #endregion

    #region 기존 등록 객체에 대한 Binding 동기화

        // ----------------------------------------------------------------------
        /// <summary>
        /// 복원된 Registry에 Binding을 추가·제거해도 일반 Spawn 알림은 재생하지 않는다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_SpawnRegistry_복원된_객체의_Binding을_동기화하고_해제()
        {
            var original = new TestRegistry();
            original.TrySpawn(out _);
            original.TrySpawn(out _);

            var restored = JsonUtility.FromJson<TestRegistry>(JsonUtility.ToJson(original));
            var trace = new List<string>();
            var binding = new TestBinding("Live", trace);
            var spawnedNotifications = 0;

            restored.OnSpawned += (_, _) => spawnedNotifications++;

            restored.AttachBinding(binding);

            CollectionAssert.AreEquivalent
            (
                new[]
                {
                    "Live.OnRegistered", "Live.OnRegistered"
                },
                trace
            );
            Assert.AreEqual(0, spawnedNotifications);

            trace.Clear();
            Assert.IsTrue(restored.DetachBinding(binding));

            CollectionAssert.AreEquivalent
            (
                new[]
                {
                    "Live.OnUnregistering", "Live.OnUnregistering"
                },
                trace
            );
            Assert.AreEqual(2, restored.Registered.Count);
            Assert.AreEqual(0, spawnedNotifications);

            trace.Clear();
            Assert.IsTrue(restored.TrySpawn(out _));
            Assert.IsEmpty(trace);
            Assert.AreEqual(1, spawnedNotifications);
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// 기존 객체에 대한 연결 도중 실패해도 모든 부분 Binding을 역순 해제한다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_SpawnRegistry_Binding_동기화실패시_부분연결을_롤백()
        {
            var registry = new TestRegistry();
            registry.TrySpawn(out _);
            registry.TrySpawn(out _);

            var trace = new List<string>();
            var binding = new TestBinding("Live", trace)
            {
                FailOnRegisteredCall = 2
            };

            Assert.Throws<InvalidOperationException>(() => registry.AttachBinding(binding));

            CollectionAssert.AreEqual
            (
                new[]
                {
                    "Live.OnRegistered",
                    "Live.OnRegistered",
                    "Live.OnUnregistering",
                    "Live.OnUnregistering"
                },
                trace
            );

            Assert.AreEqual(2, registry.Registered.Count);
            Assert.IsFalse(registry.DetachBinding(binding));

            trace.Clear();
            registry.TrySpawn(out _);
            Assert.IsEmpty(trace);
        }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// Binding 연결·해제 동기화 중 Registry 수명과 Binding 구성을 변경하지 못하게 한다.
        /// </summary>
        // --------------------------------------------------------------------------------
        [Test]
        public void TEST_SpawnRegistry_Binding_동기화중에는_Registry와_Binding변경을_차단()
        {
            var registry = new TestRegistry();
            registry.TrySpawn(out var existing);

            var trace = new List<string>();
            var other = new TestBinding("Other", trace);
            var binding = new TestBinding("Live", trace);

            void AssertMutationRejected()
            {
                Assert.Throws<InvalidOperationException>(() => registry.TrySpawn(out _));
                Assert.Throws<InvalidOperationException>
                (
                    () => registry.Despawn(existing, DespawnReason.Removed)
                );
                Assert.Throws<InvalidOperationException>(() => registry.DespawnAll());
                Assert.Throws<InvalidOperationException>(() => registry.AttachBinding(other));
                Assert.Throws<InvalidOperationException>(() => registry.DetachBinding(binding));
            }

            binding.OnRegisteredAction = AssertMutationRejected;
            binding.OnUnregisteringAction = AssertMutationRejected;

            registry.AttachBinding(binding);

            Assert.AreEqual(1, registry.Registered.Count);
            Assert.AreEqual(SpawnState.Spawned, existing.SpawnState);
            Assert.IsTrue(registry.DetachBinding(binding));
            Assert.AreEqual(1, registry.Registered.Count);
            Assert.AreEqual(SpawnState.Spawned, existing.SpawnState);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 일부 Binding 해제 실패에도 남은 연결을 정리한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_SpawnRegistry_Binding_해제실패에도_연결을_종료()
        {
            var registry = new TestRegistry();
            registry.TrySpawn(out _);
            registry.TrySpawn(out _);

            var trace = new List<string>();
            var binding = new TestBinding("Live", trace)
            {
                FailOnUnregistering = true
            };

            registry.AttachBinding(binding);
            trace.Clear();

            Assert.Throws<AggregateException>(() => registry.DetachBinding(binding));
            CollectionAssert.AreEquivalent
            (
                new[]
                {
                    "Live.OnUnregistering", "Live.OnUnregistering"
                },
                trace
            );
            Assert.AreEqual(2, registry.Registered.Count);
            Assert.IsFalse(registry.DetachBinding(binding));
        }

    #endregion

    #region Registry 조회의 읽기 전용 계약

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> 공개 Registry 사전을 mutable Dictionary로 다운캐스팅할 수 없으며
        /// <br/> 등록·역직렬화 후에도 조회 기능은 유지된다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_SpawnRegistry_Registered조회View는_직접수정을_허용하지않음()
        {
            var registry = new TestRegistry();
            var readOnly = registry.Registered;

            Assert.IsFalse(readOnly is IDictionary<ulong, TestObject>);
            Assert.IsFalse(readOnly is RegisteredDictionary<ulong, TestObject>);
            Assert.AreSame(readOnly, registry.Registered);

            Assert.IsTrue(registry.TrySpawn(out var entity));
            Assert.AreEqual(1, readOnly.Count);
            Assert.IsTrue(readOnly.TryGetValue(entity.Key, out var found));
            Assert.AreSame(entity, found);

            var restored = JsonUtility.FromJson<TestRegistry>(JsonUtility.ToJson(registry));
            Assert.AreEqual(1, restored.Registered.Count);
            Assert.IsFalse(restored.Registered is IDictionary<ulong, TestObject>);
            Assert.IsNotNull(restored.Find(entity.Key));

            entity.Despawn();
            Assert.AreEqual(0, readOnly.Count);
        }

    #endregion

    #region 직렬화 복원과 Runtime 재연결

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/> JSONRoundTrip후 Runtime연결을 복원하고 SpawnHook은 Replay하지않음 계약을
        /// <br/> 검증한다.
        /// </summary>
        // --------------------------------------------------------------------------------
        [Test]
        public void TEST_SpawnRegistry_JSONRoundTrip후_Runtime연결을_복원하고_SpawnHook은_Replay하지않음()
        {
            TestObject.SpawnedHookCount = 0;

            var registry = new TestRegistry();
            Assert.IsTrue(registry.TrySpawn(out var original));

            var key = original.Key;
            var json = JsonUtility.ToJson(registry);

            TestObject.SpawnedHookCount = 0;

            var restored = JsonUtility.FromJson<TestRegistry>(json);
            var restoredObject = restored.Find(key);

            Assert.IsNotNull(restoredObject);
            Assert.AreEqual(SpawnState.Spawned, restoredObject.SpawnState);
            Assert.AreEqual(0, TestObject.SpawnedHookCount);

            ((IDespawnable)restoredObject).Despawn();

            Assert.AreEqual(0, restored.Registered.Count);
            Assert.AreEqual(SpawnState.Despawned, restoredObject.SpawnState);
        }

    #endregion

    #region 중복 Key 및 Rollback

        // ------------------------------------------------------------
        /// <summary>
        /// 중복Key등록실패가 기존Membership을 보존 계약을 검증한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_SpawnRegistry_중복Key등록실패가_기존Membership을_보존()
        {
            var registry = new TestRegistry();
            registry.TrySpawn(out var first);

            var duplicate = new TestObject();
            duplicate.SetKey(first.Key);
            registry.NextObject = duplicate;

            Assert.Throws<InvalidOperationException>(() => registry.TrySpawn(out _));

            Assert.AreEqual(SpawnState.Despawned, duplicate.SpawnState);
            Assert.AreEqual(1, registry.Registered.Count);
            Assert.AreSame(first, registry.Find(first.Key));
        }

    #endregion

    }
}
