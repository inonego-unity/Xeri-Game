/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : TEST_GroundSuspension2D.cs
수정일 : 2026-11-12

# 설명
GroundSuspension2D가 별도 물리 질의 없이 GroundCheckSample2D를 지지 계산에 사용하는 계약을 검증한다.

# 테스트 구성
S: GroundChecker 표본 소비와 지지 승인 정책
========================================================================= BLOCK_HEADER_END */

using UnityEngine;

using NUnit.Framework;

using inonego.Xeri.Game.Controller;

namespace inonego.Xeri.Game.TEST.Controller._GroundSuspension
{
    // ============================================================
    /// <summary>
    /// GroundSuspension2D의 GroundChecker 표본 소비 계약 테스트.
    /// </summary>
    // ============================================================
    public sealed class TEST_GroundSuspension2D
    {

    #region 필드

        private GameObject actorObject = null;
        private GameObject groundObject = null;
        private Rigidbody2D actorRigid = null;
        private Rigidbody2D groundRigid = null;
        private CapsuleCollider2D capsule = null;
        private BoxCollider2D groundCollider = null;
        private GroundSuspension2D suspension = null;
        private GroundSuspension2DSettings settings = null;

    #endregion

    #region 준비 및 정리

        // ------------------------------------------------------------
        /// <summary>
        /// 지지 계산에 필요한 2D Capsule과 지면 참조를 준비한다.
        /// </summary>
        // ------------------------------------------------------------
        [SetUp]
        public void SetUp()
        {
            actorObject = new GameObject("GroundSuspension2DActor");
            actorRigid = actorObject.AddComponent<Rigidbody2D>();
            actorRigid.gravityScale = 2f;
            capsule = actorObject.AddComponent<CapsuleCollider2D>();
            capsule.direction = CapsuleDirection2D.Vertical;
            capsule.size = new Vector2(1f, 2f);

            groundObject = new GameObject("GroundSuspension2DGround");
            groundCollider = groundObject.AddComponent<BoxCollider2D>();
            groundRigid = groundObject.AddComponent<Rigidbody2D>();
            groundRigid.bodyType = RigidbodyType2D.Kinematic;

            settings = new GroundSuspension2DSettings
            {
                TargetHeight = 0.2f,
                MaximumDistance = 0.3f,
                MaximumSlopeAngle = 60f,
                Strength = 100f,
                Damping = 0f,
                MaxAcceleration = 1000f,
            };
            suspension = new GroundSuspension2D();
            suspension.Init(actorRigid, capsule, settings);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Suspension 참조를 해제하고 생성한 Physics2D 객체를 정리한다.
        /// </summary>
        // ------------------------------------------------------------
        [TearDown]
        public void TearDown()
        {
            suspension?.Release();

            Object.DestroyImmediate(groundObject);
            Object.DestroyImmediate(actorObject);
        }

    #endregion

    #region S-1: Cast 표본 소비

        // ------------------------------------------------------------
        /// <summary>
        /// Checker의 Cast 표면 정보가 2D 지지 표본과 가속도 계산에 사용되는지 검증한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_GroundSuspension2D_CheckerCast표본으로_지지계산()
        {
            var point = new Vector3(0f, -1f, 0f);
            var checkerSample = new GroundCheckSample2D
            (
                groundCollider,
                groundRigid,
                0.1f,
                point,
                Vector3.up
            );

            var sample = suspension.Sample(checkerSample);
            var expectedAcceleration = Mathf.Max
            (
                0f,
                Vector2.Dot(-Physics2D.gravity * actorRigid.gravityScale, Vector2.up)
            ) + 10f;

            Assert.IsTrue(sample.HasGround);
            Assert.AreEqual(checkerSample.Ground, sample.Ground);
            Assert.AreEqual(checkerSample.GroundRigid, sample.GroundRigid);
            Assert.AreEqual(0.1f, sample.Distance, 0.0001f);
            Assert.That(Vector3.Distance(point, sample.Point), Is.LessThan(0.0001f));
            Assert.That(Vector3.Distance(Vector3.up, sample.Normal), Is.LessThan(0.0001f));
            Assert.That(Vector3.Distance(Vector3.zero, sample.GroundVelocity), Is.LessThan(0.0001f));
            Assert.That(Vector3.Distance(Vector3.zero, sample.GroundAngularVelocity), Is.LessThan(0.0001f));
            Assert.AreEqual(expectedAcceleration, sample.Acceleration, 0.0001f);
        }

    #endregion

    #region S-2: 지지 경사 정책

        // ------------------------------------------------------------
        /// <summary>
        /// Checker가 감지했어도 허용 경사를 넘는 2D 표면은 지지하지 않는지 검증한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_GroundSuspension2D_최대경사초과_지지하지않음()
        {
            var checkerSample = new GroundCheckSample2D
            (
                groundCollider,
                groundRigid,
                0.1f,
                Vector3.zero,
                Vector3.right
            );

            var sample = suspension.Sample(checkerSample);

            Assert.IsTrue(checkerSample.HasGround);
            Assert.IsFalse(sample.HasGround);
        }

    #endregion

    #region S-3: 최대 지면 추종 거리

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/>지지 중인 2D 지면이 최대 추종 거리보다 멀어지면 지지를 끝내고
        /// <br/>원거리 표본을 접지 상태로 승인하지 않는지 검증한다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_GroundSuspension2D_최대거리초과_지면추종종료()
        {
            var nearSample = new GroundCheckSample2D
            (
                groundCollider,
                groundRigid,
                0.1f,
                Vector3.zero,
                Vector3.up
            );
            var farSample = new GroundCheckSample2D
            (
                groundCollider,
                groundRigid,
                0.4f,
                Vector3.zero,
                Vector3.up
            );

            Assert.IsTrue(suspension.Sample(nearSample).HasGround);
            Assert.IsFalse(suspension.Sample(farSample).HasGround);
        }

    #endregion

    }
}
