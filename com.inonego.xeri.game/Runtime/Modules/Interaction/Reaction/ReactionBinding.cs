/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : ReactionBinding.cs
수정일 : 2026-11-12

# 설명
같은 Scene·Prefab Scope의 Signal Source, 선택적 ICond Guard와 직렬화 Action Target을 연결한다.

# 제약사항
1차 정책은 IgnoreWhileRunning만 지원하며 EndpointAddress, Registry, Sequence와 비동기 취소 상태를 소유하지 않는다.
Action Target은 SerializeReference로 소유하고 Xeri picker를 통해 authoring하며 런타임 변경은 Configure로 연결한다.
========================================================================= BLOCK_HEADER_END */

using System;

using UnityEngine;

namespace inonego.Xeri.Game
{
    using inonego.Xeri.Serializable;

    // ============================================================
    /// <summary>
    /// Signal 발생을 선택적 Guard 판정 뒤 Action Target 실행으로 연결하는 Component.
    /// </summary>
    // ============================================================
    public sealed class ReactionBinding : MonoBehaviour
    {
    #region 필드

        [SerializeField]
        private MonoBehaviour source = null;

        [SerializeField]
        private MonoBehaviour guard = null;

        [SerializeReference, SerializeReferencePicker]
        private IActionTarget target = null;

        private ISignalSource boundSource = null;
        private ICond<ReactionContext> boundGuard = null;
        private IActionTarget boundTarget = null;
        private bool isRunning = false;

    #endregion

    #region 유니티 이벤트

        // ------------------------------------------------------------
        /// <summary>
        /// 직접 참조한 Source·Guard·Target 계약을 해석하고 Source Signal을 구독한다.
        /// </summary>
        // ------------------------------------------------------------
        private void OnEnable()
        {
            Bind();
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Binding 비활성화 뒤에는 Source Signal이 Target 실행을 시작하지 않게 한다.
        /// </summary>
        // ------------------------------------------------------------
        private void OnDisable()
        {
            Unbind();
        }

    #endregion

    #region 메서드

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> 같은 Scene·Prefab Scope의 직접 Endpoint를 런타임에 교체한다.
        /// <br/> 기존 구독을 해제한 뒤 유효성이 확인된 새 Endpoint만 다시 구독한다.
        /// </summary>
        // ------------------------------------------------------------
        public void Configure
        (
            MonoBehaviour source,
            MonoBehaviour guard,
            IActionTarget target
        )
        {
            // 유효하지 않은 입력은 기존 연결까지 끊지 않도록 먼저 계약을 검증한다.
            if (!(source is ISignalSource))
            {
                throw new ArgumentException("ReactionBinding Source는 ISignalSource를 구현한 MonoBehaviour여야 합니다.", nameof(source));
            }

            if (guard != null && !(guard is ICond<ReactionContext>))
            {
                throw new ArgumentException("ReactionBinding Guard는 ICond<ReactionContext>를 구현한 MonoBehaviour여야 합니다.", nameof(guard));
            }

            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            // 이전 Source에서 먼저 해제해 교체 중에는 오래된 Signal이 Target을 실행하지 않게 한다.
            Unbind();

            this.source = source;
            this.guard = guard;
            this.target = target;

            // 활성 상태에서는 새 직접 참조를 즉시 해석해 기존 Inspector 경로와 같은 수명주기를 유지한다.
            if (isActiveAndEnabled)
            {
                Bind();
            }
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Authoring Endpoint를 최소 Reaction 계약으로 해석하고 유효한 경우에만 구독한다.
        /// </summary>
        // ------------------------------------------------------------
        private void Bind()
        {
            boundSource = source as ISignalSource;
            boundGuard = guard as ICond<ReactionContext>;
            boundTarget = target;

            if (boundSource == null)
            {
                Debug.LogError("ReactionBinding Source는 ISignalSource를 구현한 MonoBehaviour여야 합니다.", this);
                return;
            }

            if (guard != null && boundGuard == null)
            {
                Debug.LogError("ReactionBinding Guard는 ICond<ReactionContext>를 구현한 MonoBehaviour여야 합니다.", this);
                return;
            }

            if (boundTarget == null)
            {
                Debug.LogError("ReactionBinding Target이 설정되어 있지 않습니다.", this);
                return;
            }

            boundSource.OnSignal += _OnSignal;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 현재 Binding이 구독한 Source만 해제한다.
        /// </summary>
        // ------------------------------------------------------------
        private void Unbind()
        {
            if (boundSource != null)
            {
                boundSource.OnSignal -= _OnSignal;
            }

            boundSource = null;
            boundGuard = null;
            boundTarget = null;
            isRunning = false;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Signal을 Guard와 Target으로 전달하고 동기 실행 중 중복 진입을 차단한다.
        /// </summary>
        // ------------------------------------------------------------
        private void HandleSignal(ReactionContext context)
        {
            if (isRunning)
            {
                // 1차 정책: 동기 Action이 재귀 Signal을 발생시켜도 같은 Binding을 다시 실행하지 않는다.
                return;
            }

            if (!TryPassGuard(context))
            {
                return;
            }

            isRunning = true;

            try
            {
                // Action Target이 Reaction Context를 해석해 실제 효과를 실행하도록 위임한다.
                boundTarget.Execute(context);
            }
            catch (Exception exception)
            {
                // Domain Action 예외는 이후 구독자의 실행을 막지 않도록 Binding 경계에서 진단한다.
                Debug.LogException(exception, this);
            }
            finally
            {
                isRunning = false;
            }
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 선택적 Guard를 판정하고 예외가 발생하면 Target 실행을 차단한다.
        /// </summary>
        // ------------------------------------------------------------
        private bool TryPassGuard(ReactionContext context)
        {
            if (boundGuard == null)
            {
                return true;
            }

            try
            {
                return boundGuard.Check(context);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
                return false;
            }
        }

    #endregion

    #region 이벤트 핸들러

        // ------------------------------------------------------------
        /// <summary>
        /// 구독한 Signal Source의 발생을 Binding 실행으로 전달한다.
        /// </summary>
        // ------------------------------------------------------------
        private void _OnSignal(ReactionContext context)
        {
            HandleSignal(context);
        }

    #endregion
    }
}
