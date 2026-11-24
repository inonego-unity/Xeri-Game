/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : AuthorityKit.cs
수정일 : 2026-11-19

# 설명
권한 Host와 Client가 Owner마다 쓰는 Ability State, Binding과 선택 규칙 묶음을 제공한다.
사용 규칙은 기본 Ability 하나에만 두고, 추가 Ability는 Ability 단위 복제 검증용 State로만 둔다.
========================================================================= BLOCK_HEADER_END */

using System;
using System.Collections;
using System.Collections.Generic;

namespace inonego.Xeri.Game.TEST._Ability.Fixture
{
    using Xeri;
    using Xeri.Game;

    // ============================================================
    /// <summary>
    /// 권한 Fixture의 Ability Spec이다. 내용은 쓰지 않는다.
    /// </summary>
    // ============================================================
    internal sealed class AuthoritySpec
    {
        // NONE
    }

    // ============================================================
    /// <summary>
    /// 한 Owner의 Ability State, Binding과 선택 규칙을 묶는다.
    /// </summary>
    // ============================================================
    internal sealed class AuthorityKit : IDisposable
    {

    #region 구성

        internal const string EntryName = "Use";

        internal readonly AbilityInstance<AuthoritySpec> Ability;
        internal readonly AbilityBindings<AuthoritySpec> Bindings = new();
        private readonly AbilityUseSelector<AuthoritySpec, int, string> selector;
        private readonly AbilityBindingLayer<AuthoritySpec> layer;
        private readonly Lease lease;

        internal AbilityState State => Ability.State;

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/> 기본 Ability 하나를 사용 Entry에 묶고 Charge·Cooldown 정책과 선택 규칙을 만든다.
        /// <br/> Cooldown은 시작에 성공한 뒤의 전이에서 시작한다.
        /// </summary>
        // --------------------------------------------------------------------------------
        internal AuthorityKit
        (
            string key, float cooldown, int charges,
            IEnumerable<IAbilityActivationCost<AuthoritySpec>> extraCosts = null,
            Action transition = null
        )
        {
            Ability = new AbilityInstance<AuthoritySpec>(new AuthoritySpec(), new AbilityState(new AbilityKey(key)));
            Ability.State.Control.ConfigureCharge(charges, 10f);
            states.Add(Ability.State);
            layer = new AbilityBindingLayer<AuthoritySpec>(0);
            layer.Set(new AbilityEntryKey(EntryName), Ability);
            lease = Bindings.AcquireLayer(layer);

            var choice = new AbilityUseChoice<AuthoritySpec, string>
            (
                key, CreatePolicy(extraCosts), transition: () => StartCooldown(cooldown, transition)
            );
            selector = new AbilityUseSelector<AuthoritySpec, int, string>
            (
                new Func<AbilityActivationContext<AuthoritySpec>, int, AbilityUseChoice<AuthoritySpec, string>>[]
                {
                    (_, _) => choice
                }
            );
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Cooldown 조건과 Charge 비용에 추가 비용을 더한 정책을 만든다.
        /// </summary>
        // ------------------------------------------------------------
        private static AbilityActivationPolicy<AuthoritySpec> CreatePolicy
        (
            IEnumerable<IAbilityActivationCost<AuthoritySpec>> extraCosts
        )
        {
            var costs = new List<IAbilityActivationCost<AuthoritySpec>>
            {
                new AbilityChargeCost<AuthoritySpec>(),
            };
            if (extraCosts != null)
            {
                costs.AddRange(extraCosts);
            }

            return new AbilityActivationPolicy<AuthoritySpec>
            (
                new IAbilityActivationRequirement<AuthoritySpec>[]
                {
                    new AbilityCooldownRequirement<AuthoritySpec>()
                },
                costs
            );
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// 시작에 성공한 사용의 전이로 Cooldown을 시작하고 추가 전이를 실행한다.
        /// </summary>
        // ----------------------------------------------------------------------
        private void StartCooldown(float cooldown, Action transition)
        {
            if (cooldown > 0f && !Ability.State.Control.TryBeginCooldown(cooldown))
            {
                throw new InvalidOperationException("Cooldown 시작이 승인 후 실패했습니다.");
            }

            transition?.Invoke();
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 리소스를 해제한다.
        /// </summary>
        // ------------------------------------------------------------
        public void Dispose()
        {
            lease.Release();
        }

    #endregion

    #region Ability 목록

        // ------------------------------------------------------------
        /// <summary>
        /// 기본 Ability를 처음으로 하는 이 Owner의 모든 Ability State이다.
        /// </summary>
        // ------------------------------------------------------------
        internal IReadOnlyList<AbilityState> States => states;
        private readonly List<AbilityState> states = new();

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> 사용 규칙 없이 State만 갖는 Ability를 Key 이름의 Entry로 추가한다.
        /// <br/> 같은 Key가 이미 있으면 예외를 던진다.
        /// </summary>
        // ----------------------------------------------------------------------
        internal AbilityState AddAbility(string key)
        {
            var abilityKey = new AbilityKey(key);
            if (FindState(abilityKey) != null)
            {
                throw new ArgumentException("이미 있는 Ability Key입니다.", nameof(key));
            }

            var ability = new AbilityInstance<AuthoritySpec>(new AuthoritySpec(), new AbilityState(abilityKey));
            layer.Set(new AbilityEntryKey(key), ability);
            states.Add(ability.State);
            return ability.State;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Key가 같은 Ability State를 반환하며 없으면 null이다.
        /// </summary>
        // ------------------------------------------------------------
        internal AbilityState FindState(AbilityKey key)
        {
            foreach (var state in states)
            {
                if (state.Key == key)
                {
                    return state;
                }
            }

            return null;
        }

    #endregion

    #region 사용 선택

        // ------------------------------------------------------------
        /// <summary>
        /// 이 Kit의 State와 Binding으로 사용을 다시 선택한다.
        /// </summary>
        // ------------------------------------------------------------
        internal bool TrySelect
        (
            string entry, int owner, object payload,
            out AbilityUseSelection<AuthoritySpec, string> selection, out string reason
        )
        {
            selection = null;
            if (!Bindings.TryResolve(new AbilityEntryKey(entry), out var binding))
            {
                reason = "Entry에 해당하는 Binding이 없습니다.";
                return false;
            }

            var context = new AbilityActivationContext<AuthoritySpec>(owner, owner, binding, payload);
            return selector.TrySelect(Bindings, context, out selection, out reason);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 기본 Entry를 지금 사용할 수 있는지 비용 소비 없이 확인한다.
        /// </summary>
        // ------------------------------------------------------------
        internal bool IsReady(int owner)
        {
            return TrySelect(EntryName, owner, 0, out var selection, out _)
                && selection.Evaluate().Succeeded;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 다시 선택한 뒤 한 번 사용을 시도한다.
        /// </summary>
        // ------------------------------------------------------------
        internal bool TryUse
        (
            string entry, int owner, object payload, AbilityBegin<string> begin,
            out AbilityActivationResult result, out AbilityUse<string> use
        )
        {
            use = default;
            if (!TrySelect(entry, owner, payload, out var selection, out var reason))
            {
                result = AbilityActivationResult.Reject(AbilityActivationFailure.InvalidState, reason: reason);
                return false;
            }

            return selection.TryActivate(begin, out result, out use);
        }

    #endregion

    }
}
