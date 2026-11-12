/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : MockHP_IF.cs
수정일 : 2026-09-22

# 설명
값 직접 설정 및 이벤트 수동 발화가 가능한 HP Mock 추상 클래스 및 구현체.
상태 자동 전환 로직 없이 필드·프로퍼티만 제공한다.
ApplyHeal·ApplyDamage 는 노옵, MakeAlive·MakeDead 는 상태만 직접 설정한다.
========================================================================= BLOCK_HEADER_END */

using System;

using UnityEngine;

namespace inonego.Xeri.Game
{
    using Primitive;

    // ===========================================================================
    /// <summary>
    /// <br/> 값 직접 설정 및 이벤트 수동 발화가 가능한 HP Mock 추상 클래스.
    /// <br/> 상태 자동 전환 로직 없이 필드·프로퍼티만 제공한다.
    /// </summary>
    // ===========================================================================
    public abstract class MockHP<TNumeric, TValue>
        : IHP<TValue>
    where TNumeric : struct, INumeric<TNumeric, TValue>
    {

    #region 이벤트

        // ------------------------------------------------------------
        /// <summary>
        /// 현재 체력 값이 변경될 때 호출된다.
        /// </summary>
        // ------------------------------------------------------------
        public event ValueChangeEventHandler<TValue> OnValueChange = null;

        // ------------------------------------------------------------
        /// <summary>
        /// 최대 체력 값이 변경될 때 호출된다.
        /// </summary>
        // ------------------------------------------------------------
        public event ValueChangeEventHandler<TValue> OnMaxValueChange = null;

        // ------------------------------------------------------------
        /// <summary>
        /// 상태(생/사)가 변경될 때 호출된다.
        /// </summary>
        // ------------------------------------------------------------
        public event ValueChangeEventHandler<HPState> OnStateChange = null;

        // ------------------------------------------------------------
        /// <summary>
        /// 힐이 적용되었을 때 호출된다.
        /// </summary>
        // ------------------------------------------------------------
        public event HPApplyEventHandler<TValue> OnHeal = null;

        // ------------------------------------------------------------
        /// <summary>
        /// 데미지가 적용되었을 때 호출된다.
        /// </summary>
        // ------------------------------------------------------------
        public event HPApplyEventHandler<TValue> OnDamage = null;

    #endregion

    #region 필드

        // ------------------------------------------------------------
        /// <summary>
        /// 생존 여부.
        /// </summary>
        // ------------------------------------------------------------
        public bool IsAlive
        {
            get => isAlive;
            set => isAlive = value;
        }
        [SerializeField]
        private bool isAlive;

        // ------------------------------------------------------------
        /// <summary>
        /// 사망 여부.
        /// </summary>
        // ------------------------------------------------------------
        public bool IsDead
        {
            get => isDead;
            set => isDead = value;
        }
        [SerializeField]
        private bool isDead;

        // ------------------------------------------------------------
        /// <summary>
        /// 현재 상태(생/사).
        /// </summary>
        // ------------------------------------------------------------
        public HPState Current
        {
            get => current;
            set => current = value;
        }
        [SerializeField]
        private HPState current;

        // ------------------------------------------------------------
        /// <summary>
        /// 현재 체력 값. 외부에는 TValue 로 노출된다.
        /// </summary>
        // ------------------------------------------------------------
        public TValue Value
        {
            get => value.Get();
            set
            {
                TNumeric n = default;
                n.Set(value);
                this.value = n;
            }
        }
        [SerializeField]
        private TNumeric value;

        // ------------------------------------------------------------
        /// <summary>
        /// 최대 체력 값. 외부에는 TValue 로 노출된다.
        /// </summary>
        // ------------------------------------------------------------
        public TValue MaxValue
        {
            get => maxValue.Get();
            set
            {
                TNumeric n = default;
                n.Set(value);
                maxValue = n;
            }
        }
        [SerializeField]
        private TNumeric maxValue;

        // ------------------------------------------------------------
        /// <summary>
        /// 체력 비율 (0.0 ~ 1.0)
        /// </summary>
        // ------------------------------------------------------------
        public float Ratio
        {
            get
            {
                var max = maxValue.ToFloat();
                return max > 0f ? value.ToFloat() / max : 0f;
            }
        }

    #endregion

    #region 메서드

        // ------------------------------------------------------------
        /// <summary>
        /// 비율과 기준에 따라 적용할 양을 계산한다.
        /// </summary>
        // ------------------------------------------------------------
        public TValue CalculateApplyAmount(float ratio, HPApplyRatioType applyRatioType)
        {
            float baseVal = applyRatioType switch
            {
                HPApplyRatioType.ByValue        => value.ToFloat(),
                HPApplyRatioType.ByMaxValue     => maxValue.ToFloat(),
                HPApplyRatioType.ByMissingValue => maxValue.ToFloat() - value.ToFloat(),
                _                               => 0f
            };

            return value.FromFloat(baseVal * ratio).Get();
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 현재 상태를 살아있는 상태로 설정한다.
        /// </summary>
        // ------------------------------------------------------------
        public void MakeAlive(bool autoChangeValue = true) => current = HPState.Alive;

        // ------------------------------------------------------------
        /// <summary>
        /// 현재 상태를 죽어있는 상태로 설정한다.
        /// </summary>
        // ------------------------------------------------------------
        public void MakeDead(bool autoChangeValue = true) => current = HPState.Dead;

        // ------------------------------------------------------------
        /// <summary>
        /// 힐 적용. Mock 은 값 변경·이벤트 발화 없음. InvokeOnHeal 로 수동 발화한다.
        /// </summary>
        // ------------------------------------------------------------
        public void ApplyHeal(TValue amount) {}

        // ------------------------------------------------------------
        /// <summary>
        /// 데미지 적용. Mock 은 값 변경·이벤트 발화 없음. InvokeOnDamage 로 수동 발화한다.
        /// </summary>
        // ------------------------------------------------------------
        public void ApplyDamage(TValue amount) {}

    #endregion

    #region 이벤트 발화

        // ------------------------------------------------------------
        /// <summary>
        /// OnValueChange 를 수동으로 발화한다.
        /// </summary>
        // ------------------------------------------------------------
        public void InvokeOnValueChange(TValue prev, TValue next)    => OnValueChange?  .Invoke(this, new(prev, next));

        // ------------------------------------------------------------
        /// <summary>
        /// OnMaxValueChange 를 수동으로 발화한다.
        /// </summary>
        // ------------------------------------------------------------
        public void InvokeOnMaxValueChange(TValue prev, TValue next) => OnMaxValueChange?.Invoke(this, new(prev, next));

        // ------------------------------------------------------------
        /// <summary>
        /// OnStateChange 를 수동으로 발화한다.
        /// </summary>
        // ------------------------------------------------------------
        public void InvokeOnStateChange(HPState prev, HPState next)  => OnStateChange?  .Invoke(this, new(prev, next));

        // ------------------------------------------------------------
        /// <summary>
        /// OnHeal 을 수동으로 발화한다.
        /// </summary>
        // ------------------------------------------------------------
        public void InvokeOnHeal(TValue amount)                      => OnHeal?         .Invoke(this, new() { Amount = amount });

        // ------------------------------------------------------------
        /// <summary>
        /// OnDamage 를 수동으로 발화한다.
        /// </summary>
        // ------------------------------------------------------------
        public void InvokeOnDamage(TValue amount)                    => OnDamage?       .Invoke(this, new() { Amount = amount });

    #endregion

    }

    // ============================================================
    /// <summary>
    /// int 기반 HP Mock 구현 클래스.
    /// </summary>
    // ============================================================
    [Serializable]
    public sealed class MockHP_I : MockHP<XNumericI, int>, IReadOnlyHP_I { }

    // ============================================================
    /// <summary>
    /// float 기반 HP Mock 구현 클래스.
    /// </summary>
    // ============================================================
    [Serializable]
    public sealed class MockHP_F : MockHP<XNumericF, float>, IReadOnlyHP_F { }

}
