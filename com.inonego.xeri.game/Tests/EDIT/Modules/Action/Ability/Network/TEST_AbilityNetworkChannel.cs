/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : TEST_AbilityNetworkChannel.cs
수정일 : 2026-11-23

# 설명
Client 예측이 ExecutionChannel로 이전 예측 Session을 선점하는 경우의 규칙(network-protocol-l1-l2.md §7.2, 차이 G8)을 in-process Fake Server/Client로 검증한다.
선점한 예측이 서버에 거절돼도 이전 Session은 되살리지 않고, 권한 측의 이전 실행 종료 통지는 이미 끝난 Session에 아무 일도 하지 않는다.
채널이 선점을 거절하면 로컬 예측 자체를 시작하지 않고 이미 지불한 비용을 환급하며 요청도 보내지 않는다.
Host, Replicator와 Client 구현은 Fixture/Network의 테스트 코드이며 Provider와 Network 타입을 사용하지 않는다.

# 테스트 구성
 H: 채널 예측 선점(G8)
========================================================================= BLOCK_HEADER_END */

using System;
using System.Collections;
using System.Collections.Generic;

using NUnit;
using NUnit.Framework;

namespace inonego.Xeri.Game.TEST._Ability
{
    using Xeri;
    using Xeri.Game;
    using Xeri.Game.TEST;
    using Xeri.Game.TEST._Ability;
    using Xeri.Game.TEST._Ability.Fixture;
    using static inonego.Xeri.Game.TEST._Ability.Fixture.AuthorityRig;

    // ======================================================================
    /// <summary>
    /// Fake Server와 Client로 예측 Session의 채널 선점 규칙을 검증한다.
    /// </summary>
    // ======================================================================
    public sealed class TEST_AbilityNetworkChannel
    {

    #region H-1: 채널 예측 선점

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/> 예측이 채널로 이전 예측 Session을 선점한 뒤 서버가 그 예측을 거절해도
        /// <br/> 이전 Session은 되살리지 않는다. 권한 측에서 이전 실행이 끝났다는 통지가 와도
        /// <br/> 이미 끝난 Session에는 아무 일도 하지 않는다.
        /// </summary>
        // --------------------------------------------------------------------------------
        [Test]
        public void TEST_AbilityNetworkChannel_선점한예측이거절돼도이전Session을되살리지않음()
        {
            var network = new FakeAbilityNetwork<AuthorityPacket>(0.1);
            using var host = new AuthorityHost(network);
            host.CreateSessions = true;

            // 서버는 Charge가 하나뿐이라 두 번째 사용을 거절한다.
            host.AddOwner(1, "Skill", 0, 1, connection: 10);
            using var kit = new AuthorityKit("Skill", 0, 2);
            var client = new AuthorityClient(network, kit, 10, 1);
            host.Replication.Enter(host.Replication.AddClientObserver(10), 1);
            var channel = new ExecutionChannel<int>((_, _) => ExecutionChannelDecision.Replace);
            var predictor = new PredictionClient(network, client, channel: channel);

            var first = predictor.Predict(0);
            Assert.IsNotNull(first);
            Assert.AreSame(first.Session, channel.Active);

            // 두 번째 예측이 채널로 첫 예측 Session을 선점한다.
            var second = predictor.Predict(0);
            Assert.IsNotNull(second);
            Assert.AreSame(second.Session, channel.Active);
            Assert.IsTrue(first.Session.HasEnded);
            Assert.AreEqual(ExecutionEndReason.Replaced, first.Session.EndReason);

            for (var i = 0; i < 4; i++)
            {
                Step(network, host, client);
            }

            // 첫 요청은 승인되고 두 번째 요청은 거절된다. 선점당한 첫 Session은 그대로 끝난 상태다.
            Assert.AreEqual(1, predictor.Confirmed);
            Assert.AreEqual(1, predictor.Rejected);
            Assert.IsTrue(second.Session.HasEnded);
            Assert.AreEqual(ExecutionEndReason.Cancelled, second.Session.EndReason);
            Assert.IsNull(channel.Active);
            Assert.IsTrue(first.Session.HasEnded);
            Assert.IsFalse(first.Session.IsActive);
            Assert.AreEqual(ExecutionEndReason.Replaced, first.Session.EndReason);

            // 권한 측 이전 실행이 끝나도 이미 끝난 Session은 종료 사유를 바꾸지 않는다.
            Assert.AreEqual(1, host.Sessions.Count);
            host.Sessions[0].End(ExecutionEndReason.Interrupted);
            for (var i = 0; i < 3; i++)
            {
                Step(network, host, client);
            }

            Assert.AreEqual(ExecutionEndReason.Replaced, first.Session.EndReason);
            Assert.IsNull(channel.Active);
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> 채널이 선점을 거절하면 로컬 예측을 시작하지 않는다. 이미 지불한 Charge는
        /// <br/> 환급하고 요청 ID와 요청도 쓰지 않으며 먼저 시작한 예측은 그대로 유지한다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_AbilityNetworkChannel_채널이선점을거절하면예측을시작하지않고비용을환급()
        {
            var network = new FakeAbilityNetwork<AuthorityPacket>(0.1);
            using var host = new AuthorityHost(network);
            host.AddOwner(1, "Skill", 0, 2, connection: 10);
            using var kit = new AuthorityKit("Skill", 0, 2);
            var client = new AuthorityClient(network, kit, 10, 1);
            var channel = new ExecutionChannel<int>
            (
                (active, requested) => requested > active
                    ? ExecutionChannelDecision.Replace
                    : ExecutionChannelDecision.Reject
            );
            var predictor = new PredictionClient(network, client, channel: channel);

            var first = predictor.Predict(0, claim: 5);
            Assert.IsNotNull(first);
            Assert.AreEqual(1, kit.State.Charge.Current);
            var pending = network.PendingCount;

            // 점유 중인 것보다 낮지 않은 Claim은 거절돼 예측이 시작되지 않는다.
            Assert.IsNull(predictor.Predict(0, claim: 5));
            Assert.AreEqual(1, predictor.Begins);
            Assert.AreEqual(1, kit.State.Charge.Current);
            Assert.AreEqual(2, client.NextRequestID);
            Assert.AreEqual(pending, network.PendingCount);
            Assert.IsTrue(first.Session.IsActive);
            Assert.AreSame(first.Session, channel.Active);

            // 더 높은 Claim은 선점한다.
            var higher = predictor.Predict(0, claim: 6);
            Assert.IsNotNull(higher);
            Assert.AreSame(higher.Session, channel.Active);
            Assert.AreEqual(ExecutionEndReason.Replaced, first.Session.EndReason);
            Assert.AreEqual(0, kit.State.Charge.Current);
        }

    #endregion

    }
}
