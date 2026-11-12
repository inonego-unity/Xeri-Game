/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : TEST_EntitySpawnRegistry.cs
수정일 : 2026-11-12

# 설명
EntitySpawnRegistry의 Key lifecycle과 직렬화 self-rehydrate 계약을 검증한다.
HP·Group lifetime 정책은 Entity Core 필수 계약이 아니므로 이 테스트에서 다루지 않는다.

# 테스트 구성
 L: Entity Key와 terminal lifecycle
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
        private sealed class TestEntity : EntityBase
        {
            // NONE
        }

        [Serializable]
        private sealed class TestRegistry : EntitySpawnRegistry<TestEntity>
        {
            public TestEntity NextEntity { get; set; }

            protected override TestEntity Acquire()
            {
                var entity = NextEntity ?? new TestEntity();
                NextEntity = null;
                return entity;
            }

            public new bool TrySpawn(out TestEntity spawned)
            {
                return base.TrySpawn(out spawned);
            }
        }

    #endregion

    #region L-1: Key와 terminal lifecycle

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

    #endregion

    #region S-1: JSON round-trip self-rehydrate

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

            restoredFirst.Despawn();

            Assert.AreEqual(1, restored.Spawned.Count);
            Assert.IsFalse(restoredFirst.HasKey);
            Assert.AreEqual(SpawnState.Despawned, restoredFirst.SpawnState);

            Assert.IsTrue(restored.TrySpawn(out var next));
            Assert.AreEqual(2UL, next.Key);
        }

    #endregion

    }
}
