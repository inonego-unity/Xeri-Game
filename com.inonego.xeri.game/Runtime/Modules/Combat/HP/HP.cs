/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : HP.cs
수정일 : 2026-09-22

# 설명
제너릭 체력(HP) 추상 클래스 및 관련 열거형·구조체 정의.
INumeric<TSelf, TValue> 기반으로 int/float 양쪽을 지원한다.
========================================================================= BLOCK_HEADER_END */

using System;

using UnityEngine;

namespace inonego.Xeri.Game
{
    using Primitive;

    // ============================================================
    /// <summary>
    /// 체력 상태 (생/사).
    /// </summary>
    // ============================================================
    public enum HPState { Alive, Dead }

    // ============================================================
    /// <summary>
    /// 체력 적용 비율 기준.
    /// </summary>
    // ============================================================
    public enum HPApplyRatioType { ByValue, ByMaxValue, ByMissingValue }

    // ============================================================
    /// <summary>
    /// 체력 적용(힐/데미지) 이벤트 핸들러.
    /// </summary>
    // ============================================================
    public delegate void HPApplyEventHandler<TValue>(object sender, HPApplyEventArgs<TValue> e);

    // ============================================================
    /// <summary>
    /// 체력 적용(힐/데미지) 이벤트 인수.
    /// </summary>
    // ============================================================
    [Serializable]
    public struct HPApplyEventArgs<TValue>
    {
        public TValue Amount;
    }

    // ===========================================================================
    /// <summary>
    /// <br/> INumeric 기반 제너릭 체력 추상 클래스.
    /// <br/> 내부 저장은 TNumeric, 외부 API 는 TValue 로 노출한다.
    /// </summary>
    // ===========================================================================
    public abstract class HP<TNumeric, TValue> : IHP<TValue>
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
        public bool IsAlive => current == HPState.Alive;

        // ------------------------------------------------------------
        /// <summary>
        /// 사망 여부.
        /// </summary>
        // ------------------------------------------------------------
        public bool IsDead  => current == HPState.Dead;

        // ------------------------------------------------------------
        /// <summary>
        /// 현재 상태(생/사).
        /// </summary>
        // ------------------------------------------------------------
        public HPState Current => current;
        [SerializeField]
        private HPState current = HPState.Dead;

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
                SetValue(n);
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
                SetMax(n);
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

    #region 생성자

        protected HP() { }

    #endregion

    #region 메서드

        // ------------------------------------------------------------
        /// <summary>
        /// 현재 상태를 살아있는 상태로 설정한다.
        /// </summary>
        // ------------------------------------------------------------
        public void MakeAlive(bool autoChangeValue = true)
        {
            SetState(HPState.Alive, autoChangeValue);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 현재 상태를 죽어있는 상태로 설정한다.
        /// </summary>
        // ------------------------------------------------------------
        public void MakeDead(bool autoChangeValue = true)
        {
            SetState(HPState.Dead, autoChangeValue);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 힐(체력 회복)을 적용한다.
        /// </summary>
        // ------------------------------------------------------------
        public void ApplyHeal(TValue amount)
        {
            if (IsDead) return;

            TNumeric numAmount = default;
            numAmount.Set(amount);

            if (numAmount.IsNegative)
            {
            #if UNITY_EDITOR
                Debug.LogWarning("값이 0보다 작을 수 없습니다.");
            #endif
                return;
            }

            if (numAmount.IsZero) return;

            SetValue(value.Add(numAmount));

            OnHeal?.Invoke(this, new HPApplyEventArgs<TValue> { Amount = amount });
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 데미지(체력 감소)를 적용한다.
        /// </summary>
        // ------------------------------------------------------------
        public void ApplyDamage(TValue amount)
        {
            if (IsDead) return;

            TNumeric numAmount = default;
            numAmount.Set(amount);

            if (numAmount.IsNegative)
            {
            #if UNITY_EDITOR
                Debug.LogWarning("값이 0보다 작을 수 없습니다.");
            #endif
                return;
            }

            if (numAmount.IsZero) return;

            SetValue(value.Sub(numAmount));

            OnDamage?.Invoke(this, new HPApplyEventArgs<TValue> { Amount = amount });
        }

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

    #endregion

    #region 내부

        private void SetState(HPState state, bool autoChangeValue = true)
        {
            var (prev, next) = (current, state);

            if (prev == next) return;

            current = next;

            if (autoChangeValue)
            {
                // 살아날 때 → 최대 체력 전체 회복, 죽을 때 → 0
                SetValue(IsAlive ? maxValue : default, autoChangeState: false);
            }

            OnStateChange?.Invoke(this, new(prev, next));
        }

        private void SetValue(TNumeric n, bool autoChangeState = true)
        {
            var prev = value;
            var next = n.Clamp(default, maxValue);  // [0, maxValue] 범위로 제한

            if (prev.Equals(next)) return;

            if (autoChangeState)
            {
                // 값이 0 경계를 교차할 때 상태를 자동 전환한다.
                if      ( prev.IsPositive &&  next.IsZero    ) SetState(HPState.Dead,  autoChangeValue: false);
                else if ( prev.IsZero     &&  next.IsPositive) SetState(HPState.Alive, autoChangeValue: false);
            }

            value = next;

            OnValueChange?.Invoke(this, new(prev.Get(), next.Get()));
        }

        private void SetMax(TNumeric n)
        {
            var prev = maxValue;
            var next = n.Max(default);  // 최대 체력은 항상 0 이상

            if (prev.Equals(next)) return;

            maxValue = next;

            if (value.CompareTo(next) > 0)
            {
                SetValue(next);
            }

            OnMaxValueChange?.Invoke(this, new(prev.Get(), next.Get()));
        }

    #endregion

    }

}
