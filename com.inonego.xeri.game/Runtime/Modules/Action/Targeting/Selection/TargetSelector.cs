/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : TargetSelector.cs
수정일 : 2026-10-31

# 설명
외부 후보와 IFilter 정책을 이용해 Single, All, Top N 결과를 선택한다.
선택 대상의 도메인 의미와 결과 컬렉션 수명은 호출자가 소유한다.
========================================================================= BLOCK_HEADER_END */

using System;
using System.Collections;
using System.Collections.Generic;

using inonego;
using inonego.Xeri;

namespace inonego.Xeri.Game
{
    // ======================================================================
    /// <summary>
    /// <br/>후보를 한 번 열거해 지정된 방식으로 대상을 선택한다.
    /// <br/>기본 점수 비교는 오름차순이며 결과의 중복 제거는 수행하지 않는다.
    /// </summary>
    // ======================================================================
    public static class TargetSelector
    {

    #region 내부 점수 평가 어댑터

        // ============================================================
        /// <summary>
        /// 일반 점수 함수를 실패 없는 Scorer 계약으로 변환한다.
        /// </summary>
        // ============================================================
        private readonly struct DelegateScorer<TCandidate> : ITargetScorer<TCandidate>
        {
            private readonly Func<TCandidate, float> scorer;

            public DelegateScorer(Func<TCandidate, float> scorer)
            {
                this.scorer = scorer;
            }

            public bool TryScore(in TCandidate candidate, out float score)
            {
                score = scorer(candidate);
                return true;
            }
        }

        // ============================================================
        /// <summary>
        /// 평가 실패를 지원하는 외부 Scorer를 값 형식 경로에 연결한다.
        /// </summary>
        // ============================================================
        private readonly struct ReferenceScorer<TCandidate> : ITargetScorer<TCandidate>
        {
            private readonly ITargetScorer<TCandidate> scorer;

            public ReferenceScorer(ITargetScorer<TCandidate> scorer)
            {
                this.scorer = scorer;
            }

            public bool TryScore(in TCandidate candidate, out float score) =>
                scorer.TryScore(candidate, out score);
        }

    #endregion

    #region 순위 후보 데이터

        private readonly struct RankedCandidate<TCandidate>
        {
            public readonly TCandidate Candidate;
            public readonly float Score;

            public RankedCandidate(TCandidate candidate, float score)
            {
                Candidate = candidate;
                Score = score;
            }
        }

    #endregion

    #region 단일 선택

        // ------------------------------------------------------------
        /// <summary>
        /// <br/>점수 함수를 사용해 가장 우선인 후보 하나를 선택한다.
        /// <br/>후보가 없거나 모든 후보가 평가에서 제외되면 false를 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        public static bool TrySelect<TCandidate>
        (
            IEnumerable<TCandidate> candidates,
            Func<TCandidate, float> scorer,
            out TCandidate selected,
            IFilter<TCandidate> filter = null,
            IComparer<float> scoreComparer = null,
            IComparer<TCandidate> tieBreaker = null
        )
        {
            if (scorer == null)
            {
                throw new ArgumentNullException(nameof(scorer));
            }

            return TrySelectCore
            (
                candidates, new DelegateScorer<TCandidate>(scorer),
                out selected, filter, scoreComparer, tieBreaker
            );
        }

        // ------------------------------------------------------------
        /// <summary>
        /// <br/>평가 실패를 표현하는 Scorer로 최적 후보 하나를 선택한다.
        /// <br/>평가 실패와 NaN 점수의 후보는 건너뛴다.
        /// </summary>
        // ------------------------------------------------------------
        public static bool TrySelect<TCandidate>
        (
            IEnumerable<TCandidate> candidates,
            ITargetScorer<TCandidate> scorer,
            out TCandidate selected,
            IFilter<TCandidate> filter = null,
            IComparer<float> scoreComparer = null,
            IComparer<TCandidate> tieBreaker = null
        )
        {
            if (scorer == null)
            {
                throw new ArgumentNullException(nameof(scorer));
            }

            return TrySelectCore
            (
                candidates, new ReferenceScorer<TCandidate>(scorer),
                out selected, filter, scoreComparer, tieBreaker
            );
        }

    #endregion

    #region 전체 선택

        // ------------------------------------------------------------
        /// <summary>
        /// <br/>필터를 통과한 후보를 발견 순서대로 결과 목록에 추가한다.
        /// <br/>이미 추가된 결과는 예외가 발생해도 롤백하지 않는다.
        /// </summary>
        // ------------------------------------------------------------
        public static int SelectAll<TCandidate>
        (
            IEnumerable<TCandidate> candidates,
            IList<TCandidate> destination,
            IFilter<TCandidate> filter = null
        )
        {
            if (candidates == null)
            {
                throw new ArgumentNullException(nameof(candidates));
            }

            if (destination == null)
            {
                throw new ArgumentNullException(nameof(destination));
            }

            var count = 0;
            foreach (var candidate in candidates)
            {
                if (filter != null && !filter.IsMatch(candidate))
                {
                    continue;
                }

                destination.Add(candidate);
                count++;
            }

            return count;
        }

    #endregion

    #region 상위 N개 선택

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/>우선순위 상위 maxCount개 후보를 결과 목록에 순위순으로 추가한다.
        /// <br/>후보 평가 중 예외가 나면 결과 목록을 변경하지 않는다.
        /// </summary>
        // ----------------------------------------------------------------------
        public static int SelectTop<TCandidate>
        (
            IEnumerable<TCandidate> candidates,
            int maxCount,
            Func<TCandidate, float> scorer,
            IList<TCandidate> destination,
            IFilter<TCandidate> filter = null,
            IComparer<float> scoreComparer = null,
            IComparer<TCandidate> tieBreaker = null
        )
        {
            if (scorer == null)
            {
                throw new ArgumentNullException(nameof(scorer));
            }

            return SelectTopCore
            (
                candidates, maxCount, new DelegateScorer<TCandidate>(scorer),
                destination, filter, scoreComparer, tieBreaker
            );
        }

