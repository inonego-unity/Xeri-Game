/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : TEST_StatusContainer.cs
수정일 : 2026-11-23

# 설명
StatusContainer의 적용 규칙, 스택별 만료 순서, 소비·제거, 변경 알림, Revision과 복제·직렬화 계약을 검증한다.

# 테스트 구성
 A: Stacking·Sourcing·Overflow 적용 규칙, 적용별 지속 시간과 인자 검사
 T: 시간 진행과 만료 알림
 C: 소비와 제거
 P: Revision, 복제, Unity 직렬화
 G: 반복 호출 GC 할당
========================================================================= BLOCK_HEADER_END */

using System;
using System.Collections;
using System.Collections.Generic;

using UnityEngine;

using NUnit;
using NUnit.Framework;

namespace inonego.Xeri.Game.TEST._Status
{
    using Xeri;
    using Xeri.Game;
    using Xeri.Utility;

    // ============================================================
    /// <summary>
    /// StatusContainer의 적용·만료·소비·복제 계약 테스트 묶음.
    /// </summary>
    // ============================================================
    public sealed class TEST_StatusContainer
    {

    #region 테스트 데이터

        // 시전자 번호 하나로 구분하는 테스트 출처.
        [Serializable]
        private struct TestOrigin : IEquatable<TestOrigin>
        {
            internal int Caster => caster;

            [SerializeField]
            private int caster;

            internal TestOrigin(int caster)
            {
                this.caster = caster;
            }

            // ------------------------------------------------------------
            /// <summary>
            /// 시전자 번호가 같은지 비교한다.
            /// </summary>
            // ------------------------------------------------------------
            public bool Equals(TestOrigin other) => caster == other.caster;

            // ------------------------------------------------------------
            /// <summary>
            /// 객체가 같은 시전자의 출처인지 비교한다.
            /// </summary>
            // ------------------------------------------------------------
            public override bool Equals(object obj) => obj is TestOrigin other && Equals(other);

            // ------------------------------------------------------------
            /// <summary>
            /// 시전자 번호를 해시 코드로 반환한다.
            /// </summary>
            // ------------------------------------------------------------
            public override int GetHashCode() => caster;
        }

        // Unity 직렬화 왕복을 위해 컨테이너를 필드로 보유한다.
        [Serializable]
        private sealed class Holder
        {
            internal StatusContainer<TestOrigin> Statuses => statuses;

            [SerializeField]
            private StatusContainer<TestOrigin> statuses = new();
        }

        private static readonly TestOrigin originA = new(1);
        private static readonly TestOrigin originB = new(2);

        private static StatusSpec CreateSpec
        (
            string key,
            int maxStacks,
            float? duration,
            StatusStacking stacking,
            StatusSourcing sourcing = StatusSourcing.PerOrigin,
            StatusOverflow? overflow = null
        )
        {
            return new StatusSpec(new StatusKey(key), maxStacks, duration, stacking, sourcing, overflow);
        }

        private static List<StatusChange<TestOrigin>> Record(StatusContainer<TestOrigin> container)
        {
            var changes = new List<StatusChange<TestOrigin>>();
            container.OnChanged += (_, change) => changes.Add(change);
            return changes;
        }

        private static void AssertChange
        (
            StatusChange<TestOrigin> change,
            StatusChangeKind kind,
            string key,
            int stackDelta,
            int stacks,
            StatusRemoveReason reason = StatusRemoveReason.None
        )
        {
            Assert.AreEqual(kind, change.Kind);
            Assert.AreEqual(new StatusKey(key), change.Key);
            Assert.AreEqual(stackDelta, change.StackDelta);
            Assert.AreEqual(stacks, change.Stacks);
            Assert.AreEqual(reason, change.Reason);
        }

        private static string Describe(List<StatusChange<TestOrigin>> changes)
        {
            var parts = new List<string>();

            foreach (var change in changes)
            {
                parts.Add($"{change.Kind}:{change.Key}:{change.Origin.Caster}:{change.StackDelta}:{change.Stacks}:{change.Reason}");
            }

            return string.Join("|", parts);
        }

    #endregion

    #region A-1: Stacking 규칙

