/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : UseController.cs
수정일 : 2026-11-12

# 설명
외부 Scanner가 공급한 UseOffer 후보 중 CurrentOffer를 선택하고 Use 입력을 전달한다.
선택 정책이 있으면 Priority를 우선한 뒤 같은 Priority 후보의 Context 점수를 비교한다.

# 제약사항
Physics·Raycast 탐색과 InputAction 구독은 프로젝트 Driver가 담당하며 이 Component는 특정 탐색 방식에 의존하지 않는다.
========================================================================= BLOCK_HEADER_END */

using System;
using System.Collections.Generic;

using UnityEngine;

namespace inonego.Xeri.Game
{
    // ============================================================
    /// <summary>
    /// 현재 Use 후보 선택과 Use 전달을 소유하는 Component.
    /// </summary>
    // ============================================================
    public sealed class UseController : MonoBehaviour
    {
    #region 필드

        // ------------------------------------------------------------
        /// <summary>
        /// Scanner가 현재 범위 안에 있다고 알려준 Offer 목록.
        /// </summary>
        // ------------------------------------------------------------
        private readonly List<UseOffer> offers = new();

        [SerializeField]
        private MonoBehaviour selectionPolicyComponent = null;

        private IUseOfferSelectionPolicy selectionPolicy = null;

        // ------------------------------------------------------------
        /// <summary>
        /// 현재 Use와 Prompt가 연결된 Offer.
        /// </summary>
        // ------------------------------------------------------------
        public UseOffer CurrentOffer => currentOffer;

        private UseOffer currentOffer = null;

    #endregion

    #region 이벤트

        // ------------------------------------------------------------
        /// <summary>
        /// CurrentOffer가 바뀌어 외부 Prompt Presenter가 표시를 전환해야 할 때 호출된다.
        /// </summary>
        // ------------------------------------------------------------
        public event Action<UseOffer> OnCurrentOfferChange = null;

        // ------------------------------------------------------------
        /// <summary>
        /// 새 CurrentOffer의 Prompt를 표시할 때 호출된다.
        /// </summary>
        // ------------------------------------------------------------
        public event Action<UsePrompt> OnPromptShow = null;

        // ------------------------------------------------------------
        /// <summary>
        /// CurrentOffer가 사라져 기존 Prompt를 감춰야 할 때 호출된다.
        /// </summary>
        // ------------------------------------------------------------
        public event Action OnPromptHide = null;

    #endregion

    #region 유니티 이벤트

