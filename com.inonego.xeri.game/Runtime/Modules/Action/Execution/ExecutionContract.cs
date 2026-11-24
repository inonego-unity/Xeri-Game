/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : ExecutionContract.cs
수정일 : 2026-11-19

# 설명
Execution의 종료 사유, 관찰용 Signal, 결과가 있는 Command와 실행 Context, 실행 Mode를 정의한다.
Context는 구조화된 owner/instigator 참조와 실행 mode만 보유하고 named primitive 변수는 Session ValueRegistry가 소유한다.
========================================================================= BLOCK_HEADER_END */

using System;

namespace inonego.Xeri.Game
{
    // ============================================================
    /// <summary>
    /// 한 ExecutionSession이 종료된 의미를 나타낸다.
    /// </summary>
    // ============================================================
    public enum ExecutionEndReason
    {
        None = 0,
        Completed = 1,
        Cancelled = 2,
        Interrupted = 3,
        Replaced = 4,
    }

    // ======================================================================
    /// <summary>
    /// 실행 중 Behavior와 Trigger가 관찰하는 project-defined Signal.
    /// </summary>
    // ======================================================================
    public readonly struct ExecutionSignal : IEquatable<ExecutionSignal>
    {

    #region 키 및 생성자

        // ------------------------------------------------------------
        /// <summary>
        /// 제어 Signal을 식별하는 키.
        /// </summary>
        // ------------------------------------------------------------
        public string Key { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 비어 있지 않은 유효한 Signal인지 확인한다.
        /// </summary>
        // ------------------------------------------------------------
        public bool IsValid => !string.IsNullOrWhiteSpace(Key);

        // ------------------------------------------------------------
        /// <summary>
        /// 공백을 정리한 유효한 Signal 키를 보관한다.
        /// </summary>
        // ------------------------------------------------------------
        public ExecutionSignal(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("Execution Signal Key가 비어 있습니다.", nameof(key));
            }

            Key = key.Trim();
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Signal 키를 문자열로 반환하며 키가 없으면 빈 문자열을 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        public override string ToString() => Key ?? string.Empty;

    #endregion

    #region 동등성

        // ------------------------------------------------------------
        /// <summary>
        /// 다른 Signal과 키 값이 같은지 비교한다.
        /// </summary>
        // ------------------------------------------------------------
        public bool Equals(ExecutionSignal other) => string.Equals(Key, other.Key, StringComparison.Ordinal);

        // ------------------------------------------------------------
        /// <summary>
        /// object가 같은 키의 Signal인지 비교한다.
        /// </summary>
        // ------------------------------------------------------------
        public override bool Equals(object obj) => obj is ExecutionSignal other && Equals(other);

        // ------------------------------------------------------------
        /// <summary>
        /// 키 값을 기준으로 해시 코드를 계산한다.
        /// </summary>
        // ------------------------------------------------------------
        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Key ?? string.Empty);

        // ------------------------------------------------------------
        /// <summary>
        /// 두 Signal의 키 값이 같은지 비교한다.
        /// </summary>
        // ------------------------------------------------------------
        public static bool operator ==(ExecutionSignal left, ExecutionSignal right) => left.Equals(right);

        // ------------------------------------------------------------
        /// <summary>
        /// 두 Signal의 키 값이 다른지 비교한다.
        /// </summary>
        // ------------------------------------------------------------
        public static bool operator !=(ExecutionSignal left, ExecutionSignal right) => !left.Equals(right);

    #endregion

    }

    // ============================================================
    /// <summary>
    /// 실행 명령의 판정 결과를 구분한다.
    /// </summary>
    // ============================================================
    public enum ExecutionCommandResult
    {
        Rejected = 0,
        Accepted = 1,
        Deferred = 2,
    }

    // ============================================================
    /// <summary>
    /// Flow가 수락 여부를 판단하는 실행 제어 명령.
    /// </summary>
    // ============================================================
    public readonly struct ExecutionCommand
    {

        // ------------------------------------------------------------
        /// <summary>
        /// 명령을 식별하는 키.
        /// </summary>
        // ------------------------------------------------------------
        public string Key { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 명령 키의 유효성.
        /// </summary>
        // ------------------------------------------------------------
        public bool IsValid => !string.IsNullOrWhiteSpace(Key);

        // ------------------------------------------------------------
        /// <summary>
        /// 공백을 정규화한 명령 키를 생성한다.
        /// </summary>
        // ------------------------------------------------------------
        public ExecutionCommand(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("Execution Command Key가 비어 있습니다.", nameof(key));
            }

            Key = key.Trim();
        }

    }

    // ============================================================
    /// <summary>
    /// Execution이 권위 실행인지, 예측이나 재시뮬레이션인지 구분한다.
    /// </summary>
    // ============================================================
    public enum ExecutionMode
    {
        Authoritative = 0,
        Predicted = 1,
        Resimulating = 2,
    }

    // ============================================================
    /// <summary>
    /// Execution 시작 시 캡처하는 지속 Owner와 실제 실행 Instigator.
    /// </summary>
    // ============================================================
    public readonly struct ExecutionContext
    {

        // ------------------------------------------------------------
        /// <summary>
        /// 실행의 지속 소유자.
        /// </summary>
        // ------------------------------------------------------------
        public object Owner { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 실제 실행을 발생시킨 주체.
        /// </summary>
        // ------------------------------------------------------------
        public object Instigator { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 실행 Mode. 기본은 권위 실행이다.
        /// </summary>
        // ------------------------------------------------------------
        public ExecutionMode Mode { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 지속 소유자와 실행 주체, 실행 Mode를 묶는다.
        /// </summary>
        // ------------------------------------------------------------
        public ExecutionContext
        (
            object owner,
            object instigator,
            ExecutionMode mode = ExecutionMode.Authoritative
        )
        {
            Owner = owner;
            Instigator = instigator;
            Mode = mode;
        }

    }
}
