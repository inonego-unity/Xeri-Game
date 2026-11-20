/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : AbilityStateControl.cs
수정일 : 2026-11-19

# 설명
AbilityState를 외부 gameplay operation이 변경하는 공개 제어 경계를 제공한다.
Cooldown과 Charge 변경은 입력을 검사한 뒤 AbilityState의 내부 연산과 Revision 규칙을 그대로 따른다.
========================================================================= BLOCK_HEADER_END */

using System;

namespace inonego.Xeri.Game
{
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

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/> 유효한 지속시간을 확인하고 Cooldown을 시작한다. 진행 중이거나 일시정지된 Cooldown이
        /// <br/> 있으면 시작하지 않고 false를 반환한다. 지속시간 0은 시작할 Cooldown이 없으므로
        /// <br/> 항상 true를 반환하며 진행 중인 Cooldown과 Revision에 영향을 주지 않는다.
        /// </summary>
        // --------------------------------------------------------------------------------
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