        // ------------------------------------------------------------
        /// <summary>
        /// Independent 스택이 최대치까지 쌓이고 각자 만료되는지 검증한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_StatusContainer_Independent스택은최대치까지쌓이고각자만료()
        {
            var container = new StatusContainer<TestOrigin>();
            var changes = Record(container);
            var spec = CreateSpec("Hisui", 3, 5f, StatusStacking.Independent);
            var key = spec.Key;

            Assert.AreEqual(StatusApplyOutcome.Added, container.Apply(spec, originA).Outcome);

            container.Advance(1f);

            Assert.AreEqual(StatusApplyOutcome.Stacked, container.Apply(spec, originA).Outcome);
            Assert.AreEqual(StatusApplyOutcome.Stacked, container.Apply(spec, originA).Outcome);
            Assert.AreEqual(StatusApplyOutcome.Rejected, container.Apply(spec, originA).Outcome);
            Assert.AreEqual(3, container.CountStacks(key));

            // 처음 스택만 먼저 끝난다.
            changes.Clear();
            container.Advance(4f);

            Assert.AreEqual(2, container.CountStacks(key));
            Assert.AreEqual(1, changes.Count);
            AssertChange(changes[0], StatusChangeKind.StacksRemoved, "Hisui", -1, 2, StatusRemoveReason.Expired);

            // 같은 시점에 끝나는 나머지 두 스택은 한 변경으로 묶인다.
            changes.Clear();
            container.Advance(1f);

            Assert.AreEqual(0, container.Statuses.Count);
            Assert.AreEqual(1, changes.Count);
            AssertChange(changes[0], StatusChangeKind.Removed, "Hisui", -2, 0, StatusRemoveReason.Expired);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// ReplaceEarliest가 가장 먼저 끝날 스택을 밀어내는지 검증한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_StatusContainer_ReplaceEarliest는가장먼저끝날스택을교체()
        {
            var container = new StatusContainer<TestOrigin>();
            var changes = Record(container);
            var spec = CreateSpec("Echo", 2, 5f, StatusStacking.Independent, overflow: StatusOverflow.ReplaceEarliest);

            container.Apply(spec, originA);
            container.Advance(1f);
            container.Apply(spec, originA);
            container.Advance(1f);

            changes.Clear();

            Assert.AreEqual(StatusApplyOutcome.Stacked, container.Apply(spec, originA).Outcome);
            Assert.AreEqual(2, changes.Count);
            AssertChange(changes[0], StatusChangeKind.StacksRemoved, "Echo", -1, 1, StatusRemoveReason.Overflowed);
            AssertChange(changes[1], StatusChangeKind.Stacked, "Echo", 1, 2);

            Assert.IsTrue(container.TryGet(spec.Key, originA, out var status));
            Assert.AreEqual(4f, status.GetStackTimer(0).RemainingTime, 0.0001f);
            Assert.AreEqual(5f, status.GetStackTimer(1).RemainingTime, 0.0001f);

            // 한 번에 최대치보다 많이 넣으면 최대 스택 수만큼만 새로 넣고 기존 스택을 모두 밀어낸다.
            changes.Clear();

            Assert.AreEqual(StatusApplyOutcome.Stacked, container.Apply(spec, originA, new StatusApply(5)).Outcome);
            Assert.AreEqual(2, changes.Count);
            AssertChange(changes[0], StatusChangeKind.StacksRemoved, "Echo", -2, 0, StatusRemoveReason.Overflowed);
            AssertChange(changes[1], StatusChangeKind.Stacked, "Echo", 2, 2);
            Assert.AreEqual(5f, status.GetStackTimer(0).RemainingTime, 0.0001f);
            Assert.AreEqual(5f, status.GetStackTimer(1).RemainingTime, 0.0001f);
        }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/> SharedRenew가 재적용마다 모든 스택 시간을 다시 시작하고
        /// <br/> 최대치에서는 Renewed로 갱신하며 Shared 출처를 최근 적용자로 바꾸는지 검증한다.
        /// </summary>
        // --------------------------------------------------------------------------------
        [Test]
        public void TEST_StatusContainer_SharedRenew는모든스택을재시작하고최근적용자를출처로()
        {
            var container = new StatusContainer<TestOrigin>();
            var changes = Record(container);
            var spec = CreateSpec("Mark", 2, 5f, StatusStacking.SharedRenew, StatusSourcing.PerKey);

            container.Apply(spec, originA);
            container.Advance(3f);

            Assert.AreEqual(StatusApplyOutcome.Stacked, container.Apply(spec, originB).Outcome);
            Assert.IsTrue(container.TryGet(spec.Key, originB, out var status));
            Assert.AreEqual(5f, status.GetStackTimer(0).RemainingTime, 0.0001f);
            Assert.AreEqual(5f, status.GetStackTimer(1).RemainingTime, 0.0001f);

            container.Advance(1f);
            changes.Clear();

            Assert.AreEqual(StatusApplyOutcome.Renewed, container.Apply(spec, originA).Outcome);
            Assert.AreEqual(1, container.Statuses.Count);
            Assert.AreEqual(originA, container.Statuses[0].Origin);
            Assert.AreEqual(5f, container.Statuses[0].GetStackTimer(0).RemainingTime, 0.0001f);
            AssertChange(changes[0], StatusChangeKind.Renewed, "Mark", 0, 2);
            Assert.AreEqual(originA, changes[0].Origin);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// SharedKeep의 새 스택이 기존 남은 시간에 함께 끝나는지 검증한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_StatusContainer_SharedKeep새스택은기존남은시간에함께만료()
        {
            var container = new StatusContainer<TestOrigin>();
            var changes = Record(container);
            var spec = CreateSpec("Buff", 3, 5f, StatusStacking.SharedKeep);

            container.Apply(spec, originA);
            container.Advance(2f);

            Assert.AreEqual(StatusApplyOutcome.Stacked, container.Apply(spec, originA, new StatusApply(2)).Outcome);
            Assert.AreEqual(StatusApplyOutcome.Rejected, container.Apply(spec, originA).Outcome);

            Assert.IsTrue(container.TryGet(spec.Key, originA, out var status));
            Assert.AreEqual(3f, status.GetStackTimer(2).RemainingTime, 0.0001f);

            changes.Clear();
            container.Advance(3f);

            Assert.AreEqual(1, changes.Count);
            AssertChange(changes[0], StatusChangeKind.Removed, "Buff", -3, 0, StatusRemoveReason.Expired);
        }

    #endregion

    #region A-2: Sourcing과 인자 검사

        // ------------------------------------------------------------
        /// <summary>
        /// PerOrigin Status가 출처마다 따로 쌓이는지 검증한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_StatusContainer_PerOrigin은출처마다분리()
        {
            var container = new StatusContainer<TestOrigin>();
            var spec = CreateSpec("Wound", 2, 5f, StatusStacking.Independent);

            Assert.AreEqual(StatusApplyOutcome.Added, container.Apply(spec, originA).Outcome);
            Assert.AreEqual(StatusApplyOutcome.Added, container.Apply(spec, originB).Outcome);
            Assert.AreEqual(StatusApplyOutcome.Stacked, container.Apply(spec, originA).Outcome);

            Assert.AreEqual(2, container.Statuses.Count);
            Assert.AreEqual(3, container.CountStacks(spec.Key));
            Assert.AreEqual(2, container.CountStacks(spec.Key, originA));
            Assert.AreEqual(1, container.CountStacks(spec.Key, originB));

            // 남은 자리보다 많이 넣으면 최대치까지만 더한다.
            Assert.AreEqual(StatusApplyOutcome.Stacked, container.Apply(spec, originB, new StatusApply(5)).Outcome);
            Assert.AreEqual(2, container.CountStacks(spec.Key, originB));
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> SharedRenew에서 Overflow를 Reject로 지정하면 자리가 있을 때는
        /// <br/> 스택을 더하며 모든 스택을 다시 시작하고,
        /// <br/> 가득 찼을 때는 거절하며 시간·Revision·알림을 바꾸지 않는지 검증한다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_StatusContainer_SharedRenew의Reject는가득차면시간도갱신하지않음()
        {
            var container = new StatusContainer<TestOrigin>();
            var spec = CreateSpec("Mark", 2, 5f, StatusStacking.SharedRenew, overflow: StatusOverflow.Reject);

            Assert.AreEqual(StatusApplyOutcome.Added, container.Apply(spec, originA).Outcome);
            container.Advance(2f);

            // 자리가 있으면 스택을 더하고 모든 스택을 다시 시작한다.
            Assert.AreEqual(StatusApplyOutcome.Stacked, container.Apply(spec, originA).Outcome);
            Assert.IsTrue(container.TryGet(spec.Key, originA, out var status));
            Assert.AreEqual(2, status.Stacks);
            Assert.AreEqual(5f, status.GetStackTimer(0).RemainingTime, 1e-4f);
            Assert.AreEqual(5f, status.GetStackTimer(1).RemainingTime, 1e-4f);

            container.Advance(2f);
            var revision = container.Revision;
            var changes = Record(container);

            // 가득 찼으면 거절하고 남은 시간·Revision·알림이 그대로다.
            Assert.AreEqual(StatusApplyOutcome.Rejected, container.Apply(spec, originA).Outcome);
            Assert.AreEqual(3f, status.GetStackTimer(0).RemainingTime, 1e-4f);
            Assert.AreEqual(revision, container.Revision);
            Assert.AreEqual(0, changes.Count);
        }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/> Overflow 기본값이 스택 규칙을 따르고, Renew는 SharedRenew에서만 유효하며,
        /// <br/> 같은 Key에 Overflow가 다른 SharedRenew를 적용하면 규칙 충돌로 거절되는지 검증한다.
        /// </summary>
        // --------------------------------------------------------------------------------
        [Test]
        public void TEST_StatusContainer_Overflow기본값은규칙을따르고Renew는SharedRenew에서만유효()
        {
            Assert.AreEqual(StatusOverflow.Renew, CreateSpec("A", 2, 5f, StatusStacking.SharedRenew).Overflow);
            Assert.AreEqual(StatusOverflow.Reject, CreateSpec("B", 2, 5f, StatusStacking.Independent).Overflow);
            Assert.AreEqual(StatusOverflow.Reject, CreateSpec("C", 2, 5f, StatusStacking.SharedKeep).Overflow);

            Assert.Throws<ArgumentException>(() => CreateSpec("D", 2, 5f, StatusStacking.Independent, overflow: StatusOverflow.Renew));
            Assert.Throws<ArgumentException>(() => CreateSpec("E", 2, 5f, StatusStacking.SharedKeep, overflow: StatusOverflow.Renew));

            var container = new StatusContainer<TestOrigin>();
            container.Apply(CreateSpec("Mark", 2, 5f, StatusStacking.SharedRenew), originA);

            var rejecting = CreateSpec("Mark", 2, 5f, StatusStacking.SharedRenew, overflow: StatusOverflow.Reject);
            Assert.Throws<ArgumentException>(() => container.Apply(rejecting, originA));
        }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/> GetStackTimer가 스택별 시간과 공유 시간 Status 모두에서
        /// <br/> 범위 밖 인덱스를 ArgumentOutOfRangeException으로 거절하는지 검증한다.
        /// </summary>
        // --------------------------------------------------------------------------------
        [Test]
        public void TEST_StatusContainer_GetStackTimer는범위밖인덱스를거절()
        {
            var container = new StatusContainer<TestOrigin>();
            var shared = CreateSpec("Brand", 2, 5f, StatusStacking.SharedRenew);
            var independent = CreateSpec("Echo", 2, 5f, StatusStacking.Independent);

            container.Apply(shared, originA);
            container.Apply(independent, originA);

            Assert.IsTrue(container.TryGet(shared.Key, originA, out var sharedStatus));
            Assert.IsTrue(container.TryGet(independent.Key, originA, out var independentStatus));

            AssertRejectsOutOfRange(sharedStatus);
            AssertRejectsOutOfRange(independentStatus);
        }

        // 유효하지 않은 인덱스(음수, 스택 수 이상)는 거절하고 유효한 인덱스는 시간을 돌려준다.
        private static void AssertRejectsOutOfRange(IReadOnlyStatus<TestOrigin> status)
        {
            Assert.IsNotNull(status.GetStackTimer(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => status.GetStackTimer(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => status.GetStackTimer(status.Stacks));
        }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/> Apply 결과가 처리 종류와 함께 늘어난 스택 수, 최대치 처리로 밀려난 스택 수,
        /// <br/> 거절 여부를 담는지 검증한다.
        /// </summary>
        // --------------------------------------------------------------------------------
        [Test]
        public void TEST_StatusContainer_Apply결과는늘어난스택과밀려난스택수를담음()
        {
            var container = new StatusContainer<TestOrigin>();
            var replacing = CreateSpec("Echo", 3, 5f, StatusStacking.Independent, overflow: StatusOverflow.ReplaceEarliest);

            var added = container.Apply(replacing, originA, new StatusApply(2));
            Assert.AreEqual(StatusApplyOutcome.Added, added.Outcome);
            Assert.AreEqual(2, added.StacksAdded);
            Assert.AreEqual(0, added.StacksReplaced);

            // 최대치를 넘는 만큼 가장 먼저 끝날 스택을 밀어낸다.
            var replaced = container.Apply(replacing, originA, new StatusApply(2));
            Assert.AreEqual(StatusApplyOutcome.Stacked, replaced.Outcome);
            Assert.AreEqual(2, replaced.StacksAdded);
            Assert.AreEqual(1, replaced.StacksReplaced);

            var rejecting = CreateSpec("Mark", 1, 5f, StatusStacking.Independent);
            container.Apply(rejecting, originA);

            var rejected = container.Apply(rejecting, originA);
            Assert.IsTrue(rejected.IsRejected);
            Assert.AreEqual(0, rejected.StacksAdded);
            Assert.AreEqual(0, rejected.StacksReplaced);

            // 시간만 갱신한 적용은 늘어난 스택이 없다.
            var renewing = CreateSpec("Brand", 1, 5f, StatusStacking.SharedRenew);
            container.Apply(renewing, originA);

            var renewed = container.Apply(renewing, originA);
            Assert.AreEqual(StatusApplyOutcome.Renewed, renewed.Outcome);
            Assert.AreEqual(0, renewed.StacksAdded);
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> Spec 동등성이 동작에 쓰이지 않는 값(지속 시간이 없을 때의 Duration,
        /// <br/> 스택 규칙에서 쓰이지 않는 Overflow 값)을 비교하지 않는지 검증한다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_StatusContainer_Spec동등성은쓰이지않는값을비교하지않음()
        {
            var container = new StatusContainer<TestOrigin>();
            var renew = CreateSpec("Mark", 2, 5f, StatusStacking.SharedRenew);
            var renewWithOverflow = CreateSpec("Mark", 2, 5f, StatusStacking.SharedRenew, overflow: StatusOverflow.ReplaceEarliest);
            var independent = CreateSpec("Echo", 2, 5f, StatusStacking.Independent);
            var independentWithOverflow = CreateSpec("Echo", 2, 5f, StatusStacking.Independent, overflow: StatusOverflow.ReplaceEarliest);

            Assert.AreEqual(renew, renewWithOverflow);
            Assert.AreEqual(renew.GetHashCode(), renewWithOverflow.GetHashCode());
            Assert.AreNotEqual(independent, independentWithOverflow);

            // 쓰이지 않는 값만 다른 Spec은 같은 Key에 그대로 적용된다.
            container.Apply(renew, originA);

            Assert.AreEqual(StatusApplyOutcome.Stacked, container.Apply(renewWithOverflow, originA).Outcome);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 잘못된 Spec·인자와 규칙 충돌이 상태 변경 전에 거절되는지 검증한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_StatusContainer_잘못된인자와규칙충돌은상태변경전예외()
        {
            var container = new StatusContainer<TestOrigin>();
            var spec = CreateSpec("Mark", 2, 5f, StatusStacking.Independent);
            var conflicting = CreateSpec("Mark", 3, 5f, StatusStacking.Independent);

            container.Apply(spec, originA);
            var revision = container.Revision;

            Assert.Throws<ArgumentException>(() => container.Apply(conflicting, originB));
            Assert.Throws<ArgumentNullException>(() => container.Apply(null, originA));
            Assert.Throws<ArgumentOutOfRangeException>(() => container.Apply(spec, originA, new StatusApply(0)));
            Assert.Throws<ArgumentOutOfRangeException>(() => container.Consume(spec.Key, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => container.Advance(float.NaN));

            Assert.AreEqual(revision, container.Revision);
            Assert.AreEqual(1, container.CountStacks(spec.Key));

            Assert.Throws<ArgumentException>(() => CreateSpec("Mark", 0, 5f, StatusStacking.Independent));
            Assert.Throws<ArgumentException>(() => CreateSpec("Mark", 1, 0f, StatusStacking.Independent));
        }

    #endregion

    #region A-3: 적용별 지속 시간

        // ----------------------------------------------------------------------
        /// <summary>
        /// Independent에서 지정 지속 시간이 이번 적용의 새 스택에만 쓰이는지 검증한다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_StatusContainer_지정지속시간은Independent새스택에만적용()
        {
            var container = new StatusContainer<TestOrigin>();
            var changes = Record(container);
            var spec = CreateSpec("Bleed", 3, 5f, StatusStacking.Independent);

            Assert.AreEqual(StatusApplyOutcome.Added, container.Apply(spec, originA, new StatusApply(duration: 2f)).Outcome);
            Assert.AreEqual(StatusApplyOutcome.Stacked, container.Apply(spec, originA).Outcome);

            Assert.IsTrue(container.TryGet(spec.Key, originA, out var status));
            Assert.AreEqual(5f, status.Spec.Duration);
            Assert.AreEqual(2f, status.GetStackTimer(0).Duration, 0.0001f);
            Assert.AreEqual(5f, status.GetStackTimer(1).Duration, 0.0001f);

            changes.Clear();
            container.Advance(2f);

            Assert.AreEqual(1, container.CountStacks(spec.Key));
            AssertChange(changes[0], StatusChangeKind.StacksRemoved, "Bleed", -1, 1, StatusRemoveReason.Expired);
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> SharedRenew가 최근 적용의 지정 지속 시간으로 모든 스택을 다시 시작하고
        /// <br/> 더 짧은 지정값이면 남은 시간도 줄어드는지 검증한다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_StatusContainer_SharedRenew는최근적용의지정지속시간으로재시작()
        {
            var container = new StatusContainer<TestOrigin>();
            var spec = CreateSpec("Mark", 2, 5f, StatusStacking.SharedRenew, StatusSourcing.PerKey);

            container.Apply(spec, originA);
            container.Advance(1f);

            Assert.AreEqual(StatusApplyOutcome.Stacked, container.Apply(spec, originB, new StatusApply(duration: 2f)).Outcome);
            Assert.AreEqual(2f, container.Statuses[0].GetStackTimer(0).RemainingTime, 0.0001f);
            Assert.AreEqual(2f, container.Statuses[0].GetStackTimer(1).RemainingTime, 0.0001f);

            // 지정하지 않은 갱신은 다시 Spec 지속 시간을 쓴다.
            Assert.AreEqual(StatusApplyOutcome.Renewed, container.Apply(spec, originA).Outcome);
            Assert.AreEqual(5f, container.Statuses[0].GetStackTimer(0).RemainingTime, 0.0001f);
            Assert.AreEqual(5f, container.Statuses[0].GetStackTimer(1).RemainingTime, 0.0001f);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// SharedKeep이 처음 적용에만 지정 지속 시간을 쓰는지 검증한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_StatusContainer_SharedKeep은첫적용에만지정지속시간사용()
        {
            var container = new StatusContainer<TestOrigin>();
            var spec = CreateSpec("Buff", 3, 5f, StatusStacking.SharedKeep);

            Assert.AreEqual(StatusApplyOutcome.Added, container.Apply(spec, originA, new StatusApply(duration: 2f)).Outcome);
            container.Advance(0.5f);

            // 더한 스택은 지정값과 관계없이 기존 스택과 같은 시점에 끝난다.
            Assert.AreEqual(StatusApplyOutcome.Stacked, container.Apply(spec, originA, new StatusApply(duration: 10f)).Outcome);
            Assert.IsTrue(container.TryGet(spec.Key, originA, out var status));
            Assert.AreEqual(1.5f, status.GetStackTimer(0).RemainingTime, 0.0001f);
            Assert.AreEqual(1.5f, status.GetStackTimer(1).RemainingTime, 0.0001f);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 잘못된 지정 지속 시간이 상태 변경 전에 거절되는지 검증한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_StatusContainer_잘못된지정지속시간은상태변경전예외()
        {
            var container = new StatusContainer<TestOrigin>();
            var timed = CreateSpec("Mark", 2, 5f, StatusStacking.Independent);
            var untimed = CreateSpec("Brand", 1, null, StatusStacking.Independent);

            container.Apply(timed, originA);
            var revision = container.Revision;

            Assert.Throws<ArgumentException>(() => container.Apply(untimed, originA, new StatusApply(duration: 3f)));
            Assert.Throws<ArgumentOutOfRangeException>(() => container.Apply(timed, originA, new StatusApply(duration: 0f)));
            Assert.Throws<ArgumentOutOfRangeException>(() => container.Apply(timed, originA, new StatusApply(duration: float.NaN)));
            Assert.Throws<ArgumentOutOfRangeException>(() => container.Apply(timed, originA, new StatusApply(duration: float.PositiveInfinity)));

            Assert.AreEqual(revision, container.Revision);
            Assert.AreEqual(1, container.CountStacks(timed.Key));
            Assert.AreEqual(0, container.CountStacks(untimed.Key));
        }

    #endregion

    #region T-1: 만료 순서와 알림

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> 한 Advance에서 여러 Status가 끝나면 남은 시간, 같으면 적용 순번 순서로
        /// <br/> 알리고 알림 시점에는 모든 만료가 이미 반영되어 있는지 검증한다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_StatusContainer_동시만료는남은시간과적용순번순서로알림()
        {
            var container = new StatusContainer<TestOrigin>();
            var x = CreateSpec("X", 3, 3f, StatusStacking.Independent);
            var y = CreateSpec("Y", 3, 2f, StatusStacking.Independent);
            var observedStacks = new List<int>();

            container.Apply(x, originA);
            container.Apply(y, originA);
            container.Advance(1f);
            container.Apply(y, originA);

            var changes = Record(container);
            container.OnChanged += (sender, _) => observedStacks.Add(sender.CountStacks(x.Key) + sender.CountStacks(y.Key));

            container.Advance(5f);

            // Y(남은 1) → X(남은 2, 순번 1) → Y(남은 2, 순번 3)
            Assert.AreEqual(3, changes.Count);
            AssertChange(changes[0], StatusChangeKind.StacksRemoved, "Y", -1, 1, StatusRemoveReason.Expired);
            AssertChange(changes[1], StatusChangeKind.Removed, "X", -1, 0, StatusRemoveReason.Expired);
            AssertChange(changes[2], StatusChangeKind.Removed, "Y", -1, 0, StatusRemoveReason.Expired);

            // 알림은 진행과 정리가 모두 끝난 뒤에 나간다.
            Assert.AreEqual(3, observedStacks.Count);
            Assert.That(observedStacks, Is.All.EqualTo(0));
        }

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> 알림 중 Apply를 허용하고, 구독자 예외가 나도 다른 구독자에게
        /// <br/> 보낸 뒤 그 예외를 전달하는지 검증한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_StatusContainer_알림중Apply허용과구독자예외격리()
        {
            var container = new StatusContainer<TestOrigin>();
            var burn = CreateSpec("Burn", 1, 1f, StatusStacking.Independent);
            var scar = CreateSpec("Scar", 1, null, StatusStacking.Independent);
            var received = 0;

            container.Apply(burn, originA);

            container.OnChanged += (_, change) =>
            {
                if (change.Kind == StatusChangeKind.Removed && change.Key == burn.Key)
                {
                    container.Apply(scar, change.Origin);
                }
            };
            container.OnChanged += (_, change) =>
            {
                if (change.Key == burn.Key)
                {
                    throw new InvalidOperationException("handler");
                }
            };
            container.OnChanged += (_, _) => received++;

            Assert.Throws<InvalidOperationException>(() => container.Advance(1f));

            Assert.AreEqual(0, container.CountStacks(burn.Key));
            Assert.AreEqual(1, container.CountStacks(scar.Key, originA));

            // 알림 중 적용한 Scar와 만료된 Burn 모두 마지막 구독자에게 전달된다.
            Assert.AreEqual(2, received);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 지속 시간이 없는 Status는 시간으로 끝나지 않는지 검증한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_StatusContainer_지속시간없는Status는Remove로만종료()
        {
            var container = new StatusContainer<TestOrigin>();
            var changes = Record(container);
            var spec = CreateSpec("Brand", 1, null, StatusStacking.Independent);

            container.Apply(spec, originA);
            container.Advance(1000f);

            Assert.AreEqual(1, container.CountStacks(spec.Key));
            Assert.IsFalse(container.Statuses[0].GetStackTimer(0).IsRunning);

            changes.Clear();

            Assert.IsTrue(container.Remove(spec.Key));
            AssertChange(changes[0], StatusChangeKind.Removed, "Brand", -1, 0, StatusRemoveReason.Removed);
        }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// 여러 구독자가 실패하면 모두 보낸 뒤 AggregateException으로 전달하는지 검증한다.
        /// </summary>
        // --------------------------------------------------------------------------------
        [Test]
        public void TEST_StatusContainer_여러구독자실패는모아서전달()
        {
            var container = new StatusContainer<TestOrigin>();
            var spec = CreateSpec("Mark", 1, 5f, StatusStacking.Independent);
            var received = 0;

            container.OnChanged += (_, _) => throw new InvalidOperationException("first");
            container.OnChanged += (_, _) => throw new ArgumentException("second");
            container.OnChanged += (_, _) => received++;

            var thrown = Assert.Throws<AggregateException>(() => container.Apply(spec, originA));

            Assert.AreEqual(2, thrown.InnerExceptions.Count);
            Assert.AreEqual(1, received);
            Assert.AreEqual(1, container.CountStacks(spec.Key));
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 구독자 예외 하나가 원래 스택 추적을 유지한 채 전달되는지 검증한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_StatusContainer_구독자예외하나는원래스택추적유지()
        {
            var container = new StatusContainer<TestOrigin>();
            var spec = CreateSpec("Mark", 1, 5f, StatusStacking.Independent);

            container.OnChanged += ThrowFromHandler;

            var thrown = Assert.Throws<InvalidOperationException>(() => container.Apply(spec, originA));

            Assert.That(thrown.StackTrace, Does.Contain(nameof(ThrowFromHandler)));
        }

        private static void ThrowFromHandler(StatusContainer<TestOrigin> sender, StatusChange<TestOrigin> change)
        {
            throw new InvalidOperationException("handler");
        }

    #endregion

    #region C-1: 소비와 제거

        // ------------------------------------------------------------
        /// <summary>
        /// Consume이 모든 출처에서 가장 먼저 끝날 스택부터 줄이는지 검증한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_StatusContainer_Consume은가장먼저끝날스택부터()
        {
            var container = new StatusContainer<TestOrigin>();
            var spec = CreateSpec("Charge", 5, 10f, StatusStacking.Independent);

            container.Apply(spec, originA);
            container.Advance(1f);
            container.Apply(spec, originB);
            container.Advance(1f);
            container.Apply(spec, originA);

            var changes = Record(container);

            // 남은 시간 A 8, B 9, A 10 중 앞의 둘을 줄인다.
            Assert.AreEqual(2, container.Consume(spec.Key, 2));
            Assert.AreEqual(2, changes.Count);
            AssertChange(changes[0], StatusChangeKind.StacksRemoved, "Charge", -1, 1, StatusRemoveReason.Consumed);
            Assert.AreEqual(originA, changes[0].Origin);
            AssertChange(changes[1], StatusChangeKind.Removed, "Charge", -1, 0, StatusRemoveReason.Consumed);
            Assert.AreEqual(originB, changes[1].Origin);

            Assert.IsTrue(container.TryGet(spec.Key, originA, out var status));
            Assert.AreEqual(10f, status.GetStackTimer(0).RemainingTime, 0.0001f);

            Assert.AreEqual(1, container.Consume(spec.Key, originA, 5));
            Assert.AreEqual(0, container.Statuses.Count);

            var revision = container.Revision;

            Assert.AreEqual(0, container.Consume(spec.Key, 1));
            Assert.AreEqual(revision, container.Revision);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Remove와 Clear의 대상·반환값·알림 순서를 검증한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_StatusContainer_Remove와Clear는대상만적용순서대로제거()
        {
            var container = new StatusContainer<TestOrigin>();
            var mark = CreateSpec("Mark", 3, 5f, StatusStacking.Independent);
            var slow = CreateSpec("Slow", 1, 5f, StatusStacking.Independent);

            container.Apply(mark, originA, new StatusApply(2));
            container.Apply(mark, originB);
            container.Apply(slow, originA);

            var changes = Record(container);

            Assert.IsFalse(container.Remove(slow.Key, originB));
            Assert.IsTrue(container.Remove(mark.Key, originB));
            Assert.AreEqual(2, container.CountStacks(mark.Key));

            container.Clear();

            Assert.AreEqual(0, container.Statuses.Count);
            Assert.AreEqual(3, changes.Count);
            AssertChange(changes[0], StatusChangeKind.Removed, "Mark", -1, 0, StatusRemoveReason.Removed);
            AssertChange(changes[1], StatusChangeKind.Removed, "Mark", -2, 0, StatusRemoveReason.Cleared);
            AssertChange(changes[2], StatusChangeKind.Removed, "Slow", -1, 0, StatusRemoveReason.Cleared);
        }

    #endregion

    #region P-1: Revision

        // ------------------------------------------------------------
        /// <summary>
        /// Revision이 이산 변경에서만 오르는지 검증한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_StatusContainer_Revision은이산변경에서만증가()
        {
            var container = new StatusContainer<TestOrigin>();
            var spec = CreateSpec("Mark", 1, 1f, StatusStacking.Independent);

            container.Clear();
            Assert.AreEqual(Revision.None, container.Revision);

            container.Apply(spec, originA);
            Assert.AreEqual(Revision.FromUInt64(1), container.Revision);

            container.Apply(spec, originA);
            Assert.AreEqual(Revision.FromUInt64(1), container.Revision);

            Assert.IsFalse(container.Remove(spec.Key, originB));
            Assert.AreEqual(Revision.FromUInt64(1), container.Revision);

            // 시간 만료는 시간에서 결정되므로 변경 번호를 바꾸지 않는다.
            container.Advance(1f);
            Assert.AreEqual(0, container.Statuses.Count);
            Assert.AreEqual(Revision.FromUInt64(1), container.Revision);

            container.Apply(spec, originA);
            container.Consume(spec.Key, 1);
            Assert.AreEqual(Revision.FromUInt64(3), container.Revision);
        }

    #endregion

    #region P-2: 복제와 직렬화

        // ----------------------------------------------------------------------
        /// <summary>
        /// CloneFrom이 알림 없이 복제하고 같은 진행에서 같은 만료를 만드는지 검증한다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_StatusContainer_CloneFrom은알림없이복제하고같은만료를재현()
        {
            var source = new StatusContainer<TestOrigin>();
            var spec = CreateSpec("Mark", 3, 3f, StatusStacking.Independent);

            source.Apply(spec, originA);
            source.Advance(1f);
            source.Apply(spec, originB);
            source.Apply(spec, originA);

            var target = new StatusContainer<TestOrigin>();
            var targetChanges = Record(target);
            var revision = target.Revision;

            target.CloneFrom(source);

            Assert.AreEqual(0, targetChanges.Count);
            Assert.AreEqual(revision.Inc(), target.Revision);
            Assert.AreEqual(3, target.CountStacks(spec.Key));

            var sourceChanges = Record(source);

            source.Advance(2f);
            target.Advance(2f);
            source.Advance(1f);
            target.Advance(1f);

            Assert.AreEqual(Describe(sourceChanges), Describe(targetChanges));
            Assert.AreEqual(0, target.Statuses.Count);
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> CloneFrom이 기존 Status를 source 구성으로 바꾸고
        /// <br/> 복제본을 source와 독립으로 만드는지 검증한다.
        /// <br/> 복제 전에 읽어 둔 Status 항목은 새로 만든 항목과 다른 인스턴스다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_StatusContainer_CloneFrom은기존Status를교체하고복제본은source와독립()
        {
            var mark = CreateSpec("Mark", 3, 3f, StatusStacking.Independent);
            var brand = CreateSpec("Brand", 1, null, StatusStacking.SharedKeep, StatusSourcing.PerKey);

            var source = new StatusContainer<TestOrigin>();
            source.Apply(mark, originA);

            var target = new StatusContainer<TestOrigin>();
            target.Apply(brand, originB);
            var stale = target.Statuses[0];

            target.CloneFrom(source);

            Assert.AreEqual(1, target.Statuses.Count);
            Assert.AreEqual(mark.Key, target.Statuses[0].Spec.Key);
            Assert.AreEqual(0, target.CountStacks(brand.Key));
            Assert.AreNotSame(stale, target.Statuses[0]);

            // 복제본을 바꿔도 source는 그대로다.
            target.Remove(mark.Key);

            Assert.AreEqual(0, target.Statuses.Count);
            Assert.AreEqual(1, source.CountStacks(mark.Key));
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> Unity 직렬화 왕복이 Spec 복사본·출처·남은 시간·적용 순번을 보존하고
        /// <br/> 복원 뒤 Revision을 올리는지 검증한다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_StatusContainer_Unity직렬화왕복은상태와순번을보존()
        {
            var holder = new Holder();
            var source = holder.Statuses;
            var mark = CreateSpec("Mark", 3, 3f, StatusStacking.Independent);
            var brand = CreateSpec("Brand", 1, null, StatusStacking.SharedKeep, StatusSourcing.PerKey);

            source.Apply(mark, originA);
            source.Advance(1f);
            source.Apply(mark, originB);
            source.Apply(brand, originB);

            var restored = JsonUtility.FromJson<Holder>(JsonUtility.ToJson(holder)).Statuses;

            Assert.IsFalse(restored.Revision.IsNone);
            Assert.AreEqual(3, restored.Statuses.Count);
            Assert.AreEqual(mark, restored.Statuses[0].Spec);
            Assert.AreNotSame(mark, restored.Statuses[0].Spec);
            Assert.AreEqual(originB, restored.Statuses[2].Origin);
            Assert.AreEqual(2f, restored.Statuses[0].GetStackTimer(0).RemainingTime, 0.0001f);
            Assert.AreEqual(3f, restored.Statuses[1].GetStackTimer(0).RemainingTime, 0.0001f);

            // 복원 뒤 새 스택도 이어지는 순번을 받아 같은 순서로 만료된다.
            source.Apply(mark, originA);
            restored.Apply(mark, originA);

            var sourceChanges = Record(source);
            var restoredChanges = Record(restored);

            source.Advance(5f);
            restored.Advance(5f);

            Assert.AreEqual(Describe(sourceChanges), Describe(restoredChanges));
            Assert.AreEqual(1, restored.Statuses.Count);
        }

    #endregion

    #region G-1: 반복 호출 GC 할당

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> 구독자가 없을 때 갱신 적용·소비·일부 만료를 반복해도 GC 할당이 없는지
        /// <br/> 검증한다. 새 Status·새 스택 생성과 구독자 호출 목록은 대상이 아니다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_StatusContainer_갱신적용소비만료반복은GC할당없음()
        {
            var container = new StatusContainer<TestOrigin>();
            var renew = CreateSpec("Mark", 2, 50f, StatusStacking.SharedRenew);
            var keep = CreateSpec("Charge", 5, 50f, StatusStacking.SharedKeep);
            var hit = CreateSpec("Hit", 5, 1f, StatusStacking.Independent);
            var leaked = new List<object>();

            container.Apply(renew, originA, new StatusApply(2));
            container.Apply(keep, originA, new StatusApply(5));

            // 남은 시간이 0.5, 0.75, 1인 독립 스택을 만든다.
            container.Apply(hit, originA);
            container.Advance(0.25f);
            container.Apply(hit, originA);
            container.Advance(0.25f);
            container.Apply(hit, originA);

            // 첫 반복에서 ListPool이 빌려 줄 목록을 만든다.
            container.Apply(renew, originA);
            container.Consume(keep.Key, 1);
            container.Advance(0.5f);

            // 계측이 실제로 할당을 잡는지 먼저 확인한다.
            Assert.That(() => leaked.Add(new object()), new UnityEngine.TestTools.Constraints.AllocatingGCMemoryConstraint());

            Assert.That
            (
                () =>
                {
                    container.Apply(renew, originA);
                    container.Consume(keep.Key, 1);
                    container.Advance(0.25f);
                },
                UnityEngine.TestTools.Constraints.ConstraintExtensions.AllocatingGCMemory(Is.Not)
            );

            Assert.AreEqual(3, container.CountStacks(keep.Key));
            Assert.AreEqual(1, container.CountStacks(hit.Key));
        }

    #endregion

    }
}
