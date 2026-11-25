/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : TEST_AbilityPersistence.cs
수정일 : 2026-11-19

# 설명
AbilityState의 Unity JSON 복원(새 인스턴스와 기존 인스턴스 덮어쓰기, 진행 중·일시정지 Cooldown, 부분 충전, Values)과 메모리 복제(Clone·CloneFrom) 계약을 검증한다.

# 테스트 구성
 P: 직렬화 저장·복원
 C: 메모리 복제
========================================================================= BLOCK_HEADER_END */

using System;

using UnityEngine;

using NUnit;
using NUnit.Framework;

namespace inonego.Xeri.Game.TEST._Ability
{
    using Xeri;
    using Xeri.Game;

    // ============================================================
    /// <summary>
    /// Ability 상태의 저장과 복원 계약을 검증하는 테스트 묶음.
    /// </summary>
    // ============================================================
    public sealed class TEST_AbilityPersistence
    {

    #region P-1: 지속 상태 직렬화와 복원

        // ----------------------------------------------------------------------
        /// <summary>
        /// 저장과 복원 후 진행 중인 Cooldown, 부분 충전, Values가 유지되는지 검증한다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_AbilityState_저장복원은진행중Cooldown과부분충전과Values를유지()
        {
            var state = new AbilityState(new AbilityKey("SavedSkill"));
            Assert.IsTrue(state.TryBeginCooldown(5f));
            state.ConfigureCharge(3, 2f, startFull: false);
            state.Values.SetI("Stack", 3);
            state.Values.SetS("Label", "saved");
            state.Values.SetF("Multiplier", 1.25f);
            state.Values.SetFlag("Empowered");
            state.Advance(1.5f);
            var revision = state.Revision;

            var restored = JsonUtility.FromJson<AbilityState>(JsonUtility.ToJson(state));

            // 저장은 Revision을 바꾸지 않고 변경 번호는 저장하지 않는다. 복원은 변경 1회로 센다.
            Assert.AreEqual(revision, state.Revision);
            Assert.AreEqual(Revision.FromUInt64(1), restored.Revision);
            Assert.AreEqual("SavedSkill", restored.Key.Value);
            Assert.IsTrue(restored.Cooldown.IsRunning);
            Assert.AreEqual(3.5f, restored.Cooldown.RemainingTime, 0.0001f);
            Assert.AreEqual(0, restored.Charge.Current);
            Assert.IsTrue(restored.Charge.Recharge.IsRunning);
            Assert.AreEqual(0.5f, restored.Charge.Recharge.RemainingTime, 0.0001f);
            Assert.AreEqual(3, restored.Values.GetI("Stack"));
            Assert.AreEqual("saved", restored.Values.GetS("Label"));
            Assert.AreEqual(1.25f, restored.Values.GetF("Multiplier"), 0.0001f);
            Assert.IsTrue(restored.Values.HasFlag("Empowered"));

            restored.Advance(0.5f);

            // 충전이 끝나면 다음 주기를 처음부터 시작한다.
            Assert.AreEqual(1, restored.Charge.Current);
            Assert.AreEqual(2f, restored.Charge.Recharge.RemainingTime, 0.0001f);
            Assert.AreEqual(3f, restored.Cooldown.RemainingTime, 0.0001f);

            // 복원한 상태의 Values 변경도 Revision에 반영한다.
            restored.Values.SetI("Stack", 4);
            Assert.AreEqual(Revision.FromUInt64(2), restored.Revision);
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> 구성하지 않은 Charge는 저장과 복원 뒤에도 null이고, 이미 구성한 State에
        /// <br/> 구성하지 않은 데이터를 덮어쓰면 Charge가 null로 돌아오는지 검증한다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_AbilityState_구성하지않은Charge는복원뒤에도null()
        {
            var plain = new AbilityState(new AbilityKey("Plain"));
            var saved = JsonUtility.ToJson(plain);

            var restored = JsonUtility.FromJson<AbilityState>(saved);
            Assert.IsNull(restored.Charge);

            var configured = new AbilityState(new AbilityKey("Plain"));
            configured.ConfigureCharge(2, 1f);
            Assert.IsNotNull(configured.Charge);

            JsonUtility.FromJsonOverwrite(saved, configured);
            Assert.IsNull(configured.Charge);

            // 구성한 Charge는 그대로 복원한다.
            var burst = new AbilityState(new AbilityKey("Burst"));
            burst.ConfigureCharge(3, 2f, startFull: false);
            var burstRestored = JsonUtility.FromJson<AbilityState>(JsonUtility.ToJson(burst));

            Assert.IsNotNull(burstRestored.Charge);
            Assert.AreEqual(3, burstRestored.Charge.Capacity);
            Assert.AreEqual(0, burstRestored.Charge.Current);
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// 복원 전에 보관한 Values 참조로 바꾼 값도 Revision에 반영되는지 검증한다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_AbilityState_복원전에보관한Values참조의변경도Revision에반영()
        {
            var state = new AbilityState(new AbilityKey("SavedSkill"));
            var values = state.Values;
            values.SetI("Stack", 1);
            var json = JsonUtility.ToJson(state);

            JsonUtility.FromJsonOverwrite(json, state);
            var revision = state.Revision;
            values.SetI("Stack", 2);

            Assert.AreEqual(revision.Inc(), state.Revision);
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> 복원 뒤 외부 Values 핸들러가 예외를 던져도 Revision이 먼저 반영되는지
        /// <br/> 검증한다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_AbilityState_복원뒤외부Values핸들러예외에도Revision에반영()
        {
            var state = new AbilityState(new AbilityKey("SavedSkill"));
            var values = state.Values;
            values.OnChange += () => throw new InvalidOperationException("외부 핸들러");
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(state), state);
            var revision = state.Revision;

            Assert.Throws<InvalidOperationException>(() => values.SetI("Stack", 1));

            Assert.AreEqual(revision.Inc(), state.Revision);
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// 일시정지한 Cooldown도 저장과 복원 후 남은 시간을 유지하는지 검증한다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_AbilityState_일시정지한Cooldown도저장복원후남은시간을유지()
        {
            var state = new AbilityState(new AbilityKey("SavedSkill"));
            Assert.IsTrue(state.TryBeginCooldown(5f));
            state.Advance(2f);
            Assert.IsTrue(state.PauseCooldown());

            var restored = JsonUtility.FromJson<AbilityState>(JsonUtility.ToJson(state));

            Assert.IsTrue(restored.Cooldown.IsPaused);
            Assert.IsFalse(restored.IsCooldownReady);
            Assert.AreEqual(3f, restored.Cooldown.RemainingTime, 0.0001f);

            restored.Advance(10f);
            Assert.AreEqual(3f, restored.Cooldown.RemainingTime, 0.0001f);

            Assert.IsTrue(restored.ResumeCooldown());
            restored.Advance(1f);
            Assert.AreEqual(2f, restored.Cooldown.RemainingTime, 0.0001f);
        }

    #endregion

    #region P-2: 기존 인스턴스에 덮어쓰는 복원

        private static AbilityState CreateRestoreSource(string key = "Skill")
        {
            var source = new AbilityState(new AbilityKey(key));
            source.TryBeginCooldown(5f);
            source.ConfigureCharge(3, 1f, startFull: false);
            source.Values.SetI("Stack", 7);
            source.Values.SetS("Label", "saved");
            source.Advance(0.4f);
            return source;
        }

        private static void Restore(AbilityState source, AbilityState target)
        {
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(source), target);
        }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// FromJsonOverwrite 복원이 내용을 덮어쓰고 Revision을 정확히 한 번 올리는지 검증한다.
        /// </summary>
        // --------------------------------------------------------------------------------
        [Test]
        public void TEST_AbilityState_직렬화복원은내용을덮어쓰고Revision을한번올림()
        {
            var target = new AbilityState(new AbilityKey("Skill"));
            target.Values.SetI("Old", 1);
            target.ConfigureCharge(2, 1f);
            var revision = target.Revision;
            var source = CreateRestoreSource();
            var sourceRevision = source.Revision;

            Restore(source, target);

            Assert.AreEqual(revision.Inc(), target.Revision);
            Assert.AreEqual(4.6f, target.Cooldown.RemainingTime, 0.0001f);
            Assert.AreEqual(0, target.Charge.Current);
            Assert.AreEqual(7, target.Values.GetI("Stack"));
            Assert.AreEqual("saved", target.Values.GetS("Label"));
            Assert.AreEqual(sourceRevision, source.Revision);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 복원 뒤에도 Values 변경이 Revision에 계속 반영되는지 검증한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_AbilityState_직렬화복원뒤에도Values변경이Revision을올림()
        {
            var target = new AbilityState(new AbilityKey("Skill"));
            target.Values.SetI("Old", 1);
            var source = CreateRestoreSource();

            Restore(source, target);
            var restored = target.Revision;

            target.Values.SetI("Stack", 9);
            Assert.AreEqual(restored.Inc(), target.Revision);
            Assert.AreEqual(7, source.Values.GetI("Stack"));

            // 두 번째 복원도 구독을 중복하지 않고 한 번만 센다.
            Restore(source, target);
            restored = target.Revision;
            target.Values.SetI("Stack", 10);
            Assert.AreEqual(restored.Inc(), target.Revision);
        }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// 진행 중인 Cooldown과 Charge 충전 진행이 복원 뒤에도 남은 시간으로 이어지는지 검증한다.
        /// </summary>
        // --------------------------------------------------------------------------------
        [Test]
        public void TEST_AbilityState_직렬화복원은진행중Cooldown과Charge충전을이어감()
        {
            var source = CreateRestoreSource();
            var target = new AbilityState(new AbilityKey("Skill"));

            Restore(source, target);

            Assert.IsTrue(target.Cooldown.IsRunning);
            Assert.AreEqual(4.6f, target.Cooldown.RemainingTime, 0.0001f);
            Assert.IsTrue(target.Charge.Recharge.IsRunning);
            Assert.AreEqual(0.6f, target.Charge.Recharge.RemainingTime, 0.0001f);

            // 지연 보정은 복원 뒤 Advance로 처리한다.
            target.Advance(0.6f);
            Assert.AreEqual(4f, target.Cooldown.RemainingTime, 0.0001f);
            Assert.AreEqual(1, target.Charge.Current);
        }

    #endregion

    #region C-1: 메모리 복제

        // ----------------------------------------------------------------------
        /// <summary>
        /// 복제 검증용으로 Cooldown·Charge·Values를 모두 진행한 상태를 만든다.
        /// </summary>
        // ----------------------------------------------------------------------
        private static AbilityState CreateProgressedState()
        {
            var state = new AbilityState(new AbilityKey("Burst"));
            state.ConfigureCharge(3, 2f);
            Assert.IsTrue(state.TryConsumeCharge(2));
            Assert.IsTrue(state.TryBeginCooldown(5f));
            state.Values.SetI("Stage", 2);
            state.Advance(0.5f);
            Assert.IsTrue(state.PauseCooldown());
            return state;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Clone은 진행 중인 상태 전체를 독립된 새 인스턴스로 복제한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_AbilityState_Clone은진행중인상태를독립사본으로복제()
        {
            var source = CreateProgressedState();

            var clone = source.Clone();

            Assert.AreNotSame(source, clone);
            Assert.AreNotSame(source.Charge, clone.Charge);
            Assert.AreEqual(source.Key, clone.Key);
            Assert.AreEqual(JsonUtility.ToJson(source), JsonUtility.ToJson(clone));

            clone.ResumeCooldown();
            clone.Advance(1f);
            clone.Values.SetI("Stage", 3);

            Assert.IsTrue(source.Cooldown.IsPaused);
            Assert.AreEqual(1, source.Charge.Current);
            Assert.AreEqual(2, source.Values.GetI("Stage"));
        }

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> CloneFrom은 인스턴스와 구독을 유지한 채 알림 없이 덮어쓰고
        /// <br/> Revision을 한 번만 올린다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_AbilityState_CloneFrom은인스턴스와구독을유지하고알림없이Revision1증가()
        {
            var source = CreateProgressedState();
            var target = new AbilityState(new AbilityKey("Burst"));
            target.ConfigureCharge(5, 1f);
            var values = target.Values;
            var charge = target.Charge;
            var cooldown = target.Cooldown;
            var recharge = target.Charge.Recharge;
            var notifications = 0;
            values.OnChange += () => notifications++;
            target.OnCooldownEnded += _ => notifications++;
            target.OnChargeRecharged += (_, _) => notifications++;
            var revision = target.Revision;

            target.CloneFrom(source);

            Assert.AreEqual(0, notifications);
            Assert.AreEqual(revision.Inc(), target.Revision);
            Assert.AreSame(values, target.Values);
            Assert.AreSame(charge, target.Charge);
            Assert.AreSame(cooldown, target.Cooldown);
            Assert.AreSame(recharge, target.Charge.Recharge);
            Assert.AreEqual(JsonUtility.ToJson(source), JsonUtility.ToJson(target));

            target.Values.SetI("Stage", 4);

            Assert.AreEqual(1, notifications);
            Assert.AreEqual(revision.Inc().Inc(), target.Revision);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Charge 구성 여부도 source를 그대로 따른다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_AbilityState_CloneFrom은Charge구성여부를source대로복제()
        {
            var configured = CreateProgressedState();
            var unconfigured = new AbilityState(new AbilityKey("Burst"));

            var target = new AbilityState(new AbilityKey("Burst"));
            target.CloneFrom(configured);

            Assert.IsNotNull(target.Charge);
            Assert.AreNotSame(configured.Charge, target.Charge);
            Assert.AreEqual(1, target.Charge.Current);

            target.CloneFrom(unconfigured);

            Assert.IsNull(target.Charge);
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// 다른 Key, 사용 중 상태, null source는 거부하고 상태를 바꾸지 않는다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_AbilityState_CloneFrom은다른Key와사용중과null을거부()
        {
            var source = CreateProgressedState();
            var other = new AbilityState(new AbilityKey("Other"));
            var revision = other.Revision;

            Assert.Throws<ArgumentException>(() => other.CloneFrom(source));
            Assert.AreEqual(revision, other.Revision);

            var target = new AbilityState(new AbilityKey("Burst"));
            Assert.Throws<ArgumentNullException>(() => target.CloneFrom(null));

            target.EnterUse();
            try
            {
                Assert.Throws<InvalidOperationException>(() => target.CloneFrom(source));
            }
            finally
            {
                target.ExitUse();
            }

            Assert.IsNull(target.Charge);
            Assert.AreEqual(Revision.None, target.Revision);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> 저장 데이터에 앞뒤 공백이 남은 Key도 AbilityKey 규칙으로
        /// <br/> 같은 Key로 보고 복제하는지 검증한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_AbilityState_CloneFrom은공백만다른저장Key를같은Key로복제()
        {
            var source = CreateProgressedState();
            var saved = JsonUtility.ToJson(source);
            var json = saved.Replace("\"Burst\"", "\"  Burst \"");
            Assert.AreNotEqual(saved, json);
            var spaced = JsonUtility.FromJson<AbilityState>(json);
            var target = new AbilityState(new AbilityKey("Burst"));

            target.CloneFrom(spaced);

            Assert.AreEqual(Revision.FromUInt64(1), target.Revision);
            Assert.AreEqual(1, target.Charge.Current);
            Assert.AreEqual(2, target.Values.GetI("Stage"));
        }

    #endregion

    }
}