        // ------------------------------------------------------------
        /// <summary>
        /// 비활성화 동안 보존한 Scanner 후보를 다시 구독하고 CurrentOffer를 복원한다.
        /// </summary>
        // ------------------------------------------------------------
        private void OnEnable()
        {
            selectionPolicy = ResolveSelectionPolicy();

            foreach (var offer in offers)
            {
                if (offer != null)
                {
                    offer.OnAvailabilityChange += _OnOfferAvailabilityChange;
                }
            }

            RefreshCurrentOffer();
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 동적 선택 정책이 있으면 움직이는 Context에 맞춰 CurrentOffer를 갱신한다.
        /// </summary>
        // ------------------------------------------------------------
        private void Update()
        {
            if (selectionPolicy != null)
            {
                RefreshCurrentOffer();
            }
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Controller가 비활성화되면 외부 Offer 구독과 현재 Prompt만 정리한다.
        /// </summary>
        // ------------------------------------------------------------
        private void OnDisable()
        {
            foreach (var offer in offers)
            {
                if (offer != null)
                {
                    offer.OnAvailabilityChange -= _OnOfferAvailabilityChange;
                }
            }

            // Scanner가 아직 범위 안에 있는 후보는 다음 활성화 때 다시 선택할 수 있도록 보존한다.
            selectionPolicy = null;
            SetCurrentOffer(null);
        }

    #endregion

    #region 메서드

        // ------------------------------------------------------------
        /// <summary>
        /// Scanner가 새 후보를 발견했을 때 Controller에 추가한다.
        /// </summary>
        // ------------------------------------------------------------
        public void AddOffer(UseOffer offer)
        {
            if (offer == null || offers.Contains(offer))
            {
                return;
            }

            offers.Add(offer);

            if (isActiveAndEnabled)
            {
                offer.OnAvailabilityChange += _OnOfferAvailabilityChange;
                RefreshCurrentOffer();
            }
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Scanner 범위를 벗어난 후보를 Controller에서 제거한다.
        /// </summary>
        // ------------------------------------------------------------
        public void RemoveOffer(UseOffer offer)
        {
            if (offer == null || !offers.Remove(offer))
            {
                return;
            }

            offer.OnAvailabilityChange -= _OnOfferAvailabilityChange;

            if (isActiveAndEnabled)
            {
                RefreshCurrentOffer();
            }
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 현재 선택된 Offer에 Use 입력을 전달한다.
        /// </summary>
        // ------------------------------------------------------------
        public bool TryUse(GameObject instigator)
        {
            var offer = currentOffer;

            if (offer == null || !offer.TryUse(instigator))
            {
                // 비활성화 직후처럼 선택 결과가 오래되었을 수 있어 다음 입력 전에 즉시 보정한다.
                RefreshCurrentOffer();
                return false;
            }

            return true;
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// 사용 가능한 후보에서 높은 Priority를 우선하고 같은 Priority는 선택 정책 점수로 결정한다.
        /// </summary>
        // ----------------------------------------------------------------------
        private void RefreshCurrentOffer()
        {
            UseOffer selected = null;
            var selectedPriority = int.MinValue;
            var selectedScore = float.PositiveInfinity;

            foreach (var offer in offers)
            {
                if (offer == null || !offer.IsAvailable)
                {
                    continue;
                }

                var score = 0.0f;

                if
                (
                    selectionPolicy != null &&
                    (!selectionPolicy.TryScore(offer, ReferenceEquals(offer, currentOffer), out score) ||
                     !float.IsFinite(score))
                )
                {
                    continue;
                }

                if
                (
                    selected == null ||
                    offer.Priority > selectedPriority ||
                    (offer.Priority == selectedPriority && score < selectedScore)
                )
                {
                    selected = offer;
                    selectedPriority = offer.Priority;
                    selectedScore = score;
                }
            }

            SetCurrentOffer(selected);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 직렬화된 선택 정책 Component를 Xeri 선택 계약으로 해석한다.
        /// </summary>
        // ------------------------------------------------------------
        private IUseOfferSelectionPolicy ResolveSelectionPolicy()
        {
            if (selectionPolicyComponent == null)
            {
                return null;
            }

            if (selectionPolicyComponent is IUseOfferSelectionPolicy policy)
            {
                return policy;
            }

            throw new InvalidOperationException
            (
                "UseController Selection Policy는 IUseOfferSelectionPolicy를 구현한 MonoBehaviour여야 합니다."
            );
        }

        // ------------------------------------------------------------
        /// <summary>
        /// CurrentOffer 전환과 Prompt 표시 전환을 같은 순서로 전파한다.
        /// </summary>
        // ------------------------------------------------------------
        private void SetCurrentOffer(UseOffer value)
        {
            if (currentOffer == value)
            {
                return;
            }

            if (currentOffer != null)
            {
                // 이전 Offer의 표시가 먼저 종료되어 Prompt가 동시에 두 개 보이지 않게 한다.
                OnPromptHide?.Invoke();
            }

            currentOffer = value;
            OnCurrentOfferChange?.Invoke(currentOffer);

            if (currentOffer != null)
            {
                OnPromptShow?.Invoke(new UsePrompt(currentOffer));
            }
        }

    #endregion

    #region 이벤트 핸들러

        // ------------------------------------------------------------
        /// <summary>
        /// 후보 Offer의 Enabled 상태가 바뀌면 CurrentOffer를 다시 선택한다.
        /// </summary>
        // ------------------------------------------------------------
        private void _OnOfferAvailabilityChange(UseOffer offer)
        {
            RefreshCurrentOffer();
        }

    #endregion
    }
}
