/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : AuthorityClient.cs
수정일 : 2026-11-19

# 설명
자기 State 뷰로 먼저 판단하고 서버 결과와 State 복사본으로 정정되는 테스트 전용 권한 Client를 제공한다.
Client는 자기 연결(Connection)의 끝점으로만 보내고 받으며, 결과는 이 연결로 온 것만 받는다.
자기 Owner는 Kit의 State로, 보이는 원격 Owner는 Ability 단위 사본(Replica)으로 들고 같은 Tick으로 진행한다.
결과·세션 전환·제거 통지·자기 실행 종료를 받는 계층(예측·예상 연출)은 Client 하나에 하나만 연결한다.
자기 Owner의 관측 사건은 원격 연출이 아니라 자기 권한 실행 기록(OwnRunning)과 연결된 계층의 종료 처리에 쓴다.
진행 중 실행은 같은 Owner라도 실행(ExecutionID)마다 따로 기록한다.
========================================================================= BLOCK_HEADER_END */

using System;
using System.Collections;
using System.Collections.Generic;

namespace inonego.Xeri.Game.TEST._Ability.Fixture
{
    using Xeri;
    using Xeri.Game;

    // ================================================================================
    /// <summary>
    /// 자기 State 뷰로 먼저 판단하고 서버 결과와 State 복사본으로 정정되는 Client이다.
    /// </summary>
    // ================================================================================
    internal sealed class AuthorityClient
    {

    #region 구성

        private readonly FakeAbilityNetwork<AuthorityPacket> network;
        private int processed;

        internal readonly AuthorityKit Kit;
        internal readonly FakeAbilityEndpoint<AuthorityPacket> Endpoint;
        internal readonly int Owner;
        internal int DroppedEpoch;

        internal int Connection => Endpoint.Connection;

        internal AuthorityClient(FakeAbilityNetwork<AuthorityPacket> network, AuthorityKit kit, int connection, int owner)
        {
            this.network = network;
            Kit = kit;
            Endpoint = network.Connect(connection);
            Owner = owner;
        }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/> 자기 Owner의 모든 Ability State와 원격 Owner 사본을 같은 시간만큼 진행한다.
        /// <br/> 알림 예외는 남은 진행을 막지 않고 모두 마친 뒤 전달한다.
        /// </summary>
        // --------------------------------------------------------------------------------
        internal void Advance(float deltaTime)
        {
            var states = new List<AbilityState>(Kit.States);
            foreach (var replica in replicas.Values)
            {
                states.AddRange(replica.Values);
            }

            List<Exception> failures = null;
            foreach (var state in states)
            {
                try
                {
                    state.Advance(deltaTime);
                }
                catch (Exception exception)
                {
                    (failures ??= new List<Exception>()).Add(exception);
                }
            }

            AuthorityRig.Rethrow(failures);
        }

    #endregion

    #region 계층 연결

