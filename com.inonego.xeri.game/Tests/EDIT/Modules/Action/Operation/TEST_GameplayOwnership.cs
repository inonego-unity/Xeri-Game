/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : TEST_GameplayOwnership.cs
수정일 : 2026-11-19

# 설명
GameplayOperation, ExecutionSession, Lease의 기존 공개 계약으로 독립 결과 객체와 지속 자원 수명을 검증한다.
비용 검사·소비와 장기 예약의 정리 책임은 테스트 전용 WorldOwner가 소유한다.
테스트 World와 Operation은 GameplayOwnershipWorld.cs의 테스트 코드다.

# 테스트 구성
 O: 독립 Effect 소유권과 출처
 R: Recipient 부재와 Lease 정리
 S: 지속 효과 소비와 장기 예약
========================================================================= BLOCK_HEADER_END */

using System;
using System.Collections;
using System.Collections.Generic;

using NUnit;
using NUnit.Framework;

namespace inonego.Xeri.Game.TEST._GameplayOwnership
{
    using Xeri;
    using Xeri.Game;

    // ============================================================
    /// <summary>
    /// Owner가 소유하는 Effect와 자원의 수명을 검증하는 테스트 묶음.
    /// </summary>
    // ============================================================
    public sealed class TEST_GameplayOwnership
    {

    #region 검증용 Session 생성

        private static ExecutionSession CreateSession(object owner, object instigator, ulong id)
        {
            var context = new ExecutionContext(owner, instigator);
            var flow = new FixedTimelineExecutionFlowSpec(60f).CreateRuntime();
            return new ExecutionSession(id, context, flow);
        }

    #endregion

    #region O-1: 독립 Effect 소유권과 출처

        // ------------------------------------------------------------
        /// <summary>
        /// Operation 적용 결과와 독립 Effect 수명이 분리되는지 검증한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_GameplayOwnership_Operation적용결과와독립Effect수명을분리한다()
        {
            var owner = new WorldOwner(0);
            var instigator = new object();
            var origin = new object();
            var ability = new object();
            var target = new object();
            var session = CreateSession(owner, instigator, 13);
            var operation = new CreateEffectOperation();
            var context = new GameplayOperationContext<WorldOwner, EffectRequest>
            (
                owner,
                instigator,
                origin,
                owner,
                new EffectRequest(ability, session, target)
            );
            var secondOperation = new CreateEffectOperation();
            var secondContext = new GameplayOperationContext<WorldOwner, EffectRequest>
            (
                owner,
                instigator,
                new object(),
                owner,
                new EffectRequest(new object(), session, new object())
            );
            var damageContext = new GameplayOperationContext<WorldOwner, int>
            (
                owner,
                instigator,
                origin,
                owner,
                60
            );
            var shieldContext = new GameplayOperationContext<WorldOwner, int>
            (
                owner,
                instigator,
                origin,
                owner,
                25
            );

            session.Begin();
            var result = GameplayOperationRunner.Invoke(operation, context);
            var secondResult = GameplayOperationRunner.Invoke(secondOperation, secondContext);
            var damageResult = GameplayOperationRunner.Invoke(new ApplyDamageOperation(), damageContext);
            var shieldResult = GameplayOperationRunner.Invoke(new AddShieldOperation(), shieldContext);
            var hitContext = new GameplayOperationContext<WorldOwner, HitRequest>
            (
                owner,
                instigator,
                origin,
                owner,
                new HitRequest(ability, session, target, 40)
            );
            session.End(ExecutionEndReason.Completed);
            var lateHitResult = GameplayOperationRunner.Invoke(new ApplyHitOperation(), hitContext);

            Assert.IsTrue(result.IsApplied);
            Assert.IsTrue(secondResult.IsApplied);
            Assert.IsTrue(damageResult.IsApplied);
            Assert.IsTrue(shieldResult.IsApplied);
            Assert.IsTrue(lateHitResult.IsApplied);
            Assert.AreEqual(0, owner.Health);
            Assert.IsTrue(owner.IsKilled);
            Assert.AreEqual(25, owner.Shield);
            Assert.AreEqual(2, owner.Effects.Count);
            Assert.AreSame(origin, operation.Created.Origin);
            Assert.AreSame(ability, operation.Created.Ability);
            Assert.AreSame(session, operation.Created.Execution);
            Assert.AreSame(target, operation.Created.Target);
            Assert.AreSame(origin, owner.LastHitOrigin);
            Assert.AreSame(ability, owner.LastHitAbility);
            Assert.AreSame(session, owner.LastHitExecution);
            Assert.AreSame(target, owner.LastHitTarget);

            owner.Effects[0].Tick();
            Assert.AreEqual(1, operation.Created.TickCount);
            operation.Created.End();
            operation.Created.End();
            Assert.AreEqual(1, operation.Created.EndCount);
            Assert.AreEqual(0, secondOperation.Created.EndCount);
        }

