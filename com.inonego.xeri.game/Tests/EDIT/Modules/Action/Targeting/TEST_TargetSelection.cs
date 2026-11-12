/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : TEST_TargetSelection.cs
수정일 : 2026-11-12

# 설명
공통 IFilter 기반 Single / All / Top N 선택과 직접 누적 선택의 외부 계약을 검증한다.

# 테스트 구성
 필터 논리 조합 / 단일 선택 / 전체 선택 / 상위 N개 선택 / 실패와 예외
========================================================================= BLOCK_HEADER_END */

using System;
using System.Collections;
using System.Collections.Generic;

using NUnit;
using NUnit.Framework;

namespace inonego.Xeri.Game.TEST._Targeting
{
    using Xeri;
    using Xeri.Game;

    // ======================================================================
    /// <summary>
    /// <br/>IFilter를 사용하는 Single / All / Top N 결과 계약을 검증한다.
    /// <br/>직접 누적 선택과 실패·예외 경계도 함께 확인한다.
    /// </summary>
    // ======================================================================
    public sealed class TEST_TargetSelection
    {

    #region 헬퍼

        private readonly struct Candidate
        {
            public int ID { get; }
            public float Score { get; }
            public bool Enabled { get; }

            public Candidate(int id, float score, bool enabled = true)
            {
                ID = id;
                Score = score;
                Enabled = enabled;
            }
        }

        private sealed class CandidateScorer : ITargetScorer<Candidate>
        {
            public bool TryScore(in Candidate candidate, out float score)
            {
                score = candidate.Score;
                return candidate.Enabled;
            }
        }

        private sealed class CandidateTieBreaker : IComparer<Candidate>
        {
            public int Compare(Candidate x, Candidate y) => x.ID.CompareTo(y.ID);
        }

        private readonly struct ScoreComparer : IComparer<float>
        {
            public int Compare(float x, float y) => x.CompareTo(y);
        }

        private readonly struct TieComparer : IComparer<Candidate>
        {
            public int Compare(Candidate x, Candidate y) => x.ID.CompareTo(y.ID);
        }

        private sealed class ReverseScoreComparer : IComparer<float>
        {
            public int Compare(float x, float y) => y.CompareTo(x);
        }

        private static IEnumerable<Candidate> EnumerateOnce(Action onEnumerate)
        {
            onEnumerate();
            yield return new Candidate(1, 1f);
        }

    #endregion

    #region 단일 후보 선택

        [Test]
        public void TEST_TargetSelector_TrySelect_IFilter와동점비교기를적용()
        {
            var candidates = new[]
            {
                new Candidate(4, 1f),
                new Candidate(1, 1f),
                new Candidate(2, 0.5f, false),
                new Candidate(3, 2f)
            };
            var filter = new PredicateFilter<Candidate>(candidate => candidate.Enabled);

            var found = TargetSelector.TrySelect
            (
                candidates, candidate => candidate.Score, out var selected,
                filter: filter, tieBreaker: new CandidateTieBreaker()
            );

            Assert.IsTrue(found);
            Assert.AreEqual(1, selected.ID);
        }

        [Test]
        public void TEST_TargetSelector_TrySelect_거부된후보를채점하지않음()
        {
            var candidates = new[]
            {
                new Candidate(1, 0f, false),
                new Candidate(2, 3f)
            };
            var filter = new PredicateFilter<Candidate>(candidate => candidate.Enabled);
            var scores = 0;
            Func<Candidate, float> scorer = candidate =>
            {
                scores++;
                return candidate.Score;
            };

            var found = TargetSelector.TrySelect
            (
                candidates, scorer, out var selected, filter
            );

            Assert.IsTrue(found);
            Assert.AreEqual(2, selected.ID);
            Assert.AreEqual(1, scores);
        }

        [Test]
        public void TEST_TargetSelector_TrySelect_평가실패와NaN을건너뜀()
        {
            var candidates = new[]
            {
                new Candidate(1, 0f, false),
                new Candidate(2, float.NaN),
                new Candidate(3, float.NegativeInfinity)
            };

            var found = TargetSelector.TrySelect
            (
                candidates, new CandidateScorer(), out var selected
            );

            Assert.IsTrue(found);
            Assert.AreEqual(3, selected.ID);
        }

        [Test]
        public void TEST_TargetSelector_TrySelect_동점비교기미지정시최초후보유지()
        {
            var candidates = new[]
            {
                new Candidate(7, 1f),
                new Candidate(3, 1f)
            };

            var found = TargetSelector.TrySelect
            (
                candidates, candidate => candidate.Score, out var selected
            );

            Assert.IsTrue(found);
            Assert.AreEqual(7, selected.ID);
        }

