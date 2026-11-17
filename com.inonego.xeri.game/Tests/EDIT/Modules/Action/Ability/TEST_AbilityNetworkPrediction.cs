/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : TEST_AbilityNetworkPrediction.cs
수정일 : 2026-11-17

# 설명
L2 상태 보정 범위를 in-process Fake Server/Client로 검증한다.
Late Join, 예상 연출, 예측 거절과 승인, 값 보정, 예측 투사체의 거절과 인계를 다룬다.
예측 계층은 테스트 코드이며 Core가 제공하는 Hook은 State의 Unity 직렬화 복원, Advance와 ExecutionContext.Mode뿐이다.
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

    #region 예측 계층

        // ============================================================
        /// <summary>
        /// 지불 횟수와 환급 횟수를 세고 지불 때 Host 동작을 실행하는 Cost이다.
        /// </summary>
        // ============================================================
        private sealed class CountingCost : IAbilityActivationCost<AuthorityDefinition>
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
            public AbilityActivationCheck Evaluate(in AbilityActivationContext<AuthorityDefinition> context) =>
                AbilityActivationCheck.Allow();

            // ------------------------------------------------------------
            /// <summary>
            /// 비용 영수증을 반환한다.
            /// </summary>
            // ------------------------------------------------------------
            public AbilityCostReceipt Pay(in AbilityActivationContext<AuthorityDefinition> context)
            {
                Paid++;
                onPay?.Invoke();
                return new AbilityCostReceipt(() => Refunds++);
            }
        }

        private sealed class GameState
        {
            internal string Stance = "Idle";
        }

        // ============================================================
        /// <summary>
        /// 예측 투사체이다. 권한 Key Registry와 분리된 표현 전용 객체이다.
        /// </summary>
        // ============================================================
        private sealed class PredictedProjectile
        {
            internal int RequestID;
            internal int SpawnIndex;
            internal ulong Key;
            internal bool IsPredicted = true;
            internal int PresentationHits;
            internal int CuePlays;
            internal AbilityUseOrigin Origin;
            internal string DespawnReason;
        }

        private sealed class PredictedUse
        {
            internal int RequestID;
            internal AbilityState Kept;
            internal long KeptTick;
            internal bool HasKept;
            internal ExecutionSession Session;
            internal Lease Motion;
            internal ulong TemporaryID;
            internal object External;
            internal AbilityUseOrigin Origin;
            internal readonly List<PredictedProjectile> Projectiles = new();
        }

        // ================================================================================
        /// <summary>
        /// <br/> 사용 직전 State 복사본을 보관하고 로컬에서 TryActivate를 예측한 뒤 서버 결과로
        /// <br/> 확정하거나 되돌린다. 되돌릴 때는 보관 뒤 흐른 Tick만큼 Advance로 보정한다.
        /// <br/> 보관한 복사본이 부족하면 복원하지 않고 권한 복사본에 맡긴다.
        /// </summary>
        // ================================================================================
        private sealed class PredictionClient
        {
            private readonly FakeAbilityNetwork<AuthorityPacket> network;
            private readonly AuthorityClient client;
            private readonly int historyCapacity;
            private readonly Func<object> captureExternal;
            private readonly Dictionary<int, PredictedUse> pending = new();
            private readonly List<int> order = new();
            private ulong nextTemporaryID = 9000;

            internal readonly Dictionary<ulong, ulong> IdMap = new();
            internal readonly List<PredictedProjectile> Projectiles = new();
            internal readonly List<PredictedProjectile> Removed = new();
            internal readonly List<string> Log = new();
            internal readonly List<PredictedUse> RejectedUses = new();
            internal readonly AbilityFixtureResults<AbilityUseOrigin> ConfirmedHits = new();
            internal int Restores;
            internal int Corrections;
            internal int Confirmed;
            internal int Rejected;
            internal int Derived;
            internal int Begins;
            internal int Ignored;
            internal int MotionReleased;
            internal int Created;

            internal PredictionClient
            (
                FakeAbilityNetwork<AuthorityPacket> network,
                AuthorityClient client,
                int historyCapacity = int.MaxValue,
                Func<object> captureExternal = null
            )
            {
                this.network = network;
                this.client = client;
                this.historyCapacity = historyCapacity;
                this.captureExternal = captureExternal;
                client.OnResult = Handle;
            }

            // --------------------------------------------------------------------------------
            /// <summary>
            /// <br/> 사용 직전 State 복사본을 보관하고 Predicted 모드 Session으로 로컬 사용을
            /// <br/> 예측해 요청을 보낸다.
            /// </summary>
            // --------------------------------------------------------------------------------
            internal PredictedUse Predict(int delayTicks, int spawnCount = 0)
            {
                var kept = new AbilityState(client.Kit.State.Key);
                UnityEngine.JsonUtility.FromJsonOverwrite(UnityEngine.JsonUtility.ToJson(client.Kit.State), kept);
                var keptTick = network.Tick;
                var external = captureExternal?.Invoke();
                PredictedUse use = null;
                bool Begin(in AbilityUse<string> started)
                {
                    var id = nextTemporaryID++;
                    var flow = new FixedTimelineExecutionFlowDefinition(10f);
                    var context = new ExecutionContext(client.Owner, client.Owner, ExecutionMode.Predicted);
                    var session = new ExecutionSession(id, context, flow.CreateRuntime(), maximumTime: flow.MaximumDuration);
                    var motion = new Lease(() => MotionReleased++);
                    session.OnEnded += (_, _) => motion.Release();
                    session.Begin();
                    Begins++;
                    use = new PredictedUse
                    {
                        Kept = kept, KeptTick = keptTick, HasKept = true, Session = session, Motion = motion,
                        TemporaryID = id, Origin = started.Origin, External = external,
                    };
                    return true;
                }

                if (!client.Kit.TryUse(AuthorityKit.EntryName, client.Owner, 1, Begin, out _, out _))
                {
                    return null;
                }

                var request = client.Create();
                use.RequestID = request.RequestID;
                for (var index = 0; index < spawnCount; index++)
                {
                    var projectile = new PredictedProjectile
                    {
                        RequestID = request.RequestID, SpawnIndex = index
                    };
                    projectile.CuePlays++;
                    use.Projectiles.Add(projectile);
                    Projectiles.Add(projectile);
                }

                pending.Add(use.RequestID, use);
                order.Add(use.RequestID);
                TrimHistory();
                client.Send(request, delayTicks);
                return use;
            }

            private void TrimHistory()
            {
                var kept = 0;
                for (var index = order.Count - 1; index >= 0; index--)
                {
                    var use = pending[order[index]];
                    if (!use.HasKept) continue;

                    kept++;
                    if (kept > historyCapacity)
                    {
                        use.HasKept = false;
                    }
                }
            }

            // ------------------------------------------------------------
            /// <summary>
            /// 서버 결과를 처리한다. 이미 처리한 요청의 결과는 무시한다.
            /// </summary>
            // ------------------------------------------------------------
            internal void Handle(AuthorityResult result)
            {
                if (!pending.TryGetValue(result.RequestID, out var use))
                {
                    Ignored++;
                    return;
                }

                var index = order.IndexOf(use.RequestID);
                pending.Remove(use.RequestID);
                order.RemoveAt(index);
                if (result.Status == AuthorityStatus.Accepted)
                {
                    Accept(use, result);
                    return;
                }

                Reject(use, index == 0);
                while (index < order.Count)
                {
                    // 거절된 예측 위에 쌓인 뒤의 예측도 같은 기반을 잃으므로 함께 취소한다.
                    var derived = pending[order[index]];
                    pending.Remove(order[index]);
                    order.RemoveAt(index);
                    Cancel(derived, "PredictionDerived");
                    Derived++;
                }
            }

            private void Accept(PredictedUse use, AuthorityResult result)
            {
                Confirmed++;
                use.HasKept = false;
                IdMap[use.TemporaryID] = result.ExecutionID;
                Log.Add("accept:" + use.RequestID);

                // D8: 예측 투사체의 표현을 권한 Key로 인계하고 대응되지 않은 것은 제거한다.
                var matched = new HashSet<PredictedProjectile>();
                foreach (var spawn in result.Spawns)
                {
                    var projectile = use.Projectiles.Find(item => item.SpawnIndex == spawn.SpawnIndex);
                    if (projectile == null) continue;

                    projectile.Key = spawn.Key;
                    projectile.Origin = use.Origin;
                    projectile.IsPredicted = false;
                    matched.Add(projectile);
                }

                foreach (var projectile in use.Projectiles)
                {
                    if (!matched.Contains(projectile))
                    {
                        Remove(projectile, "Unmatched");
                    }
                }
            }

            private void Reject(PredictedUse use, bool oldest)
            {
                Rejected++;
                Log.Add("reject:" + use.RequestID);
                RejectedUses.Add(use);
                Cancel(use, "PredictionRejected");
                if (use.HasKept && oldest)
                {
                    UnityEngine.JsonUtility.FromJsonOverwrite(UnityEngine.JsonUtility.ToJson(use.Kept), client.Kit.State);
                    client.Kit.State.Advance((network.Tick - use.KeptTick) * network.TickDelta);
                    Restores++;
                }
                else
                {
                    Corrections++;
                }
            }

            private void Cancel(PredictedUse use, string reason)
            {
                if (use.Session.IsActive)
                {
                    use.Session.End(ExecutionEndReason.Cancelled);
                }

                foreach (var projectile in use.Projectiles)
                {
                    Remove(projectile, reason);
                }
            }

            private void Remove(PredictedProjectile projectile, string reason)
            {
                if (!Projectiles.Remove(projectile)) return;

                projectile.DespawnReason = reason;
                Removed.Add(projectile);
            }

            // ----------------------------------------------------------------------
            /// <summary>
            /// <br/> Hit을 처리한다. 예측 객체의 Hit은 연출 값으로만 남기고, 확정 결과는 권한
            /// <br/> Key를 받은 객체의 Hit만 기록한다.
            /// </summary>
            // ----------------------------------------------------------------------
            internal void OnHit(PredictedProjectile projectile)
            {
                if (projectile.DespawnReason != null) return;

                if (projectile.IsPredicted)
                {
                    projectile.PresentationHits++;
                    return;
                }

                ConfirmedHits.Record(projectile.Origin, 9, 1);
            }

            // ----------------------------------------------------------------------
            /// <summary>
            /// 권한 Entity가 복제되어 왔을 때 인계된 표현을 반환하고, 없으면 새 표현을 만든다.
            /// </summary>
            // ----------------------------------------------------------------------
            internal PredictedProjectile AuthoritativeSpawnArrived(ulong key)
            {
                var existing = Projectiles.Find(item => item.Key == key);
                if (existing != null) return existing;

                var created = new PredictedProjectile
                {
                    Key = key, IsPredicted = false
                };
                created.CuePlays++;
                Projectiles.Add(created);
                Created++;
                return created;
            }
        }

        private sealed class Presentation
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
        private sealed class AnticipationClient
        {
            private readonly AuthorityClient client;
            private readonly Dictionary<int, Presentation> items = new();

            internal AnticipationClient(AuthorityClient client)
            {
                this.client = client;
                client.OnResult = Handle;
            }

            internal Presentation Begin(int delayTicks)
            {
                var request = client.Create();
                client.Send(request, delayTicks);
                var presentation = new Presentation
                {
                    RequestID = request.RequestID, Cues = 1, CooldownUi = 5
                };
                items.Add(request.RequestID, presentation);
                return presentation;
            }

            private void Handle(AuthorityResult result)
            {
                if (!items.TryGetValue(result.RequestID, out var presentation)) return;

                if (result.Status == AuthorityStatus.Accepted)
                {
                    // 확정 사건으로 교체하며 Cue는 다시 재생하지 않는다.
                    presentation.Confirmed = true;
                    return;
                }

                presentation.Cancelled = true;
                presentation.Cues = 0;
                presentation.CooldownUi = 0;
            }
        }

        private static void StepTo
        (
            FakeAbilityNetwork<AuthorityPacket> network, AuthorityHost host, AuthorityClient client,
            long tick, Action<long> serverEvent = null
        )
        {
            while (network.Tick < tick)
            {
                Step(network, host, client, serverEvent: () => serverEvent?.Invoke(network.Tick));
            }
        }

    #endregion

    #region J-1: Late Join (N03)

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/> 진행 중인 State에 새 Peer가 접속하면 State 복사본과 전송 지연 보정으로 같은
        /// <br/> 남은 시간을 얻고 실행은 관측 사건으로만 잇는다.
        /// </summary>
        // --------------------------------------------------------------------------------
        [Category("N03")]
        [TestCase(false)]
        [TestCase(true)]
        public void TEST_AbilityNetworkPrediction_LateJoin은복사본으로상태를잇고실행은관측사건으로만(bool despawnDuringJoin)
        {
            var network = new FakeAbilityNetwork<AuthorityPacket>(0.1);
            using var host = new AuthorityHost(network);
            host.CreateSessions = true;
            var serverKit = host.AddOwner(2, "Enemy", 5f, 3);
            var server = serverKit.State;
            Assert.AreEqual(AuthorityStatus.Accepted, host.Execute(2, 0, 0, AuthorityKit.EntryName, 0).Status);
            server.Values.SetI("stack", 4);
            Idle(network, host, 20);

            using var peerKit = new AuthorityKit("Enemy", 5f, 3);
            var peer = new AuthorityClient(network, peerKit, 11, 2);
            var peerRevision = peerKit.State.Revision;
            host.ReplyDelay = 3;
            var observer = host.AddClientObserver();
            host.Enter(observer, 2);

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
                // 접속 중 Despawn: 제거 알림이 복사본보다 먼저 도착하고 늦은 복사본은 적용되지 않는다.
                host.Despawn(2);
                StepTo(network, host, peer, 25);
                Assert.IsTrue(peer.Despawned);
                Assert.AreEqual(1, peer.DroppedSnapshots);
                Assert.AreEqual(peerRevision, peerKit.State.Revision);
                Assert.IsTrue(peerKit.State.IsCooldownReady);
                return;
            }

            // 복사본은 3 Tick 전의 서버 State이므로 전송 지연만큼 진행한 지금은 같은 남은 시간이다.
            StepTo(network, host, peer, 25);
            Assert.AreEqual(2.5f, Remaining(server), 1e-4f);
            Assert.AreEqual(Remaining(server), Remaining(peerKit.State), 1e-4f);
            Assert.AreEqual(server.Charge.Current, peerKit.State.Charge.Current);
            Assert.AreEqual(server.Charge.Recharge.RemainingTime, peerKit.State.Charge.Recharge.RemainingTime, 1e-4f);

            // 이후에도 같은 시간만큼 진행하면 같은 시점에 Cooldown이 끝난다.
            foreach (var elapsed in new[]
            {
                1f,
                1.5f,
                3f
            })
            {
                server.Advance(elapsed);
                peerKit.State.Advance(elapsed);
                Assert.AreEqual(Remaining(server), Remaining(peerKit.State), 1e-4f);
                Assert.AreEqual(server.IsCooldownReady, peerKit.State.IsCooldownReady);
            }

            Assert.AreEqual(4, peerKit.State.Values.GetI("stack"));
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
        [Category("N03")]
        [Test]
        public void TEST_AbilityNetworkPrediction_같은순서로등록한Peer는같은Ability로해석()
        {
            var entry = new AbilityEntryKey("Q");
            AbilityBindingLayer<AuthorityDefinition> Layer(int priority, string key)
            {
                var ability = new AbilityInstance<AuthorityDefinition>(new AuthorityDefinition(), new AbilityState(new AbilityKey(key)));
                var layer = new AbilityBindingLayer<AuthorityDefinition>(priority);
                layer.Set(entry, ability);
                return layer;
            }

            var serverBindings = new AbilityBindings<AuthorityDefinition>();
            using var serverBase = serverBindings.AcquireLayer(Layer(0, "Base"));
            using var serverForm = serverBindings.AcquireLayer(Layer(1, "Form"));
            var peerBindings = new AbilityBindings<AuthorityDefinition>();
            using var peerBase = peerBindings.AcquireLayer(Layer(0, "Base"));
            using var peerForm = peerBindings.AcquireLayer(Layer(1, "Form"));

            Assert.IsTrue(serverBindings.TryResolve(entry, out var serverResolved));
            Assert.IsTrue(peerBindings.TryResolve(entry, out var peerResolved));
            Assert.AreEqual(new AbilityKey("Form"), serverResolved.Instance.State.Key);
            Assert.AreEqual(serverResolved.Instance.State.Key, peerResolved.Instance.State.Key);
            Assert.AreEqual(serverBindings.Revision, peerBindings.Revision);

            // 같은 Priority는 두 Peer 모두 나중에 등록한 Layer가 우선한다.
            var entryX = new AbilityEntryKey("X");
            AbilityBindingLayer<AuthorityDefinition> SamePriority(string key)
            {
                var ability = new AbilityInstance<AuthorityDefinition>(new AuthorityDefinition(), new AbilityState(new AbilityKey(key)));
                var layer = new AbilityBindingLayer<AuthorityDefinition>(1);
                layer.Set(entryX, ability);
                return layer;
            }

            var serverSame = new AbilityBindings<AuthorityDefinition>();
            using var serverLow = serverSame.AcquireLayer(SamePriority("Low"));
            using var serverHigh = serverSame.AcquireLayer(SamePriority("High"));
            var peerSame = new AbilityBindings<AuthorityDefinition>();
            using var peerLow = peerSame.AcquireLayer(SamePriority("Low"));
            using var peerHigh = peerSame.AcquireLayer(SamePriority("High"));

            Assert.IsTrue(serverSame.TryResolve(entryX, out var serverX));
            Assert.IsTrue(peerSame.TryResolve(entryX, out var peerX));
            Assert.AreEqual(new AbilityKey("High"), serverX.Instance.State.Key);
            Assert.AreEqual(serverX.Instance.State.Key, peerX.Instance.State.Key);
        }

    #endregion

    #region A-1: Anticipation (N06)

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> 예상 연출은 권한과 클라이언트 State를 바꾸지 않고 서버 거절 시 취소되며
        /// <br/> 승인 시 Cue를 다시 재생하지 않는다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Category("N06")]
        [TestCase(false)]
        [TestCase(true)]
        public void TEST_AbilityNetworkPrediction_예상연출은State를바꾸지않고거절되면취소(bool accepted)
        {
            var network = new FakeAbilityNetwork<AuthorityPacket>(0.1);
            using var host = new AuthorityHost(network);
            using var kit = new AuthorityKit("Skill", 0, 3);
            var serverKit = host.AddOwner(1, "Skill", 0, 3, sender: 10);
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
                        Assert.IsTrue(serverKit.State.Control.TryBeginCooldown(5f));
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

    #region D-1: 예측 거절 (N07)

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> 거절된 예측은 보관한 State 복사본을 Unity 직렬화로 복원하고 Session과
        /// <br/> Lease를 정리하며 Receipt를 환급하지 않는다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Category("N07")]
        [TestCase(false)]
        [TestCase(true)]
        public void TEST_AbilityNetworkPrediction_예측거절은보관복사본으로한번만복원(bool nextInputSameTick)
        {
            var network = new FakeAbilityNetwork<AuthorityPacket>(0.1);
            using var host = new AuthorityHost(network);
            var serverKit = host.AddOwner(1, "Skill", 1f, 3, sender: 10);
            var counting = new CountingCost();
            var costs = new IAbilityActivationCost<AuthorityDefinition>[]
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
            Assert.Greater(predicted, baseline);

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
                Assert.Greater(kit.State.Revision, predicted);
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
                        Assert.IsTrue(serverKit.State.Control.TryBeginCooldown(0.4f));
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
        /// <br/> 전이 단계가 AbilityState 밖의 게임 상태를 바꾸면 Unity 직렬화 복원으로 되돌아가지
        /// <br/> 않고 Host의 별도 복원이 필요하다.
        /// </summary>
        // --------------------------------------------------------------------------------
        [Category("N07")]
        [Test]
        public void TEST_AbilityNetworkPrediction_전이단계의외부상태는직렬화복원으로되돌아가지않음()
        {
            var network = new FakeAbilityNetwork<AuthorityPacket>(0.1);
            using var host = new AuthorityHost(network);
            var serverKit = host.AddOwner(1, "Skill", 0, 3, sender: 10);
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
                        Assert.IsTrue(serverKit.State.Control.TryBeginCooldown(5f));
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

    #endregion

    #region D-2: 예측 승인 (N08)

        // --------------------------------------------------------------------------------
        /// <summary>
        /// 승인되면 임시 실행 ID를 권한 ID에 대응하고 예측 Begin은 한 번이며 중복 승인은 무시한다.
        /// </summary>
        // --------------------------------------------------------------------------------
        [Category("N08")]
        [Test]
        public void TEST_AbilityNetworkPrediction_예측승인은임시ID를권한ID에대응하고중복승인은무시()
        {
            var network = new FakeAbilityNetwork<AuthorityPacket>(0.1);
            using var host = new AuthorityHost(network);
            var serverKit = host.AddOwner(1, "Skill", 0, 3, sender: 10);
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

    #endregion

    #region C-1: 보정 (N10)

        // --------------------------------------------------------------------------------
        /// <summary>
        /// 예측 Charge가 서버와 다르면 서버 State 복사본으로 보정하고 승인된 예측 Session은 유지한다.
        /// </summary>
        // --------------------------------------------------------------------------------
        [Category("N10")]
        [Test]
        public void TEST_AbilityNetworkPrediction_예측Charge가서버와다르면복사본으로보정하고승인된Session은유지()
        {
            var network = new FakeAbilityNetwork<AuthorityPacket>(0.1);
            using var host = new AuthorityHost(network);
            var serverKit = host.AddOwner(1, "Skill", 0, 3, sender: 10);
            using var kit = new AuthorityKit("Skill", 0, 3);
            host.ReplyDelay = 2;
            var observer = host.AddClientObserver();
            host.Enter(observer, 1, send: false);
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
                        Assert.IsTrue(serverKit.State.Control.TryConsumeCharge(1));
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
        /// <br/> 예측은 함께 취소한다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Category("N10")]
        [Test]
        public void TEST_AbilityNetworkPrediction_History가부족하면복원하지않고권한복사본으로보정()
        {
            var network = new FakeAbilityNetwork<AuthorityPacket>(0.1);
            using var host = new AuthorityHost(network);
            var serverKit = host.AddOwner(1, "Skill", 0, 3, sender: 10);
            using var kit = new AuthorityKit("Skill", 0, 3);
            host.ReplyDelay = 2;
            var observer = host.AddClientObserver();
            host.Enter(observer, 1, send: false);
            var client = new AuthorityClient(network, kit, 10, 1);
            var predictor = new PredictionClient(network, client, historyCapacity: 1);

            var first = predictor.Predict(2);
            var second = predictor.Predict(2);
            Assert.AreEqual(1, kit.State.Charge.Current);
            StepTo
            (
                network, host, client, 6,
                tick =>
                {
                    if (tick == 1)
                    {
                        Assert.IsTrue(serverKit.State.Control.TryBeginCooldown(5f));
                    }
                }
            );

            Assert.AreEqual(1, predictor.Rejected);
            Assert.AreEqual(0, predictor.Restores);
            Assert.AreEqual(1, predictor.Corrections);
            Assert.AreEqual(1, predictor.Derived);
            Assert.AreEqual(1, predictor.Ignored);
            Assert.AreEqual(ExecutionEndReason.Cancelled, first.Session.EndReason);
            Assert.AreEqual(ExecutionEndReason.Cancelled, second.Session.EndReason);
            Assert.AreEqual(2, predictor.MotionReleased);

            // 오래된 보관 복사본을 적용했다면 서버 Cooldown이 사라졌을 것이다.
            Assert.IsFalse(kit.State.IsCooldownReady);
            Assert.AreEqual(Remaining(serverKit.State), Remaining(kit.State), 1e-4f);
            Assert.AreEqual(3, kit.State.Charge.Current);
            Assert.AreEqual(0, host.Started.Count);
        }

    #endregion

    #region P-1: 예측 투사체 거절 (N12)

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> 거절된 예측 투사체는 제거되고 거절 전의 예측 Hit은 연출로만 남으며 권한
        /// <br/> Registry에는 아무것도 없다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Category("N12")]
        [Test]
        public void TEST_AbilityNetworkPrediction_거절된예측투사체는제거되고예측Hit은연출로만남음()
        {
            var network = new FakeAbilityNetwork<AuthorityPacket>(0.1);
            using var host = new AuthorityHost(network);
            host.SpawnsPerUse = 2;
            var serverKit = host.AddOwner(1, "Skill", 0, 3, sender: 10);
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
                        Assert.IsTrue(serverKit.State.Control.TryBeginCooldown(5f));
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

    #region P-2: 예측 투사체 승인 (N14)

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> 승인되면 (RequestID, SpawnIndex)로 1:1 인계하고 부분 승인이면
        /// <br/> 대응되지 않은 예측 투사체를 제거한다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Category("N14")]
        [TestCase(false)]
        [TestCase(true)]
        public void TEST_AbilityNetworkPrediction_예측투사체는승인된SpawnIndex로인계되고나머지는제거(bool partial)
        {
            var network = new FakeAbilityNetwork<AuthorityPacket>(0.1);
            using var host = new AuthorityHost(network);
            host.SpawnsPerUse = 2;
            host.SpawnLimit = partial ? 1 : int.MaxValue;
            host.AddOwner(1, "Skill", 0, 3, sender: 10);
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
            Assert.AreSame(first, predictor.AuthoritativeSpawnArrived(host.Spawned[0].Key));
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
                Assert.AreSame(second, predictor.AuthoritativeSpawnArrived(host.Spawned[1].Key));
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

    #endregion

    }
}
