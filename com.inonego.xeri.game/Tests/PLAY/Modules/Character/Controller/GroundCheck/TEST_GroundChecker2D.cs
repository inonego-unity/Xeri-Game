/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : TEST_GroundChecker2D.cs
수정일 : 2026-11-12

# 설명
GroundChecker2D의 지면 표본과 착지/이탈 이벤트 계약을 검증하는 Play Mode 테스트.
이벤트 검증은 실제 Rigidbody2D 물리 흐름을 유지하며 OnLeave를 직접 관찰하고,
점프 상승이 최고점에 도달할 때까지 단일 접지→이탈 전이를 검증한다.

# 테스트 구성
 S: GroundCheckSample 결과와 지면 후보 선택
 E: 콜라이더별 착지/이탈 이벤트 상태 전이
========================================================================= BLOCK_HEADER_END */

using System;
using System.Collections;

using UnityEngine;
using UnityEngine.TestTools;

using NUnit;
using NUnit.Framework;

using inonego;
using inonego.Xeri;
using inonego.Xeri.Game;
using inonego.Xeri.Game.Controller;

namespace inonego.Xeri.Game.TEST.Controller._GroundChecker
{

    // ============================================================
    /// <summary>
    /// GroundChecker2D Play Mode 테스트 클래스.
    /// </summary>
    // ============================================================
    public class TEST_GroundChecker2D
    {

    #region 이벤트 흐름 헬퍼

        private const int EVENT_TIMEOUT_FIXED_STEPS = 300;
        private const float SETTLED_VERTICAL_SPEED = 0.01f;
        private const float JUMP_IMPULSE = 15f;

        private GameObject CreateGroundObject(int layer)
        {
            var groundObject = new GameObject("Ground");
            groundObject.transform.position = new Vector3(0f, -4f, 0f);
            groundObject.transform.localScale = new Vector3(20f, 4f, 1f);
            groundObject.layer = layer;

            groundObject.AddComponent<BoxCollider2D>();

            var groundRigidbody = groundObject.AddComponent<Rigidbody2D>();
            groundRigidbody.bodyType = RigidbodyType2D.Kinematic;

            return groundObject;
        }

        private Vector3 GetPlayerPosition(int index)
        {
            const float startX = -6f;
            const float spacing = 4f;

            return new Vector3(startX + spacing * index, 1f, 0f);
        }

        private GameObject CreatePlayerObject(string name, int index)
        {
            var playerObject = new GameObject(name);
            playerObject.transform.position = GetPlayerPosition(index);

            var playerRigidbody = playerObject.AddComponent<Rigidbody2D>();
            playerRigidbody.bodyType = RigidbodyType2D.Kinematic;
            playerRigidbody.constraints = RigidbodyConstraints2D.FreezeRotation;

            return playerObject;
        }

        private void CheckAll(GroundChecker2D[] groundCheckers)
        {
            foreach (var groundChecker in groundCheckers)
            {
                groundChecker.Check(Time.fixedDeltaTime);
            }
        }

        private IEnumerator WaitForEveryPlayerToLandAndSettle
        (
            GroundChecker2D[] groundCheckers,
            GameObject[] players,
            int[] landEventCounts
        )
        {
            for (int fixedStep = 0; fixedStep < EVENT_TIMEOUT_FIXED_STEPS; fixedStep++)
            {
                yield return new WaitForFixedUpdate();

                CheckAll(groundCheckers);

                var allSettled = true;

                for (int i = 0; i < players.Length; i++)
                {
                    var rigidbody = players[i].GetComponent<Rigidbody2D>();

                    if
                    (
                        landEventCounts[i] < 1 ||
                        !groundCheckers[i].IsOnGround ||
                        Mathf.Abs(rigidbody.linearVelocity.y) > SETTLED_VERTICAL_SPEED
                    )
                    {
                        allSettled = false;
                        break;
                    }
                }

                if (allSettled)
                {
                    yield break;
                }
            }

            Assert.Fail("모든 대상이 OnLand 후 정지된 접지 상태에 도달하지 못했습니다. 제한: " + EVENT_TIMEOUT_FIXED_STEPS + " FixedUpdate");
        }

