/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : UnityEventActionTarget.cs
수정일 : 2026-11-12

# 설명
직렬화 UnityEvent를 IActionTarget 실행 계약으로 노출하는 범용 Reaction Action Target.

# 제약사항
UnityEvent listener의 반환값은 수집하지 않으며 실행 예외의 진단은 ReactionBinding이 담당한다.
========================================================================= BLOCK_HEADER_END */

using System;

using UnityEngine;
using UnityEngine.Events;

namespace inonego.Xeri.Game
{
    // ============================================================
    /// <summary>
    /// Inspector에서 구성한 UnityEvent를 Reaction Action으로 실행한다.
    /// </summary>
    // ============================================================
    [Serializable]
    public sealed class UnityEventActionTarget : IActionTarget
    {

    #region 필드

        // ------------------------------------------------------------
        /// <summary>
        /// Inspector와 런타임 조립이 공유하는 실행 Event.
        /// </summary>
        // ------------------------------------------------------------
        public UnityEvent Action => action;

        [SerializeField]
        private UnityEvent action = new UnityEvent();

    #endregion

    #region IActionTarget

        // ------------------------------------------------------------
        /// <summary>
        /// Reaction 실행 요청을 직렬화된 UnityEvent listener에 전달한다.
        /// </summary>
        // ------------------------------------------------------------
        public void Execute(ReactionContext context)
        {
            // UnityEvent가 authoring한 listener 집합을 실행하며 결과 해석은 소유하지 않는다.
            action.Invoke();
        }

    #endregion

    }
}
