/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : ReactionContracts.cs
수정일 : 2026-11-12

# 설명
ReactionBinding이 사용하는 Signal Source, Action Target과 실행 Context 계약.

# 제약사항
동적 주소 해석, Registry, 비동기 실행 상태와 Sequence는 이 계약에 포함하지 않는다.
========================================================================= BLOCK_HEADER_END */

using System;

using UnityEngine;

namespace inonego.Xeri.Game
{
    // ============================================================
    /// <summary>
    /// Signal 발생 시점의 Source와 실행 주체를 전달하는 Context.
    /// </summary>
    // ============================================================
    public readonly struct ReactionContext
    {
    #region 필드

        // ------------------------------------------------------------
        /// <summary>
        /// Signal을 발생시킨 Component.
        /// </summary>
        // ------------------------------------------------------------
        public Component Source { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// Signal을 시작한 GameObject.
        /// </summary>
        // ------------------------------------------------------------
        public GameObject Instigator { get; }

    #endregion

    #region 생성자

        // ------------------------------------------------------------
        /// <summary>
        /// Signal 발생 사실을 나타내는 Context를 만든다.
        /// </summary>
        // ------------------------------------------------------------
        public ReactionContext(Component source, GameObject instigator)
        {
            Source = source;
            Instigator = instigator;
        }

    #endregion
    }

    // ============================================================
    /// <summary>
    /// ReactionBinding이 구독할 Signal 발생 Source.
    /// </summary>
    // ============================================================
    public interface ISignalSource
    {
    #region 이벤트

        // ------------------------------------------------------------
        /// <summary>
        /// Signal 발생 시 실행 Context와 함께 호출된다.
        /// </summary>
        // ------------------------------------------------------------
        public event Action<ReactionContext> OnSignal;

    #endregion
    }

    // ============================================================
    /// <summary>
    /// ReactionBinding이 실행을 요청할 효과 대상.
    /// </summary>
    // ============================================================
    public interface IActionTarget
    {
    #region 메서드

        // ------------------------------------------------------------
        /// <summary>
        /// 전달받은 Context와 함께 효과를 실행한다.
        /// </summary>
        // ------------------------------------------------------------
        public void Execute(ReactionContext context);

    #endregion
    }
}
