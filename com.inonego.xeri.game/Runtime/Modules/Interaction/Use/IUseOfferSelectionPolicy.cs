/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : IUseOfferSelectionPolicy.cs
수정일 : 2026-11-12

# 설명
UseController가 후보의 공간·상황 의존 선택 점수를 외부 정책에 위임하기 위한 최소 계약.

# 제약사항
후보 발견 방식과 Camera·Player 같은 프로젝트 Context는 계약에 포함하지 않는다.
========================================================================= BLOCK_HEADER_END */

namespace inonego.Xeri.Game
{
    // ============================================================
    /// <summary>
    /// UseOffer 하나의 선택 가능 여부와 낮을수록 우선인 점수를 제공한다.
    /// </summary>
    // ============================================================
    public interface IUseOfferSelectionPolicy
    {
    #region 메서드

        // ----------------------------------------------------------------------
        /// <summary>
        /// 후보를 선택할 수 있으면 현재 Context에서의 점수를 반환한다.
        /// </summary>
        // ----------------------------------------------------------------------
        public bool TryScore
        (
            UseOffer offer,
            bool isCurrentOffer,
            out float score
        );

    #endregion
    }
}
