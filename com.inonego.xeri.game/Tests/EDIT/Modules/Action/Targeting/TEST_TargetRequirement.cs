/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : TEST_TargetRequirement.cs
수정일 : 2026-11-12

# 설명
게임 타겟 입력의 필수 필드 All/Any 의미를 검증한다.
========================================================================= BLOCK_HEADER_END */

using NUnit;
using NUnit.Framework;

using inonego;
using inonego.Xeri;
using inonego.Xeri.Game;

namespace inonego.Xeri.Game.TEST._Targeting
{
    // ============================================================
    /// <summary>
    /// 게임 타겟 입력 필드의 조합 판정 계약을 검증한다.
    /// </summary>
    // ============================================================
    public sealed class TEST_TargetRequirement
    {

    #region 요구조건

        [Test]
        public void RequiredAll과RequiredAny를독립적으로판정함()
        {
            var requirement = new SpatialTargetRequirement
            (
                SpatialTargetField.Reference,
                SpatialTargetField.Direction | SpatialTargetField.Position
            );

            Assert.IsTrue
            (
                requirement.IsSatisfiedBy
                (
                    SpatialTargetField.Reference | SpatialTargetField.Position
                )
            );
            Assert.IsFalse(requirement.IsSatisfiedBy(SpatialTargetField.Reference));
            Assert.IsFalse
            (
                requirement.IsSatisfiedBy
                (
                    SpatialTargetField.Direction | SpatialTargetField.Position
                )
            );
        }

    #endregion

    }
}
