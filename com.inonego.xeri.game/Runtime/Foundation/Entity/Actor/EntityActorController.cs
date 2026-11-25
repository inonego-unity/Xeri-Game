/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : EntityActorController.cs
수정일 : 2026-11-16

# 설명
Entity Actor Factory와 Registry를 조립해 Actor 객체 수명과 Entity binding 수명을 분리 관리한다.
Actor는 Registered 상태에서 Detached 또는 Bound(Entity)일 수 있으며 Attach/Detach/Rebind를 명시적으로 지원한다.
Entity SpawnRegistry와의 자동 연결은 제공하지 않는다. 자동 Actor 구성은 EntityActorRuntimeBinding이 담당한다.

# 특이사항, 제약사항
Runtime 결합 시 직접 변경을 차단한다. 단독 Controller의 수명 작업 단위는 개별 Actor이며 같은 Actor의 중첩 변경만 거절한다.
서로 다른 Actor의 수명 작업은 Factory·Actor 훅 중에도 허용한다.
ReleaseAll은 호출 시점에 등록된 Actor 수명만 반환하며 반환 중 풀에서 재사용된 같은 인스턴스의 새 수명은 대상이 아니다.
========================================================================= BLOCK_HEADER_END */

using System;
using System.Collections;
using System.Collections.Generic;

