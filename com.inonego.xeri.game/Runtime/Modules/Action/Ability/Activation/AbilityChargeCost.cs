/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : AbilityChargeCost.cs
수정일 : 2026-11-09

# 설명
Ability Charge 비용을 현재 수량으로 검사하고 사용 확정 시 직접 소비한다.
수량·Recharge 시계는 AbilityChargeState가 소유하며 소비 예외 시 자동 복원하지 않는다.
========================================================================= BLOCK_HEADER_END */

using System;

namespace inonego.Xeri.Game
{
    // ============================================================
    /// <summary>
    /// Ability의 Charge 수량을 검사하고 사용 확정 시 소비한다.
    /// </summary>
    // ============================================================
    public sealed class AbilityChargeCost<TDefinition, TProjectContext> :
        IAbilityActivationCost<TDefinition, TProjectContext>
    where TDefinition : class
    {

    #region 비용 구성

        // ------------------------------------------------------------
        /// <summary>
        /// 확정 시 소비할 Charge 수량을 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        public int Amount { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 양수의 소비 수량을 가진 Charge 비용을 생성한다.
        /// </summary>
        // ------------------------------------------------------------
        public AbilityChargeCost(int amount = 1)
        {
            if (amount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(amount));
            }

            Amount = amount;
        }

    #endregion

    #region 검사와 소비

        // ------------------------------------------------------------
        /// <summary>
        /// Charge를 소비하지 않고 현재 잔량을 확인한다.
        /// </summary>
        // ------------------------------------------------------------
        public AbilityActivationCheck Evaluate
        (
            in AbilityActivationContext<TDefinition, TProjectContext> context
        )
        {
            return context.Ability?.State?.Charge?.Current >= Amount
                ? AbilityActivationCheck.Allow()
                : AbilityActivationCheck.Reject("Ability Charge가 부족합니다.");
        }

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> 현재 Charge에서 지정 수량을 소비한다.
        /// <br/> 검증 이후 잔량이 변경되어 소비할 수 없으면 예외를 전달한다.
        /// <br/> 알림 예외가 발생해도 소비한 Charge를 되돌리지 않는다.
        /// </summary>
        // ------------------------------------------------------------
        public void Pay
        (
            in AbilityActivationContext<TDefinition, TProjectContext> context
        )
        {
            var control = context.Ability?.State?.Control;
            if (control == null || !control.TryConsumeCharge(Amount))
            {
                throw new InvalidOperationException("Ability Charge 소비가 승인 후 실패했습니다.");
            }
        }

    #endregion

    }
}
