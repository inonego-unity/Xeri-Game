/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : MotorSnapshot.cs
수정일 : 2026-11-21

# 설명
Motor의 과거 Owned State와 복원 범위를 보관하는 값을 정의한다.

# 특이사항, 제약사항
원격 현재 상태의 복제 포맷으로 쓰지 않는다. 생성은 Motor만 한다.
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
    /// <br/> Motor의 과거 Owned State와 복원 범위를 보관합니다.
    /// <br/> 원격의 현재 상태 복제 포맷으로 사용하지 않습니다.
    /// </summary>
    // ============================================================
    public readonly struct MotorSnapshot<TState> where TState : unmanaged
    {

    #region 필드

        // ------------------------------------------------------------
        /// <summary>
        /// Snapshot의 Actor와 로컬 실행 범위입니다.
        /// </summary>
        // ------------------------------------------------------------
        public MotionActorKey Actor { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// Snapshot이 속한 외부 Step Index입니다.
        /// </summary>
        // ------------------------------------------------------------
        public long Step { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 현재 상태 적용 시 갱신되는 History 수명입니다.
        /// </summary>
        // ------------------------------------------------------------
        public long HistoryGeneration { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// Backend가 복원 가능한 것으로 선언한 Owned State입니다.
        /// </summary>
        // ------------------------------------------------------------
        public TState State { get; }

    #endregion

    #region 생성자

        internal MotorSnapshot
        (
            MotionActorKey actor, long step,
            long historyGeneration, in TState state
        )
        {
            Actor = actor;
            Step = step;
            HistoryGeneration = historyGeneration;
            State = state;
        }

    #endregion

    }
}
