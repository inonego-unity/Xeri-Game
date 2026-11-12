/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : SpatialTargetRequirement.cs
수정일 : 2026-10-31

# 설명
게임 타겟 입력의 Direction·Position·Reference 존재 조건만 평가한다.
Spatial 범용 해석 모듈과는 별개의 Targeting 입력 검증 계약이다.
========================================================================= BLOCK_HEADER_END */

using System;

using UnityEngine;

namespace inonego.Xeri.Game
{
    // ============================================================
    /// <summary>
    /// 게임 타겟 입력에 포함된 공간 필드의 집합.
    /// </summary>
    // ============================================================
    [Flags]
    public enum SpatialTargetField
    {
        None = 0,
        Direction = 1 << 0,
        Position = 1 << 1,
        Reference = 1 << 2,
    }

    // ============================================================
    /// <summary>
    /// 게임 타겟 입력의 필수 필드 조합을 판정한다.
    /// </summary>
    // ============================================================
    [Serializable]
    public struct SpatialTargetRequirement
    {

    #region 필드

        public SpatialTargetField RequiredAll => requiredAll;

        [SerializeField]
        private SpatialTargetField requiredAll;

        public SpatialTargetField RequiredAny => requiredAny;

        [SerializeField]
        private SpatialTargetField requiredAny;

        public bool IsEmpty =>
            requiredAll == SpatialTargetField.None &&
            requiredAny == SpatialTargetField.None;

    #endregion

    #region 생성자

        public SpatialTargetRequirement(SpatialTargetField requiredField)
            : this(requiredField, SpatialTargetField.None)
        {
            // NONE
        }

        public SpatialTargetRequirement
        (
            SpatialTargetField requiredAll,
            SpatialTargetField requiredAny
        )
        {
            this.requiredAll = Mask(requiredAll);
            this.requiredAny = Mask(requiredAny);
        }

    #endregion

    #region 검증

        // ------------------------------------------------------------
        /// <summary>
        /// 전체 필수 조건과 하나 이상 조건을 각각 검증한다.
        /// </summary>
        // ------------------------------------------------------------
        public bool IsSatisfiedBy(SpatialTargetField fields) =>
            (fields & requiredAll) == requiredAll &&
            (requiredAny == SpatialTargetField.None ||
             (fields & requiredAny) != SpatialTargetField.None);

        private static SpatialTargetField Mask(SpatialTargetField fields) =>
            fields &
            (SpatialTargetField.Direction |
             SpatialTargetField.Position |
             SpatialTargetField.Reference);

    #endregion

    }
}
