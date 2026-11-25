/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : TEST_AbilityNetworkStatus.cs
수정일 : 2026-11-23

# 설명
Ability 밖 지속 상태인 Status의 복제 규칙(network-protocol-l1-l2.md §15, 차이 G9)을 in-process Fake Server/Client로 검증한다.
Status는 서버 권한이며 컨테이너(Owner) 단위로 Revision이 바뀐 Tick에만 보내고, 받은 쪽은 CloneFrom 뒤 전송 지연만큼 Advance한다.
시간 만료는 Revision을 바꾸지 않으므로 보내지 않고 양쪽이 같은 고정 delta로 같은 Tick에 만료한다.
Host, Replicator와 Client 구현은 Fixture/Network의 테스트 코드이며 Provider와 Network 타입을 사용하지 않는다.

# 테스트 구성
 G: Status 복제(G9)
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

    // ============================================================
    /// <summary>
    /// Fake Server와 Client로 Status 복제 규칙을 검증한다.
    /// </summary>
    // ============================================================
    public sealed class TEST_AbilityNetworkStatus
    {

    #region G-1: Status 복제

        private static readonly StatusKey burn = new("Burn");

        private static StatusSpec CreateBurn(float duration)
        {
            return new StatusSpec(burn, 1, duration, StatusStacking.SharedRenew, StatusSourcing.PerKey);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 서버 Status의 첫 스택 남은 시간을 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        private static float StatusRemaining(StatusContainer<int> container)
        {
            Assert.IsTrue(container.TryGet(burn, 0, out var status));
            return status.GetStackTimer(0).RemainingTime;
        }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/> Status Snapshot은 컨테이너의 Revision이 바뀐 Tick에만 보내고 시간 만료는
        /// <br/> Revision을 바꾸지 않으므로 보내지 않는다.
        /// </summary>
        // --------------------------------------------------------------------------------
        [Test]
        public void TEST_AbilityNetworkStatus_Revision이바뀐Tick에만보내고시간만료는보내지않음()
        {
            var network = new FakeAbilityNetwork<AuthorityPacket>(0.1);
            using var host = new AuthorityHost(network);
            var serverKit = host.AddOwner(1, "Skill", 0, 3, connection: 10);
            var observer = host.Replication.AddObserver();
            host.Replication.Enter(observer, 1);
            Assert.AreEqual(1, observer.CountStatus(1));

            // 변경이 없으면 다시 보내지 않는다.
            host.Replication.Replicate();
            Assert.AreEqual(1, observer.CountStatus(1));

            // 이산 변경은 그 Tick에 한 번 보낸다.
            serverKit.Statuses.Apply(CreateBurn(0.25f), 0);
            host.Replication.Replicate();
            Assert.AreEqual(2, observer.CountStatus(1));
            host.Replication.Replicate();
            Assert.AreEqual(2, observer.CountStatus(1));
            var revision = serverKit.Statuses.Revision;
            Assert.IsTrue(observer.StatusLastSent[1].Mark.IsCurrent);

            // 시간 만료는 Revision을 바꾸지 않으므로 보내지 않는다.
            host.Advance(0.3f);
            Assert.AreEqual(0, serverKit.Statuses.CountStacks(burn));
            Assert.AreEqual(revision, serverKit.Statuses.Revision);
            host.Replication.Replicate();
            Assert.AreEqual(2, observer.CountStatus(1));
        }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/> 받은 쪽은 CloneFrom 뒤 전송 지연만큼 Advance해
        /// <br/> 서버와 같은 남은 시간을 갖고,
        /// <br/> CloneFrom은 변경 알림 없이 Revision만 올려
        /// <br/> 효과 계층이 Revision 변화를 보게 한다.
        /// <br/> Client는 Status를 예측하지 않으므로 자기 Owner도 Snapshot으로만 바뀐다.
        /// </summary>
        // --------------------------------------------------------------------------------
        [Test]
        public void TEST_AbilityNetworkStatus_CloneFrom뒤전송지연만큼Advance해서버와같은남은시간()
        {
            var network = new FakeAbilityNetwork<AuthorityPacket>(0.1);
            using var host = new AuthorityHost(network);
            var serverKit = host.AddOwner(1, "Skill", 0, 3, connection: 10);
            using var kit = new AuthorityKit("Skill", 0, 3);
            var client = new AuthorityClient(network, kit, 10, 1);
            host.Replication.Enter(host.Replication.AddClientObserver(10), 1);
            Step(network, host, client);

            var changes = 0;
            kit.Statuses.OnChanged += (_, _) => changes++;
            var revision = kit.Statuses.Revision;
            serverKit.Statuses.Apply(CreateBurn(5f), 0);

            // 서버가 바꾼 것을 받기 전의 Client는 스스로 Status를 만들지 않는다.
            Assert.AreEqual(0, kit.Statuses.CountStacks(burn));

            for (var i = 0; i < 3; i++)
            {
                Step(network, host, client);
            }

            Assert.AreEqual(1, kit.Statuses.CountStacks(burn));
            Assert.AreEqual(StatusRemaining(serverKit.Statuses), StatusRemaining(kit.Statuses), 1e-4f);
            Assert.AreEqual(0, changes);
            Assert.IsTrue(kit.Statuses.Revision > revision);
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// 시간 만료는 보내지 않아도 양쪽이 같은 고정 delta로 같은 Tick에 만료한다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_AbilityNetworkStatus_시간만료는Snapshot없이양쪽이같은Tick에만료()
        {
            var network = new FakeAbilityNetwork<AuthorityPacket>(0.1);
            using var host = new AuthorityHost(network);
            var serverKit = host.AddOwner(1, "Skill", 0, 3, connection: 10);
            using var kit = new AuthorityKit("Skill", 0, 3);
            var client = new AuthorityClient(network, kit, 10, 1);
            host.Replication.Enter(host.Replication.AddClientObserver(10), 1);
            Step(network, host, client);

            serverKit.Statuses.Apply(CreateBurn(0.55f), 0);
            for (var i = 0; i < 3; i++)
            {
                Step(network, host, client);
            }

            Assert.AreEqual(1, kit.Statuses.CountStacks(burn));
            var applied = client.StatusSnapshotsApplied;

            // 만료 전후 모든 Tick에서 두 쪽의 스택 수가 같다.
            for (var i = 0; i < 6; i++)
            {
                Step(network, host, client);
                Assert.AreEqual(serverKit.Statuses.CountStacks(burn), kit.Statuses.CountStacks(burn));
            }

            Assert.AreEqual(0, kit.Statuses.CountStacks(burn));
            Assert.AreEqual(applied, client.StatusSnapshotsApplied);
        }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/> 원격 Owner의 Status 사본은 처음 받을 때 만들고, 이탈하면 지우며 다시 보이면
        /// <br/> 처음부터 받는다. Owner 제거도 사본을 지운다.
        /// </summary>
        // --------------------------------------------------------------------------------
        [Test]
        public void TEST_AbilityNetworkStatus_원격Owner사본은이탈과제거로지우고재진입하면다시받음()
        {
            var network = new FakeAbilityNetwork<AuthorityPacket>(0.1);
            using var host = new AuthorityHost(network);
            host.AddOwner(1, "Skill", 0, 3, connection: 10);
            var remote = host.AddOwner(2, "Skill", 0, 3);
            using var kit = new AuthorityKit("Skill", 0, 3);
            var client = new AuthorityClient(network, kit, 10, 1);
            var observer = host.Replication.AddClientObserver(10);
            host.Replication.Enter(observer, 1);
            remote.Statuses.Apply(CreateBurn(5f), 0);
            host.Replication.Enter(observer, 2);
            Step(network, host, client);
            Step(network, host, client);

            Assert.IsNotNull(client.StatusReplica(2));
            Assert.AreEqual(1, client.StatusReplica(2).CountStacks(burn));
            Assert.AreEqual(StatusRemaining(remote.Statuses), StatusRemaining(client.StatusReplica(2)), 1e-4f);

            // 이탈하면 사본을 지우고 다시 보이면 보이는 시점의 Status를 처음부터 받는다.
            host.Replication.Leave(observer, 2);
            Step(network, host, client);
            Assert.IsNull(client.StatusReplica(2));

            host.Replication.Enter(observer, 2);
            Step(network, host, client);
            Step(network, host, client);
            Assert.AreEqual(1, client.StatusReplica(2).CountStacks(burn));

            // Owner 제거도 사본을 지운다.
            host.Despawn(2);
            Step(network, host, client);
            Assert.IsNull(client.StatusReplica(2));
        }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/> 새 Epoch에 합류하면 Status 사본과 적용 기록을 버리고 서버가 현재 Status를
        /// <br/> 처음부터 다시 보낸다.
        /// </summary>
        // --------------------------------------------------------------------------------
        [Test]
        public void TEST_AbilityNetworkStatus_새Epoch합류는사본을버리고Status를다시받음()
        {
            var network = new FakeAbilityNetwork<AuthorityPacket>(0.1);
            using var host = new AuthorityHost(network);
            host.AddOwner(1, "Skill", 0, 3, connection: 10);
            var remote = host.AddOwner(2, "Skill", 0, 3);
            using var kit = new AuthorityKit("Skill", 0, 3);
            var client = new AuthorityClient(network, kit, 10, 1);
            var observer = host.Replication.AddClientObserver(10);
            host.Replication.Enter(observer, 1);
            host.Replication.Enter(observer, 2);
            remote.Statuses.Apply(CreateBurn(5f), 0);
            for (var i = 0; i < 3; i++)
            {
                Step(network, host, client);
            }

            Assert.AreEqual(1, client.StatusReplica(2).CountStacks(burn));
            var applied = client.StatusSnapshotsApplied;

            // 서버 Status는 바뀌지 않은 채 새 세션으로 전환한다.
            network.Server.ChangeEpoch(1);
            client.JoinEpoch(1);
            Assert.IsNull(client.StatusReplica(2));

            for (var i = 0; i < 3; i++)
            {
                Step(network, host, client);
            }

            Assert.IsNotNull(client.StatusReplica(2));
            Assert.AreEqual(1, client.StatusReplica(2).CountStacks(burn));
            Assert.Greater(client.StatusSnapshotsApplied, applied);
            Assert.AreEqual(StatusRemaining(remote.Statuses), StatusRemaining(client.StatusReplica(2)), 1e-4f);
        }

    #endregion

    }
}
