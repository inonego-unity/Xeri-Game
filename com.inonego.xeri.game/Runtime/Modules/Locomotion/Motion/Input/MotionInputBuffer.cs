/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : MotionInputBuffer.cs
수정일 : 2026-11-22

# 설명
Host가 한 Tick 동안 모은 기본 의도와 Modifier 명령을 보관하고 Step에 MotionStepInput으로 넘기는 버퍼를 정의한다.
오래 유지하는 Modifier는 Core Lease로 돌려주어 해제 시 종료 명령을 넣는다.

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
    /// <br/> 한 Tick의 Motion 입력을 모으는 재사용 버퍼.
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
        /// 버퍼에 쌓인 입력 수.
        /// </summary>
        // ------------------------------------------------------------
        public int Count => intents.Count + modifiers.Count;

        private readonly ValueList<MotionIntent> intents;
        private readonly ValueList<MotionModifierCommand> modifiers;
        private Vector3 up;

    #endregion

    #region 생성자

        // ------------------------------------------------------------
        /// <summary>
        /// 입력 종류별 초기 용량으로 버퍼를 만든다.
        /// </summary>
        // ------------------------------------------------------------
        public MotionInputBuffer(int capacity = 8)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            intents = new ValueList<MotionIntent>(capacity);
            modifiers = new ValueList<MotionModifierCommand>(capacity);
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
        /// Modifier 명령을 추가한다.
        /// </summary>
        // ------------------------------------------------------------
        public void Add(in MotionModifierCommand command) => modifiers.Add(command);

        // ------------------------------------------------------------
        /// <summary>
        /// 이번 Tick의 Up 축을 정한다. 정하지 않으면 Model의 Up을 유지한다.
        /// </summary>
        // ------------------------------------------------------------
        public void SetUp(Vector3 up)
        {
            this.up = up;
        }

    #endregion

    #region Step 전달

        // ------------------------------------------------------------
        /// <summary>
        /// 쌓인 입력을 Step 입력으로 묶는다.
        /// </summary>
        // ------------------------------------------------------------
        public MotionStepInput AsStepInput() => new(intents.AsSpan(), modifiers.AsSpan(), up);

        // ------------------------------------------------------------
        /// <summary>
        /// 쌓인 입력을 비운다.
        /// </summary>
        // ------------------------------------------------------------
        public void Clear()
        {
            intents.Clear();
            modifiers.Clear();
            up = Vector3.zero;
        }

    #endregion

    #region Modifier Lease

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> 적용 명령을 넣고, 해제하면 같은 항목의 종료 명령을 넣는 Lease를 돌려준다.
        /// <br/> 사다리·탑승처럼 해제 시점을 다른 코드가 정하는 Modifier에 쓴다.
        /// </summary>
        // ----------------------------------------------------------------------
        public Lease AcquireModifier(in MotionModifierCommand apply)
        {
            if (apply.Action != MotionCommandAction.Apply || apply.IsImpulse)
            {
                throw new ArgumentException("지속 Modifier 적용 명령이 필요합니다.", nameof(apply));
            }

            var source = apply.Source;

            Add(apply);

            return new Lease(() => Add(MotionModifierCommand.End(source)));
        }

    #endregion

    }
}
