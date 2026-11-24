/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : ExecutionChannel.cs
수정일 : 2026-11-20

# 설명
배타 실행 채널 하나에 활성 ExecutionSession을 최대 하나만 두고 선점을 중재한다.
채널은 Session을 직접 소유하므로 별도 Token 없이, Session이 끝나면 OnEnded로 채널이 비워진다.
선점은 이전 Session을 Replaced·Interrupted로 끝낸 뒤 새 Session을 시작하는 순서로만 일어난다.
선점 규칙은 프로젝트가 판정 함수로 주입하고 채널은 Claim 값을 해석하지 않는다.

# 특이사항, 제약사항
전환(판정·이전 종료·새 시작) 중의 TryStart는 false다. 이전 Session 종료 통지나 새 Session 시작 안에서 다시 선점할 수 없다.
이미 시작한 Session은 받지 않는다. 채널이 이전 종료 뒤 새 시작 순서를 보장해야 하기 때문이다.
저장·복제하지 않는다. Session이 비영속이므로 채널도 실행에서 파생되는 상태다.
========================================================================= BLOCK_HEADER_END */

using System;

namespace inonego.Xeri.Game
{
    // ============================================================
    /// <summary>
    /// 점유 중인 채널에 새 실행을 요청했을 때의 판정.
    /// </summary>
    // ============================================================
    public enum ExecutionChannelDecision { Reject, Replace, Interrupt }

    // ======================================================================
    /// <summary>
    /// <br/> 활성 ExecutionSession을 최대 하나만 두는 배타 실행 채널.
    /// <br/> TClaim은 프로젝트가 정하는 점유 정보이며 판정 함수에만 전달된다.
    /// </summary>
    // ======================================================================
    public sealed class ExecutionChannel<TClaim>
    {

    #region 필드

        private readonly Func<TClaim, TClaim, ExecutionChannelDecision> arbitrate;

        // 점유할 때마다 대리자를 만들지 않도록 종료 처리기를 한 번만 만든다.
        private readonly Action<ExecutionSession, ExecutionEndReason> endedHandler;

        // ------------------------------------------------------------
        /// <summary>
        /// 채널을 점유한 Session. 비어 있으면 null이다.
        /// </summary>
        // ------------------------------------------------------------
        public ExecutionSession Active => active;

        private ExecutionSession active = null;

        // ------------------------------------------------------------
        /// <summary>
        /// 점유 Session의 Claim. 비어 있으면 default다.
        /// </summary>
        // ------------------------------------------------------------
        public TClaim ActiveClaim => activeClaim;

        private TClaim activeClaim = default;

        // ------------------------------------------------------------
        /// <summary>
        /// 채널이 점유 중인지 여부.
        /// </summary>
        // ------------------------------------------------------------
        public bool IsOccupied => active != null;

        // ----------------------------------------------------------------------
        /// <summary>
        /// 판정·이전 종료·새 시작이 진행 중인지 여부. 이 동안 TryStart는 false다.
        /// </summary>
        // ----------------------------------------------------------------------
        public bool IsTransitioning => isTransitioning;

        private bool isTransitioning = false;

    #endregion

    #region 생성자

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> (현재 Claim, 요청 Claim)으로 선점을 판정하는 함수로 채널을 생성한다.
        /// <br/> 판정 함수는 점유 중인 채널에만 호출된다.
        /// </summary>
        // ----------------------------------------------------------------------
        public ExecutionChannel(Func<TClaim, TClaim, ExecutionChannelDecision> arbitrate)
        {
            this.arbitrate = arbitrate ?? throw new ArgumentNullException(nameof(arbitrate));
            endedHandler   = HandleEnded;
        }

    #endregion

    #region 시작

        // ------------------------------------------------------------
        /// <summary>
        /// 상태를 바꾸지 않고 claim으로 시작할 수 있는지 확인한다.
        /// </summary>
        // ------------------------------------------------------------
        public bool CanStart(in TClaim claim)
        {
            if (isTransitioning)
            {
                return false;
            }

            return active == null || Decide(claim) != ExecutionChannelDecision.Reject;
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> 판정이 허락하면 이전 Session을 끝내고 session을 시작해 채널을 점유한다.
        /// <br/> 이전 종료나 새 시작이 실패하면 채널은 비고 예외를 그대로 전달한다.
        /// <br/> 이전 Session은 되살리지 않는다.
        /// </summary>
        // ----------------------------------------------------------------------
        public bool TryStart(ExecutionSession session, in TClaim claim)
        {
            if (session == null)
            {
                throw new ArgumentNullException(nameof(session));
            }

            if (session.HasBegun)
            {
                throw new ArgumentException("시작하지 않은 Session만 채널에서 시작할 수 있습니다.", nameof(session));
            }

            if (isTransitioning)
            {
                return false;
            }

            isTransitioning = true;

            try
            {
                if (active != null)
                {
                    var decision = Decide(claim);

                    if (decision == ExecutionChannelDecision.Reject)
                    {
                        return false;
                    }

                    // 이전 Session은 구독을 풀고 채널을 비운 뒤 끝내므로 늦은 종료 통지가 새 점유를 건드리지 않는다.
                    var previous = active;
                    Release(previous);

                    previous.End
                    (
                        decision == ExecutionChannelDecision.Replace
                            ? ExecutionEndReason.Replaced
                            : ExecutionEndReason.Interrupted
                    );
                }

                // 시작 실패는 Session이 Cancelled로 정리하며 OnEnded로 채널을 비운다.
                Occupy(session, claim);
                session.Begin();
            }
            finally
            {
                isTransitioning = false;
            }

            return true;
        }

        // 판정 함수 결과를 확인한다.
        private ExecutionChannelDecision Decide(in TClaim claim)
        {
            var decision = arbitrate(activeClaim, claim);

            var isDefined =
                decision is ExecutionChannelDecision.Reject or
                ExecutionChannelDecision.Replace or
                ExecutionChannelDecision.Interrupt;

            if (!isDefined)
            {
                throw new InvalidOperationException("정의되지 않은 ExecutionChannelDecision입니다.");
            }

            return decision;
        }

    #endregion

    #region 점유 수명

        private void Occupy(ExecutionSession session, in TClaim claim)
        {
            active      = session;
            activeClaim = claim;
            session.OnEnded += endedHandler;
        }

        private void Release(ExecutionSession session)
        {
            session.OnEnded -= endedHandler;
            active      = null;
            activeClaim = default;
        }

        // 점유 Session이 어떤 이유로든 끝나면 채널을 비운다.
        private void HandleEnded(ExecutionSession session, ExecutionEndReason reason)
        {
            if (ReferenceEquals(session, active))
            {
                Release(session);
            }
        }

    #endregion

    }
}
