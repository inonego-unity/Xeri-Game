/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : ExecutionFlow.cs
수정일 : 2026-11-19

# 설명
Execution 진행 규칙의 공유 가능한 Spec과 한 Session에 귀속되는 Flow Runtime 기반을 제공한다.
Flow는 Session의 적용 시간으로 완료와 Command를 판정하며, 종류별 구현은 같은 폴더의 파일에 둔다.
========================================================================= BLOCK_HEADER_END */

using System;

namespace inonego.Xeri.Game
{
    // ============================================================
    /// <summary>
    /// Execution 진행 규칙의 공유 가능한 Spec base.
    /// </summary>
    // ============================================================
    [Serializable]
    public abstract class ExecutionFlowSpec
    {

    #region 시간 설정·생성

        // ------------------------------------------------------------
        /// <summary>
        /// Flow가 소유하는 최대 실행 시간을 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        public abstract float MaximumDuration { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 설정한 Flow Spec으로부터 실행별 Runtime 객체를 생성한다.
        /// </summary>
        // ------------------------------------------------------------
        public abstract ExecutionFlowRuntime CreateRuntime();

        // ------------------------------------------------------------
        /// <summary>
        /// Flow의 시간 관련 입력을 유효한 상태로 정규화한다.
        /// </summary>
        // ------------------------------------------------------------
        public virtual void Normalize()
        {
            // NONE
        }

    #endregion

    }

    // ============================================================
    /// <summary>
    /// 한 ExecutionSession에 귀속되는 Flow 실행 상태 base.
    /// </summary>
    // ============================================================
    public abstract class ExecutionFlowRuntime
    {

    #region Session 소유권

        // ------------------------------------------------------------
        /// <summary>
        /// 이 Runtime을 단독 소유하는 ExecutionSession.
        /// </summary>
        // ------------------------------------------------------------
        internal ExecutionSession Session { get; private set; }

        // ------------------------------------------------------------
        /// <summary>
        /// 실행별 Runtime을 한 Session에 한 번만 귀속시킨다.
        /// </summary>
        // ------------------------------------------------------------
        internal void Bind(ExecutionSession session)
        {
            if (Session != null)
            {
                throw new InvalidOperationException("Execution Flow Runtime은 여러 Session에 연결할 수 없습니다.");
            }

            Session = session ?? throw new ArgumentNullException(nameof(session));
        }

    #endregion

    #region 진행 상태

        // ------------------------------------------------------------
        /// <summary>
        /// 진행이 종료 조건을 만족했는지 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        public abstract bool IsComplete { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 유지 구간에 머무는 Flow인지 나타낸다.
        /// </summary>
        // ------------------------------------------------------------
        public virtual bool IsHolding => false;

        // ------------------------------------------------------------
        /// <summary>
        /// 유지 상태에서 해제되었는지 여부.
        /// </summary>
        // ------------------------------------------------------------
        public virtual bool HasReleased => false;

        // ------------------------------------------------------------
        /// <summary>
        /// 유지 해제가 확정된 실행 시각.
        /// </summary>
        // ------------------------------------------------------------
        public virtual float ReleaseTime => 0f;

        // ------------------------------------------------------------
        /// <summary>
        /// 최대 유지 시간 대비 해제 진행 비율.
        /// </summary>
        // ------------------------------------------------------------
        public virtual float ReleaseProgress => 0f;

    #endregion

    #region 실행 수명과 Command

        // ------------------------------------------------------------
        /// <summary>
        /// Execution 시작 시 Flow의 시간 진행 상태를 초기화한다.
        /// </summary>
        // ------------------------------------------------------------
        public virtual void Begin(ExecutionSession session)
        {
            // NONE
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 경과 시간에 따라 Flow가 완료 조건으로 진행하게 한다.
        /// </summary>
        // ------------------------------------------------------------
        public abstract void Advance(ExecutionSession session, float deltaTime);

        // ------------------------------------------------------------
        /// <summary>
        /// 실행 명령을 판정하며 지원하지 않는 명령은 거부한다.
        /// </summary>
        // ------------------------------------------------------------
        public virtual ExecutionCommandResult Command(ExecutionSession session, in ExecutionCommand command)
        {
            return ExecutionCommandResult.Rejected;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Execution이 종료될 때 Flow의 수명을 함께 마무리한다.
        /// </summary>
        // ------------------------------------------------------------
        public virtual void End(ExecutionSession session, ExecutionEndReason reason)
        {
            // NONE
        }

    #endregion

    }
}
