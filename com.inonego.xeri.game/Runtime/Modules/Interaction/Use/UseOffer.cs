/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : UseOffer.cs
수정일 : 2026-11-12

# 설명
도메인 객체에 선택적으로 부착되어 Prompt, 우선순위, 공간 Anchor와 Used Signal을 제공하는 Use Offer.

# 제약사항
Door·NPC 등 도메인 API를 직접 호출하지 않으며 실제 효과는 ReactionBinding과 Action Target이 담당한다.
========================================================================= BLOCK_HEADER_END */

using System;

using UnityEngine;

namespace inonego.Xeri.Game
{
    // ============================================================
    /// <summary>
    /// 직접 Use 후보 하나를 표현하는 Signal Source Component.
    /// </summary>
    // ============================================================
    public sealed class UseOffer : MonoBehaviour, ISignalSource
    {
    #region 필드

        // ------------------------------------------------------------
        /// <summary>
        /// Inspector와 Prompt UI에 표시할 기본 텍스트.
        /// </summary>
        // ------------------------------------------------------------
        public string PromptText => promptText;

        [SerializeField]
        private string promptText = "Interact";

        // ------------------------------------------------------------
        /// <summary>
        /// 같은 후보 집합에서 높은 값이 먼저 선택되는 우선순위.
        /// </summary>
        // ------------------------------------------------------------
        public int Priority => priority;

        [SerializeField]
        private int priority = 0;

        // ------------------------------------------------------------
        /// <summary>
        /// Controller가 현재 사용 가능한 후보로 취급할 수 있는지 여부.
        /// </summary>
        // ------------------------------------------------------------
        public bool IsAvailable => isActiveAndEnabled && isAvailable;

        [SerializeField]
        private bool isAvailable = true;

        // ------------------------------------------------------------
        /// <summary>
        /// 거리·시선·Prompt가 공유할 World 기준점. 비어 있으면 이 Offer Transform을 사용한다.
        /// </summary>
        // ------------------------------------------------------------
        public Transform Anchor => anchor != null ? anchor : transform;

        [SerializeField]
        private Transform anchor = null;

    #endregion

    #region 이벤트

        // ------------------------------------------------------------
        /// <summary>
        /// Offer가 사용되어 ReactionBinding이 처리할 Signal을 발생시킨다.
        /// </summary>
        // ------------------------------------------------------------
        public event Action<ReactionContext> OnSignal = null;

        // ------------------------------------------------------------
        /// <summary>
        /// 사용 가능 여부가 바뀌어 Controller 후보 선택을 다시 계산해야 할 때 호출된다.
        /// </summary>
        // ------------------------------------------------------------
        public event Action<UseOffer> OnAvailabilityChange = null;

    #endregion

    #region 유니티 이벤트

        // ------------------------------------------------------------
        /// <summary>
        /// 활성화된 Offer를 Controller 후보 선택에 다시 포함시킨다.
        /// </summary>
        // ------------------------------------------------------------
        private void OnEnable()
        {
            OnAvailabilityChange?.Invoke(this);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 비활성 Offer가 CurrentOffer로 남지 않도록 후보 선택 갱신을 요청한다.
        /// </summary>
        // ------------------------------------------------------------
        private void OnDisable()
        {
            OnAvailabilityChange?.Invoke(this);
        }

    #endregion

    #region 메서드

        // ------------------------------------------------------------
        /// <summary>
        /// Offer의 사용 가능 여부를 변경한다.
        /// </summary>
        // ------------------------------------------------------------
        public void SetAvailable(bool value)
        {
            if (isAvailable == value)
            {
                return;
            }

            isAvailable = value;

            // CurrentOffer가 더 이상 유효하지 않을 수 있으므로 구독 Controller에 즉시 알린다.
            OnAvailabilityChange?.Invoke(this);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 지정한 사용자가 Offer를 사용할 수 있으면 Used Signal을 발생시킨다.
        /// </summary>
        // ------------------------------------------------------------
        public bool TryUse(GameObject instigator)
        {
            if (!IsAvailable)
            {
                return false;
            }

            // 실제 도메인 효과는 연결된 ReactionBinding이 결정하도록 발생 사실만 전파한다.
            OnSignal?.Invoke(new ReactionContext(this, instigator));
            return true;
        }

    #endregion
    }
}
