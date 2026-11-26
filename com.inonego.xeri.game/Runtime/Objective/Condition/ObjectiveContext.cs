/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : ObjectiveContext.cs
수정일 : 2026-11-26

# 설명
Task 및 Entry Eligibility Condition에 전달할 Objective 평가 Context.

# 특이사항, 제약사항
Progress는 복사하지 않고 조회 전용 뷰로 받는다. 한 평가의 모든 Condition이 같은 값을 읽는다.
Context가 보는 Registry는 소유자(ObjectiveInstance)가 이후 쓰기 때 복사해 바꾸지 않는다(쓰기 시 복사).
호출자는 쓰기 가능한 인터페이스로 형 변환하지 않는다.
Entry 참여 시각은 Context 생성 때 복사한 값이다.
다른 Objective와 이름 붙은 Fact 소스는 Directory로 조회하며, 읽은 대상은 평가의 읽기 집합에 기록한다.
외부 World/Dialogue Fact는 각 Domain Owner의 조회 계약을 사용한다.
========================================================================= BLOCK_HEADER_END */

using System;
using System.Collections;
using System.Collections.Generic;

using inonego;
using inonego.Xeri;
using inonego.Xeri.Game;
using inonego.Xeri.Serializable;

namespace inonego.Xeri.Game.Objective
{
    // ============================================================
    /// <summary>
    /// Progress와 시간·상태를 노출하는 일회성 판정 Context.
    /// </summary>
    // ============================================================
    public sealed class ObjectiveContext
    {

    #region 프로퍼티

        // ----------------------------------------------------------------------
        /// <summary>
        /// 현재 평가 회차가 읽는 Progress의 조회 전용 뷰. 평가 동안 값이 바뀌지 않는다.
        /// </summary>
        // ----------------------------------------------------------------------
        public IValueLookup Progress { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 현재 실행의 누적 경과 시간.
        /// </summary>
        // ------------------------------------------------------------
        public double ElapsedTime { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 현재 실행의 영속 상태.
        /// </summary>
        // ------------------------------------------------------------
        public ObjectiveStatus Status { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 현재 실행이 속한 Scope의 ID. 직접 만든 Context는 null이다.
        /// </summary>
        // ------------------------------------------------------------
        public string ScopeID { get; }

        private readonly IReadOnlyDictionary<string, double> openTimes;
        private readonly ObjectiveDirectory directory;
        private readonly HashSet<(string Scope, string Name)> reads;

    #endregion

    #region 생성자

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/>같은 평가 시점의 Progress 뷰와 시간·상태로 Context를 만든다.
        /// <br/>Progress는 복사하지 않으므로 호출자는 이 Context가 쓰이는 동안 그 Registry를
        /// <br/>제자리에서 바꾸지 않아야 한다.
        /// </summary>
        // --------------------------------------------------------------------------------
        public ObjectiveContext(IValueLookup progress, double elapsedTime, ObjectiveStatus status)
            : this(progress, elapsedTime, status, null, null, null, null)
        {
            // NONE
        }

        internal ObjectiveContext
        (
            IValueLookup progress, double elapsedTime, ObjectiveStatus status, string scopeID,
            IReadOnlyDictionary<string, double> openTimes, ObjectiveDirectory directory,
            HashSet<(string Scope, string Name)> reads
        )
        {
            Progress = progress ?? throw new ArgumentNullException(nameof(progress));
            if (double.IsNaN(elapsedTime) || double.IsInfinity(elapsedTime) || elapsedTime < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(elapsedTime));
            }

            if (!status.IsDefined())
            {
                throw new ArgumentOutOfRangeException(nameof(status));
            }

            ElapsedTime = elapsedTime;
            Status = status;
            ScopeID = scopeID;
            this.openTimes = openTimes;
            this.directory = directory;
            this.reads = reads;
        }

    #endregion

    #region 조회

        // ------------------------------------------------------------
        /// <summary>
        /// <br/>Entry가 참여를 시작한 뒤 흐른 시간을 조회한다.
        /// <br/>시간을 재도록 선언한 Entry가 지금 참여 중일 때만 true다.
        /// </summary>
        // ------------------------------------------------------------
        public bool TryGetOpenElapsed(string entryID, out double elapsed)
        {
            if (entryID != null && openTimes != null && openTimes.TryGetValue(entryID, out double opened))
            {
                elapsed = ElapsedTime - opened;
                return true;
            }

            elapsed = 0;
            return false;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 같은 Scope에서 Spec ID로 현재 회차의 다른 Objective를 조회한다.
        /// </summary>
        // ------------------------------------------------------------
        public bool TryGetObjective(string specID, out ObjectiveInstance instance) =>
            TryGetObjective(ScopeID, specID, out instance);

        // ------------------------------------------------------------
        /// <summary>
        /// <br/>Scope와 Spec ID로 현재 회차의 다른 Objective를 조회한다.
        /// <br/>조회한 대상은 읽기 집합에 기록한다.
        /// </summary>
        // ------------------------------------------------------------
        public bool TryGetObjective(string scopeID, string specID, out ObjectiveInstance instance)
        {
            if (scopeID != null && specID != null)
            {
                reads?.Add((scopeID, specID));
            }

            if (directory != null)
            {
                return directory.TryGetInstance(scopeID, specID, out instance);
            }

            instance = null;
            return false;
        }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/>이름으로 등록된 Fact 소스의 조회 전용 뷰를 얻는다. 읽은 소스는 읽기 집합에 기록한다.
        /// <br/>등록되지 않았으면 false로 위장하지 않고 예외로 알린다.
        /// </summary>
        // --------------------------------------------------------------------------------
        public IValueLookup Source(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("Fact source name must be specified.", nameof(name));
            }

            if (directory == null || !directory.TryGetSource(name, out var source))
            {
                throw new InvalidOperationException($"Fact source '{name}' is not registered.");
            }

            reads?.Add((null, name));
            return source;
        }

    #endregion

    }
}
