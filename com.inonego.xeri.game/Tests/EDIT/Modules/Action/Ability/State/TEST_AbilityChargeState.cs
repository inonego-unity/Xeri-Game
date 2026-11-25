/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : TEST_AbilityChargeState.cs
수정일 : 2026-11-23

# 설명
Charge의 충전 시간 진행과 ChargeState 복원·경계 계약, 운영 중 구성 변경과 충전 단축, 지불 영수증 환급이 수량과 충전 시계를 지불 전 상태로 되돌리는지 검증한다.

# 테스트 구성
 C: Charge 충전 진행·복원·운영 중 구성 변경과 충전 단축
 R: 지불 영수증 환급
========================================================================= BLOCK_HEADER_END */

using System;
using System.Collections;
using System.Collections.Generic;

using UnityEngine;

using NUnit;
using NUnit.Framework;

namespace inonego.Xeri.Game.TEST._Ability
{
    using Xeri;
    using Xeri.Game;
    using Xeri.Utility;

    // ============================================================
    /// <summary>
    /// Charge 충전 진행과 복원 계약을 검증하는 테스트 묶음.
    /// </summary>
    // ============================================================
    public sealed class TEST_AbilityChargeState
    {

    #region C-1: 충전 시간 진행

        // ------------------------------------------------------------
        /// <summary>
        /// Charge가 Cooldown과 독립적으로 순차 충전되는지 검증한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_AbilityChargeState_Cooldown과독립적으로순차Recharge()
        {
            var state = new AbilityState(new AbilityKey("Burst"));
            state.ConfigureCharge(3, 1f);
            state.TryBeginCooldown(5f);

            Assert.IsTrue(state.TryConsumeCharge(2));
            Assert.AreEqual(1, state.Charge.Current);

            state.Advance(1f);

            Assert.AreEqual(2, state.Charge.Current);
            Assert.AreEqual(4f, state.Cooldown.RemainingTime, 0.0001f);

            state.Advance(1f);

            Assert.AreEqual(3, state.Charge.Current);
            Assert.IsTrue(state.Charge.IsFull);
            Assert.AreEqual(3f, state.Cooldown.RemainingTime, 0.0001f);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 큰 Delta에서 여러 Charge를 연속으로 복구하는지 검증한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_AbilityChargeState_큰Delta에서여러Charge를연속복구()
        {
            var state = new AbilityState(new AbilityKey("Burst"));
            state.ConfigureCharge(3, 1f);

            Assert.IsTrue(state.TryConsumeCharge(3));
            Assert.AreEqual(0, state.Charge.Current);

            var ended = 0;
            state.OnChargeRecharged += (_, count) => ended += count;
            state.Advance(2.5f);

            Assert.AreEqual(2, state.Charge.Current);
            Assert.AreEqual(2, ended);
            Assert.AreEqual(0.5f, state.Charge.Recharge.RemainingTime, 0.0001f);

            var large = new AbilityState(new AbilityKey("Large"));
            large.ConfigureCharge(1_000_001, 1f, startFull: false);
            large.Advance(1_000_000.5f);
            Assert.AreEqual(1_000_000, large.Charge.Current);
            Assert.AreEqual(0.5f, large.Charge.Recharge.RemainingTime, 0.0001f);
        }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/> 부분 진행한 충전을 남은 시간보다 큰 Delta로 넘길 때 float 반올림으로 경과 시간
        /// <br/> 합이 지속 시간에 못 미쳐도 한 주기만 완료하고 다음 주기를 이어 가는지 검증한다.
        /// </summary>
        // --------------------------------------------------------------------------------
        [Test]
        public void TEST_AbilityChargeState_반올림으로경과합이모자라도한주기만완료()
        {
            // float에서 0.0343 + (0.7 - 0.0343)이 0.7보다 작아지는 값이다.
            var state = new AbilityState(new AbilityKey("Burst"));
            state.ConfigureCharge(3, 0.7f);
            Assert.IsTrue(state.TryConsumeCharge(2));
            state.Advance(0.0343f);

            var recharged = 0;
            state.OnChargeRecharged += (_, count) => recharged += count;
            state.Advance(0.7f);

            Assert.AreEqual(2, state.Charge.Current);
            Assert.AreEqual(1, recharged);
            Assert.IsTrue(state.Charge.Recharge.IsRunning);
            Assert.AreEqual(0.7f - 0.0343f, state.Charge.Recharge.RemainingTime, 0.0001f);
        }

    #endregion

    #region C-2: ChargeState 복원 및 경계

        // ------------------------------------------------------------
        /// <summary>
        /// 진행 중 Restore가 충전 경과 시간을 유지하는지 검증한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_AbilityChargeState_진행중Restore는충전경과시간을유지()
        {
            var state = new AbilityState(new AbilityKey("Burst"));
            state.ConfigureCharge(3, 1f);
            Assert.IsTrue(state.TryConsumeCharge(3));

            state.Advance(0.4f);
            Assert.AreEqual(1, state.AddCharge(1));
            Assert.AreEqual(1, state.Charge.Current);
            Assert.AreEqual(0.6f, state.Charge.Recharge.RemainingTime, 0.0001f);

            state.Advance(0.6f);
            Assert.AreEqual(2, state.Charge.Current);
            Assert.AreEqual(1f, state.Charge.Recharge.RemainingTime, 0.0001f);

            Assert.AreEqual(1, state.AddCharge(2));
            Assert.IsTrue(state.Charge.IsFull);
            Assert.IsFalse(state.Charge.Recharge.IsRunning);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 최대 정수 용량에서도 Restore가 넘치지 않는지 검증한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_AbilityChargeState_최대정수용량에서도Restore가넘치지않음()
        {
            var state = new AbilityState(new AbilityKey("Burst"));
            state.ConfigureCharge(int.MaxValue, 1f, startFull: false);

            Assert.AreEqual(int.MaxValue, state.AddCharge(int.MaxValue));
            Assert.AreEqual(int.MaxValue, state.Charge.Current);
            Assert.IsTrue(state.TryConsumeCharge(1));
            Assert.AreEqual(1, state.AddCharge(int.MaxValue));
            Assert.AreEqual(int.MaxValue, state.Charge.Current);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 실패한 초기 구성이 상태를 생성하지 않는지 검증한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_AbilityChargeState_실패한초기구성은상태를생성하지않음()
        {
            var state = new AbilityState(new AbilityKey("Burst"));

            Assert.Throws<ArgumentOutOfRangeException>
            (
                () => state.ConfigureCharge(0, 1f)
            );
            Assert.IsNull(state.Charge);

            state.ConfigureCharge(2, 1f);
            state.TryConsumeCharge(1);

            Assert.Throws<ArgumentOutOfRangeException>
            (
                () => state.ConfigureCharge(3, float.NaN)
            );
            Assert.AreEqual(2, state.Charge.Capacity);
            Assert.AreEqual(1, state.Charge.Current);
        }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/> 다시 구성해도 Charge와 충전 Timer 인스턴스를 유지해 보관한 읽기 참조가 유효하고
        /// <br/> 충전은 새 구성으로 처음부터 시작하는지 검증한다.
        /// </summary>
        // --------------------------------------------------------------------------------
        [Test]
        public void TEST_AbilityChargeState_재구성은인스턴스를유지하고충전을새로시작()
        {
            var state = new AbilityState(new AbilityKey("Burst"));
            state.ConfigureCharge(3, 1f);
            Assert.IsTrue(state.TryConsumeCharge(1));
            state.Advance(0.4f);
            var charge = state.Charge;
            var recharge = state.Charge.Recharge;

            state.ConfigureCharge(2, 3f, startFull: false);

            Assert.AreSame(charge, state.Charge);
            Assert.AreSame(recharge, state.Charge.Recharge);
            Assert.AreEqual(0, charge.Current);
            Assert.IsTrue(recharge.IsRunning);
            Assert.AreEqual(3f, recharge.RemainingTime, 0.0001f);

            state.Advance(3f);
            Assert.AreEqual(1, charge.Current);
            Assert.AreEqual(3f, recharge.RemainingTime, 0.0001f);

            state.ConfigureCharge(2, 3f);
            Assert.IsTrue(charge.IsFull);
            Assert.IsFalse(recharge.IsRunning);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> 범위를 벗어난 저장 값을 역직렬화하면 구성 규칙의 범위로 고치고
        /// <br/> 필요한 충전을 이어서 시작하는지 검증한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_AbilityChargeState_역직렬화는범위밖저장값을구성규칙으로보정()
        {
            var overfilled = JsonUtility.FromJson<AbilityChargeState>
            (
                "{\"capacity\":0,\"current\":5,\"rechargeDuration\":0}"
            );

            Assert.AreEqual(1, overfilled.Capacity);
            Assert.AreEqual(1, overfilled.Current);
            Assert.AreEqual(1f, overfilled.RechargeDuration);
            Assert.IsFalse(overfilled.Recharge.IsRunning);

            var negative = JsonUtility.FromJson<AbilityChargeState>
            (
                "{\"capacity\":3,\"current\":-2,\"rechargeDuration\":-1}"
            );

            Assert.AreEqual(3, negative.Capacity);
            Assert.AreEqual(0, negative.Current);
            Assert.AreEqual(1f, negative.RechargeDuration);
            Assert.IsTrue(negative.Recharge.IsRunning);
            Assert.AreEqual(1f, negative.Recharge.RemainingTime, 0.0001f);
        }

    #endregion

    #region C-3: 운영 중 구성 변경과 충전 단축

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/> ResizeCharge가 현재 수량과 충전 경과 시간을 유지하고, 용량이 줄면 수량을 줄이며
        /// <br/> 늘어도 수량을 지급하지 않고, 같은 구성이면 바뀌지 않는지 검증한다.
        /// </summary>
        // --------------------------------------------------------------------------------
        [Test]
        public void TEST_AbilityChargeState_Resize는현재수와충전진행을유지()
        {
            var state = new AbilityState(new AbilityKey("Burst"));
            state.ConfigureCharge(3, 2f);
            Assert.IsTrue(state.TryConsumeCharge(2));
            state.Advance(0.5f);
            var charge = state.Charge;
            var recharge = charge.Recharge;
            var revision = state.Revision;

            Assert.IsTrue(state.ResizeCharge(4, 4f));
            Assert.AreSame(charge, state.Charge);
            Assert.AreEqual(4, charge.Capacity);
            Assert.AreEqual(1, charge.Current);
            Assert.AreEqual(4f, recharge.Duration, 0.0001f);
            Assert.AreEqual(0.5f, recharge.ElapsedTime, 0.0001f);
            Assert.AreEqual(revision.Inc(), state.Revision);

            Assert.IsFalse(state.ResizeCharge(4, 4f));
            Assert.AreEqual(revision.Inc(), state.Revision);

            // 용량이 현재 수량 이하로 줄면 가득 찬 상태가 되어 충전이 멈춘다.
            Assert.IsTrue(state.ResizeCharge(1, 4f));
            Assert.AreEqual(1, charge.Current);
            Assert.IsFalse(recharge.IsRunning);

            // 용량이 늘어도 수량은 지급하지 않고 새 충전만 시작한다.
            Assert.IsTrue(state.ResizeCharge(2, 4f));
            Assert.AreEqual(1, charge.Current);
            Assert.IsTrue(recharge.IsRunning);
            Assert.AreEqual(4f, recharge.RemainingTime, 0.0001f);
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> 새 충전 주기가 이미 지난 경과 시간 이하면 그 주기를 알림 없이 완료하고
        /// <br/> 다음 주기를 새 구성으로 시작하는지 검증한다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_AbilityChargeState_Resize로주기가경과시간보다짧아지면그주기를완료()
        {
            var state = new AbilityState(new AbilityKey("Burst"));
            state.ConfigureCharge(3, 2f);
            Assert.IsTrue(state.TryConsumeCharge(2));
            state.Advance(1.5f);
            var notified = 0;
            state.OnChargeRecharged += (_, _) => notified++;

            Assert.IsTrue(state.ResizeCharge(3, 1f));

            Assert.AreEqual(2, state.Charge.Current);
            Assert.IsTrue(state.Charge.Recharge.IsRunning);
            Assert.AreEqual(1f, state.Charge.Recharge.RemainingTime, 0.0001f);
            Assert.AreEqual(0, notified);
        }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/> ReduceRecharge가 현재 주기의 남은 시간만 줄이고, 소진하면 알림 없이 완료하며
        /// <br/> 초과분은 다음 주기로 넘기지 않는지 검증한다.
        /// </summary>
        // --------------------------------------------------------------------------------
        [Test]
        public void TEST_AbilityChargeState_ReduceRecharge는남은시간만줄이고초과분을넘기지않음()
        {
            var state = new AbilityState(new AbilityKey("Burst"));
            state.ConfigureCharge(3, 2f);
            Assert.IsTrue(state.TryConsumeCharge(2));
            var notified = 0;
            state.OnChargeRecharged += (_, _) => notified++;
            var revision = state.Revision;

            Assert.IsTrue(state.ReduceRecharge(0.5f));
            Assert.AreEqual(1, state.Charge.Current);
            Assert.AreEqual(1.5f, state.Charge.Recharge.RemainingTime, 0.0001f);
            Assert.AreEqual(revision.Inc(), state.Revision);

            Assert.IsTrue(state.ReduceRecharge(10f));
            Assert.AreEqual(2, state.Charge.Current);
            Assert.AreEqual(2f, state.Charge.Recharge.RemainingTime, 0.0001f);
            Assert.AreEqual(revision.Inc().Inc(), state.Revision);
            Assert.AreEqual(0, notified);

            // 줄일 것이 없으면 바뀌지 않는다.
            Assert.IsFalse(state.ReduceRecharge(0f));
            Assert.AreEqual(1, state.AddCharge(1));
            Assert.IsFalse(state.ReduceRecharge(1f));
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Charge가 없으면 false, 잘못된 인자는 예외인지 검증한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_AbilityChargeState_Resize와ReduceRecharge인자와미구성처리()
        {
            var unconfigured = new AbilityState(new AbilityKey("Plain"));

            Assert.IsFalse(unconfigured.ResizeCharge(2, 1f));
            Assert.IsFalse(unconfigured.ReduceRecharge(1f));
            Assert.IsNull(unconfigured.Charge);

            var state = new AbilityState(new AbilityKey("Burst"));
            state.ConfigureCharge(2, 1f);
            var revision = state.Revision;

            Assert.Throws<ArgumentOutOfRangeException>(() => state.ResizeCharge(0, 1f));
            Assert.Throws<ArgumentOutOfRangeException>(() => state.ResizeCharge(2, 0f));
            Assert.Throws<ArgumentOutOfRangeException>(() => state.ResizeCharge(2, float.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => state.ReduceRecharge(-1f));
            Assert.Throws<ArgumentOutOfRangeException>(() => state.ReduceRecharge(float.PositiveInfinity));

            Assert.AreEqual(revision, state.Revision);
            Assert.AreEqual(2, state.Charge.Capacity);
        }

    #endregion

    #region R-1: 지불 영수증 환급

        private static AbilityState CreateState(int capacity = 3)
        {
            var state = new AbilityState(new AbilityKey("Burst"));
            state.ConfigureCharge(capacity, 1f);
            return state;
        }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// 가득 찬 상태에서 지불한 Charge를 환급하면 수량과 충전 시계가 지불 전과 같은지 검증한다.
        /// </summary>
        // --------------------------------------------------------------------------------
        [Test]
        public void TEST_AbilityState_PayCharge환급은가득찬상태를정확히복원()
        {
            var state = CreateState(3);
            var recharge = state.Charge.Recharge;
            var beforeJson = JsonUtility.ToJson(state.Charge);
            var revision = state.Revision;

            var receipt = state.PayCharge(1);

            Assert.AreEqual(2, state.Charge.Current);
            Assert.IsTrue(state.Charge.Recharge.IsRunning);
            Assert.AreEqual(revision.Inc(), state.Revision);

            receipt.Refund();

            Assert.AreEqual(beforeJson, JsonUtility.ToJson(state.Charge));
            Assert.AreEqual(3, state.Charge.Current);
            Assert.IsFalse(state.Charge.Recharge.IsRunning);
            Assert.AreSame(recharge, state.Charge.Recharge);
            Assert.AreEqual(revision.Inc().Inc(), state.Revision);
        }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// 충전 시계가 진행 중이던 상태의 환급이 경과 시간까지 지불 전으로 되돌리는지 검증한다.
        /// </summary>
        // --------------------------------------------------------------------------------
        [Test]
        public void TEST_AbilityState_PayCharge환급은충전시계진행도지불전으로복원()
        {
            var state = CreateState(3);
            Assert.IsTrue(state.TryConsumeCharge(1));
            state.Advance(0.4f);
            var beforeJson = JsonUtility.ToJson(state.Charge);

            var receipt = state.PayCharge(2);
            Assert.AreEqual(0, state.Charge.Current);

            receipt.Refund();

            Assert.AreEqual(beforeJson, JsonUtility.ToJson(state.Charge));
            Assert.AreEqual(2, state.Charge.Current);
            Assert.IsTrue(state.Charge.Recharge.IsRunning);
            Assert.AreEqual(0.6f, state.Charge.Recharge.RemainingTime, 0.0001f);
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// 지불 뒤 Charge가 바뀌었으면 환급이 예외를 던지고 상태를 바꾸지 않는지 검증한다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_AbilityState_PayCharge환급은지불후변경을감지하면거부()
        {
            var advanced = CreateState(3);
            var advancedReceipt = advanced.PayCharge(1);
            advanced.Advance(0.1f);
            var advancedRevision = advanced.Revision;

            Assert.Throws<InvalidOperationException>(() => advancedReceipt.Refund());
            Assert.AreEqual(2, advanced.Charge.Current);
            Assert.AreEqual(advancedRevision, advanced.Revision);

            var restored = CreateState(3);
            var restoredReceipt = restored.PayCharge(2);
            Assert.AreEqual(1, restored.AddCharge(1));

            Assert.Throws<InvalidOperationException>(() => restoredReceipt.Refund());
            Assert.AreEqual(2, restored.Charge.Current);

            var configured = CreateState(3);
            var configuredReceipt = configured.PayCharge(1);
            configured.ConfigureCharge(5, 1f);

            Assert.Throws<InvalidOperationException>(() => configuredReceipt.Refund());
            Assert.AreEqual(5, configured.Charge.Capacity);

            var resized = CreateState(3);
            var resizedReceipt = resized.PayCharge(1);
            Assert.IsTrue(resized.ResizeCharge(4, 1f));

            Assert.Throws<InvalidOperationException>(() => resizedReceipt.Refund());
            Assert.AreEqual(2, resized.Charge.Current);

            var reduced = CreateState(3);
            var reducedReceipt = reduced.PayCharge(1);
            Assert.IsTrue(reduced.ReduceRecharge(0.5f));

            Assert.Throws<InvalidOperationException>(() => reducedReceipt.Refund());
            Assert.AreEqual(2, reduced.Charge.Current);
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// 지불할 수 없으면 예외를 던지고 Charge와 Revision을 바꾸지 않는지 검증한다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_AbilityState_PayCharge는지불불가시상태를바꾸지않음()
        {
            var state = CreateState(1);
            Assert.IsTrue(state.TryConsumeCharge(1));
            var revision = state.Revision;

            Assert.Throws<InvalidOperationException>(() => state.PayCharge(1));
            Assert.AreEqual(0, state.Charge.Current);
            Assert.AreEqual(revision, state.Revision);

            var empty = new AbilityState(new AbilityKey("None"));
            Assert.Throws<InvalidOperationException>(() => empty.PayCharge(1));
            Assert.AreEqual(Revision.None, empty.Revision);
        }

    #endregion

    }
}
