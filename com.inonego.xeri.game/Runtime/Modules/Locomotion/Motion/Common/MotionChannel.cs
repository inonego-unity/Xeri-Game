/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : MotionChannel.cs
수정일 : 2026-11-21

# 설명
월드 좌표축에 의존하지 않는 운동 채널(Planar·Elevation·Rotation)을 정의한다.

# 특이사항, 제약사항
Flags 열거형이다. None과 정의되지 않은 비트는 입력 검증에서 거절한다.
========================================================================= BLOCK_HEADER_END */

using System;

using inonego;
using inonego.Xeri;

namespace inonego.Xeri.Game.Motion
{
    // ============================================================
    /// <summary>
    /// 월드 좌표축에 의존하지 않는 운동 채널.
    /// </summary>
    // ============================================================
    [Flags]
    public enum MotionChannel
    {
        None = 0,
        Planar = 1,
        Elevation = 2,
        Rotation = 4,
        All = Planar | Elevation | Rotation
    }
}
