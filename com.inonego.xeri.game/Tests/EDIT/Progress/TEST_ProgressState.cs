/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : TEST_ProgressState.cs
수정일 : 2026-11-24

# 설명
공용 Progress의 자료형별 값·알림·독립 소유권과 Unity 저장 복원을 검증한다.

# 특이사항, 제약사항
ValueRegistry를 직접 사용하며 Reader·목표 판정 API는 제공하지 않고 복제는 IDeepCloneable로 제공한다.
========================================================================= BLOCK_HEADER_END */

using System;

using UnityEngine;

using NUnit;
using NUnit.Framework;

using inonego;
using inonego.Xeri;
using inonego.Xeri.Game;
using inonego.Xeri.Game.Progress;
using inonego.Xeri.Serializable;

namespace inonego.Xeri.Game.TEST.Progress
{
    // ============================================================
    /// <summary>
    /// 공용 진행 상태의 ValueRegistry 소유와 직렬화를 검증한다.
    /// </summary>
    // ============================================================
    public sealed class TEST_ProgressState
    {

    #region 복제

        // --------------------------------------------------------------------------------
        /// <summary>
        /// 복제는 같은 값을 가진 독립 ProgressState를 만들고 이후 변경이 서로 영향을 주지 않는다.
        /// </summary>
        // --------------------------------------------------------------------------------
        [Test]
        public void TEST_ProgressState_복제는독립값을가진다()
        {
            var state = new ProgressState();
            state.Values.SetI("count", 3);
            state.Values.SetFlag("done");

            var copy = state.Clone();

            Assert.That(copy, Is.Not.SameAs(state));
            Assert.That(copy.Values.GetI("count"), Is.EqualTo(3));
            Assert.That(copy.Values.HasFlag("done"), Is.True);

            copy.Values.SetI("count", 9);
            Assert.That(state.Values.GetI("count"), Is.EqualTo(3));

            Assert.Throws<ArgumentNullException>(() => copy.CloneFrom(null));
        }

    #endregion

    #region 값·알림

        // ------------------------------------------------------------
        /// <summary>
        /// Progress는 자료형별 Key를 Registry에서 직접 저장·조회한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ProgressState_모든값종류를자유롭게등록한다()
        {
            var state = new ProgressState();
            ValueRegistry values = state.Values;

            values.SetI("kill.slime", 4);
            values.AddI("kill.slime", 8);
            values.SetF("distance", 125.5f);
            values.SetS("dialogue.choice", "accept");
            values.SetFlag("npc.talked");

            Assert.That(values.GetI("kill.slime"), Is.EqualTo(12));
            Assert.That(values.GetF("distance"), Is.EqualTo(125.5f));
            Assert.That(values.GetS("dialogue.choice"), Is.EqualTo("accept"));
            Assert.That(values.HasFlag("npc.talked"), Is.True);
            Assert.That(state.Values, Is.SameAs(values));

            Assert.That(values.Remove("npc.talked"), Is.True);
            Assert.That(values.HasFlag("npc.talked"), Is.False);
            values.Clear();
            Assert.That(values.ValIs, Is.Empty);
            Assert.That(values.ValFs, Is.Empty);
            Assert.That(values.ValSs, Is.Empty);
            Assert.That(values.ValFlags, Is.Empty);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Registry의 중복 변경 생략과 타입 충돌 검증을 재사용한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ProgressState_Registry기존알림과타입검증을사용한다()
        {
            var state = new ProgressState();
            int count = 0;
            state.Values.OnChange += () => count++;

            state.Values.SetI("count", 0);
            state.Values.SetI("count", 0);
            state.Values.AddI("count", 1);
            state.Values.SetFlag("reported");
            state.Values.SetFlag("reported");
            Assert.Throws<InvalidOperationException>(() => state.Values.SetS("count", "invalid"));
            Assert.That(count, Is.EqualTo(3));

            Assert.That(state.Values.Remove("missing"), Is.False);
            Assert.That(count, Is.EqualTo(3));
            state.Values.Clear();
            Assert.That(count, Is.EqualTo(4));
            state.Values.Clear();
            Assert.That(count, Is.EqualTo(4));
        }

        // ------------------------------------------------------------
        /// <summary>
        /// World와 Account처럼 별개 소유자가 독립 진행값을 보관한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ProgressState_서로다른소유자는진행값을공유하지않는다()
        {
            var world = new ProgressState();
            var account = new ProgressState();
            world.Values.AddI("kills", 3);
            account.Values.AddI("kills", 7);

            Assert.That(world.Values, Is.Not.SameAs(account.Values));
            Assert.That(world.Values.GetI("kills"), Is.EqualTo(3));
            Assert.That(account.Values.GetI("kills"), Is.EqualTo(7));

            world.Values.Clear();
            Assert.That(account.Values.GetI("kills"), Is.EqualTo(7));
        }

    #endregion

    #region 저장·복원

        // ------------------------------------------------------------
        /// <summary>
        /// Unity 직렬화에서 Registry가 저장·독립 복원된다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ProgressState_JsonUtility로원본Registry만복원한다()
        {
            var state = new ProgressState();
            state.Values.SetI("count", 12);
            state.Values.SetF("distance", 2.5f);
            state.Values.SetS("choice", "yes");
            state.Values.SetFlag("reported");

            string json = JsonUtility.ToJson(state);
            Assert.That(json, Does.Contain("values"));
            Assert.That(json, Does.Not.Contain("goal"));
            Assert.That(json, Does.Not.Contain("manuals"));
            Assert.That(json, Does.Not.Contain("counters"));

            var restored = JsonUtility.FromJson<ProgressState>(json);
            Assert.That(restored.Values.GetI("count"), Is.EqualTo(12));
            Assert.That(restored.Values.GetF("distance"), Is.EqualTo(2.5f));
            Assert.That(restored.Values.GetS("choice"), Is.EqualTo("yes"));
            Assert.That(restored.Values.HasFlag("reported"), Is.True);
            restored.Values.SetI("count", 14);
            Assert.That(state.Values.GetI("count"), Is.EqualTo(12));
        }

    #endregion

    }
}
