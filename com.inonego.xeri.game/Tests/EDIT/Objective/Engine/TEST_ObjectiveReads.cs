/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : TEST_ObjectiveReads.cs
수정일 : 2026-11-26

# 설명
ObjectiveReads의 기록 상태 구분, 내용 비교, 복사, 읽기 전용 노출을 검증한다.

# 특이사항, 제약사항
default는 기록하지 않는 상태이며 내용 비교에서는 빈 집합과 같다. 구조체 복사본은 같은 집합을 가리킨다.
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
    /// ObjectiveReads의 기록·비교·복사를 검증한다.
    /// </summary>
    // ============================================================
    public sealed class TEST_ObjectiveReads
    {

    #region 기록

        // ------------------------------------------------------------
        /// <summary>
        /// <br/>default는 기록하지 않는 상태라 Add와 Clear를 무시한다.
        /// <br/>예외 없이 비어 있는 채로 남는다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveReads_default는기록하지않고Add를무시한다()
        {
            ObjectiveReads reads = default;

            reads.Add("scope", "name");
            reads.Clear();

            Assert.That(reads.IsTracking, Is.False);
            Assert.That(reads.Count, Is.EqualTo(0));
            Assert.That(reads.View.Count, Is.EqualTo(0));
        }

        // ------------------------------------------------------------
        /// <summary>
        /// <br/>Create로 만든 것은 비어 있어도 기록하는 상태다.
        /// <br/>Add한 대상은 보이고 Clear하면 비워진다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveReads_Create는기록하고Clear로비운다()
        {
            var reads = ObjectiveReads.Create();

            Assert.That(reads.IsTracking, Is.True);
            Assert.That(reads.Count, Is.EqualTo(0));

            reads.Add("scope", "name");
            reads.Add(null, "fact");
            reads.Add("scope", "name");

            Assert.That(reads.Count, Is.EqualTo(2));
            Assert.That(reads.View.Contains(("scope", "name")), Is.True);
            Assert.That(reads.View.Contains((null, "fact")), Is.True);

            reads.Clear();

            Assert.That(reads.Count, Is.EqualTo(0));
            Assert.That(reads.IsTracking, Is.True);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// <br/>구조체를 복사해도 같은 집합을 가리킨다.
        /// <br/>복사본에 기록한 대상이 원래 값에서도 보인다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveReads_구조체복사본은같은집합을공유한다()
        {
            var original = ObjectiveReads.Create();
            var copied = original;

            copied.Add("scope", "name");

            Assert.That(original.Count, Is.EqualTo(1));
        }

    #endregion

    #region 비교

        // ------------------------------------------------------------
        /// <summary>
        /// 같은 내용이면 서로 다른 집합이어도, 넣은 순서가 달라도 같다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveReads_같은내용은순서와무관하게같다()
        {
            var left = ObjectiveReads.Create();
            left.Add("a", "x");
            left.Add(null, "y");
            var right = ObjectiveReads.Create();
            right.Add(null, "y");
            right.Add("a", "x");

            Assert.That(left.IsEquivalentTo(right), Is.True);
            Assert.That(right.IsEquivalentTo(left), Is.True);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// <br/>개수가 같아도 원소가 하나라도 다르면 다르다.
        /// <br/>개수가 다르면 당연히 다르다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveReads_원소가다르면다르다()
        {
            var left = ObjectiveReads.Create();
            left.Add("a", "x");
            left.Add("a", "y");
            var sameCount = ObjectiveReads.Create();
            sameCount.Add("a", "x");
            sameCount.Add("a", "z");
            var moreCount = ObjectiveReads.Create();
            moreCount.Add("a", "x");
            moreCount.Add("a", "y");
            moreCount.Add("a", "z");

            Assert.That(left.IsEquivalentTo(sameCount), Is.False);
            Assert.That(sameCount.IsEquivalentTo(left), Is.False);
            Assert.That(left.IsEquivalentTo(moreCount), Is.False);
            Assert.That(moreCount.IsEquivalentTo(left), Is.False);
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// 기록하지 않는 상태와 비어 있는 집합은 같고, 내용이 있는 집합과는 다르다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveReads_default와빈집합은같다()
        {
            ObjectiveReads none = default;
            var empty = ObjectiveReads.Create();
            var filled = ObjectiveReads.Create();
            filled.Add("a", "x");

            Assert.That(none.IsEquivalentTo(empty), Is.True);
            Assert.That(empty.IsEquivalentTo(none), Is.True);
            Assert.That(none.IsEquivalentTo(default), Is.True);
            Assert.That(none.IsEquivalentTo(filled), Is.False);
            Assert.That(filled.IsEquivalentTo(none), Is.False);
        }

    #endregion

    #region 복사

        // ------------------------------------------------------------
        /// <summary>
        /// <br/>Copy는 내용이 같은 새 집합을 만든다.
        /// <br/>원본을 비우거나 바꿔도 복사본은 그대로다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveReads_Copy는원본과분리된다()
        {
            var original = ObjectiveReads.Create();
            original.Add("a", "x");
            original.Add(null, "y");

            var copied = original.Copy();
            original.Clear();
            original.Add("b", "z");

            Assert.That(copied.Count, Is.EqualTo(2));
            Assert.That(copied.View.Contains(("a", "x")), Is.True);
            Assert.That(copied.View.Contains((null, "y")), Is.True);
            Assert.That(copied.View.Contains(("b", "z")), Is.False);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// <br/>비어 있는 것을 복사하면 기록하지 않는 상태가 된다.
        /// <br/>기록하지 않는 상태를 복사해도 마찬가지다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveReads_비어있으면Copy가기록하지않는다()
        {
            var empty = ObjectiveReads.Create();
            ObjectiveReads none = default;

            Assert.That(empty.Copy().IsTracking, Is.False);
            Assert.That(none.Copy().IsTracking, Is.False);
        }

    #endregion

    }
}
