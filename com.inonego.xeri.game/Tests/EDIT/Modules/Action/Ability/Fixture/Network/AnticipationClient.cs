/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : AnticipationClient.cs
수정일 : 2026-11-19

# 설명
권한 State를 바꾸지 않고 연출 값만 미리 시작하는 테스트 전용 Anticipation Client를 제공한다.
========================================================================= BLOCK_HEADER_END */

using System;
using System.Collections;
using System.Collections.Generic;

namespace inonego.Xeri.Game.TEST._Ability.Fixture
{
    using Xeri;
    using Xeri.Game;

    // ============================================================
    /// <summary>
    /// 결과를 기다리며 미리 시작한 연출 하나의 값이다.
    /// </summary>
    // ============================================================
    internal sealed class Presentation
    {
        internal int RequestID;
        internal int Cues;
        internal bool Confirmed;
        internal bool Cancelled;
        internal double CooldownUi;
    }

    // ======================================================================
    /// <summary>
    /// 권한 State를 바꾸지 않고 연출 값만 미리 시작하는 Anticipation Client이다.
    /// </summary>
    // ======================================================================
    internal sealed class AnticipationClient
    {

    #region 구성

        private readonly AuthorityClient client;
        private readonly Dictionary<int, Presentation> items = new();

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/> Client의 결과·세션 전환·Owner 제거 통지를 이 연출 계층이 받도록 연결한다.
        /// <br/> Client 하나에는 계층 하나만 연결할 수 있다.
        /// </summary>
        // --------------------------------------------------------------------------------
        internal AnticipationClient(AuthorityClient client)
        {
            this.client = client;
            client.Attach(Handle, Reset, Reset, null);
        }

    #endregion

    #region 연출 시작과 결과

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> 요청을 보내고 연출을 미리 시작한다. 입력을 받을 수 없거나
        /// <br/> 로컬 사전 검사에서 준비 상태가 아니면 null이다.
        /// </summary>
        // ------------------------------------------------------------
        internal Presentation Begin(int delayTicks)
        {
            if (!client.AcceptsInput || !client.Kit.IsReady(client.Owner)) return null;

            var request = client.Create();
            client.Send(request, delayTicks);
            var presentation = new Presentation
            {
                RequestID = request.RequestID, Cues = 1, CooldownUi = 5
            };
            items.Add(request.RequestID, presentation);
            return presentation;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 승인이면 확정 사건으로 교체하고, 아니면 연출을 취소한다.
        /// </summary>
        // ------------------------------------------------------------
        private void Handle(AuthorityResult result)
        {
            if (!items.TryGetValue(result.RequestID, out var presentation)) return;

            if (result.Status == AuthorityStatus.Accepted)
            {
                // 확정 사건으로 교체하며 Cue는 다시 재생하지 않는다.
                presentation.Confirmed = true;
                return;
            }

            Cancel(presentation);
        }

    #endregion

    #region 연출 취소

        // ------------------------------------------------------------
        /// <summary>
        /// 새 권한 세대로 전환하면 확정되지 않은 연출을 취소하고 비운다.
        /// </summary>
        // ------------------------------------------------------------
        private void Reset()
        {
            foreach (var presentation in items.Values)
            {
                if (presentation.Confirmed) continue;

                Cancel(presentation);
            }

            items.Clear();
        }

        private static void Cancel(Presentation presentation)
        {
            presentation.Cancelled = true;
            presentation.Cues = 0;
            presentation.CooldownUi = 0;
        }

    #endregion

    }
}
