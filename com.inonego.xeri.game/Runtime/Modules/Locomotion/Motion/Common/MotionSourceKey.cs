/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : MotionSourceKey.cs
수정일 : 2026-11-25

# 설명
Actor별 Modifier 항목의 키와 수명 세대를 정의한다.

# 특이사항, 제약사항
키는 사용처가 정한 문자열(예: "Dash")이며 그대로 채널 MValue의 Modifier 키가 된다. 비교는 서수 비교다.
같은 키에 더 큰 Generation이 오면 이전 항목을 대신하고, 같은 Generation은 갱신, 작은 Generation은 거절된다.
========================================================================= BLOCK_HEADER_END */

using System;

using inonego;
using inonego.Xeri;

namespace inonego.Xeri.Game.Motion
{
    // ============================================================
    /// <summary>
    /// Actor별 Modifier 항목의 키와 수명 세대.
    /// </summary>
    // ============================================================
    public readonly struct MotionSourceKey : IEquatable<MotionSourceKey>
    {

    #region 필드

        // ------------------------------------------------------------
        /// <summary>
        /// Actor 안에서 항목을 구분하는 키.
        /// </summary>
        // ------------------------------------------------------------
        public string Key { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 같은 키의 수명 세대.
        /// </summary>
        // ------------------------------------------------------------
        public Revision Generation { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 기본값이 아닌 유효한 식별자인지 나타낸다.
        /// </summary>
        // ------------------------------------------------------------
        public bool IsValid => !string.IsNullOrEmpty(Key) && !Generation.IsNone;

    #endregion

    #region 생성자

        // ------------------------------------------------------------
        /// <summary>
        /// 키와 Generation을 설정한다.
        /// </summary>
        // ------------------------------------------------------------
        public MotionSourceKey(string key, Revision generation)
        {
            if (string.IsNullOrEmpty(key))
            {
                throw new ArgumentException("키가 필요합니다.", nameof(key));
            }

            if (generation.IsNone)
            {
                throw new ArgumentOutOfRangeException(nameof(generation));
            }

            Key = key;
            Generation = generation;
        }

    #endregion

    #region 동등성 비교

        // ------------------------------------------------------------
        /// <summary>
        /// 키와 Generation이 일치하는지 비교한다.
        /// </summary>
        // ------------------------------------------------------------
        public bool Equals(MotionSourceKey other) =>
            string.Equals(Key, other.Key, StringComparison.Ordinal) && Generation == other.Generation;

        // ------------------------------------------------------------
        /// <summary>
        /// 키와 Generation이 일치하는지 비교한다.
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
                return ((Key != null ? StringComparer.Ordinal.GetHashCode(Key) : 0) * 397) ^ Generation.GetHashCode();
            }
        }

    #endregion

    }
}