    #endregion

    #region R-1: Recipient 부재와 Lease 정리

        // ----------------------------------------------------------------------
        /// <summary>
        /// Recipient가 없으면 Operation을 거부하고 Lease를 정리하는지 검증한다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_GameplayOwnership_Recipient부재시Operation거부와Lease정리를검증한다()
        {
            var owner = new WorldOwner(4);
            var instigator = new object();
            var origin = new object();
            var ability = new object();
            var target = new object();
            var session = CreateSession(owner, instigator, 14);
            var reservation = owner.TryReserve(3);
            var callbackCount = 0;
            var cleanupCount = 0;
            Action<ExecutionSession, ExecutionEndReason> onEnded = (_, _) => callbackCount++;
            session.OnEnded += onEnded;
            session.Begin();

            var missingRecipientContext = new GameplayOperationContext<WorldOwner, EffectRequest>
            (
                owner,
                instigator,
                origin,
                null,
                new EffectRequest(ability, session, target)
            );
            var missingRecipientResult = GameplayOperationRunner.Invoke
            (
                new CreateEffectOperation(),
                missingRecipientContext
            );
            var cleanup = new Lease
            (
                () =>
                {
                    reservation.Release();
                    session.OnEnded -= onEnded;
                    cleanupCount++;
                }
            );

            Assert.IsFalse(missingRecipientResult.Succeeded);
            Assert.AreEqual(0, owner.Effects.Count);
            Assert.AreEqual(3, owner.ReservedResource);

            cleanup.Release();
            cleanup.Release();
            session.End(ExecutionEndReason.Cancelled);

            Assert.AreEqual(4, owner.AvailableResource);
            Assert.AreEqual(0, owner.ReservedResource);
            Assert.AreEqual(1, cleanupCount);
            Assert.AreEqual(0, callbackCount);
        }

    #endregion

    #region S-1: 지속 효과 소비와 장기 예약

        // ------------------------------------------------------------
        /// <summary>
        /// 지속 Owner가 독립 Tick과 부족 시 일회 종료를 소유하는지 검증한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_GameplayOwnership_지속Owner가독립Tick과부족시일회종료를소유한다()
        {
            var owner = new WorldOwner(5);
            var effect = new Effect(null, null, null, null);
            owner.Effects.Add(effect);
            var endLease = new Lease(effect.End);
            var operation = new TickEffectOperation();
            var context = new GameplayOperationContext<WorldOwner, EffectTickRequest>
            (
                owner,
                owner,
                effect,
                owner,
                new EffectTickRequest(effect, 3)
            );

            var applied = GameplayOperationRunner.Invoke(operation, context);
            Assert.IsTrue(applied.IsApplied);
            Assert.AreEqual(1, effect.TickCount);
            var exhausted = GameplayOperationRunner.Invoke(operation, context);
            Assert.IsTrue(exhausted.IsNoOp);
            endLease.Release();
            endLease.Release();

            Assert.AreEqual(1, effect.EndCount);
            Assert.AreEqual(2, owner.AvailableResource);
            Assert.AreEqual(0, owner.ReservedResource);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 장기 예약이 지불된 Cost와 독립적으로 중복 해제되지 않는지 검증한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_GameplayOwnership_장기예약은지불된Cost와독립적으로중복해제되지않는다()
        {
            var owner = new WorldOwner(10);
            var abilityState = new AbilityState(new AbilityKey("Fixture"));
            var ability = new AbilityInstance<object>(new object(), abilityState);
            var binding = new AbilityBinding<object>(new AbilityEntryKey("Fixture"), ability);
            var context = new AbilityActivationContext<object>(owner, owner, binding, 1);
            var policy = new AbilityActivationPolicy<object>
            (
                cost: new IAbilityActivationCost<object>[]
                {
                    new ActivationChargeCost(),
                }
            );

            Assert.IsTrue(policy.TryActivate(context, null, new List<AbilityCostReceipt>(), out var activationResult));
            Assert.IsTrue(activationResult.Succeeded);
            Assert.AreEqual(9, owner.AvailableResource);

            var longReservation = owner.TryReserve(4);

            Assert.IsNotNull(longReservation);
            Assert.AreEqual(5, owner.AvailableResource);
            Assert.AreEqual(4, owner.ReservedResource);
            Assert.IsNull(owner.TryReserve(6));

            // 단일 사용 비용은 그대로 남고 장기 예약만 독립적으로 해제된다.
            Assert.AreEqual(5, owner.AvailableResource);
            Assert.AreEqual(4, owner.ReservedResource);

            longReservation.Release();
            longReservation.Release();

            Assert.AreEqual(9, owner.AvailableResource);
            Assert.AreEqual(0, owner.ReservedResource);
        }

    #endregion

    }
}
