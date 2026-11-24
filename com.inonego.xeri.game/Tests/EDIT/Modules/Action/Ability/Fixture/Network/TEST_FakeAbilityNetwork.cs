/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : TEST_FakeAbilityNetwork.cs
수정일 : 2026-11-19

# 설명
테스트 전송 환경의 고정 Tick과 메시지 전달 조건, 연결 신원을 검증한다.

# 테스트 구성
 D: 고정 Tick·지연·중복·유실·순서 역전·순서 보장 채널
 E: Epoch 변경과 입력 검증
 C: 연결별 끝점과 연결 신원
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

    #region D-1: 고정 Tick·지연·중복·유실·순서 역전·순서 보장 채널

        // ------------------------------------------------------------
        /// <summary>
        /// 고정 Tick과 지연 전달이 양방향에 적용된다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_FakeAbilityNetwork_고정Tick과지연전달()
        {
            var network = new FakeAbilityNetwork<string>(0.1);
            var client = network.Connect(1);
            network.Send(client, network.Server, "request", delayTicks: 2);
            network.Send(network.Server, client, "response");
            network.DeliverReady();

            Assert.AreEqual(0, network.Server.Received.Count);
            Assert.AreEqual("response", client.Received[0].Payload);
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
            var client = network.Connect(1);
            network.Send(client, network.Server, 1, copies: 2);
            network.Send(client, network.Server, 2, drop: true);
            network.Send(client, network.Server, 3);
            network.DeliverReady(reverseOrder: true);

            Assert.AreEqual(3, network.Server.Received.Count);
            Assert.AreEqual(3, network.Server.Received[0].Payload);
            Assert.AreEqual(1, network.Server.Received[1].Payload);
            Assert.AreEqual(1, network.Server.Received[2].Payload);
            Assert.AreEqual(0, network.PendingCount);
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> 순서 보장 채널의 메시지는 지연이 줄거나 역순을 주입해도 보낸 순서로 도착하고
        /// <br/> 중복·유실은 주입할 수 없다. 다른 메시지와의 상대 순서는 보장하지 않는다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_FakeAbilityNetwork_순서보장채널은지연과역순에도보낸순서로도착()
        {
            var network = new FakeAbilityNetwork<int>(1);
            var client = network.Connect(1);
            network.Send(network.Server, client, 1, delayTicks: 2, ordered: true);
            network.Send(network.Server, client, 2, ordered: true);
            network.Send(network.Server, client, 3, ordered: true);
            network.Send(network.Server, client, 9);
            network.DeliverReady(reverseOrder: true);

            // 지연이 짧은 뒤 메시지도 앞 메시지보다 먼저 도착하지 않는다.
            Assert.AreEqual(1, client.Received.Count);
            Assert.AreEqual(9, client.Received[0].Payload);

            network.AdvanceTick();
            network.AdvanceTick();
            network.Send(network.Server, client, 8);
            network.DeliverReady(reverseOrder: true);
            Assert.AreEqual(5, client.Received.Count);
            Assert.AreEqual(8, client.Received[1].Payload);
            Assert.AreEqual(1, client.Received[2].Payload);
            Assert.AreEqual(2, client.Received[3].Payload);
            Assert.AreEqual(3, client.Received[4].Payload);

            Assert.Throws<ArgumentException>(() => network.Send(network.Server, client, 4, copies: 2, ordered: true));
            Assert.Throws<ArgumentException>(() => network.Send(network.Server, client, 4, drop: true, ordered: true));
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
            var client = network.Connect(1);
            client.ChangeEpoch(4);
            network.Send(client, network.Server, "old", delayTicks: 1);
            client.ChangeEpoch(5);
            network.Send(client, network.Server, "new");
            network.DeliverReady();
            network.AdvanceTick();
            network.DeliverReady();

            Assert.AreEqual(5, network.Server.Received[0].Epoch);
            Assert.AreEqual(4, network.Server.Received[1].Epoch);
            Assert.AreEqual("old", network.Server.Received[1].Payload);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 유효하지 않은 시간과 전송 조건, 연결 신원을 거부한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_FakeAbilityNetwork_유효하지않은조건거부()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new FakeAbilityNetwork<int>(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new FakeAbilityNetwork<int>(double.NaN));
            var network = new FakeAbilityNetwork<int>(1);
            var client = network.Connect(1);
            Assert.Throws<ArgumentOutOfRangeException>
            (
                () => network.Send(client, network.Server, 1, delayTicks: -1)
            );
            Assert.Throws<ArgumentOutOfRangeException>
            (
                () => network.Send(client, network.Server, 1, copies: 0)
            );
            Assert.Throws<ArgumentOutOfRangeException>(() => network.Connect(FakeAbilityNetwork<int>.ServerConnection));
            Assert.Throws<ArgumentOutOfRangeException>(() => network.Connect(-1));
            Assert.AreEqual(0, network.PendingCount);
        }

    #endregion

    #region C-1: 연결별 끝점과 연결 신원

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> 연결마다 끝점이 따로 있어 받은 메시지가 섞이지 않고,
        /// <br/> 메시지에는 보낸 끝점의 연결 신원이 전송 계층에서 붙는다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_FakeAbilityNetwork_연결별끝점은메시지를나누고보낸연결을붙임()
        {
            var network = new FakeAbilityNetwork<string>(1);
            var first = network.Connect(10);
            var second = network.Connect(11);
            Assert.AreSame(first, network.Connect(10));
            Assert.AreNotSame(first, second);

            network.Send(first, network.Server, "from-10");
            network.Send(second, network.Server, "from-11");
            network.Send(network.Server, second, "to-11");
            network.DeliverReady();

            Assert.AreEqual(10, network.Server.Received[0].Connection);
            Assert.AreEqual(11, network.Server.Received[1].Connection);
            Assert.AreEqual(0, first.Received.Count);
            Assert.AreEqual(1, second.Received.Count);
            Assert.AreEqual(FakeAbilityNetwork<string>.ServerConnection, second.Received[0].Connection);
        }

    #endregion

    }
}
