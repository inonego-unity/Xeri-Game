/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : TEST_FakeAbilityNetwork.cs
수정일 : 2026-11-17

# 설명
테스트 전송 환경의 고정 Tick과 메시지 전달 조건을 검증한다.
========================================================================= BLOCK_HEADER_END */

using System;

using NUnit;
using NUnit.Framework;

namespace inonego.Xeri.Game.TEST._Ability.Fixture
{
    // ============================================================
    /// <summary>
    /// 가짜 전송 환경의 시간과 메시지 제어를 검증한다.
    /// </summary>
    // ============================================================
    public sealed class TEST_FakeAbilityNetwork
    {

    #region D-1: 고정 Tick·지연·중복·유실·순서 역전

        // ------------------------------------------------------------
        /// <summary>
        /// 고정 Tick과 지연 전달이 양방향에 적용된다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_FakeAbilityNetwork_고정Tick과지연전달()
        {
            var network = new FakeAbilityNetwork<string>(0.1);
            network.Send(network.Client, network.Server, "request", delayTicks: 2);
            network.Send(network.Server, network.Client, "response");
            network.DeliverReady();

            Assert.AreEqual(0, network.Server.Received.Count);
            Assert.AreEqual("response", network.Client.Received[0].Payload);
            network.AdvanceTick();
            network.DeliverReady();
            Assert.AreEqual(0, network.Server.Received.Count);
            network.AdvanceTick();
            network.DeliverReady();
            Assert.AreEqual(2, network.Tick);
            Assert.AreEqual(0.2, network.Now, 0.000001);
            Assert.AreEqual("request", network.Server.Received[0].Payload);
            Assert.AreEqual(0, network.Server.Received[0].SentTick);
            Assert.AreEqual(0, network.PendingCount);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 중복과 유실 및 준비된 메시지의 역순을 주입한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_FakeAbilityNetwork_중복유실순서역전()
        {
            var network = new FakeAbilityNetwork<int>(1);
            network.Send(network.Client, network.Server, 1, copies: 2);
            network.Send(network.Client, network.Server, 2, drop: true);
            network.Send(network.Client, network.Server, 3);
            network.DeliverReady(reverseOrder: true);

            Assert.AreEqual(3, network.Server.Received.Count);
            Assert.AreEqual(3, network.Server.Received[0].Payload);
            Assert.AreEqual(1, network.Server.Received[1].Payload);
            Assert.AreEqual(1, network.Server.Received[2].Payload);
            Assert.AreEqual(0, network.PendingCount);
        }

    #endregion

    #region E-1: Epoch 변경과 입력 검증

        // ------------------------------------------------------------
        /// <summary>
        /// Epoch 변경 전 메시지는 발송 당시 Epoch를 보존한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_FakeAbilityNetwork_Epoch변경과지연메시지()
        {
            var network = new FakeAbilityNetwork<string>(1);
            network.Client.ChangeEpoch(4);
            network.Send(network.Client, network.Server, "old", delayTicks: 1);
            network.Client.ChangeEpoch(5);
            network.Send(network.Client, network.Server, "new");
            network.DeliverReady();
            network.AdvanceTick();
            network.DeliverReady();

            Assert.AreEqual(5, network.Server.Received[0].Epoch);
            Assert.AreEqual(4, network.Server.Received[1].Epoch);
            Assert.AreEqual("old", network.Server.Received[1].Payload);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 유효하지 않은 시간과 전송 조건을 거부한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_FakeAbilityNetwork_유효하지않은조건거부()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new FakeAbilityNetwork<int>(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new FakeAbilityNetwork<int>(double.NaN));
            var network = new FakeAbilityNetwork<int>(1);
            Assert.Throws<ArgumentOutOfRangeException>
            (
                () => network.Send(network.Client, network.Server, 1, delayTicks: -1)
            );
            Assert.Throws<ArgumentOutOfRangeException>
            (
                () => network.Send(network.Client, network.Server, 1, copies: 0)
            );
            Assert.AreEqual(0, network.PendingCount);
        }

    #endregion

    }
}
