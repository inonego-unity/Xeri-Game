/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : BoardSpace.cs
수정일 : 2026-05-01

# 설명
정수 인덱스 기반 보드 공간. BoardSpaceBase<int, TPlaceable>의 기본 구현체.
========================================================================= BLOCK_HEADER_END */

using System;

using UnityEngine;

namespace inonego.Xeri.Game
{
    // ============================================================
    /// <summary>
    /// 정수 인덱스 기반 보드 공간입니다.
    /// </summary>
    // ============================================================
    [Serializable]
    public class BoardSpace<TPlaceable> : BoardSpaceBase<int, TPlaceable>
    where TPlaceable : class
    {
        // NONE
    }
}
