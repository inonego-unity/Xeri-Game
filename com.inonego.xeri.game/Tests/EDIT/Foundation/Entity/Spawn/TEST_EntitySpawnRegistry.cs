/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : TEST_EntitySpawnRegistry.cs
수정일 : 2026-11-14

# 설명
EntitySpawnRegistry의 Registry별 Key 범위, 등록 수명, 외부 Key 수용과 직렬화 복원을 검증한다.
HP·Group lifetime 정책은 Entity Core 필수 계약이 아니므로 이 테스트에서 다루지 않는다.

# 테스트 구성
 L: Entity Key, 등록 수명과 Registry 소속 Handle 검증
 I: 외부 확정 Key 및 KeyGenerator 충돌
 S: 직렬화 후 membership·runtime 연결·KeyGenerator 복원
========================================================================= BLOCK_HEADER_END */

using System;
using System.Collections;
using System.Collections.Generic;

using UnityEngine;

using NUnit;
using NUnit.Framework;

namespace inonego.Xeri.Game.TEST._EntitySpawn
{
    using Xeri;
    using Xeri.Game;

    // ======================================================================
    /// <summary>
    /// EntitySpawnRegistry의 Entity-specific lifecycle 계약을 검증한다.
    /// </summary>
    // ======================================================================
    public class TEST_EntitySpawnRegistry
    {

    #region 헬퍼

        [Serializable]
        private class TestEntity : EntityBase
        {
            // NONE
        }

        [Serializable]
        private sealed class TestKeyHookEntity : TestEntity
        {
            // ------------------------------------------------------------
            /// <summary>
            /// Key 설정 훅 호출 횟수.
            /// </summary>
            // ------------------------------------------------------------
            public int AssignedCount { get; private set; }

            // ------------------------------------------------------------
            /// <summary>
            /// Key 해제 훅 호출 횟수.
            /// </summary>
            // ------------------------------------------------------------
            public int ClearedCount { get; private set; }

            // ------------------------------------------------------------
            /// <summary>
            /// Spawning 훅에서 실패하도록 설정한다.
            /// </summary>
            // ------------------------------------------------------------
            public bool FailDuringSpawning { get; set; }

            // ------------------------------------------------------------
            /// <summary>
            /// Key 할당 후 설정 훅에서 실패하도록 설정한다.
            /// </summary>
            // ------------------------------------------------------------
            public bool FailAfterAssign { get; set; }

            // ------------------------------------------------------------
            /// <summary>
            /// Key 해제 훅에서 실패하도록 설정한다.
            /// </summary>
            // ------------------------------------------------------------
            public bool FailDuringClear { get; set; }

            // ------------------------------------------------------------
            /// <summary>
            /// Registry가 지정한 값과 다른 Key를 설정한다.
            /// </summary>
            // ------------------------------------------------------------
            public bool AssignDifferentKey { get; set; }

            protected internal override void SetKey(ulong value)
            {
                base.SetKey(AssignDifferentKey ? value + 1UL : value);
                AssignedCount++;

                if (FailAfterAssign)
                {
                    throw new InvalidOperationException("테스트 Key 설정 훅 실패");
                }
            }

            protected internal override void ClearKey()
            {
                ClearedCount++;

                if (FailDuringClear)
                {
                    throw new InvalidOperationException("테스트 Key 해제 훅 실패");
                }

                base.ClearKey();
            }

            protected override void OnSpawning()
            {
                if (FailDuringSpawning)
                {
                    throw new InvalidOperationException("테스트 Spawn 훅 실패");
                }
            }
        }

        [Serializable]
        private sealed class TestDirectEntity : TestEntity
        {
            // ------------------------------------------------------------
            /// <summary>
            /// 게임 Spawn 훅이 호출된 횟수.
            /// </summary>
            // ------------------------------------------------------------
            public int SpawnHooks { get; private set; }

            // ------------------------------------------------------------
            /// <summary>
            /// 게임 Despawn 훅이 호출된 횟수.
            /// </summary>
            // ------------------------------------------------------------
            public int DespawnHooks { get; private set; }

            // ------------------------------------------------------------
            /// <summary>
            /// 외부에서 선택하여 적용한 공개 상태 값.
            /// </summary>
            // ------------------------------------------------------------
            public int VisibleValue;

            protected override void OnSpawning()
            {
                SpawnHooks++;
            }

            protected override void OnSpawned()
            {
                SpawnHooks++;
            }

            protected override void OnDespawning(DespawnReason reason)
            {
                DespawnHooks++;
            }

            protected override void OnDespawned(DespawnReason reason)
            {
                DespawnHooks++;
            }
        }

        private sealed class TestDirectBinding : ISpawnRegistryBinding<ulong, TestEntity>
        {
            // ------------------------------------------------------------
            /// <summary>
            /// 필수 등록 Binding 실패를 주입한다.
            /// </summary>
            // ------------------------------------------------------------
            public bool FailOnRegister { get; set; }

            // ------------------------------------------------------------
            /// <summary>
            /// 직접 등록 정리 중 실패를 주입한다.
            /// </summary>
            // ------------------------------------------------------------
            public bool FailOnRemove { get; set; }

            // ------------------------------------------------------------
            /// <summary>
            /// 상태 등록에 성공한 횟수를 반환한다.
            /// </summary>
            // ------------------------------------------------------------
            public int RegisterCalls { get; private set; }

            // ------------------------------------------------------------
            /// <summary>
            /// 상태 정리를 시도한 횟수를 반환한다.
            /// </summary>
            // ------------------------------------------------------------
            public int RemoveCalls { get; private set; }

            // ------------------------------------------------------------
            /// <summary>
            /// Registry 등록 Binding 콜백에서 실패를 주입한다.
            /// </summary>
            // ------------------------------------------------------------
            public void OnRegistered(ulong key, TestEntity entity)
            {
                RegisterCalls++;

                if (FailOnRegister)
                {
                    throw new InvalidOperationException("Direct binding 생성 실패");
                }
            }

            // ------------------------------------------------------------
            /// <summary>
            /// Registry 등록 해제 Binding 콜백에서 실패를 주입한다.
            /// </summary>
            // ------------------------------------------------------------
            public void OnUnregistering(ulong key, TestEntity entity, DespawnReason reason)
            {
                RemoveCalls++;

                if (FailOnRemove)
                {
                    throw new InvalidOperationException("Direct binding 정리 실패");
                }
            }
        }

        private sealed class TestSimpleBinding : ISpawnRegistryBinding<ulong, TestEntity>
        {
            // ------------------------------------------------------------
            /// <summary>
            /// 공통 Registry Binding 등록 호출 횟수.
            /// </summary>
            // ------------------------------------------------------------
            public int RegisterCalls { get; private set; }

            // ------------------------------------------------------------
            /// <summary>
            /// Registry의 등록 완료 콜백을 처리한다.
            /// </summary>
            // ------------------------------------------------------------
            public void OnRegistered(ulong key, TestEntity entity)
            {
                RegisterCalls++;
            }

            // ------------------------------------------------------------
            /// <summary>
            /// Registry의 등록 해제 콜백을 처리한다.
            /// </summary>
            // ------------------------------------------------------------
            public void OnUnregistering(ulong key, TestEntity entity, DespawnReason reason)
            {
                // NONE
            }
        }

        private sealed class TestUnifiedBinding : ISpawnRegistryBinding<ulong, TestEntity>
        {
            // ------------------------------------------------------------
            /// <summary>
            /// 등록된 객체의 총수.
            /// </summary>
            // ------------------------------------------------------------
            public int RegisteredCount { get; private set; }

            // ------------------------------------------------------------
            /// <summary>
            /// 해제된 객체의 총수.
            /// </summary>
            // ------------------------------------------------------------
            public int RemovedCount { get; private set; }

            // ------------------------------------------------------------
            /// <summary>
            /// 모든 등록 경로에서 동일하게 실행한다.
            /// </summary>
            // ------------------------------------------------------------
            public void OnRegistered(ulong key, TestEntity entity)
            {
                RegisteredCount++;
            }

