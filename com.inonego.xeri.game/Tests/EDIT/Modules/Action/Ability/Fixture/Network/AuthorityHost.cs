/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : AuthorityHost.cs
수정일 : 2026-11-24

# 설명
권한 사용 프로토콜(L1)의 테스트 전용 권한 Host를 제공한다.
요청 검증, 중복 제거, 서버 재선택과 Owner 수명을 맡고 AOI와 변경분 복제는 AuthorityReplicator에 맡긴다.
요청자의 연결 신원은 전송 계층이 붙인 값만 쓰고, 결과는 요청한 연결에만 보낸다.
요청 장부는 (Owner, Epoch)마다 마지막 Request ID와 최근 RequestWindow개의 결과(BoundedFIFOMap)를 보관하고 지난 세대 장부는 버린다.
RequestWindow는 장부가 처음 만들어질 때 적용되며 그 뒤 값을 바꿔도 이미 만들어진 장부의 창 크기는 바뀌지 않는다.
결과는 결과 채널(ReplyDelay), 관측 사건·Owner 목록은 순서가 보장되는 관측 채널(ObservationDelay)로 보내며 두 채널끼리는 순서가 바뀔 수 있다.
========================================================================= BLOCK_HEADER_END */

using System;
using System.Collections;
using System.Collections.Generic;

namespace inonego.Xeri.Game.TEST._Ability.Fixture
{
    using Xeri;
    using Xeri.Game;

    // ======================================================================
    /// <summary>
    /// 요청 검증, 중복 제거, 서버 재선택과 Owner 수명을 맡는 권한 Host이다.
    /// </summary>
    // ======================================================================
    internal sealed class AuthorityHost : IDisposable
    {

    #region 내부 데이터

        // ============================================================
        /// <summary>
        /// 한 (Owner, Epoch)의 마지막 Request ID와 최근 결과 창이다.
        /// </summary>
        // ============================================================
        private sealed class RequestLedger
        {
            internal int Last;
            internal readonly BoundedFIFOMap<int, AuthorityResult> Results;

            internal RequestLedger(int window)
            {
                Results = new BoundedFIFOMap<int, AuthorityResult>(window);
            }
        }

    #endregion

    #region 구성

        private readonly FakeAbilityNetwork<AuthorityPacket> network;
        private readonly Dictionary<int, int> connectionToOwner = new();
        private readonly Dictionary<int, AuthorityKit> kits = new();
        private readonly Dictionary<(int Owner, int Epoch), RequestLedger> ledgers = new();
        private readonly HashSet<(int Owner, int Epoch)> retired = new();
        private readonly List<(int Owner, ExecutionSession Session)> ownedSessions = new();
        private ulong nextExecutionID = 100;
        private ulong nextSpawnKey = 500;
        private int processed;

        internal readonly AuthorityReplicator Replication;

