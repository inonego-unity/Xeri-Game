/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : TEST_Entity.cs
수정일 : 2026-11-12

# 설명
EntityBase의 Identity와 Registry lifecycle 계약을 하나의 상태 전이로 검증한다.
HP·Group은 Entity Core 책임이 아니므로 이 테스트에서 다루지 않는다.

# 테스트 구성
 L: Registry lifecycle과 Identity
========================================================================= BLOCK_HEADER_END */

using System;

using NUnit;
using NUnit.Framework;

namespace inonego.Xeri.Game.TEST._Entity
{
    using Xeri;
    using Xeri.Game;

    // ============================================================
    /// <summary>
    /// EntityBase의 Registry lifecycle과 Identity 계약을 검증한다.
    /// </summary>
    // ============================================================
    public class TEST_Entity
    {

    #region 헬퍼

        private sealed class TestEntity : EntityBase
        {
            // NONE
        }

        private sealed class TestRegistry : EntitySpawnRegistry<TestEntity>
        {
            public TestEntity NextEntity { get; set; }

            protected override TestEntity Acquire()
            {
                var entity = NextEntity ?? new TestEntity();
                NextEntity = null;
                return entity;
            }

            public bool TrySpawnPublic(out TestEntity spawned)
            {
                return TrySpawn(out spawned);
            }
        }

    #endregion

    #region L-1: Registry lifecycle과 Identity

        [Test]
        public void TEST_Entity_RegistryLifecycle에_따라_Identity와_ReadOnlyState_전이()
        {
            var registry = new TestRegistry();
            var entity = new TestEntity();
            IReadOnlyEntity readOnly = entity;

            Assert.AreEqual(SpawnState.Despawned, readOnly.SpawnState);
            Assert.IsFalse(readOnly.HasKey);

            registry.NextEntity = entity;
            Assert.IsTrue(registry.TrySpawnPublic(out var spawned));
            Assert.AreSame(entity, spawned);
            Assert.AreEqual(SpawnState.Spawned, readOnly.SpawnState);
            Assert.IsTrue(readOnly.HasKey);
            Assert.AreEqual(0UL, readOnly.Key);

            entity.Despawn();

            Assert.AreEqual(SpawnState.Despawned, readOnly.SpawnState);
            Assert.IsFalse(readOnly.HasKey);
            Assert.Throws<InvalidOperationException>
            (
                () =>
                {
                    var _ = entity.Key;
                }
            );
        }

    #endregion

    }
}
