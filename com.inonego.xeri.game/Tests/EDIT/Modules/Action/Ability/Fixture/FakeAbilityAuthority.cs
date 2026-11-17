/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : FakeAbilityAuthority.cs
수정일 : 2026-11-17

# 설명
권한 사용 프로토콜(L1)의 테스트 전용 Fake Host, Client와 메시지를 제공한다.
요청 검증, 중복 제거, 서버 재선택, 변경분 복제, 전송 지연 보정과 AOI 필터는 모두 이 파일의 테스트 코드에 둔다.
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
    /// 권한 Host가 요청에 돌려주는 결과 분류이다.
    /// </summary>
    // ============================================================
    internal enum AuthorityStatus
    {
        Accepted,
        Rejected,
        Duplicate,
        Stale,
        StaleEpoch,
        UnknownOwner,
        BadRequest,
    }

    // ============================================================
    /// <summary>
    /// Client가 입력을 처리한 결과 분류이다.
    /// </summary>
    // ============================================================
    internal enum AuthorityInput
    {
        Sent,
        LocallyRejected,
        Switching,
    }

    internal sealed class AuthorityRequest
    {
        internal readonly int Sender;
        internal readonly int RequestID;
        internal readonly int Owner;
        internal readonly string Entry;
        internal readonly long ObservedTick;
        internal readonly object Payload;

        internal AuthorityRequest(int sender, int requestID, int owner, string entry, long observedTick, object payload)
        {
            Sender = sender;
            RequestID = requestID;
            Owner = owner;
            Entry = entry;
            ObservedTick = observedTick;
            Payload = payload;
        }
    }

    internal sealed class AuthorityResult
    {
        internal readonly int RequestID;
        internal readonly int RequestEpoch;
        internal readonly AuthorityStatus Status;
        internal readonly string Reason;
        internal readonly ulong ExecutionID;
        internal readonly long StartTick;
        internal readonly AuthorityResult Original;
        internal readonly IReadOnlyList<(int SpawnIndex, ulong Key)> Spawns;

        internal AuthorityResult
        (
            int requestID, int requestEpoch, AuthorityStatus status, string reason = "",
            ulong executionID = 0, long startTick = 0, AuthorityResult original = null,
            IReadOnlyList<(int SpawnIndex, ulong Key)> spawns = null
        )
        {
            RequestID = requestID;
            RequestEpoch = requestEpoch;
            Status = status;
            Reason = reason;
            ExecutionID = executionID;
            StartTick = startTick;
            Original = original;
            Spawns = spawns ?? Array.Empty<(int SpawnIndex, ulong Key)>();
        }
    }

    // ============================================================
    /// <summary>
    /// 서버 State의 복사본과 원본 Revision, 복사한 Tick을 보관한다.
    /// </summary>
    // ============================================================
    internal sealed class AuthoritySnapshot
    {
        internal readonly AbilityState State;
        internal readonly ulong Revision;
        internal readonly long Tick;

        internal AuthoritySnapshot(AbilityState source, long tick)
        {
            State = new AbilityState(source.Key);
            UnityEngine.JsonUtility.FromJsonOverwrite(UnityEngine.JsonUtility.ToJson(source), State);
            Revision = source.Revision;
            Tick = tick;
        }
    }

    internal sealed class AuthorityPacket
    {
        internal AuthorityRequest Request;
        internal AuthorityResult Result;
        internal int SnapshotOwner;
        internal bool HasSnapshot;
        internal bool Despawned;
        internal AuthoritySnapshot Snapshot;

        internal static AuthorityPacket Of(AuthorityRequest request) =>
            new AuthorityPacket
        {
            Request = request
        };

        internal static AuthorityPacket Of(AuthorityResult result) =>
            new AuthorityPacket
        {
            Result = result
        };

        internal static AuthorityPacket Of(int owner, AuthoritySnapshot snapshot) =>
            new AuthorityPacket
        {
            SnapshotOwner = owner, HasSnapshot = true, Snapshot = snapshot
        };

        internal static AuthorityPacket OfDespawn(int owner) =>
            new AuthorityPacket
        {
            SnapshotOwner = owner, Despawned = true
        };
    }

    internal sealed class AuthorityDefinition
    {
        // NONE
    }

    // ============================================================
    /// <summary>
    /// 한 Owner의 Ability State, Binding과 선택 규칙을 묶는다.
    /// </summary>
    // ============================================================
    internal sealed class AuthorityKit : IDisposable
    {
        internal const string EntryName = "Use";

        internal readonly AbilityInstance<AuthorityDefinition> Ability;
        internal readonly AbilityBindings<AuthorityDefinition> Bindings = new();
        private readonly AbilityUseSelector<AuthorityDefinition, int, string> selector;
        private readonly Lease lease;

        internal AbilityState State => Ability.State;

        internal AuthorityKit
        (
            string key, float cooldown, int charges,
            IEnumerable<IAbilityActivationCost<AuthorityDefinition>> extraCosts = null,
            Action transition = null
        )
        {
            Ability = new AbilityInstance<AuthorityDefinition>(new AuthorityDefinition(), new AbilityState(new AbilityKey(key)));
            Ability.State.Control.ConfigureCharge(charges, 10f);
            var layer = new AbilityBindingLayer<AuthorityDefinition>(0);
            layer.Set(new AbilityEntryKey(EntryName), Ability);
            lease = Bindings.AcquireLayer(layer);
            var costs = new List<IAbilityActivationCost<AuthorityDefinition>>
            {
                new AbilityChargeCost<AuthorityDefinition>(),
            };
            if (extraCosts != null)
            {
                costs.AddRange(extraCosts);
            }

            var policy = new AbilityActivationPolicy<AuthorityDefinition>
            (
                new IAbilityActivationRequirement<AuthorityDefinition>[]
                {
                    new AbilityCooldownRequirement<AuthorityDefinition>()
                },
                costs
            );

            // Cooldown은 Host가 시작에 성공한 뒤 전이에서 시작한다.
            void StartCooldown()
            {
                if (cooldown > 0f && !Ability.State.Control.TryBeginCooldown(cooldown))
                {
                    throw new InvalidOperationException("Cooldown 시작이 승인 후 실패했습니다.");
                }

                transition?.Invoke();
            }

            var choice = new AbilityUseChoice<AuthorityDefinition, string>(key, policy, transition: StartCooldown);
            selector = new AbilityUseSelector<AuthorityDefinition, int, string>
            (
                new Func<AbilityActivationContext<AuthorityDefinition>, int, AbilityUseChoice<AuthorityDefinition, string>>[]
                {
                    (_, _) => choice
                }
            );
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 리소스를 해제한다.
        /// </summary>
        // ------------------------------------------------------------
        public void Dispose()
        {
            lease.Release();
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 이 Kit의 State와 Binding으로 사용을 다시 선택한다.
        /// </summary>
        // ------------------------------------------------------------
        internal bool TrySelect
        (
            string entry, int owner, object payload,
            out AbilityUseSelection<AuthorityDefinition, string> selection, out string reason
        )
        {
            selection = null;
            if (!Bindings.TryResolve(new AbilityEntryKey(entry), out var binding))
            {
                reason = "Entry에 해당하는 Binding이 없습니다.";
                return false;
            }

            var context = new AbilityActivationContext<AuthorityDefinition>(owner, owner, binding, payload);
            return selector.TrySelect(Bindings, context, out selection, out reason);
        }

        internal bool IsReady(int owner)
        {
            return TrySelect(EntryName, owner, 0, out var selection, out _)
                && selection.Evaluate().Succeeded;
        }

        internal bool TryUse
        (
            string entry, int owner, object payload, AbilityBegin<string> begin,
            out AbilityActivationResult result, out AbilityUse<string> use
        )
        {
            use = default;
            if (!TrySelect(entry, owner, payload, out var selection, out var reason))
            {
                result = AbilityActivationResult.Reject(AbilityActivationFailure.InvalidState, reason: reason);
                return false;
            }

            return selection.TryActivate(begin, out result, out use);
        }
    }

    // ============================================================
    /// <summary>
    /// AOI 필터를 적용받는 복제 대상이다.
    /// </summary>
    // ============================================================
    internal sealed class AuthorityObserver
    {
        internal readonly HashSet<int> Visible = new();
        internal readonly Dictionary<int, ulong> LastSent = new();
        internal readonly List<(int Owner, AuthoritySnapshot Snapshot)> Snapshots = new();
        internal readonly List<(int Owner, AbilityUseOrigin Origin, ulong ExecutionID)> Started = new();
        internal readonly List<(int Owner, AbilityUseOrigin Origin, ulong ExecutionID, long StartTick)> InProgress = new();
        internal Action<int, AuthoritySnapshot> Deliver;

        internal AuthorityObserver()
        {
            Deliver = (owner, snapshot) => Snapshots.Add((owner, snapshot));
        }

        internal int Count(int owner)
        {
            var count = 0;
            foreach (var entry in Snapshots)
            {
                if (entry.Owner == owner)
                {
                    count++;
                }
            }

            return count;
        }
    }

    // ======================================================================
    /// <summary>
    /// 요청 검증, 중복 제거, 서버 재선택과 변경분 복제를 맡는 권한 Host이다.
    /// </summary>
    // ======================================================================
    internal sealed class AuthorityHost : IDisposable
    {
        private readonly FakeAbilityNetwork<AuthorityPacket> network;
        private readonly Dictionary<int, int> senderToOwner = new();
        private readonly Dictionary<int, AuthorityKit> kits = new();
        private readonly Dictionary<(int Owner, int Epoch), AuthorityResult> lastResults = new();
        private readonly Dictionary<int, (AbilityUseOrigin Origin, ulong ExecutionID, long Tick)> running = new();
        private readonly List<AuthorityObserver> observers = new();
        private ulong nextExecutionID = 100;
        private ulong nextSpawnKey = 500;
        private int processed;

        internal readonly List<(ulong ExecutionID, AbilityUseOrigin Origin, long Tick)> Started = new();
        internal readonly List<(ulong Key, AbilityUseOrigin Origin, int RequestID, int SpawnIndex)> Spawned = new();
        internal readonly List<ExecutionSession> Sessions = new();
        internal int ReplyDelay;
        internal int ResultCopies = 1;
        internal bool CreateSessions;
        internal int SpawnsPerUse;
        internal int SpawnLimit = int.MaxValue;

        internal AuthorityHost(FakeAbilityNetwork<AuthorityPacket> network)
        {
            this.network = network;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 리소스를 해제한다.
        /// </summary>
        // ------------------------------------------------------------
        public void Dispose()
        {
            foreach (var kit in kits.Values)
            {
                kit.Dispose();
            }
        }

        internal AuthorityKit AddOwner
        (
            int owner, string key, float cooldown, int charges, int sender = -1,
            IEnumerable<IAbilityActivationCost<AuthorityDefinition>> extraCosts = null,
            Action transition = null
        )
        {
            var kit = new AuthorityKit(key, cooldown, charges, extraCosts, transition);
            kits.Add(owner, kit);
            if (sender >= 0)
            {
                senderToOwner.Add(sender, owner);
            }

            return kit;
        }

        internal AuthorityKit GetKit(int owner) => kits[owner];

        // ----------------------------------------------------------------------
        /// <summary>
        /// 모든 Owner의 State를 같은 시간만큼 진행한다. Revision은 바뀌지 않는다.
        /// </summary>
        // ----------------------------------------------------------------------
        internal void Advance(float deltaTime)
        {
            foreach (var kit in kits.Values)
            {
                kit.State.Advance(deltaTime);
            }
        }

        internal AuthorityObserver AddObserver()
        {
            var observer = new AuthorityObserver();
            observers.Add(observer);
            return observer;
        }

        internal AuthorityObserver AddClientObserver()
        {
            var observer = AddObserver();
            observer.Deliver = (owner, snapshot) =>
                network.Send(network.Server, network.Client, AuthorityPacket.Of(owner, snapshot), ReplyDelay);
            return observer;
        }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// AOI에 진입시키고 현재 Snapshot을 한 번 보낸다. 진행 중 실행은 관측 사건으로만 알린다.
        /// </summary>
        // --------------------------------------------------------------------------------
        internal void Enter(AuthorityObserver observer, int owner, bool send = true)
        {
            if (!observer.Visible.Add(owner)) return;

            if (send)
            {
                SendSnapshot(observer, owner);
            }
            else
            {
                observer.LastSent[owner] = kits[owner].State.Revision;
            }

            if (running.TryGetValue(owner, out var current))
            {
                // 진입 시점에 이미 진행 중인 실행은 새 시작이 아니라 진행 중 관측으로만 알린다.
                observer.InProgress.Add((owner, current.Origin, current.ExecutionID, current.Tick));
            }
        }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// Owner의 진행 중 실행이 끝났음을 기록해 이후 진입에는 진행 중 관측을 보내지 않는다.
        /// </summary>
        // --------------------------------------------------------------------------------
        internal void EndExecution(int owner)
        {
            running.Remove(owner);
        }

        internal void Leave(AuthorityObserver observer, int owner)
        {
            observer.Visible.Remove(owner);
            observer.LastSent.Remove(owner);
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// Owner를 제거하고 모든 Observer의 가시 목록에서 뺀 뒤 Client에 제거를 알린다.
        /// </summary>
        // ----------------------------------------------------------------------
        internal void Despawn(int owner, int delayTicks = 0)
        {
            kits[owner].Dispose();
            kits.Remove(owner);
            running.Remove(owner);
            foreach (var observer in observers)
            {
                Leave(observer, owner);
            }

            network.Send(network.Server, network.Client, AuthorityPacket.OfDespawn(owner), delayTicks);
        }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// 보이는 Owner 중 마지막 전송 Revision과 다른 Ability만 State 복사본으로 보낸다.
        /// </summary>
        // --------------------------------------------------------------------------------
        internal void Replicate()
        {
            foreach (var observer in observers)
            {
                foreach (var owner in new List<int>(observer.Visible))
                {
                    if (observer.LastSent.TryGetValue(owner, out var sent)
                        && sent == kits[owner].State.Revision)
                    {
                        continue;
                    }

                    SendSnapshot(observer, owner);
                }
            }
        }

        private void SendSnapshot(AuthorityObserver observer, int owner)
        {
            var snapshot = new AuthoritySnapshot(kits[owner].State, network.Tick);
            observer.LastSent[owner] = kits[owner].State.Revision;
            observer.Deliver(owner, snapshot);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 도착한 요청을 처리하고 결과를 요청자에게 보낸 뒤 변경분을 복제한다.
        /// </summary>
        // ------------------------------------------------------------
        internal void Pump()
        {
            var received = network.Server.Received;
            while (processed < received.Count)
            {
                var message = received[processed++];
                if (message.Payload.Request == null) continue;

                var result = Handle(message.Epoch, message.Payload.Request);
                network.Send(network.Server, network.Client, AuthorityPacket.Of(result), ReplyDelay, ResultCopies);
            }

            Replicate();
        }

        private AuthorityResult Handle(int epoch, AuthorityRequest request)
        {
            if (epoch != network.Server.Epoch)
            {
                return new AuthorityResult(request.RequestID, epoch, AuthorityStatus.StaleEpoch, "이전 Epoch의 요청입니다.");
            }

            if (!senderToOwner.TryGetValue(request.Sender, out var owner) || owner != request.Owner)
            {
                return new AuthorityResult(request.RequestID, epoch, AuthorityStatus.UnknownOwner, "Sender와 Owner가 일치하지 않습니다.");
            }

            var key = (owner, epoch);
            if (lastResults.TryGetValue(key, out var previous))
            {
                if (request.RequestID == previous.RequestID)
                {
                    return new AuthorityResult
                    (
                        request.RequestID, epoch, AuthorityStatus.Duplicate, "재전송된 요청입니다.", original: previous
                    );
                }

                if (request.RequestID < previous.RequestID)
                {
                    return new AuthorityResult(request.RequestID, epoch, AuthorityStatus.Stale, "이미 지나간 Request ID입니다.");
                }
            }

            var result = Execute(owner, request.RequestID, epoch, request.Entry, request.Payload);
            lastResults[key] = result;
            return result;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 서버 State로 다시 선택하고 TryActivate로 한 번 사용한다.
        /// </summary>
        // ------------------------------------------------------------
        internal AuthorityResult Execute(int owner, int requestID, int epoch, string entry, object payload)
        {
            if (!(payload is int))
            {
                return new AuthorityResult(requestID, epoch, AuthorityStatus.BadRequest, "Payload 데이터 형식이 맞지 않습니다.");
            }

            ulong executionID = 0;
            var spawns = new List<(int SpawnIndex, ulong Key)>();
            bool Begin(in AbilityUse<string> use)
            {
                executionID = nextExecutionID++;
                Started.Add((executionID, use.Origin, network.Tick));
                running[owner] = (use.Origin, executionID, network.Tick);
                if (CreateSessions)
                {
                    var flow = new FixedTimelineExecutionFlowDefinition(10f);
                    var session = new ExecutionSession
                    (
                        executionID, new ExecutionContext(owner, owner), flow.CreateRuntime(),
                        maximumTime: flow.MaximumDuration
                    );
                    session.Begin();
                    Sessions.Add(session);
                }

                for (var index = 0; index < SpawnsPerUse && index < SpawnLimit; index++)
                {
                    var spawn = nextSpawnKey++;
                    Spawned.Add((spawn, use.Origin, requestID, index));
                    spawns.Add((index, spawn));
                }

                foreach (var observer in observers)
                {
                    if (observer.Visible.Contains(owner))
                    {
                        observer.Started.Add((owner, use.Origin, executionID));
                    }
                }

                return true;
            }

            if (!kits[owner].TryUse(entry, owner, payload, Begin, out var activation, out var used))
            {
                return new AuthorityResult(requestID, epoch, AuthorityStatus.Rejected, activation.Reason);
            }

            return new AuthorityResult
            (
                requestID, epoch, AuthorityStatus.Accepted, string.Empty,
                executionID, network.Tick, spawns: spawns
            );
        }
    }

    // ================================================================================
    /// <summary>
    /// 자기 State 뷰로 먼저 판단하고 서버 결과와 State 복사본으로 정정되는 Client이다.
    /// </summary>
    // ================================================================================
    internal sealed class AuthorityClient
    {
        private readonly FakeAbilityNetwork<AuthorityPacket> network;
        private int processed;
        private ulong appliedRevision;
        private bool applied;

        internal readonly AuthorityKit Kit;
        internal readonly int Sender;
        internal readonly int Owner;
        internal int NextRequestID = 1;
        internal bool Switching;
        internal bool Despawned;
        internal int DroppedSnapshots;
        internal readonly List<AuthorityResult> Results = new();
        internal Action<AuthorityResult> OnResult;

        internal AuthorityClient(FakeAbilityNetwork<AuthorityPacket> network, AuthorityKit kit, int sender, int owner)
        {
            this.network = network;
            Kit = kit;
            Sender = sender;
            Owner = owner;
        }

        internal AuthorityRequest Create()
        {
            return new AuthorityRequest(Sender, NextRequestID++, Owner, AuthorityKit.EntryName, network.Tick, 1);
        }

        internal void Send(AuthorityRequest request, int delayTicks = 0, int copies = 1) =>
            network.Send(network.Client, network.Server, AuthorityPacket.Of(request), delayTicks, copies);

        // --------------------------------------------------------------------------------
        /// <summary>
        /// 전환 중 입력은 거절하고 대기열에 넣지 않으며, 로컬 뷰가 준비 상태일 때만 요청을 보낸다.
        /// </summary>
        // --------------------------------------------------------------------------------
        internal AuthorityInput TryUse(out AuthorityRequest request, int delayTicks = 0)
        {
            request = null;
            if (Switching) return AuthorityInput.Switching;

            if (!Kit.IsReady(Owner)) return AuthorityInput.LocallyRejected;

            request = Create();
            Send(request, delayTicks);
            return AuthorityInput.Sent;
        }

        internal void ResetSession()
        {
            NextRequestID = 1;
            processed = network.Client.Received.Count;
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> 결과를 보관하고 복제 복사본을 원본 Revision 순서대로 적용한다. 복사 뒤
        /// <br/> 전송에 걸린 Tick만큼 진행해 보정하며 제거된 Owner의 복사본은 버린다.
        /// </summary>
        // ----------------------------------------------------------------------
        internal void Pump()
        {
            var received = network.Client.Received;
            while (processed < received.Count)
            {
                var payload = received[processed++].Payload;
                if (payload.Result != null)
                {
                    Results.Add(payload.Result);
                    OnResult?.Invoke(payload.Result);
                }

                if (payload.Despawned && payload.SnapshotOwner == Owner)
                {
                    Despawned = true;
                }

                if (payload.HasSnapshot && payload.SnapshotOwner == Owner)
                {
                    if (Despawned)
                    {
                        DroppedSnapshots++;
                    }
                    else if (!applied || payload.Snapshot.Revision > appliedRevision)
                    {
                        UnityEngine.JsonUtility.FromJsonOverwrite(UnityEngine.JsonUtility.ToJson(payload.Snapshot.State), Kit.State);
                        Kit.State.Advance((network.Tick - payload.Snapshot.Tick) * network.TickDelta);
                        appliedRevision = payload.Snapshot.Revision;
                        applied = true;
                    }
                }
            }
        }
    }

    // ============================================================
    /// <summary>
    /// Tick 진행과 결과 집계를 돕는 정적 보조 동작이다.
    /// </summary>
    // ============================================================
    internal static class AuthorityRig
    {
        internal static void Step
        (
            FakeAbilityNetwork<AuthorityPacket> network, AuthorityHost host, AuthorityClient client,
            bool reverse = false, Action serverEvent = null, Action afterClient = null
        )
        {
            network.AdvanceTick();
            host.Advance(network.TickDelta);
            client.Kit.State.Advance(network.TickDelta);
            network.DeliverReady(reverse);
            serverEvent?.Invoke();
            host.Pump();
            client.Pump();
            afterClient?.Invoke();
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

        // ----------------------------------------------------------------------
        /// <summary>
        /// 진행 중이거나 일시정지된 Cooldown의 남은 시간을 반환하며 준비 상태면 0이다.
        /// </summary>
        // ----------------------------------------------------------------------
        internal static float Remaining(AbilityState state) =>
            state.IsCooldownReady ? 0f : state.Cooldown.RemainingTime;

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
    }
}