        internal AuthorityHost(FakeAbilityNetwork<AuthorityPacket> network)
        {
            this.network = network;
            Replication = new AuthorityReplicator(network, FindStates, () => ObservationTicks, FindStatuses);
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

    #endregion

    #region 주입 설정

        internal int ReplyDelay;
        internal int ResultCopies = 1;

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/> 관측 채널(Snapshot·실행 관측·AOI 이탈·제거 통지·Owner 목록)의 전송 지연이다.
        /// <br/> 비우면 ReplyDelay와 같다. 관측 채널 안에서는 보낸 순서가 유지된다.
        /// </summary>
        // --------------------------------------------------------------------------------
        internal int? ObservationDelay;

        private int ObservationTicks => ObservationDelay ?? ReplyDelay;

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> 요청 장부가 결과를 보관하는 최근 요청 수이다. 1보다 작으면 1로 본다.
        /// <br/> 장부가 처음 만들어질 때 적용되므로 요청을 보내기 전에 정한다.
        /// </summary>
        // ----------------------------------------------------------------------
        internal int RequestWindow = 8;

        // ------------------------------------------------------------
        /// <summary>
        /// true를 반환한 결과는 보내지 않고 버린다(결과 유실 주입).
        /// </summary>
        // ------------------------------------------------------------
        internal Func<AuthorityResult, bool> DropResult;

        internal bool CreateSessions;
        internal Action BeginFault;
        internal int SpawnsPerUse;
        internal int SpawnLimit = int.MaxValue;

    #endregion

    #region 관측 기록

        internal readonly List<(ulong ExecutionID, AbilityUseOrigin Origin, long Tick)> Started = new();
        internal readonly List<(ulong Key, AbilityUseOrigin Origin, int RequestID, int SpawnIndex)> Spawned = new();
        internal readonly List<ExecutionSession> Sessions = new();
        internal readonly List<Exception> Faults = new();

    #endregion

    #region Owner 등록과 진행

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> Owner를 등록하고 연결 신원(connection)을 Owner에 대응시킨다.
        /// <br/> Owner나 연결이 이미 있거나 같은 Epoch에서 제거된 Owner이면
        /// <br/> 아무것도 만들거나 등록하지 않고 예외를 던진다.
        /// <br/> 조종하는 연결이 자기 Owner를 보는 구성은 Adapter 책임이며,
        /// <br/> 테스트는 필요할 때 그 연결의 Client Observer로 만든다.
        /// </summary>
        // ----------------------------------------------------------------------
        internal AuthorityKit AddOwner
        (
            int owner, string key, float cooldown, int charges, int connection = -1,
            IEnumerable<IAbilityActivationCost<AuthoritySpec>> extraCosts = null,
            Action transition = null
        )
        {
            if (kits.ContainsKey(owner))
            {
                throw new ArgumentException("이미 등록된 Owner입니다.", nameof(owner));
            }

            if (connection >= 0 && connectionToOwner.ContainsKey(connection))
            {
                throw new ArgumentException("이미 등록된 연결입니다.", nameof(connection));
            }

            if (retired.Contains((owner, network.Server.Epoch)))
            {
                throw new ArgumentException("제거된 Owner는 새 Epoch에서만 다시 등록할 수 있습니다.", nameof(owner));
            }

            var kit = new AuthorityKit(key, cooldown, charges, extraCosts, transition);
            kits.Add(owner, kit);
            if (connection >= 0)
            {
                connectionToOwner.Add(connection, owner);
            }

            return kit;
        }

        internal AuthorityKit GetKit(int owner) => kits[owner];

        // ------------------------------------------------------------
        /// <summary>
        /// 등록된 Owner의 Ability State 목록을 반환하며 없으면 null이다.
        /// </summary>
        // ------------------------------------------------------------
        private IReadOnlyList<AbilityState> FindStates(int owner)
        {
            return kits.TryGetValue(owner, out var kit) ? kit.Abilities.States : null;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 등록된 Owner의 Status 컨테이너를 반환하며 없으면 null이다.
        /// </summary>
        // ------------------------------------------------------------
        private StatusContainer<int> FindStatuses(int owner)
        {
            return kits.TryGetValue(owner, out var kit) ? kit.Statuses : null;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> 모든 Owner의 모든 Ability State를 같은 시간만큼 진행한다.
        /// <br/> Revision은 바뀌지 않는다. 진행 뒤 알림이 Owner를 제거해도
        /// <br/> 남은 Owner를 이어서 진행한다.
        /// <br/> 알림 예외는 남은 진행을 막지 않고 모두 마친 뒤 전달한다.
        /// </summary>
        // ------------------------------------------------------------
        internal void Advance(float deltaTime)
        {
            List<Exception> failures = null;
            foreach (var owner in new List<int>(kits.Keys))
            {
                if (!kits.TryGetValue(owner, out var kit)) continue;

                try
                {
                    kit.Abilities.Advance(deltaTime);
                }
                catch (Exception exception)
                {
                    (failures ??= new List<Exception>()).Add(exception);
                }

                // Status 시계도 같은 시간만큼 진행한다. 시간 만료는 Revision을 바꾸지 않는다.
                try
                {
                    kit.Statuses.Advance(deltaTime);
                }
                catch (Exception exception)
                {
                    (failures ??= new List<Exception>()).Add(exception);
                }
            }

            AuthorityRig.Rethrow(failures);
        }

    #endregion

    #region Owner 제거

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/> Owner를 제거하고, 그 Owner를 보던 Observer와 조종하던 연결에 관측 사건과
        /// <br/> 같은 경로로 제거를 알린 뒤 모든 Observer의 가시 목록에서 뺀다.
        /// </summary>
        // --------------------------------------------------------------------------------
        internal void Despawn(int owner)
        {
            var kit = kits[owner];
            kits.Remove(owner);
            Replication.ClearExecution(owner);

            // 재등록 금지는 현재 세대에만 적용되므로 지난 세대의 제거 기록은 버린다.
            retired.RemoveWhere(item => item.Epoch != network.Server.Epoch);
            retired.Add((owner, network.Server.Epoch));
            var failures = EndSessions(owner);
            try
            {
                // Session 종료 콜백이 Kit를 쓸 수 있으므로 Session을 끝낸 뒤 Binding을 해제한다.
                kit.Dispose();
            }
            catch (Exception exception)
            {
                (failures ??= new List<Exception>()).Add(exception);
            }

            var connections = ForgetRequests(owner);
            var deliveries = Replication.RemoveOwner(owner, connections);
            if (deliveries != null)
            {
                (failures ??= new List<Exception>()).AddRange(deliveries);
            }

            // 종료 콜백, Binding 해제와 통지 전달의 예외는 제거 정리를 모두 마친 뒤에 호출자에게 전달한다.
            AuthorityRig.Rethrow(failures);
        }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/> 제거된 Owner가 시작한 Session 중 아직 활성인 것을 Cancelled로 종료한다.
        /// <br/> 한 Session의 종료 예외가 나머지 종료를 막지 않도록 모아서 반환한다.
        /// </summary>
        // --------------------------------------------------------------------------------
        private List<Exception> EndSessions(int owner)
        {
            List<Exception> failures = null;
            foreach (var owned in ownedSessions)
            {
                if (owned.Owner != owner || !owned.Session.IsActive)
                {
                    continue;
                }

                try
                {
                    owned.Session.End(ExecutionEndReason.Cancelled);
                }
                catch (Exception exception)
                {
                    (failures ??= new List<Exception>()).Add(exception);
                }
            }

            return failures;
        }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/> 제거된 Owner에 대응된 연결과 요청 기록을 지우고 지운 연결을 반환한다.
        /// <br/> 이후 도착한 요청은 알 수 없는 Owner로 거부되고 같은 연결을 다시 등록할 수 있다.
        /// </summary>
        // --------------------------------------------------------------------------------
        private List<int> ForgetRequests(int owner)
        {
            var connections = new List<int>();
            foreach (var pair in connectionToOwner)
            {
                if (pair.Value == owner)
                {
                    connections.Add(pair.Key);
                }
            }

            foreach (var connection in connections)
            {
                connectionToOwner.Remove(connection);
            }

            var keys = new List<(int Owner, int Epoch)>();
            foreach (var key in ledgers.Keys)
            {
                if (key.Owner == owner)
                {
                    keys.Add(key);
                }
            }

            foreach (var key in keys)
            {
                ledgers.Remove(key);
            }

            return connections;
        }

    #endregion

    #region 수신

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> 도착한 메시지를 처리한 뒤 변경분을 복제한다. 합류 통지는 세션 재동기화로,
        /// <br/> 요청은 검증·실행 뒤 결과를 요청한 연결에만 보낸다. 한 메시지의 처리 실패는
        /// <br/> 같은 Tick의 나머지 메시지와 복제를 막지 않고 모두 마친 뒤 전달한다.
        /// </summary>
        // ----------------------------------------------------------------------
        internal void Pump()
        {
            List<Exception> failures = null;
            var received = network.Server.Received;
            while (processed < received.Count)
            {
                var message = received[processed++];
                try
                {
                    if (message.Payload.SessionReady)
                    {
                        Rejoin(message.Connection, message.Epoch);
                    }
                    else if (message.Payload.Request != null)
                    {
                        Reply(message.Connection, message.Epoch, message.Payload.Request);
                    }
                }
                catch (Exception exception)
                {
                    (failures ??= new List<Exception>()).Add(exception);
                }
            }

            try
            {
                Replication.Replicate();
            }
            catch (Exception exception)
            {
                (failures ??= new List<Exception>()).Add(exception);
            }

            AuthorityRig.Rethrow(failures);
        }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/> 현재 세대에 합류한 연결의 Snapshot 전송 기록을 비우고 Owner 목록을 보낸다.
        /// <br/> 진행 중 실행 재통지가 실패해도 Owner 목록은 보낸다.
        /// <br/> 앞선 세대나 서버보다 앞선 세대의 합류 통지는 무시한다.
        /// </summary>
        // --------------------------------------------------------------------------------
        private void Rejoin(int connection, int epoch)
        {
            if (epoch != network.Server.Epoch) return;

            // 합류한 Client에는 이전에 보낸 Snapshot이나 제거 통지가 도착했다고 가정하지 않는다.
            try
            {
                Replication.ResyncClientObservers(connection);
            }
            finally
            {
                var owners = AuthorityPacket.ForOwners(new List<int>(kits.Keys));
                network.Send(network.Server, network.Connect(connection), owners, ObservationTicks, ordered: true);
            }
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> 요청을 처리하고 결과를 요청한 연결에만 보낸다. 실행되지 않은 결과이면
        /// <br/> 그 연결이 조종하는 Owner의 권한 State를 다음 복제에서 다시 받게 한다.
        /// </summary>
        // ----------------------------------------------------------------------
        private void Reply(int connection, int epoch, AuthorityRequest request)
        {
            var result = Handle(epoch, connection, request);
            var drop = DropResult?.Invoke(result) == true;
            network.Send
            (
                network.Server, network.Connect(connection), AuthorityPacket.ForResult(result),
                ReplyDelay, ResultCopies, drop
            );

            // 실행되지 않은 요청은 서버 State를 바꾸지 않아 Snapshot이 가지 않으므로, Client가
            // 복원하지 않은 예측 소비(Stale, 가장 오래되지 않은 거절)를 권한 값으로 정정하게 한다.
            // 이전 세대 요청은 Client가 이미 세대 전환으로 정리했으므로 다시 보내지 않는다.
            var effective = result.Original ?? result;
            if (effective.Status != AuthorityStatus.Accepted
                && result.Status != AuthorityStatus.StaleEpoch
                && connectionToOwner.TryGetValue(connection, out var owner))
            {
                Replication.ResendOwner(connection, owner);
            }
        }

    #endregion

    #region 요청 검증과 장부

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/> 명세의 검사 순서(Epoch → 연결과 Owner 대응 → 장부 창 → 지난 ID →
        /// <br/> 형식 → 재선택)로 요청을 처리한다. 연결 신원은 전송 계층이 붙인 값이다.
        /// <br/> 실행되지 않은 채 예외로 끝난 요청은 거절로 기록하고 예외는 Faults에 남긴다.
        /// </summary>
        // --------------------------------------------------------------------------------
        private AuthorityResult Handle(int epoch, int connection, AuthorityRequest request)
        {
            if (epoch != network.Server.Epoch)
            {
                return new AuthorityResult(request.RequestID, epoch, AuthorityStatus.StaleEpoch, "이전 Epoch의 요청입니다.");
            }

            if (!connectionToOwner.TryGetValue(connection, out var owner) || owner != request.Owner)
            {
                return new AuthorityResult(request.RequestID, epoch, AuthorityStatus.UnknownOwner, "연결과 Owner가 일치하지 않습니다.");
            }

            var key = (owner, epoch);
            if (ledgers.TryGetValue(key, out var ledger))
            {
                if (ledger.Results.TryGetValue(request.RequestID, out var previous))
                {
                    return new AuthorityResult
                    (
                        request.RequestID, epoch, AuthorityStatus.Duplicate, "재전송된 요청입니다.", original: previous
                    );
                }

                if (request.RequestID <= ledger.Last)
                {
                    return new AuthorityResult(request.RequestID, epoch, AuthorityStatus.Stale, "이미 지나간 Request ID입니다.");
                }
            }

            AuthorityResult result;
            try
            {
                result = Execute(owner, request.RequestID, epoch, request.Entry, request.Payload);
            }
            catch (Exception exception)
            {
                // 시작 실패나 시작 전 예외는 비용이 환급된 채 실행되지 않았으므로 거절로 기록해
                // Client 예측이 결과를 받고 재전송도 같은 결과를 받게 한다.
                Faults.Add(exception);
                result = new AuthorityResult(request.RequestID, epoch, AuthorityStatus.Rejected, exception.Message);
            }

            // 실행 중 Owner가 제거됐으면 장부도 지워졌으므로 다시 만들지 않는다.
            // 이후 요청은 연결 대응이 없어 알 수 없는 Owner로 거부된다.
            if (kits.ContainsKey(owner))
            {
                Record(key, result);
            }

            return result;
        }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/> 처리한 요청의 결과를 장부 창에 넣고 마지막 Request ID를 올린다.
        /// <br/> 창 크기를 넘으면 가장 오래된 결과부터 버리고, 같은 Owner의 지난 세대 장부는 지운다.
        /// </summary>
        // --------------------------------------------------------------------------------
        private void Record((int Owner, int Epoch) key, AuthorityResult result)
        {
            if (!ledgers.TryGetValue(key, out var ledger))
            {
                foreach (var stale in new List<(int Owner, int Epoch)>(ledgers.Keys))
                {
                    if (stale.Owner == key.Owner)
                    {
                        ledgers.Remove(stale);
                    }
                }

                ledger = new RequestLedger(Math.Max(1, RequestWindow));
                ledgers.Add(key, ledger);
            }

            // 같은 Request ID는 Handle이 Duplicate로 먼저 걸러 새 ID만 들어온다. 가득 차면 가장 오래된 결과가 밀려난다.
            ledger.Last = result.RequestID;
            ledger.Results.TryAdd(result.RequestID, result);
        }

    #endregion

    #region 서버 재선택과 실행

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/> 요청 경로(Epoch·연결·장부 검사)를 거치지 않고 Owner의 기본 Entry를 현재 세대에서
        /// <br/> 바로 실행한다. 실행·관측·제거 규칙만 따로 검증하는 테스트용 진입점이다.
        /// </summary>
        // --------------------------------------------------------------------------------
        internal AuthorityResult ExecuteDirect(int owner, int requestID) =>
            Execute(owner, requestID, network.Server.Epoch, AuthorityKit.EntryName, 1);

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/> 서버 State로 다시 선택하고 TryActivate로 한 번 사용한다. begin 뒤의 전이 예외와
        /// <br/> 사용이 확정된 뒤의 시작 사건 전달 실패는 Faults에 남기고 처리된 요청으로 기록한다.
        /// </summary>
        // --------------------------------------------------------------------------------
        private AuthorityResult Execute(int owner, int requestID, int epoch, string entry, object payload)
        {
            if (!(payload is int))
            {
                return new AuthorityResult(requestID, epoch, AuthorityStatus.BadRequest, "Payload 데이터 형식이 맞지 않습니다.");
            }

            if (string.IsNullOrWhiteSpace(entry))
            {
                return new AuthorityResult(requestID, epoch, AuthorityStatus.BadRequest, "Entry가 비어 있습니다.");
            }

            ulong executionID = 0;
            var origin = default(AbilityUseOrigin);
            ExecutionSession started = null;
            var beginCompleted = false;
            var spawns = new List<(int SpawnIndex, ulong Key)>();
            bool Begin(in AbilityUse<string> use)
            {
                var startedID = nextExecutionID++;
                ExecutionSession session = null;
                try
                {
                    session = CreateSessions ? StartSession(owner, startedID) : null;
                    BeginFault?.Invoke();
                }
                catch
                {
                    // 시작하지 못한 실행은 Session만 정리하고 기록 없이 예외를 그대로 전달한다.
                    DiscardSession(session);
                    throw;
                }

                // 시작에 성공한 실행만 시작 기록에 남긴다. 관측 사건은 사용이 확정된 뒤 보낸다.
                executionID = startedID;
                origin = use.Origin;
                started = session;
                Started.Add((startedID, use.Origin, network.Tick));
                IssueSpawns(use.Origin, requestID, spawns);
                beginCompleted = true;
                return true;
            }

            var reason = string.Empty;
            try
            {
                if (!kits[owner].TryUse(entry, owner, payload, Begin, out var activation, out _))
                {
                    return new AuthorityResult(requestID, epoch, AuthorityStatus.Rejected, activation.Reason);
                }
            }
            catch (Exception exception) when (beginCompleted)
            {
                // 실행이 이미 시작됐으므로 요청은 처리된 것으로 기록해 재전송이 다시 실행하지 않게 한다.
                Faults.Add(exception);
                reason = exception.Message;
            }

            // 전이 중 실행이 끝났거나 Owner가 제거됐으면 진행 중 실행으로 기록하지 않는다.
            if (kits.ContainsKey(owner) && (started == null || started.IsActive))
            {
                try
                {
                    Replication.BeginExecution(owner, origin, executionID, network.Tick);
                }
                catch (Exception exception)
                {
                    // 관측 사건 전달 실패는 확정된 사용과 비용을 되돌리지 않는다.
                    Faults.Add(exception);
                    reason = string.IsNullOrEmpty(reason) ? exception.Message : reason;
                }
            }

            return new AuthorityResult
            (
                requestID, epoch, AuthorityStatus.Accepted, reason,
                executionID, network.Tick, spawns: spawns
            );
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> 권한 Session을 만들어 시작하고 Owner 소유로 기록한다. 실행이 끝나면
        /// <br/> 진행 중 기록을 지우고 AOI 안 Observer에 종료 사유를 그대로 알린다.
        /// </summary>
        // ----------------------------------------------------------------------
        private ExecutionSession StartSession(int owner, ulong executionID)
        {
            var flow = new FixedTimelineExecutionFlowSpec(10f);
            var session = new ExecutionSession
            (
                executionID, new ExecutionContext(owner, owner), flow.CreateRuntime(),
                maximumTime: flow.MaximumDuration
            );
            session.Begin();
            Sessions.Add(session);
            ownedSessions.Add((owner, session));
            session.OnEnded += (_, endReason) => Replication.EndExecution(owner, executionID, endReason);
            return session;
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> 시작하지 못한 실행의 Session을 기록에서 빼고 Cancelled로 종료한다.
        /// <br/> 정리 중 종료 콜백 예외는 원래 시작 예외를 가리지 않게 삼킨다.
        /// </summary>
        // ----------------------------------------------------------------------
        private void DiscardSession(ExecutionSession session)
        {
            if (session == null) return;

            Sessions.Remove(session);
            ownedSessions.RemoveAll(item => ReferenceEquals(item.Session, session));
            if (!session.IsActive) return;

            try
            {
                session.End(ExecutionEndReason.Cancelled);
            }
            catch
            {
                // NONE
            }
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 시작한 실행의 Spawn Key를 정해진 수만큼 발급해 기록한다.
        /// </summary>
        // ------------------------------------------------------------
        private void IssueSpawns
        (
            AbilityUseOrigin origin, int requestID, List<(int SpawnIndex, ulong Key)> spawns
        )
        {
            for (var index = 0; index < SpawnsPerUse && index < SpawnLimit; index++)
            {
                var spawn = nextSpawnKey++;
                Spawned.Add((spawn, origin, requestID, index));
                spawns.Add((index, spawn));
            }
        }

    #endregion

    }
}
