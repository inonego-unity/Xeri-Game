/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : MotionInputBuffer.cs
수정일 : 2026-11-21

# 설명
Host가 한 Tick 동안 모은 층별 Motion 입력을 보관하고 Step에 MotionStepInput으로 넘기는 버퍼를 정의한다.
오래 유지하는 Override는 Core Lease로 돌려주어 해제 시 종료 명령을 넣는다.

# 특이사항, 제약사항
AsStepInput이 돌려준 Span은 다음 Add로 버퍼가 커지거나 Clear하기 전까지만 유효하다.
Lease 해제는 종료 명령을 버퍼에 넣을 뿐이다. Restore로 항목이 이미 사라졌으면 Model이 NotFound로 무시한다.
되감기·재실행에서는 Lease가 아니라 기록한 입력 값을 다시 넣는다.
========================================================================= BLOCK_HEADER_END */

using System;

using UnityEngine;

using inonego;
using inonego.Xeri;

namespace inonego.Xeri.Game.Motion
{
    // ============================================================
    /// <summary>
    /// <br/> 한 Tick의 층별 Motion 입력을 모으는 재사용 버퍼.
    /// <br/> Step 뒤 Clear로 비운다.
    /// </summary>
    // ============================================================
    public sealed class MotionInputBuffer
    {

    #region 내부 데이터

        private sealed class ValueList<T>
        {
            internal T[] Items;
            internal int Count;

            internal ValueList(int capacity)
            {
                Items = new T[capacity];
            }

            internal void Add(in T item)
            {
                if (Count == Items.Length)
                {
                    Array.Resize(ref Items, Items.Length * 2);
                }

                Items[Count++] = item;
            }

            internal ReadOnlySpan<T> AsSpan() => new(Items, 0, Count);

            internal void Clear()
            {
                Array.Clear(Items, 0, Count);
                Count = 0;
            }
        }

    #endregion

    #region 필드

        // ------------------------------------------------------------
        /// <summary>
        /// 모인 입력 수(Impulse 제외).
        /// </summary>
        // ------------------------------------------------------------
        public int Count => intents.Count + overrides.Count + scales.Count + externals.Count;

        // ------------------------------------------------------------
        /// <summary>
        /// 이번 Tick에 모인 Impulse 합계.
        /// </summary>
        // ------------------------------------------------------------
        public Vector3 Impulse => impulse;
        private Vector3 impulse;

        private readonly ValueList<MotionIntent> intents;
        private readonly ValueList<MotionOverrideCommand> overrides;
        private readonly ValueList<MotionScaleCommand> scales;
        private readonly ValueList<MotionExternalCommand> externals;

    #endregion

    #region 생성자

        // ------------------------------------------------------------
        /// <summary>
        /// 층별 초기 용량으로 버퍼를 만든다.
        /// </summary>
        // ------------------------------------------------------------
        public MotionInputBuffer(int capacity = 8)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            intents = new ValueList<MotionIntent>(capacity);
            overrides = new ValueList<MotionOverrideCommand>(capacity);
            scales = new ValueList<MotionScaleCommand>(capacity);
            externals = new ValueList<MotionExternalCommand>(capacity);
        }

    #endregion

    #region 입력 추가

        // ------------------------------------------------------------
        /// <summary>
        /// 기본 의도를 추가한다.
        /// </summary>
        // ------------------------------------------------------------
        public void Add(in MotionIntent intent) => intents.Add(intent);

        // ------------------------------------------------------------
        /// <summary>
        /// Override 명령을 추가한다.
        /// </summary>
        // ------------------------------------------------------------
        public void Add(in MotionOverrideCommand command) => overrides.Add(command);

        // ------------------------------------------------------------
        /// <summary>
        /// 제어 배율 명령을 추가한다.
        /// </summary>
        // ------------------------------------------------------------
        public void Add(in MotionScaleCommand command) => scales.Add(command);

        // ------------------------------------------------------------
        /// <summary>
        /// 외부 속도 명령을 추가한다.
        /// </summary>
        // ------------------------------------------------------------
        public void Add(in MotionExternalCommand command) => externals.Add(command);

        // ------------------------------------------------------------
        /// <summary>
        /// 이번 Tick의 Impulse에 속도 변화량을 더한다.
        /// </summary>
        // ------------------------------------------------------------
        public void AddImpulse(Vector3 velocityChange)
        {
            impulse += velocityChange;
        }

    #endregion

    #region Step 전달

        // ------------------------------------------------------------
        /// <summary>
        /// 모인 입력을 Step에 넘길 묶음으로 읽는다.
        /// </summary>
        // ------------------------------------------------------------
        public MotionStepInput AsStepInput() => new
        (
            intents.AsSpan(), overrides.AsSpan(), scales.AsSpan(), externals.AsSpan(), impulse
        );

        // ------------------------------------------------------------
        /// <summary>
        /// 모인 입력과 Impulse를 비운다.
        /// </summary>
        // ------------------------------------------------------------
        public void Clear()
        {
            intents.Clear();
            overrides.Clear();
            scales.Clear();
            externals.Clear();
            impulse = Vector3.zero;
        }

    #endregion

    #region Override Lease

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> Override 시작 명령을 넣고, 해제하면 종료 명령을 넣는 Lease를 돌려준다.
        /// <br/> 시작(Set) 명령이어야 한다.
        /// </summary>
        // ----------------------------------------------------------------------
        public Lease AcquireOverride(in MotionOverrideCommand begin)
        {
            if (begin.Action != MotionCommandAction.Set)
            {
                throw new ArgumentException("Override 시작 명령이 필요합니다.", nameof(begin));
            }

            var source = begin.Source;
            Add(begin);
            return new Lease(() => Add(MotionOverrideCommand.End(source)));
        }

    #endregion

    }
}
