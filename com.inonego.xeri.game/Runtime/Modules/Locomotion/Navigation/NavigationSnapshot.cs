/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : NavigationSnapshot.cs
수정일 : 2026-11-25

# 설명
Goal과 Path만 포함하는 독립 Owned Snapshot을 정의한다.

# 특이사항, 제약사항
Planner 작업과 Physics World는 복원하지 않는다. 생성은 Controller만 한다.
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
    /// <br/> Goal과 Path만 포함하는 독립 Owned Snapshot입니다.
    /// <br/> Planner 작업 및 Physics World는 복원하지 않습니다.
    /// </summary>
    // ============================================================
    public sealed class NavigationSnapshot<TPoint> where TPoint : unmanaged
    {
        // ------------------------------------------------------------
        /// <summary>
        /// Snapshot 소유 Actor입니다.
        /// </summary>
        // ------------------------------------------------------------
        public MotionActorKey Actor { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 캡처 당시 목표 Generation이며 복원 때 다시 쓰지 않습니다.
        /// </summary>
        // ------------------------------------------------------------
        public Revision GoalGeneration { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 캡처 당시 World Revision이며 없으면 None입니다.
        /// </summary>
        // ------------------------------------------------------------
        public Revision WorldRevision { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 캡처 당시 진행 상태입니다.
        /// </summary>
        // ------------------------------------------------------------
        public NavigationStatus Status { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 현재 목표가 있는지 나타냅니다.
        /// </summary>
        // ------------------------------------------------------------
        public bool HasGoal { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 캡처 당시 목표 지점입니다.
        /// </summary>
        // ------------------------------------------------------------
        public TPoint Goal { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 독립 복사된 Waypoint 목록입니다.
        /// </summary>
        // ------------------------------------------------------------
        public IReadOnlyList<TPoint> Waypoints { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 캡처 당시 다음 Waypoint 인덱스입니다.
        /// </summary>
        // ------------------------------------------------------------
        public int WaypointIndex { get; }

        internal NavigationSnapshot
        (
            MotionActorKey actor, Revision goalGeneration, Revision worldRevision,
            NavigationStatus status, bool hasGoal, in TPoint goal,
            TPoint[] waypoints, int index
        )
        {
            Actor = actor;
            GoalGeneration = goalGeneration;
            WorldRevision = worldRevision;
            Status = status;
            HasGoal = hasGoal;
            Goal = goal;
            Waypoints = Array.AsReadOnly(waypoints);
            WaypointIndex = index;
        }
    }
}
