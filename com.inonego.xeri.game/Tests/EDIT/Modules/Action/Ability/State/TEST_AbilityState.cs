/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : TEST_AbilityState.cs
수정일 : 2026-11-19

# 설명
Ability State의 Owner별 독립성, 제어와 시간 입력, Revision과 사용 구간 계약을 검증한다.
직렬화 복원은 TEST_AbilityPersistence.cs에서 검증한다.
진행이 끝난 뒤 보내는 Cooldown 종료·Charge 충전 알림 계약을 검증한다.

# 테스트 구성
 S: Owner 상태·제어·시간·알림·Revision·사용 구간
========================================================================= BLOCK_HEADER_END */

using System;
using System.Collections;
using System.Collections.Generic;

using NUnit;
using NUnit.Framework;

namespace inonego.Xeri.Game.TEST._Ability
{
    using Xeri;
    using Xeri.Game;
    using Xeri.Utility;
    using Xeri.Game.TEST;
    using Xeri.Game.TEST._Ability;
    using Xeri.Game.TEST._Ability.Fixture;

    // ================================================================================
    /// <summary>
    /// Ability State의 Owner별 상태와 제어, 알림, Revision 계약을 검증하는 테스트 묶음.
    /// </summary>
    // ================================================================================
    public sealed class TEST_AbilityState : AbilityTestBase
    {

    #region S-1: 소유자별 상태와 시간 독립성

        // ----------------------------------------------------------------------
        /// <summary>
        /// 같은 Spec을 가진 Instance가 Owner별 State를 독립적으로 보존하는지 검증한다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_AbilityInstance_같은Spec의OwnerState를독립보존()
        {
            var spec = new TestSpec("Fireball");
            var key = new AbilityKey("Fireball");
            var first = new AbilityInstance<TestSpec>
            (
                spec,
                new AbilityState(key)
            );
            var second = new AbilityInstance<TestSpec>
            (
                spec,
                new AbilityState(key)
            );

            first.State.Values.SetI("Stack", 3);
            first.State.Control.TryBeginCooldown(2f);
            first.State.Control.ConfigureCharge(2, 1f);
            first.State.Control.TryConsumeCharge(1);
            first.State.Values.SetI("FollowupUses", 1);

            second.State.Control.ConfigureCharge(3, 2f);
            second.State.Values.SetI("Stack", 7);
            second.State.Values.SetI("FollowupUses", 4);

            Assert.AreSame(spec, first.Spec);
            Assert.AreSame(spec, second.Spec);
            Assert.AreNotSame(first.State, second.State);
            Assert.AreEqual(3, first.State.Values.GetI("Stack"));
            Assert.AreEqual(7, second.State.Values.GetI("Stack"));
            Assert.AreEqual(1, first.State.Values.GetI("FollowupUses"));
            Assert.AreEqual(4, second.State.Values.GetI("FollowupUses"));
            Assert.AreEqual(1, first.State.Charge.Current);
            Assert.AreEqual(3, second.State.Charge.Current);
            Assert.AreNotSame(first.State.Charge, second.State.Charge);
            Assert.IsFalse(first.State.IsCooldownReady);
            Assert.IsTrue(second.State.IsCooldownReady);

            first.State.Advance(0.5f);

            Assert.AreEqual(1, first.State.Charge.Current);
            Assert.AreEqual(3, second.State.Charge.Current);
            Assert.AreEqual(1, first.State.Values.GetI("FollowupUses"));
            Assert.AreEqual(4, second.State.Values.GetI("FollowupUses"));
        }

    #endregion

    #region S-2: 상태 제어와 시간 입력

