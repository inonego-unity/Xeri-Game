/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : TEST_BoardViewBase.cs
수정일 : 2026-11-12

# 설명
BoardViewBase 시스템의 핵심 기능 테스트. Play Mode.
Bind/Unbind, OnAddSpace/OnRemoveSpace 이벤트, ReloadTileMap 등을 검증한다.

# 테스트 구성
 E: 기본 기능 (Bind/이벤트/ReloadTileMap/Unbind 통합 흐름)
========================================================================= BLOCK_HEADER_END */

using System;
using System.Collections;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;

using NUnit;
using NUnit.Framework;

using inonego.Xeri;
using inonego.Xeri.Game;

using Object = UnityEngine.Object;

namespace inonego.Xeri.Game.TEST._Board
{

    // ============================================================
    /// <summary>
    /// BoardViewBase 시스템의 핵심 기능 테스트 클래스.
    /// </summary>
    // ============================================================
    public class TEST_BoardViewBase
    {

    #region 헬퍼

        // ------------------------------------------------------------
        /// <summary>
        /// 테스트용 기물입니다.
        /// </summary>
        // ------------------------------------------------------------
        public class TestPiece
        {
            public string Name;
            public TestPiece() {}
            public TestPiece(string name) { Name = name; }
            public override string ToString() => Name;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 테스트용 공간입니다.
        /// </summary>
        // ------------------------------------------------------------
        [Serializable]
        public class TestSpace : BoardSpace<TestPiece>
        {
            public TestSpace() {}
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 테스트용 타일입니다.
        /// </summary>
        // ------------------------------------------------------------
        public class TestMonoTile : MonoBehaviour { }

        // ------------------------------------------------------------
        /// <summary>
        /// 테스트용 BoardViewBase 구현체입니다.
        /// </summary>
        // ------------------------------------------------------------
        public class TestBoardView2D : BoardViewBase<Board2D<int, TestSpace, TestPiece>, Vector2Int, int, TestSpace, TestPiece, TestMonoTile>
        {
            [SerializeField]
            private float lTileSize = 1f;

            public override Vector3 ToLocalPos(Vector2Int vector)
            {
                return new Vector3(vector.x * lTileSize, 0f, vector.y * lTileSize);
            }

            public override Vector3 ToLocalPos(Vector2Int vector, int index)
            {
                return ToLocalPos(vector) + Vector3.zero;
            }

