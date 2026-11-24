/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : AuthorityReplicator.cs
수정일 : 2026-11-23

# 설명
권한 Host의 AOI 가시 목록, Ability 단위 Snapshot 변경분 복제와 실행 관측을 맡는 테스트 전용 Replicator를 제공한다.
Owner 등록과 요청 처리는 AuthorityHost가 맡고, 이 파일은 Owner의 Ability State 목록을 조회 콜백으로만 읽는다.
Snapshot, 실행 시작·진행 중·종료, AOI 이탈, Owner 제거 통지는 모두 Observer의 전달 콜백 하나로 보낸다.
그래서 AOI와 Epoch 규칙이 사건 종류와 무관하게 같은 경로에서 적용되고, 연결별로 보낸 순서대로 도착한다(관측 채널).
한 Observer의 전달 실패는 다른 Observer 전달과 상태 정리를 막지 않으며 모아서 마지막에 전달한다.
Snapshot은 (Owner, AbilityKey) 단위로 보내며 Observer가 숨긴 Ability는 보내지 않는다.
Owner의 Status 컨테이너는 Ability State와 별개로 Owner 단위 Status Snapshot으로 Revision이 바뀐 Tick에만 보낸다.
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
    /// AOI 필터와 공개 범위를 적용받는 복제 대상이다.
    /// </summary>
    // ============================================================
    internal sealed class AuthorityObserver
    {

    #region 구성

        internal readonly HashSet<int> Visible = new();

        // ------------------------------------------------------------
        /// <summary>
        /// Client로 보내는 Observer의 대상 연결이며 아니면 null이다.
        /// </summary>
        // ------------------------------------------------------------
        internal int? Connection;

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> 이 Observer에게 사건 하나를 전달하는 유일한 경로이다.
        /// <br/> 인자는 (Owner, 메시지, 전송 지연 Tick)이며 기본은 사건 기록이다.
        /// </summary>
        // ----------------------------------------------------------------------
        internal Action<int, AuthorityPacket, int> Deliver;

        internal AuthorityObserver()
        {
            Deliver = (owner, packet, _) => Record(owner, packet);
        }

    #endregion

    #region 공개 범위와 전송 기록

        internal readonly Dictionary<(int Owner, AbilityKey Key), (int Epoch, ulong Revision)> LastSent = new();
        internal readonly Dictionary<int, (int Epoch, ulong Revision)> StatusLastSent = new();
        private readonly HashSet<(int Owner, AbilityKey Key)> hidden = new();

        // ------------------------------------------------------------
        /// <summary>
        /// 공개 범위로 숨기지 않은 Ability인지 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        internal bool Discloses(int owner, AbilityKey key) => !hidden.Contains((owner, key));

        // ----------------------------------------------------------------------
        /// <summary>
        /// 공개 범위를 바꾼다. 보이는 동안의 변경은 Replicator가 이탈·재진입으로 알린다.
        /// </summary>
        // ----------------------------------------------------------------------
        internal void SetHidden(int owner, AbilityKey key, bool isHidden)
        {
            if (isHidden)
            {
                hidden.Add((owner, key));
            }
            else
            {
                hidden.Remove((owner, key));
            }
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Owner의 Ability 전송 기록과 Status 전송 기록을 모두 지운다.
        /// </summary>
        // ------------------------------------------------------------
        internal void ForgetAllSent(int owner)
        {
            ForgetSent(owner);
            StatusLastSent.Remove(owner);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Owner의 모든 Ability 전송 기록을 지운다.
        /// </summary>
        // ------------------------------------------------------------
        internal void ForgetSent(int owner)
        {
            var keys = new List<(int Owner, AbilityKey Key)>();
            foreach (var key in LastSent.Keys)
            {
                if (key.Owner == owner)
                {
                    keys.Add(key);
                }
            }

            foreach (var key in keys)
            {
                LastSent.Remove(key);
            }
        }

    #endregion

    #region 사건 기록

        internal readonly List<(int Owner, AuthoritySnapshot Snapshot)> Snapshots = new();
        internal readonly List<(int Owner, AuthorityStatusSnapshot Snapshot)> StatusSnapshots = new();
        internal readonly List<(int Owner, AbilityUseOrigin Origin, ulong ExecutionID)> Started = new();
        internal readonly List<(int Owner, AbilityUseOrigin Origin, ulong ExecutionID, long StartTick)> InProgress = new();
        internal readonly List<(int Owner, ulong ExecutionID, ExecutionEndReason Reason)> Ended = new();
        internal readonly List<int> Removed = new();
        internal readonly List<int> Left = new();

        // ------------------------------------------------------------
        /// <summary>
        /// 전달한 사건을 종류별 목록에 기록한다.
        /// </summary>
        // ------------------------------------------------------------
        internal void Record(int owner, AuthorityPacket packet)
        {
            if (packet.HasSnapshot)
            {
                Snapshots.Add((owner, packet.Snapshot));
            }

            if (packet.HasStatus)
            {
                StatusSnapshots.Add((owner, packet.StatusSnapshot));
            }

            switch (packet.Observation)
            {
                case AuthorityObservation.Started:
                    Started.Add((owner, packet.Origin, packet.ExecutionID));
                    break;

                case AuthorityObservation.InProgress:
                    InProgress.Add((owner, packet.Origin, packet.ExecutionID, packet.StartTick));
                    break;

                case AuthorityObservation.Ended:
                    Ended.Add((owner, packet.ExecutionID, packet.EndReason));
                    break;
            }

            if (packet.Despawned)
            {
                Removed.Add(owner);
            }

            if (packet.Left)
            {
                Left.Add(owner);
            }
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Owner에 대해 기록한 Snapshot 수를 반환한다.
        /// </summary>
        // ------------------------------------------------------------
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

        // ------------------------------------------------------------
        /// <summary>
        /// Owner에 대해 기록한 Status Snapshot 수를 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        internal int CountStatus(int owner)
        {
            var count = 0;
            foreach (var entry in StatusSnapshots)
            {
                if (entry.Owner == owner)
                {
                    count++;
                }
            }

            return count;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Owner의 한 Ability에 대해 기록한 Snapshot 수를 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        internal int Count(int owner, AbilityKey key)
        {
            var count = 0;
            foreach (var entry in Snapshots)
            {
                if (entry.Owner == owner && entry.Snapshot.Key == key)
                {
                    count++;
                }
            }

            return count;
        }

    #endregion

    }

    // ======================================================================
    /// <summary>
    /// <br/> AOI 가시 목록과 Ability 단위 Snapshot 변경분 복제, 실행 관측과
    /// <br/> Owner 제거 통지를 한 전달 경로로 맡는다.
    /// </summary>
    // ======================================================================
    internal sealed class AuthorityReplicator
    {

    #region 구성

        private readonly FakeAbilityNetwork<AuthorityPacket> network;
        private readonly Func<int, IReadOnlyList<AbilityState>> findStates;
        private readonly Func<int, StatusContainer<int>> findStatuses;
        private readonly Func<int> clientDelay;
        private readonly List<AuthorityObserver> observers = new();
        private readonly HashSet<(AuthorityObserver Observer, int Owner, AbilityKey Key)> sending = new();
        private readonly HashSet<(AuthorityObserver Observer, int Owner)> sendingStatus = new();
        private readonly Dictionary<int, List<(AbilityUseOrigin Origin, ulong ExecutionID, long Tick)>> running = new();

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/> Owner의 Ability State 목록 조회 콜백과 Client 전송 지연 콜백으로 생성한다.
        /// <br/> State 목록 조회 콜백은 등록되지 않은 Owner에 null을 반환한다.
        /// <br/> Status 컨테이너 조회 콜백은 Status를 복제하지 않거나
        /// <br/> 등록되지 않은 Owner에 null을 반환한다.
        /// </summary>
        // --------------------------------------------------------------------------------
        internal AuthorityReplicator
        (
            FakeAbilityNetwork<AuthorityPacket> network,
            Func<int, IReadOnlyList<AbilityState>> findStates,
            Func<int> clientDelay,
            Func<int, StatusContainer<int>> findStatuses = null
        )
        {
            this.network = network;
            this.findStates = findStates;
            this.clientDelay = clientDelay;
            this.findStatuses = findStatuses ?? (_ => null);
        }

    #endregion

    #region Observer 등록과 재동기화

        // ------------------------------------------------------------
        /// <summary>
        /// 받은 사건을 기록만 하는 Observer를 추가한다.
        /// </summary>
        // ------------------------------------------------------------
        internal AuthorityObserver AddObserver()
        {
            var observer = new AuthorityObserver();
            observers.Add(observer);
            return observer;
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> 지정한 연결의 Client 끝점으로 사건을 보내는 Observer를 추가한다.
        /// <br/> 보낸 사건은 Observer 목록에도 같은 내용으로 기록한다.
        /// </summary>
        // ----------------------------------------------------------------------
        internal AuthorityObserver AddClientObserver(int connection)
        {
            var observer = AddObserver();
            observer.Connection = connection;
            observer.Deliver = (owner, packet, delayTicks) =>
            {
                observer.Record(owner, packet);
                network.Send(network.Server, network.Connect(connection), packet, delayTicks, ordered: true);
            };
            return observer;
        }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/> 지정한 연결에 새 세대로 합류한 Client를 다시 맞춘다. 그 연결로 보내는 Observer의
        /// <br/> 전송 기록을 지워 다음 복제가 Snapshot을 다시 보내게 하고, 보이는 Owner의
        /// <br/> 진행 중 실행은 진행 중 관측 사건으로 다시 알린다.
        /// </summary>
        // --------------------------------------------------------------------------------
        internal void ResyncClientObservers(int connection)
        {
            List<Exception> failures = null;
            foreach (var observer in new List<AuthorityObserver>(observers))
            {
                if (observer.Connection != connection) continue;

                observer.LastSent.Clear();
                observer.StatusLastSent.Clear();
                foreach (var owner in new List<int>(observer.Visible))
                {
                    try
                    {
                        NotifyInProgress(observer, owner);
                    }
                    catch (Exception exception)
                    {
                        (failures ??= new List<Exception>()).Add(exception);
                    }
                }
            }

            AuthorityRig.Rethrow(failures);
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> 지정한 연결로 보내는 Observer가 그 Owner를 보고 있으면 전송 기록을 지워
        /// <br/> 다음 복제에서 그 Owner의 공개 Ability Snapshot을 다시 보내게 한다.
        /// <br/> 서버 State가 바뀌지 않아도 Client를 권한 값으로 정정할 때 쓴다.
        /// </summary>
        // ----------------------------------------------------------------------
        internal void ResendOwner(int connection, int owner)
        {
            foreach (var observer in observers)
            {
                if (observer.Connection == connection && observer.Visible.Contains(owner))
                {
                    observer.ForgetSent(owner);
                }
            }
        }

    #endregion

    #region AOI

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/> AOI에 진입시키고 공개할 Ability마다 현재 Snapshot을 한 번 보낸다.
        /// <br/> 진행 중 실행은 Snapshot보다 먼저 진행 중 관측 사건으로만 알린다.
        /// <br/> 등록되지 않은 Owner는 아무 상태도 바꾸지 않고 예외로 거부한다.
        /// <br/> 전달 실패는 나머지 전송을 마친 뒤 전달하며 보내지 못한 Snapshot은 다음 복제가 보낸다.
        /// </summary>
        // --------------------------------------------------------------------------------
        internal void Enter(AuthorityObserver observer, int owner, bool send = true)
        {
            var states = findStates(owner);
            if (states == null)
            {
                throw new ArgumentException("등록되지 않은 Owner는 AOI에 진입할 수 없습니다.", nameof(owner));
            }

            if (!observer.Visible.Add(owner)) return;

            // Enter는 다시 시도되지 않으므로 진행 중 관측을 Snapshot보다 먼저 보낸다.
            List<Exception> failures = null;
            try
            {
                NotifyInProgress(observer, owner);
            }
            catch (Exception exception)
            {
                (failures ??= new List<Exception>()).Add(exception);
            }

            foreach (var state in new List<AbilityState>(states))
            {
                if (!observer.Discloses(owner, state.Key)) continue;

                if (!send)
                {
                    observer.LastSent[(owner, state.Key)] = (network.Server.Epoch, state.Revision);
                    continue;
                }

                try
                {
                    SendSnapshot(observer, owner, state);
                }
                catch (Exception exception)
                {
                    (failures ??= new List<Exception>()).Add(exception);
                }
            }

            var statuses = findStatuses(owner);
            if (statuses != null)
            {
                if (!send)
                {
                    observer.StatusLastSent[owner] = (network.Server.Epoch, statuses.Revision);
                }
                else
                {
                    try
                    {
                        SendStatus(observer, owner, statuses);
                    }
                    catch (Exception exception)
                    {
                        (failures ??= new List<Exception>()).Add(exception);
                    }
                }
            }

            AuthorityRig.Rethrow(failures);
        }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/> AOI에서 빼고 관측 사건과 같은 경로로 이탈을 알린 뒤 그 Owner의 전송 기록을 지운다.
        /// <br/> 이미 보이지 않는 Owner이면 아무것도 하지 않는다.
        /// </summary>
        // --------------------------------------------------------------------------------
        internal void Leave(AuthorityObserver observer, int owner)
        {
            if (!observer.Visible.Contains(owner)) return;

            try
            {
                observer.Deliver(owner, AuthorityPacket.ForLeave(owner), clientDelay());
            }
            finally
            {
                // 통지가 실패해도 이 Observer는 더 이상 그 Owner를 보지 않는다.
                Forget(observer, owner);
            }
        }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/> 한 Ability의 공개 범위를 바꾼다. 보이는 동안 바꾸면 이탈과 재진입으로 알려
        /// <br/> 숨긴 Ability의 사본을 Client에서 지우고 공개한 Ability를 처음부터 보낸다.
        /// <br/> 이탈 통지가 실패해도 공개 범위는 바뀌고 다시 진입한 뒤 실패를 전달한다.
        /// </summary>
        // --------------------------------------------------------------------------------
        internal void SetDisclosure(AuthorityObserver observer, int owner, AbilityKey key, bool disclosed)
        {
            if (observer.Discloses(owner, key) == disclosed) return;

            observer.SetHidden(owner, key, !disclosed);
            if (!observer.Visible.Contains(owner)) return;

            List<Exception> failures = null;
            try
            {
                Leave(observer, owner);
            }
            catch (Exception exception)
            {
                (failures ??= new List<Exception>()).Add(exception);
            }

            try
            {
                Enter(observer, owner);
            }
            catch (Exception exception)
            {
                (failures ??= new List<Exception>()).Add(exception);
            }

            AuthorityRig.Rethrow(failures);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 통지 없이 가시 목록과 그 Owner의 전송 기록에서 뺀다.
        /// </summary>
        // ------------------------------------------------------------
        private static void Forget(AuthorityObserver observer, int owner)
        {
            observer.Visible.Remove(owner);
            observer.ForgetAllSent(owner);
        }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/> Owner의 진행 중인 실행마다 새 시작이 아니라 진행 중 관측 사건으로 시작 순서대로
        /// <br/> 이 Observer에 알린다. 한 실행의 전달 실패는 나머지를 보낸 뒤 전달한다.
        /// </summary>
        // --------------------------------------------------------------------------------
        private void NotifyInProgress(AuthorityObserver observer, int owner)
        {
            if (!running.TryGetValue(owner, out var executions)) return;

            List<Exception> failures = null;
            foreach (var current in new List<(AbilityUseOrigin Origin, ulong ExecutionID, long Tick)>(executions))
            {
                try
                {
                    var inProgress = AuthorityPacket.ForInProgress(owner, current.Origin, current.ExecutionID, current.Tick);
                    observer.Deliver(owner, inProgress, clientDelay());
                }
                catch (Exception exception)
                {
                    (failures ??= new List<Exception>()).Add(exception);
                }
            }

            AuthorityRig.Rethrow(failures);
        }

    #endregion

    #region 실행 관측

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/> 사용이 확정된 실행을 Owner의 진행 중 실행에 더하고 그 Owner가 보이는
        /// <br/> Observer에 시작 사건을 알린다. 같은 Owner의 다른 진행 중 실행은 그대로 둔다.
        /// <br/> 배타 교체는 게임 Host가 이전 실행을 Replaced로 끝내서 표현한다.
        /// <br/> 전달 실패는 모든 Observer에 보낸 뒤 전달한다.
        /// </summary>
        // --------------------------------------------------------------------------------
        internal void BeginExecution(int owner, AbilityUseOrigin origin, ulong executionID, long tick)
        {
            if (!running.TryGetValue(owner, out var executions))
            {
                executions = new List<(AbilityUseOrigin Origin, ulong ExecutionID, long Tick)>();
                running.Add(owner, executions);
            }

            executions.Add((origin, executionID, tick));
            List<Exception> failures = null;
            Publish(owner, AuthorityPacket.ForStarted(owner, origin, executionID, tick), clientDelay(), ref failures);
            AuthorityRig.Rethrow(failures);
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> Owner의 진행 중 실행에서 그 실행의 기록만 지우고, 그 Owner가 보이는
        /// <br/> Observer에 종료 사유를 그대로 실은 종료 사건을 알린다.
        /// <br/> 그 실행의 기록이 없으면 아무것도 하지 않는다.
        /// </summary>
        // ----------------------------------------------------------------------
        internal void EndExecution(int owner, ulong executionID, ExecutionEndReason reason)
        {
            if (!running.TryGetValue(owner, out var executions)) return;

            var index = executions.FindIndex(item => item.ExecutionID == executionID);
            if (index < 0) return;

            executions.RemoveAt(index);
            if (executions.Count == 0)
            {
                running.Remove(owner);
            }

            List<Exception> failures = null;
            Publish(owner, AuthorityPacket.ForEnded(owner, executionID, reason), clientDelay(), ref failures);
            AuthorityRig.Rethrow(failures);
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> 제거된 Owner의 모든 진행 중 실행 기록을 종료 사건 없이 지운다.
        /// <br/> 원격에는 Owner 제거 통지가 실행 종료까지 대신한다.
        /// </summary>
        // ----------------------------------------------------------------------
        internal void ClearExecution(int owner)
        {
            running.Remove(owner);
        }

    #endregion

    #region Owner 제거

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/> 제거된 Owner를 보던 Observer와 그 Owner를 조종하던 연결에 관측 채널로 제거를
        /// <br/> 알린 뒤 이탈 통지 없이 모든 가시 목록과 전송 기록에서 뺀다. 조종하던 연결은
        /// <br/> 그 연결의 Observer가 이미 알렸으면 다시 보내지 않는다. 전달 실패는 정리를 막지
        /// <br/> 않고 모아서 반환한다.
        /// </summary>
        // --------------------------------------------------------------------------------
        internal List<Exception> RemoveOwner(int owner, IReadOnlyList<int> ownerConnections)
        {
            var packet = AuthorityPacket.ForDespawn(owner);
            List<Exception> failures = null;
            var notified = Publish(owner, packet, clientDelay(), ref failures);
            foreach (var connection in ownerConnections)
            {
                if (notified.Add(connection))
                {
                    network.Send(network.Server, network.Connect(connection), packet, clientDelay(), ordered: true);
                }
            }

            foreach (var observer in observers)
            {
                Forget(observer, owner);
            }

            return failures;
        }

    #endregion

    #region 변경분 복제

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/> 보이는 Owner의 공개 Ability 중 마지막 전송 Epoch·Revision과 다른
        /// <br/> Ability의 State 복사본만 보낸다. 전송 중 제거된 Owner는 건너뛴다.
        /// <br/> 한 전송의 실패는 나머지 전송을 마친 뒤 전달하며 그 Snapshot은 다음 복제가 보낸다.
        /// </summary>
        // --------------------------------------------------------------------------------
        internal void Replicate()
        {
            List<Exception> failures = null;
            foreach (var observer in new List<AuthorityObserver>(observers))
            {
                foreach (var owner in new List<int>(observer.Visible))
                {
                    // 전송 콜백이 다른 Owner를 제거했을 수 있으므로 보내기 직전에 확인한다.
                    var states = findStates(owner);
                    if (!observer.Visible.Contains(owner) || states == null)
                    {
                        continue;
                    }

                    foreach (var state in new List<AbilityState>(states))
                    {
                        if (!observer.Visible.Contains(owner) || findStates(owner) == null)
                        {
                            break;
                        }

                        if (!observer.Discloses(owner, state.Key) || IsSent(observer, owner, state))
                        {
                            continue;
                        }

                        try
                        {
                            SendSnapshot(observer, owner, state);
                        }
                        catch (Exception exception)
                        {
                            (failures ??= new List<Exception>()).Add(exception);
                        }
                    }

                    // Status는 이산 변경(Revision 증가)이 있을 때만 보낸다. 시간 만료는 보내지 않는다.
                    var statuses = findStatuses(owner);
                    if (!observer.Visible.Contains(owner) || statuses == null || IsStatusSent(observer, owner, statuses))
                    {
                        continue;
                    }

                    try
                    {
                        SendStatus(observer, owner, statuses);
                    }
                    catch (Exception exception)
                    {
                        (failures ??= new List<Exception>()).Add(exception);
                    }
                }
            }

            AuthorityRig.Rethrow(failures);
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// 이 Observer에 현재 세대의 같은 Status Revision을 이미 보냈는지 반환한다.
        /// </summary>
        // ----------------------------------------------------------------------
        private bool IsStatusSent(AuthorityObserver observer, int owner, StatusContainer<int> statuses)
        {
            return observer.StatusLastSent.TryGetValue(owner, out var sent)
                && sent.Epoch == network.Server.Epoch
                && sent.Revision == statuses.Revision;
        }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/> Status Snapshot을 보내고 전달이 끝난 뒤에만 보낸 Epoch·Revision을 기록한다.
        /// <br/> 같은 Observer·Owner의 전송 중 콜백이 다시 보내려 하면 건너뛴다.
        /// </summary>
        // --------------------------------------------------------------------------------
        private void SendStatus(AuthorityObserver observer, int owner, StatusContainer<int> statuses)
        {
            if (!sendingStatus.Add((observer, owner))) return;

            try
            {
                var snapshot = new AuthorityStatusSnapshot(statuses, network.Tick);
                var epoch = network.Server.Epoch;
                var revision = statuses.Revision;
                observer.Deliver(owner, AuthorityPacket.ForStatus(owner, snapshot), clientDelay());

                if (observer.Visible.Contains(owner) && findStatuses(owner) != null)
                {
                    observer.StatusLastSent[owner] = (epoch, revision);
                }
            }
            finally
            {
                sendingStatus.Remove((observer, owner));
            }
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 이 Observer에 현재 세대의 같은 Revision을 이미 보냈는지 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        private bool IsSent(AuthorityObserver observer, int owner, AbilityState state)
        {
            return observer.LastSent.TryGetValue((owner, state.Key), out var sent)
                && sent.Epoch == network.Server.Epoch
                && sent.Revision == state.Revision;
        }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/> Snapshot을 보내고 전달이 끝난 뒤에만 보낸 Epoch·Revision을 기록한다.
        /// <br/> 같은 Observer·Owner·Ability의 전송 중 콜백이 다시 보내려 하면 건너뛰어
        /// <br/> 기록 전의 같은 Revision이 중복 전달되거나 무한 재귀하지 않게 한다.
        /// </summary>
        // --------------------------------------------------------------------------------
        private void SendSnapshot(AuthorityObserver observer, int owner, AbilityState state)
        {
            if (!sending.Add((observer, owner, state.Key))) return;

            try
            {
                var snapshot = new AuthoritySnapshot(state, network.Tick);
                var epoch = network.Server.Epoch;
                var revision = state.Revision;
                observer.Deliver(owner, AuthorityPacket.ForSnapshot(owner, snapshot), clientDelay());

                // 전송 콜백이 예외 없이 끝난 뒤에만 보낸 Epoch와 Revision으로 기록한다.
                // 콜백이 Owner를 제거했거나 가시 목록에서 뺐다면 기록을 되살리지 않는다.
                if (observer.Visible.Contains(owner) && findStates(owner) != null)
                {
                    observer.LastSent[(owner, state.Key)] = (epoch, revision);
                }
            }
            finally
            {
                sending.Remove((observer, owner, state.Key));
            }
        }

    #endregion

    #region 사건 전달

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/> Owner가 보이는 모든 Observer에게 같은 사건을 같은 경로로 전달하고
        /// <br/> 전달받은 Client Observer의 연결 목록을 반환한다.
        /// <br/> 한 Observer의 전달 실패는 failures에 모으고 나머지 Observer에 계속 보낸다.
        /// </summary>
        // --------------------------------------------------------------------------------
        private HashSet<int> Publish(int owner, AuthorityPacket packet, int delayTicks, ref List<Exception> failures)
        {
            var notified = new HashSet<int>();
            foreach (var observer in new List<AuthorityObserver>(observers))
            {
                if (!observer.Visible.Contains(owner)) continue;

                try
                {
                    observer.Deliver(owner, packet, delayTicks);
                }
                catch (Exception exception)
                {
                    (failures ??= new List<Exception>()).Add(exception);
                    continue;
                }

                if (observer.Connection.HasValue)
                {
                    notified.Add(observer.Connection.Value);
                }
            }

            return notified;
        }

    #endregion

    }
}
