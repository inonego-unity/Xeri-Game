/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : MotionSourceKey.cs
수정일 : 2026-11-21

# 설명
Actor별 입력 항목의 슬롯 ID와 수명 세대를 정의한다.

# 특이사항, 제약사항
같은 슬롯에 더 큰 Generation이 오면 이전 항목을 대신하고 작은 Generation은 거절된다.
========================================================================= BLOCK_HEADER_END */

using System;

using inonego;
using inonego.Xeri;

namespace inonego.Xeri.Game.Motion
{
    // ============================================================
    /// <summary>
    /// Actor별 입력 항목의 슬롯과 수명 세대.
    /// </summary>
    // ============================================================
    public readonly struct MotionSourceKey : IEquatable<MotionSourceKey>
    {

    #region 필드

        // ------------------------------------------------------------
        /// <summary>
        /// Actor 내 항목 슬롯 식별자.
        /// </summary>
        // ------------------------------------------------------------
        public long ID { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 항목 슬롯의 수명 세대.
        /// </summary>
        // ------------------------------------------------------------
        public long Generation { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 기본값이 아닌 유효한 식별자인지 나타낸다.
        /// </summary>
        // ------------------------------------------------------------
        public bool IsValid => ID > 0 && Generation > 0;

    #endregion

    #region 생성자

        // ------------------------------------------------------------
        /// <summary>
        /// 항목 ID와 Generation을 설정한다.
        /// </summary>
        // ------------------------------------------------------------
        public MotionSourceKey(long id, long generation)
        {
            if (id <= 0 || generation <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(id));
            }

            ID = id;
            Generation = generation;
        }

    #endregion

    #region 동등성 비교

        // ------------------------------------------------------------
        /// <summary>
        /// 항목 ID와 Generation이 일치하는지 비교한다.
        /// </summary>
        // ------------------------------------------------------------
        public bool Equals(MotionSourceKey other) => ID == other.ID && Generation == other.Generation;

        // ------------------------------------------------------------
        /// <summary>
        /// 항목 ID와 Generation이 일치하는지 비교한다.
        /// </summary>
        // ------------------------------------------------------------
        public override bool Equals(object obj) => obj is MotionSourceKey key && Equals(key);

        // ------------------------------------------------------------
        /// <summary>
        /// 항목 식별자의 해시값을 계산한다.
        /// </summary>
        // ------------------------------------------------------------
        public override int GetHashCode()
        {
            unchecked
            {
                return (ID.GetHashCode() * 397) ^ Generation.GetHashCode();
            }
        }

    #endregion

    }
}
