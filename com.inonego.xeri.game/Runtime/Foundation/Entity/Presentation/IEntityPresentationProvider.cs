/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : IEntityPresentationProvider.cs
수정일 : 2026-10-03

# 설명
Entity Actor 수명에 Presentation 종류 하나의 생성·반환과 Entity Transfer 대응을 연결하는 공용 계약.
구체 Presentation의 Attach/Detach, 독립 Despawn 정책과 source Provider routing은 구현이 소유한다.
========================================================================= BLOCK_HEADER_END */

namespace inonego.Xeri.Game
{
    // ================================================================================
    /// <summary>
    /// <br/> Entity Actor의 생성·반환과 Entity Transfer에 Presentation 종류 하나를
    /// <br/> 참여시키는 수명 계약.
    /// </summary>
    // ================================================================================
    public interface IEntityPresentationProvider<TEntityActor, TEntity>
    where TEntityActor : EntityActorBase<TEntity>
    where TEntity : class, IEntity
    {
        public void OnActorCreated(TEntity entity, TEntityActor actor);

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/> Actor terminal 반환에 Presentation을 참여시킨다.
        /// <br/> Entity에 연결된 Actor는 현재 Entity를 전달하고 Detached Actor terminal
        /// <br/> 반환에서는 null을 전달할 수 있다.
        /// </summary>
        // --------------------------------------------------------------------------------
        public void OnActorReleasing
        (
            TEntity entity,
            TEntityActor actor,
            DespawnReason reason
        );

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/> 동일 Actor가 previous Entity에서 next Entity로 Transfer된 뒤 필요한
        /// <br/> Presentation 관계·구성을 갱신한다.
        /// <br/> 예외를 던지는 구현은 자신이 시작한 부분 변경을 호출 전 상태로 복구해야 한다.
        /// </summary>
        // --------------------------------------------------------------------------------
        public void OnActorTransferred
        (
            TEntity previousEntity,
            TEntity nextEntity,
            TEntityActor actor
        );

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> 이후 Provider의 Transfer 실패로 transaction이 되돌아갈 때 완료된
        /// <br/> Transfer를 previous Entity 기준으로 복구한다.
        /// </summary>
        // ----------------------------------------------------------------------
        public void OnActorTransferRollback
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
        public void OnPresentationOwnerTransferred
        (
            TEntityActor previousOwner,
            TEntityActor nextOwner
        );

        // --------------------------------------------------------------------------------
        /// <summary>
        /// 이후 Provider 실패로 owner transfer가 되돌아갈 때 previous Actor 기준으로 복구한다.
        /// </summary>
        // --------------------------------------------------------------------------------
        public void OnPresentationOwnerTransferRollback
        (
            TEntityActor previousOwner,
            TEntityActor nextOwner
        );
    }
}
