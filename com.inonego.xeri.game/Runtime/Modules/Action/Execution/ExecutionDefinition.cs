/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : ExecutionDefinition.cs
수정일 : 2026-09-23

# 설명
Flow와 Behavior authoring을 한 Execution Definition으로 조립하고 Session factory를 제공한다.
Definition은 Runtime state를 소유하지 않으며 CreateSession마다 독립 Runtime을 생성한다.
========================================================================= BLOCK_HEADER_END */

using System;
using System.Collections;
using System.Collections.Generic;

using UnityEngine;

namespace inonego.Xeri.Game
{
    // ======================================================================
    /// <summary>
    /// Flow와 Behavior 정의를 조합하고 요청마다 독립 ExecutionSession을 생성한다.
    /// </summary>
    // ======================================================================
    [Serializable]
    public sealed class ExecutionDefinition
    {

    #region 설정

        // ------------------------------------------------------------
        /// <summary>
        /// 이 실행에 적용할 시간 진행 정의.
        /// </summary>
        // ------------------------------------------------------------
        public ExecutionFlowDefinition Flow => flow;

        [SerializeReference]
        private ExecutionFlowDefinition flow = new FixedTimelineExecutionFlowDefinition();

        // ------------------------------------------------------------
        /// <summary>
        /// 이 실행에 연결된 Behavior 정의 목록.
        /// </summary>
        // ------------------------------------------------------------
        public IReadOnlyList<ExecutionBehaviorDefinition> Behaviors => behaviors;

        [SerializeReference]
        private ExecutionBehaviorDefinition[] behaviors = Array.Empty<ExecutionBehaviorDefinition>();

        // ------------------------------------------------------------
        /// <summary>
        /// 설정된 Behavior 정의의 개수.
        /// </summary>
        // ------------------------------------------------------------
        public int BehaviorCount => behaviors?.Length ?? 0;

    #endregion

    #region 생성자

        // ------------------------------------------------------------
        /// <summary>
        /// Unity 직렬화에 사용할 기본 실행 정의를 생성한다.
        /// </summary>
        // ------------------------------------------------------------
        public ExecutionDefinition()
        {
            // NONE
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 지정된 Flow와 Behavior 정의로 실행 구성을 생성한다.
        /// </summary>
        // ------------------------------------------------------------
        public ExecutionDefinition(ExecutionFlowDefinition flow, ExecutionBehaviorDefinition[] behaviors = null)
        {
            this.flow = flow ?? throw new ArgumentNullException(nameof(flow));
            this.behaviors = behaviors ?? Array.Empty<ExecutionBehaviorDefinition>();
        }

    #endregion

    #region 세션 생성

        // --------------------------------------------------------------------------------
        /// <summary>
        /// Flow와 Behavior 정의에서 새 Runtime을 구성하며 정의 자체는 실행 상태를 보관하지 않는다.
        /// </summary>
        // --------------------------------------------------------------------------------
        public ExecutionSession CreateSession(ulong executionID, in ExecutionContext context)
        {
            if (flow == null)
            {
                throw new InvalidOperationException("Execution Definition에 Flow가 없습니다.");
            }

            var flowRuntime = flow.CreateRuntime() ??
                throw new InvalidOperationException("Execution Flow Runtime을 생성할 수 없습니다.");
            var behaviorRuntime = new List<ExecutionBehaviorRuntime>(BehaviorCount);

            for (var index = 0; index < BehaviorCount; index++)
            {
                var definition = behaviors[index];
                if (definition == null) continue;

                var runtime = definition.CreateRuntime();
                if (runtime != null)
                {
                    behaviorRuntime.Add(runtime);
                }
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
        /// Flow와 각 Behavior의 authoring 경계를 유효한 범위로 보정한다.
        /// </summary>
        // ------------------------------------------------------------
        public void Normalize()
        {
            flow ??= new FixedTimelineExecutionFlowDefinition();
            flow.Normalize();
            behaviors ??= Array.Empty<ExecutionBehaviorDefinition>();

            var maximumDuration = flow.MaximumDuration;
            for (var index = 0; index < behaviors.Length; index++)
            {
                behaviors[index]?.Normalize(maximumDuration);
            }
        }

    #endregion

    }
}