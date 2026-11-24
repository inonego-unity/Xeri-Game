/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : TEST_EntityActorFactory.cs
수정일 : 2026-11-16

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

        private sealed class EntitySample : EntityBase
        {
            // NONE
        }

        private sealed class ActorProbe : EntityActorBase<EntitySample>
        {
            // NONE
        }

        private sealed class ActorFactoryProbe : EntityActorFactory<ActorProbe, EntitySample>
        {
            // ------------------------------------------------------------
            /// <summary>
            /// GameObject Provider로 생성 실패 Factory를 만든다.
            /// </summary>
            // ------------------------------------------------------------
            public ActorFactoryProbe(IGameObjectProvider provider) : base(provider)
            {
                // NONE
            }

            protected override void PrepareActor(ActorProbe actor, EntitySample entity)
            {
                throw new InvalidOperationException("테스트 Actor 준비 실패");
            }
        }

        private sealed class GameObjectProviderProbe : IGameObjectProvider
        {
            // ------------------------------------------------------------
            /// <summary>
            /// 테스트에서 사용하지 않는 부모 Transform.
            /// </summary>
            // ------------------------------------------------------------
            public Transform Parent { get; set; }

            // ------------------------------------------------------------
            /// <summary>
            /// 획득 시 반환할 GameObject.
            /// </summary>
            // ------------------------------------------------------------
            public GameObject GameObject { get; set; }

            // ------------------------------------------------------------
            /// <summary>
            /// 마지막으로 반환된 GameObject.
            /// </summary>
            // ------------------------------------------------------------
            public GameObject Released { get; private set; }

            // ------------------------------------------------------------
            /// <summary>
            /// 반환 호출 횟수.
            /// </summary>
            // ------------------------------------------------------------
            public int ReleaseCount { get; private set; }

            // ------------------------------------------------------------
            /// <summary>
            /// 설정한 GameObject를 그대로 반환한다.
            /// </summary>
            // ------------------------------------------------------------
            public GameObject Acquire(bool worldPositionStays = true)
            {
                return GameObject;
            }

            // ------------------------------------------------------------
            /// <summary>
            /// 설정한 GameObject를 비동기로 반환한다.
            /// </summary>
            // ------------------------------------------------------------
            public async Awaitable<GameObject> AcquireAsync(bool worldPositionStays = true)
            {
                return await Task.FromResult(GameObject);
            }

            // ------------------------------------------------------------
            /// <summary>
            /// 반환된 GameObject와 호출 횟수를 기록한다.
            /// </summary>
            // ------------------------------------------------------------
            public void Release(GameObject go, bool worldPositionStays = true)
            {
                Released = go;
                ReleaseCount++;
            }
        }

    #endregion

    #region X-1: Actor 생성 실패 rollback

        // ------------------------------------------------------------
        /// <summary>
        /// Actor 준비가 실패하면 획득한 GameObject를 Provider로 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        [UnityTest]
        public IEnumerator TEST_EntityActorFactory_Create_Actor_생성_실패_Provider_반환()
        {
            var gameObject = new GameObject("FailingActor");
            var actor = gameObject.AddComponent<ActorProbe>();
            var provider = new GameObjectProviderProbe
            {
                GameObject = gameObject
            };
            var factory = new ActorFactoryProbe(provider);
            var entity = new EntitySample();

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
