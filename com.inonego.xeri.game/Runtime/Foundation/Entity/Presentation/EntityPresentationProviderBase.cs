/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : EntityPresentationProviderBase.cs
수정일 : 2026-11-16

# 설명
IEntityPresentationProvider 구현의 기반 클래스.
생성·반환만 필수로 구현하고 Entity Rebind와 Presentation Owner 이전 콜백은 아무 일도 하지 않는 기본 구현을 제공한다.

# 특이사항, 제약사항
같은 Actor의 콜백은 중첩되지 않지만 다른 Actor의 콜백은 중첩될 수 있으므로 파생 클래스는 Actor별 상태를 분리해야 한다.
========================================================================= BLOCK_HEADER_END */

namespace inonego.Xeri.Game
{
    // ======================================================================
    /// <summary>
    /// <br/> 생성·반환만 필수로 구현하는 Presentation Provider 기반 클래스.
    /// <br/> Rebind 계열 콜백은 기본적으로 아무 일도 하지 않는다.
    /// </summary>
    // ======================================================================
    public abstract class EntityPresentationProviderBase<TEntityActor, TEntity> :
        IEntityPresentationProvider<TEntityActor, TEntity>
    where TEntityActor : EntityActorBase<TEntity>
    where TEntity : class, IEntity
    {

    #region 필수 구현

        // ------------------------------------------------------------
        /// <summary>
        /// Presentation 자원을 획득한다.
        /// </summary>
        // ------------------------------------------------------------
        public abstract void OnActorCreated(TEntity entity, TEntityActor actor);

        // ------------------------------------------------------------
        /// <summary>
        /// Actor terminal 반환과 실패한 생성의 부분 자원을 정리한다.
        /// </summary>
        // ------------------------------------------------------------
        public abstract void OnActorReleasing
        (
            TEntity entity,
            TEntityActor actor,
            DespawnReason reason
        );

    #endregion

    #region 선택 구현

        // ------------------------------------------------------------
        /// <summary>
        /// Entity Rebind 뒤 갱신이 필요 없으면 아무 일도 하지 않는다.
        /// </summary>
        // ------------------------------------------------------------
        public virtual void OnActorRebound
        (
            TEntity previousEntity,
            TEntity nextEntity,
            TEntityActor actor
        )
        {
            // NONE
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Entity Rebind 복구가 필요 없으면 아무 일도 하지 않는다.
        /// </summary>
        // ------------------------------------------------------------
        public virtual void OnActorRebindRollback
        (
            TEntity previousEntity,
            TEntity nextEntity,
            TEntityActor actor
        )
        {
            // NONE
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Presentation Owner 이전이 필요 없으면 아무 일도 하지 않는다.
        /// </summary>
        // ------------------------------------------------------------
        public virtual void OnPresentationOwnerRebound
        (
            TEntityActor previousOwner,
            TEntityActor nextOwner
        )
        {
            // NONE
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Presentation Owner 이전 복구가 필요 없으면 아무 일도 하지 않는다.
        /// </summary>
        // ------------------------------------------------------------
        public virtual void OnPresentationOwnerRebindRollback
        (
            TEntityActor previousOwner,
            TEntityActor nextOwner
        )
        {
            // NONE
        }

    #endregion

    }
}
