/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : AbilityState.cs
수정일 : 2026-11-19

# 설명
Owner별 Ability 지속 상태, Cooldown, Charge와 Values를 독립 관리한다.
Cooldown·Charge Timer는 내부 저장소로만 쓰고 값 읽기(ITimerProgress)만 공개하며, 시간 진행 중에는 외부 코드를 호출하지 않는다.
Cooldown 종료·Charge 충전 알림은 진행이 끝난 뒤 보낸다.
외부 변경은 AbilityStateControl에서 수행하고 active Execution 상태는 보관하지 않는다.
실제로 바뀐 이산 변경마다 Revision을 한 번 올리며 시간 진행(Advance)은 Revision을 바꾸지 않는다.
Unity 직렬화로 복원하면 Revision을 올리고 복원된 Values를 즉시 관찰한다. 같은 Key 데이터만, 사용 중이 아닐 때만 복원한다.
CloneFrom은 인스턴스와 구독을 유지한 채 변경 알림 없이 복제하고 Key 불일치·사용 중 복제를 거부한다.
========================================================================= BLOCK_HEADER_END */

using System;
using System.Collections;
using System.Collections.Generic;

using UnityEngine;

namespace inonego.Xeri.Game
{
    using Xeri;
    using Xeri.Serializable;
    using Xeri.Utility;

    // ================================================================================
    /// <summary>
    /// <br/> 한 Owner에 귀속되는 Ability 지속 runtime state.
    /// <br/> 메모리 안의 복원은 CloneFrom으로, 저장 데이터 복원은 Unity 직렬화로 수행한다.
    /// <br/> 어느 쪽이든 복원하면 Revision이 1 증가한다.
    /// <br/> CloneFrom은 Key 불일치와 사용 중 복제를 예외로 거부한다.
    /// <br/> 직렬화 복원은 콜백 안에서 검사할 수 없다.
    /// <br/> 호출자가 같은 Key, 사용 중 아님을 지켜야 한다.
    /// </summary>
    // ================================================================================
    [Serializable]
    public class AbilityState : ISerializationCallbackReceiver, IDeepCloneable<AbilityState>
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
        public ITimerProgress Cooldown => cooldown;

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
        public bool IsCooldownReady => !cooldown.IsRunning && !cooldown.IsPaused;

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

    #region 이벤트

        // ------------------------------------------------------------
        /// <summary>
        /// 진행 중이던 Cooldown이 시간 진행으로 끝난 뒤 발생한다.
        /// </summary>
        // ------------------------------------------------------------
        public event Action<AbilityState> OnCooldownEnded = null;

        // ------------------------------------------------------------
        /// <summary>
        /// 시간 진행으로 Charge가 충전된 뒤 충전된 수와 함께 발생한다.
        /// </summary>
        // ------------------------------------------------------------
        public event Action<AbilityState, int> OnChargeRecharged = null;

    #endregion

    #region 생성자

        // ------------------------------------------------------------
        /// <summary>
        /// Unity 직렬화를 위한 기본 AbilityState를 생성한다.
        /// </summary>
        // ------------------------------------------------------------
        public AbilityState()
        {
            ObserveValues();
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
            
            ObserveValues();
        }

    #endregion

    #region 복제

        // ------------------------------------------------------------
        /// <summary>
        /// 같은 Ability Key를 가진 빈 AbilityState 인스턴스를 생성한다.
        /// </summary>
        // ------------------------------------------------------------
        public AbilityState @new() => Key.IsValid ? new AbilityState(Key) : new AbilityState();

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> source의 Cooldown·Values·Charge를 변경 알림 없이 복제한다.
        /// <br/> 복제 뒤 Revision을 1 올리며 인스턴스와 구독은 유지한다.
        /// <br/> 같은 Key의 상태만, 사용 중이 아닐 때만 복제한다.
        /// </summary>
        // ----------------------------------------------------------------------
        public void CloneFrom(AbilityState source)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            if (ReferenceEquals(source, this))
            {
                return;
            }

