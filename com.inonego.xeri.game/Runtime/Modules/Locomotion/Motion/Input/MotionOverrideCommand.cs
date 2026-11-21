/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : MotionOverrideCommand.cs
수정일 : 2026-11-21

# 설명
아래 층을 가리는 Override의 시작·종료 명령을 정의한다.

# 특이사항, 제약사항
입력은 사용처 Producer가 만드는 unmanaged 값이며 Motion은 Producer의 타입을 참조하지 않는다.
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
    /// 아래 층을 가리는 Override의 시작·종료 명령.
    /// </summary>
    // ============================================================
    public readonly struct MotionOverrideCommand
    {

    #region 필드

        // ------------------------------------------------------------
        /// <summary>
        /// 시작(Set) 또는 종료(Remove).
        /// </summary>
        // ------------------------------------------------------------
        public MotionCommandAction Action { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// Override 식별자.
        /// </summary>
        // ------------------------------------------------------------
        public MotionSourceKey Source { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 가리는 채널.
        /// </summary>
        // ------------------------------------------------------------
        public MotionChannel Channels { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 우선순위. 높을수록 위층이다.
        /// </summary>
        // ------------------------------------------------------------
        public int Priority { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// Track이 없을 때 쓰는 선속도.
        /// </summary>
        // ------------------------------------------------------------
        public Vector3 Linear { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// Track이 없을 때 쓰는 각속도(rad/s).
        /// </summary>
        // ------------------------------------------------------------
        public Vector3 Angular { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 지속 시간(초). 0이면 종료 명령이나 Track 끝까지 유지한다.
        /// </summary>
        // ------------------------------------------------------------
        public float Duration { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 평가할 Track 식별자. 0이면 Track이 없다.
        /// </summary>
        // ------------------------------------------------------------
        public int TrackID { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// Track 재생 속도 배율.
        /// </summary>
        // ------------------------------------------------------------
        public float TrackRate { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 사용처가 정한 동작 식별자. 표현·복제에서 동작을 구분한다.
        /// </summary>
        // ------------------------------------------------------------
        public int ActionID { get; }

    #endregion

    #region 생성

        private MotionOverrideCommand
        (
            MotionCommandAction action, MotionSourceKey source, MotionChannel channels, int priority,
            Vector3 linear, Vector3 angular, float duration, int trackID, float trackRate, int actionID
        )
        {
            Action = action;
            Source = source;
            Channels = channels;
            Priority = priority;
            Linear = linear;
            Angular = angular;
            Duration = duration;
            TrackID = trackID;
            TrackRate = trackRate;
            ActionID = actionID;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 고정 속도 Override를 시작한다.
        /// </summary>
        // ------------------------------------------------------------
        public static MotionOverrideCommand Begin
        (
            MotionSourceKey source, MotionChannel channels, int priority,
            Vector3 linear, Vector3 angular = default, float duration = 0f, int actionID = 0
        ) => new(MotionCommandAction.Set, source, channels, priority, linear, angular, duration, 0, 1f, actionID);

        // ------------------------------------------------------------
        /// <summary>
        /// Track 변위를 평가하는 Override를 시작한다.
        /// </summary>
        // ------------------------------------------------------------
        public static MotionOverrideCommand BeginTrack
        (
            MotionSourceKey source, MotionChannel channels, int priority,
            int trackID, float trackRate = 1f, float duration = 0f, int actionID = 0
        ) => new(MotionCommandAction.Set, source, channels, priority, Vector3.zero, Vector3.zero, duration, trackID, trackRate, actionID);

        // ------------------------------------------------------------
        /// <summary>
        /// Override를 종료한다.
        /// </summary>
        // ------------------------------------------------------------
        public static MotionOverrideCommand End(MotionSourceKey source) =>
            new(MotionCommandAction.Remove, source, MotionChannel.None, 0, Vector3.zero, Vector3.zero, 0f, 0, 0f, 0);

    #endregion

    }
}
