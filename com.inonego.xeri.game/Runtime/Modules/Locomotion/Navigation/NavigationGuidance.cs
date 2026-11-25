/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : NavigationGuidance.cs
수정일 : 2026-11-25

# 설명
다음 Waypoint를 소비자에게 알리는 Guidance를 정의한다.

# 특이사항, 제약사항
이동 속도·Direction·Body 쓰기는 포함하지 않는다. Actor·Goal Generation·PathGeneration·Waypoint Index로 지연 통지를 구별한다.
========================================================================= BLOCK_HEADER_END */

using System;
using System.Collections;
using System.Collections.Generic;

using inonego;
using inonego.Xeri;
using inonego.Xeri.Game;
using inonego.Xeri.Game.Motion;

namespace inonego.Xeri.Game.Navigation
{
    // ============================================================
    /// <summary>
    /// <br/> 다음 Waypoint를 소비자에게 알리는 Guidance입니다.
    /// <br/> 이동 속도·Direction·Body 쓰기는 포함하지 않습니다.
    /// </summary>
    // ============================================================
    public readonly struct NavigationGuidance<TPoint> where TPoint : unmanaged
    {
        // ------------------------------------------------------------
        /// <summary>
        /// Guidance를 소유하는 Actor입니다.
        /// </summary>
        // ------------------------------------------------------------
        public MotionActorKey Actor { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// Guidance의 목표 Generation입니다.
        /// </summary>
        // ------------------------------------------------------------
        public Revision GoalGeneration { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 같은 Goal 내 재계획·Restore도 구별하는 경로 ID입니다.
        /// </summary>
        // ------------------------------------------------------------
        public Revision PathGeneration { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 현재 Waypoint의 인덱스입니다.
        /// </summary>
        // ------------------------------------------------------------
        public int WaypointIndex { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 현재 Waypoint의 위치입니다.
        /// </summary>
        // ------------------------------------------------------------
        public TPoint Target { get; }

        internal NavigationGuidance
        (
            MotionActorKey actor, Revision goalGeneration, Revision pathGeneration,
            int index, in TPoint target
        )
        {
            Actor = actor;
            GoalGeneration = goalGeneration;
            PathGeneration = pathGeneration;
            WaypointIndex = index;
            Target = target;
        }
    }
}
