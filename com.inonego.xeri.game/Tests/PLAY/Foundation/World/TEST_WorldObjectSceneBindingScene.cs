/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : TEST_WorldObjectSceneBindingScene.cs
수정일 : 2026-11-15

# 설명
별도 외부 Scene 에셋 없이 Unity Scene의 생성·언로드·재생성 수명에서
WorldObjectSceneBinding과 실제 MonoBehaviour 상태 적용을 검증한다.
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
    // ============================================================
    /// <summary>
    /// WorldObjectSceneBinding의 Scene 재진입 상태 보존을 검증한다.
    /// </summary>
    // ============================================================
    public class TEST_WorldObjectSceneBindingScene
    {

    #region 테스트 데이터

        [Serializable]
        private sealed class SaveData
        {
            // ------------------------------------------------------------
            /// <summary>
            /// Unity JsonUtility로 저장할 WorldObject 상태.
            /// </summary>
            // ------------------------------------------------------------
            public TestSceneWorldState State = null;
        }

        private readonly List<string> sceneNames = new();

    #endregion

    #region Scene 상태 적용

        // ------------------------------------------------------------
        /// <summary>
        /// Scene 수명이 종료돼도 직렬화한 State는 새 객체로 복원된다.
        /// </summary>
        // ------------------------------------------------------------
        [UnityTest]
        public IEnumerator TEST_WorldObjectSceneBindingScene_저장후_새_Scene에서_상태를_복원()
        {
            var firstScene = CreateScene();
            var first = AddWorldObject(firstScene, "gate", 3);
            var store = new WorldObjectStateStore();
            var initialBinding = new WorldObjectSceneBinding(store);

            initialBinding.Bind(Objects(first), obj => ((TestSceneWorldObject)obj).CreateState());
            Assert.AreEqual(3, first.Value);

            first.Change(23);
            Assert.IsTrue(store.TryGetState("gate", out var current));

            var data = new SaveData
            {
                State = (TestSceneWorldState)current
            };
            var json = JsonUtility.ToJson(data);

            initialBinding.Unbind();
            Assert.IsFalse(first.IsBound);

            yield return SceneManager.UnloadSceneAsync(firstScene);

            var loaded = JsonUtility.FromJson<SaveData>(json);
            Assert.IsNotNull(loaded.State);
            Assert.AreEqual("gate", loaded.State.ID);
            Assert.AreEqual(23, loaded.State.Value);

            var loadedStates = new WorldObjectState[]
            {
                loaded.State
            };
            var restoredStore = new WorldObjectStateStore(loadedStates);
            var restoredBinding = new WorldObjectSceneBinding(restoredStore);

            var nextScene = CreateScene();
            var next = AddWorldObject(nextScene, "gate", 99);
            restoredBinding.Bind(Objects(next));

            Assert.AreEqual(23, next.Value);
            Assert.AreNotEqual(99, next.Value);
            Assert.AreNotSame(current, loaded.State);

            next.Change(41);
            restoredBinding.Unbind();
            yield return SceneManager.UnloadSceneAsync(nextScene);

            var revisitedScene = CreateScene();
            var revisited = AddWorldObject(revisitedScene, "gate", 3);
            restoredBinding.Bind(Objects(revisited));

            Assert.AreEqual(41, revisited.Value);
            Assert.AreEqual(1, restoredStore.States.Count);

            restoredBinding.Unbind();
            Assert.IsFalse(revisited.IsBound);
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// 파괴된 Scene 객체는 새 연결에서 거절하고 기존 연결은 안전하게 해제한다.
        /// </summary>
        // ----------------------------------------------------------------------
        [UnityTest]
        public IEnumerator TEST_WorldObjectSceneBindingScene_파괴객체_거절과_연결정리()
        {
            var scene = CreateScene();
            var destroyedBeforeBind = AddWorldObject(scene, "discarded", 1);
            var store = new WorldObjectStateStore();
            var binding = new WorldObjectSceneBinding(store);

            UnityEngine.Object.Destroy(destroyedBeforeBind.gameObject);
            yield return null;

            Assert.Throws<ArgumentException>(() => binding.Bind(Objects(destroyedBeforeBind), obj => ((TestSceneWorldObject)obj).CreateState()));
            Assert.IsFalse((binding.Status != WorldObjectBindingStatus.Unbound));
            Assert.AreEqual(0, store.States.Count);

            var first = AddWorldObject(scene, "gate", 3);
            binding.Bind(Objects(first), obj => ((TestSceneWorldObject)obj).CreateState());
            first.Change(17);

            UnityEngine.Object.Destroy(first.gameObject);
            yield return null;

            binding.Unbind();

            Assert.IsFalse((binding.Status != WorldObjectBindingStatus.Unbound));
            Assert.IsTrue(store.TryGetState("gate", out var saved));
            Assert.AreEqual(17, ((TestSceneWorldState)saved).Value);

            var next = AddWorldObject(scene, "gate", 99);
            binding.Bind(Objects(next));

            Assert.AreEqual(17, next.Value);
            binding.Unbind();
            binding.Unbind();
            Assert.IsFalse(next.IsBound);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 두 Scene을 동시에 연결하고 하나만 종료해도 다른 상태는 유지된다.
        /// </summary>
        // ------------------------------------------------------------
        [UnityTest]
        public IEnumerator TEST_WorldObjectSceneBindingScene_동시_Scene의_독립_수명()
        {
            var firstScene = CreateScene();
            var secondScene = CreateScene();
            var first = AddWorldObject(firstScene, "first", 1);
            var second = AddWorldObject(secondScene, "second", 2);
            var store = new WorldObjectStateStore();
            var firstBinding = new WorldObjectSceneBinding(store);
            var secondBinding = new WorldObjectSceneBinding(store);

            firstBinding.Bind(Objects(first), obj => ((TestSceneWorldObject)obj).CreateState());
            secondBinding.Bind(Objects(second), obj => ((TestSceneWorldObject)obj).CreateState());

            first.Change(10);
            second.Change(20);
            firstBinding.Unbind();

            yield return SceneManager.UnloadSceneAsync(firstScene);

            Assert.AreEqual(WorldObjectBindingStatus.Unbound, firstBinding.Status);
            Assert.AreEqual(WorldObjectBindingStatus.Bound, secondBinding.Status);
            Assert.AreEqual(20, second.Value);
            Assert.AreEqual(2, store.States.Count);

            var revisitedScene = CreateScene();
            var revisited = AddWorldObject(revisitedScene, "first", 99);
            firstBinding.Bind(Objects(revisited));

            Assert.AreEqual(10, revisited.Value);
            Assert.AreEqual(20, second.Value);
            firstBinding.Unbind();
            secondBinding.Unbind();
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 서로 다른 World Store의 같은 ID는 다른 상태를 유지한다.
        /// </summary>
        // ------------------------------------------------------------
        [UnityTest]
        public IEnumerator TEST_WorldObjectSceneBindingScene_별도_World는_같은_ID를_사용()
        {
            var firstScene = CreateScene();
            var secondScene = CreateScene();
            var first = AddWorldObject(firstScene, "door", 3);
            var second = AddWorldObject(secondScene, "door", 7);
            var firstStore = new WorldObjectStateStore();
            var secondStore = new WorldObjectStateStore();
            var firstBinding = new WorldObjectSceneBinding(firstStore);
            var secondBinding = new WorldObjectSceneBinding(secondStore);

            firstBinding.Bind(Objects(first), obj => ((TestSceneWorldObject)obj).CreateState());
            secondBinding.Bind(Objects(second), obj => ((TestSceneWorldObject)obj).CreateState());

            first.Change(30);
            second.Change(70);

            Assert.AreEqual(30, first.Value);
            Assert.AreEqual(70, second.Value);
            firstBinding.Unbind();
            secondBinding.Unbind();

            yield return SceneManager.UnloadSceneAsync(firstScene);

            Assert.IsTrue(firstStore.TryGetState("door", out var firstState));
            Assert.IsTrue(secondStore.TryGetState("door", out var secondState));
            Assert.AreNotSame(firstState, secondState);
            Assert.AreEqual(30, ((TestSceneWorldState)firstState).Value);
            Assert.AreEqual(70, ((TestSceneWorldState)secondState).Value);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 같은 ID라도 서로 다른 논리 Scope와 Scene에서 상태가 격리된다.
        /// </summary>
        // ------------------------------------------------------------
        [UnityTest]
        public IEnumerator TEST_WorldObjectSceneBindingScene_다중_Scope_동시_Scene_격리()
        {
            var firstScene = CreateScene();
            var secondScene = CreateScene();
            var first = AddWorldObject(firstScene, "gate", 1);
            var second = AddWorldObject(secondScene, "gate", 1);

            var firstStore = new WorldObjectStateStore
            (
                new WorldObjectState[]
                {
                    new TestSceneWorldState("gate", 3)
                }
            );
            var secondStore = new WorldObjectStateStore
            (
                new WorldObjectState[]
                {
                    new TestSceneWorldState("gate", 9)
                }
            );
            var firstBinding = new WorldObjectSceneBinding(firstStore);
            var secondBinding = new WorldObjectSceneBinding(secondStore);

            firstBinding.Bind(Objects(first));
            secondBinding.Bind(Objects(second));

            Assert.AreEqual(3, first.Value);
            Assert.AreEqual(9, second.Value);

            first.Change(24);
            Assert.AreEqual(24, first.Value);
            Assert.AreEqual(9, second.Value);

            secondBinding.Unbind();
            yield return SceneManager.UnloadSceneAsync(secondScene);

            Assert.IsTrue(first.IsBound);
            Assert.AreEqual(24, first.Value);
            Assert.IsTrue(firstStore.TryGetState("gate", out var stored));
            Assert.AreEqual(24, ((TestSceneWorldState)stored).Value);

            firstBinding.Unbind();
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 외부 현재 상태 적용은 Live 상호작용을 재실행하지 않는다.
        /// </summary>
        // ------------------------------------------------------------
        [UnityTest]
        public IEnumerator TEST_WorldObjectSceneBindingScene_현재_상태_적용과_재방문()
        {
            var scene = CreateScene();
            var target = AddWorldObject(scene, "gate", 99);
            var initial = new TestSceneWorldState("gate", 6);
            var store = new WorldObjectStateStore
            (
                new WorldObjectState[]
                {
                    initial
                }
            );
            var binding = new WorldObjectSceneBinding(store);

            binding.Bind(Objects(target));
            var current = new TestSceneWorldState("gate", 18);
            binding.ApplyCurrentState(current);

            Assert.AreEqual(18, target.Value);
            Assert.AreEqual(0, target.LiveChangeCount);
            Assert.AreEqual(1, target.CurrentApplyCount);
            Assert.IsTrue(store.TryGetState("gate", out var stored));
            Assert.AreSame(current, stored);

            binding.Unbind();
            yield return SceneManager.UnloadSceneAsync(scene);

            var nextScene = CreateScene();
            var reloaded = AddWorldObject(nextScene, "gate", 0);
            binding.Bind(Objects(reloaded));

            Assert.AreEqual(18, reloaded.Value);
            Assert.AreEqual(0, reloaded.CurrentApplyCount);
            binding.Unbind();
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> State 적용 도중 Scene 값만 바뀌면 Store는 이전 State를 보존한다.
        /// <br/> 연결을 해제하고 새 Scene에서 지속 State로 복구한다.
        /// </summary>
        // ----------------------------------------------------------------------
        [UnityTest]
        public IEnumerator TEST_WorldObjectSceneBindingScene_부분적용실패후_새_Scene에서_재구성()
        {
            var firstScene = CreateScene();
            var first = AddWorldObject(firstScene, "gate", 99);
            var original = new TestSceneWorldState("gate", 7);
            var initialStates = new WorldObjectState[]
            {
                original
            };
            var store = new WorldObjectStateStore(initialStates);
            var binding = new WorldObjectSceneBinding(store);

            binding.Bind(Objects(first));
            Assert.AreEqual(7, first.Value);

            first.FailCurrentApply = true;
            var uncommitted = new TestSceneWorldState("gate", 41);
            Assert.Throws<InvalidOperationException>(() => binding.ApplyCurrentState(uncommitted));

            Assert.AreEqual(WorldObjectBindingStatus.CleanupPending, binding.Status);
            Assert.AreEqual(41, first.Value);
            Assert.AreEqual(0, first.LiveChangeCount);
            Assert.AreEqual(1, first.CurrentApplyCount);
            Assert.IsTrue(store.TryGetState("gate", out var saved));
            Assert.AreSame(original, saved);
            Assert.AreNotSame(uncommitted, saved);

            binding.Unbind();
            Assert.AreEqual(WorldObjectBindingStatus.Unbound, binding.Status);
            Assert.IsFalse(first.IsBound);
            yield return SceneManager.UnloadSceneAsync(firstScene);

            var secondScene = CreateScene();
            var second = AddWorldObject(secondScene, "gate", 100);
            binding.Bind(Objects(second));

            Assert.AreEqual(WorldObjectBindingStatus.Bound, binding.Status);
            Assert.AreEqual(7, second.Value);
            Assert.AreEqual(0, second.LiveChangeCount);
            Assert.AreEqual(0, second.CurrentApplyCount);
            Assert.IsTrue(store.TryGetState("gate", out saved));
            Assert.AreSame(original, saved);
            binding.Unbind();
        }

    #endregion

    #region 정리

        // ------------------------------------------------------------
        /// <summary>
        /// 테스트가 생성한 모든 Unity Scene을 종료한다.
        /// </summary>
        // ------------------------------------------------------------
        [UnityTearDown]
        public IEnumerator TearDown()
        {
            foreach (var name in sceneNames)
            {
                var scene = SceneManager.GetSceneByName(name);

                if (scene.IsValid() && scene.isLoaded)
                {
                    yield return SceneManager.UnloadSceneAsync(scene);
                }
            }

            sceneNames.Clear();
        }

    #endregion

    #region Scene 생성

        private Scene CreateScene()
        {
            var name = "WorldStateTest_" + Guid.NewGuid().ToString("N");
            var scene = SceneManager.CreateScene(name);

            sceneNames.Add(name);
            return scene;
        }

        private static TestSceneWorldObject AddWorldObject(Scene scene, string id, int initialValue)
        {
            var gameObject = new GameObject("WorldObject");
            var obj = gameObject.AddComponent<TestSceneWorldObject>();

            obj.Configure(id, initialValue);
            SceneManager.MoveGameObjectToScene(gameObject, scene);

            return obj;
        }

        private static IWorldObject[] Objects(TestSceneWorldObject obj)
        {
            return new IWorldObject[]
            {
                obj
            };
        }

    #endregion

    }
}
