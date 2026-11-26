/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : TEST_AbilityNetworkPrediction.cs
수정일 : 2026-11-26

# 설명
L2 상태 보정 범위를 in-process Fake Server/Client로 검증한다.
Late Join, 예상 연출, 예측 거절과 승인, Owner 제거, 값 보정, 예측 투사체의 거절과 인계, 전환 중 입력 거절을 다룬다.
예측 계층은 Fixture/Network의 테스트 코드이며 Core가 제공하는 Hook은 State 복제(Clone·CloneFrom), Advance와 ExecutionContext.Mode뿐이다.

# 테스트 구성
 J: Late Join
 A: 예상 연출(Anticipation)
 D: 예측 거절과 승인
 O: Owner 제거
 C: 값 보정
 P: 예측 투사체 거절과 인계
 W: 전환 중 입력
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
    /// 예상 연출, 예측 사용과 보정 경로를 Fake Server와 Client로 검증한다.
    /// </summary>
    // ======================================================================
    public sealed class TEST_AbilityNetworkPrediction
    {

    #region 검증용 Cost와 상태

        // ============================================================
        /// <summary>
        /// 지불 횟수와 환급 횟수를 세고 지불 때 Host 동작을 실행하는 Cost이다.
        /// </summary>
        // ============================================================
        private sealed class CountingCost : IAbilityActivationCost<AuthoritySpec>
        {
            private readonly Action onPay;
            internal int Paid { get; private set; }
            internal int Refunds { get; private set; }

            internal CountingCost(Action onPay = null)
            {
                this.onPay = onPay;
            }

            // ------------------------------------------------------------
            /// <summary>
            /// 검사 결과를 반환한다.
            /// </summary>
            // ------------------------------------------------------------
            public AbilityActivationCheck Evaluate(in AbilityActivationContext<AuthoritySpec> context) =>
                AbilityActivationCheck.Allow();

            // ------------------------------------------------------------
            /// <summary>
            /// 비용 영수증을 반환한다.
            /// </summary>
            // ------------------------------------------------------------
            public AbilityCostReceipt Pay(in AbilityActivationContext<AuthoritySpec> context)
            {
                Paid++;
                onPay?.Invoke();
                return AbilityCostReceipt.From(() => Refunds++);
            }
        }

        // ============================================================
        /// <summary>
        /// 검사에서 항상 거부하는 Cost이다. 서버만 거절하게 만들 때 쓴다.
        /// </summary>
        // ============================================================
        private sealed class DenyCost : IAbilityActivationCost<AuthoritySpec>
        {
            // ------------------------------------------------------------
            /// <summary>
            /// 항상 거부한다.
            /// </summary>
            // ------------------------------------------------------------
            public AbilityActivationCheck Evaluate(in AbilityActivationContext<AuthoritySpec> context) =>
                AbilityActivationCheck.Reject("서버 전용 거부");

            // ------------------------------------------------------------
            /// <summary>
            /// 검사에서 거부하므로 호출되지 않는다.
            /// </summary>
            // ------------------------------------------------------------
            public AbilityCostReceipt Pay(in AbilityActivationContext<AuthoritySpec> context) =>
                throw new InvalidOperationException("거부한 비용은 지불하지 않습니다.");
        }

        private sealed class GameState
        {
            internal string Stance = "Idle";
        }

    #endregion

    #region J-1: Late Join

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/> 진행 중인 State에 새 Peer가 접속하면 원격 Owner의 State 사본과 전송 지연 보정으로
        /// <br/> 같은 남은 시간을 얻고 실행은 관측 사건으로만 잇는다.
        /// </summary>
        // --------------------------------------------------------------------------------
        [TestCase(false)]
        [TestCase(true)]
        public void TEST_AbilityNetworkPrediction_LateJoin은복사본으로상태를잇고실행은관측사건으로만(bool despawnDuringJoin)
        {
            var network = new FakeAbilityNetwork<AuthorityPacket>(0.1);
            using var host = new AuthorityHost(network);
            host.CreateSessions = true;
            var serverKit = host.AddOwner(2, "Enemy", 5f, 3);
            var server = serverKit.State;
            Assert.AreEqual(AuthorityStatus.Accepted, host.ExecuteDirect(2, 0).Status);
            server.Values.SetI("stack", 4);
            Idle(network, host, 20);

            // 새 Peer는 자기 Owner 3을 조종하면서 적 Owner 2를 관찰한다.
            host.AddOwner(3, "Skill", 0, 3, connection: 11);
            using var peerKit = new AuthorityKit("Skill", 0, 3);
            var peer = new AuthorityClient(network, peerKit, 11, 3);
            host.ReplyDelay = 3;
            var observer = host.Replication.AddClientObserver(11);
            host.Replication.Enter(observer, 2);

            // 진행 중인 실행은 Session 재생성 없이 관측 사건 한 개로만 전달된다.
            Assert.AreEqual(0, observer.Started.Count);
            Assert.AreEqual(1, observer.InProgress.Count);
            Assert.AreEqual(host.Started[0].Origin, observer.InProgress[0].Origin);
            Assert.AreEqual(100UL, observer.InProgress[0].ExecutionID);
            Assert.AreEqual(host.Started[0].Tick, observer.InProgress[0].StartTick);
            var session = host.Sessions[0];
            Assert.AreEqual(3f, Remaining(server), 1e-4f);

            if (despawnDuringJoin)
            {
                // 접속 중 Despawn: 제거 통지는 순서가 보장된 관측 채널로 먼저 보낸 복사본 뒤에
                // 도착해 이미 만든 사본과 진행 중 연출을 정리한다.
                host.Despawn(2);
                StepTo(network, host, peer, 25);
                Assert.IsFalse(peer.Despawned);
                Assert.AreEqual(1, observer.Removed.Count);
                Assert.IsFalse(peer.RemoteRunning.ContainsKey(2));
                Assert.IsNull(peer.Replica(2, server.Key));
                return;
            }

            // 복사본은 3 Tick 전의 서버 State이므로 전송 지연만큼 진행한 지금은 같은 남은 시간이다.
            StepTo(network, host, peer, 25);
            var replica = peer.Replica(2, server.Key);
            Assert.IsNotNull(replica);
            Assert.AreEqual(2.5f, Remaining(server), 1e-4f);
            Assert.AreEqual(Remaining(server), Remaining(replica), 1e-4f);
            Assert.AreEqual(server.Charge.Current, replica.Charge.Current);
            Assert.AreEqual(server.Charge.Recharge.RemainingTime, replica.Charge.Recharge.RemainingTime, 1e-4f);

            // 이후에도 같은 시간만큼 진행하면 같은 시점에 Cooldown이 끝난다.
            foreach (var elapsed in new[]
            {
                1f,
                1.5f,
                3f
            })
            {
                server.Advance(elapsed);
                replica.Advance(elapsed);
                Assert.AreEqual(Remaining(server), Remaining(replica), 1e-4f);
                Assert.AreEqual(server.IsCooldownReady, replica.IsCooldownReady);
            }

            Assert.AreEqual(4, replica.Values.GetI("stack"));
            Assert.IsTrue(peer.RemoteRunning[2].Contains(100UL));
            Assert.AreEqual(1, host.Sessions.Count);
            Assert.AreSame(session, host.Sessions[0]);
            Assert.IsTrue(session.IsActive);
            Assert.AreEqual(0, observer.Started.Count);
            Assert.AreEqual(1, observer.InProgress.Count);
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> Host가 Peer마다 같은 순서로 Layer를 등록하면 같은 Entry가 같은
        /// <br/> Ability로 해석되고 같은 Priority는 나중 등록이 우선한다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_AbilityNetworkPrediction_같은순서로등록한Peer는같은Ability로해석()
        {
            var entry = new AbilityEntryKey("Q");
            AbilityBindingLayer<AuthoritySpec> Layer(int priority, string key)
            {
                var ability = new AbilityInstance<AuthoritySpec>(new AuthoritySpec(), new AbilityState(new AbilityKey(key)));
                var layer = new AbilityBindingLayer<AuthoritySpec>(priority);
                layer.Set(entry, ability);
                return layer;
            }

            var serverBindings = new AbilityBindings<AuthoritySpec>();
            using var serverBase = serverBindings.AcquireLayer(Layer(0, "Base"));
            using var serverForm = serverBindings.AcquireLayer(Layer(1, "Form"));
            var peerBindings = new AbilityBindings<AuthoritySpec>();
            using var peerBase = peerBindings.AcquireLayer(Layer(0, "Base"));
            using var peerForm = peerBindings.AcquireLayer(Layer(1, "Form"));

            Assert.IsTrue(serverBindings.TryResolve(entry, out var serverResolved));
            Assert.IsTrue(peerBindings.TryResolve(entry, out var peerResolved));
            Assert.AreEqual(new AbilityKey("Form"), serverResolved.Instance.State.Key);
            Assert.AreEqual(serverResolved.Instance.State.Key, peerResolved.Instance.State.Key);
            Assert.AreEqual(serverBindings.Revision, peerBindings.Revision);

            // 같은 Priority는 두 Peer 모두 나중에 등록한 Layer가 우선한다.
            var entryX = new AbilityEntryKey("X");
            AbilityBindingLayer<AuthoritySpec> SamePriority(string key)
            {
                var ability = new AbilityInstance<AuthoritySpec>(new AuthoritySpec(), new AbilityState(new AbilityKey(key)));
                var layer = new AbilityBindingLayer<AuthoritySpec>(1);
                layer.Set(entryX, ability);
                return layer;
            }

            var serverSame = new AbilityBindings<AuthoritySpec>();
            using var serverLow = serverSame.AcquireLayer(SamePriority("Low"));
            using var serverHigh = serverSame.AcquireLayer(SamePriority("High"));
            var peerSame = new AbilityBindings<AuthoritySpec>();
            using var peerLow = peerSame.AcquireLayer(SamePriority("Low"));
            using var peerHigh = peerSame.AcquireLayer(SamePriority("High"));

            Assert.IsTrue(serverSame.TryResolve(entryX, out var serverX));
            Assert.IsTrue(peerSame.TryResolve(entryX, out var peerX));
            Assert.AreEqual(new AbilityKey("High"), serverX.Instance.State.Key);
            Assert.AreEqual(serverX.Instance.State.Key, peerX.Instance.State.Key);
        }

    #endregion

    #region A-1: Anticipation

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> 예상 연출은 권한과 클라이언트 State를 바꾸지 않고 서버 거절 시 취소되며
        /// <br/> 승인 시 Cue를 다시 재생하지 않는다.
        /// </summary>
        // ----------------------------------------------------------------------
        [TestCase(false)]
        [TestCase(true)]
        public void TEST_AbilityNetworkPrediction_예상연출은State를바꾸지않고거절되면취소(bool accepted)
        {
            var network = new FakeAbilityNetwork<AuthorityPacket>(0.1);
            using var host = new AuthorityHost(network);
            using var kit = new AuthorityKit("Skill", 0, 3);
            var serverKit = host.AddOwner(1, "Skill", 0, 3, connection: 10);
            host.ReplyDelay = 3;
            var client = new AuthorityClient(network, kit, 10, 1);
            var anticipation = new AnticipationClient(client);
            var revision = kit.State.Revision;

            // 300ms 지연 동안 연출만 먼저 시작된다.
            Assert.IsTrue(kit.IsReady(1));
            var presentation = anticipation.Begin(3);
            Assert.AreEqual(1, presentation.Cues);
            Assert.AreEqual(5d, presentation.CooldownUi);
            StepTo
            (
                network, host, client, 5,
                tick =>
                {
                    if (!accepted && tick == 1)
                    {
                        Assert.IsTrue(serverKit.State.TryBeginCooldown(5f));
                    }
                }
            );
            Assert.IsFalse(presentation.Cancelled);
            Assert.IsFalse(presentation.Confirmed);
            Assert.AreEqual(revision, kit.State.Revision);

            StepTo(network, host, client, 6);
            Assert.AreEqual(revision, kit.State.Revision);
            Assert.AreEqual(3, kit.State.Charge.Current);
            if (accepted)
            {
                Assert.IsTrue(presentation.Confirmed);
                Assert.IsFalse(presentation.Cancelled);
                Assert.AreEqual(1, presentation.Cues);
                Assert.AreEqual(2, serverKit.State.Charge.Current);
                Assert.AreEqual(1, host.Started.Count);
            }
            else
            {
                Assert.IsTrue(presentation.Cancelled);
                Assert.AreEqual(0, presentation.Cues);
                Assert.AreEqual(0d, presentation.CooldownUi);
                Assert.AreEqual(3, serverKit.State.Charge.Current);
                Assert.AreEqual(0, host.Started.Count);
            }
        }

    #endregion

    #region D-1: 예측 거절

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> 거절된 예측은 보관한 State 복사본을 CloneFrom으로 복원하고 Session과
        /// <br/> Lease를 정리하며 Receipt를 환급하지 않는다.
        /// </summary>
        // ----------------------------------------------------------------------
        [TestCase(false)]
        [TestCase(true)]
        public void TEST_AbilityNetworkPrediction_예측거절은보관복사본으로한번만복원(bool nextInputSameTick)
        {
            var network = new FakeAbilityNetwork<AuthorityPacket>(0.1);
            using var host = new AuthorityHost(network);
            var serverKit = host.AddOwner(1, "Skill", 1f, 3, connection: 10);
            var counting = new CountingCost();
            var costs = new IAbilityActivationCost<AuthoritySpec>[]
            {
                counting,
            };
            using var kit = new AuthorityKit("Skill", 1f, 3, costs);
            host.ReplyDelay = 2;
            var client = new AuthorityClient(network, kit, 10, 1);
            var predictor = new PredictionClient(network, client);
            var baseline = kit.State.Revision;

            var first = predictor.Predict(2);
            Assert.IsNotNull(first);
            Assert.AreEqual(ExecutionMode.Predicted, first.Session.Context.Mode);
            Assert.IsTrue(first.Session.IsActive);
            Assert.IsFalse(first.Motion.IsDisposed);
            Assert.AreEqual(2, kit.State.Charge.Current);
            Assert.IsFalse(kit.State.IsCooldownReady);
            var predicted = kit.State.Revision;
            Assert.IsTrue(predicted > baseline);

            PredictedUse next = null;
            void VerifyRestored()
            {
                Assert.AreEqual(1, predictor.Restores);
                Assert.AreEqual(0, predictor.Corrections);
                Assert.AreEqual(1, counting.Paid);
                Assert.AreEqual(0, counting.Refunds);
                Assert.AreEqual(3, kit.State.Charge.Current);
                Assert.IsTrue(kit.State.IsCooldownReady);
                Assert.AreEqual(0f, Remaining(kit.State));
                Assert.IsTrue(kit.State.Revision > predicted);
                Assert.IsTrue(first.Session.HasEnded);
                Assert.AreEqual(ExecutionEndReason.Cancelled, first.Session.EndReason);
                Assert.IsTrue(first.Motion.IsDisposed);
                Assert.AreEqual(1, predictor.MotionReleased);
            }

            StepTo
            (
                network, host, client, 3,
                tick =>
                {
                    if (tick == 1)
                    {
                        Assert.IsTrue(serverKit.State.TryBeginCooldown(0.4f));
                    }
                }
            );
            Assert.IsFalse(first.Session.HasEnded);

            // 거절 결과가 도착한 Tick에 다음 입력이 이어질 수 있다.
            Step
            (
                network, host, client,
                afterClient: () =>
                {
                    VerifyRestored();
                    if (nextInputSameTick)
                    {
                        next = predictor.Predict(2);
                    }
                }
            );

            if (nextInputSameTick)
            {
                Assert.IsNotNull(next);
                StepTo(network, host, client, 9);
                Assert.AreEqual(1, predictor.Confirmed);
                Assert.AreEqual(1, host.Started.Count);
                Assert.AreEqual(next.Origin, host.Started[0].Origin);
                Assert.AreEqual(host.Started[0].ExecutionID, predictor.IdMap[next.TemporaryID]);
                Assert.IsTrue(next.Session.IsActive);
                Assert.AreEqual(0, counting.Refunds);
                Assert.AreEqual(1, predictor.Restores);
            }
            else
            {
                StepTo(network, host, client, 9);
                Assert.AreEqual(0, host.Started.Count);
                Assert.AreEqual(3, serverKit.State.Charge.Current);
            }
        }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/> 전이 단계가 AbilityState 밖의 게임 상태를 바꾸면 State 복제 복원으로 되돌아가지
        /// <br/> 않고 Host의 별도 복원이 필요하다.
        /// </summary>
        // --------------------------------------------------------------------------------
        [Test]
        public void TEST_AbilityNetworkPrediction_전이단계의외부상태는State복제복원으로되돌아가지않음()
        {
            var network = new FakeAbilityNetwork<AuthorityPacket>(0.1);
            using var host = new AuthorityHost(network);
            var serverKit = host.AddOwner(1, "Skill", 0, 3, connection: 10);
            var game = new GameState();
            using var kit = new AuthorityKit("Skill", 0, 3, transition: () => game.Stance = "Combat");
            host.ReplyDelay = 2;
            var client = new AuthorityClient(network, kit, 10, 1);
            var predictor = new PredictionClient(network, client, captureExternal: () => game.Stance);

            var use = predictor.Predict(2);
            Assert.IsNotNull(use);
            Assert.AreEqual("Idle", use.External);
            Assert.AreEqual("Combat", game.Stance);
            StepTo
            (
                network, host, client, 4,
                tick =>
                {
                    if (tick == 1)
                    {
                        Assert.IsTrue(serverKit.State.TryBeginCooldown(5f));
                    }
                }
            );

            // AbilityState는 복원되지만 전이가 바꾼 게임 상태는 그대로이다.
            Assert.AreEqual(1, predictor.Restores);
            Assert.AreEqual(3, kit.State.Charge.Current);
            Assert.IsTrue(kit.State.IsCooldownReady);
            Assert.AreEqual("Combat", game.Stance);

            // 되돌림은 예측 거절 기록을 받은 Host가 예측 직전에 보관한 값으로 직접 한다.
            Assert.Contains("reject:1", predictor.Log);
            Assert.AreEqual(1, predictor.RejectedUses.Count);
            foreach (var rejected in predictor.RejectedUses)
            {
                game.Stance = (string)rejected.External;
            }

            Assert.AreEqual("Idle", game.Stance);
        }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/> 예측 뒤 서버 Snapshot이 먼저 적용되고 거절이 늦게 도착하면 보관 복사본으로
        /// <br/> 복원하지 않고 서버의 Cooldown과 Charge를 유지한다.
        /// </summary>
        // --------------------------------------------------------------------------------
        [Test]
        public void TEST_AbilityNetworkPrediction_거절보다먼저적용된권한Snapshot은복원으로덮지않음()
        {
            var network = new FakeAbilityNetwork<AuthorityPacket>(0.1);
            using var host = new AuthorityHost(network);
            var serverKit = host.AddOwner(1, "Skill", 0, 3, connection: 10);
            using var kit = new AuthorityKit("Skill", 0, 3);
            host.ReplyDelay = 2;
            var observer = host.Replication.AddClientObserver(10);
            host.Replication.Enter(observer, 1, send: false);
            var client = new AuthorityClient(network, kit, 10, 1);
            var predictor = new PredictionClient(network, client);

            var use = predictor.Predict(2);
            Assert.IsNotNull(use);
            StepTo
            (
                network, host, client, 4,
                tick =>
                {
                    // 요청이 서버에 닿기 전에 서버 상태가 바뀌어 요청은 거절되고 Snapshot이 먼저 도착한다.
                    if (tick == 1)
                    {
                        Assert.IsTrue(serverKit.State.TryBeginCooldown(5f));
                        Assert.IsTrue(serverKit.State.TryConsumeCharge(1));
                    }
                }
            );

            // 서버 변경 Snapshot과, 거절 뒤 서버가 다시 보낸 같은 Revision의 정정 Snapshot을 적용한다.
            Assert.AreEqual(2, client.SnapshotsApplied);
            Assert.AreEqual(1, predictor.Rejected);
            Assert.AreEqual(0, predictor.Restores);
            Assert.AreEqual(1, predictor.Corrections);
            Assert.AreEqual(ExecutionEndReason.Cancelled, use.Session.EndReason);
            Assert.IsFalse(kit.State.IsCooldownReady);
            Assert.AreEqual(Remaining(serverKit.State), Remaining(kit.State), 1e-4f);
            Assert.AreEqual(2, serverKit.State.Charge.Current);
            Assert.AreEqual(serverKit.State.Charge.Current, kit.State.Charge.Current);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> 같은 Tick 안에서 예측을 시작한 뒤 권한 Snapshot이 적용되면
        /// <br/> 이어지는 거절이 보관 복사본으로 최신 권한 상태를 덮지 않는다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_AbilityNetworkPrediction_같은Tick에예측뒤적용된권한Snapshot은복원으로덮지않음()
        {
            var network = new FakeAbilityNetwork<AuthorityPacket>(0.1);
            using var host = new AuthorityHost(network);
            var serverKit = host.AddOwner(1, "Skill", 0, 3, connection: 10);
            using var kit = new AuthorityKit("Skill", 0, 3);
            host.ReplyDelay = 1;
            var observer = host.Replication.AddClientObserver(10);
            host.Replication.Enter(observer, 1, send: false);
            var client = new AuthorityClient(network, kit, 10, 1);
            var predictor = new PredictionClient(network, client);
            PredictedUse use = null;

            Step
            (
                network, host, client,
                serverEvent: () =>
                {
                    Assert.IsTrue(serverKit.State.TryBeginCooldown(5f));
                    Assert.IsTrue(serverKit.State.TryConsumeCharge(1));
                }
            );

            // Snapshot이 도착한 Tick에, 적용되기 전에 예측을 시작한다.
            Step(network, host, client, serverEvent: () => use = predictor.Predict(0));
            Assert.IsNotNull(use);
            Assert.AreEqual(1, client.SnapshotsApplied);
            StepTo(network, host, client, 4);

            Assert.AreEqual(1, predictor.Rejected);
            Assert.AreEqual(0, predictor.Restores);
            Assert.AreEqual(1, predictor.Corrections);
            Assert.IsFalse(kit.State.IsCooldownReady);
            Assert.AreEqual(Remaining(serverKit.State), Remaining(kit.State), 1e-4f);
            Assert.AreEqual(2, serverKit.State.Charge.Current);
            Assert.AreEqual(serverKit.State.Charge.Current, kit.State.Charge.Current);
        }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/> 예측한 Ability와 다른 Ability의 권한 Snapshot이 먼저 도착해도,
        /// <br/> 그 Ability를 바꾸지 않는 거절이면 예측한 Ability를 보관 복사본으로 복원하는지
        /// <br/> 검증한다. 서버의 그 Ability는 바뀌지 않으므로 복원하지 않으면 정정 Snapshot이
        /// <br/> 다시 올 때까지 어긋난다.
        /// </summary>
        // --------------------------------------------------------------------------------
        [Test]
        public void TEST_AbilityNetworkPrediction_다른Ability의Snapshot은예측한Ability의복원을막지않음()
        {
            var network = new FakeAbilityNetwork<AuthorityPacket>(0.1);
            using var host = new AuthorityHost(network);
            var denyCosts = new[]
            {
                new DenyCost(),
            };
            var serverKit = host.AddOwner(1, "Skill", 0, 3, connection: 10, extraCosts: denyCosts);
            var serverUltimate = serverKit.AddAbility("Ultimate");
            using var kit = new AuthorityKit("Skill", 0, 3);
            var clientUltimate = kit.AddAbility("Ultimate");
            host.ReplyDelay = 1;
            var observer = host.Replication.AddClientObserver(10);
            host.Replication.Enter(observer, 1, send: false);
            var client = new AuthorityClient(network, kit, 10, 1);
            var predictor = new PredictionClient(network, client);

            var use = predictor.Predict(2);
            Assert.IsNotNull(use);
            Assert.AreEqual(2, kit.State.Charge.Current);

            // 거절(Tick 3)보다 먼저(Tick 2) 다른 Ability의 Snapshot이 적용된다.
            StepTo
            (
                network, host, client, 4,
                tick =>
                {
                    if (tick == 1)
                    {
                        serverUltimate.Values.SetI("Stack", 1);
                    }
                }
            );

            Assert.AreEqual(1, clientUltimate.Values.GetI("Stack"));
            Assert.AreEqual(1, predictor.Rejected);
            Assert.AreEqual(1, predictor.Restores);
            Assert.AreEqual(0, predictor.Corrections);
            Assert.AreEqual(serverKit.State.Charge.Current, kit.State.Charge.Current);
            Assert.AreEqual(3, kit.State.Charge.Current);
        }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/> 서버 State를 바꾸지 않은 거절과 Stale 뒤에도 요청한 연결이 이미 적용한 것과
        /// <br/> 같은 Revision의 권한 Snapshot을 다시 받아, 복원하지 않은 예측 소비가 권한 값으로
        /// <br/> 정정되는지 검증한다.
        /// </summary>
        // --------------------------------------------------------------------------------
        [Test]
        public void TEST_AbilityNetworkPrediction_실행되지않은요청뒤에도권한Snapshot을다시받아정정()
        {
            var network = new FakeAbilityNetwork<AuthorityPacket>(0.1);
            using var host = new AuthorityHost(network);
            var denyCosts = new[]
            {
                new DenyCost(),
            };
            var serverKit = host.AddOwner(1, "Skill", 0, 3, connection: 10, extraCosts: denyCosts);
            using var kit = new AuthorityKit("Skill", 0, 3);
            host.ReplyDelay = 1;
            var observer = host.Replication.AddClientObserver(10);
            host.Replication.Enter(observer, 1);
            var client = new AuthorityClient(network, kit, 10, 1);
            var predictor = new PredictionClient(network, client);

            // Client가 현재 Revision을 이미 적용해 두어 정정 Snapshot은 같은 Revision으로 온다.
            StepTo(network, host, client, 2);
            Assert.AreEqual(1, client.SnapshotsApplied);

            // 뒤 예측이 먼저 도착해 거절되고(가장 오래되지 않아 복원 안 함) 앞 예측은 Stale이 된다.
            var first = predictor.Predict(2);
            var second = predictor.Predict(1);
            Assert.IsNotNull(first);
            Assert.IsNotNull(second);
            Assert.AreEqual(1, kit.State.Charge.Current);
            StepTo(network, host, client, 7);

            Assert.AreEqual(1, predictor.Rejected);
            Assert.AreEqual(1, predictor.Superseded);
            Assert.AreEqual(0, predictor.Restores);
            Assert.AreEqual(0, host.Started.Count);
            Assert.AreEqual(3, serverKit.State.Charge.Current);
            Assert.AreEqual(serverKit.State.Charge.Current, kit.State.Charge.Current);
        }

    #endregion

    #region D-2: 예측 승인

        // --------------------------------------------------------------------------------
        /// <summary>
        /// 승인되면 임시 실행 ID를 권한 ID에 대응하고 예측 Begin은 한 번이며 중복 승인은 무시한다.
        /// </summary>
        // --------------------------------------------------------------------------------
        [Test]
        public void TEST_AbilityNetworkPrediction_예측승인은임시ID를권한ID에대응하고중복승인은무시()
        {
            var network = new FakeAbilityNetwork<AuthorityPacket>(0.1);
            using var host = new AuthorityHost(network);
            var serverKit = host.AddOwner(1, "Skill", 0, 3, connection: 10);
            using var kit = new AuthorityKit("Skill", 0, 3);
            host.ReplyDelay = 2;
            host.ResultCopies = 2;
            var client = new AuthorityClient(network, kit, 10, 1);
            var predictor = new PredictionClient(network, client);

            var use = predictor.Predict(2);
            Assert.AreEqual(9000UL, use.TemporaryID);
            StepTo(network, host, client, 5);

            Assert.AreEqual(2, client.Results.Count);
            Assert.AreEqual(1, predictor.Confirmed);
            Assert.AreEqual(1, predictor.Ignored);
            Assert.AreEqual(1, predictor.Begins);
            Assert.AreEqual(1, host.Started.Count);
            Assert.AreEqual(1, predictor.IdMap.Count);
            Assert.AreEqual(host.Started[0].ExecutionID, predictor.IdMap[use.TemporaryID]);
            Assert.AreNotEqual(use.TemporaryID, predictor.IdMap[use.TemporaryID]);
            Assert.AreEqual(host.Started[0].Origin, use.Origin);
            Assert.AreEqual(serverKit.State.Charge.Current, kit.State.Charge.Current);
            Assert.IsTrue(use.Session.IsActive);
            Assert.IsFalse(use.Motion.IsDisposed);
            Assert.AreEqual(0, predictor.Restores);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> 이전 Epoch의 늦은 승인은 같은 Request ID를 쓰는
        /// <br/> 새 Epoch의 예측을 확정하지 않는다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_AbilityNetworkPrediction_Epoch가달라진같은RequestID의늦은승인은새예측을확정하지않음()
        {
            var network = new FakeAbilityNetwork<AuthorityPacket>(0.1);
            using var host = new AuthorityHost(network);
            host.AddOwner(1, "Skill", 0, 3, connection: 10);
            using var kit = new AuthorityKit("Skill", 0, 3);
            host.ReplyDelay = 3;
            var client = new AuthorityClient(network, kit, 10, 1);
            var predictor = new PredictionClient(network, client);

            var previous = predictor.Predict(0);
            Step(network, host, client);
            Assert.AreEqual(1, host.Started.Count);

            // 승인 응답이 전송 중인 채로 새 Epoch로 전환하면 이전 예측은 정리된다.
            network.Server.ChangeEpoch(1);
            client.JoinEpoch(1);
            Assert.AreEqual(1, predictor.Abandoned);
            Assert.IsFalse(previous.Session.IsActive);

            var current = predictor.Predict(5);
            Assert.AreEqual(previous.RequestID, current.RequestID);
            StepTo(network, host, client, 5);

            Assert.AreEqual(1, client.DroppedEpoch);
            Assert.AreEqual(0, predictor.Confirmed);
            Assert.IsTrue(current.Session.IsActive);

            StepTo(network, host, client, 10);

            Assert.AreEqual(1, predictor.Confirmed);
            Assert.AreEqual(2, host.Started.Count);
            Assert.AreEqual(host.Started[1].ExecutionID, predictor.IdMap[current.TemporaryID]);
        }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/> 새 세대로 합류하면 대기 중인 예측뿐 아니라 이미 승인된 예측 Session도 끝내고,
        /// <br/> 서버에서 계속되는 자기 실행은 합류 뒤 진행 중 관측으로 다시 받는지 검증한다.
        /// </summary>
        // --------------------------------------------------------------------------------
        [Test]
        public void TEST_AbilityNetworkPrediction_세대합류는승인된예측Session을끝내고계속되는자기실행은진행중으로받음()
        {
            var network = new FakeAbilityNetwork<AuthorityPacket>(0.1);
            using var host = new AuthorityHost(network);
            host.CreateSessions = true;
            host.AddOwner(1, "Skill", 0, 3, connection: 10);
            using var kit = new AuthorityKit("Skill", 0, 3);
            host.ReplyDelay = 1;
            var observer = host.Replication.AddClientObserver(10);
            host.Replication.Enter(observer, 1, send: false);
            var client = new AuthorityClient(network, kit, 10, 1);
            var predictor = new PredictionClient(network, client);

            var accepted = predictor.Predict(0);
            StepTo(network, host, client, 2);
            Assert.AreEqual(1, predictor.Confirmed);
            Assert.IsTrue(accepted.Session.IsActive);
            Assert.IsTrue(client.OwnRunning.Contains(host.Started[0].ExecutionID));

            var pending = predictor.Predict(5);
            network.Server.ChangeEpoch(1);
            client.JoinEpoch(1);

            Assert.AreEqual(ExecutionEndReason.Cancelled, accepted.Session.EndReason);
            Assert.IsTrue(accepted.Motion.IsDisposed);
            Assert.AreEqual(ExecutionEndReason.Cancelled, pending.Session.EndReason);
            Assert.AreEqual(1, predictor.Abandoned);
            Assert.AreEqual(0, client.OwnRunning.Count);

            // 서버 실행은 세대 전환과 무관하게 계속되고 합류한 연결에 진행 중으로 다시 알린다.
            StepTo(network, host, client, 4);
            Assert.IsTrue(host.Sessions[0].IsActive);
            Assert.IsTrue(client.OwnRunning.Contains(host.Started[0].ExecutionID));
        }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/> 권한 측에서 먼저 끝난 자기 실행은 승인된 예측 Session을 같은 종료 사유로 끝내고
        /// <br/> 자기 실행 기록을 지우는지 검증한다.
        /// </summary>
        // --------------------------------------------------------------------------------
        [Test]
        public void TEST_AbilityNetworkPrediction_권한에서끝난자기실행은승인된예측Session을같은사유로끝냄()
        {
            var network = new FakeAbilityNetwork<AuthorityPacket>(0.1);
            using var host = new AuthorityHost(network);
            host.CreateSessions = true;
            host.AddOwner(1, "Skill", 0, 3, connection: 10);
            using var kit = new AuthorityKit("Skill", 0, 3);
            host.ReplyDelay = 1;
            var observer = host.Replication.AddClientObserver(10);
            host.Replication.Enter(observer, 1, send: false);
            var client = new AuthorityClient(network, kit, 10, 1);
            var predictor = new PredictionClient(network, client);

            var use = predictor.Predict(0);
            StepTo(network, host, client, 2);
            Assert.AreEqual(1, predictor.Confirmed);
            Assert.IsTrue(use.Session.IsActive);

            host.Sessions[0].End(ExecutionEndReason.Interrupted);
            StepTo(network, host, client, 4);

            Assert.AreEqual(ExecutionEndReason.Interrupted, use.Session.EndReason);
            Assert.IsTrue(use.Motion.IsDisposed);
            Assert.AreEqual(0, client.OwnRunning.Count);
            Assert.AreEqual(0, client.RemoteEnded.Count);
        }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/> 자기 실행이 겹칠 때 권한에서 먼저 끝난 실행의 승인된 예측 Session만 같은
        /// <br/> 사유로 끝나고, 다른 실행의 예측 Session과 자기 실행 기록은 남는지 검증한다.
        /// </summary>
        // --------------------------------------------------------------------------------
        [Test]
        public void TEST_AbilityNetworkPrediction_겹친자기실행은권한에서끝난실행의예측Session만끝냄()
        {
            var network = new FakeAbilityNetwork<AuthorityPacket>(0.1);
            using var host = new AuthorityHost(network);
            host.CreateSessions = true;
            host.AddOwner(1, "Skill", 0, 3, connection: 10);
            using var kit = new AuthorityKit("Skill", 0, 3);
            host.ReplyDelay = 1;
            var observer = host.Replication.AddClientObserver(10);
            host.Replication.Enter(observer, 1, send: false);
            var client = new AuthorityClient(network, kit, 10, 1);
            var predictor = new PredictionClient(network, client);

            var first = predictor.Predict(0);
            var second = predictor.Predict(0);
            StepTo(network, host, client, 2);
            Assert.AreEqual(2, predictor.Confirmed);
            Assert.AreEqual(2, client.OwnRunning.Count);

            host.Sessions[0].End(ExecutionEndReason.Interrupted);
            StepTo(network, host, client, 4);

            Assert.AreEqual(ExecutionEndReason.Interrupted, first.Session.EndReason);
            Assert.IsTrue(second.Session.IsActive);
            Assert.AreEqual(1, client.OwnRunning.Count);
            Assert.IsTrue(client.OwnRunning.Contains(host.Started[1].ExecutionID));
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> 관측 채널이 빨라 권한 종료가 승인 결과보다 먼저 도착해도 승인할 때 예측
        /// <br/> Session을 같은 종료 사유로 끝내는지 검증한다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_AbilityNetworkPrediction_승인보다먼저온권한종료도승인할때예측Session을끝냄()
        {
            var network = new FakeAbilityNetwork<AuthorityPacket>(0.1);
            using var host = new AuthorityHost(network);
            host.CreateSessions = true;
            host.AddOwner(1, "Skill", 0, 3, connection: 10);
            using var kit = new AuthorityKit("Skill", 0, 3);
            host.ReplyDelay = 3;
            host.ObservationDelay = 0;
            var observer = host.Replication.AddClientObserver(10);
            host.Replication.Enter(observer, 1, send: false);
            var client = new AuthorityClient(network, kit, 10, 1);
            var predictor = new PredictionClient(network, client);

            var use = predictor.Predict(0);
            Step(network, host, client, afterClient: () => host.Sessions[0].End(ExecutionEndReason.Interrupted));
            Step(network, host, client);
            Assert.AreEqual(0, predictor.Confirmed);
            Assert.IsTrue(use.Session.IsActive);
            Assert.AreEqual(0, client.OwnRunning.Count);

            StepTo(network, host, client, 5);
            Assert.AreEqual(1, predictor.Confirmed);
            Assert.AreEqual(ExecutionEndReason.Interrupted, use.Session.EndReason);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> Duplicate 응답이 승인보다 먼저 도착해도 승인된 예측을
        /// <br/> 취소하지 않고 한 번만 확정한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_AbilityNetworkPrediction_Duplicate가승인보다먼저도착해도예측을취소하지않음()
        {
            var network = new FakeAbilityNetwork<AuthorityPacket>(0.1);
            using var host = new AuthorityHost(network);
            host.AddOwner(1, "Skill", 0, 3, connection: 10);
            using var kit = new AuthorityKit("Skill", 0, 3);
            host.ReplyDelay = 1;
            host.SpawnsPerUse = 1;
            var client = new AuthorityClient(network, kit, 10, 1);
            var predictor = new PredictionClient(network, client);

            var use = predictor.Predict(0, spawnCount: 1);
            client.Send(new AuthorityRequest(use.RequestID, 1, AuthorityKit.EntryName, network.Tick, 1));

            // 서버는 한 번만 실행하고 재전송에는 Duplicate를 반환한다.
            Step(network, host, client);
            Assert.AreEqual(1, host.Started.Count);

            // 응답 순서가 뒤집혀 Duplicate가 승인보다 먼저 도착한다.
            Step(network, host, client, reverse: true);

            Assert.AreEqual(2, client.Results.Count);
            Assert.AreEqual(AuthorityStatus.Duplicate, client.Results[0].Status);
            Assert.AreEqual(1, predictor.Confirmed);
            Assert.AreEqual(0, predictor.Rejected);
            Assert.AreEqual(1, predictor.Ignored);
            Assert.IsTrue(use.Session.IsActive);
            Assert.IsFalse(use.Projectiles[0].IsPredicted);
            Assert.AreEqual(host.Started[0].ExecutionID, predictor.IdMap[use.TemporaryID]);
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> 요청 순서가 뒤집혀 먼저 보낸 요청이 Stale로 거부돼도 그 예측만 취소하고
        /// <br/> 서버가 승인한 뒤 요청의 예측과 실행 ID 대응은 유지한다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_AbilityNetworkPrediction_순서역전으로Stale인요청은승인된뒤요청의예측을취소하지않음()
        {
            var network = new FakeAbilityNetwork<AuthorityPacket>(0.1);
            using var host = new AuthorityHost(network);
            var serverKit = host.AddOwner(1, "Skill", 0, 3, connection: 10);
            using var kit = new AuthorityKit("Skill", 0, 3);
            host.ReplyDelay = 2;
            var observer = host.Replication.AddClientObserver(10);
            host.Replication.Enter(observer, 1, send: false);
            var client = new AuthorityClient(network, kit, 10, 1);
            var predictor = new PredictionClient(network, client);

            var first = predictor.Predict(2);
            var second = predictor.Predict(2);
            Assert.IsNotNull(first);
            Assert.IsNotNull(second);

            // 요청은 2, 1 순서로 서버에 닿고 응답도 뒤집혀 Stale(1), Accepted(2) 순서로 도착한다.
            Step(network, host, client);
            Step(network, host, client, reverse: true);
            Assert.AreEqual(1, host.Started.Count);
            Step(network, host, client);
            Step(network, host, client, reverse: true);

            Assert.AreEqual(AuthorityStatus.Stale, client.Results[0].Status);
            Assert.AreEqual(AuthorityStatus.Accepted, client.Results[1].Status);
            Assert.AreEqual(1, predictor.Superseded);
            Assert.AreEqual(1, predictor.Confirmed);
            Assert.AreEqual(0, predictor.Rejected);
            Assert.AreEqual(0, predictor.Derived);
            Assert.AreEqual(0, predictor.Restores);
            Assert.AreEqual(0, predictor.Ignored);
            Assert.AreEqual(ExecutionEndReason.Cancelled, first.Session.EndReason);
            Assert.IsTrue(first.Motion.IsDisposed);
            Assert.IsTrue(second.Session.IsActive);
            Assert.IsFalse(second.Motion.IsDisposed);
            Assert.IsFalse(predictor.IdMap.ContainsKey(first.TemporaryID));
            Assert.AreEqual(host.Started[0].ExecutionID, predictor.IdMap[second.TemporaryID]);

            // 취소된 예측의 비용은 권한 Snapshot이 정정해 서버와 같아진다.
            Assert.AreEqual(2, serverKit.State.Charge.Current);
            Assert.AreEqual(serverKit.State.Charge.Current, kit.State.Charge.Current);
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> 서로 다른 Owner가 같은 Request ID를 동시에 보내도 각 클라이언트는
        /// <br/> 자기 요청의 결과로만 예측을 확정한다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_AbilityNetworkPrediction_Owner가다른같은RequestID는각자의결과로만확정()
        {
            var network = new FakeAbilityNetwork<AuthorityPacket>(0.1);
            using var host = new AuthorityHost(network);
            host.AddOwner(1, "Skill", 0, 3, connection: 10);
            host.AddOwner(2, "Skill", 0, 3, connection: 11);
            using var firstKit = new AuthorityKit("Skill", 0, 3);
            using var secondKit = new AuthorityKit("Skill", 0, 3);
            host.ReplyDelay = 2;
            var firstClient = new AuthorityClient(network, firstKit, 10, 1);
            var secondClient = new AuthorityClient(network, secondKit, 11, 2);
            var firstPredictor = new PredictionClient(network, firstClient);
            var secondPredictor = new PredictionClient(network, secondClient);

            var first = firstPredictor.Predict(2);
            var second = secondPredictor.Predict(2);
            Assert.AreEqual(first.RequestID, second.RequestID);
            while (network.Tick < 5)
            {
                var others = new[]
                {
                    secondClient,
                };
                Step(network, host, firstClient, others: others);
            }

            Assert.AreEqual(2, host.Started.Count);
            Assert.AreEqual(1, firstClient.Results.Count);
            Assert.AreEqual(1, secondClient.Results.Count);
            Assert.AreEqual(1, firstPredictor.Confirmed);
            Assert.AreEqual(1, secondPredictor.Confirmed);
            Assert.AreEqual(0, firstPredictor.Ignored);
            Assert.AreEqual(0, secondPredictor.Ignored);
            Assert.AreEqual(host.Started[0].ExecutionID, firstPredictor.IdMap[first.TemporaryID]);
            Assert.AreEqual(host.Started[1].ExecutionID, secondPredictor.IdMap[second.TemporaryID]);
        }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/> 승인 결과가 유실된 뒤 다음 요청이 처리되고 나서 도착한 재전송은
        /// <br/> 장부 창 안이면 원래 결과로 예측을 확정하고, 창 밖이면 Stale로 그 예측만 취소한다.
        /// <br/> 어느 경우든 서버는 다시 실행하지 않는다.
        /// </summary>
        // --------------------------------------------------------------------------------
        [TestCase(8, true)]
        [TestCase(1, false)]
        public void TEST_AbilityNetworkPrediction_유실된승인의늦은재전송은장부창안이면원래결과로확정(int window, bool confirmed)
        {
            var network = new FakeAbilityNetwork<AuthorityPacket>(0.1);
            using var host = new AuthorityHost(network);
            host.AddOwner(1, "Skill", 0, 3, connection: 10);
            using var kit = new AuthorityKit("Skill", 0, 3);
            host.ReplyDelay = 1;
            host.RequestWindow = window;
            host.DropResult = result => result.RequestID == 1 && result.Status == AuthorityStatus.Accepted;
            var client = new AuthorityClient(network, kit, 10, 1);
            var predictor = new PredictionClient(network, client);

            var first = predictor.Predict(0);
            var second = predictor.Predict(0);
            StepTo(network, host, client, 3);
            Assert.AreEqual(2, host.Started.Count);
            Assert.AreEqual(1, predictor.Confirmed);
            Assert.IsTrue(first.Session.IsActive);

            // 첫 요청의 결과를 받지 못한 Client가 재전송한다.
            client.Send(new AuthorityRequest(first.RequestID, 1, AuthorityKit.EntryName, network.Tick, 1));
            StepTo(network, host, client, 6);

            Assert.AreEqual(2, host.Started.Count);
            Assert.AreEqual(host.Started[1].ExecutionID, predictor.IdMap[second.TemporaryID]);
            if (confirmed)
            {
                Assert.AreEqual(AuthorityStatus.Duplicate, client.Results[client.Results.Count - 1].Status);
                Assert.AreEqual(2, predictor.Confirmed);
                Assert.IsTrue(first.Session.IsActive);
                Assert.AreEqual(host.Started[0].ExecutionID, predictor.IdMap[first.TemporaryID]);
            }
            else
            {
                Assert.AreEqual(AuthorityStatus.Stale, client.Results[client.Results.Count - 1].Status);
                Assert.AreEqual(1, predictor.Confirmed);
                Assert.AreEqual(1, predictor.Superseded);
                Assert.AreEqual(ExecutionEndReason.Cancelled, first.Session.EndReason);
                Assert.IsFalse(predictor.IdMap.ContainsKey(first.TemporaryID));
            }
        }

    #endregion

    #region O-1: Owner 제거

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> Owner 제거 통지를 받으면 대기 중인 예측을 취소하고 늦게 도착한 승인을
        /// <br/> 확정하지 않으며 새 예측과 사용을 막는다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_AbilityNetworkPrediction_Owner제거뒤늦게온승인은확정하지않고예측을정리()
        {
            var network = new FakeAbilityNetwork<AuthorityPacket>(0.1);
            using var host = new AuthorityHost(network);
            host.AddOwner(1, "Skill", 0, 3, connection: 10);
            using var kit = new AuthorityKit("Skill", 0, 3);
            host.ReplyDelay = 2;
            host.ObservationDelay = 0;
            var client = new AuthorityClient(network, kit, 10, 1);
            var predictor = new PredictionClient(network, client);

            var use = predictor.Predict(1);
            Step(network, host, client);
            Assert.AreEqual(1, host.Started.Count);

            // 결과 채널보다 빠른 관측 채널로 제거 통지가 승인 응답보다 먼저 도착한다.
            host.Despawn(1);
            Step(network, host, client);
            Assert.IsTrue(client.Despawned);
            Step(network, host, client);

            Assert.AreEqual(1, client.Results.Count);
            Assert.AreEqual(0, predictor.Confirmed);
            Assert.AreEqual(1, predictor.Abandoned);
            Assert.AreEqual(0, predictor.IdMap.Count);
            Assert.AreEqual(ExecutionEndReason.Cancelled, use.Session.EndReason);
            Assert.IsTrue(use.Motion.IsDisposed);
            Assert.IsNull(predictor.Predict(0));
            Assert.AreEqual(AuthorityInput.LocallyRejected, client.TryUse(out _));
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> Owner를 제거하면 이미 승인된 예측 Session과 서버 Session을 종료하고
        /// <br/> Lease를 해제하며 권한 Key를 받은 투사체는 독립 수명으로 유지한다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_AbilityNetworkPrediction_Owner제거는승인된Session을종료하고투사체는유지()
        {
            var network = new FakeAbilityNetwork<AuthorityPacket>(0.1);
            using var host = new AuthorityHost(network);
            host.CreateSessions = true;
            host.SpawnsPerUse = 1;
            host.AddOwner(1, "Skill", 0, 3, connection: 10);
            using var kit = new AuthorityKit("Skill", 0, 3);
            var client = new AuthorityClient(network, kit, 10, 1);
            var predictor = new PredictionClient(network, client);

            var use = predictor.Predict(0, spawnCount: 1);
            for (var i = 0; i < 3; i++)
            {
                Step(network, host, client);
            }

            Assert.AreEqual(1, predictor.Confirmed);
            Assert.IsTrue(use.Session.IsActive);
            Assert.IsTrue(host.Sessions[0].IsActive);

            host.Despawn(1);
            Step(network, host, client);
            Step(network, host, client);

            Assert.IsTrue(client.Despawned);
            Assert.AreEqual(ExecutionEndReason.Cancelled, use.Session.EndReason);
            Assert.IsTrue(use.Motion.IsDisposed);
            Assert.AreEqual(1, predictor.MotionReleased);
            Assert.IsFalse(host.Sessions[0].IsActive);
            Assert.AreEqual(1, predictor.Projectiles.Count);
            Assert.IsNull(use.Projectiles[0].DespawnReason);
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> 승인된 Session의 종료 콜백이 예외를 던져도 나머지 승인된 Session을
        /// <br/> 종료하고 정리한 뒤 예외를 전달한다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_AbilityNetworkPrediction_승인Session종료예외에도나머지Session을종료()
        {
            var network = new FakeAbilityNetwork<AuthorityPacket>(0.1);
            using var host = new AuthorityHost(network);
            host.AddOwner(1, "Skill", 0, 3, connection: 10);
            using var kit = new AuthorityKit("Skill", 0, 3);
            var client = new AuthorityClient(network, kit, 10, 1);
            var predictor = new PredictionClient(network, client);

            var first = predictor.Predict(0);
            var second = predictor.Predict(0);
            for (var i = 0; i < 4; i++)
            {
                Step(network, host, client);
            }

            Assert.AreEqual(2, predictor.Confirmed);
            first.Session.OnEnded += (_, _) => throw new InvalidOperationException("종료 콜백 실패");

            host.Despawn(1);
            Assert.Throws<InvalidOperationException>(() => Step(network, host, client));

            Assert.IsTrue(client.Despawned);
            Assert.IsFalse(first.Session.IsActive);
            Assert.IsFalse(second.Session.IsActive);
            Assert.IsTrue(second.Motion.IsDisposed);
        }

    #endregion

    #region C-1: 보정

        // --------------------------------------------------------------------------------
        /// <summary>
        /// 예측 Charge가 서버와 다르면 서버 State 복사본으로 보정하고 승인된 예측 Session은 유지한다.
        /// </summary>
        // --------------------------------------------------------------------------------
        [Test]
        public void TEST_AbilityNetworkPrediction_예측Charge가서버와다르면복사본으로보정하고승인된Session은유지()
        {
            var network = new FakeAbilityNetwork<AuthorityPacket>(0.1);
            using var host = new AuthorityHost(network);
            var serverKit = host.AddOwner(1, "Skill", 0, 3, connection: 10);
            using var kit = new AuthorityKit("Skill", 0, 3);
            host.ReplyDelay = 2;
            var observer = host.Replication.AddClientObserver(10);
            host.Replication.Enter(observer, 1, send: false);
            var client = new AuthorityClient(network, kit, 10, 1);
            var predictor = new PredictionClient(network, client);

            var use = predictor.Predict(2);
            var predictedCharge = kit.State.Charge.Current;
            StepTo
            (
                network, host, client, 8,
                tick =>
                {
                    // 다른 경로의 서버 소비로 서버 값이 클라이언트 예측과 어긋난다.
                    if (tick == 1)
                    {
                        Assert.IsTrue(serverKit.State.TryConsumeCharge(1));
                    }
                }
            );

            var serverCharge = serverKit.State.Charge.Current;
            Assert.AreEqual(2, predictedCharge);
            Assert.AreEqual(1, serverCharge);
            Assert.AreEqual(serverCharge, kit.State.Charge.Current);

            // 정책: 승인된 예측 Session은 값 불일치만으로 취소하지 않고 State만 서버 값으로 보정한다.
            Assert.AreEqual(1, predictor.Confirmed);
            Assert.AreEqual(0, predictor.Restores);
            Assert.IsTrue(use.Session.IsActive);
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> 보관 복사본이 부족하면 복원하지 않고 서버 State 복사본으로 보정하며 뒤의
        /// <br/> 예측은 함께 취소한다. 서버 Snapshot이 거절보다 늦게 도착하게 해
        /// <br/> 보관 복사본 개수만이 복원 여부를 가르도록 한다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_AbilityNetworkPrediction_History가부족하면복원하지않고권한복사본으로보정()
        {
            var network = new FakeAbilityNetwork<AuthorityPacket>(0.1);
            using var host = new AuthorityHost(network);
            var serverKit = host.AddOwner(1, "Skill", 0, 3, connection: 10);
            using var kit = new AuthorityKit("Skill", 0, 3);
            host.ReplyDelay = 2;
            var observer = host.Replication.AddClientObserver(10);

            // 거절(Tick 4)보다 Snapshot이 늦게(Tick 5) 도착하도록 전송 지연을 늘린다.
            observer.Deliver = (_, packet, _) =>
                network.Send(network.Server, network.Connect(10), packet, 4);
            host.Replication.Enter(observer, 1, send: false);
            var client = new AuthorityClient(network, kit, 10, 1);
            var predictor = new PredictionClient(network, client, historyCapacity: 1);

            var first = predictor.Predict(2);
            var second = predictor.Predict(2);
            Assert.AreEqual(1, kit.State.Charge.Current);
            StepTo
            (
                network, host, client, 4,
                tick =>
                {
                    if (tick == 1)
                    {
                        Assert.IsTrue(serverKit.State.TryBeginCooldown(5f));
                    }
                }
            );

            // 거절은 도착했고 Snapshot은 아직 적용 전이어야 보관 복사본 개수만이 복원 여부를 가른다.
            Assert.AreEqual(1, predictor.Rejected);
            Assert.AreEqual(0, client.SnapshotsApplied);
            StepTo(network, host, client, 6);

            Assert.AreEqual(1, predictor.Rejected);
            Assert.AreEqual(0, predictor.Restores);
            Assert.AreEqual(1, predictor.Corrections);
            Assert.AreEqual(1, predictor.Derived);
            Assert.AreEqual(1, predictor.Ignored);
            Assert.AreEqual(ExecutionEndReason.Cancelled, first.Session.EndReason);
            Assert.AreEqual(ExecutionEndReason.Cancelled, second.Session.EndReason);
            Assert.AreEqual(2, predictor.MotionReleased);

            // 거절 시점에는 Snapshot이 적용되기 전이므로 복원하지 않은 것은 보관 복사본 부족 때문이다.
            // 늦게 온 서버 변경 Snapshot과 거절 뒤의 정정 Snapshot을 모두 적용한다.
            Assert.AreEqual(2, client.SnapshotsApplied);
            Assert.IsFalse(kit.State.IsCooldownReady);
            Assert.AreEqual(Remaining(serverKit.State), Remaining(kit.State), 1e-4f);
            Assert.AreEqual(3, kit.State.Charge.Current);
            Assert.AreEqual(0, host.Started.Count);
        }

    #endregion

    #region P-1: 예측 투사체 거절

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> 거절된 예측 투사체는 제거되고 거절 전의 예측 Hit은 연출로만 남으며 권한
        /// <br/> Registry에는 아무것도 없다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_AbilityNetworkPrediction_거절된예측투사체는제거되고예측Hit은연출로만남음()
        {
            var network = new FakeAbilityNetwork<AuthorityPacket>(0.1);
            using var host = new AuthorityHost(network);
            host.SpawnsPerUse = 2;
            var serverKit = host.AddOwner(1, "Skill", 0, 3, connection: 10);
            using var kit = new AuthorityKit("Skill", 0, 3);
            host.ReplyDelay = 2;
            var client = new AuthorityClient(network, kit, 10, 1);
            var predictor = new PredictionClient(network, client);
            var confirmed = predictor.ConfirmedHits;

            var use = predictor.Predict(2, spawnCount: 2);
            var first = use.Projectiles[0];
            var second = use.Projectiles[1];
            Assert.AreEqual(2, predictor.Projectiles.Count);
            Assert.AreEqual(0UL, first.Key);
            Assert.AreEqual(0UL, second.Key);

            StepTo
            (
                network, host, client, 3,
                tick =>
                {
                    if (tick == 1)
                    {
                        Assert.IsTrue(serverKit.State.TryBeginCooldown(5f));
                    }
                }
            );

            // 거절 전에 예측 투사체가 Hit한다. 확정 결과는 만들어지지 않는다.
            predictor.OnHit(first);
            Assert.AreEqual(1, first.PresentationHits);
            Assert.AreEqual(0, confirmed.Entries.Count);

            StepTo(network, host, client, 4);
            Assert.AreEqual(0, predictor.Projectiles.Count);
            Assert.AreEqual(2, predictor.Removed.Count);
            Assert.AreEqual("PredictionRejected", first.DespawnReason);
            Assert.AreEqual("PredictionRejected", second.DespawnReason);
            Assert.AreEqual(1, first.PresentationHits);
            Assert.AreEqual(0, confirmed.Entries.Count);
            Assert.AreEqual(0, host.Spawned.Count);
            Assert.AreEqual(0, host.Started.Count);
        }

    #endregion

    #region P-2: 예측 투사체 승인

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> 승인되면 (RequestID, SpawnIndex)로 1:1 인계하고 부분 승인이면
        /// <br/> 대응되지 않은 예측 투사체를 제거한다.
        /// </summary>
        // ----------------------------------------------------------------------
        [TestCase(false)]
        [TestCase(true)]
        public void TEST_AbilityNetworkPrediction_예측투사체는승인된SpawnIndex로인계되고나머지는제거(bool partial)
        {
            var network = new FakeAbilityNetwork<AuthorityPacket>(0.1);
            using var host = new AuthorityHost(network);
            host.SpawnsPerUse = 2;
            host.SpawnLimit = partial ? 1 : int.MaxValue;
            host.AddOwner(1, "Skill", 0, 3, connection: 10);
            using var kit = new AuthorityKit("Skill", 0, 3);
            host.ReplyDelay = 2;
            var client = new AuthorityClient(network, kit, 10, 1);
            var predictor = new PredictionClient(network, client);
            var confirmed = predictor.ConfirmedHits;

            var use = predictor.Predict(2, spawnCount: 2);
            var first = use.Projectiles[0];
            var second = use.Projectiles[1];
            StepTo(network, host, client, 1);

            // 승인 전의 예측 Hit은 연출로만 남는다.
            predictor.OnHit(first);
            Assert.AreEqual(0, confirmed.Entries.Count);
            StepTo(network, host, client, 5);

            var result = client.Results[0];
            Assert.AreEqual(AuthorityStatus.Accepted, result.Status);
            Assert.AreEqual(partial ? 1 : 2, result.Spawns.Count);
            Assert.AreEqual(partial ? 1 : 2, host.Spawned.Count);

            // D8: 같은 표현 객체를 권한 Key로 인계한다. Cue는 다시 재생하지 않고 새 표현도 만들지 않는다.
            Assert.AreEqual(host.Spawned[0].Key, first.Key);
            Assert.IsFalse(first.IsPredicted);
            Assert.AreEqual(1, first.PresentationHits);
            Assert.AreSame(first, predictor.AuthoritativeSpawnArrived(host.Spawned[0].Key, host.Spawned[0].RequestID, host.Spawned[0].SpawnIndex));
            Assert.AreEqual(1, first.CuePlays);
            if (partial)
            {
                Assert.AreEqual("Unmatched", second.DespawnReason);
                Assert.AreEqual(1, predictor.Projectiles.Count);
                Assert.IsTrue(predictor.Removed.Contains(second));

                // 제거된 예측 객체의 Hit은 연출에도 확정에도 반영되지 않는다.
                predictor.OnHit(second);
                Assert.AreEqual(0, second.PresentationHits);
                Assert.AreEqual(0, confirmed.Entries.Count);
            }
            else
            {
                Assert.AreEqual(host.Spawned[1].Key, second.Key);
                Assert.IsFalse(second.IsPredicted);
                Assert.AreSame(second, predictor.AuthoritativeSpawnArrived(host.Spawned[1].Key, host.Spawned[1].RequestID, host.Spawned[1].SpawnIndex));
                Assert.AreEqual(1, second.CuePlays);
                Assert.AreEqual(2, predictor.Projectiles.Count);
                Assert.IsNull(second.DespawnReason);
            }

            Assert.AreEqual(0, predictor.Created);
            for (var index = 0; index < host.Spawned.Count; index++)
            {
                Assert.AreEqual(1, host.Spawned[index].RequestID);
                Assert.AreEqual(index, host.Spawned[index].SpawnIndex);
            }

            // 인계된 권한 객체의 Hit만 확정 결과가 되고, 인계 전에 낸 예측 Hit은 확정 결과에 섞이지 않는다.
            Assert.AreEqual(0, confirmed.Entries.Count);
            predictor.OnHit(first);
            Assert.AreEqual(1, confirmed.Entries.Count);
            Assert.AreEqual(host.Started[0].Origin, confirmed.Entries[0].Origin);
            Assert.AreEqual(host.Started[0].ExecutionID, result.ExecutionID);
            Assert.AreEqual(1, first.PresentationHits);
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> 서버 투사체 통지가 승인 응답보다 먼저 도착해도 새 표현을 만들지 않고
        /// <br/> 예측 투사체를 인계해 Key당 표현을 하나로 유지한다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_AbilityNetworkPrediction_승인보다먼저도착한권한Spawn도예측투사체를인계()
        {
            var network = new FakeAbilityNetwork<AuthorityPacket>(0.1);
            using var host = new AuthorityHost(network);
            host.SpawnsPerUse = 1;
            host.AddOwner(1, "Skill", 0, 3, connection: 10);
            using var kit = new AuthorityKit("Skill", 0, 3);
            host.ReplyDelay = 2;
            var client = new AuthorityClient(network, kit, 10, 1);
            var predictor = new PredictionClient(network, client);
            var confirmed = predictor.ConfirmedHits;

            var use = predictor.Predict(2, spawnCount: 1);
            var projectile = use.Projectiles[0];
            StepTo(network, host, client, 2);

            // 서버는 승인했지만 승인 응답은 아직 도착하지 않았다.
            Assert.AreEqual(1, host.Spawned.Count);
            Assert.AreEqual(0, predictor.Confirmed);
            var spawn = host.Spawned[0];
            var arrived = predictor.AuthoritativeSpawnArrived(spawn.Key, spawn.RequestID, spawn.SpawnIndex);

            Assert.AreSame(projectile, arrived);
            Assert.AreEqual(0, predictor.Created);
            Assert.AreEqual(1, predictor.Projectiles.Count);
            Assert.AreEqual(spawn.Key, projectile.Key);
            Assert.IsFalse(projectile.IsPredicted);
            Assert.AreEqual(use.Origin, projectile.Origin);

            // 이어서 도착한 승인은 같은 표현을 다시 인계할 뿐 새 표현이나 Cue를 만들지 않는다.
            StepTo(network, host, client, 5);
            Assert.AreEqual(1, predictor.Confirmed);
            Assert.AreEqual(0, predictor.Created);
            Assert.AreEqual(1, predictor.Projectiles.Count);
            Assert.AreEqual(1, projectile.CuePlays);
            Assert.AreSame(projectile, predictor.AuthoritativeSpawnArrived(spawn.Key, spawn.RequestID, spawn.SpawnIndex));

            predictor.OnHit(projectile);
            Assert.AreEqual(1, confirmed.Entries.Count);
            Assert.AreEqual(host.Started[0].Origin, confirmed.Entries[0].Origin);
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> 승인 전에 권한 Key를 인계받은 투사체는 승인보다 먼저 온 Owner 제거 통지로
        /// <br/> 예측을 취소해도 독립 수명으로 남는지 검증한다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_AbilityNetworkPrediction_승인전에권한Key를받은투사체는예측취소에도유지()
        {
            var network = new FakeAbilityNetwork<AuthorityPacket>(0.1);
            using var host = new AuthorityHost(network);
            host.SpawnsPerUse = 1;
            host.AddOwner(1, "Skill", 0, 3, connection: 10);
            using var kit = new AuthorityKit("Skill", 0, 3);
            host.ReplyDelay = 2;
            host.ObservationDelay = 0;
            var client = new AuthorityClient(network, kit, 10, 1);
            var predictor = new PredictionClient(network, client);

            var use = predictor.Predict(2, spawnCount: 1);
            var projectile = use.Projectiles[0];
            StepTo(network, host, client, 2);
            var spawn = host.Spawned[0];
            Assert.AreSame(projectile, predictor.AuthoritativeSpawnArrived(spawn.Key, spawn.RequestID, spawn.SpawnIndex));

            // 관측 채널이 빨라 제거 통지가 승인 응답보다 먼저 도착한다.
            host.Despawn(1);
            Step(network, host, client);
            Assert.IsTrue(client.Despawned);
            Assert.AreEqual(0, predictor.Confirmed);
            Assert.AreEqual(1, predictor.Abandoned);
            Assert.AreEqual(ExecutionEndReason.Cancelled, use.Session.EndReason);

            Assert.IsTrue(predictor.Projectiles.Contains(projectile));
            Assert.IsNull(projectile.DespawnReason);
            Assert.AreEqual(spawn.Key, projectile.Key);
        }

    #endregion

    #region W-1: 전환 중 입력

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> 세션 전환 중에는 예측과 예상 연출도 입력을 거절해 State를 바꾸거나
        /// <br/> 요청을 보내지 않고 전환이 끝나면 다시 받으며, Client 하나에는
        /// <br/> 결과를 받는 계층이 하나만 연결되는지 검증한다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_AbilityNetworkPrediction_전환중에는예측과예상연출도입력을보내지않음()
        {
            var network = new FakeAbilityNetwork<AuthorityPacket>(0.1);
            using var host = new AuthorityHost(network);
            host.AddOwner(1, "Skill", 1f, 3, connection: 10);
            host.AddOwner(2, "Skill", 1f, 3, connection: 11);
            using var kit = new AuthorityKit("Skill", 1f, 3);
            using var otherKit = new AuthorityKit("Skill", 1f, 3);
            var client = new AuthorityClient(network, kit, 10, 1);
            var other = new AuthorityClient(network, otherKit, 11, 2);
            var predictor = new PredictionClient(network, client);
            var anticipation = new AnticipationClient(other);
            var revision = kit.State.Revision;
            var pending = network.PendingCount;

            // Client 하나에는 결과를 받는 계층 하나만 연결된다.
            Assert.Throws<InvalidOperationException>(() => new AnticipationClient(client));

            client.Switching = true;
            other.Switching = true;

            Assert.IsNull(predictor.Predict(0));
            Assert.AreEqual(0, predictor.Begins);
            Assert.AreEqual(revision, kit.State.Revision);
            Assert.AreEqual(3, kit.State.Charge.Current);
            Assert.IsNull(anticipation.Begin(0));
            Assert.AreEqual(pending, network.PendingCount);
            Assert.AreEqual(1, client.NextRequestID);
            Assert.AreEqual(1, other.NextRequestID);

            client.Switching = false;
            other.Switching = false;

            Assert.IsNotNull(predictor.Predict(0));
            Assert.IsNotNull(anticipation.Begin(0));
            Assert.AreEqual(pending + 2, network.PendingCount);
        }

    #endregion

    }
}
