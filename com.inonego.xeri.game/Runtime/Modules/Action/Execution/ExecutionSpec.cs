/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : ExecutionSpec.cs
수정일 : 2026-11-19

# 설명
Flow와 Behavior Spec을 한 Execution Spec으로 조립하고 Session factory를 제공한다.
Spec은 Runtime state를 소유하지 않으며 CreateSession마다 독립 Runtime을 생성한다.
========================================================================= BLOCK_HEADER_END */

using System;
using System.Collections;
using System.Collections.Generic;

using UnityEngine;

namespace inonego.Xeri.Game
{
    // ======================================================================
    /// <summary>
    /// Flow와 Behavior Spec을 조합하고 요청마다 독립 ExecutionSession을 생성한다.
    /// </summary>
    // ======================================================================
    [Serializable]
    public sealed class ExecutionSpec
    {

    #region 설정

        // ------------------------------------------------------------
        /// <summary>
        /// 이 실행에 적용할 시간 진행 Spec.
        /// </summary>
        // ------------------------------------------------------------
        public ExecutionFlowSpec Flow => flow;

        [SerializeReference]
        private ExecutionFlowSpec flow = new FixedTimelineExecutionFlowSpec();

        // ------------------------------------------------------------
        /// <summary>
        /// 이 실행에 연결된 Behavior Spec 목록.
        /// </summary>
        // ------------------------------------------------------------
        public IReadOnlyList<ExecutionBehaviorSpec> Behaviors => behaviors;

        [SerializeReference]
        private ExecutionBehaviorSpec[] behaviors = Array.Empty<ExecutionBehaviorSpec>();

        // ------------------------------------------------------------
        /// <summary>
        /// 설정된 Behavior Spec의 개수.
        /// </summary>
        // ------------------------------------------------------------
        public int BehaviorCount => behaviors?.Length ?? 0;

    #endregion

    #region 생성자

        // ------------------------------------------------------------
        /// <summary>
        /// Unity 직렬화에 사용할 기본 실행 Spec을 생성한다.
        /// </summary>
        // ------------------------------------------------------------
        public ExecutionSpec()
        {
            // NONE
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 지정된 Flow와 Behavior Spec으로 실행 구성을 생성한다.
        /// </summary>
        // ------------------------------------------------------------
        public ExecutionSpec(ExecutionFlowSpec flow, ExecutionBehaviorSpec[] behaviors = null)
        {
            this.flow = flow ?? throw new ArgumentNullException(nameof(flow));
            this.behaviors = behaviors ?? Array.Empty<ExecutionBehaviorSpec>();
        }

    #endregion

    #region 세션 생성

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/> Flow와 Behavior Spec에서 새 Runtime을 구성하며
        /// <br/> Spec 자체는 실행 상태를 보관하지 않는다.
        /// <br/> 타입을 고르지 않은 빈 Behavior 항목은 건너뛰고, Flow나 Behavior Spec이
        /// <br/> Runtime을 만들지 못하면 예외를 던진다.
        /// </summary>
        // --------------------------------------------------------------------------------
        public ExecutionSession CreateSession(ulong executionID, in ExecutionContext context)
        {
            if (flow == null)
            {
                throw new InvalidOperationException("Execution Spec에 Flow가 없습니다.");
            }

            var flowRuntime = flow.CreateRuntime() ??
                throw new InvalidOperationException("Execution Flow Runtime을 생성할 수 없습니다.");
            var behaviorRuntime = new List<ExecutionBehaviorRuntime>(BehaviorCount);

            for (var index = 0; index < BehaviorCount; index++)
            {
                // Inspector에서 항목만 추가하고 타입을 고르지 않은 빈 칸은 실행에 참여하지 않는다.
                var spec = behaviors[index];
                if (spec == null) continue;

                var runtime = spec.CreateRuntime() ??
                    throw new InvalidOperationException("Execution Behavior Runtime을 생성할 수 없습니다.");
                behaviorRuntime.Add(runtime);
            }

            return new ExecutionSession
            (
                executionID,
                context,
                flowRuntime,
                behaviorRuntime,
                flow.MaximumDuration
            );
        }

    #endregion

    #region 정규화

        // ------------------------------------------------------------
        /// <summary>
        /// Flow와 각 Behavior Spec의 경계를 유효한 범위로 보정한다.
        /// </summary>
        // ------------------------------------------------------------
        public void Normalize()
        {
            flow ??= new FixedTimelineExecutionFlowSpec();
            flow.Normalize();
            behaviors ??= Array.Empty<ExecutionBehaviorSpec>();

            var maximumDuration = flow.MaximumDuration;
            for (var index = 0; index < behaviors.Length; index++)
            {
                behaviors[index]?.Normalize(maximumDuration);
            }
        }

    #endregion

    }
}