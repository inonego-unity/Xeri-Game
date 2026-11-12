/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : ExecutionBehavior.cs
수정일 : 2026-09-23

# 설명
Execution 중 병렬로 참여하는 Behavior의 Definition과 Runtime 수명 계약을 정의한다.
Definition은 공유 가능한 authoring이고 Runtime은 Session마다 새로 생성한다.
========================================================================= BLOCK_HEADER_END */

using System;

namespace inonego.Xeri.Game
{
    // ============================================================
    /// <summary>
    /// Execution Behavior의 공유 가능한 authoring base.
    /// </summary>
    // ============================================================
    [Serializable]
    public abstract class ExecutionBehaviorDefinition
    {

    #region 생성·정규화

        // ------------------------------------------------------------
        /// <summary>
        /// Behavior 정의로부터 실행별 독립 Runtime 객체를 생성한다.
        /// </summary>
        // ------------------------------------------------------------
        public abstract ExecutionBehaviorRuntime CreateRuntime();

        // ------------------------------------------------------------
        /// <summary>
        /// 부모 Execution 최대 시간에 맞게 Behavior 설정을 정규화한다.
        /// </summary>
        // ------------------------------------------------------------
        public virtual void Normalize(float maximumDuration)
        {
            // NONE
        }

    #endregion

    }

    // ============================================================
    /// <summary>
    /// 한 ExecutionSession에 귀속되는 Behavior 실행 상태 base.
    /// </summary>
    // ============================================================
    public abstract class ExecutionBehaviorRuntime
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
                throw new InvalidOperationException("Execution Behavior Runtime은 여러 Session에 연결할 수 없습니다.");
            }

            Session = session ?? throw new ArgumentNullException(nameof(session));
        }

    #endregion

    #region 실행 수명과 Signal

        // ------------------------------------------------------------
        /// <summary>
        /// Execution 시작 시 Runtime Behavior의 초기 상태를 설정한다.
        /// </summary>
        // ------------------------------------------------------------
        public virtual void Begin(ExecutionSession session)
        {
            // NONE
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Execution 진행 시간에 따라 Behavior의 내부 상태를 갱신한다.
        /// </summary>
        // ------------------------------------------------------------
        public virtual void Advance(ExecutionSession session, float deltaTime)
        {
            // NONE
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Execution에서 전달받은 제어 Signal을 Behavior에 알린다.
        /// </summary>
        // ------------------------------------------------------------
        public virtual void Signal(ExecutionSession session, in ExecutionSignal signal)
        {
            // NONE
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 종료 사유를 받고 Behavior가 보유한 실행 자원을 정리한다.
        /// </summary>
        // ------------------------------------------------------------
        public virtual void End(ExecutionSession session, ExecutionEndReason reason)
        {
            // NONE
        }

    #endregion

    }
}