        private IEnumerator WaitForEveryPlayerToLeaveAndReachApex
        (
            GroundChecker2D[] groundCheckers,
            GameObject[] players,
            int[] leaveEventCounts
        )
        {
            for (int fixedStep = 0; fixedStep < EVENT_TIMEOUT_FIXED_STEPS; fixedStep++)
            {
                yield return new WaitForFixedUpdate();

                CheckAll(groundCheckers);

                var allReachedApex = true;

                for (int i = 0; i < players.Length; i++)
                {
                    var rigidbody = players[i].GetComponent<Rigidbody2D>();

                    if (leaveEventCounts[i] < 1 || rigidbody.linearVelocity.y > 0f)
                    {
                        allReachedApex = false;
                        break;
                    }
                }

                if (allReachedApex)
                {
                    yield break;
                }
            }

            Assert.Fail("모든 대상이 OnLeave 후 최고점에 도달하지 못했습니다. 제한: " + EVENT_TIMEOUT_FIXED_STEPS + " FixedUpdate");
        }

    #endregion

    #region Sample 픽스처

        // ----------------------------------------------------------------------
        /// <summary>
        /// GroundCheckSample 검증에 사용할 2D 지면과 원형 검사 오브젝트를 생성합니다.
        /// </summary>
        // ----------------------------------------------------------------------
        private (GameObject Ground, BoxCollider2D GroundCollider, Rigidbody2D GroundRigid,
            GameObject Player, GroundChecker2D Checker) CreateSampleFixture(float playerY)
        {
            const int groundLayer = 8;

            var ground = new GameObject("SampleGround");
            ground.layer = groundLayer;
            ground.transform.position = new Vector3(0f, -0.5f, 0f);

            var groundCollider = ground.AddComponent<BoxCollider2D>();
            groundCollider.size = new Vector2(4f, 1f);

            var groundRigid = ground.AddComponent<Rigidbody2D>();
            groundRigid.bodyType = RigidbodyType2D.Kinematic;

            var player = new GameObject("SamplePlayer");
            player.transform.position = new Vector3(0f, playerY, 0f);

            var playerRigid = player.AddComponent<Rigidbody2D>();
            playerRigid.bodyType = RigidbodyType2D.Kinematic;

            var playerCollider = player.AddComponent<CircleCollider2D>();
            playerCollider.radius = 0.5f;

            var checker = new GroundChecker2D
            {
                Config = new GroundCheckerConfig
                {
                    Layer = 1 << groundLayer,
                    Depth = 0.25f,
                },
            };
            checker.Init(player);

            return (ground, groundCollider, groundRigid, player, checker);
        }

    #endregion

    #region S-1: GroundCheckSample 결과

        [Test]
        public void TEST_GroundChecker2D_Cast결과를_Sample에_기록하고_갱신한다()
        {
            var fixture = CreateSampleFixture(0.6f);
            var landCount = 0;
            var leaveCount = 0;

            fixture.Checker.OnLand += (_, _) => landCount++;
            fixture.Checker.OnLeave += (_, _) => leaveCount++;

            try
            {
                Physics2D.SyncTransforms();
                fixture.Checker.Check(Time.fixedDeltaTime);

                var firstSample = fixture.Checker.Sample;

                Assert.That(firstSample.HasGround, Is.True);
                Assert.That(firstSample.Ground, Is.SameAs(fixture.Ground));
                Assert.That(firstSample.GroundCollider, Is.SameAs(fixture.GroundCollider));
                Assert.That(firstSample.GroundRigid, Is.SameAs(fixture.GroundRigid));
                Assert.That(firstSample.Distance, Is.EqualTo(0.1f).Within(0.02f));
                Assert.That(firstSample.Point.y, Is.EqualTo(0f).Within(0.02f));
                Assert.That(firstSample.Normal.y, Is.GreaterThan(0.9f));
                Assert.That(landCount, Is.EqualTo(1));
                Assert.That(leaveCount, Is.Zero);

                var firstDistance = firstSample.Distance;

                // 같은 지면을 유지해도 다음 Tick의 거리 정보가 이전 표본에 머물지 않아야 합니다.
                fixture.Player.transform.position = new Vector3(0f, 0.55f, 0f);
                Physics2D.SyncTransforms();
                fixture.Checker.Check(Time.fixedDeltaTime);

                var nextSample = fixture.Checker.Sample;
                Assert.That(nextSample.Distance, Is.LessThan(firstDistance));
                Assert.That(landCount, Is.EqualTo(1));
                Assert.That(leaveCount, Is.Zero);

                // 감지 범위를 벗어나면 이전 Ground 표본을 남기지 않아야 합니다.
                fixture.Player.transform.position = new Vector3(0f, 2f, 0f);
                Physics2D.SyncTransforms();
                fixture.Checker.Check(Time.fixedDeltaTime);

                var clearedSample = fixture.Checker.Sample;
                Assert.That(clearedSample.HasGround, Is.False);
                Assert.That(landCount, Is.EqualTo(1));
                Assert.That(leaveCount, Is.EqualTo(1));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(fixture.Player);
                UnityEngine.Object.DestroyImmediate(fixture.Ground);
            }
        }

