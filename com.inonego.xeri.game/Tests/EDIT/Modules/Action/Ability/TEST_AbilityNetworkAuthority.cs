/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : TEST_AbilityNetworkAuthority.cs
수정일 : 2026-11-17

# 설명
권한 사용 프로토콜(L1)을 in-process Fake Server/Client로 검증한다.
Request 단일 적용, 서버 재선택과 State 정정, Epoch 거부, AOI 복제 필터, 로컬에서 온라인 전환을 다룬다.
복제는 State 복사본과 전송 지연 보정(Advance)으로 하며 시각이 아니라 남은 시간으로 비교한다.
Host와 Client 구현은 Fixture/FakeAbilityAuthority.cs의 테스트 코드이며 Provider와 Network 타입을 사용하지 않는다.
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

    // ================================================================================
    /// <summary>
    /// Fake Server와 Client로 권한 사용 프로토콜의 단일 적용과 상태 연속성을 검증한다.
    /// </summary>
    // ================================================================================
    public sealed class TEST_AbilityNetworkAuthority
    {

    #region R-1: 요청 단일 적용 (N01)

        // --------------------------------------------------------------------------------
        /// <summary>
        /// 같은 Request ID의 재전송과 순서 역전에도 Cost, 실행, 사용 기록은 한 번만 적용된다.
        /// </summary>
        // --------------------------------------------------------------------------------
        [Category("N01")]
        [Test]
        public void TEST_AbilityNetworkAuthority_같은RequestID재전송은한번만적용되고Duplicate를반환()
        {
            var network = new FakeAbilityNetwork<AuthorityPacket>(0.1);
            using var host = new AuthorityHost(network);
            using var kit = new AuthorityKit("Skill", 0, 3);
            host.AddOwner(1, "Skill", 0, 3, sender: 10);
            var client = new AuthorityClient(network, kit, 10, 1);
            var server = host.GetKit(1).State;
            host.SpawnsPerUse = 1;

            var first = client.Create();
            client.Send(first, copies: 3);
            Step(network, host, client, reverse: true);
            Step(network, host, client);

            Assert.AreEqual(3, client.Results.Count);
            Assert.AreEqual(1, Count(client, AuthorityStatus.Accepted));
            Assert.AreEqual(2, Count(client, AuthorityStatus.Duplicate));
            var accepted = client.Results.Find(result => result.Status == AuthorityStatus.Accepted);
            foreach (var duplicate in client.Results)
            {
                if (duplicate.Status != AuthorityStatus.Duplicate) continue;

                Assert.AreSame(accepted, duplicate.Original);
                Assert.AreEqual(100UL, duplicate.Original.ExecutionID);
            }

            Assert.AreEqual(1, host.Started.Count);
            Assert.AreEqual(2, server.Charge.Current);

            // Spawn도 재전송 횟수와 무관하게 권한에서 한 번만 일어난다.
            Assert.AreEqual(1, host.Spawned.Count);
            Assert.AreEqual(1, accepted.Spawns.Count);
            Assert.AreEqual(host.Spawned[0].Key, accepted.Spawns[0].Key);

            // 이전 ID의 재전송이 더 새 요청보다 늦게 도착해도 다시 적용되지 않는다.
            var second = client.Create();
            client.Send(first);
            client.Send(second);
            Step(network, host, client, reverse: true);
            Step(network, host, client);

            Assert.AreEqual(2, host.Started.Count);
            Assert.AreEqual(2, host.Spawned.Count);
            Assert.AreEqual(1, server.Charge.Current);
            Assert.AreEqual(1, Count(client, AuthorityStatus.Stale));
            var secondResult = client.Results.Find(result => result.RequestID == 2 && result.Status == AuthorityStatus.Accepted);
            Assert.IsNotNull(secondResult);
            Assert.AreEqual(101UL, secondResult.ExecutionID);

            client.Send(second);
            Step(network, host, client);
            Step(network, host, client);
            Assert.AreEqual(2, host.Started.Count);
            Assert.AreEqual(2, host.Spawned.Count);
            Assert.AreSame(secondResult, client.Results[client.Results.Count - 1].Original);
        }

    #endregion

    #region S-1: 서버 재선택과 State 정정 (N02)

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> 클라이언트 뷰가 준비 상태여도 서버 Cooldown이 먼저 시작되면 거절하고
        /// <br/> State 복사본으로 정정된다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Category("N02")]
        [Test]
        public void TEST_AbilityNetworkAuthority_서버Cooldown이먼저시작되면거절하고복사본으로정정()
        {
            var network = new FakeAbilityNetwork<AuthorityPacket>(0.1);
            using var host = new AuthorityHost(network);
            using var kit = new AuthorityKit("Skill", 0, 3);
            var serverKit = host.AddOwner(1, "Skill", 0, 3, sender: 10);
            var clientObserver = host.AddClientObserver();
            host.Enter(clientObserver, 1, send: false);

            // 지연 300ms는 0.1초 Tick 3개이다.
            host.ReplyDelay = 3;
            var client = new AuthorityClient(network, kit, 10, 1);
            Assert.AreEqual(AuthorityInput.Sent, client.TryUse(out _, delayTicks: 3));
            Assert.IsTrue(kit.State.IsCooldownReady);

            for (var tick = 1; tick <= 4; tick++)
            {
                Step
                (
                    network, host, client,
                    serverEvent: () =>
                    {
                        if (network.Tick == 1)
                        {
                            Assert.IsTrue(serverKit.State.Control.TryBeginCooldown(5f));
                        }
                    }
                );
            }

            // 거절 결과보다 복사본이 먼저 도착해 클라이언트 State가 정정되고, 전송 지연만큼 진행해 보정한다.
            Assert.AreEqual(0, client.Results.Count);
            Assert.IsFalse(kit.State.IsCooldownReady);
            Assert.AreEqual(4.7f, Remaining(serverKit.State), 1e-4f);
            Assert.AreEqual(Remaining(serverKit.State), Remaining(kit.State), 1e-4f);

            for (var tick = 5; tick <= 7; tick++)
            {
                Step(network, host, client);
            }

            Assert.AreEqual(1, client.Results.Count);
            Assert.AreEqual(AuthorityStatus.Rejected, client.Results[0].Status);
            Assert.IsNotEmpty(client.Results[0].Reason);
            Assert.AreEqual(0, host.Started.Count);
            Assert.AreEqual(3, serverKit.State.Charge.Current);
            Assert.AreEqual(3, kit.State.Charge.Current);
            Assert.AreEqual(AuthorityInput.LocallyRejected, client.TryUse(out _));
        }

    #endregion

    #region E-1: Epoch 거부 (N04)

        // ----------------------------------------------------------------------
        /// <summary>
        /// Reconnect 뒤 이전 Epoch의 요청은 거부되고 현재 Epoch의 요청만 한 번 실행된다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Category("N04")]
        [TestCase(false)]
        [TestCase(true)]
        public void TEST_AbilityNetworkAuthority_이전Epoch요청은거부되고중복실행없음(bool reverse)
        {
            var network = new FakeAbilityNetwork<AuthorityPacket>(0.1);
            using var host = new AuthorityHost(network);
            using var kit = new AuthorityKit("Skill", 0, 3);
            host.AddOwner(1, "Skill", 0, 3, sender: 10);
            var client = new AuthorityClient(network, kit, 10, 1);
            var server = host.GetKit(1).State;

            // 이전 Epoch의 요청이 전송 중인 채로 Reconnect한다.
            var stale = client.Create();
            client.Send(stale, delayTicks: 1, copies: 2);
            network.Server.ChangeEpoch(1);
            network.Client.ChangeEpoch(1);
            client.ResetSession();
            var current = client.Create();
            Assert.AreEqual(1, current.RequestID);
            client.Send(current, delayTicks: 1);

            // 두 Epoch의 요청이 같은 Tick에 도착한다.
            Step(network, host, client, reverse);
            Step(network, host, client);

            Assert.AreEqual(2, Count(client, AuthorityStatus.StaleEpoch));
            Assert.AreEqual(1, Count(client, AuthorityStatus.Accepted));
            foreach (var result in client.Results)
            {
                Assert.AreEqual(result.Status == AuthorityStatus.Accepted ? 1 : 0, result.RequestEpoch);
            }

            Assert.AreEqual(1, host.Started.Count);
            Assert.AreEqual(2, server.Charge.Current);

            // 늦게 온 이전 Epoch 요청은 이후에도 상태를 바꾸지 못한다.
            network.Client.ChangeEpoch(0);
            client.Send(stale);
            network.Client.ChangeEpoch(1);
            Step(network, host, client);
            Step(network, host, client);
            Assert.AreEqual(3, Count(client, AuthorityStatus.StaleEpoch));
            Assert.AreEqual(1, host.Started.Count);
            Assert.AreEqual(2, server.Charge.Current);
        }

    #endregion

    #region F-1: AOI 필터 (N05)

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/> 시야 밖 Observer는 해당 Ability의 State 복사본과 시작 사건을 받지 않고
        /// <br/> 진입과 Revision 변경에만 전송된다.
        /// </summary>
        // --------------------------------------------------------------------------------
        [Category("N05")]
        [Test]
        public void TEST_AbilityNetworkAuthority_AOI밖에는복사본과시작사건을보내지않음()
        {
            var network = new FakeAbilityNetwork<AuthorityPacket>(0.1);
            using var host = new AuthorityHost(network);
            var enemy = host.AddOwner(2, "Enemy", 0, 3);
            var ally = host.AddObserver();
            var far = host.AddObserver();

            enemy.State.Control.TryBeginCooldown(5f);
            host.Replicate();
            Assert.AreEqual(0, ally.Count(2));
            Assert.AreEqual(0, far.Count(2));

            host.Enter(ally, 2);
            Assert.AreEqual(1, ally.Count(2));
            host.Replicate();
            host.Replicate();
            Assert.AreEqual(1, ally.Count(2));

            // 보이는 동안 Revision이 바뀌면 한 번만 전송한다.
            enemy.State.Values.SetI("rage", 1);
            host.Replicate();
            host.Replicate();
            Assert.AreEqual(2, ally.Count(2));

            // 사용은 Revision을 여러 번 올려도 복사본 한 번과 시작 사건 한 번으로 전달된다.
            // 시간 진행은 Revision을 올리지 않으므로 Cooldown이 끝나도 복사본을 다시 보내지 않는다.
            var revisionBeforeIdle = enemy.State.Revision;
            Idle(network, host, 60);
            Assert.IsTrue(enemy.State.IsCooldownReady);
            Assert.AreEqual(revisionBeforeIdle, enemy.State.Revision);
            host.Replicate();
            Assert.AreEqual(2, ally.Count(2));

            var used = host.Execute(2, 0, 0, AuthorityKit.EntryName, 0);
            Assert.AreEqual(AuthorityStatus.Accepted, used.Status);
            host.Replicate();
            Assert.AreEqual(3, ally.Count(2));
            Assert.AreEqual(enemy.State.Revision, ally.Snapshots[ally.Snapshots.Count - 1].Snapshot.Revision);
            Assert.AreEqual(1, ally.Started.Count);
            Assert.AreEqual(used.ExecutionID, ally.Started[0].ExecutionID);
            Assert.AreEqual(new AbilityKey("Enemy"), ally.Started[0].Origin.AbilityKey);

            // 이탈 중의 변경은 보내지 않고 재진입할 때마다 현재 값을 한 번 보낸다.
            host.Leave(ally, 2);
            enemy.State.Values.SetI("rage", 2);
            enemy.State.Values.SetI("rage", 3);
            host.Replicate();
            Assert.AreEqual(3, ally.Count(2));
            for (var i = 1; i <= 3; i++)
            {
                host.Enter(ally, 2);
                host.Enter(ally, 2);
                host.Replicate();
                Assert.AreEqual(3 + i, ally.Count(2));
                host.Leave(ally, 2);
            }

            Assert.AreEqual(0, far.Snapshots.Count);
            Assert.AreEqual(0, far.Started.Count);
            Assert.AreEqual(0, far.InProgress.Count);

            // 재진입마다 진행 중 관측을 한 번 받지만 새 시작 사건은 아니다.
            Assert.AreEqual(1, ally.Started.Count);
            Assert.AreEqual(3, ally.InProgress.Count);
            Assert.AreEqual(used.ExecutionID, ally.InProgress[0].ExecutionID);
            Assert.AreEqual(ally.Started[0].Origin, ally.InProgress[0].Origin);

            // 실행이 끝난 뒤의 진입에는 진행 중 관측도 보내지 않는다.
            host.EndExecution(2);
            host.Enter(ally, 2);
            Assert.AreEqual(3, ally.InProgress.Count);
            Assert.AreEqual(1, ally.Started.Count);
        }

    #endregion

    #region O-1: 로컬에서 온라인 전환 (N11)

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> 로컬 정지, 새 Epoch, 신원 매핑, Unity 직렬화 복원으로 State가 이어지고
        /// <br/> 이전 Session 명령은 거부된다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Category("N11")]
        [Test]
        public void TEST_AbilityNetworkAuthority_로컬에서온라인전환은State를잇고이전Session명령을거부()
        {
            var network = new FakeAbilityNetwork<AuthorityPacket>(0.1);
            using var host = new AuthorityHost(network);
            using var local = new AuthorityKit("Skill", 2f, 3);
            var client = new AuthorityClient(network, local, 10, 1);
            const int localOwner = 1;

            // 로컬 게임: Cooldown이 끝난 뒤 두 번 사용한다. 로컬 시간은 Step이 진행한다.
            AbilityBegin<string> accept = (in AbilityUse<string> use) => true;
            Assert.IsTrue(local.TryUse(AuthorityKit.EntryName, localOwner, 1, accept, out _, out _));

            // 로컬 Session 때 보낸 명령이 온라인 전환 뒤에 도착하도록 지연시킨다.
            var oldCommand = client.Create();
            client.Send(oldCommand, delayTicks: 40);
            for (var i = 0; i < 30; i++)
            {
                Step(network, host, client);
            }

            Assert.IsTrue(local.State.IsCooldownReady);
            Assert.IsTrue(local.TryUse(AuthorityKit.EntryName, localOwner, 1, accept, out _, out _));
            local.State.Values.SetI("combo", 2);
            Assert.IsFalse(local.TryUse(AuthorityKit.EntryName, localOwner, 1, accept, out _, out _));
            for (var i = 0; i < 5; i++)
            {
                Step(network, host, client);
            }

            // 전환 규칙: 정지 구간의 입력은 거절하며 대기열에 넣지 않는다.
            client.Switching = true;
            var pending = network.PendingCount;
            Assert.AreEqual(AuthorityInput.Switching, client.TryUse(out _));
            Assert.AreEqual(pending, network.PendingCount);
            var revisionAtStop = local.State.Revision;
            Assert.AreEqual(1.5f, Remaining(local.State), 1e-4f);
            Assert.AreEqual(1, local.State.Charge.Current);

            // 새 Session과 Epoch, 신원 매핑(로컬 플레이어 1 = 서버 Owner 1, 연결 10).
            network.Server.ChangeEpoch(1);
            network.Client.ChangeEpoch(1);
            client.ResetSession();
            var serverKit = host.AddOwner(1, "Skill", 2f, 3, sender: 10);
            var before = serverKit.State.Revision;
            UnityEngine.JsonUtility.FromJsonOverwrite(UnityEngine.JsonUtility.ToJson(local.State), serverKit.State);
            Assert.AreEqual(revisionAtStop, local.State.Revision);
            Assert.Greater(serverKit.State.Revision, before);

            // 경과 기준 Timer이므로 시각 변환 없이 남은 시간, Charge, 충전 진행, Values가 그대로 이어진다.
            Assert.AreEqual(1.5f, Remaining(serverKit.State), 1e-4f);
            Assert.AreEqual(local.State.Charge.Current, serverKit.State.Charge.Current);
            Assert.AreEqual
            (
                local.State.Charge.Recharge.RemainingTime,
                serverKit.State.Charge.Recharge.RemainingTime, 1e-4f
            );
            Assert.AreEqual(2, serverKit.State.Values.GetI("combo"));

            // 초기 복제 뒤 재개한다. 한 Tick의 전송 지연은 Advance로 보정한다.
            var observer = host.AddClientObserver();
            host.Enter(observer, 1);
            client.Switching = false;
            Step(network, host, client);
            Assert.AreEqual(1.4f, Remaining(serverKit.State), 1e-4f);
            Assert.AreEqual(Remaining(serverKit.State), Remaining(local.State), 1e-4f);
            Assert.AreEqual(serverKit.State.Charge.Current, local.State.Charge.Current);
            Assert.AreEqual(0, host.Started.Count);

            // 이전 Session 명령은 거부되고, 신원이 다른 연결의 요청도 실행되지 않는다.
            for (var i = 0; i < 6; i++)
            {
                Step(network, host, client);
            }

            Assert.AreEqual(1, Count(client, AuthorityStatus.StaleEpoch));
            Assert.AreEqual(0, host.Started.Count);
            client.Send(new AuthorityRequest(99, 1, 1, AuthorityKit.EntryName, network.Tick, 1));
            Step(network, host, client);
            Step(network, host, client);
            Assert.AreEqual(1, Count(client, AuthorityStatus.UnknownOwner));
            Assert.AreEqual(0, host.Started.Count);

            // Cooldown이 끝난 뒤 새 Session의 요청은 이어받은 Charge에서 승인된다.
            var carriedCharge = serverKit.State.Charge.Current;
            while (network.Tick < 70)
            {
                Step(network, host, client);
            }

            Assert.AreEqual(AuthorityInput.Sent, client.TryUse(out _));
            Step(network, host, client);
            Step(network, host, client);
            Assert.AreEqual(1, Count(client, AuthorityStatus.Accepted));
            Assert.AreEqual(1, host.Started.Count);
            Assert.AreEqual(carriedCharge - 1, serverKit.State.Charge.Current);
            Assert.AreEqual(serverKit.State.Charge.Current, local.State.Charge.Current);
        }

    #endregion

    }
}