namespace inonego.Xeri.Game
{
    // ================================================================================
    /// <summary>
    /// <br/> Entity Actor의 Create/Release 객체 수명과 Attach/Detach/Rebind 관계 수명을
    /// <br/> 관리하는 공용 Controller.
    /// </summary>
    // ================================================================================
    [Serializable]
    public class EntityActorController<TEntityActor, TEntity, TContext>
    where TEntityActor : EntityActorBase<TEntity>
    where TEntity : class, IEntity
    {

    #region 필드

        // Runtime과 결합한 Controller의 모든 객체·관계 변경은 해당 Runtime만 수행한다.
        [NonSerialized]
        private bool isRuntimeOwned = false;

        // 단독 Controller에서 수명 작업이 진행 중인 Actor. 같은 Actor의 중첩 변경만 차단한다.
        [NonSerialized]
        private readonly HashSet<TEntityActor> processingActors = new
        (
            ReferenceEqualityComparer<TEntityActor>.Instance
        );

        // 아직 Actor가 확정되지 않은 명시적 생성 작업 수.
        [NonSerialized]
        private int creatingCount = 0;

        // ------------------------------------------------------------
        /// <summary>
        /// 현재 Controller가 객체 수명을 추적하는 모든 Actor.
        /// </summary>
        // ------------------------------------------------------------
        public ReadOnlySetView<TEntityActor> Actors => registry.Actors;

        // ------------------------------------------------------------
        /// <summary>
        /// 현재 Entity에 연결된 Actor.
        /// </summary>
        // ------------------------------------------------------------
        public IEnumerable<TEntityActor> BoundActors => registry.BoundActors;

        // ------------------------------------------------------------
        /// <summary>
        /// 현재 Entity에 연결되지 않은 Detached Actor.
        /// </summary>
        // ------------------------------------------------------------
        public IEnumerable<TEntityActor> DetachedActors => registry.DetachedActors;

        [NonSerialized]
        private readonly IEntityActorFactory<TEntityActor, TEntity, TContext> factory = null;

        [NonSerialized]
        private readonly EntityActorRegistry<TEntityActor, TEntity> registry = null;

    #endregion

    #region 생성자

        // ------------------------------------------------------------
        /// <summary>
        /// Factory와 Actor Registry를 주입받는다.
        /// </summary>
        // ------------------------------------------------------------
        public EntityActorController
        (
            IEntityActorFactory<TEntityActor, TEntity, TContext> factory,
            EntityActorRegistry<TEntityActor, TEntity> registry = null
        )
        {
            this.factory = factory ?? throw new ArgumentNullException(nameof(factory));
            this.registry = registry ?? new EntityActorRegistry<TEntityActor, TEntity>();
            this.registry.ClaimController();
        }

    #endregion

    #region 명시적 객체 수명

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/> Detached Actor를 생성하고 Controller 객체 수명에 등록한다.
        /// <br/> Entity 매개변수는 Provider/Prepare 선택에만 사용하며 binding은 만들지 않는다.
        /// </summary>
        // --------------------------------------------------------------------------------
        public TEntityActor Create
        (
            TEntity entity,
            in TContext context
        )
        {
            EnsureStandalone();
            creatingCount++;

            try
            {
                return CreateCore(entity, context);
            }
            finally
            {
                creatingCount--;
            }
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 동일 Actor 객체 수명을 terminal 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        public bool Release(TEntityActor actor, DespawnReason reason)
        {
            EnsureStandalone();
            return ReleaseGuarded(actor, reason);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Entity에 현재 연결된 모든 Actor를 terminal 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        public void ReleaseAll(TEntity entity, DespawnReason reason)
        {
            EnsureStandalone();
            ReleaseActors(registry.FindAll(entity), reason);
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// Controller가 추적하는 모든 Bound/Detached Actor를 terminal 반환한다.
        /// </summary>
        // ----------------------------------------------------------------------
        public void ReleaseAll(DespawnReason reason)
        {
            EnsureStandalone();
            ReleaseActors(registry.Actors, reason);
        }

    #endregion

    #region 명시적 Entity 연결 수명

        // ------------------------------------------------------------
        /// <summary>
        /// 등록된 Detached Actor를 Entity에 연결한다.
        /// </summary>
        // ------------------------------------------------------------
        public bool Attach(TEntityActor actor, TEntity entity)
        {
            EnsureStandalone();
            BeginActorChange(actor);

            try
            {
                return AttachCore(actor, entity);
            }
            finally
            {
                EndActorChange(actor);
            }
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Bound Actor를 Entity에서 분리해 Detached 상태로 유지한다.
        /// </summary>
        // ------------------------------------------------------------
        public bool Detach(TEntityActor actor)
        {
            EnsureStandalone();
            BeginActorChange(actor);

            try
            {
                return DetachCore(actor);
            }
            finally
            {
                EndActorChange(actor);
            }
        }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/> 같은 Actor 객체를 terminal 반환하지 않고 현재 Entity에서 다음 Entity로
        /// <br/> 원자적으로 재연결한다.
        /// </summary>
        // --------------------------------------------------------------------------------
        public bool Rebind(TEntityActor actor, TEntity nextEntity)
        {
            EnsureStandalone();
            BeginActorChange(actor);

            try
            {
                return RebindCore(actor, nextEntity);
            }
            finally
            {
                EndActorChange(actor);
            }
        }

    #endregion

    #region 조회

        // ------------------------------------------------------------
        /// <summary>
        /// Entity에 대응하는 모든 Bound Actor를 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        public IReadOnlyList<TEntityActor> FindAll(TEntity entity) => registry.FindAll(entity);

        // ------------------------------------------------------------
        /// <summary>
        /// Entity에 대응하는 Bound Actor가 하나일 때만 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        public bool TryFindSingle(TEntity entity, out TEntityActor actor) =>
            registry.TryFindSingle(entity, out actor);

        // ------------------------------------------------------------
        /// <summary>
        /// 동일 Actor 객체 수명을 현재 Controller가 추적하는지 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        public bool Contains(TEntityActor actor) => registry.Contains(actor);

        // ------------------------------------------------------------
        /// <summary>
        /// 동일 Actor가 현재 Entity에 연결되어 있는지 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        public bool IsBound(TEntityActor actor) => registry.IsBound(actor);

        // ------------------------------------------------------------
        /// <summary>
        /// 동일 Actor의 현재 Entity owner를 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        public bool TryGetEntity(TEntityActor actor, out TEntity entity) =>
            registry.TryGetEntity(actor, out entity);

    #endregion

    #region Runtime 소유권

        // ------------------------------------------------------------
        /// <summary>
        /// 결합 Runtime에 Actor 객체 수명의 변경 권한을 위임한다.
        /// </summary>
        // ------------------------------------------------------------
        internal void ClaimRuntime()
        {
            if
            (
                isRuntimeOwned ||
                creatingCount != 0 ||
                processingActors.Count != 0 ||
                registry.Count != 0
            )
            {
                throw new InvalidOperationException
                (
                    "수명 작업이 없는 빈 독립 Controller만 하나의 Runtime에 결합할 수 있습니다."
                );
            }

            isRuntimeOwned = true;
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// Runtime 결합 실패 시 아직 생성하지 않은 Controller의 소유권을 되돌린다.
        /// </summary>
        // ----------------------------------------------------------------------
        internal void ReleaseRuntimeClaim()
        {
            if (registry.Count == 0)
            {
                isRuntimeOwned = false;
            }
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Runtime과 결합한 Controller에 독립 변경을 허용하지 않는다.
        /// </summary>
        // ------------------------------------------------------------
        private void EnsureStandalone()
        {
            if (isRuntimeOwned)
            {
                throw new InvalidOperationException("Runtime에 결합한 Actor Controller는 직접 변경할 수 없습니다.");
            }
        }

    #endregion

    #region 객체 수명 내부

        // ------------------------------------------------------------
        /// <summary>
        /// Detached Actor를 생성하고 Registry에 객체 수명을 등록한다.
        /// </summary>
        // ------------------------------------------------------------
        internal TEntityActor CreateCore
        (
            TEntity entity,
            in TContext context
        )
        {
            var actor = factory.Create(entity, context);

            // Factory는 이전 Release가 끝나기 전 같은 인스턴스를 새 수명으로 재발급하면 안 된다.
            // 이 경우 중첩 Release로 복구하지 않고 즉시 거절해 Controller Registry 오염을 막는다.
            if (!ReferenceEquals(actor, null) && processingActors.Contains(actor))
            {
                throw new InvalidOperationException
                (
                    "반환 중인 Entity Actor 인스턴스를 새 수명으로 다시 생성할 수 없습니다."
                );
            }

            try
            {
                registry.Register(actor);
                return actor;
            }
            catch (Exception registerException)
            {
                try
                {
                    factory.Release(actor, DespawnReason.SpawnRollback);
                }
                catch (Exception releaseException)
                {
                    throw new AggregateException
                    (
                        "Entity Actor 객체 등록과 rollback 반환이 모두 실패했습니다.",
                        registerException,
                        releaseException
                    );
                }

                throw;
            }
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> Bound 상태면 먼저 Detach한 뒤 Registry 객체 등록과 Factory 자원을
        /// <br/> terminal 반환한다. 각 정리는 가능한 범위까지 끝까지 시도한다.
        /// </summary>
        // ----------------------------------------------------------------------
        internal bool ReleaseCore(TEntityActor actor, DespawnReason reason)
        {
            if (actor == null || !registry.Contains(actor))
            {
                return false;
            }

            var failures = new ExceptionBuffer();

            if (registry.IsBound(actor))
            {
                try
                {
                    DetachCore(actor);
                }
                catch (Exception exception)
                {
                    failures.Add(exception);
                }
            }

            try
            {
                registry.Unregister(actor);
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }

            try
            {
                factory.Release(actor, reason);
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }

            failures.ThrowWhenAny("Entity Actor terminal 반환 중 일부 정리가 실패했습니다.");

            return true;
        }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/> 호출 시점에 등록된 Actor 수명 전체의 terminal 반환을 끝까지 시도한다.
        /// <br/> 반환 중 풀에서 재사용돼 다시 등록된 같은 인스턴스는 새 수명이므로 반환하지 않는다.
        /// </summary>
        // --------------------------------------------------------------------------------
        private void ReleaseActors(IEnumerable<TEntityActor> source, DespawnReason reason)
        {
            // 원본은 반환 중 바뀌는 View이므로 참조와 등록 수명 번호를 먼저 고정한다.
            var targets = new List<(TEntityActor actor, Revision generation)>();

            foreach (var actor in source)
            {
                targets.Add((actor, registry.GetRegistrationGeneration(actor)));
            }

            var failures = new ExceptionBuffer();

            foreach (var (actor, generation) in targets)
            {
                try
                {
                    // 중첩 호출이 이미 반환했거나 새 수명으로 다시 등록된 Actor는 건너뛴다.
                    if (registry.GetRegistrationGeneration(actor) != generation)
                    {
                        continue;
                    }

                    ReleaseGuarded(actor, reason);
                }
                catch (Exception exception)
                {
                    failures.Add(exception);
                }
            }

            failures.ThrowWhenAny("일부 Entity Actor 반환에 실패했습니다.");
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// 같은 Actor의 수명 작업 중이면 거절하고, 아니면 terminal 반환을 실행한다.
        /// </summary>
        // ----------------------------------------------------------------------
        private bool ReleaseGuarded(TEntityActor actor, DespawnReason reason)
        {
            BeginActorChange(actor);

            try
            {
                return ReleaseCore(actor, reason);
            }
            finally
            {
                EndActorChange(actor);
            }
        }

    #endregion

    #region Entity 관계 내부

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/> Detached Actor의 concrete Bind 훅을 먼저 완료하고 Registry owner mapping을
        /// <br/> 확정한다. Registry 반영 실패 시 concrete binding을 rollback한다.
        /// </summary>
        // --------------------------------------------------------------------------------
        internal bool AttachCore(TEntityActor actor, TEntity entity)
        {
            if (entity == null)
            {
                throw new ArgumentNullException(nameof(entity));
            }

            if
            (
                actor == null ||
                !registry.Contains(actor) ||
                registry.IsBound(actor)
            )
            {
                return false;
            }

            actor.BindEntity(entity);

            try
            {
                registry.Attach(actor, entity);
                return true;
            }
            catch (Exception registerException)
            {
                try
                {
                    actor.UnbindEntity();
                }
                catch (Exception unbindException)
                {
                    throw new AggregateException
                    (
                        "Entity Actor Registry Attach와 concrete rollback Unbind가 모두 실패했습니다.",
                        registerException,
                        unbindException
                    );
                }

                throw;
            }
        }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/> concrete Entity binding을 먼저 종료하고 Registry mapping을 제거해 Actor를
        /// <br/> Detached 상태로 유지한다.
        /// </summary>
        // --------------------------------------------------------------------------------
        private bool DetachCore(TEntityActor actor)
        {
            if
            (
                actor == null ||
                !registry.TryGetEntity(actor, out _)
            )
            {
                return false;
            }

            Exception unbindException = null;

            try
            {
                actor.UnbindEntity();
            }
            catch (Exception exception)
            {
                unbindException = exception;
            }

            registry.Detach(actor);

            if (unbindException != null)
            {
                throw unbindException;
            }

            return true;
        }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/> Bound Actor를 old Entity에서 Detach한 뒤 next Entity에 Attach한다.
        /// <br/> 어느 단계든 실패하면 old Entity 관계 복원을 시도한다.
        /// </summary>
        // --------------------------------------------------------------------------------
        internal bool RebindCore(TEntityActor actor, TEntity nextEntity)
        {
            if (nextEntity == null)
            {
                throw new ArgumentNullException(nameof(nextEntity));
            }

            if
            (
                actor == null ||
                !registry.TryGetEntity(actor, out var previousEntity)
            )
            {
                return false;
            }

            if (ReferenceEquals(previousEntity, nextEntity))
            {
                return true;
            }

            try
            {
                DetachCore(actor);
            }
            catch (Exception detachException)
            {
                try
                {
                    AttachCore(actor, previousEntity);
                }
                catch (Exception rollbackException)
                {
                    throw new AggregateException
                    (
                        "Entity Actor Detach 실패와 old Entity rollback Attach가 모두 실패했습니다.",
                        detachException,
                        rollbackException
                    );
                }

                throw;
            }

            try
            {
                if (!AttachCore(actor, nextEntity))
                {
                    throw new InvalidOperationException("Detached Actor를 다음 Entity에 Attach할 수 없습니다.");
                }

                return true;
            }
            catch (Exception attachException)
            {
                try
                {
                    if (!AttachCore(actor, previousEntity))
                    {
                        throw new InvalidOperationException("Entity Actor를 이전 Entity에 다시 Attach할 수 없습니다.");
                    }
                }
                catch (Exception rollbackException)
                {
                    throw new AggregateException
                    (
                        "Entity Actor Rebind와 old Entity rollback이 모두 실패했습니다.",
                        attachException,
                        rollbackException
                    );
                }

                throw;
            }
        }

    #endregion

    #region 수명 작업 보호

        // ----------------------------------------------------------------------
        /// <summary>
        /// 같은 Actor의 수명 작업이 진행 중이면 거절하고 Actor 작업을 시작한다.
        /// </summary>
        // ----------------------------------------------------------------------
        private void BeginActorChange(TEntityActor actor)
        {
            if (ReferenceEquals(actor, null))
            {
                return;
            }

            if (!processingActors.Add(actor))
            {
                throw new InvalidOperationException("수명 작업 중인 Actor에는 다른 수명 작업을 시작할 수 없습니다.");
            }
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Actor의 수명 작업 보호를 종료한다.
        /// </summary>
        // ------------------------------------------------------------
        private void EndActorChange(TEntityActor actor)
        {
            if (!ReferenceEquals(actor, null))
            {
                processingActors.Remove(actor);
            }
        }

    #endregion

    }

    // ======================================================================
    /// <summary>
    /// 생성 Context가 필요 없는 Entity Actor 수명을 관리하는 Controller.
    /// </summary>
    // ======================================================================
    [Serializable]
    public sealed class EntityActorController<TEntityActor, TEntity> :
        EntityActorController<TEntityActor, TEntity, EntityActorNoContext>
    where TEntityActor : EntityActorBase<TEntity>
    where TEntity : class, IEntity
    {

    #region 생성자

        // ------------------------------------------------------------
        /// <summary>
        /// Context 없는 Factory와 Actor Registry를 주입받는다.
        /// </summary>
        // ------------------------------------------------------------
        public EntityActorController
        (
            IEntityActorFactory<TEntityActor, TEntity, EntityActorNoContext> factory,
            EntityActorRegistry<TEntityActor, TEntity> registry = null
        ) : base(factory, registry)
        {
            // NONE
        }

    #endregion

    #region 명시적 객체 수명

        // ------------------------------------------------------------
        /// <summary>
        /// Entity를 생성 source로 사용하는 Detached Actor를 생성한다.
        /// </summary>
        // ------------------------------------------------------------
        public TEntityActor Create(TEntity entity)
        {
            var context = default(EntityActorNoContext);
            return base.Create(entity, context);
        }

    #endregion

    }
}