            // 직렬화로 들어온 앞뒤 공백까지 AbilityKey와 같은 규칙으로 비교한다.
            if (Key != source.Key)
            {
                throw new ArgumentException("같은 Ability Key의 상태만 복제할 수 있습니다.", nameof(source));
            }

            if (isInUse)
            {
                throw new InvalidOperationException("사용 중인 AbilityState는 복제로 덮어쓸 수 없습니다.");
            }

            cooldown.CloneFrom(source.cooldown);

            ObserveValues();
            values.CloneFrom(source.values);

            // 구성된 Charge는 인스턴스를 유지해 읽기 전용 참조와 Timer 구독이 끊기지 않게 한다.
            if (source.charge == null)
            {
                charge = null;
            }
            else if (charge == null)
            {
                charge = source.charge.Clone();
            }
            else
            {
                charge.CloneFrom(source.charge);
            }

            MarkChanged();
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

    #region 시간 진행

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> Cooldown과 Charge 시계를 동일한 deltaTime으로 진행한다.
        /// <br/> 진행 중에는 외부 코드를 호출하지 않고 끝난 뒤 종료·충전을 알린다.
        /// </summary>
        // ----------------------------------------------------------------------
        public void Advance(float deltaTime)
        {
            if (float.IsNaN(deltaTime) || float.IsInfinity(deltaTime))
            {
                throw new ArgumentOutOfRangeException(nameof(deltaTime));
            }

            var clampedDeltaTime = Mathf.Max(0f, deltaTime);

            var wasCooling = cooldown.IsRunning;
            cooldown.Tick(clampedDeltaTime);
            var cooldownEnded = wasCooling && !cooldown.IsRunning && !cooldown.IsPaused;
            var recharged = charge?.Advance(clampedDeltaTime) ?? 0;

            // 진행이 모두 끝난 뒤에 알린다.
            RaiseAdvanced(cooldownEnded, recharged);
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> 진행이 끝난 상태로 Cooldown 종료와 Charge 충전을 순서대로 알린다.
        /// <br/> 모든 구독자에게 보낸 뒤 구독자 예외를 전달한다.
        /// </summary>
        // ----------------------------------------------------------------------
        private void RaiseAdvanced(bool cooldownEnded, int recharged)
        {
            var failures = new ExceptionBuffer();

            if (cooldownEnded && OnCooldownEnded != null)
            {
                foreach (Action<AbilityState> observer in OnCooldownEnded.GetInvocationList())
                {
                    try
                    {
                        observer(this);
                    }
                    catch (Exception exception)
                    {
                        failures.Add(exception);
                    }
                }
            }

            if (recharged > 0 && OnChargeRecharged != null)
            {
                foreach (Action<AbilityState, int> observer in OnChargeRecharged.GetInvocationList())
                {
                    try
                    {
                        observer(this, recharged);
                    }
                    catch (Exception exception)
                    {
                        failures.Add(exception);
                    }
                }
            }

            failures.ThrowWhenAny("Ability 상태 알림 중 복수의 오류가 발생했습니다.");
        }

    #endregion

    #region 쿨다운 상태

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> Cooldown이 시작 가능한 경우에만 지속시간을 설정한다.
        /// <br/> 지속시간 0은 시작할 것이 없으므로 아무것도 바꾸지 않고 true를 반환한다.
        /// </summary>
        // ----------------------------------------------------------------------
        internal bool TryBeginCooldown(float duration)
        {
            if (duration <= 0f)
            {
                return true;
            }

            if (cooldown.IsRunning || cooldown.IsPaused)
            {
                return false;
            }

            MarkChanged();
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
            if (!cooldown.IsRunning && !cooldown.IsPaused)
            {
                return false;
            }

            MarkChanged();
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
                (!cooldown.IsRunning && !cooldown.IsPaused)
            )
            {
                return false;
            }

            var remaining = cooldown.RemainingTime;
            if (amount < remaining)
            {
                cooldown.RemainingTime = remaining - amount;

                // Timer는 float 정밀도로 남은 시간을 반영하므로 대입한 뒤의 실제 값으로 판정한다.
                var reduced = cooldown.RemainingTime;
                if (reduced == remaining)
                {
                    return false;
                }

                MarkChanged();
                if (reduced <= 0f)
                {
                    cooldown.Stop();
                }

                return true;
            }

            MarkChanged();
            cooldown.Stop();
            return true;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 진행 중인 Cooldown을 일시정지하고 실제 변경 여부를 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        internal bool PauseCooldown()
        {
            if (!cooldown.IsRunning)
            {
                return false;
            }

            MarkChanged();
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
            if (!cooldown.IsPaused)
            {
                return false;
            }

            MarkChanged();
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
            // 검증은 상태를 바꾸기 전에 끝나므로 예외면 아무것도 바뀌지 않는다.
            if (charge == null)
            {
                charge = new AbilityChargeState(capacity, rechargeDuration, startFull);
            }
            else
            {
                charge.Configure(capacity, rechargeDuration, startFull);
            }

            MarkChanged();
            return charge;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 설정된 Charge에서 지정 수량을 소비할 수 있는지 확인한다.
        /// </summary>
        // ------------------------------------------------------------
        internal bool TryConsumeCharge(int amount)
        {
            if (charge == null || !charge.TryConsume(amount))
            {
                return false;
            }

            MarkChanged();
            return true;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 현재 Capacity를 초과하지 않도록 Charge를 복원한다.
        /// </summary>
        // ------------------------------------------------------------
        internal int RestoreCharge(int amount)
        {
            var restored = charge?.Restore(amount) ?? 0;
            if (restored > 0)
            {
                MarkChanged();
            }

            return restored;
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> Charge를 소비하고 지불 전 상태로 정확히 되돌리는 환급 영수증을 반환한다.
        /// <br/> 환급은 수량과 충전 시계 진행을 변경 알림 없이 함께 복원하며
        /// <br/> 지불 뒤 Charge가 변경됐으면 예외를 던진다.
        /// </summary>
        // ----------------------------------------------------------------------
        internal AbilityCostReceipt PayCharge(int amount)
        {
            var paidCharge = charge;
            var before = paidCharge?.Clone();

            if (!TryConsumeCharge(amount))
            {
                throw new InvalidOperationException("Ability Charge 소비가 승인 후 실패했습니다.");
            }

            var after = paidCharge.Clone();

            return new AbilityCostReceipt
            (
                () =>
                {
                    if (!ReferenceEquals(charge, paidCharge) || !paidCharge.IsSame(after))
                    {
                        throw new InvalidOperationException("지불 이후 Charge가 변경되어 환급할 수 없습니다.");
                    }

                    MarkChanged();
                    paidCharge.CloneFrom(before);
                }
            );
        }

    #endregion

    #region Revision 반영

        // ------------------------------------------------------------
        /// <summary>
        /// 실제로 바뀐 이산 변경 하나에 대해 Revision을 한 번 올린다.
        /// </summary>
        // ------------------------------------------------------------
        private void MarkChanged()
        {
            revision++;
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> 현재 ValueRegistry의 변경을 Revision에 반영하도록 구독한다.
        /// <br/> 같은 인스턴스는 한 번만 구독한다.
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
            MarkChanged();
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

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> 복원이 끝나면 Revision을 한 번 올려 복원 이전 선택을 무효화한다.
        /// <br/> 복원된 Values를 즉시 관찰해 먼저 보관한 참조의 변경도 반영한다.
        /// </summary>
        // ----------------------------------------------------------------------
        public void OnAfterDeserialize()
        {
            ObserveValues();
            MarkChanged();
        }

    #endregion

    }
}