            // ------------------------------------------------------------
            /// <summary>
            /// 모든 해제 경로에서 동일하게 실행한다.
            /// </summary>
            // ------------------------------------------------------------
            public void OnUnregistering(ulong key, TestEntity entity, DespawnReason reason)
            {
                RemovedCount++;
            }
        }

        [Serializable]
        private sealed class TestParamEntity : EntityBase, INeedToInit<int>
        {
            // ------------------------------------------------------------
            /// <summary>
            /// 파라미터를 사용한 Entity 초기화.
            /// </summary>
            // ------------------------------------------------------------
            public void Init(int param)
            {
                // NONE
            }

            // ------------------------------------------------------------
            /// <summary>
            /// 테스트 Entity의 초기화 수명을 종료한다.
            /// </summary>
            // ------------------------------------------------------------
            public void Release()
            {
                // NONE
            }
        }

        [Serializable]
        private sealed class TestParamRegistry : EntitySpawnRegistry<TestParamEntity, int>
        {
            // ------------------------------------------------------------
            /// <summary>
            /// 획득 출처로 객체를 반환한 횟수.
            /// </summary>
            // ------------------------------------------------------------
            public int ReleaseCount { get; private set; }

            // ------------------------------------------------------------
            /// <summary>
            /// 획득 객체 반환 중 예외를 발생시킨다.
            /// </summary>
            // ------------------------------------------------------------
            public bool FailRelease { get; set; }

            // ------------------------------------------------------------
            /// <summary>
            /// 다음 Spawn에 사용할 Entity 후보.
            /// </summary>
            // ------------------------------------------------------------
            public TestParamEntity NextEntity { get; set; }

            // ------------------------------------------------------------
            /// <summary>
            /// 다음 Acquire에서 null을 반환한다.
            /// </summary>
            // ------------------------------------------------------------
            public bool ReturnNullOnAcquire { get; set; }

            protected override TestParamEntity Acquire(int param)
            {
                if (ReturnNullOnAcquire)
                {
                    ReturnNullOnAcquire = false;
                    return null;
                }

                var entity = NextEntity ?? new TestParamEntity();
                NextEntity = null;
                return entity;
            }

            protected override void Release(TestParamEntity entity, DespawnReason reason)
            {
                ReleaseCount++;

                if (FailRelease)
                {
                    throw new InvalidOperationException("테스트 파라미터 Release 실패");
                }
            }
        }

        [Serializable]
        private sealed class TestRegistry : EntitySpawnRegistry<TestEntity>
        {
            // ------------------------------------------------------------
            /// <summary>
            /// 획득 출처에 객체를 반환한 횟수.
            /// </summary>
            // ------------------------------------------------------------
            public int ReleaseCount { get; private set; }

            // ------------------------------------------------------------
            /// <summary>
            /// 획득 객체 반환 중 예외를 발생시킨다.
            /// </summary>
            // ------------------------------------------------------------
            public bool FailRelease { get; set; }

            // ------------------------------------------------------------
            /// <summary>
            /// 다음 Spawn에서 사용할 Entity 후보.
            /// </summary>
            // ------------------------------------------------------------
            public TestEntity NextEntity { get; set; }

            // ------------------------------------------------------------
            /// <summary>
            /// 다음 Acquire에서 null을 반환한다.
            /// </summary>
            // ------------------------------------------------------------
            public bool ReturnNullOnAcquire { get; set; }

            protected override TestEntity Acquire()
            {
                if (ReturnNullOnAcquire)
                {
                    ReturnNullOnAcquire = false;
                    return null;
                }

                var entity = NextEntity ?? new TestEntity();
                NextEntity = null;
                return entity;
            }

            protected override void Release(TestEntity entity, DespawnReason reason)
            {
                ReleaseCount++;

                if (FailRelease)
                {
                    throw new InvalidOperationException("테스트 Release 실패");
                }
            }
        }

    #endregion

