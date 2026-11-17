/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : AbilityState.cs
수정일 : 2026-11-17

# 설명
Owner별 Ability 지속 상태, Cooldown, Charge와 Values를 독립 관리한다.
동일 Advance 도중 새로 시작된 Charge 시계에는 이미 경과한 시간을 적용하지 않는다.
외부 변경은 AbilityStateControl에서 수행하고 active Execution 상태는 보관하지 않는다.
실제 외부 이산 변경마다 Revision을 올리며 시간 진행(Advance)은 Revision을 바꾸지 않는다.
Unity 직렬화로 복원하면 Revision을 올리고 Values 관찰을 다시 구독한다. 같은 Key 데이터만, 사용 중이 아닐 때만 복원한다.
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
    using Xeri.Serializable;
    using Xeri.Utility;

    // ======================================================================
    /// <summary>
    /// <br/> 한 Owner에 귀속되는 Ability 지속 runtime state.
    /// <br/> 복원은 Unity 직렬화(JsonUtility.FromJsonOverwrite 등)로 수행한다.
    /// <br/> 복원하면 Revision이 1 증가한다.
    /// <br/> 호출자는 같은 Ability Key의 데이터만 복원해야 한다.
    /// <br/> 사용 중에는 복원하지 않아야 하며 콜백 안에서는 검사할 수 없다.
    /// </summary>
    // ======================================================================
    [Serializable]
    public class AbilityState : ISerializationCallbackReceiver
    {

    #region 지속 상태

        // ------------------------------------------------------------
        /// <summary>
        /// Owner에 귀속된 Ability의 논리 Key를 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        public AbilityKey Key =>
            string.IsNullOrWhiteSpace(key)
                ? default
                : new AbilityKey(key);

        // ------------------------------------------------------------
        /// <summary>
        /// 직렬화되는 Ability Key 문자열을 보관한다.
        /// </summary>
        // ------------------------------------------------------------
        [SerializeField]
        protected string key = string.Empty;

        // ------------------------------------------------------------
        /// <summary>
        /// 현재 쿨다운 타이머를 읽기 전용으로 제공한다.
        /// </summary>
        // ------------------------------------------------------------
        public IReadOnlyTimer Cooldown => cooldown;

        // ------------------------------------------------------------
        /// <summary>
        /// 직렬화되는 Cooldown Timer를 보관한다.
        /// </summary>
        // ------------------------------------------------------------
        [SerializeField]
        protected Timer cooldown = new();

        // --------------------------------------------------------------------------------
        /// <summary>
        /// Execution별 임시 값과 달리 이 Ability 소유자의 직렬화 가능한 primitive 값을 보관한다.
        /// </summary>
        // --------------------------------------------------------------------------------
        public ValueRegistry Values
        {
            get
            {
                values ??= new ValueRegistry();
                ObserveValues();
                return values;
            }
        }

        [SerializeField]
        private ValueRegistry values = new();

        // Revision에 반영 중인 ValueRegistry 인스턴스.
        [NonSerialized]
        private ValueRegistry observedValues = null;

        // ------------------------------------------------------------
        /// <summary>
        /// 선택적으로 구성된 Charge 상태를 읽기 전용으로 제공한다.
        /// </summary>
        // ------------------------------------------------------------
        public IReadOnlyAbilityChargeState Charge => charge;

        [SerializeField]
        private AbilityChargeState charge = null;

        // ----------------------------------------------------------------------
        /// <summary>
        /// Cooldown이 시작 가능한 상태인지 반환한다. 실제 사용 승인은 Policy가 판단한다.
        /// </summary>
        // ----------------------------------------------------------------------
        public bool IsCooldownReady =>
            cooldown == null ||
            (!cooldown.IsRunning && !cooldown.IsPaused);

        [NonSerialized]
        private bool isAdvancing = false;

        [NonSerialized]
        private bool isInUse = false;

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> 실제 외부 이산 변경마다 증가하는 변경 번호를 반환한다.
        /// <br/> Advance의 시간 진행은 변경 번호를 바꾸지 않는다.
        /// </summary>
        // ------------------------------------------------------------
        public ulong Revision => revision;

        [NonSerialized]
        private ulong revision = 0;

        [NonSerialized]
        private AbilityStateControl control = null;

        // ------------------------------------------------------------
        /// <summary>
        /// Ability 지속 상태의 변경을 제한하는 제어 API를 제공한다.
        /// </summary>
        // ------------------------------------------------------------
        public AbilityStateControl Control
        {
            get
            {
                control ??= new AbilityStateControl(this);
                return control;
            }
        }

    #endregion

    #region 생성자

        // ------------------------------------------------------------
        /// <summary>
        /// Unity 직렬화를 위한 기본 AbilityState를 생성한다.
        /// </summary>
        // ------------------------------------------------------------
        public AbilityState()
        {
            // NONE
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 유효한 Ability Key를 가진 Owner 상태를 생성한다.
        /// </summary>
        // ------------------------------------------------------------
        public AbilityState(AbilityKey key)
        {
            if (!key.IsValid)
            {
                throw new ArgumentException("유효한 Ability Key가 필요합니다.", nameof(key));
            }

            this.key = key.Value;
        }

    #endregion

    #region 사용 구간

        // ------------------------------------------------------------
        /// <summary>
        /// 사용 구간에 진입한다. 이미 사용 중이면 예외를 던진다.
        /// </summary>
        // ------------------------------------------------------------
        internal void EnterUse()
        {
            if (isInUse)
            {
                throw new InvalidOperationException("AbilityState 사용 중 중첩 사용은 허용하지 않습니다.");
            }

            isInUse = true;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 사용 구간을 종료한다.
        /// </summary>
        // ------------------------------------------------------------
        internal void ExitUse()
        {
            isInUse = false;
        }

    #endregion

    #region 직렬화 복원

        // ------------------------------------------------------------
        /// <summary>
        /// Unity 직렬화 전에 호출되며 처리할 내용이 없다.
        /// </summary>
        // ------------------------------------------------------------
        public void OnBeforeSerialize()
        {
            // NONE
        }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/> 역직렬화(복원)가 끝나면 Revision을 한 번 올려 복원 이전 선택을 무효화한다.
        /// <br/> Values 관찰 기록은 비워 다음 Values 접근에서 다시 구독한다.
        /// </summary>
        // --------------------------------------------------------------------------------
        public void OnAfterDeserialize()
        {
            if (observedValues != null)
            {
                observedValues.OnChange -= OnValuesChange;
                observedValues = null;
            }

            revision++;
        }
    #endregion

    #region 시간 진행

        // ------------------------------------------------------------
        /// <summary>
        /// Cooldown과 Charge 시계를 동일한 deltaTime으로 진행한다.
        /// </summary>
        // ------------------------------------------------------------
        public void Advance(float deltaTime)
        {
            if (float.IsNaN(deltaTime) || float.IsInfinity(deltaTime))
            {
                throw new ArgumentOutOfRangeException(nameof(deltaTime));
            }

            if (isAdvancing)
            {
                throw new InvalidOperationException("AbilityState 진행 중 중첩 Advance는 허용하지 않습니다.");
            }

            isAdvancing = true;
            try
            {
                var safeDeltaTime = Mathf.Max(0f, deltaTime);

                // Tick 도중 새로 생성하거나 재설정한 상태에는 이미 경과한 시간을 적용하지 않는다.
                var currentCharge = charge;
                var chargeConfigurationVersion = currentCharge?.ConfigurationVersion;
                var chargeRechargeVersion = currentCharge?.RechargeVersion;
                var wasRecharging = currentCharge?.Recharge.IsRunning == true;
                cooldown ??= new Timer();
                List<Exception> errors = null;

                // 서로 독립적인 시계는 한 구독자의 실패로 다른 시계의 진행이 막히지 않는다.
                try
                {
                    cooldown.Tick(safeDeltaTime);
                }
                catch (Exception exception)
                {
                    (errors ??= new List<Exception>()).Add(exception);
                }

                if (currentCharge != null && ReferenceEquals(charge, currentCharge) &&
                    currentCharge.ConfigurationVersion == chargeConfigurationVersion &&
                    currentCharge.RechargeVersion == chargeRechargeVersion && wasRecharging)
                {
                    try
                    {
                        currentCharge.Advance(safeDeltaTime);
                    }
                    catch (Exception exception)
                    {
                        (errors ??= new List<Exception>()).Add(exception);
                    }
                }

                if (errors?.Count == 1)
                {
                    ExceptionDispatchInfo.Capture(errors[0]).Throw();
                }

                if (errors?.Count > 1)
                {
                    throw new AggregateException("Ability 상태 시간 진행 중 복수의 오류가 발생했습니다.", errors);
                }
            }
            finally
            {
                isAdvancing = false;
            }
        }

    #endregion

    #region 쿨다운 상태

        // ------------------------------------------------------------
        /// <summary>
        /// Cooldown이 시작 가능한 경우에만 지속시간을 설정한다.
        /// </summary>
        // ------------------------------------------------------------
        internal bool TryBeginCooldown(float duration)
        {
            cooldown ??= new Timer();

            if (duration <= 0f)
            {
                return true;
            }

            if (cooldown.IsRunning || cooldown.IsPaused)
            {
                return false;
            }

            revision++;
            cooldown.Start(duration);
            return true;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 진행 중인 Cooldown을 종료하고 실제 변경 여부를 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        internal bool ResetCooldown()
        {
            cooldown ??= new Timer();
            if (!cooldown.IsRunning && !cooldown.IsPaused)
            {
                return false;
            }

            revision++;
            cooldown.Stop();
            return true;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Cooldown 잔여시간을 감소시키고 소진되면 종료한다.
        /// </summary>
        // ------------------------------------------------------------
        internal bool ReduceCooldown(float amount)
        {
            if
            (
                amount <= 0f ||
                cooldown == null ||
                (!cooldown.IsRunning && !cooldown.IsPaused)
            )
            {
                return false;
            }

            revision++;
            if (amount >= cooldown.RemainingTime)
            {
                cooldown.Stop();
                return true;
            }

            cooldown.RemainingTime -= amount;
            return true;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 진행 중인 Cooldown을 일시정지하고 실제 변경 여부를 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        internal bool PauseCooldown()
        {
            cooldown ??= new Timer();
            if (!cooldown.IsRunning)
            {
                return false;
            }

            revision++;
            cooldown.Pause();
            return true;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 일시정지된 Cooldown을 재개하고 실제 변경 여부를 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        internal bool ResumeCooldown()
        {
            cooldown ??= new Timer();
            if (!cooldown.IsPaused)
            {
                return false;
            }

            revision++;
            cooldown.Resume();
            return true;
        }

    #endregion

    #region 충전 자원

        // ------------------------------------------------------------
        /// <summary>
        /// Charge 상태를 생성 또는 재설정하고 소유 상태에 연결한다.
        /// </summary>
        // ------------------------------------------------------------
        internal AbilityChargeState ConfigureCharge
        (
            int capacity,
            float rechargeDuration,
            bool startFull
        )
        {
            if (charge == null)
            {
                // 최초 구성은 검증이 끝난 인스턴스만 상태에 연결한다.
                charge = new AbilityChargeState(capacity, rechargeDuration, startFull);
                revision++;
            }
            else
            {
                // 재설정은 검증 이후 알림 중 예외가 나도 이미 적용되므로 구성 번호로 변경 여부를 판별한다.
                var configurationVersion = charge.ConfigurationVersion;
                try
                {
                    charge.Configure(capacity, rechargeDuration, startFull);
                }
                finally
                {
                    if (charge.ConfigurationVersion != configurationVersion)
                    {
                        revision++;
                    }
                }
            }

            return charge;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 설정된 Charge에서 지정 수량을 소비할 수 있는지 확인한다.
        /// </summary>
        // ------------------------------------------------------------
        internal bool TryConsumeCharge(int amount)
        {
            // 알림 도중 변경 번호를 읽는 쪽이 이미 바뀐 상태를 보도록 소비 전에 올린다.
            if (charge != null && amount > 0 && charge.Current >= amount)
            {
                revision++;
            }

            return charge != null && charge.TryConsume(amount);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 현재 Capacity를 초과하지 않도록 Charge를 복원한다.
        /// </summary>
        // ------------------------------------------------------------
        internal int RestoreCharge(int amount)
        {
            if (charge != null && amount > 0 && charge.Current < charge.Capacity)
            {
                revision++;
            }

            return charge?.Restore(amount) ?? 0;
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> Charge를 소비하고 지불 전 상태로 정확히 되돌리는 환급 영수증을 반환한다.
        /// <br/> 환급은 수량과 충전 시계 진행을 함께 복원하며
        /// <br/> 지불 뒤 Charge가 변경됐으면 예외를 던진다.
        /// </summary>
        // ----------------------------------------------------------------------
        internal AbilityCostReceipt PayCharge(int amount)
        {
            var paidCharge = charge;
            var beforeJson = paidCharge != null ? JsonUtility.ToJson(paidCharge) : null;

            if (!TryConsumeCharge(amount))
            {
                throw new InvalidOperationException("Ability Charge 소비가 승인 후 실패했습니다.");
            }

            var afterJson = JsonUtility.ToJson(paidCharge);

            return new AbilityCostReceipt
            (
                () =>
                {
                    if (!ReferenceEquals(charge, paidCharge) || JsonUtility.ToJson(paidCharge) != afterJson)
                    {
                        throw new InvalidOperationException("지불 이후 Charge가 변경되어 환급할 수 없습니다.");
                    }

                    revision++;
                    paidCharge.OverwriteFromJson(beforeJson);
                }
            );
        }

    #endregion

    #region Values 관찰

        // ----------------------------------------------------------------------
        /// <summary>
        /// 현재 ValueRegistry의 변경을 Revision에 반영하도록 한 번만 구독한다.
        /// </summary>
        // ----------------------------------------------------------------------
        private void ObserveValues()
        {
            if (ReferenceEquals(observedValues, values))
            {
                return;
            }

            if (observedValues != null)
            {
                observedValues.OnChange -= OnValuesChange;
            }

            values.OnChange += OnValuesChange;
            observedValues = values;
        }

        private void OnValuesChange()
        {
            revision++;
        }

    #endregion

    }

    // ======================================================================
    /// <summary>
    /// AbilityState를 외부 gameplay operation이 변경하는 공개 제어 경계.
    /// </summary>
    // ======================================================================
    public sealed class AbilityStateControl
    {

    #region 연결

        private readonly AbilityState state;

        // ------------------------------------------------------------
        /// <summary>
        /// 지정된 AbilityState를 제어하는 API를 생성한다.
        /// </summary>
        // ------------------------------------------------------------
        internal AbilityStateControl(AbilityState state)
        {
            this.state = state ?? throw new ArgumentNullException(nameof(state));
        }

    #endregion

    #region 쿨다운 제어

        // ------------------------------------------------------------
        /// <summary>
        /// 유효한 지속시간을 확인하고 실행 가능한 Cooldown만 시작한다.
        /// </summary>
        // ------------------------------------------------------------
        public bool TryBeginCooldown(float duration)
        {
            if (float.IsNaN(duration) || float.IsInfinity(duration) || duration < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(duration));
            }

            return state.TryBeginCooldown(duration);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 진행 중인 쿨다운을 종료하고 변경 여부를 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        public bool ResetCooldown() => state.ResetCooldown();

        // ------------------------------------------------------------
        /// <summary>
        /// 진행 중인 Cooldown을 일시정지하고 변경 여부를 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        public bool PauseCooldown() => state.PauseCooldown();

        // ------------------------------------------------------------
        /// <summary>
        /// 일시정지된 Cooldown을 재개하고 변경 여부를 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        public bool ResumeCooldown() => state.ResumeCooldown();

        // ------------------------------------------------------------
        /// <summary>
        /// Cooldown의 남은 시간을 지정량만큼 줄이고 소진 시 종료한다.
        /// </summary>
        // ------------------------------------------------------------
        public bool ReduceCooldown(float amount)
        {
            if (float.IsNaN(amount) || float.IsInfinity(amount) || amount < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(amount));
            }

            return state.ReduceCooldown(amount);
        }

    #endregion

    #region 충전량 제어

        // ------------------------------------------------------------
        /// <summary>
        /// Charge 용량과 충전 주기를 구성하고 읽기 전용 상태를 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        public IReadOnlyAbilityChargeState ConfigureCharge
        (
            int capacity,
            float rechargeDuration,
            bool startFull = true
        ) => state.ConfigureCharge(capacity, rechargeDuration, startFull);

        // ------------------------------------------------------------
        /// <summary>
        /// 유효한 수량만큼 Charge를 소비할 수 있는지 검사하고 소비한다.
        /// </summary>
        // ------------------------------------------------------------
        public bool TryConsumeCharge(int amount)
        {
            if (amount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(amount));
            }

            return state.TryConsumeCharge(amount);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 최대 용량 이하로 Charge를 복원하고 실제 증가량을 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        public int RestoreCharge(int amount)
        {
            if (amount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(amount));
            }

            return state.RestoreCharge(amount);
        }

    #endregion

    }
}
