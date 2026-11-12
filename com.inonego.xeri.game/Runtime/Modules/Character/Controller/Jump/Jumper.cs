/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : Jumper.cs
수정일 : 2026-11-07

# 설명
IJumper 구현체.
코요테 점프와 점프 버퍼를 Timer로 관리하며,
호출자가 전달한 접지 상태 전환에 따라 점프 상태를 관리한다.
========================================================================= BLOCK_HEADER_END */

using System;

using UnityEngine;

namespace inonego.Xeri.Game.Controller
{
    using Utility;

    // ============================================================
    /// <summary>
    /// 점프 기능을 담당하는 클래스입니다.
    /// </summary>
    // ============================================================
    [Serializable]
    public class Jumper : IJumper, INeedToInit
    {

    #region 필드

        // ------------------------------------------------------------
        /// <summary>
        /// 점프 실행이 허용된 상태인지 여부입니다.
        /// </summary>
        // ------------------------------------------------------------
        public bool IsJumpAllowed
        {
            get => isJumpAllowed;
            set => isJumpAllowed = value;
        }

        [SerializeField, ReadOnly]
        private bool isJumpAllowed = true;

        // ------------------------------------------------------------
        /// <summary>
        /// 점프 중인지 여부를 가져옵니다.
        /// </summary>
        // ------------------------------------------------------------
        public bool IsJumping => isJumping;

        [SerializeField, ReadOnly]
        private bool isJumping = false;

        // ------------------------------------------------------------
        /// <summary>
        /// 최대 점프 횟수입니다.
        /// </summary>
        // ------------------------------------------------------------
        public int MaxCount
        {
            get => maxCount;
            set
            {
                maxCount = Mathf.Max(0, value);

                // MaxCount 변경 시 현재 Count를 새 범위로 재계산합니다.
                Count = Count;
            }
        }

        [SerializeField]
        private int maxCount = 1;

        // ------------------------------------------------------------
        /// <summary>
        /// 현재 남은 점프 횟수입니다.
        /// </summary>
        // ------------------------------------------------------------
        public int Count
        {
            get => count;
            set => count = Mathf.Clamp(value, 0, maxCount);
        }

        [SerializeField]
        private int count = 0;

        // ------------------------------------------------------------
        /// <summary>
        /// 코요테 점프 지속 시간입니다.
        /// </summary>
        // ------------------------------------------------------------
        public float CoyoteJumpDuration
        {
            get => coyoteJumpDuration;
            set => coyoteJumpDuration = Mathf.Max(0f, value);
        }

        [SerializeField]
        private float coyoteJumpDuration = 0.1f;

        // ------------------------------------------------------------
        /// <summary>
        /// 점프 버퍼 지속 시간입니다.
        /// </summary>
        // ------------------------------------------------------------
        public float JumpBufferDuration
        {
            get => jumpBufferDuration;
            set => jumpBufferDuration = Mathf.Max(0f, value);
        }

        [SerializeField]
        private float jumpBufferDuration = 0.1f;

        // ------------------------------------------------------------
        /// <summary>
        /// 코요테 점프 타이머입니다.
        /// </summary>
        // ------------------------------------------------------------
        public IReadOnlyTimer CoyoteJumpTimer => coyoteJumpTimer;

        [SerializeField]
        private Timer coyoteJumpTimer = new Timer();

        // ------------------------------------------------------------
        /// <summary>
        /// 점프 버퍼 타이머입니다.
        /// </summary>
        // ------------------------------------------------------------
        public IReadOnlyTimer JumpBufferTimer => jumpBufferTimer;

        [SerializeField]
        private Timer jumpBufferTimer = new Timer();

        // ------------------------------------------------------------
        /// <summary>
        /// 직전 물리 Tick에 전달받은 접지 여부입니다.
        /// </summary>
        // ------------------------------------------------------------
        [SerializeField, ReadOnly]
        private bool wasGrounded = false;

    #endregion

    #region 이벤트

        // ------------------------------------------------------------
        /// <summary>
        /// 점프가 시작될 때 호출되는 이벤트입니다.
        /// </summary>
        // ------------------------------------------------------------
        public event EventHandler<JumpEventArgs> OnJump = null;

    #endregion

    #region 초기화

        // ------------------------------------------------------------
        /// <summary>
        /// 점프 상태와 타이머를 초기화합니다.
        /// </summary>
        // ------------------------------------------------------------
        public void Init()
        {
            coyoteJumpTimer.Stop();
            CancelPending();
            Reset();
            wasGrounded = false;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 점프 상태와 타이머를 해제합니다.
        /// </summary>
        // ------------------------------------------------------------
        public void Release()
        {
            coyoteJumpTimer.Stop();
            CancelPending();
            Reset();
            wasGrounded = false;
        }

    #endregion

    #region 메서드

        // ------------------------------------------------------------
        /// <summary>
        /// 점프를 실행하도록 트리거합니다.
        /// </summary>
        // ------------------------------------------------------------
        public void Trigger()
        {
            StartJumpBufferTimer();
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 현재 점프 조건이 충족되면 즉시 점프를 실행합니다.
        /// </summary>
        // ------------------------------------------------------------
        public bool TryJump()
        {
            var canJump = IsJumpAllowed &&
                          (coyoteJumpTimer.IsRunning || isJumping);

            if (!canJump || Count <= 0)
            {
                return false;
            }

            jumpBufferTimer.Stop();
            coyoteJumpTimer.Stop();

            Count--;
            isJumping = true;

            OnJump?.Invoke
            (
                this,
                new()
                {
                    MaxCount = MaxCount,
                    Count = Count
                }
            );
            return true;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 실행 대기 중인 점프 요청을 취소합니다.
        /// </summary>
        // ------------------------------------------------------------
        public void CancelPending()
        {
            jumpBufferTimer.Stop();
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 물리 갱신을 진행합니다.
        /// </summary>
        // ------------------------------------------------------------
        public void FixedTick(float fixedDeltaTime, bool isGrounded)
        {
            coyoteJumpTimer.Tick(fixedDeltaTime);

            var isTriggered = jumpBufferTimer.IsRunning;

            // 비접지에서 접지로 전환된 Tick에 점프 상태와 횟수를 복원한다.
            if (!wasGrounded && isGrounded)
            {
                Reset();
            }

            // 접지 중에는 이탈 직후에도 점프할 수 있도록 코요테 시간을 계속 갱신한다.
            if (isGrounded)
            {
                StartCoyoteJumpTimer();
            }

            if (isTriggered)
            {
                TryJump();
            }

            jumpBufferTimer.Tick(fixedDeltaTime);
            wasGrounded = isGrounded;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 점프 상태와 카운트를 초기화합니다.
        /// </summary>
        // ------------------------------------------------------------
        public void Reset()
        {
            isJumping = false;

            Count = MaxCount;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 코요테 점프 타이머를 시작합니다.
        /// </summary>
        // ------------------------------------------------------------
        private void StartCoyoteJumpTimer()
        {
            coyoteJumpTimer.Stop();
            coyoteJumpTimer.Start(CoyoteJumpDuration);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 점프 버퍼 타이머를 시작합니다.
        /// </summary>
        // ------------------------------------------------------------
        private void StartJumpBufferTimer()
        {
            jumpBufferTimer.Stop();
            jumpBufferTimer.Start(JumpBufferDuration);
        }

    #endregion

    }
}