    #region 직접 등록과 공통 Registration 수명

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> 허용된 상태를 먼저 적용하고 게임 Spawn/Despawn 훅 없이 등록·제거한다.
        /// <br/> Entity 수명과 획득 출처는 기존 Registry와 독립적으로 유지한다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_EntitySpawnRegistry_직접등록구성은_게임Spawn이_아님()
        {
            var registry = new TestRegistry();
            var entity = new TestDirectEntity
            {
                VisibleValue = 42
            };
            var lifecycleEvents = 0;

            registry.OnSpawning += (_, _) => lifecycleEvents++;
            registry.OnSpawned += (_, _) => lifecycleEvents++;
            registry.OnDespawning += (_, _, _) => lifecycleEvents++;
            registry.OnDespawned += (_, _, _) => lifecycleEvents++;

            registry.RegisterWithKey(entity, 81UL);

            Assert.AreEqual(42, entity.VisibleValue);
            Assert.AreEqual(SpawnState.Spawned, entity.SpawnState);
            Assert.AreSame(entity, registry.Find(81UL));
            Assert.AreEqual(0, entity.SpawnHooks);
            Assert.AreEqual(0, lifecycleEvents);

            var handle = new EntityHandle(entity);
            Assert.IsTrue(registry.TryResolve(handle, out _));

            entity.Despawn();

            Assert.AreEqual(SpawnState.Despawned, entity.SpawnState);
            Assert.IsFalse(entity.HasKey);
            Assert.AreEqual(0, entity.DespawnHooks);
            Assert.AreEqual(0, lifecycleEvents);
            Assert.AreEqual(0, registry.ReleaseCount);
            Assert.AreEqual(0, registry.Registered.Count);
            Assert.IsFalse(registry.TryResolve(handle, out _));
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> 게임 Spawn과 직접 등록의 Registration 이벤트 순서를 검증한다.
        /// <br/> Spawn 전용 알림이 직접 등록에 전달되지 않음을 확인한다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_EntitySpawnRegistry_등록이벤트는_Spawn과_직접등록에_공통발생()
        {
            var registry = new TestRegistry();
            var trace = new List<string>();
            var spawnNotifications = 0;

            registry.OnRegistering += (key, _) =>
            {
                Assert.IsNull(registry.Find(key));
                trace.Add($"Registering:{key}");
            };
            registry.OnRegistered += (key, entity) =>
            {
                Assert.AreSame(entity, registry.Find(key));
                trace.Add($"Registered:{key}");
            };
            registry.OnUnregistering += (key, entity, _) =>
            {
                Assert.AreSame(entity, registry.Find(key));
                trace.Add($"Unregistering:{key}");
            };
            registry.OnUnregistered += (key, entity, _) =>
            {
                Assert.IsNull(registry.Find(key));
                Assert.AreEqual(SpawnState.Despawned, entity.SpawnState);
                Assert.IsFalse(entity.HasKey);
                trace.Add($"Unregistered:{key}");
            };

            registry.OnSpawning += (_, _) => spawnNotifications++;
            registry.OnSpawned += (_, _) => spawnNotifications++;
            registry.OnDespawning += (_, _, _) => spawnNotifications++;
            registry.OnDespawned += (_, _, _) => spawnNotifications++;

            var live = registry.SpawnWithKey(new TestDirectEntity(), 101UL);
            var direct = registry.RegisterWithKey(new TestDirectEntity(), 102UL);
            Assert.IsTrue(registry.Unregister(direct));
            live.Despawn();

            CollectionAssert.AreEqual
            (
                new[]
                {
                    "Registering:101", "Registered:101",
                    "Registering:102", "Registered:102",
                    "Unregistering:102", "Unregistered:102",
                    "Unregistering:101", "Unregistered:101"
                },
                trace
            );
            Assert.AreEqual(4, spawnNotifications);
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> 게임 Spawn과 직접 등록 시작 알림에 오류가 있어도 같은 이벤트의
        /// <br/> 후속 구독자를 호출하고 아직 미완료인 등록은 Rollback한다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_EntitySpawnRegistry_등록시작이벤트_구독자실패격리와Rollback()
        {
            for (var direct = 0; direct < 2; direct++)
            {
                var registry = new TestRegistry();
                var entity = new TestDirectEntity();
                var trace = new List<string>();
                registry.OnRegistering += (_, _) =>
                {
                    trace.Add("First");
                    throw new InvalidOperationException("registration start failure");
                };
                registry.OnRegistering += (_, _) => trace.Add("Second");

                if (direct == 0)
                {
                    Assert.Throws<InvalidOperationException>
                    (
                        () => registry.SpawnWithKey(entity, 120UL)
                    );
                }
                else
                {
                    Assert.Throws<InvalidOperationException>
                    (
                        () => registry.RegisterWithKey(entity, 120UL)
                    );
                }

                CollectionAssert.AreEqual
                (
                    new[]
                    {
                        "First", "Second"
                    },
                    trace
                );
                Assert.AreEqual(0, registry.Registered.Count);
                Assert.AreEqual(SpawnState.Despawned, entity.SpawnState);
                Assert.IsFalse(entity.HasKey);
            }

            var earlyRegistry = new TestRegistry();
            var earlyTrace = new List<string>();
            earlyRegistry.OnSpawning += (_, _) =>
            {
                earlyTrace.Add("First");
                throw new InvalidOperationException("spawn start failure");
            };
            earlyRegistry.OnSpawning += (_, _) => earlyTrace.Add("Second");

            Assert.Throws<InvalidOperationException>
            (
                () => earlyRegistry.SpawnWithKey(new TestDirectEntity(), 121UL)
            );
            CollectionAssert.AreEqual
            (
                new[]
                {
                    "First", "Second"
                },
                earlyTrace
            );
            Assert.AreEqual(0, earlyRegistry.Registered.Count);
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> 게임 Spawn과 직접 등록의 완료·해제 이벤트 구독자는 예외와 관계없이
        /// <br/> 모두 실행되며 커밋된 수명의 정리 결과를 유지한다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_EntitySpawnRegistry_완료해제이벤트_구독자실패격리()
        {
            for (var direct = 0; direct < 2; direct++)
            {
                var registry = new TestRegistry();
                var entity = new TestDirectEntity();
                var trace = new List<string>();

                registry.OnRegistered += (_, _) =>
                {
                    trace.Add("Registered.First");
                    throw new InvalidOperationException("registered observer failure");
                };
                registry.OnRegistered += (_, _) => trace.Add("Registered.Second");

                registry.OnSpawned += (_, _) =>
                {
                    trace.Add("Spawned.First");
                    throw new InvalidOperationException("spawned observer failure");
                };
                registry.OnSpawned += (_, _) => trace.Add("Spawned.Second");

                if (direct == 0)
                {
                    Assert.Throws<AggregateException>
                    (
                        () => registry.SpawnWithKey(entity, 122UL)
                    );
                }
                else
                {
                    Assert.Throws<InvalidOperationException>
                    (
                        () => registry.RegisterWithKey(entity, 122UL)
                    );
                }

                Assert.AreSame(entity, registry.Find(122UL));
                Assert.AreEqual(SpawnState.Spawned, entity.SpawnState);

                registry.OnDespawning += (_, _, _) =>
                {
                    trace.Add("Despawning.First");
                    throw new InvalidOperationException("despawning observer failure");
                };
                registry.OnDespawning += (_, _, _) => trace.Add("Despawning.Second");

                registry.OnUnregistering += (_, _, _) =>
                {
                    trace.Add("Unregistering.First");
                    throw new InvalidOperationException("unregistering observer failure");
                };
                registry.OnUnregistering += (_, _, _) => trace.Add("Unregistering.Second");

                registry.OnUnregistered += (_, _, _) =>
                {
                    trace.Add("Unregistered.First");
                    throw new InvalidOperationException("unregistered observer failure");
                };
                registry.OnUnregistered += (_, _, _) => trace.Add("Unregistered.Second");

                registry.OnDespawned += (_, _, _) =>
                {
                    trace.Add("Despawned.First");
                    throw new InvalidOperationException("despawned observer failure");
                };
                registry.OnDespawned += (_, _, _) => trace.Add("Despawned.Second");

                if (direct == 0)
                {
                    Assert.Throws<AggregateException>(() => entity.Despawn());
                }
                else
                {
                    Assert.Throws<AggregateException>(() => registry.Unregister(entity));
                }

                string[] expected;

                if (direct == 0)
                {
                    expected = new[]
                    {
                        "Registered.First", "Registered.Second",
                        "Spawned.First", "Spawned.Second",
                        "Despawning.First", "Despawning.Second",
                        "Unregistering.First", "Unregistering.Second",
                        "Unregistered.First", "Unregistered.Second",
                        "Despawned.First", "Despawned.Second"
                    };
                }
                else
                {
                    expected = new[]
                    {
                        "Registered.First", "Registered.Second",
                        "Unregistering.First", "Unregistering.Second",
                        "Unregistered.First", "Unregistered.Second"
                    };
                }

                CollectionAssert.AreEqual(expected, trace);
                Assert.AreEqual(0, registry.Registered.Count);
                Assert.AreEqual(SpawnState.Despawned, entity.SpawnState);
                Assert.IsFalse(entity.HasKey);
            }
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 직접 등록 완료 알림 실패는 확정된 Registry 수명을 되돌리지 않는다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_EntitySpawnRegistry_직접등록완료알림실패도_등록수명유지()
        {
            var registry = new TestRegistry();
            var entity = new TestDirectEntity();
            var spawnNotifications = 0;

            registry.OnRegistered += (_, _) =>
                throw new InvalidOperationException("registration observer failure");
            registry.OnSpawned += (_, _) => spawnNotifications++;

            var error = Assert.Throws<InvalidOperationException>
            (
                () => registry.RegisterWithKey(entity, 103UL)
            );

            Assert.AreEqual("registration observer failure", error.Message);
            Assert.AreSame(entity, registry.Find(103UL));
            Assert.IsTrue(registry.TryResolve(new EntityHandle(entity), out _));
            Assert.AreEqual(SpawnState.Spawned, entity.SpawnState);
            Assert.AreEqual(0, entity.SpawnHooks);
            Assert.AreEqual(0, spawnNotifications);

            Assert.IsTrue(registry.Unregister(entity));
            Assert.AreEqual(0, registry.Registered.Count);
            Assert.IsFalse(entity.HasKey);
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> 해제 이벤트 실패에도 두 등록 방식의 자원과 membership을 종료한다.
        /// <br/> 해제 실패 원인은 모두 호출자에게 전달한다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_EntitySpawnRegistry_해제알림복합실패도_terminal종료()
        {
            var registry = new TestRegistry();
            var direct = new TestDirectEntity();
            var spawned = new TestDirectEntity();
            registry.RegisterWithKey(direct, 104UL);
            registry.SpawnWithKey(spawned, 105UL);
            var unregisteringCount = 0;
            var unregisteredCount = 0;

            registry.OnUnregistering += (_, _, _) =>
            {
                unregisteringCount++;
                throw new InvalidOperationException("unregistering observer failure");
            };
            registry.OnUnregistered += (_, _, _) =>
            {
                unregisteredCount++;
                throw new InvalidOperationException("unregistered observer failure");
            };

            Assert.Throws<AggregateException>(() => registry.Unregister(direct));
            Assert.AreEqual(SpawnState.Despawned, direct.SpawnState);
            Assert.IsFalse(direct.HasKey);
            Assert.AreEqual(0, direct.DespawnHooks);
            Assert.IsNull(registry.Find(104UL));

            Assert.Throws<AggregateException>(() => spawned.Despawn());
            Assert.AreEqual(SpawnState.Despawned, spawned.SpawnState);
            Assert.IsFalse(spawned.HasKey);
            Assert.IsNull(registry.Find(105UL));
            Assert.AreEqual(0, registry.Registered.Count);
            Assert.AreEqual(2, unregisteringCount);
            Assert.AreEqual(2, unregisteredCount);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> 두 등록 경로의 완료 알림 중 동일 Entity 제거를 거절한다.
        /// <br/> 두 완료 알림을 끝낸 뒤에는 정상적으로 제거할 수 있다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_EntitySpawnRegistry_등록완료알림중_같은Entity제거거절()
        {
            var registry = new TestRegistry();
            var gameEntity = new TestDirectEntity();
            var directEntity = new TestDirectEntity();
            var spawnedNotifications = 0;
            var registeredNotifications = 0;

            registry.OnRegistered += (_, entity) =>
            {
                registeredNotifications++;
                Assert.Throws<InvalidOperationException>(() => entity.Despawn());
                Assert.Throws<InvalidOperationException>(() => registry.DespawnAll());
                Assert.AreEqual(SpawnState.Spawned, entity.SpawnState);
            };
            registry.OnSpawned += (_, entity) =>
            {
                spawnedNotifications++;
                Assert.AreSame(gameEntity, entity);
                Assert.AreEqual(SpawnState.Spawned, entity.SpawnState);
                Assert.AreSame(entity, registry.Find(entity.Key));
            };

            registry.SpawnWithKey(gameEntity, 110UL);
            registry.RegisterWithKey(directEntity, 111UL);

            Assert.AreEqual(2, registeredNotifications);
            Assert.AreEqual(1, spawnedNotifications);
            Assert.AreEqual(2, registry.Registered.Count);

            Assert.IsTrue(registry.Unregister(directEntity));
            gameEntity.Despawn();
            Assert.AreEqual(0, registry.Registered.Count);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> 직접 등록의 시작 알림에서 같은 Entity를 게임 Spawn으로
        /// <br/> 재진입할 수 없고 최초 등록 상태는 유지된다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_EntitySpawnRegistry_등록시작알림중_동일EntitySpawn재진입거절()
        {
            var registry = new TestRegistry();
            var entity = new TestDirectEntity();
            var observed = 0;

            registry.OnRegistering += (_, candidate) =>
            {
                if (!ReferenceEquals(candidate, entity))
                {
                    return;
                }

                observed++;

                Assert.Throws<InvalidOperationException>
                (
                    () => registry.SpawnWithKey(entity, 114UL)
                );
                Assert.AreEqual(SpawnState.Despawned, entity.SpawnState);
                Assert.IsNull(registry.Find(114UL));
            };

            registry.RegisterWithKey(entity, 114UL);

            Assert.AreEqual(1, observed);
            Assert.AreSame(entity, registry.Find(114UL));
            Assert.IsTrue(registry.Unregister(entity));
            Assert.AreEqual(0, registry.Registered.Count);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> 등록 해제 완료 알림 중 동일 Entity를 재등록하지 못하게 하고,
        /// <br/> 종료가 완전히 끝난 뒤 새로운 등록을 허용한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_EntitySpawnRegistry_해제완료알림중_동일Entity재등록거절()
        {
            var registry = new TestRegistry();
            var gameEntity = new TestDirectEntity();
            var directEntity = new TestDirectEntity();
            var completed = 0;

            registry.OnUnregistered += (key, entity, _) =>
            {
                completed++;
                Assert.AreEqual(SpawnState.Despawned, entity.SpawnState);
                Assert.IsFalse(entity.HasKey);
                Assert.IsNull(registry.Find(key));

                Assert.Throws<InvalidOperationException>
                (
                    () => registry.RegisterWithKey(entity, key)
                );
                Assert.Throws<InvalidOperationException>
                (
                    () => registry.SpawnWithKey(entity, key)
                );
                Assert.IsFalse(entity.HasKey);
            };

            registry.SpawnWithKey(gameEntity, 115UL);
            registry.RegisterWithKey(directEntity, 116UL);

            gameEntity.Despawn();
            Assert.IsTrue(registry.Unregister(directEntity));

            Assert.AreEqual(2, completed);
            Assert.AreEqual(0, registry.Registered.Count);

            registry.RegisterWithKey(gameEntity, 115UL);
            registry.RegisterWithKey(directEntity, 116UL);
            Assert.AreEqual(2, registry.Registered.Count);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> 등록 실패 시 부분 Binding은 정리하지만 완료된 수명의
        /// <br/> 정상 해제 알림은 발행하지 않는다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_EntitySpawnRegistry_등록Rollback은_정상해제알림을_발행하지않음()
        {
            var registry = new TestRegistry();
            var binding = new TestDirectBinding
            {
                FailOnRegister = true
            };
            registry.AttachBinding(binding);

            var registering = 0;
            var registered = 0;
            var unregistering = 0;
            var unregistered = 0;
            var despawning = 0;
            var despawned = 0;
            registry.OnRegistering += (_, _) => registering++;
            registry.OnRegistered += (_, _) => registered++;
            registry.OnUnregistering += (_, _, _) => unregistering++;
            registry.OnUnregistered += (_, _, _) => unregistered++;
            registry.OnDespawning += (_, _, _) => despawning++;
            registry.OnDespawned += (_, _, _) => despawned++;

            var gameEntity = new TestDirectEntity();
            var directEntity = new TestDirectEntity();

            Assert.Throws<InvalidOperationException>
            (
                () => registry.SpawnWithKey(gameEntity, 112UL)
            );
            Assert.Throws<InvalidOperationException>
            (
                () => registry.RegisterWithKey(directEntity, 113UL)
            );

            Assert.AreEqual(2, registering);
            Assert.AreEqual(0, registered);
            Assert.AreEqual(0, unregistering);
            Assert.AreEqual(0, unregistered);
            Assert.AreEqual(0, despawning);
            Assert.AreEqual(0, despawned);
            Assert.AreEqual(2, binding.RegisterCalls);
            Assert.AreEqual(2, binding.RemoveCalls);
            Assert.AreEqual(0, registry.Registered.Count);
            Assert.AreEqual(SpawnState.Despawned, gameEntity.SpawnState);
            Assert.AreEqual(SpawnState.Despawned, directEntity.SpawnState);
            Assert.IsFalse(gameEntity.HasKey);
            Assert.IsFalse(directEntity.HasKey);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> 이미 완료된 등록은 SpawnRollback 사유로 종료하더라도
        /// <br/> 정상 Registration 완료 알림을 끝까지 발행한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_EntitySpawnRegistry_완료된등록의_Rollback사유해제는_종료알림발행()
        {
            var registry = new TestRegistry();
            var gameEntity = new TestDirectEntity();
            var directEntity = new TestDirectEntity();
            var trace = new List<string>();

            registry.OnDespawning += (key, _, reason) =>
            {
                Assert.AreEqual(DespawnReason.SpawnRollback, reason);
                trace.Add($"Despawning:{key}");
            };
            registry.OnUnregistering += (key, _, reason) =>
            {
                Assert.AreEqual(DespawnReason.SpawnRollback, reason);
                trace.Add($"Unregistering:{key}");
            };
            registry.OnUnregistered += (key, entity, reason) =>
            {
                Assert.AreEqual(DespawnReason.SpawnRollback, reason);
                Assert.IsFalse(entity.HasKey);
                trace.Add($"Unregistered:{key}");
            };
            registry.OnDespawned += (key, _, reason) =>
            {
                Assert.AreEqual(DespawnReason.SpawnRollback, reason);
                trace.Add($"Despawned:{key}");
            };

            registry.SpawnWithKey(gameEntity, 117UL);
            registry.RegisterWithKey(directEntity, 118UL);

            gameEntity.Despawn(DespawnReason.SpawnRollback);
            Assert.IsTrue(registry.Unregister(directEntity, DespawnReason.SpawnRollback));

            CollectionAssert.AreEqual
            (
                new[]
                {
                    "Despawning:117",
                    "Unregistering:117",
                    "Unregistered:117",
                    "Despawned:117",
                    "Unregistering:118",
                    "Unregistered:118"
                },
                trace
            );
            Assert.AreEqual(0, registry.Registered.Count);
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// Spawn 이벤트는 게임 수명에만 발생하며 Binding은 모든 등록을 처리한다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_EntitySpawnRegistry_혼합Registry의_이벤트와_Binding은_수명을_구분()
        {
            var registry = new TestRegistry();
            var binding = new TestUnifiedBinding();
            registry.AttachBinding(binding);

            var liveSpawnEvents = 0;
            var liveDespawnEvents = 0;

            registry.OnSpawning += (_, _) => liveSpawnEvents++;
            registry.OnSpawned += (_, _) => liveSpawnEvents++;
            registry.OnDespawning += (_, _, _) => liveDespawnEvents++;
            registry.OnDespawned += (_, _, _) => liveDespawnEvents++;

            var live = registry.SpawnWithKey(new TestDirectEntity(), 101UL);
            var current = registry.RegisterWithKey(new TestDirectEntity(), 102UL);

            Assert.AreEqual(2, registry.Registered.Count);
            Assert.AreEqual(2, liveSpawnEvents);
            Assert.AreEqual(0, liveDespawnEvents);
            Assert.AreEqual(2, binding.RegisteredCount);
            Assert.IsTrue(registry.TryResolve(new EntityHandle(live), out _));
            Assert.IsTrue(registry.TryResolve(new EntityHandle(current), out _));

            Assert.IsTrue(registry.Unregister(current));

            Assert.AreEqual(2, liveSpawnEvents);
            Assert.AreEqual(0, liveDespawnEvents);
            Assert.AreEqual(1, binding.RemovedCount);
            Assert.AreEqual(1, registry.Registered.Count);

            live.Despawn();

            Assert.AreEqual(2, liveDespawnEvents);
            Assert.AreEqual(2, binding.RemovedCount);
            Assert.AreEqual(0, registry.Registered.Count);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 동일 Key의 중복 등록을 거절하며 기존 Direct 수명을 보존한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_EntitySpawnRegistry_직접등록Key중복등록은_기존수명을_유지()
        {
            var registry = new TestRegistry();
            var first = new TestDirectEntity();
            var duplicate = new TestDirectEntity();
            registry.RegisterWithKey(first, 81UL);

            Assert.Throws<InvalidOperationException>
            (
                () => registry.RegisterWithKey(duplicate, 81UL)
            );

            Assert.AreSame(first, registry.Find(81UL));
            Assert.IsFalse(duplicate.HasKey);
            Assert.AreEqual(SpawnState.Despawned, duplicate.SpawnState);
            Assert.AreEqual(0, duplicate.SpawnHooks);
            Assert.AreEqual(0, registry.ReleaseCount);

            Assert.IsTrue(registry.Unregister(first));
            Assert.AreEqual(0, registry.Registered.Count);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> 직접 등록 Binding이 생성·정리 실패해도
        /// <br/> Registry membership과 Key를 제거한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_EntitySpawnRegistry_직접등록Binding실패도_등록을_롤백()
        {
            var registry = new TestRegistry();
            var binding = new TestDirectBinding
            {
                FailOnRegister = true,
                FailOnRemove = true
            };
            registry.AttachBinding(binding);
            var entity = new TestDirectEntity();

            var error = Assert.Throws<AggregateException>
            (
                () => registry.RegisterWithKey(entity, 75UL)
            );

            Assert.AreEqual(1, binding.RegisterCalls);
            Assert.AreEqual(1, binding.RemoveCalls);
            Assert.AreEqual(2, error.InnerExceptions.Count);
            Assert.AreEqual(0, registry.Registered.Count);
            Assert.AreEqual(SpawnState.Despawned, entity.SpawnState);
            Assert.IsFalse(entity.HasKey);
            Assert.AreEqual(0, entity.DespawnHooks);
            Assert.AreEqual(0, registry.ReleaseCount);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> 공통 Binding이 직접 등록에도 실행되며,
        /// <br/> 뒤늦게 연결한 Binding에도 기존 객체를 동기화한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_EntitySpawnRegistry_일반Binding은_직접등록에도_연결된다()
        {
            var registry = new TestRegistry();
            var binding = new TestSimpleBinding();
            registry.AttachBinding(binding);
            var entity = registry.RegisterWithKey(new TestDirectEntity(), 54UL);

            Assert.AreEqual(1, binding.RegisterCalls);
            Assert.AreSame(entity, registry.Find(54UL));
            Assert.IsTrue(registry.DetachBinding(binding));

            var restoredBinding = new TestSimpleBinding();
            registry.AttachBinding(restoredBinding);
            Assert.AreEqual(1, restoredBinding.RegisterCalls);
            Assert.IsTrue(registry.Unregister(entity));
            Assert.AreEqual(0, registry.Registered.Count);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> 게임 Spawn과 직접 등록 객체가 함께 복원돼도
        /// <br/> 공통 Binding을 연결하고 해제할 수 있다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_EntitySpawnRegistry_혼합Registry복원후_공통Binding동기화()
        {
            var source = new TestRegistry();
            var live = new TestDirectEntity();
            source.NextEntity = live;
            Assert.IsTrue(source.TrySpawn(out _));

            var current = new TestDirectEntity();
            source.RegisterWithKey(current, 125UL);

            var restored = JsonUtility.FromJson<TestRegistry>(JsonUtility.ToJson(source));
            var binding = new TestUnifiedBinding();

            Assert.IsTrue(restored.TryResolve(new EntityHandle(restored.Find(125UL)), out _));
            Assert.AreEqual(2, restored.Registered.Count);

            restored.AttachBinding(binding);

            Assert.AreEqual(2, binding.RegisteredCount);
            Assert.AreEqual(0, binding.RemovedCount);

            Assert.IsTrue(restored.DetachBinding(binding));

            Assert.AreEqual(2, binding.RemovedCount);
            Assert.AreEqual(2, restored.Registered.Count);

            restored.DespawnAll();

            Assert.AreEqual(0, restored.Registered.Count);
            Assert.AreEqual(2, binding.RemovedCount);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> Key 해제 훅 실패에도 Direct Entry의
        /// <br/> 기본 Key와 Registry 등록은 제거된다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_EntitySpawnRegistry_직접등록Key해제실패도_기본Identity제거()
        {
            var registry = new TestRegistry();
            var bound = new TestKeyHookEntity
            {
                FailDuringClear = true
            };

            registry.RegisterWithKey(bound, 201UL);

            Assert.Throws<InvalidOperationException>
            (
                () => registry.Unregister(bound)
            );

            Assert.IsFalse(bound.HasKey);
            Assert.AreEqual(SpawnState.Despawned, bound.SpawnState);
            Assert.AreEqual(0, registry.Registered.Count);
            Assert.IsNull(registry.Find(201UL));
            Assert.AreEqual(1, bound.ClearedCount);

            var unbound = new TestKeyHookEntity
            {
                FailAfterAssign = true,
                FailDuringClear = true
            };

            var error = Assert.Throws<AggregateException>
            (
                () => registry.RegisterWithKey(unbound, 202UL)
            );

            Assert.AreEqual(2, error.InnerExceptions.Count);
            Assert.IsFalse(unbound.HasKey);
            Assert.AreEqual(SpawnState.Despawned, unbound.SpawnState);
            Assert.AreEqual(0, registry.Registered.Count);
            Assert.AreEqual(1, unbound.ClearedCount);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> 직접 등록 Binding 해제가 실패해도
        /// <br/> Binding 연결만 종료하고 Entity는 보존한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_EntitySpawnRegistry_직접등록Binding해제실패는_Entity수명유지()
        {
            var registry = new TestRegistry();
            var replica = registry.RegisterWithKey(new TestDirectEntity(), 212UL);
            var binding = new TestDirectBinding
            {
                FailOnRemove = true
            };

            registry.AttachBinding(binding);

            Assert.AreEqual(1, binding.RegisterCalls);

            var error = Assert.Throws<AggregateException>
            (
                () => registry.DetachBinding(binding)
            );

            Assert.AreEqual(1, error.InnerExceptions.Count);
            Assert.AreEqual(1, binding.RemoveCalls);
            Assert.AreSame(replica, registry.Find(212UL));
            Assert.IsTrue(registry.TryResolve(new EntityHandle(replica), out _));

            Assert.IsTrue(registry.Unregister(replica));

            Assert.AreEqual(1, binding.RemoveCalls);
            Assert.AreEqual(0, registry.Registered.Count);
            Assert.IsFalse(replica.HasKey);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> Authority Spawn과 직접 등록가 공존해도
        /// <br/> DespawnAll은 두 종료 경로를 구분한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_EntitySpawnRegistry_일반Spawn과_직접등록Entry는_종료계약이_다름()
        {
            var registry = new TestRegistry();
            var authority = new TestDirectEntity();
            var projected = new TestDirectEntity();
            registry.NextEntity = authority;

            Assert.IsTrue(registry.TrySpawn(out var spawned));
            Assert.AreSame(authority, spawned);
            registry.RegisterWithKey(projected, 91UL);

            Assert.AreEqual(2, authority.SpawnHooks);
            Assert.AreEqual(0, projected.SpawnHooks);
            Assert.IsFalse(registry.Unregister(authority));

            registry.DespawnAll();

            Assert.AreEqual(2, authority.DespawnHooks);
            Assert.AreEqual(0, projected.DespawnHooks);
            Assert.AreEqual(1, registry.ReleaseCount);
            Assert.IsFalse(authority.HasKey);
            Assert.IsFalse(projected.HasKey);
            Assert.AreEqual(0, registry.Registered.Count);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> 이전 등록 수명은 조회되지 않고
        /// <br/> 같은 Key의 새로운 Direct 수명만 조회한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_EntitySpawnRegistry_직접등록조회는_동일등록수명만_수용()
        {
            var registry = new TestRegistry();
            var first = new TestDirectEntity();
            registry.RegisterWithKey(first, 33UL);

            var oldHandle = new EntityHandle(first);
            Assert.IsTrue(registry.TryResolve(oldHandle, out var current));
            Assert.AreSame(first, current);
            ((TestDirectEntity)current).VisibleValue = 11;

            first.Despawn();
            registry.RegisterWithKey(first, 33UL);

            Assert.IsFalse(registry.TryResolve(oldHandle, out _));
            Assert.AreEqual(11, first.VisibleValue);

            var currentHandle = new EntityHandle(first);
            Assert.IsTrue(registry.TryResolve(currentHandle, out current));
            ((TestDirectEntity)current).VisibleValue = 22;
            Assert.AreEqual(22, first.VisibleValue);
            first.Despawn();

            var authority = registry.SpawnWithKey(new TestDirectEntity(), 41UL);
            var authorityHandle = new EntityHandle(authority);
            Assert.IsTrue(registry.TryResolve(authorityHandle, out _));
            authority.Despawn();
        }

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> 역직렬화 후에도 게임 Despawn이 아닌
        /// <br/> 관측 해제 경로로 종료한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_EntitySpawnRegistry_직접등록Entry의_복원은_제거모드를_보존()
        {
            var registry = new TestRegistry();
            registry.RegisterWithKey(new TestDirectEntity(), 72UL);

            var restored = JsonUtility.FromJson<TestRegistry>(JsonUtility.ToJson(registry));
            var entity = restored.Find(72UL) as TestDirectEntity;

            Assert.IsNotNull(entity);
            Assert.AreEqual(SpawnState.Spawned, entity.SpawnState);

            entity.Despawn();

            Assert.AreEqual(0, entity.DespawnHooks);
            Assert.IsFalse(entity.HasKey);
            Assert.AreEqual(0, restored.Registered.Count);
            Assert.AreEqual(0, restored.ReleaseCount);
        }

    #endregion

    #region Entity Key와 terminal 수명

        // ------------------------------------------------------------
        /// <summary>
        /// 생성기가 발급한 Key와 일반 Despawn의 정리를 검증한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_EntitySpawnRegistry_SpawnDespawn에서_Key를_발급하고_해제()
        {
            var registry = new TestRegistry();

            Assert.IsTrue(registry.TrySpawn(out var first));
            Assert.IsTrue(registry.TrySpawn(out var second));
            Assert.IsTrue(registry.TrySpawn(out var third));

            Assert.AreEqual(0UL, first.Key);
            Assert.AreEqual(1UL, second.Key);
            Assert.AreEqual(2UL, third.Key);

            second.Despawn();

            Assert.IsFalse(second.HasKey);
            Assert.AreEqual(SpawnState.Despawned, second.SpawnState);

            Assert.IsTrue(registry.TrySpawn(out var next));
            Assert.AreEqual(3UL, next.Key);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Key 훅은 Despawn과 rollback에서도 호출된다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_EntitySpawnRegistry_파생_Entity의_Key_확장훅을_유지()
        {
            var registry = new TestRegistry();
            var normal = new TestKeyHookEntity();

            registry.NextEntity = normal;
            Assert.IsTrue(registry.TrySpawn(out _));
            Assert.AreEqual(1, normal.AssignedCount);
            Assert.AreEqual(0, normal.ClearedCount);

            normal.Despawn();
            Assert.AreEqual(1, normal.ClearedCount);
            Assert.IsFalse(normal.HasKey);

            var failed = new TestKeyHookEntity
            {
                FailDuringSpawning = true
            };
            registry.NextEntity = failed;

            Assert.Throws<InvalidOperationException>(() => registry.TrySpawn(out _));

            Assert.AreEqual(1, failed.AssignedCount);
            Assert.AreEqual(1, failed.ClearedCount);
            Assert.IsFalse(failed.HasKey);
            Assert.AreEqual(SpawnState.Despawned, failed.SpawnState);

            var partial = new TestKeyHookEntity
            {
                FailAfterAssign = true
            };
            registry.NextEntity = partial;

            Assert.Throws<InvalidOperationException>(() => registry.TrySpawn(out _));
            Assert.AreEqual(1, partial.AssignedCount);
            Assert.AreEqual(1, partial.ClearedCount);
            Assert.IsFalse(partial.HasKey);
            Assert.AreEqual(SpawnState.Despawned, partial.SpawnState);

            var clearFailure = new TestKeyHookEntity
            {
                FailDuringClear = true
            };
            registry.NextEntity = clearFailure;
            Assert.IsTrue(registry.TrySpawn(out _));

            Assert.Throws<InvalidOperationException>(() => clearFailure.Despawn());
            Assert.AreEqual(1, clearFailure.ClearedCount);
            Assert.IsFalse(clearFailure.HasKey);
            Assert.AreEqual(SpawnState.Despawned, clearFailure.SpawnState);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 객체 미획득은 Key 발급 없이 false를 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_EntitySpawnRegistry_획득실패는_false이고_Key를_발급하지_않음()
        {
            var registry = new TestRegistry
            {
                ReturnNullOnAcquire = true
            };
            Assert.IsFalse(registry.TrySpawn(out var none));
            Assert.IsNull(none);
            Assert.AreEqual(0, registry.Registered.Count);
            Assert.IsTrue(registry.TrySpawn(out var entity));
            Assert.AreEqual(0UL, entity.Key);

            var paramRegistry = new TestParamRegistry
            {
                ReturnNullOnAcquire = true
            };
            Assert.IsFalse(paramRegistry.TrySpawn(23, out var noParamEntity));
            Assert.IsNull(noParamEntity);
            Assert.AreEqual(0, paramRegistry.Registered.Count);
            Assert.IsTrue(paramRegistry.TrySpawn(23, out var paramEntity));
            Assert.AreEqual(0UL, paramEntity.Key);
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> Key 할당 전 실패와 공통 Spawn 진입 후 실패의 반환 책임을 구분하고,
        /// <br/> 완료 Observer 예외가 확정된 객체를 반환하지 않는지 검증한다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_EntitySpawnRegistry_Spawn_실패와_완료_경계에서_Release_책임을_유지()
        {
            var registry = new TestRegistry();

            var keyFailure = new TestKeyHookEntity
            {
                FailAfterAssign = true
            };
            registry.NextEntity = keyFailure;

            Assert.Throws<InvalidOperationException>(() => registry.TrySpawn(out _));
            Assert.AreEqual(1, registry.ReleaseCount);
            Assert.IsFalse(keyFailure.HasKey);
            Assert.AreEqual(SpawnState.Despawned, keyFailure.SpawnState);

            var spawnFailure = new TestKeyHookEntity
            {
                FailDuringSpawning = true
            };
            registry.NextEntity = spawnFailure;

            Assert.Throws<InvalidOperationException>(() => registry.TrySpawn(out _));
            Assert.AreEqual(2, registry.ReleaseCount);
            Assert.IsFalse(spawnFailure.HasKey);
            Assert.AreEqual(SpawnState.Despawned, spawnFailure.SpawnState);

            var committed = new TestEntity();
            registry.NextEntity = committed;
            registry.OnSpawned += (_, _) => throw new InvalidOperationException("완료 Observer 실패");

            Assert.Throws<InvalidOperationException>(() => registry.TrySpawn(out _));
            Assert.AreEqual(2, registry.ReleaseCount);
            Assert.AreSame(committed, registry.Find(committed.Key));
            Assert.AreEqual(SpawnState.Spawned, committed.SpawnState);

            committed.Despawn();
            Assert.AreEqual(3, registry.ReleaseCount);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Key 설정과 반환이 모두 실패하면 두 원인을 보존한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_EntitySpawnRegistry_Key설정과_Release_이중실패를_함께전달()
        {
            var registry = new TestRegistry
            {
                FailRelease = true
            };
            var entity = new TestKeyHookEntity
            {
                FailAfterAssign = true
            };
            registry.NextEntity = entity;

            var failures = Assert.Throws<AggregateException>(() => registry.TrySpawn(out _));

            Assert.AreEqual(2, failures.InnerExceptions.Count);
            Assert.AreEqual("테스트 Key 설정 훅 실패", failures.InnerExceptions[0].Message);
            Assert.AreEqual("테스트 Release 실패", failures.InnerExceptions[1].Message);
            Assert.AreEqual(1, registry.ReleaseCount);
            Assert.IsFalse(entity.HasKey);
            Assert.AreEqual(SpawnState.Despawned, entity.SpawnState);
            Assert.AreEqual(0, registry.Registered.Count);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 파라미터 Spawn도 Key 거절과 반환 실패를 함께 전달한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_EntitySpawnRegistry_파라미터_Key거절과_Release_이중실패()
        {
            var source = new TestParamRegistry();
            var original = source.SpawnWithKey(new TestParamEntity(), 88UL);
            var acquired = JsonUtility.FromJson<TestParamEntity>(JsonUtility.ToJson(original));
            var registry = new TestParamRegistry
            {
                NextEntity = acquired,
                FailRelease = true
            };

            var failures = Assert.Throws<AggregateException>(() => registry.TrySpawn(5, out _));

            Assert.AreEqual(2, failures.InnerExceptions.Count);
            StringAssert.Contains("이미 Key", failures.InnerExceptions[0].Message);
            Assert.AreEqual("테스트 파라미터 Release 실패", failures.InnerExceptions[1].Message);
            Assert.AreEqual(1, registry.ReleaseCount);
            Assert.AreEqual(88UL, acquired.Key);
            Assert.AreEqual(SpawnState.Despawned, acquired.SpawnState);
            Assert.AreEqual(0, registry.Registered.Count);
        }

    #endregion

    #region Registry별 Key 범위

        // ----------------------------------------------------------------------
        /// <summary>
        /// 같은 Key라도 Registry가 다르면 서로 다른 Entity 수명으로 관리한다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_EntitySpawnRegistry_독립_Registry의_같은_Key는_서로_간섭하지_않음()
        {
            var firstRegistry = new TestRegistry();
            var secondRegistry = new TestRegistry();

            Assert.IsTrue(firstRegistry.TrySpawn(out var first));
            Assert.IsTrue(secondRegistry.TrySpawn(out var second));
            Assert.AreEqual(first.Key, second.Key);

            var firstHandle = new EntityHandle(first);
            var secondHandle = new EntityHandle(second);

            first.Despawn();

            Assert.IsNull(firstRegistry.Find(firstHandle.Key));
            Assert.AreSame(second, secondRegistry.Find(secondHandle.Key));
            Assert.IsFalse(firstHandle.IsValid());
            Assert.IsTrue(secondHandle.IsValid());
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// 같은 객체와 Key의 재등록은 이전 Handle의 등록 수명을 되살리지 않는다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_EntitySpawnRegistry_같은_Entity와_Key를_재등록해도_이전_Handle은_무효()
        {
            var registry = new TestRegistry();
            var entity = new TestEntity();

            Assert.AreSame(entity, registry.SpawnWithKey(entity, 7UL));
            var previous = new EntityHandle(entity);

            entity.Despawn();

            Assert.IsFalse(previous.IsValid());
            Assert.IsFalse(entity.HasKey);

            Assert.AreSame(entity, registry.SpawnWithKey(entity, 7UL));
            var current = new EntityHandle(entity);

            Assert.AreEqual(previous.Key, current.Key);
            Assert.AreNotEqual(previous.RegistrationRevision, current.RegistrationRevision);
            Assert.IsFalse(previous.IsValid());
            Assert.IsTrue(current.IsValid());
        }

    #endregion

    #region Local Handle의 등록 수명

        // ------------------------------------------------------------
        /// <summary>
        /// 다른 Registry와 오래된 수명의 Handle은 거절한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_EntitySpawnRegistry_TryResolve는_등록객체와_수명을_함께검증()
        {
            var firstRegistry = new TestRegistry();
            var otherRegistry = new TestRegistry();
            var entity = firstRegistry.SpawnWithKey(new TestEntity(), 71UL);
            var other = otherRegistry.SpawnWithKey(new TestEntity(), 71UL);
            var oldHandle = new EntityHandle(entity);

            Assert.IsTrue(firstRegistry.TryResolve(oldHandle, out var found));
            Assert.AreSame(entity, found);
            Assert.IsFalse(otherRegistry.TryResolve(oldHandle, out var missing));
            Assert.IsNull(missing);
            Assert.IsFalse(firstRegistry.TryResolve(default, out missing));

            entity.Despawn();

            Assert.IsFalse(firstRegistry.TryResolve(oldHandle, out missing));
            Assert.AreSame(entity, firstRegistry.SpawnWithKey(entity, 71UL));
            Assert.IsFalse(firstRegistry.TryResolve(oldHandle, out missing));

            var currentHandle = new EntityHandle(entity);
            Assert.IsTrue(firstRegistry.TryResolve(currentHandle, out var current));
            Assert.AreSame(entity, current);
            Assert.IsFalse(otherRegistry.TryResolve(currentHandle, out missing));

            entity.Despawn();
            other.Despawn();
        }

    #endregion

    #region 확정 Key 등록과 충돌 정책

        // ----------------------------------------------------------------------
        /// <summary>
        /// 외부 Key는 Registry를 통해 등록하고 중복 등록을 원본 변경 없이 거절한다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_EntitySpawnRegistry_외부_확정_Key를_수용하고_중복은_거절()
        {
            var registry = new TestRegistry();
            var entity = new TestEntity();

            Assert.AreSame(entity, registry.SpawnWithKey(entity, 91UL));
            Assert.AreEqual(91UL, entity.Key);
            Assert.AreSame(entity, registry.Find(91UL));

            var duplicate = new TestEntity();

            Assert.Throws<InvalidOperationException>
            (
                () => registry.SpawnWithKey(duplicate, 91UL)
            );

            Assert.IsFalse(duplicate.HasKey);
            Assert.AreEqual(SpawnState.Despawned, duplicate.SpawnState);
            Assert.AreSame(entity, registry.Find(91UL));

            Assert.IsTrue(registry.TrySpawn(out var local));
            Assert.AreEqual(0UL, local.Key);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 외부 Key 설정 훅의 실패와 정리를 검증한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_EntitySpawnRegistry_외부_Key_설정훅_실패는_부분할당을_정리()
        {
            var registry = new TestRegistry();

            var partial = new TestKeyHookEntity
            {
                FailAfterAssign = true
            };

            Assert.Throws<InvalidOperationException>
            (
                () => registry.SpawnWithKey(partial, 72UL)
            );

            Assert.AreEqual(1, partial.AssignedCount);
            Assert.AreEqual(1, partial.ClearedCount);
            Assert.IsFalse(partial.HasKey);
            Assert.AreEqual(SpawnState.Despawned, partial.SpawnState);
            Assert.AreEqual(0, registry.Registered.Count);

            var changed = new TestKeyHookEntity
            {
                AssignDifferentKey = true
            };

            Assert.Throws<InvalidOperationException>
            (
                () => registry.SpawnWithKey(changed, 73UL)
            );

            Assert.AreEqual(1, changed.AssignedCount);
            Assert.AreEqual(1, changed.ClearedCount);
            Assert.IsFalse(changed.HasKey);
            Assert.AreEqual(0, registry.Registered.Count);

            var clearingFailed = new TestKeyHookEntity
            {
                FailAfterAssign = true,
                FailDuringClear = true
            };

            var failures = Assert.Throws<AggregateException>
            (
                () => registry.SpawnWithKey(clearingFailed, 74UL)
            );

            Assert.AreEqual(2, failures.InnerExceptions.Count);
            Assert.AreEqual(1, clearingFailed.ClearedCount);
            Assert.IsFalse(clearingFailed.HasKey);
            Assert.AreEqual(0, registry.Registered.Count);

            var healthy = new TestKeyHookEntity();
            Assert.AreSame(healthy, registry.SpawnWithKey(healthy, 72UL));
            Assert.AreSame(healthy, registry.Find(72UL));
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 복원한 Key는 다시 설정하지 않는다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_EntitySpawnRegistry_외부_기존_Key는_설정훅을_다시_호출하지_않음()
        {
            var source = new TestRegistry();
            var original = source.SpawnWithKey(new TestKeyHookEntity(), 81UL);
            var restored = JsonUtility.FromJson<TestKeyHookEntity>(JsonUtility.ToJson(original));
            var target = new TestRegistry();

            Assert.IsNotNull(restored);
            Assert.AreEqual(SpawnState.Despawned, restored.SpawnState);
            Assert.AreEqual(81UL, restored.Key);
            Assert.AreSame(restored, target.SpawnWithKey(restored, 81UL));
            Assert.AreEqual(0, restored.AssignedCount);
            Assert.AreSame(restored, target.Find(81UL));

            // Key가 이미 있는 객체는 상위 SpawnExisting 경로로도 등록할 수 있다.
            var direct = JsonUtility.FromJson<TestKeyHookEntity>(JsonUtility.ToJson(original));
            var commonRegistry = new TestRegistry();

            Assert.AreSame(direct, commonRegistry.SpawnExisting(direct));
            Assert.AreEqual(0, direct.AssignedCount);
            Assert.AreSame(direct, commonRegistry.Find(81UL));
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// 생성기가 확정 Key와 충돌하면 중복을 등록하지 않고 다음 발급을 유지한다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_EntitySpawnRegistry_생성_Key와_확정_Key가_충돌하면_명시적으로_거절()
        {
            var registry = new TestRegistry();
            var imported = new TestEntity();
            var local = new TestEntity();

            registry.SpawnWithKey(imported, 0UL);
            registry.NextEntity = local;

            Assert.Throws<InvalidOperationException>
            (
                () => registry.TrySpawn(out _)
            );

            Assert.AreSame(imported, registry.Find(0UL));
            Assert.AreEqual(1, registry.Registered.Count);
            Assert.IsFalse(local.HasKey);
            Assert.AreEqual(SpawnState.Despawned, local.SpawnState);

            Assert.IsTrue(registry.TrySpawn(out var next));
            Assert.AreEqual(1UL, next.Key);
        }

    #endregion

    #region Key 발급과 Identity 보존

        // ------------------------------------------------------------
        /// <summary>
        /// 기존 Key가 있는 후보를 거절해도 Key를 보존한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_EntitySpawnRegistry_기존_Key가_있는_Acquire_후보의_Key를_보존()
        {
            var registry = new TestRegistry();
            var original = registry.SpawnWithKey(new TestEntity(), 77UL);
            var acquired = JsonUtility.FromJson<TestEntity>(JsonUtility.ToJson(original));

            Assert.AreEqual(SpawnState.Despawned, acquired.SpawnState);
            Assert.AreEqual(77UL, acquired.Key);

            registry.NextEntity = acquired;
            Assert.Throws<InvalidOperationException>(() => registry.TrySpawn(out _));

            Assert.IsTrue(acquired.HasKey);
            Assert.AreEqual(77UL, acquired.Key);
            Assert.AreEqual(SpawnState.Despawned, acquired.SpawnState);
            Assert.AreSame(original, registry.Find(77UL));
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 파라미터 Spawn 실패 후에도 기존 Key를 보존한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_EntitySpawnRegistry_파라미터_Spawn_발급실패에도_기존_Key를_보존()
        {
            var registry = new TestParamRegistry();
            var original = registry.SpawnWithKey(new TestParamEntity(), 38UL);
            var acquired = JsonUtility.FromJson<TestParamEntity>(JsonUtility.ToJson(original));

            Assert.AreEqual(SpawnState.Despawned, acquired.SpawnState);
            Assert.AreEqual(38UL, acquired.Key);

            registry.NextEntity = acquired;
            Assert.Throws<InvalidOperationException>(() => registry.TrySpawn(12, out _));

            Assert.IsTrue(acquired.HasKey);
            Assert.AreEqual(38UL, acquired.Key);
            Assert.AreEqual(SpawnState.Despawned, acquired.SpawnState);
            Assert.AreSame(original, registry.Find(38UL));
        }

    #endregion

    #region 직렬화 복원과 Runtime 재연결

        // ------------------------------------------------------------
        /// <summary>
        /// JSON 복원 시 Registry 수명과 KeyGenerator를 검증한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_EntitySpawnRegistry_JSONRoundTrip후_Membership과_RegistryRuntime_복원()
        {
            var registry = new TestRegistry();

            Assert.IsTrue(registry.TrySpawn(out var first));
            Assert.IsTrue(registry.TrySpawn(out var second));

            var firstKey = first.Key;
            var secondKey = second.Key;
            var json = JsonUtility.ToJson(registry);
            var restored = JsonUtility.FromJson<TestRegistry>(json);

            Assert.IsNotNull(restored);
            Assert.AreEqual(2, restored.Registered.Count);

            var restoredFirst = restored.Find(firstKey);
            var restoredSecond = restored.Find(secondKey);

            Assert.IsNotNull(restoredFirst);
            Assert.IsNotNull(restoredSecond);
            Assert.AreEqual(SpawnState.Spawned, restoredFirst.SpawnState);
            Assert.AreEqual(SpawnState.Spawned, restoredSecond.SpawnState);
            Assert.Greater(restoredFirst.RegistrationRevision, 0UL);
            Assert.Greater(restoredSecond.RegistrationRevision, 0UL);

            var restoredHandle = new EntityHandle(restoredFirst);
            Assert.IsTrue(restoredHandle.IsValid());

            restoredFirst.Despawn();
            Assert.IsFalse(restoredHandle.IsValid());

            Assert.AreEqual(1, restored.Registered.Count);
            Assert.IsFalse(restoredFirst.HasKey);
            Assert.AreEqual(SpawnState.Despawned, restoredFirst.SpawnState);

            Assert.IsTrue(restored.TrySpawn(out var next));
            Assert.AreEqual(2UL, next.Key);
        }

    #endregion

    }
}
