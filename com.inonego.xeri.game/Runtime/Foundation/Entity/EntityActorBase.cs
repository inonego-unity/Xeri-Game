/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : EntityActorBase.cs
수정일 : 2026-10-03

# 설명
Entity 모델을 Unity GameObject로 표현하는 Actor 베이스 클래스.
Actor 객체 수명과 분리된 Entity Attach/Detach 관계, Key 위임과 대칭 Bind/Unbind 훅을 제공한다.
========================================================================= BLOCK_HEADER_END */

using System;

using UnityEngine;

namespace inonego.Xeri.Game
{
    // ============================================================
    /// <summary>
    /// <br/> Entity 모델을 Unity Actor로 표현하기 위한 베이스 클래스.
    /// <br/> Awake와 OnEnable은 Entity 연결을 전제로 하지 않는다.
    /// <br/> Entity 의존 초기화와 정리는 전용 Actor 훅에서 수행한다.
    /// </summary>
    // ============================================================
    [Serializable]
    public abstract class EntityActorBase<TEntity> : MonoBehaviour, IEntityActor
    where TEntity : class, IEntity
    {

    #region 키 설정

        // ------------------------------------------------------------
        /// <summary>
        /// Entity의 키.
        /// </summary>
        // ------------------------------------------------------------
        public ulong Key
        {
            get
            {
                if (entity != null && entity.HasKey)
                {
                    return entity.Key;
                }

                throw new InvalidOperationException("키가 설정되어 있지 않습니다.");
            }
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Entity 키 설정 여부.
        /// </summary>
        // ------------------------------------------------------------
        public bool HasKey => entity != null && entity.HasKey;

    #endregion

    #region 필드

        // ------------------------------------------------------------
        /// <summary>
        /// 대응되는 논리 Entity. Detached 상태에서는 null이다.
        /// </summary>
        // ------------------------------------------------------------
        public TEntity Entity => entity;

        [NonSerialized]
        private TEntity entity = null;

        // ------------------------------------------------------------
        /// <summary>
        /// 현재 Actor가 Entity에 연결되어 있는지 여부.
        /// </summary>
        // ------------------------------------------------------------
        public bool IsBound => entity != null;

    #endregion

    #region 인터페이스 구현

        // ------------------------------------------------------------
        /// <summary>
        /// 외부 읽기 전용 계약으로 연결된 Entity를 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        IReadOnlyEntity IReadOnlyEntityActor.Entity => entity;

        // ------------------------------------------------------------
        /// <summary>
        /// 내부 Actor 계약으로 연결된 Entity를 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        IEntity IEntityActor.Entity => entity;

    #endregion

    #region Entity 연결

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/> Detached Actor를 Entity에 연결한다.
        /// <br/> Bind 훅 실패 시 같은 훅의 대칭 Unbind를 시도하고 Detached 상태로 복구한다.
        /// </summary>
        // --------------------------------------------------------------------------------
        internal void BindEntity(TEntity entity)
        {
            if (entity == null)
            {
                throw new ArgumentNullException(nameof(entity));
            }

            if (this.entity != null)
            {
                throw new InvalidOperationException
                (
                    "이미 Entity가 연결된 Actor에는 새 Entity를 연결할 수 없습니다."
                );
            }

            this.entity = entity;

            try
            {
                OnBindEntity(entity);
            }
            catch (Exception bindException)
            {
                try
                {
                    OnUnbindEntity(entity);
                }
                catch (Exception unbindException)
                {
                    this.entity = null;
                    throw new AggregateException
                    (
                        "Entity Actor 연결과 rollback 연결 해제가 모두 실패했습니다.",
                        bindException,
                        unbindException
                    );
                }

                this.entity = null;
                throw;
            }
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> 현재 Entity 연결을 해제해 Actor를 Detached 상태로 전환한다.
        /// <br/> Unbind 훅이 실패해도 owner 참조는 비워 관계 종료를 보장한다.
        /// </summary>
        // ----------------------------------------------------------------------
        internal void UnbindEntity()
        {
            var previous = entity;
            if (previous == null) return;

            try
            {
                OnUnbindEntity(previous);
            }
            finally
            {
                entity = null;
            }
        }

    #endregion

    #region 확장 훅

        // ------------------------------------------------------------
        /// <summary>
        /// Entity 참조가 설정된 뒤 Entity 의존 초기화를 수행한다.
        /// </summary>
        // ------------------------------------------------------------
        protected virtual void OnBindEntity(TEntity entity)
        {
            // NONE
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Entity 참조를 비우기 전에 연결 해제 처리를 수행한다.
        /// </summary>
        // ------------------------------------------------------------
        protected virtual void OnUnbindEntity(TEntity previousEntity)
        {
            // NONE
        }

    #endregion

    }
}
