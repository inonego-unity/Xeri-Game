/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : TEST_SpawnRegistry.cs
수정일 : 2026-11-13

# 설명
SpawnRegistry의 lifecycle transaction, required binding, committed notification과 self-rehydrate 계약을 검증한다.
단순 Find/Count 동작보다 여러 hook·binding·rollback 경계가 결합된 상태 전이를 우선 검증한다.

# 테스트 구성
 L: 기본 lifecycle과 bulk cleanup
 T: required binding transaction과 commit 경계
 B: 기존 등록 집합에 대한 Binding 동기화와 rollback
 S: 직렬화 후 runtime self-rehydrate
 X: 등록 실패 rollback
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
            /// Spawn 완료 참여 순서를 기록하고 지정된 실패를 전달한다.
            /// </summary>
            // ------------------------------------------------------------
            void ISpawnable.OnSpawned()
            {
                SpawnedHookCount++;
                Trace?.Add("Object.OnSpawned");
            }

            // ------------------------------------------------------------
            /// <summary>
            /// Despawn 참여 순서를 기록하고 지정된 실패를 전달한다.
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
            /// 필수 Spawn Binding 실패 여부.
            /// </summary>
            // ------------------------------------------------------------
            public bool FailOnSpawned { get; set; }

            // ------------------------------------------------------------
            /// <summary>
            /// N번째 Spawned 동기화에서 실패한다. 0은 비활성.
            /// </summary>
            // ------------------------------------------------------------
            public int FailOnSpawnedCall { get; set; }

            // ------------------------------------------------------------
            /// <summary>
            /// Binding에서 호출할 추가 검증 작업.
            /// </summary>
            // ------------------------------------------------------------
            public Action OnSpawnedAction { get; set; }

            private int spawnedCallCount = 0;

            // ------------------------------------------------------------
            /// <summary>
            /// 필수 Despawn Binding 실패 여부.
            /// </summary>
            // ------------------------------------------------------------
            public bool FailOnDespawning { get; set; }

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
            /// Spawn 완료 참여 순서를 기록하고 지정된 실패를 전달한다.
            /// </summary>
            // ------------------------------------------------------------
            public void OnSpawned(ulong key, TestObject spawnable)
            {
                trace.Add($"{name}.OnSpawned");
                spawnedCallCount++;
                OnSpawnedAction?.Invoke();

                if (FailOnSpawned || (FailOnSpawnedCall > 0 && spawnedCallCount == FailOnSpawnedCall))
                {
                    throw new InvalidOperationException($"{name} spawn failure");
                }
            }

            // ------------------------------------------------------------
            /// <summary>
            /// Despawn 참여 순서를 기록하고 지정된 실패를 전달한다.
            /// </summary>
            // ------------------------------------------------------------
            public void OnDespawning
            (
                ulong key,
                TestObject despawnable,
                DespawnReason reason
            )
            {
                trace.Add($"{name}.OnDespawning");

                if (FailOnDespawning)
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

    #region L-1: Spawn과 terminal Despawn

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
            Assert.AreSame(obj, registry.Spawned[obj.Key]);

            registry.Despawn(obj, DespawnReason.Removed);

            Assert.AreEqual(SpawnState.Despawned, obj.SpawnState);
            Assert.AreEqual(0, registry.Spawned.Count);
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
            Assert.AreEqual(0, registry.Spawned.Count);

            Assert.IsTrue(registry.TrySpawn(out var spawned));
            Assert.AreEqual(0UL, spawned.Key);

            var paramRegistry = new TestParamRegistry
            {
                ReturnNullOnAcquire = true
            };

            Assert.IsFalse(paramRegistry.TrySpawn(27, out var noParamObject));
            Assert.IsNull(noParamObject);
            Assert.AreEqual(0, paramRegistry.Spawned.Count);

            Assert.IsTrue(paramRegistry.TrySpawn(27, out var initialized));
            Assert.AreEqual(27, initialized.InitializedWith);
            Assert.AreSame(initialized, paramRegistry.Find(0UL));
        }

    #endregion

    #region L-2: DespawnAll terminal cleanup

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

            registry.DespawnAll();

            Assert.AreEqual(0, registry.Spawned.Count);
            Assert.AreEqual(SpawnState.Despawned, first.SpawnState);
            Assert.AreEqual(SpawnState.Despawned, second.SpawnState);
            Assert.AreEqual(SpawnState.Despawned, third.SpawnState);
        }

    #endregion

    #region T-1: required binding 실행 순서와 commit 경계

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
                    "First.OnSpawned",
                    "Second.OnSpawned",
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
                    "Second.OnDespawning",
                    "First.OnDespawning",
                    "Registry.OnDespawning",
                    "Object.OnDespawned",
                    "Registry.OnDespawned"
                },
                trace
            );
        }

    #endregion

    #region T-2: required binding Spawn 실패 rollback

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
                FailOnSpawned = true
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
                    "First.OnSpawned",
                    "Second.OnSpawned",
                    "Object.OnDespawning",
                    "Second.OnDespawning",
                    "First.OnDespawning",
                    "Object.OnDespawned"
                },
                trace
            );
            Assert.AreEqual(SpawnState.Despawned, obj.SpawnState);
            Assert.AreEqual(0, registry.Spawned.Count);
        }

    #endregion

    #region T-3: required binding Despawn 실패 격리

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
                FailOnDespawning = true
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
                    "Third.OnDespawning",
                    "Second.OnDespawning",
                    "First.OnDespawning",
                    "Object.OnDespawned"
                },
                trace
            );
            Assert.AreEqual(SpawnState.Despawned, obj.SpawnState);
            Assert.AreEqual(0, registry.Spawned.Count);
        }

    #endregion

    #region T-4: committed notification 실패

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
            Assert.AreEqual(1, registry.Spawned.Count);
            Assert.AreSame(obj, registry.Spawned[obj.Key]);

            registry.OnSpawned -= observer;
            registry.Despawn(obj, DespawnReason.Removed);
        }

    #endregion

    #region B-1: 현재 등록 집합에 대한 Binding 동기화

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
                    "Live.OnSpawned", "Live.OnSpawned"
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
                    "Live.OnDespawning", "Live.OnDespawning"
                },
                trace
            );
            Assert.AreEqual(2, restored.Spawned.Count);
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
                FailOnSpawnedCall = 2
            };

            Assert.Throws<InvalidOperationException>(() => registry.AttachBinding(binding));

            CollectionAssert.AreEqual
            (
                new[]
                {
                    "Live.OnSpawned",
                    "Live.OnSpawned",
                    "Live.OnDespawning",
                    "Live.OnDespawning"
                },
                trace
            );

            Assert.AreEqual(2, registry.Spawned.Count);
            Assert.IsFalse(registry.DetachBinding(binding));

            trace.Clear();
            registry.TrySpawn(out _);
            Assert.IsEmpty(trace);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Binding 중 Registry의 등록 집합 변경을 거절한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_SpawnRegistry_Binding_동기화중에는_스폰과_디스폰을_차단()
        {
            var registry = new TestRegistry();
            registry.TrySpawn(out var existing);

            var trace = new List<string>();
            var binding = new TestBinding("Live", trace)
            {
                OnSpawnedAction = () =>
                {
                    Assert.Throws<InvalidOperationException>(() => registry.TrySpawn(out _));
                    Assert.Throws<InvalidOperationException>(() => registry.Despawn(existing, DespawnReason.Removed));
                    Assert.Throws<InvalidOperationException>(() => registry.DespawnAll());
                }
            };

            registry.AttachBinding(binding);

            Assert.AreEqual(1, registry.Spawned.Count);
            Assert.AreEqual(SpawnState.Spawned, existing.SpawnState);
            Assert.IsTrue(registry.DetachBinding(binding));
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
                FailOnDespawning = true
            };

            registry.AttachBinding(binding);
            trace.Clear();

            Assert.Throws<AggregateException>(() => registry.DetachBinding(binding));
            CollectionAssert.AreEquivalent
            (
                new[]
                {
                    "Live.OnDespawning", "Live.OnDespawning"
                },
                trace
            );
            Assert.AreEqual(2, registry.Spawned.Count);
            Assert.IsFalse(registry.DetachBinding(binding));
        }

    #endregion

    #region S-1: JSON round-trip self-rehydrate

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

            Assert.AreEqual(0, restored.Spawned.Count);
            Assert.AreEqual(SpawnState.Despawned, restoredObject.SpawnState);
        }

    #endregion

    #region X-1: 중복 Key 등록 rollback

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
            Assert.AreEqual(1, registry.Spawned.Count);
            Assert.AreSame(first, registry.Find(first.Key));
        }

    #endregion

    }
}
