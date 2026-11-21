/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : MotionStepInput.cs
수정일 : 2026-11-21

# 설명
한 Tick에 Model로 넘기는 층별 입력 묶음을 정의한다.

# 특이사항, 제약사항
ref struct라 필드로 보관할 수 없다. Host는 MotionInputBuffer에 모았다가 AsStepInput으로 묶는다.
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
    /// <br/> 한 Tick에 Model로 넘기는 층별 입력 묶음.
    /// <br/> Impulse는 이번 Tick에 한 번 더하는 속도 변화량 합계다.
    /// </summary>
    // ============================================================
    public readonly ref struct MotionStepInput
    {

    #region 필드

        // ------------------------------------------------------------
        /// <summary>
        /// 기본 의도.
        /// </summary>
        // ------------------------------------------------------------
        public ReadOnlySpan<MotionIntent> Intents { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// Override 명령.
        /// </summary>
        // ------------------------------------------------------------
        public ReadOnlySpan<MotionOverrideCommand> Overrides { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 제어 배율 명령.
        /// </summary>
        // ------------------------------------------------------------
        public ReadOnlySpan<MotionScaleCommand> Scales { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 외부 속도 명령.
        /// </summary>
        // ------------------------------------------------------------
        public ReadOnlySpan<MotionExternalCommand> Externals { get; }

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> 이번 Tick에 Body 속도를 한 번 바꾸는 변화량.
        /// <br/> Free 채널에서는 물리가 이어받고 Set 채널에서는 다음 Tick에 덮인다.
        /// </summary>
        // ----------------------------------------------------------------------
        public Vector3 Impulse { get; }

    #endregion

    #region 생성자

        // ------------------------------------------------------------
        /// <summary>
        /// 층별 입력으로 묶음을 만든다. 생략한 층은 비어 있다.
        /// </summary>
        // ------------------------------------------------------------
        public MotionStepInput
        (
            ReadOnlySpan<MotionIntent> intents = default,
            ReadOnlySpan<MotionOverrideCommand> overrides = default,
            ReadOnlySpan<MotionScaleCommand> scales = default,
            ReadOnlySpan<MotionExternalCommand> externals = default,
            Vector3 impulse = default
        )
        {
            Intents = intents;
            Overrides = overrides;
            Scales = scales;
            Externals = externals;
            Impulse = impulse;
        }

    #endregion

    }
}
