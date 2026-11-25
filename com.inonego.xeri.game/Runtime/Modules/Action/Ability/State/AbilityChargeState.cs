/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : AbilityChargeState.cs
수정일 : 2026-11-23

# 설명
Ability의 Charge 수량과 순차 Recharge를 Cooldown과 독립 관리한다.
충전 시계의 시작 시점을 Charge 자체가 식별하며 모든 충전 완료를 동일한 경로로 처리한다.
Spec 사용 의미는 소유하지 않으며 충전량 경계와 수량 복원의 오버플로를 방지한다.
충전 Timer는 내부 저장소로만 쓰고 값 읽기(ITimerProgress)만 공개하므로 진행 중 외부 코드를 호출하지 않는다.
복제는 수량·구성·충전 시계를 변경 알림 없이 정확히 덮어쓴다.
Resize는 현재 수량과 충전 경과 시간을 유지한 채 구성을 바꾸고, ReduceRecharge는 현재 주기의 남은 시간만 줄이며 초과분은 다음 주기로 넘기지 않는다.
========================================================================= BLOCK_HEADER_END */

using System;
using System.Collections;
using System.Collections.Generic;

using UnityEngine;

namespace inonego.Xeri.Game
{
    using Xeri;
    using Xeri.Utility;

    // ============================================================
    /// <summary>
    /// Charge 수량과 Recharge 진행을 상태 변경 없이 관찰하는 계약.
    /// </summary>
    // ============================================================
    public interface IReadOnlyAbilityChargeState
    {

        // ------------------------------------------------------------
        /// <summary>
        /// 설정된 최대 Charge 수량을 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        int Capacity { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 현재 보유한 Charge 수량을 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        int Current { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 현재 소비 가능한 Charge가 있는지 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        bool HasCharge { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// Charge가 최대 수량인지 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        bool IsFull { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 현재 충전 타이머를 읽기 전용으로 제공한다.
        /// </summary>
        // ------------------------------------------------------------
        ITimerProgress Recharge { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 한 Charge를 충전하는 데 필요한 설정 시간을 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        float RechargeDuration { get; }
    }

