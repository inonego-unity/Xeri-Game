/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : HP_IF.cs
수정일 : 2026-09-18

# 설명
HP<,> 추상 클래스의 int/float 구체 구현체.
========================================================================= BLOCK_HEADER_END */

using System;

namespace inonego.Xeri.Game
{
    using Primitive;

    // ============================================================
    /// <summary>
    /// int 기반 체력 구현 클래스.
    /// </summary>
    // ============================================================
    [Serializable]
    public sealed class HP_I : HP<XNumericI, int>, IReadOnlyHP_I
    {
        // NONE
    }

    // ============================================================
    /// <summary>
    /// float 기반 체력 구현 클래스.
    /// </summary>
    // ============================================================
    [Serializable]
    public sealed class HP_F : HP<XNumericF, float>, IReadOnlyHP_F
    {
        // NONE
    }

}
