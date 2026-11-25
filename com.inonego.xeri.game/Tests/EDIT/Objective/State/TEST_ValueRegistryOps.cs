/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : TEST_ValueRegistryOps.cs
수정일 : 2026-11-24

# 설명
Objective가 Progress에 쓰는 ValueRegistry의 값 유무·비교·키 이름 변경을 검증한다.

# 특이사항, 제약사항
Core에는 별도 테스트 프로젝트가 없어 Objective 테스트에서 이 연산을 직접 확인한다.
========================================================================= BLOCK_HEADER_END */

using System;
using System.Collections;
using System.Collections.Generic;

using NUnit;
using NUnit.Framework;

using inonego;
using inonego.Xeri;
using inonego.Xeri.Serializable;

namespace inonego.Xeri.Game.TEST.Objective
{
    // ============================================================
    /// <summary>
    /// ValueRegistry의 HasAny, IsSameAs, Rename을 검증한다.
    /// </summary>
    // ============================================================
    public sealed class TEST_ValueRegistryOps
    {

    #region 테스트 데이터

        private static ValueRegistry Registry()
        {
            var values = new ValueRegistry();
            values.SetS("text", "hello");
            values.SetI("count", 3);
            values.SetF("ratio", 1.5f);
            values.SetFlag("done");
            return values;
        }

        private static IReadOnlyDictionary<string, string> Map(params (string From, string To)[] pairs)
        {
            var result = new Dictionary<string, string>();
            foreach (var (oldKey, newKey) in pairs)
            {
                result.Add(oldKey, newKey);
            }

            return result;
        }

    #endregion

    #region 값 유무·비교

        // ----------------------------------------------------------------------
        /// <summary>
        /// 어떤 타입이든 값이 하나라도 있으면 있다고 하고, 모두 지우면 다시 없다고 한다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_ValueRegistryOps_값이있는지확인한다()
        {
            var values = new ValueRegistry();
            Assert.That(values.HasAny, Is.False);

            values.SetFlag("done");
            Assert.That(values.HasAny, Is.True);

            values.Clear();
            Assert.That(values.HasAny, Is.False);
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// 모든 타입의 값이 같을 때만 같고, 무시할 키는 어떤 타입이든 비교에서 뺀다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_ValueRegistryOps_값구성이같은지비교한다()
        {
            var first = Registry();

            Assert.That(first.IsSameAs(Registry()), Is.True);
            Assert.That(first.IsSameAs(first), Is.True);
            Assert.That(first.IsSameAs(null), Is.False);

            var changed = Registry();
            changed.SetI("count", 4);
            Assert.That(first.IsSameAs(changed), Is.False);
            Assert.That(first.IsSameAs(changed, "count"), Is.True);

            var extra = Registry();
            extra.SetFlag("extra");
            Assert.That(first.IsSameAs(extra), Is.False);
            Assert.That(extra.IsSameAs(first), Is.False);
            Assert.That(first.IsSameAs(extra, "extra"), Is.True);

            var differentKind = new ValueRegistry();
            differentKind.SetS("text", "hello");
            differentKind.SetI("count", 3);
            differentKind.SetF("ratio", 1.5f);
            differentKind.SetS("done", "yes");
            Assert.That(first.IsSameAs(differentKind), Is.False);
        }

    #endregion

    #region 이름 변경

        // ------------------------------------------------------------
        /// <summary>
        /// 모든 타입의 값이 새 이름으로 옮겨지고, 없는 이름은 건너뛴다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ValueRegistryOps_모든타입을새이름으로옮긴다()
        {
            var values = Registry();

            values.Rename(Map(("text", "t2"), ("count", "c2"), ("ratio", "r2"), ("done", "d2"), ("missing", "m2")));

            Assert.That(values.GetS("t2"), Is.EqualTo("hello"));
            Assert.That(values.GetI("c2"), Is.EqualTo(3));
            Assert.That(values.GetF("r2"), Is.EqualTo(1.5f));
            Assert.That(values.HasFlag("d2"), Is.True);
            Assert.That(values.Has("text") || values.Has("count") || values.Has("ratio") || values.Has("done"), Is.False);
            Assert.That(values.Has("m2"), Is.False);
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// 이름을 서로 바꿔도 값이 섞이지 않고, 새 이름이 null이면 그 키를 지운다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_ValueRegistryOps_교환과삭제를지원한다()
        {
            var values = new ValueRegistry();
            values.SetI("a", 1);
            values.SetI("b", 2);
            values.SetI("gone", 9);

            values.Rename(Map(("a", "b"), ("b", "a"), ("gone", null)));

            Assert.That(values.GetI("a"), Is.EqualTo(2));
            Assert.That(values.GetI("b"), Is.EqualTo(1));
            Assert.That(values.Has("gone"), Is.False);
        }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// 바뀐 만큼이 아니라 이름 변경 한 번을 하나의 변경으로 알리고, 바뀐 것이 없으면 알리지 않는다.
        /// </summary>
        // --------------------------------------------------------------------------------
        [Test]
        public void TEST_ValueRegistryOps_이름변경은한번만알린다()
        {
            var values = Registry();
            int changes = 0;
            values.OnChange += () => changes++;

            values.Rename(Map(("count", "c2"), ("ratio", "r2")));
            Assert.That(changes, Is.EqualTo(1));

            values.Rename(Map(("missing", "other")));
            Assert.That(changes, Is.EqualTo(1));
        }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/>새 이름이 바뀌지 않는 기존 키와 겹치거나 둘이 같은 새 이름으로 가면 아무것도 바꾸지
        /// <br/>않고 거부한다.
        /// </summary>
        // --------------------------------------------------------------------------------
        [Test]
        public void TEST_ValueRegistryOps_겹치면아무것도바꾸지않고거부한다()
        {
            var values = Registry();
            var before = Registry();

            Assert.Throws<InvalidOperationException>(() => values.Rename(Map(("count", "ratio"))));
            Assert.Throws<InvalidOperationException>(() => values.Rename(Map(("count", "x"), ("ratio", "x"))));
            Assert.Throws<ArgumentNullException>(() => values.Rename(null));

            Assert.That(values.IsSameAs(before), Is.True);
        }

    #endregion

    }
}
