/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : MotionCommandAction.cs
수정일 : 2026-11-22

# 설명
Modifier 명령의 동작(Apply·End·Clear)을 정의한다.
========================================================================= BLOCK_HEADER_END */

using System;

using UnityEngine;

using inonego;
using inonego.Xeri;

namespace inonego.Xeri.Game.Motion
{
    // ============================================================
    /// <summary>
    /// Modifier 명령의 동작.
    /// </summary>
    // ============================================================
    public enum MotionCommandAction { Apply, End, Clear }
}
