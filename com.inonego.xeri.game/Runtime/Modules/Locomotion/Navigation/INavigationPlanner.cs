/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : INavigationPlanner.cs
수정일 : 2026-11-25

# 설명
외부 Planner가 소비하는 계약과 그 경로 요청 값(NavigationPlanRequest)을 정의한다.

# 특이사항, 제약사항
비동기 결과는 사용처가 Controller에 다시 전달한다. 늦게 도착한 결과는 요청 ID와 Revision으로 구별한다.
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
    /// 동기·비동기 경로 계산을 제공하는 외부 Planner 계약입니다.
    /// </summary>
    // ============================================================
    public interface INavigationPlanner<TPoint> where TPoint : unmanaged
    {
        // ------------------------------------------------------------
        /// <summary>
        /// 공개 경로 요청을 소비합니다.
        /// </summary>
        // ------------------------------------------------------------
        void Request(in NavigationPlanRequest<TPoint> request);
    }

    // ============================================================
    /// <summary>
    /// <br/> 외부 Planner가 소비하는 경로 요청입니다.
    /// <br/> 늦게 도착한 결과는 요청 ID와 Revision으로 구별합니다.
    /// </summary>
    // ============================================================
    public readonly struct NavigationPlanRequest<TPoint> where TPoint : unmanaged
    {
        // ------------------------------------------------------------
        /// <summary>
        /// 요청을 소유하는 Actor입니다.
        /// </summary>
        // ------------------------------------------------------------
        public MotionActorKey Actor { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 요청 당시 목표 Generation입니다.
        /// </summary>
        // ------------------------------------------------------------
        public Revision GoalGeneration { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// Controller 내에서 재사용하지 않는 요청 번호입니다.
        /// </summary>
        // ------------------------------------------------------------
        public long RequestID { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> 외부 Host가 제공한 World Revision입니다.
        /// <br/> Host는 1부터 발급하며 None은 허용하지 않습니다.
        /// </summary>
        // ------------------------------------------------------------
        public Revision WorldRevision { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 경로 시작 지점입니다.
        /// </summary>
        // ------------------------------------------------------------
        public TPoint Start { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 경로 목표 지점입니다.
        /// </summary>
        // ------------------------------------------------------------
        public TPoint Goal { get; }

        internal NavigationPlanRequest
        (
            MotionActorKey actor, Revision goalGeneration, long requestID,
            Revision worldRevision, in TPoint start, in TPoint goal
        )
        {
            Actor = actor;
            GoalGeneration = goalGeneration;
            RequestID = requestID;
            WorldRevision = worldRevision;
            Start = start;
            Goal = goal;
        }
    }
}
