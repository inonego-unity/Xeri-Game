/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : TEST_GameplayOwnership.cs
수정일 : 2026-11-12

# 설명
GameplayOperation, ExecutionSession, Lease의 기존 공개 계약으로 독립 결과 객체와 지속 자원 수명을 검증한다.
비용 검사·소비와 장기 예약의 정리 책임은 테스트 전용 WorldOwner가 소유한다.
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

    #region 테스트 Owner와 게임 규칙

        private sealed class Effect
        {
            // ------------------------------------------------------------
            /// <summary>
            /// Effect를 발생시킨 Origin을 반환한다.
            /// </summary>
            // ------------------------------------------------------------
            public object Origin { get; }

            // ------------------------------------------------------------
            /// <summary>
            /// Effect를 만든 Ability를 반환한다.
            /// </summary>
            // ------------------------------------------------------------
            public object Ability { get; }

            // ------------------------------------------------------------
            /// <summary>
            /// Effect를 만든 Execution을 반환한다.
            /// </summary>
            // ------------------------------------------------------------
            public object Execution { get; }

            // ------------------------------------------------------------
            /// <summary>
            /// Effect의 대상을 반환한다.
            /// </summary>
            // ------------------------------------------------------------
            public object Target { get; }

            // ------------------------------------------------------------
            /// <summary>
            /// Tick 호출 횟수를 반환한다.
            /// </summary>
            // ------------------------------------------------------------
            public int TickCount { get; private set; }

            // ------------------------------------------------------------
            /// <summary>
            /// End 호출 횟수를 반환한다.
            /// </summary>
            // ------------------------------------------------------------
            public int EndCount { get; private set; }

            // ------------------------------------------------------------
            /// <summary>
            /// 발생 주체와 대상 정보를 담은 Effect를 생성한다.
            /// </summary>
            // ------------------------------------------------------------
            public Effect(object origin, object ability, object execution, object target)
            {
                Origin = origin;
                Ability = ability;
                Execution = execution;
                Target = target;
            }

            // ------------------------------------------------------------
            /// <summary>
            /// Tick 호출 횟수를 증가시킨다.
            /// </summary>
            // ------------------------------------------------------------
            public void Tick()
            {
                TickCount++;
            }

            // ------------------------------------------------------------
            /// <summary>
            /// Effect를 한 번만 종료한다.
            /// </summary>
            // ------------------------------------------------------------
            public void End()
            {
                if (EndCount > 0)
                {
                    return;
                }

                EndCount++;
            }
        }

        private sealed class WorldOwner
        {
            // ------------------------------------------------------------
            /// <summary>
            /// Owner가 소유한 Effect 목록을 반환한다.
            /// </summary>
            // ------------------------------------------------------------
            public List<Effect> Effects { get; } = new();

            // ------------------------------------------------------------
            /// <summary>
            /// 현재 체력을 반환한다.
            /// </summary>
            // ------------------------------------------------------------
            public int Health { get; private set; } = 100;

            // ------------------------------------------------------------
            /// <summary>
            /// 체력이 0 이하인지 반환한다.
            /// </summary>
            // ------------------------------------------------------------
            public bool IsKilled => Health <= 0;

            // ------------------------------------------------------------
            /// <summary>
            /// 현재 보호막 수치를 반환한다.
            /// </summary>
            // ------------------------------------------------------------
            public int Shield { get; private set; }

            // ------------------------------------------------------------
            /// <summary>
            /// 사용 가능한 자원량을 반환한다.
            /// </summary>
            // ------------------------------------------------------------
            public int AvailableResource { get; private set; }

            // ------------------------------------------------------------
            /// <summary>
            /// 예약된 자원량을 반환한다.
            /// </summary>
            // ------------------------------------------------------------
            public int ReservedResource { get; private set; }

            // ------------------------------------------------------------
            /// <summary>
            /// 마지막 피격의 Origin을 반환한다.
            /// </summary>
            // ------------------------------------------------------------
            public object LastHitOrigin { get; private set; }

            // ------------------------------------------------------------
            /// <summary>
            /// 마지막 피격의 Ability를 반환한다.
            /// </summary>
            // ------------------------------------------------------------
            public object LastHitAbility { get; private set; }

            // ------------------------------------------------------------
            /// <summary>
            /// 마지막 피격의 Execution을 반환한다.
            /// </summary>
            // ------------------------------------------------------------
            public object LastHitExecution { get; private set; }

            // ------------------------------------------------------------
            /// <summary>
            /// 마지막 피격의 대상을 반환한다.
            /// </summary>
            // ------------------------------------------------------------
            public object LastHitTarget { get; private set; }

            // ------------------------------------------------------------
            /// <summary>
            /// 초기 자원량으로 테스트 Owner를 생성한다.
            /// </summary>
            // ------------------------------------------------------------
            public WorldOwner(int resource)
            {
                AvailableResource = resource;
            }

            // ------------------------------------------------------------
            /// <summary>
            /// 피해를 적용하고 체력이 0 미만으로 내려가지 않게 한다.
            /// </summary>
            // ------------------------------------------------------------
            public void ApplyDamage(int damage)
            {
                Health = Math.Max(0, Health - damage);
            }

            // ------------------------------------------------------------
            /// <summary>
            /// 보호막 수치를 증가시킨다.
            /// </summary>
            // ------------------------------------------------------------
            public void AddShield(int amount)
            {
                Shield += amount;
            }

            // ------------------------------------------------------------
            /// <summary>
            /// 피격 요청을 받아 피해와 피격 정보를 기록한다.
            /// </summary>
            // ------------------------------------------------------------
            public void ApplyHit(in GameplayOperationContext<WorldOwner, HitRequest> context)
            {
                LastHitOrigin = context.Origin;
                LastHitAbility = context.Payload.Ability;
                LastHitExecution = context.Payload.Execution;
                LastHitTarget = context.Payload.Target;
                ApplyDamage(context.Payload.Amount);
            }

            // ------------------------------------------------------------
            /// <summary>
            /// 요청으로 Effect를 생성하고 목록에 추가한다.
            /// </summary>
            // ------------------------------------------------------------
            public Effect CreateEffect(in GameplayOperationContext<WorldOwner, EffectRequest> context)
            {
                var effect = new Effect(context.Origin, context.Payload.Ability, context.Payload.Execution, context.Payload.Target);
                Effects.Add(effect);
                return effect;
            }

            // ------------------------------------------------------------
            /// <summary>
            /// 자원을 예약할 수 있으면 Lease를 반환한다.
            /// </summary>
            // ------------------------------------------------------------
            public Lease TryReserve(int amount)
            {
                if (amount <= 0 || AvailableResource < amount)
                {
                    return null;
                }

                AvailableResource -= amount;
                ReservedResource += amount;
                return new Lease
                (
                    () =>
                    {
                        ReservedResource -= amount;
                        AvailableResource += amount;
                    }
                );
            }

            // ------------------------------------------------------------
            /// <summary>
            /// 사용 가능한 자원을 소비할 수 있으면 소비한다.
            /// </summary>
            // ------------------------------------------------------------
            public bool TryConsume(int amount)
            {
                if (amount <= 0 || AvailableResource < amount)
                {
                    return false;
                }

                AvailableResource -= amount;
                return true;
            }

            // ------------------------------------------------------------
            /// <summary>
            /// Effect를 Tick하고 비용을 소비한다.
            /// </summary>
            // ------------------------------------------------------------
            public bool TickAndConsume(Effect effect, int amount)
            {
                if (effect.EndCount > 0 || amount <= 0 || AvailableResource < amount)
                {
                    effect.End();
                    return false;
                }

                AvailableResource -= amount;
                effect.Tick();
                return true;
            }
        }

        private sealed class EffectRequest
        {
            // ------------------------------------------------------------
            /// <summary>
            /// 요청의 Ability를 반환한다.
            /// </summary>
            // ------------------------------------------------------------
            public object Ability { get; }

            // ------------------------------------------------------------
            /// <summary>
            /// 요청의 Execution을 반환한다.
            /// </summary>
            // ------------------------------------------------------------
            public object Execution { get; }

            // ------------------------------------------------------------
            /// <summary>
            /// 요청의 대상을 반환한다.
            /// </summary>
            // ------------------------------------------------------------
            public object Target { get; }

            // ------------------------------------------------------------
            /// <summary>
            /// Ability, Execution, 대상으로 Effect 요청을 생성한다.
            /// </summary>
            // ------------------------------------------------------------
            public EffectRequest(object ability, object execution, object target)
            {
                Ability = ability;
                Execution = execution;
                Target = target;
            }
        }

        private sealed class EffectTickRequest
        {
            // ------------------------------------------------------------
            /// <summary>
            /// Tick 대상 Effect를 반환한다.
            /// </summary>
            // ------------------------------------------------------------
            public Effect Effect { get; }

            // ------------------------------------------------------------
            /// <summary>
            /// Tick에 필요한 비용을 반환한다.
            /// </summary>
            // ------------------------------------------------------------
            public int Cost { get; }

            // ------------------------------------------------------------
            /// <summary>
            /// Effect와 비용으로 Tick 요청을 생성한다.
            /// </summary>
            // ------------------------------------------------------------
            public EffectTickRequest(Effect effect, int cost)
            {
                Effect = effect;
                Cost = cost;
            }
        }

        private sealed class HitRequest
        {
            // ------------------------------------------------------------
            /// <summary>
            /// 피격 요청의 Ability를 반환한다.
            /// </summary>
            // ------------------------------------------------------------
            public object Ability { get; }

            // ------------------------------------------------------------
            /// <summary>
            /// 피격 요청의 Execution을 반환한다.
            /// </summary>
            // ------------------------------------------------------------
            public object Execution { get; }

            // ------------------------------------------------------------
            /// <summary>
            /// 피격 요청의 대상을 반환한다.
            /// </summary>
            // ------------------------------------------------------------
            public object Target { get; }

            // ------------------------------------------------------------
            /// <summary>
            /// 피격 피해량을 반환한다.
            /// </summary>
            // ------------------------------------------------------------
            public int Amount { get; }

            // ------------------------------------------------------------
            /// <summary>
            /// 피격 정보와 피해량으로 요청을 생성한다.
            /// </summary>
            // ------------------------------------------------------------
            public HitRequest(object ability, object execution, object target, int amount)
            {
                Ability = ability;
                Execution = execution;
                Target = target;
                Amount = amount;
            }
        }

        private sealed class CreateEffectOperation : IGameplayOperation<WorldOwner, EffectRequest>
        {
            // ------------------------------------------------------------
            /// <summary>
            /// 마지막으로 생성된 Effect를 반환한다.
            /// </summary>
            // ------------------------------------------------------------
            public Effect Created { get; private set; }

            // ------------------------------------------------------------
            /// <summary>
            /// Effect 생성 Operation을 실행하고 결과를 반환한다.
            /// </summary>
            // ------------------------------------------------------------
            public GameplayOperationResult Invoke(in GameplayOperationContext<WorldOwner, EffectRequest> context)
            {
                if (context.Recipient == null)
                {
                    return GameplayOperationResult.Reject("world owner missing");
                }

                Created = context.Recipient.CreateEffect(context);
                return GameplayOperationResult.Applied();
            }
        }

        private sealed class ApplyDamageOperation : IGameplayOperation<WorldOwner, int>
        {
            // ------------------------------------------------------------
            /// <summary>
            /// 자원 소비 Operation을 실행하고 결과를 반환한다.
            /// </summary>
            // ------------------------------------------------------------
            public GameplayOperationResult Invoke(in GameplayOperationContext<WorldOwner, int> context)
            {
                if (context.Recipient == null || context.Payload <= 0)
                {
                    return GameplayOperationResult.Reject("invalid damage target or amount");
                }

                context.Recipient.ApplyDamage(context.Payload);
                return GameplayOperationResult.Applied();
            }
        }

        private sealed class ApplyHitOperation : IGameplayOperation<WorldOwner, HitRequest>
        {
            // ------------------------------------------------------------
            /// <summary>
            /// 피격 Operation을 실행하고 결과를 반환한다.
            /// </summary>
            // ------------------------------------------------------------
            public GameplayOperationResult Invoke
            (
                in GameplayOperationContext<WorldOwner, HitRequest> context
            )
            {
                if (context.Recipient == null)
                {
                    return GameplayOperationResult.Reject("world owner missing");
                }

                context.Recipient.ApplyHit(context);
                return GameplayOperationResult.Applied();
            }
        }

        private sealed class AddShieldOperation : IGameplayOperation<WorldOwner, int>
        {
            // ------------------------------------------------------------
            /// <summary>
            /// 자원 예약 Operation을 실행하고 결과를 반환한다.
            /// </summary>
            // ------------------------------------------------------------
            public GameplayOperationResult Invoke(in GameplayOperationContext<WorldOwner, int> context)
            {
                if (context.Recipient == null || context.Payload <= 0)
                {
                    return GameplayOperationResult.Reject("invalid shield target or amount");
                }

                context.Recipient.AddShield(context.Payload);
                return GameplayOperationResult.Applied();
            }
        }

        private sealed class TickEffectOperation : IGameplayOperation<WorldOwner, EffectTickRequest>
        {
            // ------------------------------------------------------------
            /// <summary>
            /// Effect Tick Operation을 실행하고 결과를 반환한다.
            /// </summary>
            // ------------------------------------------------------------
            public GameplayOperationResult Invoke
            (
                in GameplayOperationContext<WorldOwner, EffectTickRequest> context
            )
            {
                return context.Recipient.TickAndConsume(context.Payload.Effect, context.Payload.Cost)
                    ? GameplayOperationResult.Applied()
                    : GameplayOperationResult.NoOp("effect ended or resource is insufficient");
            }
        }

        private sealed class ActivationChargeCost : IAbilityActivationCost<object>
        {
            // ------------------------------------------------------------
            /// <summary>
            /// Cost가 지불 가능한지 평가한다.
            /// </summary>
            // ------------------------------------------------------------
            public AbilityActivationCheck Evaluate
            (
                in AbilityActivationContext<object> context
            )
            {
                var owner = (WorldOwner)context.Owner;
                var amount = (int)context.Payload;
                return amount > 0 && owner.AvailableResource >= amount
                    ? AbilityActivationCheck.Allow()
                    : AbilityActivationCheck.Reject("activation resource is insufficient");
            }

            // ------------------------------------------------------------
            /// <summary>
            /// Cost를 지불하고 영수증을 반환한다.
            /// </summary>
            // ------------------------------------------------------------
            public AbilityCostReceipt Pay
            (
                in AbilityActivationContext<object> context
            )
            {
                var owner = (WorldOwner)context.Owner;
                var amount = (int)context.Payload;
                if (!owner.TryConsume(amount))
                {
                    throw new InvalidOperationException("activation resource changed after evaluation");
                }

                return AbilityCostReceipt.NoChange;
            }
        }

        private static ExecutionSession CreateSession(object owner, object instigator, ulong id)
        {
            var context = new ExecutionContext(owner, instigator);
            var flow = new FixedTimelineExecutionFlowDefinition(60f).CreateRuntime();
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

            Assert.IsTrue(policy.TryActivate(context, out var activationResult));
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
