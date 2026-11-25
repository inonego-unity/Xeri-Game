/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : AbilityCooldownState.cs
수정일 : 2026-11-24

# 설명
Ability 하나의 Cooldown 규칙(시작·종료·단축·일시정지·재개·시간 진행)을 소유하는 내부 부품.
Core Timer를 저장소로 쓰며 값 읽기(ITimerProgress)만 AbilityState에 노출한다.

# 특이사항, 제약사항
변경 메서드는 실제로 바뀌었는지를 bool로 반환하고 Revision·알림은 이 부품을 소유한 AbilityState가 맡는다.
잘못된 인자(NaN·무한대·음수)는 ValidateDuration·ValidateReduction이 ArgumentOutOfRangeException으로 거절한다.
Unity 직렬화는 Timer 하나를 이 부품 안에 저장한다.
========================================================================= BLOCK_HEADER_END */

using System;

using UnityEngine;

namespace inonego.Xeri.Game
{
    using Xeri;
    using Xeri.Utility;

    // ======================================================================
    /// <summary>
    /// <br/> Ability 하나의 Cooldown 규칙을 소유하는 내부 부품.
    /// <br/> 변경 여부만 반환하고 Revision과 알림은 소유한 AbilityState가 처리한다.
    /// </summary>
    // ======================================================================
    [Serializable]
    internal sealed class AbilityCooldownState : IDeepCloneable<AbilityCooldownState>
    {

    #region 필드

        // ------------------------------------------------------------
        /// <summary>
        /// Cooldown 시계를 읽기 전용으로 제공한다.
        /// </summary>
        // ------------------------------------------------------------
        internal ITimerProgress Progress => timer;

        [SerializeField]
        private Timer timer = new();

        // ----------------------------------------------------------------------
        /// <summary>
        /// Cooldown이 진행 중이거나 일시정지되지 않아 시작할 수 있는 상태인지 반환한다.
        /// </summary>
        // ----------------------------------------------------------------------
        internal bool IsReady => !timer.IsRunning && !timer.IsPaused;

    #endregion

    #region 복제

        // ------------------------------------------------------------
        /// <summary>
        /// 복제에 쓸 빈 Cooldown 부품을 생성한다.
        /// </summary>
        // ------------------------------------------------------------
        public AbilityCooldownState @new() => new();

        // ------------------------------------------------------------
        /// <summary>
        /// source의 Cooldown 시계를 알림 없이 복사한다.
        /// </summary>
        // ------------------------------------------------------------
        public void CloneFrom(AbilityCooldownState source)
        {
            timer.CloneFrom(source.timer);
        }

    #endregion

    #region 인자 검사

        // ------------------------------------------------------------
        /// <summary>
        /// 시작할 지속시간이 유한한 0 이상인지 확인한다.
        /// </summary>
        // ------------------------------------------------------------
        internal static void ValidateDuration(float duration)
        {
            if (float.IsNaN(duration) || float.IsInfinity(duration) || duration < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(duration));
            }
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 줄이는 양이 유한한 0 이상인지 확인한다.
        /// </summary>
        // ------------------------------------------------------------
        internal static void ValidateReduction(float amount)
        {
            if (float.IsNaN(amount) || float.IsInfinity(amount) || amount < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(amount));
            }
        }

    #endregion

    #region 변경

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> 시작 가능하면 양수 지속시간으로 Cooldown을 시작하고 true를 반환한다.
        /// <br/> 진행 중이거나 일시정지된 Cooldown이 있으면 false를 반환한다.
        /// </summary>
        // ----------------------------------------------------------------------
        internal bool TryBegin(float duration)
        {
            if (!IsReady)
            {
                return false;
            }

            timer.Start(duration);
            return true;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 진행 중인 Cooldown을 일시정지하고 실제 변경 여부를 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        internal bool Pause()
        {
            if (!timer.IsRunning)
            {
                return false;
            }

            timer.Pause();
            return true;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 일시정지된 Cooldown을 재개하고 실제 변경 여부를 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        internal bool Resume()
        {
            if (!timer.IsPaused)
            {
                return false;
            }

            timer.Resume();
            return true;
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> 남은 시간을 amount만큼 줄이고 소진되면 종료한다.
        /// <br/> 시작하지 않았거나 float 정밀도로 남은 시간이 그대로면 false를 반환한다.
        /// </summary>
        // ----------------------------------------------------------------------
        internal bool Reduce(float amount)
        {
            if (amount <= 0f || IsReady)
            {
                return false;
            }

            var remaining = timer.RemainingTime;

            if (amount < remaining)
            {
                timer.RemainingTime = remaining - amount;

                // Timer는 float 정밀도로 남은 시간을 반영하므로 대입한 뒤의 실제 값으로 판정한다.
                var reduced = timer.RemainingTime;

                if (reduced == remaining)
                {
                    return false;
                }

                if (reduced <= 0f)
                {
                    timer.Stop();
                }

                return true;
            }

            timer.Stop();
            return true;
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// 진행 중이거나 일시정지된 Cooldown을 종료하고 실제 변경 여부를 반환한다.
        /// </summary>
        // ----------------------------------------------------------------------
        internal bool Reset()
        {
            if (IsReady)
            {
                return false;
            }

            timer.Stop();
            return true;
        }

    #endregion

    #region 시간 진행

        // ----------------------------------------------------------------------
        /// <summary>
        /// deltaTime만큼 진행하고 진행 중이던 Cooldown이 이번에 끝났는지 반환한다.
        /// </summary>
        // ----------------------------------------------------------------------
        internal bool Advance(float deltaTime)
        {
            var wasRunning = timer.IsRunning;

            timer.Tick(deltaTime);

            return wasRunning && IsReady;
        }

    #endregion

    }
}
