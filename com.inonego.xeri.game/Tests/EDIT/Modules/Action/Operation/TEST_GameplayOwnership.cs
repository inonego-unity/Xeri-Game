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

    public sealed class TEST_GameplayOwnership
    {

    #region 테스트 Owner와 게임 규칙

        private sealed class Effect
        {
            public object Origin { get; }
            public object Ability { get; }
            public object Execution { get; }
            public object Target { get; }
            public int TickCount { get; private set; }
            public int EndCount { get; private set; }

            public Effect(object origin, object ability, object execution, object target)
            {
                Origin = origin;
                Ability = ability;
                Execution = execution;
                Target = target;
            }

            public void Tick()
            {
                TickCount++;
            }

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
            public List<Effect> Effects { get; } = new();
            public int Health { get; private set; } = 100;
            public bool IsKilled => Health <= 0;
            public int Shield { get; private set; }
            public int AvailableResource { get; private set; }
            public int ReservedResource { get; private set; }
            public object LastHitOrigin { get; private set; }
            public object LastHitAbility { get; private set; }
            public object LastHitExecution { get; private set; }
            public object LastHitTarget { get; private set; }

            public WorldOwner(int resource)
            {
                AvailableResource = resource;
            }

            public void ApplyDamage(int damage)
            {
                Health = Math.Max(0, Health - damage);
            }

            public void AddShield(int amount)
            {
                Shield += amount;
            }

            public void ApplyHit(in GameplayOperationContext<WorldOwner, HitRequest> context)
            {
                LastHitOrigin = context.Origin;
                LastHitAbility = context.Project.Ability;
                LastHitExecution = context.Project.Execution;
                LastHitTarget = context.Project.Target;
                ApplyDamage(context.Project.Amount);
            }

            public Effect CreateEffect(in GameplayOperationContext<WorldOwner, EffectRequest> context)
            {
                var effect = new Effect(context.Origin, context.Project.Ability, context.Project.Execution, context.Project.Target);
                Effects.Add(effect);
                return effect;
            }

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

            public bool TryConsume(int amount)
            {
                if (amount <= 0 || AvailableResource < amount)
                {
                    return false;
                }

                AvailableResource -= amount;
                return true;
            }

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
            public object Ability { get; }
            public object Execution { get; }
            public object Target { get; }

            public EffectRequest(object ability, object execution, object target)
            {
                Ability = ability;
                Execution = execution;
                Target = target;
            }
        }

        private sealed class EffectTickRequest
        {
            public Effect Effect { get; }
            public int Cost { get; }

            public EffectTickRequest(Effect effect, int cost)
            {
                Effect = effect;
                Cost = cost;
            }
        }

        private sealed class HitRequest
        {
            public object Ability { get; }
            public object Execution { get; }
            public object Target { get; }
            public int Amount { get; }

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
            public Effect Created { get; private set; }

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
            public GameplayOperationResult Invoke(in GameplayOperationContext<WorldOwner, int> context)
            {
                if (context.Recipient == null || context.Project <= 0)
                {
                    return GameplayOperationResult.Reject("invalid damage target or amount");
                }

                context.Recipient.ApplyDamage(context.Project);
                return GameplayOperationResult.Applied();
            }
        }

        private sealed class ApplyHitOperation : IGameplayOperation<WorldOwner, HitRequest>
        {
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
            public GameplayOperationResult Invoke(in GameplayOperationContext<WorldOwner, int> context)
            {
                if (context.Recipient == null || context.Project <= 0)
                {
                    return GameplayOperationResult.Reject("invalid shield target or amount");
                }

                context.Recipient.AddShield(context.Project);
                return GameplayOperationResult.Applied();
            }
        }

        private sealed class TickEffectOperation : IGameplayOperation<WorldOwner, EffectTickRequest>
        {
            public GameplayOperationResult Invoke
            (
                in GameplayOperationContext<WorldOwner, EffectTickRequest> context
            )
            {
                return context.Recipient.TickAndConsume(context.Project.Effect, context.Project.Cost)
                    ? GameplayOperationResult.Applied()
                    : GameplayOperationResult.NoOp("effect ended or resource is insufficient");
            }
        }

        private sealed class ActivationChargeCost : IAbilityActivationCost<object, int>
        {
            public AbilityActivationCheck Evaluate
            (
                in AbilityActivationContext<object, int> context
            )
            {
                var owner = (WorldOwner)context.Owner;
                return context.Project > 0 && owner.AvailableResource >= context.Project
                    ? AbilityActivationCheck.Allow()
                    : AbilityActivationCheck.Reject("activation resource is insufficient");
            }

            public void Pay
            (
                in AbilityActivationContext<object, int> context
            )
            {
                if (!((WorldOwner)context.Owner).TryConsume(context.Project))
                {
                    throw new InvalidOperationException("activation resource changed after evaluation");
                }
            }
        }

        private static ExecutionSession CreateSession(object owner, object instigator, ulong id)
        {
            var context = new ExecutionContext(owner, instigator);
            var flow = new FixedTimelineExecutionFlowDefinition(60f).CreateRuntime();
            return new ExecutionSession(id, context, flow);
        }

    #endregion

    #region 독립 Effect 소유권과 출처

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

    #region Recipient 부재와 Lease 정리

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

    #region 지속 효과 소비와 장기 예약

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

        [Test]
        public void TEST_GameplayOwnership_장기예약은지불된Cost와독립적으로중복해제되지않는다()
        {
            var owner = new WorldOwner(10);
            var abilityState = new AbilityState(new AbilityKey("Fixture"));
            var ability = new AbilityInstance<object>(new object(), abilityState);
            var binding = new AbilityBinding<object>(new AbilityEntryKey("Fixture"), ability);
            var context = new AbilityActivationContext<object, int>(owner, owner, binding, 1);
            var policy = new AbilityActivationPolicy<object, int>
            (
                cost: new IAbilityActivationCost<object, int>[]
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
