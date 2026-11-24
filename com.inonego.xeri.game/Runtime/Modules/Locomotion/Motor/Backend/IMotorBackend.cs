/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : IMotorBackend.cs
수정일 : 2026-11-21

# 설명
Motor가 사용하는 실제 Body 입출력 계약을 정의한다.

# 특이사항, 제약사항
Physics Simulation Step은 포함하지 않는다. 유효하지 않은 프레임이나 계산 결과는 아무것도 바꾸지 않고 거절한다.
========================================================================= BLOCK_HEADER_END */

using System;

using UnityEngine;

using inonego;
using inonego.Xeri;
using inonego.Xeri.Game;
using inonego.Xeri.Game.Motion;

namespace inonego.Xeri.Game.Motor
{
    // ============================================================
    /// <summary>
    /// <br/> Motor가 사용하는 실제 Body 입출력 계약입니다.
    /// <br/> Physics Simulation Step은 이 계약에 포함되지 않습니다.
    /// </summary>
    // ============================================================
    public interface IMotorBackend<TState> where TState : unmanaged
    {
        // ------------------------------------------------------------
        /// <summary>
        /// 마지막 Physics Step 이후의 실제 Body 상태를 읽습니다.
        /// </summary>
        // ------------------------------------------------------------
        TState ReadCurrentState();

        // ------------------------------------------------------------
        /// <summary>
        /// 권한 있는 현재 Body 상태를 즉시 적용합니다.
        /// </summary>
        // ------------------------------------------------------------
        void ApplyCurrentState(in TState state);

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> 한 Tick의 합성 결과를 Body에 한 번에 적용합니다.
        /// <br/> 유효하지 않은 프레임이나 계산 결과는 바꾸지 않고 거절합니다.
        /// </summary>
        // ------------------------------------------------------------
        bool TryApply(in MotionFrame frame);
    }
}
