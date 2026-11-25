/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : ObjectiveTestConds.cs
수정일 : 2026-11-25

# 설명
Objective 테스트가 함께 쓰는 Condition 픽스처를 모은다.

# 특이사항, 제약사항
테스트 어셈블리 안에서만 쓰며 각 테스트가 값과 호출 횟수를 직접 조작한다.
========================================================================= BLOCK_HEADER_END */

using System;
using System.Collections;
using System.Collections.Generic;

using inonego;
using inonego.Xeri;
using inonego.Xeri.Game;
using inonego.Xeri.Game.Objective;

namespace inonego.Xeri.Game.TEST.Objective
{
    // ============================================================
    /// <summary>
    /// 값을 바꿀 수 있고 호출 횟수를 세는 Condition.
    /// </summary>
    // ============================================================
    internal sealed class ToggleCond : ICond<ObjectiveContext>
    {
        // ------------------------------------------------------------
        /// <summary>
        /// Check가 돌려줄 값.
        /// </summary>
        // ------------------------------------------------------------
        public bool Value { get; set; }

        // ------------------------------------------------------------
        /// <summary>
        /// Check가 실제로 호출된 횟수.
        /// </summary>
        // ------------------------------------------------------------
        public int Calls { get; private set; }

        // ------------------------------------------------------------
        /// <summary>
        /// 시작 값을 정한다.
        /// </summary>
        // ------------------------------------------------------------
        public ToggleCond(bool value = false)
        {
            Value = value;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 호출 횟수를 기록하고 현재 값을 돌려준다.
        /// </summary>
        // ------------------------------------------------------------
        public bool Check(ObjectiveContext context)
        {
            Calls++;
            return Value;
        }
    }

    // ============================================================
    /// <summary>
    /// Progress의 정수 값이 기준 이상이면 참인 Condition.
    /// </summary>
    // ============================================================
    internal sealed class CounterCond : ICond<ObjectiveContext>
    {
        private readonly string id;
        private readonly int required;

        // ------------------------------------------------------------
        /// <summary>
        /// 읽을 Progress ID와 기준 값을 정한다.
        /// </summary>
        // ------------------------------------------------------------
        public CounterCond(string id, int required = 1)
        {
            this.id = id;
            this.required = required;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Progress 값이 기준 이상인지 돌려준다.
        /// </summary>
        // ------------------------------------------------------------
        public bool Check(ObjectiveContext context) => context.Progress.GetI(id) >= required;
    }

    // ============================================================
    /// <summary>
    /// Progress의 Flag가 서 있으면 참인 Condition.
    /// </summary>
    // ============================================================
    internal sealed class FlagCond : ICond<ObjectiveContext>
    {
        private readonly string id;

        // ------------------------------------------------------------
        /// <summary>
        /// 읽을 Flag ID를 정한다.
        /// </summary>
        // ------------------------------------------------------------
        public FlagCond(string id)
        {
            this.id = id;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Flag가 서 있는지 돌려준다.
        /// </summary>
        // ------------------------------------------------------------
        public bool Check(ObjectiveContext context) => context.Progress.HasFlag(id);
    }

    // ============================================================
    /// <summary>
    /// 평가되면 항상 예외를 던지는 Condition.
    /// </summary>
    // ============================================================
    internal sealed class ThrowCond : ICond<ObjectiveContext>
    {
        private readonly string message;

        // ------------------------------------------------------------
        /// <summary>
        /// 던질 예외의 메시지를 정한다.
        /// </summary>
        // ------------------------------------------------------------
        public ThrowCond(string message = "injected")
        {
            this.message = message;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 항상 InvalidOperationException을 던진다.
        /// </summary>
        // ------------------------------------------------------------
        public bool Check(ObjectiveContext context) => throw new InvalidOperationException(message);
    }
}
