/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : IEntityPresentationProvider.cs
수정일 : 2026-11-16

# 설명
Entity Actor 수명에 Presentation 종류 하나의 생성·반환과 Entity Rebind 대응을 연결하는 공용 계약.
생성 실패 중 부분 획득한 자원도 SpawnRollback 반환에 참여한다.
구체 Presentation의 Attach/Detach, 독립 Despawn 정책과 source Provider routing은 구현이 소유한다.

# 특이사항, 제약사항
같은 Actor에 대한 콜백은 중첩 호출되지 않지만, 콜백 중 다른 Actor의 수명 작업이 같은 Provider 인스턴스의
콜백을 다시 호출할 수 있다. Provider는 Actor별 상태를 분리하고 Actor 간 재진입에 안전해야 한다.
========================================================================= BLOCK_HEADER_END */

namespace inonego.Xeri.Game
{
    // ================================================================================
    /// <summary>
    /// <br/> Entity Actor의 생성·반환과 Entity Rebind에 Presentation 종류 하나를
    /// <br/> 참여시키는 수명 계약.
    /// <br/> 같은 Actor의 콜백은 중첩되지 않으나 다른 Actor의 콜백은 중첩될 수 있다.
    /// </summary>
    // ================================================================================
    public interface IEntityPresentationProvider<TEntityActor, TEntity>
    where TEntityActor : EntityActorBase<TEntity>
    where TEntity : class, IEntity
    {
        // ------------------------------------------------------------
        /// <summary>
        /// <br/> Presentation 자원을 획득한다.
        /// <br/> 생성 실패 시에도 OnActorReleasing으로 부분 정리한다.
        /// </summary>
        // ------------------------------------------------------------
        public void OnActorCreated(TEntity entity, TEntityActor actor);

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> Actor terminal 반환과 실패한 생성의 부분 자원 정리에 참여한다.
        /// <br/> 생성 도중 예외가 발생했어도 안전하게 호출할 수 있어야 한다.
        /// <br/> 반환 예외는 집계하며 같은 수명에서는 자동 재시도하지 않는다.
        /// </summary>
        // ----------------------------------------------------------------------
        public void OnActorReleasing
        (
            TEntity entity,
            TEntityActor actor,
            DespawnReason reason
        );

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/> 동일 Actor가 previous Entity에서 next Entity로 Rebind된 뒤 필요한
        /// <br/> Presentation 관계·구성을 갱신한다.
        /// <br/> 중간 실패한 Provider까지 역순 Rollback을 호출하므로 부분 변경도 복원해야 한다.
        /// </summary>
        // --------------------------------------------------------------------------------
        public void OnActorRebound
        (
            TEntity previousEntity,
            TEntity nextEntity,
            TEntityActor actor
        );

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> 자신 또는 이후 Provider의 Rebind 실패 시 시도된
        /// <br/> Rebind를 previous Entity 기준으로 복구한다.
        /// </summary>
        // ------------------------------------------------------------
        public void OnActorRebindRollback
        (
            TEntity previousEntity,
            TEntity nextEntity,
            TEntityActor actor
        );

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> 동일 Presentation 종류의 concrete owner를 previous Actor에서
        /// <br/> next Actor로 이전한다.
        /// </summary>
        // ----------------------------------------------------------------------
        public void OnPresentationOwnerRebound
        (
            TEntityActor previousOwner,
            TEntityActor nextOwner
        );

        // --------------------------------------------------------------------------------
        /// <summary>
        /// 자신 또는 이후 Provider 실패 시 부분 변경을 포함해 previous Actor로 복원한다.
        /// </summary>
        // --------------------------------------------------------------------------------
        public void OnPresentationOwnerRebindRollback
        (
            TEntityActor previousOwner,
            TEntityActor nextOwner
        );
    }
}
