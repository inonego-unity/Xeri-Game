/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : EntityBrain.cs
수정일 : 2026-08-16

# 설명
하나의 Entity를 판단 대상으로 삼는 AI Brain의 공통 수명과 실행 경계.
프로젝트별 판단 상태, 행동 선택과 실행 요청은 파생 구현이 소유한다.
========================================================================= BLOCK_HEADER_END */

using System;

using UnityEngine;

namespace inonego.Xeri.Game
{
    // ======================================================================
    /// <summary>
    /// 하나의 살아 있는 Entity에 바인딩되어 판단을 실행하는 추상 Brain.
    /// </summary>
    // ======================================================================
    [Serializable]
    public abstract class EntityBrain : IBindable<IReadOnlyEntity>
    {

    #region 필드

        // ------------------------------------------------------------
        /// <summary>
        /// 현재 바인딩된 Entity. Unbound 상태에서는 null.
        /// </summary>
        // ------------------------------------------------------------
        public IReadOnlyEntity Entity => entity;

        // ------------------------------------------------------------
        /// <summary>
        /// 현재 Entity에 바인딩되어 있는지 여부.
        /// </summary>
        // ------------------------------------------------------------
        public bool IsBound => entity != null;

        // ------------------------------------------------------------
        /// <summary>
        /// 현재 판단 실행이 활성화되어 있는지 여부.
        /// </summary>
        // ------------------------------------------------------------
        public bool IsActive => isActive;

        [NonSerialized]
        private IReadOnlyEntity entity = null;

        [SerializeField]
        private bool isActive = true;

    #endregion

    #region 메서드

        // ------------------------------------------------------------
        /// <summary>
        /// Spawned Entity를 현재 판단 대상으로 바인딩한다.
        /// </summary>
        // ------------------------------------------------------------
        public void Bind(IReadOnlyEntity entity)
        {
            if (entity == null)
            {
                throw new ArgumentNullException(nameof(entity));
            }

            if (this.entity != null)
            {
                throw new InvalidOperationException("EntityBrain이 이미 Entity에 바인딩되어 있습니다.");
            }

            if (entity.SpawnState != SpawnState.Spawned)
            {
                throw new InvalidOperationException
                (
                    $"Spawned Entity에만 Brain을 바인딩할 수 있습니다. 현재 상태: {entity.SpawnState}"
                );
            }

            this.entity = entity;

            try
            {
                OnBound(entity);
            }
            catch (Exception bindException)
            {
                this.entity = null;

                try
                {
                    OnUnbound(entity);
                }
                catch (Exception rollbackException)
                {
                    throw new AggregateException
                    (
                        "EntityBrain 바인딩과 rollback이 모두 실패했습니다.",
                        bindException,
                        rollbackException
                    );
                }

                throw;
            }
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 현재 Entity 바인딩을 해제한다.
        /// </summary>
        // ------------------------------------------------------------
        public void Unbind()
        {
            var entity = this.entity;
            if (entity == null) return;

            // 외부 cleanup이 실패해도 Core의 바인딩 상태는 terminal 해제한다.
            this.entity = null;
            OnUnbound(entity);
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
        /// <br/> Entity가 Spawned 상태를 벗어난 뒤에는 새 판단을 실행하지 않는다.
        /// </summary>
        // ----------------------------------------------------------------------
        public void Tick(float deltaTime)
        {
            var entity = this.entity;

            if
            (
                entity == null ||
                !isActive ||
                entity.SpawnState != SpawnState.Spawned
            )
            {
                return;
            }

            OnTick(deltaTime);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Entity 바인딩이 완료된 뒤 호출된다.
        /// </summary>
        // ------------------------------------------------------------
        protected virtual void OnBound(IReadOnlyEntity entity)
        {
            // NONE
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Entity 바인딩이 해제된 뒤 호출된다.
        /// </summary>
        // ------------------------------------------------------------
        protected virtual void OnUnbound(IReadOnlyEntity entity)
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

    }
}
