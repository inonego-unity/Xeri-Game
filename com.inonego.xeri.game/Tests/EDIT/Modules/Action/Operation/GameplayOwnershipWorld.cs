/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : GameplayOwnershipWorld.cs
수정일 : 2026-11-19

# 설명
Ownership 테스트의 World Owner 상태(체력·보호막·자원 예약과 소비·소유 Effect)와 Effect·요청 값을 제공한다.
World Owner에 적용하는 Effect 생성·피해·피격·보호막·Tick Operation과 자원 Charge Cost도 함께 둔다.
========================================================================= BLOCK_HEADER_END */

using System;
using System.Collections;
using System.Collections.Generic;

namespace inonego.Xeri.Game.TEST._GameplayOwnership
{
    using Xeri;
    using Xeri.Game;

    internal sealed class Effect
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

    internal sealed class WorldOwner
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

    internal sealed class EffectRequest
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

    internal sealed class EffectTickRequest
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

    internal sealed class HitRequest
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

    internal sealed class CreateEffectOperation : IGameplayOperation<WorldOwner, EffectRequest>
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

    internal sealed class ApplyDamageOperation : IGameplayOperation<WorldOwner, int>
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

    internal sealed class ApplyHitOperation : IGameplayOperation<WorldOwner, HitRequest>
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

    internal sealed class AddShieldOperation : IGameplayOperation<WorldOwner, int>
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

    internal sealed class TickEffectOperation : IGameplayOperation<WorldOwner, EffectTickRequest>
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

    internal sealed class ActivationChargeCost : IAbilityActivationCost<object>
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
}