        [Test]
        public void TEST_GroundChecker2D_시작중첩도_표면정보를_기록한다()
        {
            var previousQueriesStartInColliders = Physics2D.queriesStartInColliders;
            var fixture = CreateSampleFixture(0.45f);

            try
            {
                Physics2D.queriesStartInColliders = true;
                Physics2D.SyncTransforms();
                fixture.Checker.Check(Time.fixedDeltaTime);

                var sample = fixture.Checker.Sample;

                Assert.That(sample.HasGround, Is.True);
                Assert.That(sample.GroundCollider, Is.SameAs(fixture.GroundCollider));
                Assert.That(sample.GroundRigid, Is.SameAs(fixture.GroundRigid));
                Assert.That(sample.Distance, Is.LessThanOrEqualTo(0f));
                Assert.That(sample.Point.y, Is.EqualTo(0f).Within(0.02f));
                Assert.That(sample.Normal.y, Is.GreaterThan(0.9f));
            }
            finally
            {
                Physics2D.queriesStartInColliders = previousQueriesStartInColliders;
                UnityEngine.Object.DestroyImmediate(fixture.Player);
                UnityEngine.Object.DestroyImmediate(fixture.Ground);
            }
        }

        [Test]
        public void TEST_GroundChecker2D_옆벽_시작중첩이_아래바닥을_가리지않는다()
        {
            var previousQueriesStartInColliders = Physics2D.queriesStartInColliders;
            var fixture = CreateSampleFixture(0.6f);
            var wall = new GameObject("SampleWall");
            wall.layer = fixture.Ground.layer;
            wall.transform.position = new Vector3(0.74f, 0.6f, 0f);

            var wallCollider = wall.AddComponent<BoxCollider2D>();
            wallCollider.size = new Vector2(0.5f, 4f);

            try
            {
                Physics2D.queriesStartInColliders = true;
                Physics2D.SyncTransforms();
                fixture.Checker.Check(Time.fixedDeltaTime);

                var sample = fixture.Checker.Sample;

                Assert.That(sample.HasGround, Is.True);
                Assert.That(sample.Ground, Is.SameAs(fixture.Ground));
                Assert.That(sample.GroundCollider, Is.SameAs(fixture.GroundCollider));
                Assert.That(sample.Normal.y, Is.GreaterThan(0.9f));
            }
            finally
            {
                Physics2D.queriesStartInColliders = previousQueriesStartInColliders;
                UnityEngine.Object.DestroyImmediate(wall);
                UnityEngine.Object.DestroyImmediate(fixture.Player);
                UnityEngine.Object.DestroyImmediate(fixture.Ground);
            }
        }

    #endregion

    #region E-1: 콜라이더별 착지와 이탈 이벤트