    // ============================================================
    /// <summary>
    /// Cooldown과 독립적으로 Charge 소비와 순차 충전 시간을 관리한다.
    /// </summary>
    // ============================================================
    [Serializable]
    public sealed class AbilityChargeState :
        IReadOnlyAbilityChargeState, ISerializationCallbackReceiver, IDeepCloneable<AbilityChargeState>
    {

    #region 필드

        // ------------------------------------------------------------
        /// <summary>
        /// 설정된 최대 Charge 수량을 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        public int Capacity => capacity;

        [SerializeField, Min(1)]
        private int capacity = 1;

        // ------------------------------------------------------------
        /// <summary>
        /// 현재 보유한 Charge 수량을 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        public int Current => current;

        [SerializeField, Min(0)]
        private int current = 1;

        // ------------------------------------------------------------
        /// <summary>
        /// 현재 소비 가능한 Charge가 있는지 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        public bool HasCharge => current > 0;

        // ------------------------------------------------------------
        /// <summary>
        /// Charge가 최대 수량인지 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        public bool IsFull => current >= capacity;

        // ------------------------------------------------------------
        /// <summary>
        /// 현재 충전 타이머를 읽기 전용으로 제공한다.
        /// </summary>
        // ------------------------------------------------------------
        public ITimerProgress Recharge => recharge;

        [SerializeField]
        private Timer recharge = new();

        // ------------------------------------------------------------
        /// <summary>
        /// 설정된 한 회 충전 소요 시간을 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        public float RechargeDuration => rechargeDuration;

        [SerializeField, Min(0.01f)]
        private float rechargeDuration = 1f;

    #endregion

    #region 생성자

        // ------------------------------------------------------------
        /// <summary>
        /// Unity 직렬화에 사용하는 기본 Charge 상태를 생성한다.
        /// </summary>
        // ------------------------------------------------------------
        public AbilityChargeState()
        {
            // NONE
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 용량과 충전 시간을 지정하여 Charge 상태를 생성한다.
        /// </summary>
        // ------------------------------------------------------------
        public AbilityChargeState
        (
            int capacity,
            float rechargeDuration,
            bool startFull = true
        )
        {
            Configure(capacity, rechargeDuration, startFull);
        }

    #endregion

    #region 복제

        // ------------------------------------------------------------
        /// <summary>
        /// 빈 AbilityChargeState 인스턴스를 생성한다.
        /// </summary>
        // ------------------------------------------------------------
        public AbilityChargeState @new() => new AbilityChargeState();

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> source의 수량·구성·충전 시계를 변경 알림 없이 복제한다.
        /// <br/> 충전 Timer 인스턴스는 유지한다.
        /// </summary>
        // ------------------------------------------------------------
        public void CloneFrom(AbilityChargeState source)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            if (ReferenceEquals(source, this))
            {
                return;
            }

            capacity = source.capacity;
            current = source.current;
            rechargeDuration = source.rechargeDuration;
            recharge.CloneFrom(source.recharge);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// other와 수량·구성·충전 시계 값이 모두 같은지 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        internal bool IsSame(AbilityChargeState other)
        {
            return other != null
                && capacity == other.capacity
                && current == other.current
                && rechargeDuration.Equals(other.rechargeDuration)
                && recharge.Current == other.recharge.Current
                && recharge.Duration.Equals(other.recharge.Duration)
                && recharge.ElapsedTime.Equals(other.recharge.ElapsedTime);
        }

    #endregion

    #region 인자 검사

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> 용량은 1 이상, 충전 주기는 유한한 양수인지 확인한다.
        /// <br/> 값 규칙의 유일한 정의이며 State의 공개 메서드도 이것을 부른다.
        /// </summary>
        // ----------------------------------------------------------------------
        internal static void ValidateConfiguration(int capacity, float rechargeDuration)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            if
            (
                float.IsNaN(rechargeDuration) ||
                float.IsInfinity(rechargeDuration) ||
                rechargeDuration <= 0f
            )
            {
                throw new ArgumentOutOfRangeException(nameof(rechargeDuration));
            }
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> 소비·추가 수량이 양수인지 확인한다.
        /// <br/> 값 규칙의 유일한 정의이며 State의 공개 메서드도 이것을 부른다.
        /// </summary>
        // ----------------------------------------------------------------------
        internal static void ValidateAmount(int amount)
        {
            if (amount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(amount));
            }
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> 충전 시간을 줄이는 양이 유한한 0 이상인지 확인한다.
        /// <br/> 값 규칙의 유일한 정의이며 State의 공개 메서드도 이것을 부른다.
        /// </summary>
        // ----------------------------------------------------------------------
        internal static void ValidateReduction(float amount)
        {
            if (float.IsNaN(amount) || float.IsInfinity(amount) || amount < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(amount));
            }
        }

    #endregion

    #region 구성 변경

        // ------------------------------------------------------------
        /// <summary>
        /// 최대 Charge와 충전 주기를 재설정하고 초기 충전 상태를 선택한다.
        /// </summary>
        // ------------------------------------------------------------
        internal void Configure
        (
            int capacity,
            float rechargeDuration,
            bool startFull
        )
        {
            ValidateConfiguration(capacity, rechargeDuration);

            this.capacity = capacity;
            this.rechargeDuration = rechargeDuration;
            current = startFull ? capacity : 0;

            // 읽기 전용 참조를 들고 있는 쪽이 계속 같은 Timer를 보도록 인스턴스를 유지한다.
            recharge.Stop();
            recharge.Reset();
            BeginRechargeWhenNeeded();
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> 현재 수량과 충전 경과 시간을 유지한 채 용량과 충전 주기를 바꾼다.
        /// <br/> 용량이 줄면 현재 수량을 용량으로 줄이고, 늘어도 수량을 지급하지 않는다.
        /// <br/> 새 충전 주기가 이미 지난 경과 시간 이하면 그 주기를 완료한다.
        /// <br/> 실제로 바뀌었는지 반환한다.
        /// </summary>
        // ----------------------------------------------------------------------
        internal bool Resize(int capacity, float rechargeDuration)
        {
            ValidateConfiguration(capacity, rechargeDuration);

            if (capacity == this.capacity && rechargeDuration.Equals(this.rechargeDuration))
            {
                return false;
            }

            this.capacity = capacity;
            this.rechargeDuration = rechargeDuration;
            current = Math.Min(current, capacity);

            if (!IsFull && (recharge.IsRunning || recharge.IsPaused))
            {
                // Core Timer는 Duration을 바꿔도 경과 시간을 유지한다.
                recharge.Duration = rechargeDuration;

                if (recharge.RemainingTime <= 0f)
                {
                    CompleteRecharge();
                    return true;
                }
            }

            BeginRechargeWhenNeeded();
            return true;
        }

    #endregion

    #region 수량 변경

        // ------------------------------------------------------------
        /// <summary>
        /// 현재 잔량이 충분하면 소비하고 필요한 충전을 시작한다.
        /// </summary>
        // ------------------------------------------------------------
        internal bool TryConsume(int amount)
        {
            ValidateAmount(amount);

            if (current < amount)
            {
                return false;
            }

            current -= amount;
            BeginRechargeWhenNeeded();
            return true;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 최대치를 넘지 않도록 Charge 수량을 더하고 실제 증가량을 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        internal int Add(int amount)
        {
            ValidateAmount(amount);

            // Capacity까지 더할 수 있는 수량을 먼저 구해 정수 덧셈 오버플로를 막는다.
            var added = Math.Min(amount, capacity - current);
            current += added;
            BeginRechargeWhenNeeded();
            return added;
        }

    #endregion

    #region 시간 진행

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> 경과 시간만큼 충전 주기를 순서대로 진행하고 완료한 Charge 수를 반환한다.
        /// <br/> 진행 중에는 외부 코드를 호출하지 않는다.
        /// </summary>
        // ----------------------------------------------------------------------
        internal int Advance(float deltaTime)
        {
            if (float.IsNaN(deltaTime) || float.IsInfinity(deltaTime))
            {
                throw new ArgumentOutOfRangeException(nameof(deltaTime));
            }

            var remainingDeltaTime = (double)deltaTime;
            var recharged = 0;

            while (remainingDeltaTime > 0d && !IsFull && recharge.IsRunning)
            {
                var remainingRechargeTime = recharge.RemainingTime;
                if (remainingDeltaTime < remainingRechargeTime)
                {
                    recharge.ElapsedTime = NextIncompleteElapsed(remainingDeltaTime);
                    break;
                }

                CompleteRecharge();
                recharged++;
                remainingDeltaTime -= remainingRechargeTime;
            }

            return recharged;
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> 진행 중인 충전 주기의 남은 시간을 amount만큼 줄이고 변경 여부를 반환한다.
        /// <br/> 남은 시간 이상이면 그 주기를 완료하며 초과분은 다음 주기로 넘기지 않는다.
        /// </summary>
        // ----------------------------------------------------------------------
        internal bool ReduceRecharge(float amount)
        {
            ValidateReduction(amount);

            if (amount <= 0f || IsFull || (!recharge.IsRunning && !recharge.IsPaused))
            {
                return false;
            }

            if (amount < recharge.RemainingTime)
            {
                var elapsed = recharge.ElapsedTime;
                var nextElapsed = NextIncompleteElapsed(amount);

                // float 정밀도로 경과 시간이 그대로면 바뀐 것이 없다.
                if (nextElapsed == elapsed)
                {
                    return false;
                }

                recharge.ElapsedTime = nextElapsed;
                return true;
            }

            CompleteRecharge();
            return true;
        }

    #endregion

    #region 내부 처리

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/> 진행 중인 충전 주기를 완료해 Charge를 하나 더하고 필요하면 다음 주기를 시작한다.
        /// <br/> 시간 진행과 State의 변경 메서드가 같은 완료 처리를 쓴다.
        /// </summary>
        // --------------------------------------------------------------------------------
        private void CompleteRecharge()
        {
            current++;

            // 남은 시간만큼 Tick하면 float 반올림으로 경과 시간 합이 지속 시간에 못 미쳐
            // 타이머가 멈추지 않을 수 있으므로 이번 주기를 직접 완료 상태로 끝낸다.
            recharge.ElapsedTime = recharge.Duration;
            recharge.Stop();
            BeginRechargeWhenNeeded();
        }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/> 현재 경과 시간에 amount를 더한 다음 경과 시간을 반환한다.
        /// <br/> float 반올림으로 미완료 구간이 완료로 바뀌지 않게 지속 시간 바로 아래로 제한한다.
        /// </summary>
        // --------------------------------------------------------------------------------
        private float NextIncompleteElapsed(double amount)
        {
            var nextElapsed = (float)((double)recharge.ElapsedTime + amount);
            if (nextElapsed >= recharge.Duration)
            {
                nextElapsed = BitConverter.Int32BitsToSingle
                (
                    BitConverter.SingleToInt32Bits(recharge.Duration) - 1
                );
            }

            return nextElapsed;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 충전이 필요하고 타이머가 준비된 경우에만 다음 주기를 시작한다.
        /// </summary>
        // ------------------------------------------------------------
        private void BeginRechargeWhenNeeded()
        {
            if (IsFull)
            {
                recharge.Stop();
                return;
            }

            if (!recharge.IsRunning && !recharge.IsPaused)
            {
                recharge.Start(rechargeDuration);
            }
        }

    #endregion

    #region 직렬화 복원

        // ------------------------------------------------------------
        /// <summary>
        /// 직렬화 전에 처리할 내용이 없다.
        /// </summary>
        // ------------------------------------------------------------
        public void OnBeforeSerialize()
        {
            // NONE
        }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/> 역직렬화한 값을 구성 규칙의 범위로 고친 뒤 필요한 다음 충전 주기를 복원한다.
        /// <br/> 용량은 1 이상, 잔량은 0~용량으로 맞추고
        /// <br/> 충전 시간이 유한한 양수가 아니면 기본값 1초로 되돌린다.
        /// </summary>
        // --------------------------------------------------------------------------------
        public void OnAfterDeserialize()
        {
            capacity = Math.Max(1, capacity);
            current = Math.Clamp(current, 0, capacity);
            if (!(rechargeDuration > 0f) || float.IsInfinity(rechargeDuration))
            {
                rechargeDuration = 1f;
            }

            BeginRechargeWhenNeeded();
        }

    #endregion

    }

}