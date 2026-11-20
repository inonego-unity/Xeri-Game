/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : TEST_ResourceFixture.cs
수정일 : 2026-11-19

# 설명
Owner별 자원 소비와 복원 픽스처를 검증한다.

# 테스트 구성
 R: Owner별 소비·복원과 입력 검증
========================================================================= BLOCK_HEADER_END */

using System;

using NUnit;
using NUnit.Framework;

namespace inonego.Xeri.Game.TEST._Ability.Fixture
{
    // ============================================================
    /// <summary>
    /// 자원 픽스처의 독립성과 원자성을 검증한다.
    /// </summary>
    // ============================================================
    public sealed class TEST_ResourceFixture
    {

    #region R-1: Owner별 소비·복원과 입력 검증

        // ------------------------------------------------------------
        /// <summary>
        /// 각 Owner의 소비와 복원은 다른 잔액에 영향을 주지 않는다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ResourceFixture_Owner별독립소비와복원()
        {
            var resource = new ResourceFixture();
            var first = new object();
            var second = new object();
            resource.Add(first, 10);
            resource.Add(second, 4);

            Assert.IsTrue(resource.TryDebit(first, 3));
            Assert.IsFalse(resource.TryDebit(second, 5));
            Assert.AreEqual(7, resource.Get(first));
            Assert.AreEqual(4, resource.Get(second));
            resource.Credit(first, 3);
            Assert.AreEqual(10, resource.Get(first));
            Assert.AreEqual(4, resource.Get(second));
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 음수와 오버플로 입력은 기존 잔액을 보존한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ResourceFixture_잘못된입력은잔액을보존()
        {
            var resource = new ResourceFixture();
            var owner = new object();
            resource.Add(owner, int.MaxValue);
            Assert.Throws<ArgumentOutOfRangeException>(() => resource.TryDebit(owner, -1));
            Assert.Throws<OverflowException>(() => resource.Credit(owner, 1));
            Assert.AreEqual(int.MaxValue, resource.Get(owner));
        }

    #endregion

    }
}
