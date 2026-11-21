/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : MotionModelSnapshot.cs
수정일 : 2026-11-22

# 설명
Motion Model의 Modifier 항목 메타데이터와 채널 MValue 상태를 보관하는 Snapshot을 정의한다.

# 특이사항, 제약사항
항목 메타데이터는 값 구조체 배열, 채널 값은 Core MValue 복제(CloneFrom)로 보관한다.
같은 Snapshot을 재사용하고 Modifier 구성이 같으면 Capture·Restore에 할당이 없다.
기본 의도는 한 Tick 수명이라 Snapshot에 넣지 않는다. StepVersion 같은 발급 토큰도 넣지 않는다.
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
    /// Modifier 항목 하나의 메타데이터.
    /// </summary>
    // ============================================================
    internal struct MotionModifierEntry
    {
        // ------------------------------------------------------------
        /// <summary>
        /// Modifier 항목을 낸 출처 키.
        /// </summary>
        // ------------------------------------------------------------
        public MotionSourceKey Source;

        // ------------------------------------------------------------
        /// <summary>
        /// 항목이 적용되는 채널.
        /// </summary>
        // ------------------------------------------------------------
        public MotionChannel Channels;

        // ------------------------------------------------------------
        /// <summary>
        /// 채널 MValue에 등록한 적용 순서.
        /// </summary>
        // ------------------------------------------------------------
        public int Order;

        // ------------------------------------------------------------
        /// <summary>
        /// 채널 값에 적용하는 연산.
        /// </summary>
        // ------------------------------------------------------------
        public NumericFOperation Operation;

        // ------------------------------------------------------------
        /// <summary>
        /// 채널별 값.
        /// </summary>
        // ------------------------------------------------------------
        public MotionChannelValues Values;

        // ------------------------------------------------------------
        /// <summary>
        /// 사용처가 해석하는 동작 식별자.
        /// </summary>
        // ------------------------------------------------------------
        public int ActionID;

        // ------------------------------------------------------------
        /// <summary>
        /// 항목이 시작된 Tick.
        /// </summary>
        // ------------------------------------------------------------
        public long StartTick;

        // ------------------------------------------------------------
        /// <summary>
        /// 한 Tick만 적용하는 충격 여부.
        /// </summary>
        // ------------------------------------------------------------
        public bool IsImpulse;

        // ------------------------------------------------------------
        /// <summary>
        /// 마지막 합성에서 이 지속 ADD·SUB 항목이 Body 속도에 반영한 월드 몫.
        /// </summary>
        // ------------------------------------------------------------
        public Vector3 Contribution;

        // ------------------------------------------------------------
        /// <summary>
        /// 마지막 Step에서 하나 이상의 채널에서 보였는지 여부.
        /// </summary>
        // ------------------------------------------------------------
        public bool IsVisible;
    }

    // ======================================================================
    /// <summary>
    /// <br/> Motion Model의 Modifier 항목과 채널 값 상태를 보관하는 Snapshot.
    /// <br/> 같은 용량의 Model에서만 Capture·Restore한다.
    /// </summary>
    // ======================================================================
    public sealed class MotionModelSnapshot
    {

    #region 필드

        // ------------------------------------------------------------
        /// <summary>
        /// Snapshot을 찍은 Actor.
        /// </summary>
        // ------------------------------------------------------------
        public MotionActorKey Actor { get; internal set; }

        // ------------------------------------------------------------
        /// <summary>
        /// Snapshot을 찍은 시점의 마지막 Tick.
        /// </summary>
        // ------------------------------------------------------------
        public long LastTick { get; internal set; } = -1;

        // ------------------------------------------------------------
        /// <summary>
        /// 항목 용량.
        /// </summary>
        // ------------------------------------------------------------
        public int Capacity { get; }

        internal readonly MotionModifierEntry[] Entries;
        internal int EntryCount;
        internal Vector3 AppliedLinear;
        internal Vector3 Up;

        internal readonly MValue<Vector3> Planar = new();
        internal readonly MValue<float> Elevation = new();
        internal readonly MValue<Vector3> Rotation = new();

    #endregion

    #region 생성자

        // ------------------------------------------------------------
        /// <summary>
        /// 항목 용량으로 Snapshot을 만든다.
        /// </summary>
        // ------------------------------------------------------------
        public MotionModelSnapshot(int capacity)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            Capacity = capacity;
            Entries = new MotionModifierEntry[capacity];
        }

    #endregion

    }
}
