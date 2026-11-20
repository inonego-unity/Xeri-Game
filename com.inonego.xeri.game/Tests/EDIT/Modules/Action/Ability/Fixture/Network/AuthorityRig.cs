/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : AuthorityRig.cs
수정일 : 2026-11-19

# 설명
권한 Fake의 Tick 진행, 정리 예외 전달과 결과 집계를 돕는 정적 보조 동작을 제공한다.
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
    /// Tick 진행과 결과 집계를 돕는 정적 보조 동작이다.
    /// </summary>
    // ============================================================
    internal static class AuthorityRig
    {

    #region Tick 진행

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/> 한 Tick을 진행한다. 서버와 모든 Client의 State를 같은 시간만큼 진행하고
        /// <br/> 도착한 메시지를 전달한 뒤 서버 사건, Host, Client들, afterClient 순서로 처리한다.
        /// <br/> 시간 진행과 수신의 한 단계 실패는 나머지 단계를 막지 않고 모두 마친 뒤 전달한다.
        /// <br/> others는 client와 함께 진행할 다른 Client이다.
        /// </summary>
        // --------------------------------------------------------------------------------
        internal static void Step
        (
            FakeAbilityNetwork<AuthorityPacket> network, AuthorityHost host, AuthorityClient client,
            bool reverse = false, Action serverEvent = null, Action afterClient = null,
            IReadOnlyList<AuthorityClient> others = null
        )
        {
            network.AdvanceTick();
            var delta = network.TickDelta;
            List<Exception> failures = null;
            Run(() => host.Advance(delta), ref failures);
            Run(() => client.Advance(delta), ref failures);
            if (others != null)
            {
                foreach (var other in others)
                {
                    Run(() => other.Advance(delta), ref failures);
                }
            }

            network.DeliverReady(reverse);
            Run(serverEvent, ref failures);
            Run(host.Pump, ref failures);
            Run(client.Pump, ref failures);
            if (others != null)
            {
                foreach (var other in others)
                {
                    Run(other.Pump, ref failures);
                }
            }

            Run(afterClient, ref failures);
            Rethrow(failures);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Tick의 한 단계를 실행하고 실패는 모아 둔다.
        /// </summary>
        // ------------------------------------------------------------
        private static void Run(Action stage, ref List<Exception> failures)
        {
            if (stage == null) return;

            try
            {
                stage();
            }
            catch (Exception exception)
            {
                (failures ??= new List<Exception>()).Add(exception);
            }
        }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// 지정한 Tick에 닿을 때까지 다른 Client와 함께 Step을 반복하고 매 Tick 서버 사건을 실행한다.
        /// </summary>
        // --------------------------------------------------------------------------------
        internal static void StepTo
        (
            FakeAbilityNetwork<AuthorityPacket> network, AuthorityHost host, AuthorityClient client,
            long tick, Action<long> serverEvent = null, IReadOnlyList<AuthorityClient> others = null
        )
        {
            while (network.Tick < tick)
            {
                Step(network, host, client, serverEvent: () => serverEvent?.Invoke(network.Tick), others: others);
            }
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 요청 처리 없이 Tick과 서버 State의 시간만 진행한다.
        /// </summary>
        // ------------------------------------------------------------
        internal static void Idle(FakeAbilityNetwork<AuthorityPacket> network, AuthorityHost host, int ticks)
        {
            for (var tick = 0; tick < ticks; tick++)
            {
                network.AdvanceTick();
                host.Advance(network.TickDelta);
            }
        }

    #endregion

    #region 조회·집계와 예외 전달

        // ----------------------------------------------------------------------
        /// <summary>
        /// 진행 중이거나 일시정지된 Cooldown의 남은 시간을 반환하며 준비 상태면 0이다.
        /// </summary>
        // ----------------------------------------------------------------------
        internal static float Remaining(AbilityState state) =>
            state.IsCooldownReady ? 0f : state.Cooldown.RemainingTime;

        // ------------------------------------------------------------
        /// <summary>
        /// Client가 받은 결과 중 지정한 분류의 수를 센다.
        /// </summary>
        // ------------------------------------------------------------
        internal static int Count(AuthorityClient client, AuthorityStatus status)
        {
            var count = 0;
            foreach (var result in client.Results)
            {
                if (result.Status == status)
                {
                    count++;
                }
            }

            return count;
        }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// 모아 둔 정리 예외를 한 건이면 그대로, 여러 건이면 묶어서 던지며 없으면 아무것도 하지 않는다.
        /// </summary>
        // --------------------------------------------------------------------------------
        internal static void Rethrow(List<Exception> failures)
        {
            if (failures == null) return;

            throw failures.Count == 1
                ? failures[0]
                : new AggregateException("정리 중 여러 단계가 실패했습니다.", failures);
        }

    #endregion

    }
}