        [UnityTest]
        public IEnumerator TEST_GroundChecker2D_4종_콜라이더_착지_이탈_이벤트()
        {
            const int groundLayer = 1;

            var previousIgnore = Physics2D.GetIgnoreLayerCollision(0, groundLayer);
            GameObject groundObject = null;
            GameObject[] players = null;

            Physics2D.IgnoreLayerCollision(0, groundLayer, false);

            try
            {
                groundObject = CreateGroundObject(groundLayer);

                var boxPlayer = CreatePlayerObject("BoxPlayer", 0);
                var circlePlayer = CreatePlayerObject("CirclePlayer", 1);
                var verticalCapsulePlayer = CreatePlayerObject("VerticalCapsulePlayer", 2);
                var horizontalCapsulePlayer = CreatePlayerObject("HorizontalCapsulePlayer", 3);

                players = new[]
                {
                    boxPlayer,
                    circlePlayer,
                    verticalCapsulePlayer,
                    horizontalCapsulePlayer,
                };

                boxPlayer.AddComponent<BoxCollider2D>().size = Vector2.one;
                circlePlayer.AddComponent<CircleCollider2D>().radius = 0.5f;

                verticalCapsulePlayer.transform.localScale = new Vector3(1f, 2f, 1f);
                var verticalCapsuleCollider = verticalCapsulePlayer.AddComponent<CapsuleCollider2D>();
                verticalCapsuleCollider.direction = CapsuleDirection2D.Vertical;
                verticalCapsuleCollider.size = new Vector2(1f, 2f);

                horizontalCapsulePlayer.transform.localScale = new Vector3(2f, 1f, 1f);
                var horizontalCapsuleCollider = horizontalCapsulePlayer.AddComponent<CapsuleCollider2D>();
                horizontalCapsuleCollider.direction = CapsuleDirection2D.Horizontal;
                horizontalCapsuleCollider.size = new Vector2(2f, 1f);

                var groundCheckers = new GroundChecker2D[players.Length];
                var landEventCounts = new int[players.Length];
                var leaveEventCounts = new int[players.Length];

                for (int i = 0; i < players.Length; i++)
                {
                    var index = i;
                    var groundChecker = new GroundChecker2D
                    {
                        Config = new GroundCheckerConfig
                        {
                            Layer = 1 << groundLayer,
                            Depth = 0.1f,
                        },
                    };
                    groundChecker.Init(players[i]);

                    var gizmoDrawer = players[i].AddComponent<GroundChecker2DGizmoDrawer>();
                    gizmoDrawer.Init(groundChecker);

                    groundChecker.OnLand += (_, _) => landEventCounts[index]++;
                    groundChecker.OnLeave += (_, _) => leaveEventCounts[index]++;

                    groundCheckers[i] = groundChecker;
                }

                Physics2D.SyncTransforms();

                foreach (var player in players)
                {
                    player.GetComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Dynamic;
                }

                yield return WaitForEveryPlayerToLandAndSettle(groundCheckers, players, landEventCounts);

                for (int i = 0; i < players.Length; i++)
                {
                    var rigidbody = players[i].GetComponent<Rigidbody2D>();

                    Assert.That(landEventCounts[i], Is.EqualTo(1), players[i].name + " OnLand 호출 횟수");
                    Assert.That(leaveEventCounts[i], Is.Zero, players[i].name + " 착지 중 OnLeave 호출 횟수");
                    Assert.That(groundCheckers[i].IsOnGround, Is.True, players[i].name + " 착지 상태");
                    Assert.That(Mathf.Abs(rigidbody.linearVelocity.y), Is.LessThanOrEqualTo(SETTLED_VERTICAL_SPEED), players[i].name + " 착지 안정화 속도");
                }

                foreach (var player in players)
                {
                    player.GetComponent<Rigidbody2D>().AddForce(Vector2.up * JUMP_IMPULSE, ForceMode2D.Impulse);
                }

                yield return WaitForEveryPlayerToLeaveAndReachApex(groundCheckers, players, leaveEventCounts);

                for (int i = 0; i < players.Length; i++)
                {
                    var rigidbody = players[i].GetComponent<Rigidbody2D>();

                    Assert.That(landEventCounts[i], Is.EqualTo(1), players[i].name + " 상승 중 OnLand 호출 횟수");
                    Assert.That(leaveEventCounts[i], Is.EqualTo(1), players[i].name + " OnLeave 호출 횟수");
                    Assert.That(groundCheckers[i].IsOnGround, Is.False, players[i].name + " 최고점의 이탈 상태");
                    Assert.That(rigidbody.linearVelocity.y, Is.LessThanOrEqualTo(0f), players[i].name + " 최고점 도달 여부");
                }
            }
            finally
            {
                if (players != null)
                {
                    foreach (var player in players)
                    {
                        UnityEngine.Object.DestroyImmediate(player);
                    }
                }

                if (groundObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(groundObject);
                }

                Physics2D.IgnoreLayerCollision(0, groundLayer, previousIgnore);
            }
        }

    #endregion

    }

}
