/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : TEST_SpecIndex.cs
수정일 : 2026-11-25

# 설명
Spec이 정해 두는 조회표(SpecIndex)와 검증 통과 표시를 검증한다.

# 특이사항, 제약사항
SpecWalker를 상속한 순회가 트리를 한 번 돌아 사실을 모으고 ObjectiveSpec이 이를 한 번만 계산해 보관하는지 확인한다.
========================================================================= BLOCK_HEADER_END */

using System;
using System.Collections;
using System.Collections.Generic;

using NUnit;
using NUnit.Framework;

using inonego;
using inonego.Xeri;
using inonego.Xeri.Game;
using inonego.Xeri.Game.Objective;

namespace inonego.Xeri.Game.TEST.Objective
{
    // ============================================================
    /// <summary>
    /// SpecIndex의 수집 결과와 ObjectiveSpec의 캐시를 검증한다.
    /// </summary>
    // ============================================================
    public sealed class TEST_SpecIndex
    {

    #region 테스트 데이터

        // Sequential 루트 아래 OncePassed Entry 하나와, 시간을 재는 Entry를 가진 Parallel Group 하나.
        private static ObjectiveSpec Spec()
        {
            var localProgressIDs2 = new[]
            {
                "a-count"
            };
            var taskA = new TaskSpec("a-node", ObjectiveCond.HasFlag("a"), localProgressIDs: localProgressIDs2);
            var localProgressIDs3 = new[]
            {
                "c-count",
                "c-extra"
            };
            var taskC = new TaskSpec("c-node", ObjectiveCond.HasFlag("c"), localProgressIDs: localProgressIDs3);
            var innerEntries = new TaskChildEntry[]
            {
                new("c-entry", taskC, tracksOpenTime: true)
            };
            var inner = new TaskGroupSpec
            (
                "inner", TaskExecutionMode.Parallel, TaskGroupCond.All(), innerEntries
            );
            var root = new TaskGroupSpec
            (
                "root", TaskExecutionMode.Sequential, TaskGroupCond.All(),
                new TaskChildEntry[]
                {
                    new("a-entry", taskA, revalidationMode: TaskRevalidationMode.OncePassed),
                    new("inner-entry", inner)
                }
            );
            return new ObjectiveSpec("quest", 1, root);
        }

    #endregion

    #region 사실 수집

        // ----------------------------------------------------------------------
        /// <summary>
        /// 트리를 한 번 돌아 Progress 소유자, 시간 Entry, 이력 대상, Entry 수를 모은다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_SpecIndex_트리에서조회표를만든다()
        {
            var index = Spec().Index;

            Assert.That(index.ProgressOwners["a-count"], Is.EqualTo("a-node"));
            Assert.That(index.ProgressOwners["c-count"], Is.EqualTo("c-node"));
            Assert.That(index.ProgressOwners["c-extra"], Is.EqualTo("c-node"));
            Assert.That(index.ProgressOwners.Count, Is.EqualTo(3));

            var expected = new[]
            {
                "c-entry"
            };
            Assert.That(index.TimedEntryIDs, Is.EquivalentTo(expected));

            // 이력은 Sequential Group만 갖는다. Parallel인 inner는 대상이 아니다.
            var expected2 = new[]
            {
                "root"
            };
            Assert.That(index.HistoryGroups.Keys, Is.EquivalentTo(expected2));
            var expected3 = new[]
            {
                "a-entry"
            };
            Assert.That(index.OncePassedEntryIDs, Is.EquivalentTo(expected3));

            Assert.That(index.EntryCount, Is.EqualTo(3));
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// 조회표는 Spec마다 한 번만 만들어 모든 Engine이 같은 객체를 공유한다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_SpecIndex_Spec이한번만계산해공유한다()
        {
            var spec = Spec();

            Assert.That(spec.Index, Is.SameAs(spec.Index));
        }

    #endregion

    #region 검증 통과 표시

        // ------------------------------------------------------------
        /// <summary>
        /// 검증을 통과해야 표시가 켜지고, 오류 수집만 한 Spec은 표시하지 않는다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_SpecIndex_검증을통과해야표시한다()
        {
            var spec = Spec();
            Assert.That(spec.IsValidated, Is.False);

            ObjectiveSpecValidator.Collect(spec);
            Assert.That(spec.IsValidated, Is.False);

            ObjectiveSpecValidator.Validate(spec);
            Assert.That(spec.IsValidated, Is.True);

            var invalid = new ObjectiveSpec("quest", 0, spec.Root);
            Assert.Throws<ArgumentException>(() => ObjectiveSpecValidator.Validate(invalid));
            Assert.That(invalid.IsValidated, Is.False);
        }

    #endregion

    }
}
