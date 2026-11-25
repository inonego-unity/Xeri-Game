/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : TEST_AbilityStateSet.cs
수정일 : 2026-11-24

# 설명
AbilityStateSet의 Key 조회·추가·제거, 일괄 진행의 구성원 규칙과 실패 수집, 직렬화 복원 보정과 CloneFrom·Clone의 Key 기준 복제, 복제 실패 정책(새 State 실패는 불변, 덮어쓰기 실패는 구성 맞춤 뒤 전달)을 검증한다.

# 테스트 구성
 E: 조회·추가·제거
 V: 일괄 진행
 P: Unity 직렬화 복원
 C: CloneFrom·Clone
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

    // ======================================================================
    /// <summary>
    /// AbilityStateSet의 구성원 관리와 일괄 진행 계약을 검증하는 테스트 묶음.
    /// </summary>
    // ======================================================================
    public sealed class TEST_AbilityStateSet
    {

    #region 테스트 데이터

        private static readonly AbilityKey dash = new("Dash");
        private static readonly AbilityKey slash = new("Slash");
        private static readonly AbilityKey guard = new("Guard");
        private static readonly AbilityKey parry = new("Parry");

        // 컬렉션 복제가 부르는 인터페이스를 다시 구현해 새 State 생성과 덮어쓰기를 일부러 실패시키는 State.
        private sealed class FaultyState : AbilityState, IDeepCloneable<AbilityState>
        {
            internal FaultyState(AbilityKey key) : base(key)
            {
                // NONE
            }

            AbilityState IDeepCloneable<AbilityState>.@new()
            {
                throw new InvalidOperationException("의도한 State 생성 실패");
            }

            void IDeepCloneableFrom<AbilityState>.CloneFrom(AbilityState source)
            {
                throw new InvalidOperationException("의도한 State 덮어쓰기 실패");
            }
        }

        // Cooldown을 시작한 State를 만든다.
        private static AbilityState CreateCooling(AbilityStateSet set, AbilityKey key, float duration)
        {
            var state = set.GetOrAdd(key);
            Assert.IsTrue(state.TryBeginCooldown(duration));
            return state;
        }

    #endregion

    #region E-1: 조회·추가·제거

        // ------------------------------------------------------------
        /// <summary>
        /// GetOrAdd·Add·TryGet이 Key마다 State 하나를 유지하는지 검증한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_AbilityStateSet_Key마다State하나를유지()
        {
            var set = new AbilityStateSet();
            var created = set.GetOrAdd(dash);

            Assert.AreSame(created, set.GetOrAdd(dash));
            Assert.IsTrue(set.TryGet(dash, out var found));
            Assert.AreSame(created, found);

            var configured = new AbilityState(slash);
            set.Add(configured);

            Assert.AreEqual(2, set.Count);
            Assert.AreSame(created, set.States[0]);
            Assert.AreSame(configured, set.States[1]);

            Assert.Throws<ArgumentException>(() => set.Add(new AbilityState(dash)));
            Assert.Throws<ArgumentException>(() => set.Add(new AbilityState()));
            Assert.Throws<ArgumentNullException>(() => set.Add(null));
            Assert.Throws<ArgumentException>(() => set.GetOrAdd(default));
            Assert.IsFalse(set.TryGet(default, out _));
            Assert.IsFalse(set.Remove(default));
            Assert.AreEqual(2, set.Count);
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// Remove가 없는 Key는 false, 사용 중인 State는 예외로 거절하는지 검증한다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_AbilityStateSet_Remove는사용중State를거절()
        {
            var set = new AbilityStateSet();
            var state = set.GetOrAdd(dash);

            Assert.IsFalse(set.Remove(slash));

            state.EnterUse();

            try
            {
                Assert.Throws<InvalidOperationException>(() => set.Remove(dash));
                Assert.AreEqual(1, set.Count);
            }
            finally
            {
                state.ExitUse();
            }

            Assert.IsTrue(set.Remove(dash));
            Assert.AreEqual(0, set.Count);
            Assert.IsFalse(set.TryGet(dash, out _));
        }

    #endregion

    #region V-1: 일괄 진행

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> 진행 중 알림에서 제거된 State는 건너뛰고 추가된 State는
        /// <br/> 이번 진행에 넣지 않는지 검증한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_AbilityStateSet_진행중제거는건너뛰고추가는다음진행부터()
        {
            var set = new AbilityStateSet();
            var first = CreateCooling(set, dash, 1f);
            var second = CreateCooling(set, slash, 5f);
            AbilityState added = null;

            first.OnCooldownEnded += _ =>
            {
                set.Remove(slash);
                added = CreateCooling(set, guard, 5f);
            };

            set.Advance(1f);

            // 제거된 State는 진행하지 않았고 추가된 State도 이번 진행을 받지 않았다.
            Assert.AreEqual(5f, second.Cooldown.RemainingTime, 0.0001f);
            Assert.AreEqual(5f, added.Cooldown.RemainingTime, 0.0001f);

            set.Advance(1f);

            Assert.AreEqual(4f, added.Cooldown.RemainingTime, 0.0001f);
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> 한 State의 알림 예외가 나머지 진행을 막지 않고, 하나면 그대로,
        /// <br/> 여러 개면 AggregateException으로 모두 진행한 뒤 전달하는지 검증한다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_AbilityStateSet_State실패는모두진행한뒤전달()
        {
            var set = new AbilityStateSet();
            var first = CreateCooling(set, dash, 1f);
            var second = CreateCooling(set, slash, 1f);
            var third = CreateCooling(set, guard, 3f);

            first.OnCooldownEnded += _ => throw new InvalidOperationException("first");

            Assert.Throws<InvalidOperationException>(() => set.Advance(1f));
            Assert.IsTrue(second.IsCooldownReady);
            Assert.AreEqual(2f, third.Cooldown.RemainingTime, 0.0001f);

            Assert.IsTrue(first.TryBeginCooldown(1f));
            Assert.IsTrue(second.TryBeginCooldown(1f));
            second.OnCooldownEnded += _ => throw new ArgumentException("second");

            var thrown = Assert.Throws<AggregateException>(() => set.Advance(1f));

            Assert.AreEqual(2, thrown.InnerExceptions.Count);
            Assert.AreEqual(1f, third.Cooldown.RemainingTime, 0.0001f);

            Assert.Throws<ArgumentOutOfRangeException>(() => set.Advance(float.NaN));
        }

    #endregion

    #region P-1: Unity 직렬화 복원

        // 직렬화 왕복을 위해 집합을 필드로 보유한다.
        [Serializable]
        private sealed class Holder
        {
            internal AbilityStateSet Set => set;

            [SerializeField]
            private AbilityStateSet set = new();
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> 직렬화 복원이 State와 Cooldown을 보존하고 Key 조회를 다시 만들며
        /// <br/> Key 없음·중복 Key State는 첫 항목만 남기는지 검증한다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_AbilityStateSet_직렬화복원은조회를재구성하고중복을보정()
        {
            var source = new Holder();
            CreateCooling(source.Set, dash, 3f);
            source.Set.GetOrAdd(slash);

            var restored = JsonUtility.FromJson<Holder>(JsonUtility.ToJson(source));

            Assert.AreEqual(2, restored.Set.Count);
            Assert.IsTrue(restored.Set.TryGet(dash, out var restoredDash));
            Assert.AreEqual(3f, restoredDash.Cooldown.RemainingTime, 0.0001f);

            // 저장 데이터에 중복 Key와 Key 없는 State가 섞여도 첫 항목만 남는다.
            var json = JsonUtility.ToJson(source);
            var slashJson = JsonUtility.ToJson(source.Set.States[1]);
            var dashJson = JsonUtility.ToJson(source.Set.States[0]);
            var emptyJson = JsonUtility.ToJson(new AbilityState());
            var corrupted = json.Replace(slashJson, $"{slashJson},{dashJson},{emptyJson}");

            Assert.AreNotEqual(json, corrupted);

            var fixedUp = JsonUtility.FromJson<Holder>(corrupted);

            Assert.AreEqual(2, fixedUp.Set.Count);
            Assert.AreEqual(dash, fixedUp.Set.States[0].Key);
            Assert.AreEqual(slash, fixedUp.Set.States[1].Key);
            Assert.IsTrue(fixedUp.Set.TryGet(dash, out var keptDash));
            Assert.AreSame(fixedUp.Set.States[0], keptDash);
        }

    #endregion

    #region C-1: CloneFrom·Clone

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/> Key가 같은 State는 인스턴스를 유지한 채 덮어쓰고, source에만 있는 Key는 추가하며,
        /// <br/> 대상에만 있는 Key는 제거하고 순서는 source를 따르는지 검증한다.
        /// </summary>
        // --------------------------------------------------------------------------------
        [Test]
        public void TEST_AbilityStateSet_CloneFrom은Key기준으로인스턴스를유지하며덮어씀()
        {
            var source = new AbilityStateSet();
            CreateCooling(source, slash, 3f);
            CreateCooling(source, dash, 5f);

            var target = new AbilityStateSet();
            var targetDash = CreateCooling(target, dash, 1f);
            var targetGuard = target.GetOrAdd(guard);
            var revision = targetDash.Revision;

            target.CloneFrom(source);

            Assert.AreEqual(2, target.Count);
            Assert.AreEqual(slash, target.States[0].Key);
            Assert.AreSame(targetDash, target.States[1]);
            Assert.AreEqual(5f, targetDash.Cooldown.Duration, 1e-4f);
            Assert.IsTrue(targetDash.Revision > revision);
            Assert.IsFalse(target.TryGet(guard, out _));
            Assert.IsTrue(target.TryGet(slash, out var added));
            Assert.AreNotSame(source.States[0], added);
            Assert.AreEqual(3f, added.Cooldown.Duration, 1e-4f);
            Assert.AreSame(targetDash, target.States[1]);
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// 사용 중인 State가 있으면 아무것도 바꾸지 않고 예외를 던지는지 검증한다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_AbilityStateSet_CloneFrom은사용중State가있으면아무것도바꾸지않음()
        {
            var source = new AbilityStateSet();
            CreateCooling(source, dash, 5f);

            var target = new AbilityStateSet();
            var targetDash = CreateCooling(target, dash, 1f);
            var targetGuard = target.GetOrAdd(guard);
            targetGuard.EnterUse();

            try
            {
                Assert.Throws<InvalidOperationException>(() => target.CloneFrom(source));
            }
            finally
            {
                targetGuard.ExitUse();
            }

            Assert.AreEqual(2, target.Count);
            Assert.IsTrue(target.TryGet(guard, out var keptGuard));
            Assert.AreSame(targetGuard, keptGuard);
            Assert.AreEqual(1f, targetDash.Cooldown.Duration, 1e-4f);
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> source에 있는 새 Key의 State를 만들다 실패하면 준비 단계에서 멈추므로
        /// <br/> 대상 집합의 구성원·순서·내용이 그대로인지 검증한다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_AbilityStateSet_CloneFrom은새State복제가실패하면집합을바꾸지않음()
        {
            var source = new AbilityStateSet();
            CreateCooling(source, dash, 5f);
            source.Add(new FaultyState(slash));

            var target = new AbilityStateSet();
            var targetDash = CreateCooling(target, dash, 1f);
            var targetGuard = target.GetOrAdd(guard);
            var revision = targetDash.Revision;

            Assert.Throws<InvalidOperationException>(() => target.CloneFrom(source));

            Assert.AreEqual(2, target.Count);
            Assert.AreSame(targetDash, target.States[0]);
            Assert.AreSame(targetGuard, target.States[1]);
            Assert.AreEqual(1f, targetDash.Cooldown.Duration, 1e-4f);
            Assert.AreEqual(revision, targetDash.Revision);
            Assert.IsFalse(target.TryGet(slash, out _));
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> 기존 State를 덮어쓰다 실패해도 나머지를 끝까지 덮어쓰고 Key 구성과 순서를
        /// <br/> source에 맞춘 뒤 실패를 그대로 전달하는지 검증한다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_AbilityStateSet_CloneFrom은덮어쓰기가실패해도나머지를맞추고실패를전달()
        {
            var source = new AbilityStateSet();
            CreateCooling(source, dash, 5f);
            source.Add(new FaultyState(slash));
            source.GetOrAdd(parry);

            var target = new AbilityStateSet();
            var faulty = new FaultyState(slash);
            target.Add(faulty);
            var targetDash = CreateCooling(target, dash, 1f);
            target.GetOrAdd(guard);

            var thrown = Assert.Throws<InvalidOperationException>(() => target.CloneFrom(source));

            Assert.AreEqual("의도한 State 덮어쓰기 실패", thrown.Message);

            // 실패와 관계없이 Key 구성과 순서는 source를 따른다.
            Assert.AreEqual(3, target.Count);
            Assert.AreSame(targetDash, target.States[0]);
            Assert.AreSame(faulty, target.States[1]);
            Assert.AreEqual(parry, target.States[2].Key);
            Assert.IsFalse(target.TryGet(guard, out _));

            // 실패하지 않은 State는 덮어써진다.
            Assert.AreEqual(5f, targetDash.Cooldown.Duration, 1e-4f);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> Clone은 같은 Key의 독립된 State 사본을 같은 순서로 만들고
        /// <br/> null·자기 자신 복제를 처리하는지 검증한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_AbilityStateSet_Clone은독립사본을만들고null과자기자신을처리()
        {
            var source = new AbilityStateSet();
            var sourceDash = CreateCooling(source, dash, 5f);
            source.GetOrAdd(slash);

            var clone = source.Clone();

            Assert.AreEqual(2, clone.Count);
            Assert.AreEqual(dash, clone.States[0].Key);
            Assert.AreEqual(slash, clone.States[1].Key);
            Assert.AreNotSame(sourceDash, clone.States[0]);
            Assert.AreEqual(5f, clone.States[0].Cooldown.Duration, 1e-4f);

            clone.States[0].ResetCooldown();
            Assert.IsTrue(sourceDash.Cooldown.IsRunning);

            Assert.Throws<ArgumentNullException>(() => source.CloneFrom(null));
            Assert.DoesNotThrow(() => source.CloneFrom(source));
            Assert.AreEqual(2, source.Count);
        }

    #endregion

    }
}