        private Action<AuthorityResult> onResult;
        private Action onSessionReset;
        private Action onDespawned;
        private Action<ulong, ExecutionEndReason> onOwnEnded;
        private bool attached;

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/> 결과·세션 전환·자기 Owner 제거 통지와 권한 측에서 끝난 자기 실행(ExecutionID,
        /// <br/> 종료 사유)을 받을 계층을 연결한다.
        /// <br/> 한 Client에는 한 계층만 연결하며 다시 연결하면 예외를 던진다.
        /// </summary>
        // --------------------------------------------------------------------------------
        internal void Attach
        (
            Action<AuthorityResult> onResult, Action onSessionReset, Action onDespawned,
            Action<ulong, ExecutionEndReason> onOwnEnded
        )
        {
            if (attached)
            {
                throw new InvalidOperationException("이 Client에는 이미 결과를 받는 계층이 연결돼 있습니다.");
            }

            attached = true;
            this.onResult = onResult;
            this.onSessionReset = onSessionReset;
            this.onDespawned = onDespawned;
            this.onOwnEnded = onOwnEnded;
        }

    #endregion

    #region 입력과 요청

        internal int NextRequestID = 1;
        internal bool Switching;

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> 전환 중이 아니고 Owner가 제거되지 않아 새 입력을 받을 수 있는지 반환한다.
        /// <br/> 요청·예측·예상 연출 계층이 모두 이 조건으로 입력을 거른다.
        /// </summary>
        // ----------------------------------------------------------------------
        internal bool AcceptsInput => !Switching && !Despawned;

        // --------------------------------------------------------------------------------
        /// <summary>
        /// 전환 중 입력은 거절하고 대기열에 넣지 않으며, 로컬 뷰가 준비 상태일 때만 요청을 보낸다.
        /// </summary>
        // --------------------------------------------------------------------------------
        internal AuthorityInput TryUse(out AuthorityRequest request, int delayTicks = 0)
        {
            request = null;
            if (!AcceptsInput)
            {
                return Despawned ? AuthorityInput.LocallyRejected : AuthorityInput.Switching;
            }

            if (!Kit.IsReady(Owner)) return AuthorityInput.LocallyRejected;

            request = Create();
            Send(request, delayTicks);
            return AuthorityInput.Sent;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 다음 Request ID로 기본 Entry 사용 요청을 만든다.
        /// </summary>
        // ------------------------------------------------------------
        internal AuthorityRequest Create()
        {
            return new AuthorityRequest(NextRequestID++, Owner, AuthorityKit.EntryName, network.Tick, 1);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 이 연결의 끝점에서 서버로 요청을 보낸다.
        /// </summary>
        // ------------------------------------------------------------
        internal void Send(AuthorityRequest request, int delayTicks = 0, int copies = 1) =>
            network.Send(Endpoint, network.Server, AuthorityPacket.ForRequest(request), delayTicks, copies);

    #endregion

    #region 세션과 수신

        internal readonly List<AuthorityResult> Results = new();

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> 이 연결을 새 권한 세대로 합류시킨다. 끝점의 Epoch를 바꾸고 Request ID와
        /// <br/> 수신 위치, 이전 세대의 Snapshot 적용 기록·원격 사본·원격 관측과
        /// <br/> 자기 실행 기록을 버린다.
        /// <br/> 서버에서 계속되는 자기 실행은 합류 뒤 진행 중 관측으로 다시 받는다.
        /// <br/> 대기 중인 예측과 연출은 연결된 계층이 정리하게 알리고,
        /// <br/> Host에는 새 세대의 Snapshot과 Owner 목록을 받을 준비가 됐음을 알린다.
        /// </summary>
        // ----------------------------------------------------------------------
        internal void JoinEpoch(int epoch)
        {
            Endpoint.ChangeEpoch(epoch);
            NextRequestID = 1;
            processed = Endpoint.Received.Count;
            appliedRevisions.Clear();
            appliedCounts.Clear();
            replicas.Clear();
            RemoteRunning.Clear();
            OwnRunning.Clear();
            Despawned = false;
            network.Send(Endpoint, network.Server, AuthorityPacket.ForSessionReady());
            onSessionReset?.Invoke();
        }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/> 이 연결에 도착한 메시지를 순서대로 처리한다. 현재 Epoch가 아닌 메시지는 버린다.
        /// <br/> 한 메시지의 처리 실패(연결된 계층의 콜백 예외 등)는 같은 Tick의 나머지
        /// <br/> 메시지 처리를 막지 않고 모두 마친 뒤 전달한다.
        /// </summary>
        // --------------------------------------------------------------------------------
        internal void Pump()
        {
            List<Exception> failures = null;
            var received = Endpoint.Received;
            while (processed < received.Count)
            {
                var message = received[processed++];
                if (message.Epoch != Endpoint.Epoch)
                {
                    DroppedEpoch++;
                    continue;
                }

                try
                {
                    Receive(message.Payload);
                }
                catch (Exception exception)
                {
                    (failures ??= new List<Exception>()).Add(exception);
                }
            }

            AuthorityRig.Rethrow(failures);
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// 한 메시지를 결과, 관측, 자기 Owner 제거, Snapshot 순서로 처리한다.
        /// </summary>
        // ----------------------------------------------------------------------
        private void Receive(AuthorityPacket payload)
        {
            if (payload.Result != null)
            {
                HandleResult(payload.Result);
            }

            Observe(payload);
            CheckOwnRemoval(payload);

            if (payload.HasSnapshot)
            {
                Apply(payload.Owner, payload.Snapshot);
            }
        }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/> 결과를 보관하고 Duplicate는 최초 결과로 풀어 처리한다.
        /// <br/> 이전 Epoch 요청의 결과나 제거된 Owner의 결과는 기록만 하고 전달하지 않는다.
        /// </summary>
        // --------------------------------------------------------------------------------
        private void HandleResult(AuthorityResult received)
        {
            Results.Add(received);

            // Duplicate는 최초 결과의 재전송이므로 최초 결과로 처리해 순서가 뒤집혀도 확정을 유지한다.
            var result = received.Status == AuthorityStatus.Duplicate && received.Original != null
                ? received.Original
                : received;

            if (result.RequestEpoch == Endpoint.Epoch && !Despawned)
            {
                onResult?.Invoke(result);
            }
        }

    #endregion

    #region Owner 수명과 원격 관측

        internal bool Despawned;

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> 관측 사건으로 알게 된 원격 Owner별 진행 중 실행(원격 연출 중)이다.
        /// <br/> 진행 중 실행이 없는 Owner는 키가 없다.
        /// </summary>
        // ----------------------------------------------------------------------
        internal readonly Dictionary<int, HashSet<ulong>> RemoteRunning = new();

        // ------------------------------------------------------------
        /// <summary>
        /// 종료 사건으로 끝낸 원격 연출과 그 종료 사유이다.
        /// </summary>
        // ------------------------------------------------------------
        internal readonly List<(int Owner, ulong ExecutionID, ExecutionEndReason Reason)> RemoteEnded = new();

        // ------------------------------------------------------------
        /// <summary>
        /// 관측 사건으로 알게 된 자기 Owner의 진행 중 권한 실행이다.
        /// </summary>
        // ------------------------------------------------------------
        internal readonly HashSet<ulong> OwnRunning = new();

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/> 자기 Owner의 제거 통지를 받거나, 합류 때 받은 Owner 목록에 자기 Owner가 없으면
        /// <br/> 제거로 처리하고 연결된 계층에 알린다. 서버가 먼저 전환한 사이의 제거 통지는
        /// <br/> 버려졌을 수 있으므로 Owner 목록으로 맞춘다.
        /// </summary>
        // --------------------------------------------------------------------------------
        private void CheckOwnRemoval(AuthorityPacket payload)
        {
            var despawned = payload.Despawned && payload.Owner == Owner;
            var missing = payload.Owners != null && !Contains(payload.Owners, Owner);
            if ((despawned || missing) && !Despawned)
            {
                Despawned = true;
                OwnRunning.Clear();
                onDespawned?.Invoke();
            }
        }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/> 원격 Owner의 실행 관측 사건, AOI 이탈, 제거 통지로 실행마다 원격 연출과
        /// <br/> 원격 사본을 맞춘다. 종료 사건은 그 실행이 진행 중으로 남아 있을 때만 목록에서
        /// <br/> 빼며 종료 사유는 받은 그대로 기록한다. 관측 채널은 순서가 보장되므로 제거·이탈
        /// <br/> 뒤에 그 Owner의 사건이 다시 오면 재진입이다.
        /// <br/> 자기 Owner의 관측 사건은 원격 연출이 아니라 자기 실행 기록으로 맞춘다.
        /// </summary>
        // --------------------------------------------------------------------------------
        private void Observe(AuthorityPacket payload)
        {
            if (payload.Owner == Owner)
            {
                ObserveOwn(payload);
                return;
            }

            switch (payload.Observation)
            {
                case AuthorityObservation.Started:
                case AuthorityObservation.InProgress:
                    if (!RemoteRunning.TryGetValue(payload.Owner, out var started))
                    {
                        started = new HashSet<ulong>();
                        RemoteRunning.Add(payload.Owner, started);
                    }

                    started.Add(payload.ExecutionID);
                    break;

                case AuthorityObservation.Ended:
                    if (RemoteRunning.TryGetValue(payload.Owner, out var running)
                        && running.Remove(payload.ExecutionID)
                        && running.Count == 0)
                    {
                        RemoteRunning.Remove(payload.Owner);
                    }

                    RemoteEnded.Add((payload.Owner, payload.ExecutionID, payload.EndReason));
                    break;
            }

            if (payload.Despawned || payload.Left)
            {
                Forget(payload.Owner);
            }
        }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/> 자기 Owner의 시작·진행 중 사건은 자기 권한 실행으로 기록하고, 종료 사건은 그
        /// <br/> 실행이면 기록을 지운 뒤 연결된 계층이 같은 실행의 예측 Session을 끝내게 알린다.
        /// </summary>
        // --------------------------------------------------------------------------------
        private void ObserveOwn(AuthorityPacket payload)
        {
            switch (payload.Observation)
            {
                case AuthorityObservation.Started:
                case AuthorityObservation.InProgress:
                    OwnRunning.Add(payload.ExecutionID);
                    break;

                case AuthorityObservation.Ended:
                    OwnRunning.Remove(payload.ExecutionID);
                    onOwnEnded?.Invoke(payload.ExecutionID, payload.EndReason);
                    break;
            }
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> 제거되거나 시야에서 사라진 원격 Owner의 사본·원격 연출·적용 기록을 지운다.
        /// <br/> 다시 보이면 진행 중 관측과 Snapshot을 처음부터 받는다.
        /// </summary>
        // ----------------------------------------------------------------------
        private void Forget(int owner)
        {
            RemoteRunning.Remove(owner);
            replicas.Remove(owner);
            RemoveOwner(appliedRevisions, owner);
            RemoveOwner(appliedCounts, owner);
        }

        private static void RemoveOwner<TValue>(Dictionary<(int Owner, AbilityKey Key), TValue> records, int owner)
        {
            foreach (var key in new List<(int Owner, AbilityKey Key)>(records.Keys))
            {
                if (key.Owner == owner)
                {
                    records.Remove(key);
                }
            }
        }

        private static bool Contains(IReadOnlyList<int> owners, int owner)
        {
            foreach (var item in owners)
            {
                if (item == owner)
                {
                    return true;
                }
            }

            return false;
        }

    #endregion

    #region Snapshot 적용

        private readonly Dictionary<(int Owner, AbilityKey Key), ulong> appliedRevisions = new();
        private readonly Dictionary<(int Owner, AbilityKey Key), int> appliedCounts = new();
        private readonly Dictionary<int, Dictionary<AbilityKey, AbilityState>> replicas = new();
        internal int SnapshotsApplied;

        // ------------------------------------------------------------
        /// <summary>
        /// 원격 Owner의 Ability 사본을 반환하며 아직 받지 않았으면 null이다.
        /// </summary>
        // ------------------------------------------------------------
        internal AbilityState Replica(int owner, AbilityKey key)
        {
            return replicas.TryGetValue(owner, out var states) && states.TryGetValue(key, out var state) ? state : null;
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> 그 Owner의 그 Ability에 지금까지 적용한 Snapshot 수를 반환한다.
        /// <br/> 예측 복원이 그 Ability의 더 최신 권한 상태를 덮지 않게 하는 데 쓴다.
        /// </summary>
        // ----------------------------------------------------------------------
        internal int AppliedCount(int owner, AbilityKey key)
        {
            return appliedCounts.TryGetValue((owner, key), out var count) ? count : 0;
        }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/> 그 Owner의 같은 Key State에 그 Ability로 처음이거나 마지막 적용보다
        /// <br/> 작지 않은 Revision의 복사본만 적용하고 전송에 걸린 Tick만큼 진행해 보정한다.
        /// <br/> 같은 Revision은 서버가 실행하지 않은 요청 뒤에 다시 보낸 정정이므로 적용한다.
        /// <br/> 원격 Owner는 처음 받을 때 사본을 만든다.
        /// <br/> 자기 Kit에 없는 Ability의 복사본은 버린다.
        /// </summary>
        // --------------------------------------------------------------------------------
        private void Apply(int owner, AuthoritySnapshot snapshot)
        {
            var state = FindTarget(owner, snapshot.Key);
            if (state == null) return;

            var key = (owner, snapshot.Key);
            if (appliedRevisions.TryGetValue(key, out var applied) && snapshot.Revision < applied)
            {
                return;
            }

            state.CloneFrom(snapshot.State);
            state.Advance((network.Tick - snapshot.Tick) * network.TickDelta);
            appliedRevisions[key] = snapshot.Revision;
            appliedCounts[key] = AppliedCount(owner, snapshot.Key) + 1;
            SnapshotsApplied++;
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> Snapshot을 적용할 State를 찾는다. 자기 Owner는 Kit의 State이고
        /// <br/> 원격 Owner는 사본이며 없으면 만든다.
        /// </summary>
        // ----------------------------------------------------------------------
        private AbilityState FindTarget(int owner, AbilityKey key)
        {
            if (owner == Owner)
            {
                return Kit.FindState(key);
            }

            if (!replicas.TryGetValue(owner, out var states))
            {
                states = new Dictionary<AbilityKey, AbilityState>();
                replicas.Add(owner, states);
            }

            if (!states.TryGetValue(key, out var state))
            {
                state = new AbilityState(key);
                states.Add(key, state);
            }

            return state;
        }

    #endregion

    }
}
