/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : GroupBrain.cs
수정일 : 2026-08-16

# 설명
하나의 AIGroup을 판단 대상으로 삼는 Group Brain의 공통 수명과 실행 경계.
공유 인지 상태, 구성원 지시 형식과 프로젝트별 집단 정책은 파생 구현이 소유한다.
========================================================================= BLOCK_HEADER_END */

using System;

using UnityEngine;

namespace inonego.Xeri.Game
{
    // ======================================================================
    /// <summary>
    /// 하나의 AIGroup에 바인딩되어 집단 단위 판단을 실행하는 추상 Brain.
    /// </summary>
    // ======================================================================
    [Serializable]
    public abstract class GroupBrain : IBindable<IReadOnlyAIGroup>
    {

    #region 필드

        // ------------------------------------------------------------
        /// <summary>
        /// 현재 바인딩된 AIGroup. Unbound 상태에서는 null.
        /// </summary>
        // ------------------------------------------------------------
        public IReadOnlyAIGroup AIGroup => aiGroup;

        // ------------------------------------------------------------
        /// <summary>
        /// 현재 AIGroup에 바인딩되어 있는지 여부.
        /// </summary>
        // ------------------------------------------------------------
        public bool IsBound => aiGroup != null;

        // ------------------------------------------------------------
        /// <summary>
        /// 현재 판단 실행이 활성화되어 있는지 여부.
        /// </summary>
        // ------------------------------------------------------------
        public bool IsActive => isActive;

        [NonSerialized]
        private IReadOnlyAIGroup aiGroup = null;

        [SerializeField]
        private bool isActive = true;

    #endregion

    #region 메서드

        // ------------------------------------------------------------
        /// <summary>
        /// 유효한 AIGroup을 현재 집단 판단 대상으로 바인딩한다.
        /// </summary>
        // ------------------------------------------------------------
        public void Bind(IReadOnlyAIGroup aiGroup)
        {
            if (aiGroup == null)
            {
                throw new ArgumentNullException(nameof(aiGroup));
            }

            if (this.aiGroup != null)
            {
                throw new InvalidOperationException("GroupBrain이 이미 AIGroup에 바인딩되어 있습니다.");
            }

            if (aiGroup.IsDisposed)
            {
                throw new ObjectDisposedException(nameof(aiGroup));
            }

            this.aiGroup = aiGroup;
            aiGroup.OnDisposed += _OnAIGroupDisposed;

            try
            {
                OnBound(aiGroup);
            }
            catch (Exception bindException)
            {
                aiGroup.OnDisposed -= _OnAIGroupDisposed;
                this.aiGroup = null;

                try
                {
                    OnUnbound(aiGroup);
                }
                catch (Exception rollbackException)
                {
                    throw new AggregateException
                    (
                        "GroupBrain 바인딩과 rollback이 모두 실패했습니다.",
                        bindException,
                        rollbackException
                    );
                }

                throw;
            }
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 현재 AIGroup 바인딩을 해제한다.
        /// </summary>
        // ------------------------------------------------------------
        public void Unbind()
        {
            var aiGroup = this.aiGroup;
            if (aiGroup == null) return;

            // 자동 Group 종료 callback을 먼저 끊고 Core의 바인딩 상태를 terminal 해제한다.
            aiGroup.OnDisposed -= _OnAIGroupDisposed;
            this.aiGroup = null;
            OnUnbound(aiGroup);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 판단 실행 활성 상태를 변경한다.
        /// </summary>
        // ------------------------------------------------------------
        public void SetActive(bool isActive)
        {
            if (this.isActive == isActive) return;

            this.isActive = isActive;
            OnActiveChanged(isActive);
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> 현재 바인딩과 활성 상태가 유효할 때 파생 Brain의 판단을 실행한다.
        /// <br/> AIGroup 수명이 종료된 뒤에는 새 판단을 실행하지 않는다.
        /// </summary>
        // ----------------------------------------------------------------------
        public void Tick(float deltaTime)
        {
            var aiGroup = this.aiGroup;

            if
            (
                aiGroup == null ||
                !isActive ||
                aiGroup.IsDisposed
            )
            {
                return;
            }

            OnTick(deltaTime);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// AIGroup 바인딩이 완료된 뒤 호출된다.
        /// </summary>
        // ------------------------------------------------------------
        protected virtual void OnBound(IReadOnlyAIGroup aiGroup)
        {
            // NONE
        }

        // ------------------------------------------------------------
        /// <summary>
        /// AIGroup 바인딩이 해제된 뒤 호출된다.
        /// </summary>
        // ------------------------------------------------------------
        protected virtual void OnUnbound(IReadOnlyAIGroup aiGroup)
        {
            // NONE
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 판단 실행 활성 상태가 변경된 뒤 호출된다.
        /// </summary>
        // ------------------------------------------------------------
        protected virtual void OnActiveChanged(bool isActive)
        {
            // NONE
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 파생 Brain의 한 Tick 판단을 실행한다.
        /// </summary>
        // ------------------------------------------------------------
        protected abstract void OnTick(float deltaTime);

    #endregion

    #region 이벤트 핸들러

        // ------------------------------------------------------------
        /// <summary>
        /// 바인딩된 AIGroup 수명이 종료되면 Brain 바인딩도 해제한다.
        /// </summary>
        // ------------------------------------------------------------
        private void _OnAIGroupDisposed(IReadOnlyAIGroup aiGroup)
        {
            if (!ReferenceEquals(this.aiGroup, aiGroup)) return;

            Unbind();
        }

    #endregion

    }
}
