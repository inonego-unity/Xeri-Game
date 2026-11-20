/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : ExecutionTestBase.cs
수정일 : 2026-11-19

# 설명
Execution 테스트가 공유하는 호출 기록 Behavior와 고정 Timeline Session 생성 도우미를 제공한다.
========================================================================= BLOCK_HEADER_END */

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

using NUnit;
using NUnit.Framework;

namespace inonego.Xeri.Game.TEST._Execution
{
    using Xeri;
    using Xeri.Game;

    // ======================================================================
    /// <summary>
    /// Execution 테스트가 공유하는 호출 기록 Behavior와 Session 생성 도우미이다.
    /// </summary>
    // ======================================================================
    public abstract class ExecutionTestBase
    {

    #region 검증용 Behavior와 Session 생성

        // ============================================================
        /// <summary>
        /// Behavior 진행·알림·수명과 실패 조건을 관찰하는 공통 테스트 대역.
        /// </summary>
        // ============================================================
        protected sealed class TraceBehaviorRuntime : ExecutionBehaviorRuntime
        {

        #region 관찰 상태

            private readonly string name;
            private readonly List<string> trace;
            private readonly bool failOnBegin;

            // ------------------------------------------------------------
            /// <summary>
            /// End 호출 횟수를 반환한다.
            /// </summary>
            // ------------------------------------------------------------
            public int EndCount { get; private set; }

            // ------------------------------------------------------------
            /// <summary>
            /// Signal 수신 횟수를 반환한다.
            /// </summary>
            // ------------------------------------------------------------
            public int SignalCount { get; private set; }

            // ------------------------------------------------------------
            /// <summary>
            /// 마지막 Advance의 Delta를 반환한다.
            /// </summary>
            // ------------------------------------------------------------
            public float LastDelta { get; private set; }

            // ------------------------------------------------------------
            /// <summary>
            /// Begin 시점에 실행할 콜백을 보관한다.
            /// </summary>
            // ------------------------------------------------------------
            public Action<ExecutionSession> OnBegin { get; set; }

            // ------------------------------------------------------------
            /// <summary>
            /// Advance 시점에 실행할 콜백을 보관한다.
            /// </summary>
            // ------------------------------------------------------------
            public Action<ExecutionSession, float> OnAdvance { get; set; }

        #endregion

        #region 생성자

            // ------------------------------------------------------------
            /// <summary>
            /// 호출 순서를 기록하는 테스트 Behavior를 생성한다.
            /// </summary>
            // ------------------------------------------------------------
            public TraceBehaviorRuntime
            (
                string name = "Probe",
                List<string> trace = null,
                bool failOnBegin = false
            )
            {
                this.name = name;
                this.trace = trace;
                this.failOnBegin = failOnBegin;
            }

        #endregion

        #region 실행 수명과 알림

            // ------------------------------------------------------------
            /// <summary>
            /// Begin 호출을 기록한다.
            /// </summary>
            // ------------------------------------------------------------
            public override void Begin(ExecutionSession session)
            {
                trace?.Add($"{name}.Begin");

                if (failOnBegin)
                {
                    throw new InvalidOperationException($"{name} begin failure");
                }

                OnBegin?.Invoke(session);
            }

            // ------------------------------------------------------------
            /// <summary>
            /// Advance 호출과 Delta를 기록한다.
            /// </summary>
            // ------------------------------------------------------------
            public override void Advance(ExecutionSession session, float deltaTime)
            {
                LastDelta = deltaTime;
                trace?.Add($"{name}.Advance");

                OnAdvance?.Invoke(session, deltaTime);
            }

            // ------------------------------------------------------------
            /// <summary>
            /// 수신한 Signal을 기록한다.
            /// </summary>
            // ------------------------------------------------------------
            public override void Signal(ExecutionSession session, in ExecutionSignal signal)
            {
                SignalCount++;
            }

            // ------------------------------------------------------------
            /// <summary>
            /// 종료 사유를 기록한다.
            /// </summary>
            // ------------------------------------------------------------
            public override void End(ExecutionSession session, ExecutionEndReason reason)
            {
                EndCount++;
                trace?.Add($"{name}.End:{reason}");
            }

        #endregion

        }

        // ------------------------------------------------------------
        /// <summary>
        /// 지정한 시간·Behavior·문맥으로 독립 Session을 구성한다.
        /// </summary>
        // ------------------------------------------------------------
        protected static ExecutionSession CreateTimelineSession
        (
            float duration = 1f,
            IReadOnlyList<ExecutionBehaviorRuntime> behavior = null,
            ExecutionContext context = default,
            ulong id = 1UL
        )
        {
            var flow = new FixedTimelineExecutionFlowSpec(duration);
            return new ExecutionSession
            (
                id,
                context,
                flow.CreateRuntime(),
                behavior,
                flow.MaximumDuration
            );
        }

    #endregion

    }
}
