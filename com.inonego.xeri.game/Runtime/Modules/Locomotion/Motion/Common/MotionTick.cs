/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : MotionTick.cs
수정일 : 2026-11-21

# 설명
외부 실행이 제공하는 Step Index와 Delta(초)를 정의한다.

# 특이사항, 제약사항
Index는 0 이상, Delta는 유한한 양수여야 한다.
========================================================================= BLOCK_HEADER_END */

using System;

using inonego;
using inonego.Xeri;

namespace inonego.Xeri.Game.Motion
{
    // ============================================================
    /// <summary>
    /// 외부 실행이 제공하는 Step Index와 Delta.
    /// </summary>
    // ============================================================
    public readonly struct MotionTick
    {

    #region 필드

        // ------------------------------------------------------------
        /// <summary>
        /// Simulation Tick 번호.
        /// </summary>
        // ------------------------------------------------------------
        public long Index { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// Host가 제공한 실행 간격(초)입니다.
        /// </summary>
        // ------------------------------------------------------------
        public double Delta { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 기본값이 아닌 유효한 Tick인지 나타낸다.
        /// </summary>
        // ------------------------------------------------------------
        public bool IsValid => Index >= 0 && Delta > 0d && !double.IsInfinity(Delta);

    #endregion

    #region 생성자

        // ------------------------------------------------------------
        /// <summary>
        /// Tick과 Delta의 유효성을 검증한다.
        /// </summary>
        // ------------------------------------------------------------
        public MotionTick(long index, double delta)
        {
            if (index < 0 || double.IsNaN(delta) || double.IsInfinity(delta) || delta <= 0d)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            Index = index;
            Delta = delta;
        }

    #endregion

    }
}
