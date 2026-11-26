/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : AbilityState.cs
수정일 : 2026-11-26

# 설명
Owner별 Ability 지속 상태, Cooldown, Charge와 Values를 독립 관리한다.
Cooldown·Charge Timer는 내부 저장소로만 쓰고 값 읽기(ITimerProgress)만 공개하며, 시간 진행 중에는 외부 코드를 호출하지 않는다.
Cooldown 종료·Charge 충전 알림은 진행이 끝난 뒤 보낸다.
Cooldown 규칙은 AbilityCooldownState, Charge 규칙은 AbilityChargeState가 소유하고 이 타입은 Key·Revision·알림·Values를 맡는다.
Cooldown·Charge 변경은 이 타입의 공개 메서드로 수행하며 잘못된 인자는 예외로, 상태 규칙에 맞지 않는 요청은 false나 0으로 알린다.
active Execution 상태는 보관하지 않는다.
실제로 바뀐 이산 변경마다 Revision을 한 번 올리며 시간 진행(Advance)은 Revision을 바꾸지 않는다.
구성하지 않은 Charge는 직렬화 왕복 뒤에도 구성하지 않은 채로 복원한다. Unity 직렬화로 복원하면 Revision을 올리고 복원된 Values를 즉시 관찰한다. 같은 Key 데이터만, 사용 중이 아닐 때만 복원한다.
CloneFrom은 인스턴스와 구독을 유지한 채 변경 알림 없이 복제하고 Key 불일치·사용 중 복제를 거부한다.
복제 중 Cooldown·Values·Charge 단계가 실패해도 나머지를 끝까지 진행하고 Revision을 올린 뒤 실패를 전달한다(실패한 단계는 이전 상태일 수 있다).
사용 구간 안의 Charge 지불은 환급 기록을 보관하고 영수증은 기록 번호로 환급한다. 기록은 사용 구간을 시작하고 끝낼 때 비운다.
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
    public class AbilityState :
        ISerializationCallbackReceiver, IDeepCloneable<AbilityState>, IRevisioned, IAbilityCostRefund
    {

    #region 필드

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
        public ITimerProgress Cooldown => cooldown.Progress;

        [SerializeField]
        private AbilityCooldownState cooldown = new();

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

        // Unity가 null인 클래스 필드를 기본 객체로 저장하므로 구성 여부를 따로 저장해 복원 때 null로 되돌린다.
        [SerializeField]
        private bool hasCharge = false;

        // ----------------------------------------------------------------------
        /// <summary>
        /// Cooldown이 시작 가능한 상태인지 반환한다. 실제 사용 승인은 Policy가 판단한다.
        /// </summary>
        // ----------------------------------------------------------------------
        public bool IsCooldownReady => cooldown.IsReady;

        // ------------------------------------------------------------
        /// <summary>
        /// 사용 구간 안인지 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        internal bool IsInUse => isInUse;

        [NonSerialized]
        private bool isInUse = false;

        // 사용 구간 안의 Charge 지불 기록과 마지막 기록 번호. 사용 구간을 시작하고 끝낼 때 기록을 비운다.
        [NonSerialized]
        private List<ChargePayment> payments = null;

        [NonSerialized]
        private int paymentNumber = 0;

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> 실제 외부 이산 변경마다 증가하는 변경 번호를 반환한다.
        /// <br/> Advance의 시간 진행은 변경 번호를 바꾸지 않는다.
        /// </summary>
        // ------------------------------------------------------------
        public Revision Revision => revision;

        [NonSerialized]
        private Revision revision = Revision.None;

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
        /// <br/> Cooldown·Values·Charge 중 하나가 실패해도 나머지를 진행하고
        /// <br/> Revision을 올린 뒤 실패를 전달한다.
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

            // 단계 하나가 실패해도 나머지를 진행해야 이미 바뀐 상태가 Revision에 반영된다.
            var failures = new ExceptionBuffer();

            try
            {
                cooldown.CloneFrom(source.cooldown);
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }

            try
            {
                ObserveValues();
                values.CloneFrom(source.values);
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }

            // 구성된 Charge는 인스턴스를 유지해 읽기 전용 참조와 Timer 구독이 끊기지 않게 한다.
            try
            {
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
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }

            MarkChanged();

            failures.ThrowWhenAny("AbilityState 복제 중 여러 구성이 실패했습니다.");
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
            payments?.Clear();
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 사용 구간을 종료하고 남은 Charge 지불 기록을 버린다.
        /// </summary>
        // ------------------------------------------------------------
        internal void ExitUse()
        {
            isInUse = false;
            payments?.Clear();
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

            var cooldownEnded = cooldown.Advance(clampedDeltaTime);
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

            if (cooldownEnded)
            {
                OnCooldownEnded.InvokeEach(this, ref failures);
            }

            if (recharged > 0)
            {
                OnChargeRecharged.InvokeEach(this, recharged, ref failures);
            }

            failures.ThrowWhenAny("Ability 상태 알림 중 복수의 오류가 발생했습니다.");
        }

    #endregion

    #region 쿨다운 변경

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/> 유효한 지속시간을 확인하고 Cooldown을 시작한다. 진행 중이거나 일시정지된 Cooldown이
        /// <br/> 있으면 시작하지 않고 false를 반환한다. 지속시간 0은 시작할 Cooldown이 없으므로
        /// <br/> 항상 true를 반환하며 진행 중인 Cooldown과 Revision에 영향을 주지 않는다.
        /// <br/> NaN·무한대·음수 지속시간은 ArgumentOutOfRangeException을 던진다.
        /// </summary>
        // --------------------------------------------------------------------------------
        public bool TryBeginCooldown(float duration)
        {
            AbilityCooldownState.ValidateDuration(duration);

            if (duration <= 0f)
            {
                return true;
            }

            return MarkChangedWhen(cooldown.TryBegin(duration));
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 진행 중인 Cooldown을 일시정지하고 실제 변경 여부를 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        public bool PauseCooldown() => MarkChangedWhen(cooldown.Pause());

        // ------------------------------------------------------------
        /// <summary>
        /// 일시정지된 Cooldown을 재개하고 실제 변경 여부를 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        public bool ResumeCooldown() => MarkChangedWhen(cooldown.Resume());

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> Cooldown의 남은 시간을 지정량만큼 줄이고 소진 시 종료한다.
        /// <br/> 실제로 바뀌었는지 반환한다.
        /// <br/> NaN·무한대·음수는 ArgumentOutOfRangeException을 던진다.
        /// </summary>
        // ----------------------------------------------------------------------
        public bool ReduceCooldown(float amount)
        {
            AbilityCooldownState.ValidateReduction(amount);

            return MarkChangedWhen(cooldown.Reduce(amount));
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 진행 중인 Cooldown을 종료하고 실제 변경 여부를 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        public bool ResetCooldown() => MarkChangedWhen(cooldown.Reset());

    #endregion

    #region 충전 변경

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

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> 현재 수량과 충전 경과 시간을 유지한 채 용량과 충전 주기를 바꾼다.
        /// <br/> 용량이 줄면 현재 수량을 용량으로 줄이고 늘어도 수량을 지급하지 않는다.
        /// <br/> 새 충전 주기가 이미 지난 경과 시간 이하면 그 주기를 완료한다.
        /// <br/> 이때 충전 알림은 보내지 않는다. 실제로 바뀌었는지 반환한다.
        /// <br/> Charge를 구성하지 않았으면 false를 반환한다.
        /// <br/> 처음부터 다시 구성하려면 ConfigureCharge를 쓴다.
        /// </summary>
        // ----------------------------------------------------------------------
        public bool ResizeCharge(int capacity, float rechargeDuration)
        {
            AbilityChargeState.ValidateConfiguration(capacity, rechargeDuration);

            return charge != null && MarkChangedWhen(charge.Resize(capacity, rechargeDuration));
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> 설정된 Charge에서 지정 수량을 소비할 수 있으면 소비하고 true를 반환한다.
        /// <br/> Charge를 구성하지 않았거나 수량이 모자라면 false를 반환한다.
        /// <br/> 0 이하 수량은 ArgumentOutOfRangeException을 던진다.
        /// </summary>
        // ----------------------------------------------------------------------
        public bool TryConsumeCharge(int amount)
        {
            AbilityChargeState.ValidateAmount(amount);

            return charge != null && MarkChangedWhen(charge.TryConsume(amount));
        }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/> 최대 용량을 넘지 않는 범위에서 Charge 수량을 더하고 실제 증가량을 반환한다.
        /// <br/> Charge를 구성하지 않았거나 이미 가득 찼으면 0을 반환한다.
        /// <br/> 0 이하 수량은 ArgumentOutOfRangeException을 던진다.
        /// </summary>
        // --------------------------------------------------------------------------------
        public int AddCharge(int amount)
        {
            AbilityChargeState.ValidateAmount(amount);

            var added = charge?.Add(amount) ?? 0;
            MarkChangedWhen(added > 0);

            return added;
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> 진행 중인 충전 주기의 남은 시간을 줄이고 변경 여부를 반환한다.
        /// <br/> 남은 시간 이상이면 그 주기를 완료하되 충전 알림은 보내지 않는다.
        /// <br/> 초과분은 다음 주기로 넘기지 않는다.
        /// </summary>
        // ----------------------------------------------------------------------
        public bool ReduceRecharge(float amount)
        {
            AbilityChargeState.ValidateReduction(amount);

            return charge != null && MarkChangedWhen(charge.ReduceRecharge(amount));
        }

    #endregion

    #region 충전 비용 지불

        // ============================================================
        /// <summary>
        /// 사용 구간 안에서 환급을 기다리는 Charge 지불 기록.
        /// </summary>
        // ============================================================
        private readonly struct ChargePayment
        {
            internal readonly int Number;
            internal readonly AbilityChargeState Charge;
            internal readonly AbilityChargeState.Snapshot Prev;
            internal readonly AbilityChargeState.Snapshot Next;

            internal ChargePayment
            (
                int number,
                AbilityChargeState charge,
                in AbilityChargeState.Snapshot prev,
                in AbilityChargeState.Snapshot next
            )
            {
                Number = number;
                Charge = charge;
                Prev = prev;
                Next = next;
            }
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
            var prev = paidCharge != null ? paidCharge.Capture() : default;

            if (!TryConsumeCharge(amount))
            {
                throw new InvalidOperationException("Ability Charge 소비가 승인 후 실패했습니다.");
            }

            var next = paidCharge.Capture();

            // 사용 구간 밖의 지불은 기록을 비울 시점이 없으므로 환급 동작을 영수증에 담는다.
            if (!isInUse)
            {
                return CreateRefundReceipt(paidCharge, prev, next);
            }

            // 사용 구간 안의 지불은 기록을 보관하고 영수증에는 기록 번호만 담아 지불마다 할당하지 않는다.
            paymentNumber++;
            payments ??= new List<ChargePayment>();
            payments.Add(new ChargePayment(paymentNumber, paidCharge, prev, next));

            return new AbilityCostReceipt(this, paymentNumber);
        }

        // 환급 동작을 담은 영수증을 만든다.
        // 캡처하는 람다를 PayCharge 밖에 두어야 기록 경로에서 클로저를 만들지 않는다.
        private AbilityCostReceipt CreateRefundReceipt
        (
            AbilityChargeState paidCharge,
            AbilityChargeState.Snapshot prev,
            AbilityChargeState.Snapshot next
        )
        {
            return AbilityCostReceipt.From
            (
                () => RefundCharge(paidCharge, prev, next)
            );
        }

        // 기록 번호로 지불을 찾아 되돌린다. 영수증은 한 번만 환급하므로 되돌린 기록은 지운다.
        // 환급이 거부되면 기록을 남겨 두어 상태가 맞아진 뒤 다시 시도할 수 있다.
        void IAbilityCostRefund.Refund(int token)
        {
            var index = FindPayment(token);
            if (index < 0)
            {
                throw new InvalidOperationException("이미 환급했거나 사용 구간이 끝난 Charge 영수증입니다.");
            }

            var record = payments[index];
            RefundCharge(record.Charge, record.Prev, record.Next);
            payments.RemoveAt(index);
        }

        // 환급은 보통 지불의 역순이므로 최근 기록부터 찾는다.
        private int FindPayment(int payment)
        {
            if (payments == null)
            {
                return -1;
            }

            for (var index = payments.Count - 1; index >= 0; index--)
            {
                if (payments[index].Number == payment)
                {
                    return index;
                }
            }

            return -1;
        }

        // 지불 뒤 Charge가 그대로일 때만 지불 전 상태로 변경 알림 없이 되돌린다.
        private void RefundCharge
        (
            AbilityChargeState paidCharge,
            in AbilityChargeState.Snapshot prev,
            in AbilityChargeState.Snapshot next
        )
        {
            if (!ReferenceEquals(charge, paidCharge) || !paidCharge.Capture().Equals(next))
            {
                throw new InvalidOperationException("지불 이후 Charge가 변경되어 환급할 수 없습니다.");
            }

            MarkChanged();
            paidCharge.RestoreFrom(prev);
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
            revision = revision.Inc();
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// 부품이 실제로 바뀌었다고 알린 경우에만 Revision을 올리고 그 값을 그대로 반환한다.
        /// </summary>
        // ----------------------------------------------------------------------
        private bool MarkChangedWhen(bool changed)
        {
            if (changed)
            {
                MarkChanged();
            }

            return changed;
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
        /// Unity 직렬화 전에 Charge를 구성했는지 기록한다.
        /// </summary>
        // ------------------------------------------------------------
        public void OnBeforeSerialize()
        {
            hasCharge = charge != null;
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> 복원이 끝나면 Revision을 한 번 올려 복원 이전 선택을 무효화한다.
        /// <br/> 복원된 Values를 즉시 관찰해 먼저 보관한 참조의 변경도 반영한다.
        /// <br/> 구성하지 않은 Charge는 구성하지 않은 상태(null)로 복원한다.
        /// </summary>
        // ----------------------------------------------------------------------
        public void OnAfterDeserialize()
        {
            // 구성하지 않은 Charge는 Unity가 만든 기본 객체를 버리고 구성하지 않은 상태로 되돌린다.
            if (!hasCharge)
            {
                charge = null;
            }

            ObserveValues();
            MarkChanged();
        }

    #endregion

    }
}
