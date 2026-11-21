/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : IMotorHistoricalBackend.cs
수정일 : 2026-11-21

# 설명
과거 Owned State를 재현할 수 있는 Backend의 선택적 계약을 정의한다.

# 특이사항, 제약사항
외부 World History의 일관성 검사는 Backend가 맡는다.
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
    /// <br/> 과거 상태를 재현할 수 있는 Backend의 선택적 계약입니다.
    /// <br/> 외부 World History의 일관성 검사는 Backend가 담당합니다.
    /// </summary>
    // ============================================================
    public interface IMotorHistoricalBackend<TState> where TState : unmanaged
    {
        // ------------------------------------------------------------
        /// <summary>
        /// 재실행에 필요한 Owned State를 읽습니다.
        /// </summary>
        // ------------------------------------------------------------
        TState CaptureHistoricalState();

        // ------------------------------------------------------------
        /// <summary>
        /// 과거 World 조건이 유효할 때만 Owned State를 복원합니다.
        /// </summary>
        // ------------------------------------------------------------
        bool TryRestoreHistoricalState(long step, in TState state);
    }
}
