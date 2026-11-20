/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : AuthorityMessages.cs
수정일 : 2026-11-19

# 설명
권한 사용 프로토콜(L1)의 테스트 전용 요청·결과·Snapshot·실행 관측·AOI 이탈·제거 통지·Owner 목록 메시지와 결과 분류를 제공한다.
요청에는 보낸 연결을 적지 않는다. 연결 신원은 전송 계층이 메시지에 붙인다.
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

    // ============================================================
    /// <summary>
    /// 원격 Observer에게 알리는 실행 관측 사건의 종류이다.
    /// </summary>
    // ============================================================
    internal enum AuthorityObservation
    {
        None,
        Started,
        InProgress,
        Ended,
    }

    // ============================================================
    /// <summary>
    /// Client가 보내는 사용 의도이다. 보낸 연결은 적지 않는다.
    /// </summary>
    // ============================================================
    internal sealed class AuthorityRequest
    {
        internal readonly int RequestID;
        internal readonly int Owner;
        internal readonly string Entry;
        internal readonly long ObservedTick;
        internal readonly object Payload;

        internal AuthorityRequest(int requestID, int owner, string entry, long observedTick, object payload)
        {
            RequestID = requestID;
            Owner = owner;
            Entry = entry;
            ObservedTick = observedTick;
            Payload = payload;
        }
    }

    // ============================================================
    /// <summary>
    /// 권한 Host가 요청한 연결에만 돌려주는 처리 결과이다.
    /// </summary>
    // ============================================================
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

    // ======================================================================
    /// <summary>
    /// 서버 Ability 하나의 State 복사본과 원본 Revision, 복사한 Tick을 보관한다.
    /// </summary>
    // ======================================================================
    internal sealed class AuthoritySnapshot
    {
        internal readonly AbilityKey Key;
        internal readonly AbilityState State;
        internal readonly ulong Revision;
        internal readonly long Tick;

        internal AuthoritySnapshot(AbilityState source, long tick)
        {
            Key = source.Key;
            State = source.Clone();
            Revision = source.Revision;
            Tick = tick;
        }
    }

    // ======================================================================
    /// <summary>
    /// <br/> 서버와 Client가 주고받는 메시지이다. Owner 관련 메시지(Snapshot,
    /// <br/> 실행 관측, AOI 이탈, 제거 통지)는 Owner 필드로 대상 Owner를 가리킨다.
    /// </summary>
    // ======================================================================
    internal sealed class AuthorityPacket
    {

    #region 요청·결과·세션

        internal AuthorityRequest Request;
        internal AuthorityResult Result;
        internal bool SessionReady;
        internal IReadOnlyList<int> Owners;

        internal static AuthorityPacket ForRequest(AuthorityRequest request) =>
            new AuthorityPacket
        {
            Request = request
        };

        internal static AuthorityPacket ForResult(AuthorityResult result) =>
            new AuthorityPacket
        {
            Result = result
        };

        internal static AuthorityPacket ForSessionReady() =>
            new AuthorityPacket
        {
            SessionReady = true
        };

        internal static AuthorityPacket ForOwners(IReadOnlyList<int> owners) =>
            new AuthorityPacket
        {
            Owners = owners
        };

    #endregion

    #region 관측 사건

        internal int Owner;
        internal bool HasSnapshot;
        internal AuthoritySnapshot Snapshot;
        internal bool Despawned;
        internal bool Left;
        internal AuthorityObservation Observation;
        internal AbilityUseOrigin Origin;
        internal ulong ExecutionID;
        internal long StartTick;
        internal ExecutionEndReason EndReason;

        internal static AuthorityPacket ForSnapshot(int owner, AuthoritySnapshot snapshot) =>
            new AuthorityPacket
        {
            Owner = owner, HasSnapshot = true, Snapshot = snapshot
        };

        internal static AuthorityPacket ForStarted(int owner, AbilityUseOrigin origin, ulong executionID, long startTick) =>
            new AuthorityPacket
        {
            Owner = owner, Observation = AuthorityObservation.Started,
            Origin = origin, ExecutionID = executionID, StartTick = startTick
        };

        internal static AuthorityPacket ForInProgress(int owner, AbilityUseOrigin origin, ulong executionID, long startTick) =>
            new AuthorityPacket
        {
            Owner = owner, Observation = AuthorityObservation.InProgress,
            Origin = origin, ExecutionID = executionID, StartTick = startTick
        };

        internal static AuthorityPacket ForEnded(int owner, ulong executionID, ExecutionEndReason reason) =>
            new AuthorityPacket
        {
            Owner = owner, Observation = AuthorityObservation.Ended,
            ExecutionID = executionID, EndReason = reason
        };

        internal static AuthorityPacket ForDespawn(int owner) =>
            new AuthorityPacket
        {
            Owner = owner, Despawned = true
        };

        internal static AuthorityPacket ForLeave(int owner) =>
            new AuthorityPacket
        {
            Owner = owner, Left = true
        };

    #endregion

    }
}
