/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : MotionStepInput.cs
수정일 : 2026-11-22

# 설명
한 Tick에 Model로 넘기는 기본 의도, Modifier 명령, Up 축 묶음을 정의한다.

# 특이사항, 제약사항
ref struct라 필드로 보관할 수 없다. Host는 MotionInputBuffer에 모았다가 AsStepInput으로 묶는다.
========================================================================= BLOCK_HEADER_END */

using System;

using UnityEngine;

using inonego;
using inonego.Xeri;

namespace inonego.Xeri.Game.Motion
{
    // ============================================================
    /// <summary>
    /// 한 Tick의 기본 의도와 Modifier 명령 묶음.
    /// </summary>
    // ============================================================
    public readonly ref struct MotionStepInput
    {

    #region 필드

        // ------------------------------------------------------------
        /// <summary>
        /// 한 Tick 수명의 기본 의도. 채널별로 나중 값이 이긴다.
        /// </summary>
        // ------------------------------------------------------------
        public ReadOnlySpan<MotionIntent> Intents { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// Modifier 명령. 순서대로 적용한다.
        /// </summary>
        // ------------------------------------------------------------
        public ReadOnlySpan<MotionModifierCommand> Modifiers { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 이번 Tick의 Up 축. 영벡터면 Model의 현재 Up을 유지한다.
        /// </summary>
        // ------------------------------------------------------------
        public Vector3 Up { get; }

    #endregion

    #region 생성자

        // ------------------------------------------------------------
        /// <summary>
        /// 기본 의도, Modifier 명령, Up 축으로 입력을 묶는다.
        /// </summary>
        // ------------------------------------------------------------
        public MotionStepInput
        (
            ReadOnlySpan<MotionIntent> intents = default,
            ReadOnlySpan<MotionModifierCommand> modifiers = default,
            Vector3 up = default
        )
        {
            Intents = intents;
            Modifiers = modifiers;
            Up = up;
        }

    #endregion

    }
}