        [Test]
        public void TEST_TargetSelection_직접순회로최적후보누적()
        {
            var selection = new TargetSelection<Candidate, ScoreComparer, TieComparer>
            (
                new ScoreComparer(), new TieComparer()
            );

            Assert.IsTrue(selection.TryConsider(new Candidate(5, 2f), 2f));
            Assert.IsFalse(selection.TryConsider(new Candidate(0, float.NaN), float.NaN));
            Assert.IsTrue(selection.TryConsider(new Candidate(4, 1f), 1f));
            Assert.IsTrue(selection.TryConsider(new Candidate(2, 1f), 1f));
            Assert.IsFalse(selection.TryConsider(new Candidate(3, 1f), 1f));

            Assert.IsTrue(selection.HasValue);
            Assert.AreEqual(2, selection.Selected.ID);
            Assert.AreEqual(1f, selection.Score);
        }

    #endregion

    #region 전체 후보 선택

        [Test]
        public void TEST_TargetSelector_SelectAll_입력순서중복과기존결과보존()
        {
            var first = new Candidate(1, 1f);
            var candidates = new[]
            {
                first, new Candidate(2, 0f, false), first, new Candidate(3, 3f)
            };
            var destination = new List<Candidate>
            {
                new Candidate(99, 0f)
            };
            var filter = new PredicateFilter<Candidate>(candidate => candidate.Enabled);

            var count = TargetSelector.SelectAll(candidates, destination, filter);

            Assert.AreEqual(3, count);
            CollectionAssert.AreEqual
            (
                new[]
                {
                    99, 1, 1, 3
                },
                destination.ConvertAll(candidate => candidate.ID)
            );
        }

        [Test]
        public void TEST_TargetSelector_SelectAll_필터없으면전체추가()
        {
            var candidates = new[]
            {
                new Candidate(1, 1f), new Candidate(2, 2f)
            };
            var destination = new List<Candidate>();

            var count = TargetSelector.SelectAll(candidates, destination);

            Assert.AreEqual(2, count);
            Assert.AreEqual(1, destination[0].ID);
            Assert.AreEqual(2, destination[1].ID);
        }

        [Test]
        public void TEST_TargetSelector_SelectAll_전체거부시기존결과유지()
        {
            var candidates = new[]
            {
                new Candidate(1, 1f)
            };
            var destination = new List<Candidate>
            {
                new Candidate(99, 0f)
            };
            var filter = new PredicateFilter<Candidate>(_ => false);

            var count = TargetSelector.SelectAll(candidates, destination, filter);

            Assert.AreEqual(0, count);
            Assert.AreEqual(1, destination.Count);
            Assert.AreEqual(99, destination[0].ID);
        }

    #endregion

    #region 상위 후보 선택

        [Test]
        public void TEST_TargetSelector_SelectTop_필터와점수및동점순서적용()
        {
            var candidates = new[]
            {
                new Candidate(5, 2f), new Candidate(7, 1f),
                new Candidate(4, 1f), new Candidate(0, 0f, false),
                new Candidate(2, 1f)
            };
            var destination = new List<Candidate>
            {
                new Candidate(99, 0f)
            };
            var filter = new PredicateFilter<Candidate>(candidate => candidate.Enabled);

            var count = TargetSelector.SelectTop
            (
                candidates, 3, candidate => candidate.Score, destination,
                filter, tieBreaker: new CandidateTieBreaker()
            );

            Assert.AreEqual(3, count);
            CollectionAssert.AreEqual
            (
                new[]
                {
                    99, 2, 4, 7
                },
                destination.ConvertAll(candidate => candidate.ID)
            );
        }

        [Test]
        public void TEST_TargetSelector_SelectTop_동점기본규칙은최초발견순서()
        {
            var candidates = new[]
            {
                new Candidate(7, 1f), new Candidate(2, 1f), new Candidate(4, 1f)
            };
            var destination = new List<Candidate>();

            var count = TargetSelector.SelectTop
            (
                candidates, 2, candidate => candidate.Score, destination
            );

            Assert.AreEqual(2, count);
            Assert.AreEqual(7, destination[0].ID);
            Assert.AreEqual(2, destination[1].ID);
        }

        [Test]
        public void TEST_TargetSelector_SelectTop_역순점수정책사용()
        {
            var candidates = new[]
            {
                new Candidate(1, 1f), new Candidate(2, 4f), new Candidate(3, 2f)
            };
            var destination = new List<Candidate>();

            var count = TargetSelector.SelectTop
            (
                candidates, 2, candidate => candidate.Score, destination,
                scoreComparer: new ReverseScoreComparer()
            );

            Assert.AreEqual(2, count);
            Assert.AreEqual(2, destination[0].ID);
            Assert.AreEqual(3, destination[1].ID);
        }

        [Test]
        public void TEST_TargetSelector_SelectTop_한개는TrySelect와동일()
        {
            var candidates = new[]
            {
                new Candidate(3, 1f), new Candidate(2, 1f), new Candidate(1, 2f)
            };
            var destination = new List<Candidate>();

            var found = TargetSelector.TrySelect
            (
                candidates, candidate => candidate.Score, out var selected,
                tieBreaker: new CandidateTieBreaker()
            );
            var count = TargetSelector.SelectTop
            (
                candidates, 1, candidate => candidate.Score, destination,
                tieBreaker: new CandidateTieBreaker()
            );

            Assert.IsTrue(found);
            Assert.AreEqual(1, count);
            Assert.AreEqual(selected.ID, destination[0].ID);
        }

