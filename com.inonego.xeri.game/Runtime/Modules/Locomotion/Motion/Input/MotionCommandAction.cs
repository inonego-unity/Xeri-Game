/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : MotionCommandAction.cs
수정일 : 2026-11-21

# 설명
Override·제어 배율·외부 속도 명령의 동작(Set·Remove·Clear)을 정의한다.

# 특이사항, 제약사항
세 명령 구조체가 함께 쓴다.
========================================================================= BLOCK_HEADER_END */

using System;

using UnityEngine;

using inonego;
using inonego.Xeri;
using inonego.Xeri.Serializable;

namespace inonego.Xeri.Game.Motion
{
    // ============================================================
    /// <summary>
    /// Override·배율·외부 속도 명령의 동작.
    /// </summary>
    // ============================================================
    public enum MotionCommandAction { Set, Remove, Clear }
}
