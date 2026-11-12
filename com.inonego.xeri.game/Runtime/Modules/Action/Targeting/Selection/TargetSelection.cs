/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : TargetSelection.cs
수정일 : 2026-10-31

# 설명
직접 순회 중 단일 최적 후보를 누적하는 선택 상태.
후보의 유효성·점수 계산과 선택 상태의 수명은 호출자가 소유한다.
========================================================================= BLOCK_HEADER_END */

using System;
using System.Collections;
using System.Collections.Generic;

namespace inonego.Xeri.Game
{
    // ============================================================
    /// <summary>
    /// <br/>점수가 계산된 후보에서 최적 후보 하나를 누적한다.
    /// <br/>비교 및 동점 규칙은 호출자가 제공하는 값 형식 정책을 따른다.
    /// </summary>
    // ============================================================
    public struct TargetSelection<TCandidate, TScoreComparer, TTieBreaker>
    where TScoreComparer : struct, IComparer<float>
    where TTieBreaker : struct, IComparer<TCandidate>
    {

    #region 선택 상태

        // ------------------------------------------------------------
        /// <summary>
        /// 선택된 후보가 있는지 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        public bool HasValue => hasValue;
        private bool hasValue;

        // ------------------------------------------------------------
        /// <summary>
        /// HasValue가 true일 때 선택된 후보를 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        public TCandidate Selected => selected;
        private TCandidate selected;

        // ------------------------------------------------------------
        /// <summary>
        /// 선택된 후보의 점수를 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        public float Score => score;
        private float score;

        private TScoreComparer scoreComparer;
        private TTieBreaker tieBreaker;

    #endregion

    #region 생성자

        // ------------------------------------------------------------
        /// <summary>
        /// 비교·동점 정책을 받아 빈 선택 상태를 생성한다.
        /// </summary>
        // ------------------------------------------------------------
        public TargetSelection(TScoreComparer scoreComparer, TTieBreaker tieBreaker)
        {
            hasValue = false;
            selected = default;
            score = float.PositiveInfinity;
            this.scoreComparer = scoreComparer;
            this.tieBreaker = tieBreaker;
        }

    #endregion

    #region 후보 누적

        // ------------------------------------------------------------
        /// <summary>
        /// <br/>유효 점수의 후보가 기존 선택보다 우선하면 교체한다.
        /// <br/>NaN은 거부하며 동점 비교가 0 이상이면 기존 선택을 유지한다.
        /// </summary>
        // ------------------------------------------------------------
        public bool TryConsider(in TCandidate candidate, float candidateScore)
        {
            if (float.IsNaN(candidateScore))
            {
                return false;
            }

            if (!hasValue)
            {
                selected = candidate;
                score = candidateScore;
                hasValue = true;
                return true;
            }

            var scoreOrder = scoreComparer.Compare(candidateScore, score);
            if (scoreOrder > 0)
            {
                return false;
            }

            if (scoreOrder == 0 && tieBreaker.Compare(candidate, selected) >= 0)
            {
                return false;
            }

            selected = candidate;
            score = candidateScore;
            return true;
        }

    #endregion

    }
}