        [Test]
        public void TEST_TargetSelector_SelectTop_실패한점수와NaN제외무한대허용()
        {
            var candidates = new[]
            {
                new Candidate(1, float.NaN), new Candidate(2, -1f, false),
                new Candidate(3, float.PositiveInfinity), new Candidate(4, 3f)
            };
            var destination = new List<Candidate>();

            var count = TargetSelector.SelectTop
            (
                candidates, 5, new CandidateScorer(), destination
            );

            Assert.AreEqual(2, count);
            Assert.AreEqual(4, destination[0].ID);
            Assert.AreEqual(3, destination[1].ID);
        }

    #endregion

    #region 실패 및 경계

        [Test]
        public void TEST_TargetSelector_빈입력과NaN만있으면선택실패()
        {
            var candidates = new[]
            {
                new Candidate(1, float.NaN)
            };
            var empty = TargetSelector.TrySelect
            (
                Array.Empty<Candidate>(), candidate => candidate.Score, out _
            );
            var invalid = TargetSelector.TrySelect
            (
                candidates, candidate => candidate.Score, out _
            );

            Assert.IsFalse(empty);
            Assert.IsFalse(invalid);
        }

        [Test]
        public void TEST_TargetSelector_SelectTop_0개는열거하지않고음수는예외()
        {
            var calls = 0;
            var candidates = EnumerateOnce(() => calls++);
            var destination = new List<Candidate>();
            Func<Candidate, float> scorer = candidate => candidate.Score;

            var count = TargetSelector.SelectTop
            (
                candidates, 0, scorer, destination
            );
            void SelectNegativeCount()
            {
                TargetSelector.SelectTop(candidates, -1, scorer, destination);
            }

            Assert.AreEqual(0, count);
            Assert.AreEqual(0, calls);
            Assert.AreEqual(0, destination.Count);
            Assert.Throws<ArgumentOutOfRangeException>(SelectNegativeCount);
        }

        [Test]
        public void TEST_TargetSelector_SelectTop_평가예외면기존목록유지()
        {
            var candidates = new[]
            {
                new Candidate(1, 1f), new Candidate(2, 2f)
            };
            var destination = new List<Candidate>
            {
                new Candidate(99, 0f)
            };
            Func<Candidate, float> scorer = candidate => candidate.ID == 2
                ? throw new InvalidOperationException() : candidate.Score;
            void SelectTopWithInvalidScore()
            {
                TargetSelector.SelectTop(candidates, 2, scorer, destination);
            }

            Assert.Throws<InvalidOperationException>(SelectTopWithInvalidScore);
            Assert.AreEqual(1, destination.Count);
            Assert.AreEqual(99, destination[0].ID);
        }

        [Test]
        public void TEST_TargetSelector_SelectAll_필터예외면이미추가된항목유지()
        {
            var candidates = new[]
            {
                new Candidate(1, 1f), new Candidate(2, 2f)
            };
            var destination = new List<Candidate>();
            var filter = new PredicateFilter<Candidate>
            (
                candidate => candidate.ID == 2
                    ? throw new InvalidOperationException()
                    : true
            );
            void SelectAllWithInvalidFilter()
            {
                TargetSelector.SelectAll(candidates, destination, filter);
            }

            Assert.Throws<InvalidOperationException>(SelectAllWithInvalidFilter);
            Assert.AreEqual(1, destination.Count);
            Assert.AreEqual(1, destination[0].ID);
        }

        [Test]
        public void TEST_TargetSelector_조건null은전체허용하고필수인자null은예외()
        {
            var candidates = Array.Empty<Candidate>();
            var destination = new List<Candidate>();
            Func<Candidate, float> scorer = candidate => candidate.Score;

            var acceptAll = new PredicateFilter<Candidate>(null);
            void SelectAllWithNullSource()
            {
                TargetSelector.SelectAll(null, destination);
            }

            void SelectAllWithNullDestination()
            {
                TargetSelector.SelectAll(candidates, null);
            }

            void SelectSingleWithNullScorer()
            {
                TargetSelector.TrySelect(candidates, (Func<Candidate, float>)null, out _);
            }

            void SelectTopWithNullDestination()
            {
                TargetSelector.SelectTop(candidates, 1, scorer, null);
            }

            Assert.IsTrue(acceptAll.IsMatch(new Candidate(1, 1f)));
            Assert.Throws<ArgumentNullException>(SelectAllWithNullSource);
            Assert.Throws<ArgumentNullException>(SelectAllWithNullDestination);
            Assert.Throws<ArgumentNullException>(SelectSingleWithNullScorer);
            Assert.Throws<ArgumentNullException>(SelectTopWithNullDestination);
        }

    #endregion

    }
}
