/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : TEST_EntitySpawnRegistry.cs
수정일 : 2026-11-13

# 설명
EntitySpawnRegistry의 Registry별 Key 범위, 등록 수명, 외부 Key 수용과 직렬화 복원을 검증한다.
HP·Group lifetime 정책은 Entity Core 필수 계약이 아니므로 이 테스트에서 다루지 않는다.

# 테스트 구성
 L: Entity Key와 등록 수명
 I: 외부 확정 Key 및 KeyGenerator 충돌
 S: 직렬화 후 membership·runtime 연결·KeyGenerator 복원
========================================================================= BLOCK_HEADER_END */

using System;

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

    #region L-1: Key와 terminal lifecycle

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
            Assert.AreEqual(0, registry.Spawned.Count);
            Assert.IsTrue(registry.TrySpawn(out var entity));
            Assert.AreEqual(0UL, entity.Key);

            var paramRegistry = new TestParamRegistry
            {
                ReturnNullOnAcquire = true
            };
            Assert.IsFalse(paramRegistry.TrySpawn(23, out var noParamEntity));
            Assert.IsNull(noParamEntity);
            Assert.AreEqual(0, paramRegistry.Spawned.Count);
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
            Assert.AreEqual(0, registry.Spawned.Count);
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
            Assert.AreEqual(0, registry.Spawned.Count);
        }

    #endregion

    #region L-2: Registry Scope와 등록 수명

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

    #region I-1: 확정 Key 수용 및 충돌 정책

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
            Assert.AreEqual(0, registry.Spawned.Count);

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
            Assert.AreEqual(0, registry.Spawned.Count);

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
            Assert.AreEqual(0, registry.Spawned.Count);

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
            Assert.AreEqual(1, registry.Spawned.Count);
            Assert.IsFalse(local.HasKey);
            Assert.AreEqual(SpawnState.Despawned, local.SpawnState);

            Assert.IsTrue(registry.TrySpawn(out var next));
            Assert.AreEqual(1UL, next.Key);
        }

    #endregion

    #region I-2: 발급 실패 시 기존 Identity 보존

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

    #region S-1: JSON round-trip self-rehydrate

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
            Assert.AreEqual(2, restored.Spawned.Count);

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

            Assert.AreEqual(1, restored.Spawned.Count);
            Assert.IsFalse(restoredFirst.HasKey);
            Assert.AreEqual(SpawnState.Despawned, restoredFirst.SpawnState);

            Assert.IsTrue(restored.TrySpawn(out var next));
            Assert.AreEqual(2UL, next.Key);
        }

    #endregion

    }
}
