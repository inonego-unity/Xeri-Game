/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : TEST_LocalFoundationCompositionScene.cs
수정일 : 2026-11-14

# 설명
Registry·자동 Actor·WorldObject 상태를 하나의 로컬 Scene 전환 흐름에서 조립한다.
독립적인 Entity 수명과 Scene State의 보존 및 표현 재구성 책임을 검증한다.

# 특이사항, 제약사항
Network/Save Host를 구현하지 않고 World State Store 및 Scene Binding을 소비한다.
========================================================================= BLOCK_HEADER_END */

using System;
using System.Collections;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

using NUnit;
using NUnit.Framework;

namespace inonego.Xeri.Game.TEST._World
{
    // ======================================================================
    /// <summary>
    /// 로컬 Registry·Actor·Scene State 조합의 실제 Unity 수명 흐름을 검증한다.
    /// </summary>
    // ======================================================================
    public class TEST_LocalFoundationCompositionScene
    {

    #region 테스트 객체

        [Serializable]
        private sealed class LocalEntity : EntityBase
        {
            // NONE
        }

        private sealed class LocalActor : EntityActorBase<LocalEntity>
        {
            // NONE
        }

        [Serializable]
        private sealed class LocalRegistry : EntitySpawnRegistry<LocalEntity>
        {
            protected override LocalEntity Acquire() => new();
        }

        private readonly List<string> sceneNames = new();
        private GameObject actorPrefab = null;
        private LocalRegistry registry = null;
        private WorldObjectStateStore worldStore = null;
        private WorldObjectSceneBinding worldBinding = null;
        private EntityActorRuntimeBinding<LocalActor, LocalEntity> actorBinding = null;

    #endregion

    #region 로컬 Scene 조합

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> Entity는 Scene 전환 중 유지하고 Actor만 해제·재생성한다.
        /// <br/> Scene에 다시 진입하면 문 상태를 이전 값으로 복원한다.
        /// </summary>
        // ------------------------------------------------------------
        [UnityTest]
        public IEnumerator TEST_LocalFoundationCompositionScene_문상태와_Entity수명은_분리된다()
        {
            var firstScene = CreateScene();
            var firstGate = AddWorldObject(firstScene, "gate", 3);
            worldStore = new WorldObjectStateStore();
            worldBinding = new WorldObjectSceneBinding(worldStore);
            worldBinding.Bind
            (
                new IWorldObject[]
                {
                    firstGate
                },
                obj => ((TestSceneWorldObject)obj).CreateState()
            );

            registry = new LocalRegistry();
            actorPrefab = new GameObject("LocalFoundationActorPrefab");
            actorPrefab.AddComponent<LocalActor>();
            actorPrefab.SetActive(false);

            var provider = new PrefabGameObjectProvider(actorPrefab, null);
            var factory = new EntityActorFactory<LocalActor, LocalEntity>(provider);
            actorBinding = EntityActorRuntimeBinding<LocalActor, LocalEntity>.CreateAndBind
            (
                registry,
                factory
            );

            Assert.IsTrue(registry.TrySpawn(out var entity));
            Assert.IsTrue(actorBinding.TryFind(entity, out var firstActor));
            Assert.AreSame(entity, firstActor.Entity);
            Assert.AreEqual(1, registry.Registered.Count);

            firstGate.Change(23);
            worldBinding.Unbind();
            actorBinding.Unbind();

            Assert.IsFalse(firstGate.IsBound);
            Assert.IsFalse(actorBinding.TryFind(entity, out _));
            Assert.AreSame(entity, registry.Find(entity.Key));

            yield return SceneManager.UnloadSceneAsync(firstScene);

            var nextScene = CreateScene();
            var nextGate = AddWorldObject(nextScene, "gate", 99);
            worldBinding.Bind
            (
                new IWorldObject[]
                {
                    nextGate
                }
            );
            actorBinding.Bind(registry);

            Assert.AreEqual(23, nextGate.Value);
            Assert.IsTrue(actorBinding.TryFind(entity, out var nextActor));
            Assert.AreSame(entity, nextActor.Entity);
            Assert.AreEqual(1, registry.Registered.Count);

            entity.Despawn();

            Assert.IsFalse(actorBinding.TryFind(entity, out _));
            Assert.AreEqual(0, registry.Registered.Count);
            Assert.IsTrue((worldBinding.Status == WorldObjectBindingStatus.Bound));
            Assert.AreEqual(23, nextGate.Value);

            nextGate.Change(41);
            actorBinding.Unbind();
            worldBinding.Unbind();
            yield return SceneManager.UnloadSceneAsync(nextScene);

            var revisitedScene = CreateScene();
            var revisitedGate = AddWorldObject(revisitedScene, "gate", 7);
            worldBinding.Bind
            (
                new IWorldObject[]
                {
                    revisitedGate
                }
            );

            Assert.AreEqual(41, revisitedGate.Value);
            Assert.AreEqual(1, worldStore.States.Count);
            worldBinding.Unbind();
        }

    #endregion

    #region Scene 헬퍼

        private Scene CreateScene()
        {
            var sceneName = "LocalComposition_" + Guid.NewGuid().ToString("N");
            var scene = SceneManager.CreateScene(sceneName);
            sceneNames.Add(sceneName);
            return scene;
        }

        private static TestSceneWorldObject AddWorldObject(Scene scene, string id, int initialValue)
        {
            var gameObject = new GameObject("SceneGate");
            var gate = gameObject.AddComponent<TestSceneWorldObject>();
            gate.Configure(id, initialValue);
            SceneManager.MoveGameObjectToScene(gameObject, scene);
            return gate;
        }

    #endregion

    #region 정리

        // ----------------------------------------------------------------------
        /// <summary>
        /// 실패한 테스트도 Binding과 Scene, Actor Factory의 원본 Prefab을 정리한다.
        /// </summary>
        // ----------------------------------------------------------------------
        [UnityTearDown]
        public IEnumerator TearDown()
        {
            actorBinding?.Unbind();

            if (worldBinding != null && worldBinding.Status != WorldObjectBindingStatus.Unbound)
            {
                worldBinding.Unbind();
            }

            registry?.DespawnAll();

            foreach (var name in sceneNames)
            {
                var scene = SceneManager.GetSceneByName(name);

                if (scene.IsValid() && scene.isLoaded)
                {
                    yield return SceneManager.UnloadSceneAsync(scene);
                }
            }

            sceneNames.Clear();

            if (actorPrefab != null)
            {
                UnityEngine.Object.DestroyImmediate(actorPrefab);
                actorPrefab = null;
            }
        }

    #endregion

    }
}
