/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : MotionActorKey.cs
수정일 : 2026-11-25

# 설명
실행 Scope와 Actor 수명을 식별하는 값을 정의한다.

# 특이사항, 제약사항
Snapshot 소유권 검증에 쓴다. Scope·Actor·Generation이 모두 양수여야 한다.
========================================================================= BLOCK_HEADER_END */

using System;

using inonego;
using inonego.Xeri;

namespace inonego.Xeri.Game.Motion
{
    // ============================================================
    /// <summary>
    /// <br/> 실행 Scope와 Actor 수명을 식별합니다.
    /// <br/> Snapshot 소유권 검증에 사용합니다.
    /// </summary>
    // ============================================================
    public readonly struct MotionActorKey : IEquatable<MotionActorKey>
    {

    #region 필드

        // ------------------------------------------------------------
        /// <summary>
        /// 독립적인 실행 인스턴스의 식별자입니다.
        /// </summary>
        // ------------------------------------------------------------
        public long ScopeID { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 실행 인스턴스 안의 Actor 식별자입니다.
        /// </summary>
        // ------------------------------------------------------------
        public long ActorID { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 해당 Actor의 수명 세대입니다.
        /// </summary>
        // ------------------------------------------------------------
        public Revision Generation { get; }

    #endregion

    #region 생성자

        // ------------------------------------------------------------
        /// <summary>
        /// Actor의 실행 범위와 수명 식별자를 구성합니다.
        /// </summary>
        // ------------------------------------------------------------
        public MotionActorKey(long scopeID, long actorID, Revision generation)
        {
            if (scopeID <= 0 || actorID <= 0 || generation.IsNone)
            {
                throw new ArgumentOutOfRangeException(nameof(scopeID));
            }

            ScopeID = scopeID;
            ActorID = actorID;
            Generation = generation;
        }

    #endregion

    #region 동등성 비교

        // ------------------------------------------------------------
        /// <summary>
        /// 세 식별 요소가 모두 같은지 검사합니다.
        /// </summary>
        // ------------------------------------------------------------
        public bool Equals(MotionActorKey other) =>
            ScopeID == other.ScopeID && ActorID == other.ActorID && Generation == other.Generation;

        // ------------------------------------------------------------
        /// <summary>
        /// 다른 객체의 Actor 식별자가 같은지 검사합니다.
        /// </summary>
        // ------------------------------------------------------------
        public override bool Equals(object obj) => obj is MotionActorKey key && Equals(key);

        // ------------------------------------------------------------
        /// <summary>
        /// Actor 식별자의 해시값을 계산합니다.
        /// </summary>
        // ------------------------------------------------------------
        public override int GetHashCode()
        {
            unchecked
            {
                var hash = ScopeID.GetHashCode();
                hash = (hash * 397) ^ ActorID.GetHashCode();
                return (hash * 397) ^ Generation.GetHashCode();
            }
        }

    #endregion

    }
}
