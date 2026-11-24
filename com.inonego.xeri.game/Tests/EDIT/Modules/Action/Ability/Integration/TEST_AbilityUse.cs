/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : TEST_AbilityUse.cs
수정일 : 2026-11-20

# 설명
사용 선택의 현재성, 다중 Cost의 지불·환급, 사용 출처, 독립 결과 수명과 중복 적중을 검증한다.
사용 식별은 Origin이 아니라 Host가 부여한 실행 ID로 확인한다.
배타 실행 교체는 TEST_AbilityExecutionChannel이 검증한다.

# 테스트 구성
 U: 선택 현재성과 다중 Cost
 R: 독립 결과와 출처
 H: 적중 결과
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
    using Xeri.Spatial;
    using Xeri.Game;
    using Xeri.Game.TEST;
    using Xeri.Game.TEST._Ability;
    using Xeri.Game.TEST._Ability.Fixture;

    // ============================================================
    /// <summary>
    /// 사용 선택·비용과 실행 수명에서 독립된 사용 결과를 검증한다.
    /// </summary>
    // ============================================================
    public sealed class TEST_AbilityUse
    {

    #region 테스트 Host 구성

        private sealed class Request : IDisposable
        {
            internal readonly AbilityInstance<object> Ability;
            internal readonly AbilityBindings<object> Bindings = new();
            internal readonly AbilityEntryKey Entry;
            private readonly Lease layerLease;
            private readonly ulong owner;
            private readonly AbilityUseSelector<object, int, string> selector;

            // ------------------------------------------------------------
            /// <summary>
            /// Host가 마지막으로 시작한 실행에 부여한 ID를 반환한다.
            /// </summary>
            // ------------------------------------------------------------
            internal ulong LastExecutionID => issued;
            private ulong issued;

            // ------------------------------------------------------------
            /// <summary>
            /// Host가 시작한 실행의 수를 반환한다.
            /// </summary>
            // ------------------------------------------------------------
            internal int Starts { get; private set; }

            internal Request
            (
                string key, ulong owner, AbilityActivationPolicy<object> policy = null,
                float cooldown = 0f, int charges = 0
            )
            {
                this.owner = owner;
                issued = owner * 100;
                Entry = new AbilityEntryKey("Use");
                Ability = new AbilityInstance<object>(new object(), new AbilityState(new AbilityKey(key)));
                if (charges > 0)
                {
                    Ability.State.Control.ConfigureCharge(charges, 10f);
                }

                var layer = new AbilityBindingLayer<object>(0);
                layer.Set(Entry, Ability);
                layerLease = Bindings.AcquireLayer(layer);
                void StartCooldown()
                {
                    if (!Ability.State.Control.TryBeginCooldown(cooldown))
                    {
                        throw new InvalidOperationException("Cooldown 시작이 승인 후 실패했습니다.");
                    }
                }

                Action transition = null;
                if (cooldown > 0f)
                {
                    transition = StartCooldown;
                }

                var selected = new AbilityUseChoice<object, string>
                (
                    key, policy ?? new AbilityActivationPolicy<object>(), transition: transition
                );
                selector = new AbilityUseSelector<object, int, string>
                (
                    new Func<AbilityActivationContext<object>, int, AbilityUseChoice<object, string>>[]
                    {
                        (_, _) => selected
                    }
                );
            }

            // ------------------------------------------------------------
            /// <summary>
            /// Fixture가 획득한 Binding Layer를 해제한다.
            /// </summary>
            // ------------------------------------------------------------
            public void Dispose()
            {
                layerLease.Release();
            }

            // ------------------------------------------------------------
            /// <summary>
            /// 현재 Binding으로 Entry를 해석해 사용을 선택한다.
            /// </summary>
            // ------------------------------------------------------------
            internal AbilityUseSelection<object, string> Select()
            {
                Assert.IsTrue(Bindings.TryResolve(Entry, out var binding));
                var context = new AbilityActivationContext<object>(owner, owner, binding, 0);
                Assert.IsTrue(selector.TrySelect(Bindings, context, out var selection, out _));
                return selection;
            }

            internal bool Run
            (
                AbilityBegin<string> begin, out AbilityActivationResult result, out AbilityUse<string> use
            ) => Run(Select(), begin, out result, out use);

            internal bool Run
            (
                AbilityUseSelection<object, string> selection,
                AbilityBegin<string> begin, out AbilityActivationResult result, out AbilityUse<string> use
            )
            {
                bool Started(in AbilityUse<string> started)
                {
                    issued++;
                    Starts++;
                    return begin(in started);
                }

                return selection.TryActivate(Started, out result, out use);
            }
        }

        private sealed class ResultObject
        {
            internal AbilityUseOrigin Origin { get; }

            internal ulong ExecutionID { get; }

            internal int EndCount { get; private set; }

            internal ResultObject(AbilityUseOrigin origin, ulong executionID)
            {
                Origin = origin;
                ExecutionID = executionID;
            }

            internal bool Emit(AbilityFixtureResults<AbilityUseOrigin> results, ulong recipient)
            {
                if (EndCount != 0) return false;

                results.Record(Origin, recipient, 1);
                return true;
            }

            internal void End()
            {
                if (EndCount == 0)
                {
                    EndCount++;
                }
            }
        }

        private sealed class Health
        {
            internal int Remaining = 6;
        }

        private sealed class DamageOperation : IGameplayOperation<Health, int>
        {
            // ------------------------------------------------------------
            /// <summary>
            /// 피해를 적용하고 무효 요청이면 결과를 변경하지 않는다.
            /// </summary>
            // ------------------------------------------------------------
            public GameplayOperationResult Invoke(in GameplayOperationContext<Health, int> context)
            {
                if (context.Payload == 0 || context.Recipient.Remaining == 0)
                {
                    return GameplayOperationResult.NoOp();
                }

                context.Recipient.Remaining = Math.Max(0, context.Recipient.Remaining - context.Payload);
                return GameplayOperationResult.Applied();
            }
        }

        private sealed class ResourceCost : IAbilityActivationCost<object>
        {
            private readonly ResourceFixture resource;
            private readonly object owner;
            private readonly int amount;
            private readonly bool failPay;

            internal ResourceCost(ResourceFixture resource, object owner, int amount, bool failPay = false)
            {
                this.resource = resource;
                this.owner = owner;
                this.amount = amount;
                this.failPay = failPay;
            }

            // ------------------------------------------------------------
            /// <summary>
            /// Owner의 자원이 충분한지 소비 없이 확인한다.
            /// </summary>
            // ------------------------------------------------------------
            public AbilityActivationCheck Evaluate(in AbilityActivationContext<object> context) =>
                resource.Get(owner) >= amount
                    ? AbilityActivationCheck.Allow()
                    : AbilityActivationCheck.Reject("Resource shortage.");

            // ------------------------------------------------------------
            /// <summary>
            /// 자원을 소비하고 같은 양을 되돌리는 영수증을 반환한다.
            /// </summary>
            // ------------------------------------------------------------
            public AbilityCostReceipt Pay(in AbilityActivationContext<object> context)
            {
                if (failPay)
                {
                    throw new InvalidOperationException("Pay failure.");
                }

                Assert.IsTrue(resource.TryDebit(owner, amount));
                return new AbilityCostReceipt(() => resource.Credit(owner, amount));
            }
        }

        private static ExecutionSession CreateSession(ulong id) =>
            new(id, new ExecutionContext(null, null), new FixedTimelineExecutionFlowSpec(2f).CreateRuntime());

    #endregion

    #region U-1: 선택 현재성과 다중 Cost

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> 같은 Entry의 연속 사용에서 선택 시점의
        /// <br/> Execution·Cost가 일치하고 토큰이 바뀐 선택은 거절한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_AbilityUse_탄소진과선택후State와Binding변경은토큰이거절()
        {
            using var request = new Request
            (
                "Gun", 1,
                new AbilityActivationPolicy<object>
                (
                    cost: new IAbilityActivationCost<object>[]
                    {
                        new AbilityChargeCost<object>(),
                    }
                ),
                charges: 2
            );
            var state = request.Ability.State;
            var begun = 0;
            bool Begin(in AbilityUse<string> started)
            {
                begun++;
                return true;
            }

            // 1회차: 탄 2 → 1. 선택한 Execution·출처가 그대로 시작에 전달된다.
            var first = request.Select();
            Assert.AreEqual("Gun", first.Execution);
            Assert.IsTrue(request.Run(first, Begin, out _, out var firstUse));
            Assert.AreEqual("Gun", firstUse.Execution);
            Assert.AreEqual(new AbilityKey("Gun"), firstUse.Origin.AbilityKey);
            Assert.AreEqual(new AbilityEntryKey("Use"), firstUse.Origin.EntryKey);
            Assert.AreEqual(1, state.Charge.Current);

            // 2회차 직전 State 변경: 변경 전에 선택한 Selection은 거절되고 비용을 소비하지 않는다.
            var stale = request.Select();
            Assert.AreEqual(1, state.Control.RestoreCharge(1));
            Assert.AreEqual(2, state.Charge.Current);
            Assert.AreEqual(AbilityActivationFailure.InvalidState, stale.Evaluate().Failure);
            Assert.IsFalse(request.Run(stale, Begin, out var staleResult, out _));
            Assert.AreEqual(AbilityActivationFailure.InvalidState, staleResult.Failure);
            Assert.AreEqual(2, state.Charge.Current);
            Assert.AreEqual(1, begun);

            // 다시 선택하면 현재 State 기준으로 사용된다: 탄 2 → 1.
            Assert.IsTrue(request.Run(request.Select(), Begin, out _, out _));
            Assert.AreEqual(1, state.Charge.Current);

            // Binding 변경도 토큰이 거절한다.
            var beforeBinding = request.Select();
            var other = new AbilityInstance<object>(new object(), new AbilityState(new AbilityKey("Other")));
            var overrideLease = request.Bindings.AcquireOverride(request.Entry, other, 1);
            Assert.IsFalse(request.Run(beforeBinding, Begin, out var bindingResult, out _));
            Assert.AreEqual(AbilityActivationFailure.InvalidState, bindingResult.Failure);
            Assert.AreEqual(1, state.Charge.Current);
            overrideLease.Release();

            // 마지막 탄을 소비하면 다음 선택의 사전 평가는 Cost를 거부하고 어떤 상태도 바꾸지 않는다.
            Assert.IsTrue(request.Run(request.Select(), Begin, out _, out _));
            Assert.AreEqual(0, state.Charge.Current);
            var revision = state.Revision;
            Assert.AreEqual(AbilityActivationFailure.CostRejected, request.Select().Evaluate().Failure);
            Assert.IsFalse(request.Run(request.Select(), Begin, out var empty, out _));
            Assert.AreEqual(AbilityActivationFailure.CostRejected, empty.Failure);
            Assert.AreEqual(revision, state.Revision);
            Assert.AreEqual(3, begun);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> 독립된 두 자원은 성공하면 둘 다, 거부하면 둘 다
        /// <br/> 그대로이고 두 번째 지불 예외는 첫 영수증을 환급한다.
        /// </summary>
        // ------------------------------------------------------------
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void TEST_AbilityUse_복수자원Cost는함께지불되거나함께그대로(int scenario)
        {
            var resource = new ResourceFixture();
            var hp = new object();
            var gauge = new object();
            resource.Add(hp, 30);
            resource.Add(gauge, scenario == 1 ? 10 : 15);
            using var request = new Request
            (
                "Skill", 1,
                new AbilityActivationPolicy<object>
                (
                    cost: new IAbilityActivationCost<object>[]
                    {
                        new ResourceCost(resource, hp, 20),
                        new ResourceCost(resource, gauge, 15, failPay: scenario == 2),
                    }
                )
            );
            var begun = 0;
            bool Begin(in AbilityUse<string> started)
            {
                begun++;
                return true;
            }

            if (scenario == 0)
            {
                // 둘 다 충분하면 둘 다 지불한다.
                Assert.IsTrue(request.Run(Begin, out _, out _));
                Assert.AreEqual(10, resource.Get(hp));
                Assert.AreEqual(0, resource.Get(gauge));
                Assert.AreEqual(1, begun);
            }
            else if (scenario == 1)
            {
                // 게이지가 부족하면 HP도 소비하지 않는다.
                Assert.IsFalse(request.Run(Begin, out var rejected, out _));
                Assert.AreEqual(AbilityActivationFailure.CostRejected, rejected.Failure);
                Assert.AreEqual(1, rejected.PolicyIndex);
                Assert.AreEqual(30, resource.Get(hp));
                Assert.AreEqual(10, resource.Get(gauge));
                Assert.AreEqual(0, begun);
            }
            else
            {
                // 두 번째 지불이 예외를 던지면 이미 지불한 HP를 첫 영수증으로 환급한다.
                Assert.Throws<InvalidOperationException>(() => request.Run(Begin, out _, out _));
                Assert.AreEqual(30, resource.Get(hp));
                Assert.AreEqual(15, resource.Get(gauge));
                Assert.AreEqual(0, begun);
            }
        }

    #endregion

    #region R-1: 독립 결과와 출처

        // ------------------------------------------------------------
        /// <summary>
        /// 실행 종료 뒤 독립 결과가 남고 원래 사용 출처를 보존하는지 검증한다.
        /// </summary>
        // ------------------------------------------------------------
        [TestCase(false)]
        [TestCase(true)]
        public void TEST_AbilityUse_독립결과가실행과소유자수명에서분리됨(bool removeOwnerOnHit)
        {
            using var request = new Request("Persistent", 1);
            var resultObjects = new List<ResultObject>();
            var owners = new HashSet<ulong>
            {
                1,
            };
            var journal = new AbilityFixtureResults<AbilityUseOrigin>();
            var session = CreateSession(24);
            var ended = 0;
            session.OnEnded += (_, _) => ended++;

            Assert.IsTrue(request.Run(Begin, out _, out var use));
            session.End(ExecutionEndReason.Completed);
            Assert.AreEqual(1, ended);
            Assert.AreEqual(2, resultObjects.Count);

            if (removeOwnerOnHit)
            {
                Assert.IsTrue(owners.Remove(1));
            }

            Assert.IsTrue(resultObjects[0].Emit(journal, 2));
            resultObjects[0].End();
            resultObjects[1].End();
            Assert.IsFalse(resultObjects[1].Emit(journal, 2));
            Assert.AreEqual(1, resultObjects[0].EndCount);
            Assert.AreEqual(1, resultObjects[1].EndCount);
            Assert.AreEqual(1, journal.Entries.Count);
            Assert.AreEqual(use.Origin, journal.Entries[0].Origin);
            Assert.AreEqual(request.LastExecutionID, resultObjects[0].ExecutionID);
            Assert.AreEqual(!removeOwnerOnHit, owners.Contains(1));

            bool Begin(in AbilityUse<string> started)
            {
                session.Begin();
                resultObjects.Add(new ResultObject(started.Origin, request.LastExecutionID));
                resultObjects.Add(new ResultObject(started.Origin, request.LastExecutionID));
                return true;
            }
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// 활성 소유자가 바뀌고 이전 Actor가 제거되어도 독립 결과의 출처를 보존한다.
        /// </summary>
        // ----------------------------------------------------------------------
        [TestCase(false)]
        [TestCase(true)]
        public void TEST_AbilityUse_소유자교체뒤추가결과와만료유지(bool removeActor)
        {
            using var request = new Request("Followup", 1);
            var roster = new AbilityFixtureRoster(1, 2);
            var actors = new Dictionary<ulong, object>
            {
                [1] = new object(),
                [2] = new object()
            };
            var journal = new AbilityFixtureResults<AbilityUseOrigin>();
            ResultObject effect = null;

            Assert.IsTrue(request.Run(Begin, out _, out var use));
            Assert.IsTrue(roster.TrySelect(2));
            if (removeActor)
            {
                Assert.IsTrue(actors.Remove(1));
            }

            var recipient = roster.ActiveOwner;
            Assert.IsTrue(actors.ContainsKey(recipient));
            Assert.IsTrue(effect.Emit(journal, recipient));
            Assert.IsTrue(effect.Emit(journal, recipient));
            effect.End();
            effect.End();
            Assert.IsFalse(effect.Emit(journal, recipient));

            Assert.AreEqual(1, effect.EndCount);
            Assert.AreEqual(2, journal.Entries.Count);
            foreach (var hit in journal.Entries)
            {
                Assert.AreEqual(2ul, hit.Recipient);
                Assert.AreEqual(use.Origin, hit.Origin);
                Assert.AreEqual(new AbilityKey("Followup"), hit.Origin.AbilityKey);
            }

            Assert.AreEqual(request.LastExecutionID, effect.ExecutionID);
            Assert.AreEqual(1, request.Starts);

            bool Begin(in AbilityUse<string> started)
            {
                effect = new ResultObject(started.Origin, request.LastExecutionID);
                return true;
            }
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> A의 Off-field Effect는 교대와 A Actor 제거 뒤에도 A의 출처로 추가
        /// <br/> 공격하고 한 번만 만료된다.
        /// </summary>
        // ----------------------------------------------------------------------
        [TestCase(false)]
        [TestCase(true)]
        public void TEST_AbilityUse_OffField효과는교대와Actor제거뒤에도A출처로추가공격하고한번만만료(bool removeActorA)
        {
            using var requestA = new Request("AbilityA", 1);
            using var requestB = new Request("AbilityB", 2);
            var roster = new AbilityFixtureRoster(1, 2);
            var actors = new Dictionary<ulong, object>
            {
                [1] = new object(),
                [2] = new object()
            };
            var journal = new AbilityFixtureResults<AbilityUseOrigin>();
            ResultObject effect = null;

            // A가 사용하며 만든 Effect는 복사한 Origin만 보관하고 A의 Session이나 Binding을 참조하지 않는다.
            Assert.IsTrue(requestA.Run(CreateEffect, out _, out var useA));
            var revisionA = requestA.Ability.State.Revision;

            // 교대하고 A의 Actor를 제거한다.
            Assert.IsTrue(roster.TrySelect(2));
            if (removeActorA)
            {
                Assert.IsTrue(actors.Remove(1));
            }

            // B가 두 번 적중하고 Effect는 매번 추가 공격한다.
            Assert.IsTrue(requestB.Run(Accept, out _, out var useB));
            for (var hit = 0; hit < 2; hit++)
            {
                journal.Record(useB.Origin, 9, 3);
                Assert.IsTrue(effect.Emit(journal, 9));
            }

            // 만료는 한 번만 확정되고 이후 추가 공격은 없다.
            effect.End();
            effect.End();
            Assert.IsFalse(effect.Emit(journal, 9));

            Assert.AreEqual(1, effect.EndCount);
            Assert.AreEqual(4, journal.Entries.Count);
            var extra = 0;
            foreach (var entry in journal.Entries)
            {
                if (entry.Amount == 1)
                {
                    extra++;
                    Assert.AreEqual(useA.Origin, entry.Origin);
                    Assert.AreEqual(new AbilityKey("AbilityA"), entry.Origin.AbilityKey);
                }
                else
                {
                    Assert.AreEqual(useB.Origin, entry.Origin);
                    Assert.AreEqual(new AbilityKey("AbilityB"), entry.Origin.AbilityKey);
                }
            }

            Assert.AreEqual(2, extra);
            Assert.AreEqual(2ul, roster.ActiveOwner);
            Assert.AreEqual(!removeActorA, actors.ContainsKey(1));
            Assert.AreEqual(1, requestA.Starts);
            Assert.AreEqual(effect.ExecutionID, requestA.LastExecutionID);
            Assert.AreNotEqual(requestA.LastExecutionID, requestB.LastExecutionID);
            Assert.AreEqual(revisionA, requestA.Ability.State.Revision);

            bool CreateEffect(in AbilityUse<string> started)
            {
                effect = new ResultObject(started.Origin, requestA.LastExecutionID);
                return true;
            }

            bool Accept(in AbilityUse<string> started) => true;
        }

        // ======================================================================
        /// <summary>
        /// <br/> 0은 현재 활성 파티원, 그 밖의 값은 그 소유자의 Actor를 가리키는
        /// <br/> 공간 참조를 활성 소유자와 Actor 위치로 해석한다.
        /// </summary>
        // ======================================================================
        private sealed class PartyAnchorProvider : ISpatialSourceProvider<ulong>
        {
            private readonly AbilityFixtureRoster roster;
            private readonly Dictionary<ulong, Vector3> positions;

            internal PartyAnchorProvider(AbilityFixtureRoster roster, Dictionary<ulong, Vector3> positions)
            {
                this.roster = roster;
                this.positions = positions;
            }

            // ------------------------------------------------------------
            /// <summary>
            /// 참조가 가리키는 Actor가 있으면 그 위치를 반환한다.
            /// </summary>
            // ------------------------------------------------------------
            public bool TryResolve
            (
                in ulong reference,
                out Vector3 position,
                out Quaternion rotation,
                out bool hasRotation
            )
            {
                rotation = Quaternion.identity;
                hasRotation = false;
                var owner = reference == 0 ? roster.ActiveOwner : reference;
                return positions.TryGetValue(owner, out position);
            }
        }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/> A가 만든 파티 추적 필드는 원점을 논리 참조(현재 활성 파티원)로 매번 다시 해석해
        /// <br/> 교대와 A Actor 제거 뒤에도 B를 따라가고 A의 사용 출처를 유지한다. 특정 Actor를
        /// <br/> 직접 참조하면 그 Actor가 사라질 때 위치를 잃고, 포착 모드는 시작 위치에 남는다.
        /// </summary>
        // --------------------------------------------------------------------------------
        [Test]
        public void TEST_AbilityUse_파티추적필드는교대뒤새Actor를따라가고A출처를유지()
        {
            using var requestA = new Request("AbilityA", 1);
            using var requestB = new Request("AbilityB", 2);
            var roster = new AbilityFixtureRoster(1, 2);
            var positions = new Dictionary<ulong, Vector3>
            {
                [1] = Vector3.zero,
                [2] = new Vector3(10f, 0f, 0f),
            };
            var provider = new PartyAnchorProvider(roster, positions);
            var party = CreateSession(0, SpatialTimeMode.Follow);
            var actorA = CreateSession(1, SpatialTimeMode.Follow);
            var captured = CreateSession(0, SpatialTimeMode.Capture);
            var fieldOrigin = default(AbilityUseOrigin);

            // A가 사용하며 필드를 만들고 필드는 A의 Origin만 복사해 보관한다.
            Assert.IsTrue(requestA.Run(CreateField, out _, out var useA));
            Assert.AreEqual(useA.Origin, fieldOrigin);

            // A가 움직이면 필드가 따라간다.
            positions[1] = new Vector3(1f, 0f, 0f);
            Evaluate(0.1f);
            Assert.AreEqual(new Vector3(1f, 0f, 0f), party.Current.Position);

            // B로 교대하고 A의 Actor를 제거하면 필드는 B를 따라간다.
            Assert.IsTrue(roster.TrySelect(2));
            Assert.IsTrue(positions.Remove(1));
            Evaluate(0.2f);

            Assert.IsTrue(party.Current.HasPosition);
            Assert.AreEqual(new Vector3(10f, 0f, 0f), party.Current.Position);
            Assert.IsFalse(actorA.Current.HasPosition);
            Assert.AreEqual(Vector3.zero, captured.Current.Position);

            // B가 사용해도 필드의 출처는 A로 남는다.
            Assert.IsTrue(requestB.Run(Accept, out _, out var useB));
            Assert.AreNotEqual(useB.Origin, fieldOrigin);
            Assert.AreEqual(new AbilityKey("AbilityA"), fieldOrigin.AbilityKey);

            SpatialSession<ulong> CreateSession(ulong reference, SpatialTimeMode mode)
            {
                var request = new SpatialRequest<ulong>(SpatialSource<ulong>.FromReference(reference));
                return new SpatialSession<ulong>(request, mode, provider);
            }

            void Evaluate(float time)
            {
                party.Evaluate(time, Vector3.zero);
                actorA.Evaluate(time, Vector3.zero);
                captured.Evaluate(time, Vector3.zero);
            }

            bool CreateField(in AbilityUse<string> started)
            {
                fieldOrigin = started.Origin;
                party.Begin(0f, Vector3.zero);
                actorA.Begin(0f, Vector3.zero);
                captured.Begin(0f, Vector3.zero);
                return true;
            }

            bool Accept(in AbilityUse<string> started) => true;
        }

    #endregion

    #region H-1: 적중 결과

        // ------------------------------------------------------------
        /// <summary>
        /// 접촉, 적용, 무효와 처치를 분리하고 반복 접촉의 재적용을 막는다.
        /// </summary>
        // ------------------------------------------------------------
        [TestCase(false)]
        [TestCase(true)]
        public void TEST_AbilityUse_다단결과와중복적중(bool duplicate)
        {
            using var request = new Request("MultiHit", 1);
            var executionID = 0ul;
            Assert.IsTrue(request.Run(Begin, out _, out var use));
            var origin = use.Origin;
            var target = new Health();
            var operation = new DamageOperation();
            var hits = new HashSet<int>();
            var journal = new AbilityFixtureResults<AbilityUseOrigin>();
            var contactCount = 0;
            var noOpCount = 0;
            var killCount = 0;

            Contact(1, 4);
            if (duplicate)
            {
                Contact(1, 4);
            }

            Assert.AreEqual(2, target.Remaining);
            Contact(2, 0);
            Contact(3, 2);

            Assert.AreEqual(duplicate ? 4 : 3, contactCount);
            Assert.AreEqual(1, noOpCount);
            Assert.AreEqual(1, killCount);
            Assert.AreEqual(0, target.Remaining);
            Assert.AreEqual(2, journal.Entries.Count);
            foreach (var hit in journal.Entries)
            {
                Assert.AreEqual(origin, hit.Origin);
            }

            Assert.AreEqual(26ul, executionID);
            Assert.AreEqual(1, request.Starts);

            void Contact(int hitID, int amount)
            {
                contactCount++;
                if (!hits.Add(hitID)) return;

                var before = target.Remaining;
                var context = new GameplayOperationContext<Health, int>(null, null, origin, target, amount);
                var applied = GameplayOperationRunner.Invoke(operation, context);
                if (applied.IsNoOp)
                {
                    noOpCount++;
                }
                else if (applied.IsApplied)
                {
                    journal.Record(origin, 2, amount);
                    if (before > 0 && target.Remaining == 0)
                    {
                        killCount++;
                    }
                }
            }

            bool Begin(in AbilityUse<string> started)
            {
                executionID = 26;
                return true;
            }
        }

    #endregion

    }
}
