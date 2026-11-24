/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : TEST_AbilityNetworkAuthority.cs
수정일 : 2026-11-19

# 설명
권한 사용 프로토콜(L1)을 in-process Fake Server/Client로 검증한다.
Request 단일 적용과 시작 실패, 서버 재선택과 State 정정, Epoch 거부와 합류 때 Owner 목록 동기화, AOI·공개 범위 복제 필터와 실행 종료 관측, 연결 신원, Owner 등록·제거, 로컬에서 온라인 전환을 다룬다.
복제는 State 복사본과 전송 지연 보정(Advance)으로 하며 시각이 아니라 남은 시간으로 비교한다.
Host, Replicator와 Client 구현은 Fixture/Network의 테스트 코드이며 Provider와 Network 타입을 사용하지 않는다.

# 테스트 구성
 R: 요청 처리·단일 적용·시작 실패
 S: 서버 재선택과 State 정정
 E: Epoch 거부와 재합류
 F: AOI와 Ability 단위 변경분 복제, 실행 관측
 I: 연결 신원
 W: Owner 등록과 제거
 O: 로컬에서 온라인 전환
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

    #region R-1: 요청 처리와 단일 적용

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/> 같은 Request ID의 재전송과 순서 역전에도 Cost, 실행, 사용 기록은 한 번만 적용된다.
        /// <br/> 장부 창 안의 재전송은 늦게 도착해도 원래 결과로 응답한다.
        /// </summary>
        // --------------------------------------------------------------------------------
        [Test]
        public void TEST_AbilityNetworkAuthority_같은RequestID재전송은한번만적용되고Duplicate를반환()
        {
            var network = new FakeAbilityNetwork<AuthorityPacket>(0.1);
            using var host = new AuthorityHost(network);
            using var kit = new AuthorityKit("Skill", 0, 3);
            host.AddOwner(1, "Skill", 0, 3, connection: 10);
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
            // 장부 창 안의 요청이므로 Stale이 아니라 원래 결과를 담은 Duplicate로 응답한다.
            var second = client.Create();
            client.Send(first);
            client.Send(second);
            Step(network, host, client, reverse: true);
            Step(network, host, client);

            Assert.AreEqual(2, host.Started.Count);
            Assert.AreEqual(2, host.Spawned.Count);
            Assert.AreEqual(1, server.Charge.Current);
            Assert.AreEqual(0, Count(client, AuthorityStatus.Stale));
            Assert.AreEqual(3, Count(client, AuthorityStatus.Duplicate));
            Assert.AreSame(accepted, client.Results.FindLast(result => result.RequestID == 1).Original);
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

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> 비어 있는 Entry의 요청은 BadRequest로 거부하고
        /// <br/> 뒤따르는 정상 요청은 처리한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_AbilityNetworkAuthority_형식오류요청은거부하고뒤따른정상요청은처리()
        {
            var network = new FakeAbilityNetwork<AuthorityPacket>(0.1);
            using var host = new AuthorityHost(network);
            using var kit = new AuthorityKit("Skill", 0, 3);
            host.AddOwner(1, "Skill", 0, 3, connection: 10);
            var client = new AuthorityClient(network, kit, 10, 1);

            client.Send(new AuthorityRequest(1, 1, null, network.Tick, 1));
            client.Send(new AuthorityRequest(2, 1, " ", network.Tick, 1));
            client.Send(new AuthorityRequest(3, 1, AuthorityKit.EntryName, network.Tick, "1"));
            client.Send(new AuthorityRequest(4, 1, AuthorityKit.EntryName, network.Tick, 1));

            Step(network, host, client);
            Step(network, host, client);

            Assert.AreEqual(3, Count(client, AuthorityStatus.BadRequest));
            Assert.AreEqual(1, Count(client, AuthorityStatus.Accepted));
            Assert.AreEqual(1, host.Started.Count);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> begin 뒤의 전이 예외가 나도 요청을 처리된 것으로 기록해
        /// <br/> 같은 Request ID의 재전송이 다시 실행되지 않는다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_AbilityNetworkAuthority_전이예외에도요청을기록해재전송을다시실행하지않음()
        {
            var network = new FakeAbilityNetwork<AuthorityPacket>(0.1);
            using var host = new AuthorityHost(network);
            var serverKit = host.AddOwner
            (
                1, "Skill", 0, 3, connection: 10,
                transition: () => throw new InvalidOperationException("transition")
            );
            using var kit = new AuthorityKit("Skill", 0, 3);
            var client = new AuthorityClient(network, kit, 10, 1);

            client.Send(client.Create(), copies: 2);
            Step(network, host, client);
            Step(network, host, client);

            Assert.AreEqual(1, host.Started.Count);
            Assert.AreEqual(1, host.Faults.Count);
            Assert.AreEqual(2, serverKit.State.Charge.Current);
            Assert.AreEqual(1, Count(client, AuthorityStatus.Accepted));
            Assert.AreEqual(1, Count(client, AuthorityStatus.Duplicate));
        }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/> 실행 시작 안에서 실패하면 비용이 환급되고 시작 기록과 Session을 남기지 않으며
        /// <br/> 이미 보고 있던 Observer에도 시작 사건을 보내지 않고 예외를 그대로 전달한다.
        /// </summary>
        // --------------------------------------------------------------------------------
        [Test]
        public void TEST_AbilityNetworkAuthority_시작실패는기록을남기지않고예외를전달()
        {
            var network = new FakeAbilityNetwork<AuthorityPacket>(0.1);
            using var host = new AuthorityHost(network);
            host.CreateSessions = true;
            var serverKit = host.AddOwner(1, "Skill", 0, 3, connection: 10);
            var watching = host.Replication.AddObserver();
            host.Replication.Enter(watching, 1);
            ExecutionSession failed = null;
            host.BeginFault = () =>
            {
                failed = host.Sessions[host.Sessions.Count - 1];
                throw new InvalidOperationException("시작 실패");
            };

            Assert.Throws<InvalidOperationException>
            (
                () => host.ExecuteDirect(1, 1)
            );
            var late = host.Replication.AddObserver();
            host.Replication.Enter(late, 1);

            Assert.AreEqual(0, host.Started.Count);
            Assert.AreEqual(0, host.Sessions.Count);
            Assert.IsNotNull(failed);
            Assert.IsFalse(failed.IsActive);
            Assert.AreEqual(0, watching.Started.Count);
            Assert.AreEqual(0, late.InProgress.Count);
            Assert.AreEqual(0, host.Faults.Count);
            Assert.AreEqual(3, serverKit.State.Charge.Current);
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> 진행 중인 실행이 있는 Owner의 새 실행이 시작에 실패해도
        /// <br/> 이전 실행의 진행 중 기록은 남아 이후 AOI 진입에 관측으로 전달된다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_AbilityNetworkAuthority_시작실패는진행중이던이전실행기록을유지()
        {
            var network = new FakeAbilityNetwork<AuthorityPacket>(0.1);
            using var host = new AuthorityHost(network);
            host.AddOwner(1, "Skill", 0, 3, connection: 10);
            var observer = host.Replication.AddObserver();

            Assert.AreEqual(AuthorityStatus.Accepted, host.ExecuteDirect(1, 1).Status);
            host.BeginFault = () => throw new InvalidOperationException("시작 실패");
            Assert.Throws<InvalidOperationException>
            (
                () => host.ExecuteDirect(1, 2)
            );
            host.Replication.Enter(observer, 1);

            Assert.AreEqual(1, host.Started.Count);
            Assert.AreEqual(1, observer.InProgress.Count);
            Assert.AreEqual(host.Started[0].ExecutionID, observer.InProgress[0].ExecutionID);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> 시작 실패를 정리하다 Session 종료 콜백이 예외를 던져도
        /// <br/> 호출자에게는 원래 시작 예외가 전달된다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_AbilityNetworkAuthority_시작실패정리중종료예외가원래예외를가리지않음()
        {
            var network = new FakeAbilityNetwork<AuthorityPacket>(0.1);
            using var host = new AuthorityHost(network);
            host.CreateSessions = true;
            host.AddOwner(1, "Skill", 0, 3, connection: 10);
            ExecutionSession failed = null;
            host.BeginFault = () =>
            {
                failed = host.Sessions[host.Sessions.Count - 1];
                failed.OnEnded += (_, _) => throw new ApplicationException("종료 실패");
                throw new InvalidOperationException("시작 실패");
            };

            var exception = Assert.Throws<InvalidOperationException>
            (
                () => host.ExecuteDirect(1, 1)
            );

            Assert.AreEqual("시작 실패", exception.Message);
            Assert.AreEqual(0, host.Sessions.Count);
            Assert.IsNotNull(failed);
            Assert.IsFalse(failed.IsActive);
            Assert.AreEqual(ExecutionEndReason.Cancelled, failed.EndReason);
        }

    #endregion

    #region S-1: 서버 재선택과 State 정정

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> 클라이언트 뷰가 준비 상태여도 서버 Cooldown이 먼저 시작되면 거절하고
        /// <br/> State 복사본으로 정정된다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_AbilityNetworkAuthority_서버Cooldown이먼저시작되면거절하고복사본으로정정()
        {
            var network = new FakeAbilityNetwork<AuthorityPacket>(0.1);
            using var host = new AuthorityHost(network);
            using var kit = new AuthorityKit("Skill", 0, 3);
            var serverKit = host.AddOwner(1, "Skill", 0, 3, connection: 10);
            var clientObserver = host.Replication.AddClientObserver(10);
            host.Replication.Enter(clientObserver, 1, send: false);

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
                            Assert.IsTrue(serverKit.State.TryBeginCooldown(5f));
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

    #region E-1: Epoch 거부

        // ----------------------------------------------------------------------
        /// <summary>
        /// Reconnect 뒤 이전 Epoch의 요청은 거부되고 현재 Epoch의 요청만 한 번 실행된다.
        /// </summary>
        // ----------------------------------------------------------------------
        [TestCase(false)]
        [TestCase(true)]
        public void TEST_AbilityNetworkAuthority_이전Epoch요청은거부되고중복실행없음(bool reverse)
        {
            var network = new FakeAbilityNetwork<AuthorityPacket>(0.1);
            using var host = new AuthorityHost(network);
            using var kit = new AuthorityKit("Skill", 0, 3);
            host.AddOwner(1, "Skill", 0, 3, connection: 10);
            var client = new AuthorityClient(network, kit, 10, 1);
            var server = host.GetKit(1).State;

            // 이전 Epoch의 요청이 전송 중인 채로 Reconnect한다.
            var stale = client.Create();
            client.Send(stale, delayTicks: 1, copies: 2);
            network.Server.ChangeEpoch(1);
            client.JoinEpoch(1);
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
            client.Endpoint.ChangeEpoch(0);
            client.Send(stale);
            client.Endpoint.ChangeEpoch(1);
            Step(network, host, client);
            Step(network, host, client);
            Assert.AreEqual(3, Count(client, AuthorityStatus.StaleEpoch));
            Assert.AreEqual(1, host.Started.Count);
            Assert.AreEqual(2, server.Charge.Current);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> 이전 Epoch에서 승인된 응답이 새 Epoch 시작 뒤
        /// <br/> 도착해도 기록하거나 전달하지 않고 버린다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_AbilityNetworkAuthority_이전Epoch에서승인된늦은응답은새Epoch시작뒤에버림()
        {
            var network = new FakeAbilityNetwork<AuthorityPacket>(0.1);
            using var host = new AuthorityHost(network);
            using var kit = new AuthorityKit("Skill", 0, 3);
            host.AddOwner(1, "Skill", 0, 3, connection: 10);
            host.ReplyDelay = 3;
            var client = new AuthorityClient(network, kit, 10, 1);

            client.Send(client.Create());
            Step(network, host, client);
            Assert.AreEqual(1, host.Started.Count);
            Assert.AreEqual(0, client.Results.Count);

            // 승인 응답이 전송 중인 채로 새 Epoch로 전환한다.
            network.Server.ChangeEpoch(1);
            client.JoinEpoch(1);
            var handled = 0;
            client.Attach(_ => handled++, null, null, null);

            for (var i = 0; i < 4; i++)
            {
                Step(network, host, client);
            }

            Assert.AreEqual(0, client.Results.Count);
            Assert.AreEqual(0, handled);
            Assert.AreEqual(1, client.DroppedEpoch);
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> Client가 전환하기 전에 도착한 미래 Epoch의 Snapshot과 제거 통지는
        /// <br/> 적용하지 않고 버린다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_AbilityNetworkAuthority_전환전Client는미래Epoch메시지를버림()
        {
            var network = new FakeAbilityNetwork<AuthorityPacket>(0.1);
            using var host = new AuthorityHost(network);
            var serverKit = host.AddOwner(1, "Skill", 0, 3, connection: 10);
            using var kit = new AuthorityKit("Skill", 0, 3);
            var observer = host.Replication.AddClientObserver(10);
            host.Replication.Enter(observer, 1, send: false);
            var client = new AuthorityClient(network, kit, 10, 1);

            // 서버만 먼저 새 Epoch로 전환한다.
            network.Server.ChangeEpoch(1);
            Assert.IsTrue(serverKit.State.TryConsumeCharge(1));
            Step(network, host, client);
            Step(network, host, client);

            // 새 Epoch의 Ability Snapshot과 Status Snapshot이 각각 한 번씩 버려진다.
            Assert.AreEqual(2, client.DroppedEpoch);
            Assert.AreEqual(0, client.SnapshotsApplied);
            Assert.AreEqual(0, client.StatusSnapshotsApplied);
            Assert.AreEqual(3, kit.State.Charge.Current);

            host.Despawn(1);
            Step(network, host, client);

            Assert.AreEqual(3, client.DroppedEpoch);
            Assert.IsFalse(client.Despawned);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> 제거 전에 보낸 요청이 재등록된 Owner를 실행하지 못하게
        /// <br/> 같은 Epoch의 재등록을 막고 새 Epoch의 이전 요청은 거부한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_AbilityNetworkAuthority_제거전요청은재등록된Owner를실행하지못함()
        {
            var network = new FakeAbilityNetwork<AuthorityPacket>(0.1);
            using var host = new AuthorityHost(network);
            using var kit = new AuthorityKit("Skill", 0, 3);
            host.AddOwner(1, "Skill", 0, 3, connection: 10);
            var client = new AuthorityClient(network, kit, 10, 1);

            client.Send(client.Create(), delayTicks: 2);
            host.Despawn(1);

            // 같은 Epoch에서는 재등록할 수 없고 새 Epoch의 세션에서만 등록한다.
            Assert.Throws<ArgumentException>(() => host.AddOwner(1, "Skill", 0, 3, connection: 10));
            network.Server.ChangeEpoch(1);
            client.JoinEpoch(1);
            var renewed = host.AddOwner(1, "Skill", 0, 3, connection: 10);
            for (var i = 0; i < 3; i++)
            {
                Step(network, host, client);
            }

            Assert.AreEqual(1, Count(client, AuthorityStatus.StaleEpoch));
            Assert.AreEqual(0, host.Started.Count);
            Assert.AreEqual(3, renewed.State.Charge.Current);
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> Revision이 같아도 Epoch가 바뀌면 Observer에게 초기 Snapshot을
        /// <br/> 다시 보낸다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_AbilityNetworkAuthority_Epoch가바뀌면Revision이같아도Snapshot을재전송()
        {
            var network = new FakeAbilityNetwork<AuthorityPacket>(0.1);
            using var host = new AuthorityHost(network);
            host.AddOwner(1, "Skill", 0, 3, connection: 10);
            using var kit = new AuthorityKit("Skill", 0, 3);
            var observer = host.Replication.AddClientObserver(10);
            var recorder = host.Replication.AddObserver();
            var client = new AuthorityClient(network, kit, 10, 1);
            host.Replication.Enter(observer, 1);
            host.Replication.Enter(recorder, 1);
            Step(network, host, client);
            Assert.AreEqual(1, client.SnapshotsApplied);
            Assert.AreEqual(1, recorder.Count(1));

            // 서버 상태는 바뀌지 않은 채 새 세션으로 전환한다.
            network.Server.ChangeEpoch(1);
            client.JoinEpoch(1);
            Step(network, host, client);
            Step(network, host, client);

            Assert.AreEqual(2, client.SnapshotsApplied);

            // 합류 통지가 없는 Observer도 전송 기록의 Epoch 비교만으로 다시 받는다.
            Assert.AreEqual(2, recorder.Count(1));
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> 서버가 먼저 새 Epoch로 전환해 보낸 Snapshot을 Client가 버렸다면
        /// <br/> Client가 전환해 합류한 뒤 같은 Revision의 Snapshot을 다시 받는다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_AbilityNetworkAuthority_서버가먼저전환해버려진Snapshot은Client합류뒤재전송()
        {
            var network = new FakeAbilityNetwork<AuthorityPacket>(0.1);
            using var host = new AuthorityHost(network);
            var serverKit = host.AddOwner(1, "Skill", 0, 3, connection: 10);
            using var kit = new AuthorityKit("Skill", 0, 3);
            var observer = host.Replication.AddClientObserver(10);
            host.Replication.Enter(observer, 1, send: false);
            var client = new AuthorityClient(network, kit, 10, 1);

            // 서버만 새 Epoch에서 변경하고 보낸 Snapshot은 아직 전환하지 않은 Client가 버린다.
            network.Server.ChangeEpoch(1);
            Assert.IsTrue(serverKit.State.TryConsumeCharge(1));
            Step(network, host, client);
            Step(network, host, client);

            // 새 Epoch의 Ability Snapshot과 Status Snapshot이 각각 한 번씩 버려진다.
            Assert.AreEqual(2, client.DroppedEpoch);
            Assert.AreEqual(3, kit.State.Charge.Current);

            // 서버 State는 더 바뀌지 않은 채 Client가 전환해 합류한다.
            client.JoinEpoch(1);
            for (var i = 0; i < 3; i++)
            {
                Step(network, host, client);
            }

            Assert.AreEqual(1, client.SnapshotsApplied);
            Assert.AreEqual(2, kit.State.Charge.Current);
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> 서버가 먼저 전환한 사이의 제거 통지를 Client가 버렸어도
        /// <br/> 합류 때 받은 Owner 목록으로 제거를 반영하고, 남은 Owner는 유지한다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_AbilityNetworkAuthority_서버가먼저전환한사이의제거는합류때Owner목록으로반영()
        {
            var network = new FakeAbilityNetwork<AuthorityPacket>(0.1);
            using var host = new AuthorityHost(network);
            host.AddOwner(1, "Skill", 0, 3, connection: 10);
            host.AddOwner(2, "Skill", 0, 3, connection: 11);
            using var kit = new AuthorityKit("Skill", 0, 3);
            using var otherKit = new AuthorityKit("Skill", 0, 3);
            var client = new AuthorityClient(network, kit, 10, 1);
            var other = new AuthorityClient(network, otherKit, 11, 2);
            var despawned = 0;
            client.Attach(null, null, () => despawned++, null);

            // 서버만 새 Epoch로 전환한 뒤 Owner 1을 제거하면 그 통지는 아직 전환하지 않은 Client가 버린다.
            network.Server.ChangeEpoch(1);
            host.Despawn(1);
            var others = new[]
            {
                other,
            };
            Step(network, host, client, others: others);
            Assert.AreEqual(1, client.DroppedEpoch);
            Assert.IsFalse(client.Despawned);

            // 두 Client가 새 Epoch에 합류하면 Owner 목록으로 제거를 맞춘다.
            client.JoinEpoch(1);
            other.JoinEpoch(1);
            Step(network, host, client, others: others);
            Step(network, host, client, others: others);

            Assert.IsTrue(client.Despawned);
            Assert.AreEqual(1, despawned);
            Assert.AreEqual(AuthorityInput.LocallyRejected, client.TryUse(out _));
            Assert.IsFalse(other.Despawned);
            Assert.AreEqual(AuthorityInput.Sent, other.TryUse(out _));
        }

    #endregion

    #region F-1: AOI와 변경분 복제

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/> 시야 밖 Observer는 해당 Ability의 State 복사본과 시작 사건을 받지 않고
        /// <br/> 진입과 Revision 변경에만 전송된다.
        /// </summary>
        // --------------------------------------------------------------------------------
        [Test]
        public void TEST_AbilityNetworkAuthority_AOI밖에는복사본과시작사건을보내지않음()
        {
            var network = new FakeAbilityNetwork<AuthorityPacket>(0.1);
            using var host = new AuthorityHost(network);
            var enemy = host.AddOwner(2, "Enemy", 0, 3);
            var ally = host.Replication.AddObserver();
            var far = host.Replication.AddObserver();

            enemy.State.TryBeginCooldown(5f);
            host.Replication.Replicate();
            Assert.AreEqual(0, ally.Count(2));
            Assert.AreEqual(0, far.Count(2));

            host.Replication.Enter(ally, 2);
            Assert.AreEqual(1, ally.Count(2));
            host.Replication.Replicate();
            host.Replication.Replicate();
            Assert.AreEqual(1, ally.Count(2));

            // 보이는 동안 Revision이 바뀌면 한 번만 전송한다.
            enemy.State.Values.SetI("rage", 1);
            host.Replication.Replicate();
            host.Replication.Replicate();
            Assert.AreEqual(2, ally.Count(2));

            // 사용은 Revision을 여러 번 올려도 복사본 한 번과 시작 사건 한 번으로 전달된다.
            // 시간 진행은 Revision을 올리지 않으므로 Cooldown이 끝나도 복사본을 다시 보내지 않는다.
            var revisionBeforeIdle = enemy.State.Revision;
            Idle(network, host, 60);
            Assert.IsTrue(enemy.State.IsCooldownReady);
            Assert.AreEqual(revisionBeforeIdle, enemy.State.Revision);
            host.Replication.Replicate();
            Assert.AreEqual(2, ally.Count(2));

            var used = host.ExecuteDirect(2, 0);
            Assert.AreEqual(AuthorityStatus.Accepted, used.Status);
            host.Replication.Replicate();
            Assert.AreEqual(3, ally.Count(2));
            Assert.AreEqual(enemy.State.Revision, ally.Snapshots[ally.Snapshots.Count - 1].Snapshot.Revision);
            Assert.AreEqual(1, ally.Started.Count);
            Assert.AreEqual(used.ExecutionID, ally.Started[0].ExecutionID);
            Assert.AreEqual(new AbilityKey("Enemy"), ally.Started[0].Origin.AbilityKey);

            // 이탈 중의 변경은 보내지 않고 재진입할 때마다 현재 값을 한 번 보낸다.
            host.Replication.Leave(ally, 2);
            enemy.State.Values.SetI("rage", 2);
            enemy.State.Values.SetI("rage", 3);
            host.Replication.Replicate();
            Assert.AreEqual(3, ally.Count(2));
            for (var i = 1; i <= 3; i++)
            {
                host.Replication.Enter(ally, 2);
                host.Replication.Enter(ally, 2);
                host.Replication.Replicate();
                Assert.AreEqual(3 + i, ally.Count(2));
                host.Replication.Leave(ally, 2);
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
            host.Replication.EndExecution(2, used.ExecutionID, ExecutionEndReason.Completed);
            host.Replication.Enter(ally, 2);
            Assert.AreEqual(3, ally.InProgress.Count);
            Assert.AreEqual(1, ally.Started.Count);
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> Snapshot 전송 콜백이 예외를 던지면 보낸 Revision으로 기록하지 않아
        /// <br/> 복구 뒤 다음 복제에서 다시 보낸다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_AbilityNetworkAuthority_전송콜백예외는보낸Revision으로기록하지않음()
        {
            var network = new FakeAbilityNetwork<AuthorityPacket>(0.1);
            using var host = new AuthorityHost(network);
            var kit = host.AddOwner(1, "Skill", 0, 3, connection: 10);
            var observer = host.Replication.AddObserver();
            var delivered = 0;
            var failing = true;
            observer.Deliver = (_, _, _) =>
            {
                if (failing)
                {
                    throw new InvalidOperationException("send failure");
                }

                delivered++;
            };
            host.Replication.Enter(observer, 1, send: false);
            Assert.IsTrue(kit.State.TryConsumeCharge(1));

            Assert.Throws<InvalidOperationException>(() => host.Replication.Replicate());
            failing = false;
            host.Replication.Replicate();
            Assert.AreEqual(1, delivered);

            // 보낸 뒤에는 같은 Revision을 다시 보내지 않는다.
            host.Replication.Replicate();
            Assert.AreEqual(1, delivered);
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> Snapshot 전송 콜백이 다른 Owner를 제거해도 복제는 예외 없이 끝나고
        /// <br/> 제거된 Owner의 Snapshot은 보내지 않는다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_AbilityNetworkAuthority_전송콜백에서Owner를제거해도복제를계속()
        {
            var network = new FakeAbilityNetwork<AuthorityPacket>(0.1);
            using var host = new AuthorityHost(network);
            var first = host.AddOwner(1, "Skill", 0, 3, connection: 10);
            var second = host.AddOwner(2, "Skill", 0, 3, connection: 11);
            var observer = host.Replication.AddObserver();
            var delivered = new List<int>();
            observer.Deliver = (owner, packet, _) =>
            {
                if (!packet.HasSnapshot)
                {
                    observer.Record(owner, packet);
                    return;
                }

                delivered.Add(owner);
                host.Despawn(owner == 1 ? 2 : 1);
            };
            host.Replication.Enter(observer, 1, send: false);
            host.Replication.Enter(observer, 2, send: false);
            Assert.IsTrue(first.State.TryConsumeCharge(1));
            Assert.IsTrue(second.State.TryConsumeCharge(1));

            Assert.DoesNotThrow(() => host.Replication.Replicate());

            Assert.AreEqual(1, delivered.Count);
            Assert.AreEqual(1, observer.Removed.Count);
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> AOI 진입 중 Snapshot 전송이 실패해도 진입 시점에 진행 중이던 실행의
        /// <br/> 관측은 남고 이후 복제가 Snapshot을 다시 보낸다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_AbilityNetworkAuthority_진입중Snapshot전송이실패해도진행중관측은기록()
        {
            var network = new FakeAbilityNetwork<AuthorityPacket>(0.1);
            using var host = new AuthorityHost(network);
            host.AddOwner(1, "Skill", 0, 3, connection: 10);
            host.ExecuteDirect(1, 1);
            var observer = host.Replication.AddObserver();
            var failing = true;
            observer.Deliver = (owner, packet, _) =>
            {
                if (packet.HasSnapshot && failing)
                {
                    failing = false;
                    throw new InvalidOperationException("전송 실패");
                }

                observer.Record(owner, packet);
            };

            Assert.Throws<InvalidOperationException>(() => host.Replication.Enter(observer, 1));
            host.Replication.Replicate();

            Assert.AreEqual(1, host.Started.Count);
            Assert.AreEqual(1, observer.Count(1));
            Assert.AreEqual(1, observer.InProgress.Count);
            Assert.AreEqual(1, observer.InProgress[0].Owner);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> 등록되지 않은 Owner의 AOI 진입은 상태를 남기지 않고 거부하며
        /// <br/> 이후 같은 ID로 등록해도 진입 없이 Snapshot을 보내지 않는다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_AbilityNetworkAuthority_미등록Owner의진입은거부하고가시상태를남기지않음()
        {
            var network = new FakeAbilityNetwork<AuthorityPacket>(0.1);
            using var host = new AuthorityHost(network);
            var observer = host.Replication.AddObserver();

            Assert.Throws<ArgumentException>(() => host.Replication.Enter(observer, 42));
            Assert.Throws<ArgumentException>(() => host.Replication.Enter(observer, 42, send: false));
            Assert.IsFalse(observer.Visible.Contains(42));

            host.AddOwner(42, "Skill", 0, 3);
            host.Replication.Replicate();

            Assert.AreEqual(0, observer.Count(42));
        }

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> Snapshot 전송 콜백이 Owner를 가시 목록에서 빼거나 제거하면
        /// <br/> 전송 기록을 다시 만들지 않는다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_AbilityNetworkAuthority_전송콜백이제거한Owner의전송기록은남기지않음()
        {
            var network = new FakeAbilityNetwork<AuthorityPacket>(0.1);
            using var host = new AuthorityHost(network);
            var left = host.AddOwner(1, "Skill", 0, 3);
            var removed = host.AddOwner(2, "Skill", 0, 3);
            var observer = host.Replication.AddObserver();
            host.Replication.Enter(observer, 1);
            host.Replication.Enter(observer, 2);
            observer.Deliver = (owner, packet, _) =>
            {
                if (!packet.HasSnapshot) return;

                if (owner == 1)
                {
                    host.Replication.Leave(observer, owner);
                }
                else
                {
                    host.Despawn(owner);
                }
            };

            Assert.IsTrue(left.State.TryConsumeCharge(1));
            Assert.IsTrue(removed.State.TryConsumeCharge(1));
            host.Replication.Replicate();

            Assert.IsFalse(observer.LastSent.ContainsKey((1, new AbilityKey("Skill"))));
            Assert.IsFalse(observer.LastSent.ContainsKey((2, new AbilityKey("Skill"))));
        }

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> Snapshot 전송 콜백이 같은 Owner의 복제를 다시 호출해도
        /// <br/> 같은 Revision을 중복 전달하지 않는다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_AbilityNetworkAuthority_전송콜백의중첩복제는같은Snapshot을중복전달하지않음()
        {
            var network = new FakeAbilityNetwork<AuthorityPacket>(0.1);
            using var host = new AuthorityHost(network);
            var serverKit = host.AddOwner(1, "Skill", 0, 3, connection: 10);
            var observer = host.Replication.AddObserver();
            host.Replication.Enter(observer, 1, send: false);
            Assert.IsTrue(serverKit.State.TryConsumeCharge(1));
            var delivered = 0;
            observer.Deliver = (owner, packet, _) =>
            {
                delivered++;
                if (delivered < 3)
                {
                    host.Replication.Replicate();
                }

                observer.Record(owner, packet);
            };

            host.Replication.Replicate();

            Assert.AreEqual(1, delivered);
            Assert.AreEqual(serverKit.State.Revision, observer.LastSent[(1, serverKit.State.Key)].Revision);
        }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/> Snapshot은 (Owner, AbilityKey) 단위로 바뀐 Ability만 보내고 공개 범위로 숨긴
        /// <br/> Ability는 보내지 않으며, 보이는 동안 공개 범위가 바뀌면 이탈·재진입으로 알리고,
        /// <br/> Client는 Ability마다 적용한다.
        /// </summary>
        // --------------------------------------------------------------------------------
        [Test]
        public void TEST_AbilityNetworkAuthority_Snapshot은Ability단위로바뀐것만보내고숨긴Ability는보내지않음()
        {
            var network = new FakeAbilityNetwork<AuthorityPacket>(0.1);
            using var host = new AuthorityHost(network);
            var serverKit = host.AddOwner(1, "Skill", 0, 3, connection: 10);
            var serverUltimate = serverKit.AddAbility("Ultimate");
            var skill = serverKit.State.Key;
            var ultimate = serverUltimate.Key;
            var all = host.Replication.AddObserver();
            var concealed = host.Replication.AddObserver();
            host.Replication.SetDisclosure(concealed, 1, ultimate, false);

            host.Replication.Enter(all, 1);
            host.Replication.Enter(concealed, 1);
            Assert.AreEqual(1, all.Count(1, skill));
            Assert.AreEqual(1, all.Count(1, ultimate));
            Assert.AreEqual(1, concealed.Count(1, skill));
            Assert.AreEqual(0, concealed.Count(1, ultimate));

            // 한 Ability만 바뀌면 그 Ability의 Snapshot만 보낸다.
            serverUltimate.Values.SetI("Stack", 1);
            host.Replication.Replicate();
            Assert.AreEqual(1, all.Count(1, skill));
            Assert.AreEqual(2, all.Count(1, ultimate));
            Assert.AreEqual(1, concealed.Count(1));

            Assert.IsTrue(serverKit.State.TryConsumeCharge(1));
            host.Replication.Replicate();
            Assert.AreEqual(2, all.Count(1, skill));
            Assert.AreEqual(2, all.Count(1, ultimate));
            Assert.AreEqual(2, concealed.Count(1, skill));
            Assert.AreEqual(0, concealed.Count(1, ultimate));

            // 보이는 동안 공개 범위가 바뀌면 이탈과 재진입으로 알려 공개된 Ability만 다시 보낸다.
            host.Replication.SetDisclosure(all, 1, ultimate, false);
            Assert.AreEqual(1, all.Left.Count);
            Assert.AreEqual(3, all.Count(1, skill));
            Assert.AreEqual(2, all.Count(1, ultimate));
            host.Replication.SetDisclosure(all, 1, ultimate, true);
            Assert.AreEqual(2, all.Left.Count);
            Assert.AreEqual(4, all.Count(1, skill));
            Assert.AreEqual(3, all.Count(1, ultimate));

            // Client는 같은 Key의 State에 Ability마다 Revision 순서로 적용한다.
            using var kit = new AuthorityKit("Skill", 0, 3);
            var clientUltimate = kit.AddAbility("Ultimate");
            var client = new AuthorityClient(network, kit, 10, 1);
            host.Replication.Enter(host.Replication.AddClientObserver(10), 1);
            Step(network, host, client);
            Assert.AreEqual(2, client.SnapshotsApplied);
            Assert.AreEqual(2, kit.State.Charge.Current);
            Assert.AreEqual(1, clientUltimate.Values.GetI("Stack"));

            serverUltimate.Values.SetI("Stack", 2);
            Step(network, host, client);
            Step(network, host, client);
            Assert.AreEqual(3, client.SnapshotsApplied);
            Assert.AreEqual(2, clientUltimate.Values.GetI("Stack"));
            Assert.AreEqual(2, kit.State.Charge.Current);
        }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/> 실행이 끝나면 AOI 안 Observer에만 종료 사유를 그대로 알리고 진행 중 기록을
        /// <br/> 지우며, Owner 제거로 끝난 실행은 종료 사건 없이 제거 통지가 대신한다.
        /// </summary>
        // --------------------------------------------------------------------------------
        [Test]
        public void TEST_AbilityNetworkAuthority_실행종료는AOI안Observer에종료사유를그대로알림()
        {
            var network = new FakeAbilityNetwork<AuthorityPacket>(0.1);
            using var host = new AuthorityHost(network);
            host.CreateSessions = true;
            host.AddOwner(1, "Skill", 0, 3, connection: 10);
            var ally = host.Replication.AddObserver();
            var far = host.Replication.AddObserver();
            host.Replication.Enter(ally, 1);

            Assert.AreEqual(AuthorityStatus.Accepted, host.ExecuteDirect(1, 1).Status);
            host.Sessions[0].End(ExecutionEndReason.Interrupted);

            Assert.AreEqual(1, ally.Ended.Count);
            Assert.AreEqual((1, host.Started[0].ExecutionID, ExecutionEndReason.Interrupted), ally.Ended[0]);
            Assert.AreEqual(0, far.Ended.Count);

            // 끝난 실행은 늦게 진입한 Observer에 진행 중으로 알리지 않는다.
            var late = host.Replication.AddObserver();
            host.Replication.Enter(late, 1);
            Assert.AreEqual(0, late.InProgress.Count);

            // 다음 단계로 교체된 종료도 사유를 줄이지 않고 그대로 알린다.
            Assert.AreEqual(AuthorityStatus.Accepted, host.ExecuteDirect(1, 2).Status);
            host.Sessions[1].End(ExecutionEndReason.Replaced);
            Assert.AreEqual(ExecutionEndReason.Replaced, ally.Ended[1].Reason);
            Assert.AreEqual(1, late.Ended.Count);

            // Owner 제거로 끝난 실행은 종료 사건 없이 제거 통지가 대신한다.
            Assert.AreEqual(AuthorityStatus.Accepted, host.ExecuteDirect(1, 3).Status);
            host.Despawn(1);
            Assert.AreEqual(ExecutionEndReason.Cancelled, host.Sessions[2].EndReason);
            Assert.AreEqual(2, ally.Ended.Count);
            Assert.AreEqual(1, ally.Removed.Count);
        }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/> 실행 시작·종료와 Owner 제거 통지가 Snapshot과 같은 경로로 AOI 안 연결에만 가고
        /// <br/> 현재 Epoch 규칙을 같이 적용받으며, Client는 종료 사유로 원격 연출을 끝낸다.
        /// </summary>
        // --------------------------------------------------------------------------------
        [Test]
        public void TEST_AbilityNetworkAuthority_관측사건은한경로로AOI안연결에만가고Client가원격연출을끝냄()
        {
            var network = new FakeAbilityNetwork<AuthorityPacket>(0.1);
            using var host = new AuthorityHost(network);
            host.CreateSessions = true;
            host.AddOwner(2, "Enemy", 0, 3);
            host.AddOwner(3, "Skill", 0, 3, connection: 11);
            host.AddOwner(4, "Skill", 0, 3, connection: 12);
            using var peerKit = new AuthorityKit("Skill", 0, 3);
            using var farKit = new AuthorityKit("Skill", 0, 3);
            var peer = new AuthorityClient(network, peerKit, 11, 3);
            var far = new AuthorityClient(network, farKit, 12, 4);
            var observer = host.Replication.AddClientObserver(11);
            host.Replication.Enter(observer, 2);
            var others = new[]
            {
                far,
            };
            Step(network, host, peer, others: others);
            var farReceived = far.Endpoint.Received.Count;

            // 시작 사건으로 원격 연출을 시작하고 종료 사유 그대로 끝낸다.
            var first = host.ExecuteDirect(2, 1);
            Step(network, host, peer, others: others);
            Assert.IsTrue(peer.RemoteRunning[2].Contains(first.ExecutionID));

            host.Sessions[0].End(ExecutionEndReason.Interrupted);
            Step(network, host, peer, others: others);
            Assert.IsFalse(peer.RemoteRunning.ContainsKey(2));
            Assert.AreEqual((2, first.ExecutionID, ExecutionEndReason.Interrupted), peer.RemoteEnded[0]);

            // 서버만 다음 세대로 넘어간 사이의 사건은 Snapshot과 같이 Epoch 규칙으로 버려진다.
            network.Server.ChangeEpoch(1);
            var stale = peer.DroppedEpoch;
            host.ExecuteDirect(2, 2);
            Step(network, host, peer, others: others);
            Assert.Greater(peer.DroppedEpoch, stale);
            Assert.IsFalse(peer.RemoteRunning.ContainsKey(2));

            // 새 세대에 합류하면 서버가 보이는 Owner의 진행 중 실행을 진행 중 관측 사건으로 다시 알린다.
            // 그 뒤 Owner 제거 통지가 같은 경로로 원격 연출을 정리한다.
            peer.JoinEpoch(1);
            Step(network, host, peer, others: others);
            Step(network, host, peer, others: others);
            Assert.IsTrue(peer.RemoteRunning[2].Contains(host.Started[1].ExecutionID));

            host.Despawn(2);
            Step(network, host, peer, others: others);
            Assert.IsFalse(peer.RemoteRunning.ContainsKey(2));
            Assert.IsFalse(peer.Despawned);
            Assert.AreEqual(1, observer.Removed.Count);

            // 보지 않던 연결에는 어떤 관측 사건도 가지 않는다.
            Assert.AreEqual(farReceived, far.Endpoint.Received.Count);
        }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/> AOI 이탈은 관측 사건과 같은 경로로 Client에 알려 원격 사본과 연출을 지우고,
        /// <br/> 시야 밖에서 끝난 실행이 다시 보일 때 유령 연출로 남지 않는지 검증한다.
        /// </summary>
        // --------------------------------------------------------------------------------
        [Test]
        public void TEST_AbilityNetworkAuthority_AOI이탈은원격사본과연출을지우고재진입하면다시받음()
        {
            var network = new FakeAbilityNetwork<AuthorityPacket>(0.1);
            using var host = new AuthorityHost(network);
            host.CreateSessions = true;
            var enemy = host.AddOwner(2, "Enemy", 0, 3);
            host.AddOwner(3, "Skill", 0, 3, connection: 11);
            using var peerKit = new AuthorityKit("Skill", 0, 3);
            var peer = new AuthorityClient(network, peerKit, 11, 3);
            var observer = host.Replication.AddClientObserver(11);
            host.Replication.Enter(observer, 2);
            host.ExecuteDirect(2, 1);
            Step(network, host, peer);
            Step(network, host, peer);
            Assert.IsNotNull(peer.Replica(2, enemy.State.Key));
            Assert.IsTrue(peer.RemoteRunning[2].Contains(host.Started[0].ExecutionID));

            // 시야에서 사라지면 사본과 원격 연출을 지운다.
            host.Replication.Leave(observer, 2);
            Step(network, host, peer);
            Assert.AreEqual(1, observer.Left.Count);
            Assert.IsNull(peer.Replica(2, enemy.State.Key));
            Assert.IsFalse(peer.RemoteRunning.ContainsKey(2));

            // 시야 밖에서 끝난 실행은 종료 사건이 오지 않아도 다시 보일 때 연출로 남지 않는다.
            host.Sessions[0].End(ExecutionEndReason.Completed);
            host.Replication.Enter(observer, 2);
            Step(network, host, peer);
            Assert.AreEqual(0, peer.RemoteEnded.Count);
            Assert.IsFalse(peer.RemoteRunning.ContainsKey(2));
            Assert.IsNotNull(peer.Replica(2, enemy.State.Key));
            Assert.AreEqual(enemy.State.Charge.Current, peer.Replica(2, enemy.State.Key).Charge.Current);
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> 보이는 동안 공개 범위로 숨긴 Ability는 원격 Client 사본에서 지워지고,
        /// <br/> 다시 공개하면 그 Ability의 사본을 처음부터 다시 받는지 검증한다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_AbilityNetworkAuthority_공개범위로숨긴Ability는원격사본에서지우고공개하면다시받음()
        {
            var network = new FakeAbilityNetwork<AuthorityPacket>(0.1);
            using var host = new AuthorityHost(network);
            var serverKit = host.AddOwner(1, "Skill", 0, 3, connection: 10);
            var ultimate = serverKit.AddAbility("Ultimate").Key;
            var skill = serverKit.State.Key;
            host.AddOwner(2, "Skill", 0, 3, connection: 11);
            using var peerKit = new AuthorityKit("Skill", 0, 3);
            var peer = new AuthorityClient(network, peerKit, 11, 2);
            var observer = host.Replication.AddClientObserver(11);
            host.Replication.Enter(observer, 1);
            Step(network, host, peer);
            Assert.IsNotNull(peer.Replica(1, skill));
            Assert.IsNotNull(peer.Replica(1, ultimate));

            host.Replication.SetDisclosure(observer, 1, ultimate, false);
            Step(network, host, peer);
            Assert.IsNotNull(peer.Replica(1, skill));
            Assert.IsNull(peer.Replica(1, ultimate));

            host.Replication.SetDisclosure(observer, 1, ultimate, true);
            Step(network, host, peer);
            Assert.IsNotNull(peer.Replica(1, skill));
            Assert.IsNotNull(peer.Replica(1, ultimate));
        }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/> 같은 Owner의 실행이 겹치면 실행마다 시작·종료를 알리고, 늦게 진입한 Observer는
        /// <br/> 진행 중인 실행을 모두 진행 중 사건으로 받으며, 앞 실행이 먼저 끝나도 뒤 실행은
        /// <br/> 진행 중으로 남는지 검증한다.
        /// </summary>
        // --------------------------------------------------------------------------------
        [Test]
        public void TEST_AbilityNetworkAuthority_겹친실행은실행마다시작과종료를알리고진입때모두받음()
        {
            var network = new FakeAbilityNetwork<AuthorityPacket>(0.1);
            using var host = new AuthorityHost(network);
            host.CreateSessions = true;
            host.AddOwner(1, "Skill", 0, 3, connection: 10);
            host.AddOwner(2, "Skill", 0, 3, connection: 11);
            using var peerKit = new AuthorityKit("Skill", 0, 3);
            var peer = new AuthorityClient(network, peerKit, 11, 2);
            host.Replication.Enter(host.Replication.AddClientObserver(11), 1);

            Assert.AreEqual(AuthorityStatus.Accepted, host.ExecuteDirect(1, 1).Status);
            Assert.AreEqual(AuthorityStatus.Accepted, host.ExecuteDirect(1, 2).Status);
            var first = host.Started[0].ExecutionID;
            var second = host.Started[1].ExecutionID;
            Step(network, host, peer);
            Assert.AreEqual(2, peer.RemoteRunning[1].Count);

            // 늦게 진입한 Observer는 진행 중인 실행을 시작 순서대로 모두 받는다.
            var late = host.Replication.AddObserver();
            host.Replication.Enter(late, 1);
            Assert.AreEqual(2, late.InProgress.Count);
            Assert.AreEqual(first, late.InProgress[0].ExecutionID);
            Assert.AreEqual(second, late.InProgress[1].ExecutionID);

            // 앞 실행이 먼저 끝나면 그 실행의 종료만 알리고 뒤 실행은 진행 중으로 남는다.
            host.Sessions[0].End(ExecutionEndReason.Interrupted);
            Step(network, host, peer);
            Assert.AreEqual(1, late.Ended.Count);
            Assert.AreEqual(first, late.Ended[0].ExecutionID);
            Assert.AreEqual(ExecutionEndReason.Interrupted, late.Ended[0].Reason);
            Assert.IsFalse(peer.RemoteRunning[1].Contains(first));
            Assert.IsTrue(peer.RemoteRunning[1].Contains(second));
            Assert.AreEqual(first, peer.RemoteEnded[0].ExecutionID);

            var rejoined = host.Replication.AddObserver();
            host.Replication.Enter(rejoined, 1);
            Assert.AreEqual(1, rejoined.InProgress.Count);
            Assert.AreEqual(second, rejoined.InProgress[0].ExecutionID);
        }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/> 한 Observer에 시작 사건 전달이 실패해도 확정된 사용과 비용은 되돌리지 않고
        /// <br/> Host Fault로 남기며 다른 Observer에는 계속 보내는지 검증한다.
        /// </summary>
        // --------------------------------------------------------------------------------
        [Test]
        public void TEST_AbilityNetworkAuthority_시작사건전달실패는확정된사용과비용을되돌리지않음()
        {
            var network = new FakeAbilityNetwork<AuthorityPacket>(0.1);
            using var host = new AuthorityHost(network);
            var serverKit = host.AddOwner(1, "Skill", 0, 3, connection: 10);
            using var kit = new AuthorityKit("Skill", 0, 3);
            var client = new AuthorityClient(network, kit, 10, 1);
            var failing = host.Replication.AddObserver();
            var watching = host.Replication.AddObserver();
            failing.Deliver = (owner, packet, _) =>
            {
                if (packet.Observation == AuthorityObservation.Started)
                {
                    throw new InvalidOperationException("시작 사건 전달 실패");
                }

                failing.Record(owner, packet);
            };
            host.Replication.Enter(failing, 1);
            host.Replication.Enter(watching, 1);

            client.Send(client.Create());
            Step(network, host, client);
            Step(network, host, client);

            Assert.AreEqual(1, Count(client, AuthorityStatus.Accepted));
            Assert.AreEqual(2, serverKit.State.Charge.Current);
            Assert.AreEqual(1, host.Started.Count);
            Assert.AreEqual(1, host.Faults.Count);
            Assert.AreEqual(1, watching.Started.Count);
            Assert.AreEqual(0, failing.Started.Count);
        }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/> 한 Observer에 제거 통지 전달이 실패해도 모든 가시 목록 정리와 다른 Observer·
        /// <br/> 조종하던 연결의 통지를 마친 뒤 실패를 전달하는지 검증한다.
        /// </summary>
        // --------------------------------------------------------------------------------
        [Test]
        public void TEST_AbilityNetworkAuthority_제거통지전달실패에도정리를끝내고조종하던연결에알림()
        {
            var network = new FakeAbilityNetwork<AuthorityPacket>(0.1);
            using var host = new AuthorityHost(network);
            host.AddOwner(1, "Skill", 0, 3, connection: 10);
            using var kit = new AuthorityKit("Skill", 0, 3);
            var client = new AuthorityClient(network, kit, 10, 1);
            var failing = host.Replication.AddObserver();
            var watching = host.Replication.AddObserver();
            failing.Deliver = (owner, packet, _) =>
            {
                if (packet.Despawned)
                {
                    throw new InvalidOperationException("제거 통지 전달 실패");
                }

                failing.Record(owner, packet);
            };
            host.Replication.Enter(failing, 1);
            host.Replication.Enter(watching, 1);

            Assert.Throws<InvalidOperationException>(() => host.Despawn(1));
            Step(network, host, client);

            Assert.IsFalse(failing.Visible.Contains(1));
            Assert.IsFalse(watching.Visible.Contains(1));
            Assert.AreEqual(1, watching.Removed.Count);
            Assert.IsTrue(client.Despawned);
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> 요청 경로에서 실행 시작이 실패하면 거절 결과를 보내고 장부에 기록해 재전송도
        /// <br/> 같은 결과를 받으며, 예외는 Host Fault로 남고 Client 예측은 되돌려진다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_AbilityNetworkAuthority_요청경로의시작실패는거절로기록하고재전송도같은결과()
        {
            var network = new FakeAbilityNetwork<AuthorityPacket>(0.1);
            using var host = new AuthorityHost(network);
            var serverKit = host.AddOwner(1, "Skill", 0, 3, connection: 10);
            using var kit = new AuthorityKit("Skill", 0, 3);
            var client = new AuthorityClient(network, kit, 10, 1);
            var predictor = new PredictionClient(network, client);
            host.BeginFault = () => throw new InvalidOperationException("시작 실패");

            var use = predictor.Predict(0);
            client.Send(new AuthorityRequest(use.RequestID, 1, AuthorityKit.EntryName, network.Tick, 1));
            Step(network, host, client);
            Step(network, host, client);

            Assert.AreEqual(1, Count(client, AuthorityStatus.Rejected));
            Assert.AreEqual(1, Count(client, AuthorityStatus.Duplicate));
            var duplicate = client.Results.Find(result => result.Status == AuthorityStatus.Duplicate);
            Assert.AreEqual(AuthorityStatus.Rejected, duplicate.Original.Status);
            Assert.AreEqual(1, host.Faults.Count);
            Assert.AreEqual(0, host.Started.Count);
            Assert.AreEqual(3, serverKit.State.Charge.Current);
            Assert.AreEqual(1, predictor.Rejected);
            Assert.IsFalse(use.Session.IsActive);
            Assert.AreEqual(3, kit.State.Charge.Current);
        }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/> 한 Observer에 Snapshot 전달이 실패해도 다른 Observer 복제는 끝내고 실패를
        /// <br/> 전달하며, 보내지 못한 Snapshot은 다음 복제에서 다시 보낸다.
        /// </summary>
        // --------------------------------------------------------------------------------
        [Test]
        public void TEST_AbilityNetworkAuthority_Snapshot전달실패는다른Observer복제를막지않고다음복제에서다시보냄()
        {
            var network = new FakeAbilityNetwork<AuthorityPacket>(0.1);
            using var host = new AuthorityHost(network);
            var serverKit = host.AddOwner(1, "Skill", 0, 3, connection: 10);
            var failing = host.Replication.AddObserver();
            var watching = host.Replication.AddObserver();
            host.Replication.Enter(failing, 1);
            host.Replication.Enter(watching, 1);
            var fail = true;
            failing.Deliver = (owner, packet, _) =>
            {
                if (packet.HasSnapshot && fail)
                {
                    throw new InvalidOperationException("Snapshot 전달 실패");
                }

                failing.Record(owner, packet);
            };

            Assert.IsTrue(serverKit.State.TryConsumeCharge(1));
            Assert.Throws<InvalidOperationException>(() => host.Pump());
            Assert.AreEqual(1, failing.Count(1));
            Assert.AreEqual(2, watching.Count(1));

            fail = false;
            host.Pump();
            Assert.AreEqual(2, failing.Count(1));
            Assert.AreEqual(2, watching.Count(1));
        }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/> 연결된 계층의 결과 콜백이 실패해도 같은 Tick의 나머지 메시지와 다른 Client의
        /// <br/> 수신을 마친 뒤 실패를 전달하는지 검증한다.
        /// </summary>
        // --------------------------------------------------------------------------------
        [Test]
        public void TEST_AbilityNetworkAuthority_Client수신은콜백실패에도같은Tick의나머지를처리()
        {
            var network = new FakeAbilityNetwork<AuthorityPacket>(0.1);
            using var host = new AuthorityHost(network);
            var serverKit = host.AddOwner(1, "Skill", 0, 3, connection: 10);
            host.AddOwner(2, "Skill", 0, 3, connection: 11);
            using var kit = new AuthorityKit("Skill", 0, 3);
            using var peerKit = new AuthorityKit("Skill", 0, 3);
            var client = new AuthorityClient(network, kit, 10, 1);
            var peer = new AuthorityClient(network, peerKit, 11, 2);
            var others = new[]
            {
                peer,
            };
            client.Attach(_ => throw new InvalidOperationException("결과 처리 실패"), null, null, null);
            host.Replication.Enter(host.Replication.AddClientObserver(10), 1, send: false);
            host.Replication.Enter(host.Replication.AddClientObserver(11), 1);

            client.Send(client.Create());
            Step(network, host, client, others: others);
            Assert.AreEqual(1, peer.SnapshotsApplied);

            // 결과 콜백이 실패해도 같은 Tick에 도착한 Snapshot을 적용하고 다른 Client도 받는다.
            Assert.Throws<InvalidOperationException>(() => Step(network, host, client, others: others));
            Assert.AreEqual(1, client.Results.Count);
            Assert.AreEqual(1, client.SnapshotsApplied);
            Assert.AreEqual(serverKit.State.Charge.Current, kit.State.Charge.Current);
            Assert.AreEqual(2, peer.SnapshotsApplied);
            Assert.IsTrue(peer.RemoteRunning[1].Contains(host.Started[0].ExecutionID));
        }

    #endregion

    #region I-1: 연결 신원

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/> 다른 연결이 남의 Owner를 적어 보낸 요청은 실행하지 않고 그 연결로만 거부하며,
        /// <br/> 각 연결의 결과는 그 연결에만 전달된다.
        /// </summary>
        // --------------------------------------------------------------------------------
        [Test]
        public void TEST_AbilityNetworkAuthority_남의Owner를적은요청은실행하지않고보낸연결로만거부()
        {
            var network = new FakeAbilityNetwork<AuthorityPacket>(0.1);
            using var host = new AuthorityHost(network);
            var victim = host.AddOwner(1, "Skill", 0, 3, connection: 10);
            var attacker = host.AddOwner(2, "Skill", 0, 3, connection: 11);
            using var victimKit = new AuthorityKit("Skill", 0, 3);
            using var attackerKit = new AuthorityKit("Skill", 0, 3);
            var victimClient = new AuthorityClient(network, victimKit, 10, 1);
            var attackerClient = new AuthorityClient(network, attackerKit, 11, 2);

            // 연결 11이 Owner 1의 요청을 만든다. 요청에는 보낸 연결을 적을 곳이 없다.
            attackerClient.Send(new AuthorityRequest(1, 1, AuthorityKit.EntryName, network.Tick, 1));
            var others = new[]
            {
                attackerClient,
            };
            Step(network, host, victimClient, others: others);
            Step(network, host, victimClient, others: others);

            Assert.AreEqual(0, host.Started.Count);
            Assert.AreEqual(3, victim.State.Charge.Current);
            Assert.AreEqual(1, Count(attackerClient, AuthorityStatus.UnknownOwner));
            Assert.AreEqual(0, victimClient.Results.Count);

            // 자기 Owner의 요청은 승인되고 결과는 보낸 연결에만 간다.
            attackerClient.Send(attackerClient.Create());
            Step(network, host, victimClient, others: others);
            Step(network, host, victimClient, others: others);

            Assert.AreEqual(1, host.Started.Count);
            Assert.AreEqual(2, attacker.State.Charge.Current);
            Assert.AreEqual(3, victim.State.Charge.Current);
            Assert.AreEqual(1, Count(attackerClient, AuthorityStatus.Accepted));
            Assert.AreEqual(0, victimClient.Results.Count);
        }

    #endregion

    #region W-1: Owner 등록과 제거

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> 제거된 Owner에게 늦게 도착한 요청은 거부하고
        /// <br/> 새 Epoch에서 같은 연결로 Owner를 다시 등록할 수 있다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_AbilityNetworkAuthority_제거된Owner의늦은요청은거부하고같은연결로재등록()
        {
            var network = new FakeAbilityNetwork<AuthorityPacket>(0.1);
            using var host = new AuthorityHost(network);
            using var kit = new AuthorityKit("Skill", 0, 3);
            host.AddOwner(1, "Skill", 0, 3, connection: 10);
            var client = new AuthorityClient(network, kit, 10, 1);

            // 제거 전에 보낸 요청이 제거 뒤에 도착한다.
            client.Send(client.Create(), delayTicks: 2);
            host.Despawn(1);
            for (var i = 0; i < 3; i++)
            {
                Step(network, host, client);
            }

            Assert.AreEqual(1, Count(client, AuthorityStatus.UnknownOwner));
            Assert.AreEqual(0, host.Started.Count);

            // 새 Epoch의 세션에서 같은 연결로 다시 등록하면 새 요청이 처리된다.
            network.Server.ChangeEpoch(1);
            client.JoinEpoch(1);
            host.AddOwner(1, "Skill", 0, 3, connection: 10);
            client.Send(client.Create());
            Step(network, host, client);
            Step(network, host, client);

            Assert.AreEqual(1, Count(client, AuthorityStatus.Accepted));
            Assert.AreEqual(1, host.Started.Count);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> 중복 연결로 Owner 등록이 실패하면 아무것도 남기지 않고
        /// <br/> 올바른 연결로 다시 등록할 수 있다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_AbilityNetworkAuthority_연결중복으로등록이실패하면Owner를남기지않음()
        {
            var network = new FakeAbilityNetwork<AuthorityPacket>(0.1);
            using var host = new AuthorityHost(network);
            host.AddOwner(1, "Skill", 0, 3, connection: 10);

            Assert.Throws<ArgumentException>(() => host.AddOwner(2, "Skill", 0, 3, connection: 10));
            Assert.Throws<KeyNotFoundException>(() => host.GetKit(2));
            Assert.Throws<ArgumentException>(() => host.AddOwner(1, "Skill", 0, 3, connection: 11));

            Assert.IsNotNull(host.AddOwner(2, "Skill", 0, 3, connection: 11));
            Assert.IsNotNull(host.GetKit(2));
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> Session 종료 콜백이 예외를 던져도 Owner 제거 정리를 끝까지 수행하고
        /// <br/> 정리가 끝난 뒤 예외를 호출자에게 전달한다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_AbilityNetworkAuthority_Session종료콜백예외에도Owner제거정리를끝까지수행()
        {
            var network = new FakeAbilityNetwork<AuthorityPacket>(0.1);
            using var host = new AuthorityHost(network);
            host.CreateSessions = true;
            host.AddOwner(1, "Skill", 0, 3, connection: 10);
            using var kit = new AuthorityKit("Skill", 0, 3);
            var observer = host.Replication.AddClientObserver(10);
            host.Replication.Enter(observer, 1);
            var client = new AuthorityClient(network, kit, 10, 1);
            host.ExecuteDirect(1, 1);
            host.Sessions[0].OnEnded += (_, _) => throw new InvalidOperationException("종료 콜백 실패");

            Assert.Throws<InvalidOperationException>(() => host.Despawn(1));
            client.Send(client.Create());
            Step(network, host, client);
            Step(network, host, client);

            Assert.IsFalse(observer.Visible.Contains(1));
            Assert.IsFalse(observer.LastSent.ContainsKey((1, new AbilityKey("Skill"))));
            Assert.IsTrue(client.Despawned);
            Assert.AreEqual(1, Count(client, AuthorityStatus.UnknownOwner));
        }

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> 충전 알림에서 Owner를 제거해도 서버 진행은 중단되지 않고
        /// <br/> 남은 Owner가 같은 Tick에 진행된다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_AbilityNetworkAuthority_충전알림에서Despawn해도남은Owner를진행()
        {
            var network = new FakeAbilityNetwork<AuthorityPacket>(0.1);
            using var host = new AuthorityHost(network);
            var first = host.AddOwner(1, "Skill", 0, 3, connection: 10);
            var second = host.AddOwner(2, "Skill", 0, 3, connection: 11);
            Assert.IsTrue(first.State.TryConsumeCharge(1));
            Assert.IsTrue(second.State.TryConsumeCharge(1));
            first.State.OnChargeRecharged += (_, _) => host.Despawn(1);

            Assert.DoesNotThrow(() => host.Advance(10f));

            Assert.Throws<KeyNotFoundException>(() => host.GetKit(1));
            Assert.AreEqual(3, second.State.Charge.Current);
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> 한 Owner의 진행 알림이 예외를 던져도 남은 Owner를 같은 Tick에 진행하고
        /// <br/> 그 Tick의 수신까지 마친 뒤 실패를 전달하는지 검증한다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_AbilityNetworkAuthority_진행알림예외에도남은Owner와수신을마치고실패를전달()
        {
            // 한 Tick이 충전 시간(10초)과 같아 Step의 시간 진행에서 충전 알림이 난다.
            var network = new FakeAbilityNetwork<AuthorityPacket>(10);
            using var host = new AuthorityHost(network);
            var first = host.AddOwner(1, "Skill", 0, 3, connection: 10);
            var second = host.AddOwner(2, "Skill", 0, 3, connection: 11);
            using var kit = new AuthorityKit("Skill", 0, 3);
            var client = new AuthorityClient(network, kit, 10, 1);
            Assert.IsTrue(first.State.TryConsumeCharge(1));
            Assert.IsTrue(second.State.TryConsumeCharge(1));
            first.State.OnChargeRecharged += (_, _) => throw new InvalidOperationException("충전 알림 실패");

            client.Send(client.Create());
            Assert.Throws<InvalidOperationException>(() => Step(network, host, client));

            Assert.AreEqual(3, second.State.Charge.Current);
            Assert.AreEqual(1, host.Started.Count);
        }

    #endregion

    #region O-1: 로컬에서 온라인 전환

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/> 로컬 정지, 새 Epoch, 신원 매핑, State 복제(CloneFrom)로 State가 이어지고
        /// <br/> 이전 Session 명령은 거부된다.
        /// </summary>
        // --------------------------------------------------------------------------------
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
            client.JoinEpoch(1);
            var serverKit = host.AddOwner(1, "Skill", 2f, 3, connection: 10);
            var before = serverKit.State.Revision;
            serverKit.State.CloneFrom(local.State);
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
            var observer = host.Replication.AddClientObserver(10);
            host.Replication.Enter(observer, 1);
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
            var stranger = network.Connect(99);
            stranger.ChangeEpoch(1);
            network.Send(stranger, network.Server, AuthorityPacket.ForRequest(new AuthorityRequest(1, 1, AuthorityKit.EntryName, network.Tick, 1)));
            Step(network, host, client);
            Step(network, host, client);
            Assert.AreEqual(0, Count(client, AuthorityStatus.UnknownOwner));
            var spoofReplies = 0;
            foreach (var message in stranger.Received)
            {
                var reply = message.Payload;
                if (reply.Result != null)
                {
                    Assert.AreEqual(AuthorityStatus.UnknownOwner, reply.Result.Status);
                    spoofReplies++;
                }
            }

            // 거부 응답은 요청을 보낸 연결(99)로만 돌아가므로 이 Client의 Results에는 들어오지 않는다.
            Assert.AreEqual(1, spoofReplies);
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

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> 이전 권한 세대의 Snapshot을 적용한 Client가
        /// <br/> 새 세대의 더 낮은 Revision Snapshot도 적용한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_AbilityNetworkAuthority_새권한세대의낮은Revision의Snapshot도적용()
        {
            var network = new FakeAbilityNetwork<AuthorityPacket>(0.1);
            using var host = new AuthorityHost(network);
            using var kit = new AuthorityKit("Skill", 0, 3);
            var client = new AuthorityClient(network, kit, 10, 1);

            // 이전 권한 세대: Revision이 높은 Snapshot을 적용한다.
            var previous = new AbilityState(kit.State.Key);
            for (var i = 1; i <= 5; i++)
            {
                previous.Values.SetI("Stack", i);
            }

            network.Send(network.Server, client.Endpoint, AuthorityPacket.ForSnapshot(1, new AuthoritySnapshot(previous, network.Tick)));
            Step(network, host, client);
            Assert.AreEqual(5, kit.State.Values.GetI("Stack"));

            // 새 권한 세대: 다른 State 인스턴스라 Revision이 이전보다 낮다.
            network.Server.ChangeEpoch(1);
            client.JoinEpoch(1);
            var next = new AbilityState(kit.State.Key);
            next.Values.SetI("Stack", 100);
            Assert.Less(next.Revision, previous.Revision);

            network.Send(network.Server, client.Endpoint, AuthorityPacket.ForSnapshot(1, new AuthoritySnapshot(next, network.Tick)));
            Step(network, host, client);

            Assert.AreEqual(100, kit.State.Values.GetI("Stack"));
        }

    #endregion

    }
}
