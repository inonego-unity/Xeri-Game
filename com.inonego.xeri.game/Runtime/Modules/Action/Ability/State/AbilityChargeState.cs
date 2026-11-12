/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : AbilityChargeState.cs
수정일 : 2026-11-09

# 설명
Ability의 Charge 수량과 순차 Recharge를 Cooldown과 독립 관리한다.
충전 시계의 시작 시점을 Charge 자체가 식별하며 모든 충전 완료를 동일한 경로로 처리한다.
Definition 사용 의미는 소유하지 않으며 충전량 경계와 수량 복원의 오버플로를 방지한다.
========================================================================= BLOCK_HEADER_END */

using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime;
using System.Runtime.ExceptionServices;

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
        IReadOnlyTimer Recharge { get; }

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
    public sealed class AbilityChargeState : IReadOnlyAbilityChargeState, ISerializationCallbackReceiver
    {

    #region 상태

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
        public IReadOnlyTimer Recharge => recharge;

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

        // Recharge 진행 중 수량이나 구성이 외부에서 변경됐는지 판별한다.
        [NonSerialized]
        private uint version = 0;

        // ------------------------------------------------------------
        /// <summary>
        /// Charge 구성 재설정 횟수를 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        internal uint ConfigurationVersion => configurationVersion;

        [NonSerialized]
        private uint configurationVersion = 0;

        // 이 Charge 소유자가 새 충전 시계를 시작한 횟수.

        // ------------------------------------------------------------
        /// <summary>
        /// 새 충전 시계가 시작된 횟수를 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        internal uint RechargeVersion => rechargeVersion;

        [NonSerialized]
        private uint rechargeVersion = 0;

        [NonSerialized]
        private bool isAdvancing = false;

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

    #region 시간 진행

        // ----------------------------------------------------------------------
        /// <summary>
        /// 경과 시간을 소비하면서 필요한 경우 여러 Charge의 연속 충전을 진행한다.
        /// </summary>
        // ----------------------------------------------------------------------
        public void Advance(float deltaTime)
        {
            if (float.IsNaN(deltaTime) || float.IsInfinity(deltaTime))
            {
                throw new ArgumentOutOfRangeException(nameof(deltaTime));
            }

            if (isAdvancing)
            {
                throw new InvalidOperationException("Recharge 진행 중 중첩 Advance는 허용하지 않습니다.");
            }

            recharge ??= new Timer();
            if (deltaTime <= 0f || IsFull || !recharge.IsRunning)
            {
                return;
            }

            isAdvancing = true;
            try
            {
                AdvanceRecharge((double)deltaTime);
            }
            finally
            {
                isAdvancing = false;
            }
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 동일 시간 기준으로 충전하고 이벤트는 개별 발행한다.
        /// </summary>
        // ------------------------------------------------------------
        private void AdvanceRecharge(double remainingDeltaTime)
        {
            List<Exception> notificationErrors = null;
            while
            (
                remainingDeltaTime > 0f &&
                !IsFull &&
                recharge.IsRunning
            )
            {
                var timer = recharge;
                var initialVersion = version;
                var initialConfigurationVersion = configurationVersion;
                var remainingRechargeTime = timer.RemainingTime;

                if (remainingDeltaTime < remainingRechargeTime)
                {
                    // float 반올림으로 미완료 구간의 종료 이벤트가 앞서 발생하지 않게 한다.
                    var nextElapsed = (float)((double)timer.ElapsedTime + remainingDeltaTime);
                    if (nextElapsed >= timer.Duration)
                    {
                        nextElapsed = BitConverter.Int32BitsToSingle
                        (
                            BitConverter.SingleToInt32Bits(timer.Duration) - 1
                        );
                    }

                    timer.ElapsedTime = nextElapsed;
                    break;
                }

                // 완료량을 먼저 확정해야 완료 이벤트의 소비자가 새 Charge를 볼 수 있다.
                // 콜백 변경으로 버전이 바뀌어도 이미 완료한 충전은 사라지지 않는다.
                current++;
                // 알림 실패는 보관하고, 동일 구성이 유지되면 남은 시간도 끝까지 적용한다.
                try
                {
                    timer.Tick(remainingRechargeTime);
                }
                catch (Exception exception)
                {
                    (notificationErrors ??= new List<Exception>()).Add(exception);
                }

                if (configurationVersion != initialConfigurationVersion ||
                    !ReferenceEquals(timer, recharge))
                {
                    // 구성 교체는 새로운 시계이므로 이전 Tick의 잔여시간을 넘기지 않는다.
                    break;
                }

                if (version != initialVersion)
                {
                    // 기존 수량의 소비·복원은 충전 시계 교체가 아니다.
                    // 남은 시간은 동일 Owner의 다음 충전 주기에 계속 적용한다.
                    try
                    {
                        BeginRechargeIfNeeded();
                    }
                    catch (Exception exception)
                    {
                        (notificationErrors ??= new List<Exception>()).Add(exception);
                        if (!recharge.IsRunning)
                        {
                            break;
                        }
                    }

                    remainingDeltaTime -= remainingRechargeTime;
                    continue;
                }

                try
                {
                    BeginRechargeIfNeeded();
                }
                catch (Exception exception)
                {
                    (notificationErrors ??= new List<Exception>()).Add(exception);
                    // Start 알림이 실패해도 동일 구성으로 새 주기가 실행 중이면 계속 진행한다.
                    if (configurationVersion != initialConfigurationVersion ||
                        !ReferenceEquals(timer, recharge) ||
                        (!IsFull && !recharge.IsRunning))
                    {
                        break;
                    }
                }

                if (configurationVersion != initialConfigurationVersion ||
                    !ReferenceEquals(timer, recharge))
                {
                    break;
                }

                remainingDeltaTime -= remainingRechargeTime;
                if (version != initialVersion)
                {
                    // Run 콜백의 수량 소비·복원은 구성 변경이 아니므로 남은 시간은 유효하다.
                    continue;
                }

            }

            if (notificationErrors?.Count == 1)
            {
                ExceptionDispatchInfo.Capture(notificationErrors[0]).Throw();
            }

            if (notificationErrors?.Count > 1)
            {
                throw new AggregateException("Charge 충전 알림 처리 중 복수의 오류가 발생했습니다.", notificationErrors);
            }
        }

    #endregion

    #region 충전량 구성과 소비·복원

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

            version++;
            configurationVersion++;
            this.capacity = capacity;
            this.rechargeDuration = rechargeDuration;
            current = startFull ? capacity : 0;
            recharge = new Timer();
            BeginRechargeIfNeeded();
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 현재 잔량이 충분하면 소비하고 필요한 충전을 시작한다.
        /// </summary>
        // ------------------------------------------------------------
        internal bool TryConsume(int amount)
        {
            if (amount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(amount));
            }

            if (current < amount)
            {
                return false;
            }

            version++;
            current -= amount;
            BeginRechargeIfNeeded();
            return true;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 최대치를 넘지 않도록 Charge를 복원하고 실제 복원량을 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        internal int Restore(int amount)
        {
            if (amount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(amount));
            }

            // Capacity까지 복원 가능한 수량을 먼저 구해 정수 덧셈 오버플로를 막는다.
            var restored = Math.Min(amount, capacity - current);
            if (restored > 0)
            {
                version++;
            }

            current += restored;

            if (IsFull)
            {
                recharge?.Stop();
            }
            else
            {
                BeginRechargeIfNeeded();
            }

            return restored;
        }

    #endregion

    #region 충전 주기 복원

        // ----------------------------------------------------------------------
        /// <summary>
        /// 완료 알림 중 저장된 Ready Timer도 복원 뒤 다음 충전 주기를 이어간다.
        /// </summary>
        // ----------------------------------------------------------------------
        public void OnBeforeSerialize()
        {
            // NONE
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 역직렬화 후 필요한 다음 충전 주기를 복원한다.
        /// </summary>
        // ------------------------------------------------------------
        public void OnAfterDeserialize()
        {
            if (capacity > 0 && rechargeDuration > 0f &&
                !float.IsNaN(rechargeDuration) && !float.IsInfinity(rechargeDuration))
            {
                BeginRechargeIfNeeded();
            }
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 충전이 필요하고 타이머가 준비된 경우에만 다음 주기를 시작한다.
        /// </summary>
        // ------------------------------------------------------------
        private void BeginRechargeIfNeeded()
        {
            if (IsFull)
            {
                recharge?.Stop();
                return;
            }

            recharge ??= new Timer();
            if (!recharge.IsRunning && !recharge.IsPaused)
            {
                rechargeVersion++;
                recharge.Start(rechargeDuration);
            }
        }

    #endregion

    }

}