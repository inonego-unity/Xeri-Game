/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : TEST_EntityActorFactory.cs
수정일 : 2026-11-12

# 설명
EntityActorFactory의 Detached Actor 생성과 생성 실패 Provider 반환 계약을 검증한다.
Unity Test Runner (Play Mode) 에서 실행한다.

# 테스트 구성
 X: Actor 생성 실패 rollback
========================================================================= BLOCK_HEADER_END */

using System;
using System.Collections;
using System.Threading;
using System.Threading.Tasks;

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
    /// EntityActorFactory 생성 실패 정리 테스트.
    /// </summary>
    // ============================================================
    public class TEST_EntityActorFactory
    {

    #region 헬퍼

        private sealed class TestEntity : EntityBase
        {
            // NONE
        }

        private sealed class TestEntityActor : EntityActorBase<TestEntity>
        {
            // NONE
        }

        private sealed class FailingFactory : EntityActorFactory<TestEntityActor, TestEntity>
        {
            public FailingFactory(IGameObjectProvider provider) : base(provider)
            {
                // NONE
            }

            protected override void PrepareActor(TestEntityActor actor, TestEntity entity)
            {
                throw new InvalidOperationException("테스트 Actor 준비 실패");
            }
        }

        private sealed class TrackingProvider : IGameObjectProvider
        {
            public Transform Parent { get; set; }
            public GameObject GameObject { get; set; }
            public GameObject Released { get; private set; }
            public int ReleaseCount { get; private set; }

            public GameObject Acquire(bool worldPositionStays = true)
            {
                return GameObject;
            }

            public async Awaitable<GameObject> AcquireAsync(bool worldPositionStays = true)
            {
                return await Task.FromResult(GameObject);
            }

            public void Release(GameObject go, bool worldPositionStays = true)
            {
                Released = go;
                ReleaseCount++;
            }
        }

    #endregion

    #region X-1: Actor 생성 실패 rollback

        [UnityTest]
        public IEnumerator TEST_EntityActorFactory_Create_Actor_생성_실패_Provider_반환()
        {
            var gameObject = new GameObject("FailingActor");
            var actor = gameObject.AddComponent<TestEntityActor>();
            var provider = new TrackingProvider
            {
                GameObject = gameObject
            };
            var factory = new FailingFactory(provider);
            var entity = new TestEntity();

            try
            {
                Assert.Throws<InvalidOperationException>(() => factory.Create(entity));
                Assert.AreEqual(1, provider.ReleaseCount);
                Assert.AreSame(gameObject, provider.Released);
                Assert.IsNull(actor.Entity);

                yield return null;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }

    #endregion

    }
}