        // ------------------------------------------------------------
        /// <summary>
        /// <br/>평가 실패를 건너뛰고 우선순위 상위 maxCount개를 추가한다.
        /// <br/>결과를 방출하기 전의 계산 예외는 목록에 영향을 주지 않는다.
        /// </summary>
        // ------------------------------------------------------------
        public static int SelectTop<TCandidate>
        (
            IEnumerable<TCandidate> candidates,
            int maxCount,
            ITargetScorer<TCandidate> scorer,
            IList<TCandidate> destination,
            IFilter<TCandidate> filter = null,
            IComparer<float> scoreComparer = null,
            IComparer<TCandidate> tieBreaker = null
        )
        {
            if (scorer == null)
            {
                throw new ArgumentNullException(nameof(scorer));
            }

            return SelectTopCore
            (
                candidates, maxCount, new ReferenceScorer<TCandidate>(scorer),
                destination, filter, scoreComparer, tieBreaker
            );
        }

    #endregion

    #region 내부 선택 알고리즘

        // ------------------------------------------------------------
        /// <summary>
        /// <br/>후보를 한 번 열거하며 필터와 점수 평가를 적용한다.
        /// <br/>점수와 동점 비교에 따라 최적 후보 하나만 갱신한다.
        /// </summary>
        // ------------------------------------------------------------
        private static bool TrySelectCore<TCandidate, TScorer>
        (
            IEnumerable<TCandidate> candidates,
            TScorer scorer,
            out TCandidate selected,
            IFilter<TCandidate> filter,
            IComparer<float> scoreComparer,
            IComparer<TCandidate> tieBreaker
        )
        where TScorer : struct, ITargetScorer<TCandidate>
        {
            if (candidates == null)
            {
                throw new ArgumentNullException(nameof(candidates));
            }

            selected = default;
            var hasValue = false;
            var bestScore = default(float);
            scoreComparer ??= Comparer<float>.Default;

            foreach (var candidate in candidates)
            {
                if (filter != null && !filter.IsMatch(candidate))
                {
                    continue;
                }

                if (!scorer.TryScore(candidate, out var score) || float.IsNaN(score))
                {
                    continue;
                }

                if
                (
                    hasValue &&
                    Compare(candidate, score, selected, bestScore, scoreComparer, tieBreaker) >= 0
                )
                {
                    continue;
                }

                selected = candidate;
                bestScore = score;
                hasValue = true;
            }

            return hasValue;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// <br/>평가 가능한 후보 중 상위 maxCount개만 순위순으로 유지한다.
        /// <br/>전체 평가가 끝난 뒤 결과를 호출자 목록에 추가한다.
        /// </summary>
        // ------------------------------------------------------------
        private static int SelectTopCore<TCandidate, TScorer>
        (
            IEnumerable<TCandidate> candidates,
            int maxCount,
            TScorer scorer,
            IList<TCandidate> destination,
            IFilter<TCandidate> filter,
            IComparer<float> scoreComparer,
            IComparer<TCandidate> tieBreaker
        )
        where TScorer : struct, ITargetScorer<TCandidate>
        {
            if (candidates == null)
            {
                throw new ArgumentNullException(nameof(candidates));
            }

            if (destination == null)
            {
                throw new ArgumentNullException(nameof(destination));
            }

            if (maxCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxCount));
            }

            if (maxCount == 0)
            {
                return 0;
            }

            scoreComparer ??= Comparer<float>.Default;
            var ranked = new List<RankedCandidate<TCandidate>>();

            // 후보 수와 관계없이 상위 maxCount개만 유지한다.
            foreach (var candidate in candidates)
            {
                if (filter != null && !filter.IsMatch(candidate))
                {
                    continue;
                }

                if (!scorer.TryScore(candidate, out var score) || float.IsNaN(score))
                {
                    continue;
                }

                var index = 0;
                while (index < ranked.Count)
                {
                    var current = ranked[index];
                    var order = Compare
                    (
                        candidate, score, current.Candidate, current.Score,
                        scoreComparer, tieBreaker
                    );
                    if (order < 0)
                    {
                        break;
                    }

                    index++;
                }

                if (index >= maxCount)
                {
                    continue;
                }

                ranked.Insert(index, new RankedCandidate<TCandidate>(candidate, score));
                if (ranked.Count > maxCount)
                {
                    ranked.RemoveAt(ranked.Count - 1);
                }
            }

            // 계산이 모두 끝난 후 호출자가 소유하는 목록에 결과를 추가한다.
            foreach (var item in ranked)
            {
                destination.Add(item.Candidate);
            }

            return ranked.Count;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 점수를 먼저 비교하고 동점이면 선택적 후보 비교기를 적용한다.
        /// </summary>
        // ------------------------------------------------------------
        private static int Compare<TCandidate>
        (
            TCandidate left, float leftScore,
            TCandidate right, float rightScore,
            IComparer<float> scoreComparer,
            IComparer<TCandidate> tieBreaker
        )
        {
            var result = scoreComparer.Compare(leftScore, rightScore);
            if (result != 0 || tieBreaker == null)
            {
                return result;
            }

            return tieBreaker.Compare(left, right);
        }

    #endregion

    }
}