        // ------------------------------------------------------------
        /// <summary>
        /// Cooldown의 시작, 감소, 초기화 제어를 검증한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_AbilityStateControl_Cooldown시작감소Reset()
        {
            var state = new AbilityState(new AbilityKey("Dash"));

            Assert.IsTrue(state.Control.TryBeginCooldown(5f));
            Assert.IsFalse(state.IsCooldownReady);
            Assert.IsTrue(state.Control.ReduceCooldown(2f));
            Assert.AreEqual(3f, state.Cooldown.RemainingTime, 0.0001f);
            Assert.IsTrue(state.Control.ResetCooldown());
            Assert.IsTrue(state.IsCooldownReady);
            Assert.IsFalse(state.Control.ResetCooldown());
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 음수 시간은 무시하고 비정상 시간은 거부하는지 검증한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_AbilityState_음수시간은무시하고비정상시간은거부()
        {
            var state = new AbilityState(new AbilityKey("Skill"));
            state.Control.ConfigureCharge(2, 1f);
            Assert.AreEqual(1f, state.Charge.RechargeDuration, 0.0001f);
            state.Control.TryConsumeCharge(1);
            state.Control.TryBeginCooldown(2f);
            state.Values.SetI("FollowupStage", 2);

            state.Advance(-5f);

            Assert.AreEqual(2f, state.Cooldown.RemainingTime, 0.0001f);
            Assert.AreEqual(1f, state.Charge.Recharge.RemainingTime, 0.0001f);
            Assert.AreEqual(2, state.Values.GetI("FollowupStage"));
            Assert.Throws<ArgumentOutOfRangeException>
            (
                () => state.Advance(float.NaN)
            );
            Assert.Throws<ArgumentOutOfRangeException>
            (
                () => state.Advance(float.PositiveInfinity)
            );
            Assert.AreEqual(2f, state.Cooldown.RemainingTime, 0.0001f);
            Assert.AreEqual(1, state.Charge.Current);
            Assert.AreEqual(2, state.Values.GetI("FollowupStage"));
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> 감소한 남은 시간이 float 정밀도로 0이 되면 일시정지 여부와 무관하게
        /// <br/> Cooldown이 종료되고 Revision이 한 번만 오르는지 검증한다.
        /// </summary>
        // ----------------------------------------------------------------------
        [TestCase(false)]
        [TestCase(true)]
        public void TEST_AbilityStateControl_ReduceCooldown은정밀도로남은시간이0이되면종료(bool paused)
        {
            var state = new AbilityState(new AbilityKey("Skill"));
            Assert.IsTrue(state.Control.TryBeginCooldown(1f));
            state.Advance(0.99999994f);
            if (paused)
            {
                Assert.IsTrue(state.Control.PauseCooldown());
            }

            var revision = state.Revision;

            // 남은 시간(약 6e-8)보다 작은 감소량이라도 Timer가 반영한 값이 0이면 종료한다.
            Assert.IsTrue(state.Control.ReduceCooldown(3e-8f));

            Assert.IsTrue(state.IsCooldownReady);
            Assert.IsFalse(state.Cooldown.IsRunning);
            Assert.IsFalse(state.Cooldown.IsPaused);
            Assert.AreEqual(revision + 1UL, state.Revision);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Pause로 진행을 멈추고 Resume으로 재개하는지 검증한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_AbilityStateControl_Pause는진행을멈추고Resume으로재개()
        {
            var state = new AbilityState(new AbilityKey("Skill"));
            Assert.IsFalse(state.Control.PauseCooldown());
            Assert.IsFalse(state.Control.ResumeCooldown());
            Assert.AreEqual(0UL, state.Revision);

            Assert.IsTrue(state.Control.TryBeginCooldown(5f));
            state.Advance(1f);
            Assert.IsTrue(state.Control.PauseCooldown());
            Assert.IsTrue(state.Cooldown.IsPaused);
            Assert.IsFalse(state.IsCooldownReady);

            state.Advance(10f);
            Assert.AreEqual(4f, state.Cooldown.RemainingTime, 0.0001f);

            Assert.IsTrue(state.Control.ResumeCooldown());
            state.Advance(1f);
            Assert.AreEqual(3f, state.Cooldown.RemainingTime, 0.0001f);
        }

    #endregion

    #region S-3: 진행 뒤 알림

        // ------------------------------------------------------------
        /// <summary>
        /// Cooldown 종료 알림에서 외부 사건이 후속 단계를 여는지 검증한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_AbilityState_Cooldown종료알림에서외부사건이후속단계를연다()
        {
            var state = new AbilityState(new AbilityKey("Skill"));
            state.Control.TryBeginCooldown(1f);
            state.OnCooldownEnded += _ => state.Values.SetI("FollowupStage", 2);

            state.Advance(1f);

            Assert.AreEqual(2, state.Values.GetI("FollowupStage"));
            state.Advance(100f);
            Assert.AreEqual(2, state.Values.GetI("FollowupStage"));
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// 알림은 Cooldown과 Charge 진행이 모두 끝난 상태에서 전달되는지 검증한다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_AbilityState_알림은진행이끝난상태로전달()
        {
            var state = new AbilityState(new AbilityKey("Skill"));
            state.Control.ConfigureCharge(2, 1f, startFull: false);
            Assert.IsTrue(state.Control.TryBeginCooldown(1f));
            var seenCurrent = -1;
            var seenRemaining = -1f;
            state.OnCooldownEnded += _ =>
            {
                seenCurrent = state.Charge.Current;
                seenRemaining = state.Charge.Recharge.RemainingTime;
            };

            state.Advance(1.5f);

            Assert.AreEqual(1, seenCurrent);
            Assert.AreEqual(0.5f, seenRemaining, 0.0001f);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> 알림 안에서 다시 진행하면 그 진행의 알림이 현재 구독자 안에서
        /// <br/> 먼저 전달되는지 검증한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_AbilityState_알림안의재진행알림은그자리에서먼저전달()
        {
            var state = new AbilityState(new AbilityKey("Skill"));
            state.Control.ConfigureCharge(2, 2f, startFull: false);
            Assert.IsTrue(state.Control.TryBeginCooldown(1f));
            var log = new List<string>();
            state.OnCooldownEnded += _ =>
            {
                log.Add("cooldown");
                state.Advance(1f);
                log.Add("cooldown-done");
            };
            state.OnChargeRecharged += (_, count) => log.Add("recharged:" + count);

            state.Advance(1f);

            Assert.AreEqual
            (
                new[]
                {
                    "cooldown",
                    "recharged:1",
                    "cooldown-done",
                },
                log
            );
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 모든 구독자에게 알린 뒤 여러 구독자 예외를 함께 전달하는지 검증한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_AbilityState_알림구독자예외를모두보낸뒤함께전달()
        {
            var state = new AbilityState(new AbilityKey("Burst"));
            state.Control.ConfigureCharge(2, 1f, startFull: false);
            Assert.IsTrue(state.Control.TryBeginCooldown(1f));
            var delivered = 0;
            state.OnCooldownEnded += _ =>
                throw new InvalidOperationException("cooldown failed");
            state.OnChargeRecharged += (_, _) =>
                throw new InvalidOperationException("recharge failed");
            state.OnChargeRecharged += (_, _) => delivered++;

            var error = Assert.Throws<AggregateException>(() => state.Advance(1f));

            Assert.AreEqual(2, error.InnerExceptions.Count);
            Assert.AreEqual(1, delivered);
            Assert.AreEqual(1, state.Charge.Current);
            Assert.IsTrue(state.Charge.Recharge.IsRunning);
            Assert.AreEqual(1f, state.Charge.Recharge.RemainingTime, 0.0001f);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> 쿨다운 알림은 진행이 끝난 뒤라 알림에서 새로 시작한 충전은
        /// <br/> 다음 Advance부터 진행하는지 검증한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_AbilityState_쿨다운알림에서시작한충전은다음Advance부터진행()
        {
            var state = new AbilityState(new AbilityKey("Skill"));
            state.Control.ConfigureCharge(2, 1f, startFull: true);
            Assert.IsTrue(state.Control.TryBeginCooldown(1f));
            var consumed = false;
            state.OnCooldownEnded += _ => consumed = state.Control.TryConsumeCharge(1);

            state.Advance(1f);

            Assert.IsTrue(consumed);
            Assert.AreEqual(1, state.Charge.Current);
            Assert.IsTrue(state.Charge.Recharge.IsRunning);
            Assert.AreEqual(1f, state.Charge.Recharge.RemainingTime, 0.0001f);
        }

    #endregion

    #region S-4: Revision

        // ------------------------------------------------------------
        /// <summary>
        /// 실제 변경마다 Revision이 한 번씩 증가하는지 검증한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_AbilityStateControl_실제변경마다Revision이한번씩증가()
        {
            var state = new AbilityState(new AbilityKey("Skill"));
            Assert.AreEqual(0UL, state.Revision);

            Assert.IsTrue(state.Control.TryBeginCooldown(5f));
            Assert.AreEqual(1UL, state.Revision);
            Assert.IsFalse(state.Control.TryBeginCooldown(5f));
            Assert.AreEqual(1UL, state.Revision);

            Assert.IsTrue(state.Control.ReduceCooldown(1f));
            Assert.AreEqual(2UL, state.Revision);
            Assert.IsFalse(state.Control.ReduceCooldown(0f));
            Assert.AreEqual(2UL, state.Revision);

            // float 정밀도보다 작은 감소량은 남은 시간을 바꾸지 않으므로 변경으로 세지 않는다.
            Assert.IsFalse(state.Control.ReduceCooldown(1e-8f));
            Assert.AreEqual(2UL, state.Revision);

            Assert.IsTrue(state.Control.PauseCooldown());
            Assert.AreEqual(3UL, state.Revision);
            Assert.IsFalse(state.Control.PauseCooldown());
            Assert.IsTrue(state.Control.ResumeCooldown());
            Assert.AreEqual(4UL, state.Revision);
            Assert.IsFalse(state.Control.ResumeCooldown());

            Assert.IsTrue(state.Control.ResetCooldown());
            Assert.AreEqual(5UL, state.Revision);
            Assert.IsFalse(state.Control.ResetCooldown());
            Assert.AreEqual(5UL, state.Revision);

            // 지속시간 0은 시작할 상태가 없으므로 성공해도 변경이 아니다.
            Assert.IsTrue(state.Control.TryBeginCooldown(0f));
            Assert.AreEqual(5UL, state.Revision);

            state.Control.ConfigureCharge(2, 1f);
            Assert.AreEqual(6UL, state.Revision);
            Assert.IsTrue(state.Control.TryConsumeCharge(1));
            Assert.AreEqual(7UL, state.Revision);
            Assert.IsFalse(state.Control.TryConsumeCharge(2));
            Assert.AreEqual(7UL, state.Revision);
            Assert.AreEqual(1, state.Control.RestoreCharge(1));
            Assert.AreEqual(8UL, state.Revision);
            Assert.AreEqual(0, state.Control.RestoreCharge(1));
            Assert.AreEqual(8UL, state.Revision);

            state.Control.ConfigureCharge(3, 1f);
            Assert.AreEqual(9UL, state.Revision);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Values의 실제 변경만 Revision에 한 번씩 반영되는지 검증한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_AbilityState_Values의실제변경만Revision에한번씩반영()
        {
            var state = new AbilityState(new AbilityKey("Skill"));

            // Values를 여러 번 읽어도 구독이 중복되지 않는다.
            var first = state.Values;
            var second = state.Values;
            Assert.AreSame(first, second);

            first.SetI("Stack", 1);
            Assert.AreEqual(1UL, state.Revision);
            second.SetI("Stack", 1);
            Assert.AreEqual(1UL, state.Revision);
            state.Values.SetS("Label", "saved");
            Assert.AreEqual(2UL, state.Revision);
            Assert.IsTrue(state.Values.Remove("Stack"));
            Assert.AreEqual(3UL, state.Revision);
            Assert.IsFalse(state.Values.Remove("Stack"));
            Assert.AreEqual(3UL, state.Revision);
            state.Values.Clear();
            Assert.AreEqual(4UL, state.Revision);
            state.Values.Clear();
            Assert.AreEqual(4UL, state.Revision);
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// Advance는 Revision을 바꾸지 않고 소비와 복원만 이산 변경으로 세는지 검증한다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_AbilityState_Advance는Revision을바꾸지않고소비복원만이산변경으로센다()
        {
            var state = new AbilityState(new AbilityKey("Skill"));
            state.Control.ConfigureCharge(3, 0.5f, startFull: false);
            Assert.IsTrue(state.Control.TryBeginCooldown(5f));
            var completed = 0;
            state.OnChargeRecharged += (_, count) => completed += count;
            var revision = state.Revision;

            for (var tick = 0; tick < 100; tick++)
            {
                state.Advance(0.1f);
            }

            // 충전 완료와 Cooldown 종료를 포함해도 시간 진행은 변경 번호를 올리지 않는다.
            Assert.AreEqual(3, completed);
            Assert.AreEqual(3, state.Charge.Current);
            Assert.IsTrue(state.IsCooldownReady);
            Assert.AreEqual(revision, state.Revision);

            Assert.IsTrue(state.Control.TryConsumeCharge(1));
            Assert.AreEqual(revision + 1, state.Revision);

            // 같은 Tick의 소비와 복원은 결과 수량이 같아도 각각 이산 변경이다.
            state.Advance(0.2f);
            var remaining = state.Charge.Recharge.RemainingTime;
            var current = state.Charge.Current;
            revision = state.Revision;

            Assert.IsTrue(state.Control.TryConsumeCharge(1));
            Assert.AreEqual(1, state.Control.RestoreCharge(1));

            Assert.AreEqual(revision + 2, state.Revision);
            Assert.AreEqual(current, state.Charge.Current);
            Assert.AreEqual(remaining, state.Charge.Recharge.RemainingTime, 0.0001f);
        }

    #endregion

    #region S-5: 사용 구간

        // ------------------------------------------------------------
        /// <summary>
        /// 사용 구간은 중첩을 거부하고 Advance를 막지 않는지 검증한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_AbilityState_사용구간은중첩을거부하고Advance를막지않음()
        {
            var state = new AbilityState(new AbilityKey("Skill"));
            state.Control.TryBeginCooldown(2f);

            state.EnterUse();
            Assert.Throws<InvalidOperationException>(() => state.EnterUse());
            state.Advance(1f);
            Assert.AreEqual(1f, state.Cooldown.RemainingTime, 0.0001f);

            state.ExitUse();
            state.EnterUse();
            state.ExitUse();
        }

    #endregion

    }
}