            // ------------------------------------------------------------
            /// <summary>
            /// 타일 프로바이더를 설정합니다.
            /// </summary>
            // ------------------------------------------------------------
            public void SetTileProvider(IGameObjectProvider provider)
            {
                TileProvider = provider;
            }
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Space키 입력을 체크합니다.
        /// </summary>
        // ------------------------------------------------------------
        private bool IsSpaceKeyPressed()
        {
        #if ENABLE_INPUT_SYSTEM
            return Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame;
        #else
            return Input.GetKeyDown(KeyCode.Space);
        #endif
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 테스트용 타일 프로바이더를 생성합니다.
        /// </summary>
        // ------------------------------------------------------------
        private PrefabGameObjectProvider CreateTileProvider()
        {
            var prefab = GameObject.CreatePrimitive(PrimitiveType.Cube);
            prefab.AddComponent<TestMonoTile>();
            prefab.name = "TilePrefab";
            prefab.transform.localScale = Vector3.one * 0.9f;

            return new PrefabGameObjectProvider(prefab, null);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 카메라 오브젝트를 생성합니다.
        /// </summary>
        // ------------------------------------------------------------
        private GameObject CreateCameraObject()
        {
            var cameraObject = new GameObject("Camera");

            var camera = cameraObject.AddComponent<Camera>();
            camera.transform.position = new Vector3(0f, 5f, -10f);
            camera.transform.rotation = Quaternion.Euler(30f, 0f, 0f);

            return cameraObject;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 테스트용 BoardView2D를 생성합니다.
        /// </summary>
        // ------------------------------------------------------------
        private TestBoardView2D CreateBoardView()
        {
            var boardViewObject = new GameObject("TestBoardView2D");
            var boardView = boardViewObject.AddComponent<TestBoardView2D>();

            boardView.SetTileProvider(CreateTileProvider());

            return boardView;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Space 키 입력을 기다립니다.
        /// </summary>
        // ------------------------------------------------------------
        private IEnumerator WaitForSpaceKey(string message)
        {
            Debug.Log(message);

            while (!IsSpaceKeyPressed())
            {
                yield return null;
            }

            Debug.Log("테스트 완료!");
        }

    #endregion

    #region 픽스처

        private MonoForTEST monoForTEST;
        private GameObject cameraObject;

        // ------------------------------------------------------------
        /// <summary>
        /// 각 테스트 시작 전에 실행됩니다.
        /// </summary>
        // ------------------------------------------------------------
        [UnitySetUp]
        public IEnumerator SetUp()
        {
            monoForTEST  = new GameObject("MonoForTEST").AddComponent<MonoForTEST>();
            cameraObject = CreateCameraObject();

            yield return null;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 각 테스트 종료 후에 실행됩니다.
        /// </summary>
        // ------------------------------------------------------------
        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (monoForTEST != null)
            {
                Object.Destroy(monoForTEST.gameObject);
            }

            if (cameraObject != null)
            {
                Object.Destroy(cameraObject);
            }

            var allObjects = Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None);

            foreach (var obj in allObjects)
            {
                if (obj != null && obj.name != "Main Camera" && obj.name != "Directional Light")
                {
                    Object.Destroy(obj);
                }
            }

            yield return null;
        }

    #endregion

    #region E-1: Bind/이벤트/ReloadTileMap/Unbind 통합

        [Explicit]
        [Category("Manual")]
        [UnityTest]
        public IEnumerator TEST_BoardViewBase_Bind_이벤트_Reload_Unbind_통합()
        {
            // ------------------------------------------------------------
            // 테스트 준비 — 3x3 보드 (공간 자동 생성 비활성화)
            // ------------------------------------------------------------
            var board    = new Board2D<int, TestSpace, TestPiece>(3, 3, init: false);
            var boardView = CreateBoardView();

            var basePoints = new[]
            {
                new Vector2Int(0, 0),
                new Vector2Int(1, 0),
                new Vector2Int(0, 1),
                new Vector2Int(1, 1),
            };

            foreach (var point in basePoints)
            {
                board.AddSpace(point);
            }

            // ------------------------------------------------------------
            // Bind 전 초기 상태 확인
            // ------------------------------------------------------------
            Assert.That(boardView.Board,          Is.Null);
            Assert.That(boardView.TileMap.Count,  Is.EqualTo(0));

            boardView.Bind(board);

            yield return new WaitForSeconds(0.5f);

            Assert.That(boardView.Board,          Is.EqualTo(board));
            Assert.That(boardView.TileMap.Count,  Is.EqualTo(basePoints.Length));

            foreach (var point in basePoints)
            {
                var lTile       = boardView.TileMap[point];
                var expectedPos = boardView.ToLocalPos(point);

                Assert.That(boardView.TileMap.ContainsKey(point), Is.True);
                Assert.That(lTile,                                Is.Not.Null);
                Assert.That(Vector3.Distance(lTile.transform.localPosition, expectedPos), Is.LessThan(0.01f));
            }

            // ------------------------------------------------------------
            // OnAddSpace 이벤트 확인
            // ------------------------------------------------------------
            var newPointA = new Vector2Int(2, 0);
            board.AddSpace(newPointA);

            yield return null;

            Assert.That(boardView.TileMap.ContainsKey(newPointA), Is.True);

            var newPointB = new Vector2Int(2, 1);
            board.AddSpace(newPointB);

            yield return null;

            // ------------------------------------------------------------
            // OnRemoveSpace 이벤트 확인
            // ------------------------------------------------------------
            var removePoint = new Vector2Int(0, 0);
            board.RemoveSpace(removePoint);

            yield return null;

            Assert.That(boardView.TileMap.ContainsKey(removePoint), Is.False);

            // ------------------------------------------------------------
            // ReloadTileMap 확인
            // ------------------------------------------------------------
            boardView.ReloadTileMap();

            yield return null;

            int spaceCount = 0;

            foreach (var kvp in board)
            {
                var point       = kvp.Key;
                var expectedPos = boardView.ToLocalPos(point);
                var lTile       = boardView.TileMap[point];

                spaceCount++;

                Assert.That(boardView.TileMap.ContainsKey(point), Is.True);
                Assert.That(lTile,                                Is.Not.Null);
                Assert.That(Vector3.Distance(lTile.transform.localPosition, expectedPos), Is.LessThan(0.01f));
            }

            Assert.That(boardView.TileMap.Count, Is.EqualTo(spaceCount));

            // ------------------------------------------------------------
            // Unbind 확인
            // ------------------------------------------------------------
            boardView.Unbind();

            yield return null;

            Assert.That(boardView.Board,         Is.Null);
            Assert.That(boardView.TileMap.Count, Is.EqualTo(0));

            // ------------------------------------------------------------
            // 시각 확인 완료 — Space바로 종료
            // ------------------------------------------------------------
            yield return WaitForSpaceKey("초기화/이벤트/동기화/해제 통합 테스트 성공! Space바를 눌러서 종료하세요.");
        }

    #endregion

    }

}
