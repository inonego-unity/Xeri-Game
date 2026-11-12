/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : EntityActorController.cs
수정일 : 2026-10-03

# 설명
Entity Actor Factory와 Registry를 조립해 Actor 객체 수명과 Entity binding 수명을 분리 관리한다.
Actor는 Registered 상태에서 Detached 또는 Bound(Entity)일 수 있으며 Attach/Detach/Rebind를 명시적으로 지원한다.
선택적으로 EntitySpawnRegistry에 연결하면 Entity 수명에 맞춰 Bound Actor 하나를 자동 생성·반환한다.
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
    public class EntityActorController<TEntityActor, TEntity, TContext> :
        IBindable<EntitySpawnRegistryBase<TEntity>>,
        ISpawnRegistryBinding<ulong, TEntity>
    where TEntityActor : EntityActorBase<TEntity>
    where TEntity : class, IEntity
    {

    #region 필드

        // ------------------------------------------------------------
        /// <summary>
        /// 현재 자동 동기화 중인 Entity Registry.
        /// </summary>
        // ------------------------------------------------------------
        public EntitySpawnRegistryBase<TEntity> BoundRegistry => boundRegistry;

        [NonSerialized]
        private EntitySpawnRegistryBase<TEntity> boundRegistry = null;

        // ------------------------------------------------------------
        /// <summary>
        /// 현재 Controller가 객체 수명을 추적하는 모든 Actor.
        /// </summary>
        // ------------------------------------------------------------
        public IEnumerable<TEntityActor> Actors => registry.Actors;

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

        [NonSerialized]
        private Func<ulong, TEntity, TContext> contextResolver = null;

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
            EnsureUnbound();
            return CreateCore(entity, context);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 동일 Actor 객체 수명을 terminal 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        public bool Release(TEntityActor actor, DespawnReason reason)
        {
            EnsureUnbound();
            return ReleaseCore(actor, reason);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Entity에 현재 연결된 모든 Actor를 terminal 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        public void ReleaseAll(TEntity entity, DespawnReason reason)
        {
            EnsureUnbound();
            ReleaseAllCore(entity, reason);
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// Controller가 추적하는 모든 Bound/Detached Actor를 terminal 반환한다.
        /// </summary>
        // ----------------------------------------------------------------------
        public void ReleaseAll(DespawnReason reason)
        {
            EnsureUnbound();
            ReleaseAllCore(reason);
        }

    #endregion

    #region 명시적 Binding 수명

        // ------------------------------------------------------------
        /// <summary>
        /// 등록된 Detached Actor를 Entity에 연결한다.
        /// </summary>
        // ------------------------------------------------------------
        public bool Attach(TEntityActor actor, TEntity entity)
        {
            EnsureUnbound();
            return AttachCore(actor, entity);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Bound Actor를 Entity에서 분리해 Detached 상태로 유지한다.
        /// </summary>
        // ------------------------------------------------------------
        public bool Detach(TEntityActor actor)
        {
            EnsureUnbound();
            return DetachCore(actor);
        }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/> 같은 Actor 객체를 terminal 반환하지 않고 현재 Entity에서 다음 Entity로
        /// <br/> 원자적으로 재연결한다.
        /// </summary>
        // --------------------------------------------------------------------------------
        public bool Rebind(TEntityActor actor, TEntity nextEntity)
        {
            EnsureUnbound();
            return RebindCore(actor, nextEntity);
        }

    #endregion

    #region Entity Registry 바인딩

        // ------------------------------------------------------------
        /// <summary>
        /// 기본 Context로 Entity Registry에 바인딩한다.
        /// </summary>
        // ------------------------------------------------------------
        public void Bind(EntitySpawnRegistryBase<TEntity> registry)
        {
            Bind(registry, contextResolver: null);
        }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/> Entity Registry에 바인딩하고 현재 Spawned Entity마다 Bound Actor 하나를
        /// <br/> 동기화한다. Context Resolver가 null이면 기본값을 사용한다.
        /// </summary>
        // --------------------------------------------------------------------------------
        public void Bind
        (
            EntitySpawnRegistryBase<TEntity> registry,
            Func<ulong, TEntity, TContext> contextResolver
        )
        {
            if (registry == null)
            {
                throw new ArgumentNullException(nameof(registry));
            }

            if (boundRegistry != null)
            {
                throw new InvalidOperationException("Entity Actor Controller가 이미 Entity Registry에 바인딩되어 있습니다.");
            }

            if (this.registry.Count > 0)
            {
                throw new InvalidOperationException("명시적으로 소유 중인 Actor가 남아 있어 Entity Registry에 바인딩할 수 없습니다.");
            }

            registry.AttachBinding(this);
            boundRegistry = registry;
            this.contextResolver = contextResolver;

            try
            {
                foreach (var (key, entity) in registry.Spawned)
                {
                    var context = ResolveContext(key, entity);
                    CreateBoundCore(entity, context);
                }
            }
            catch (Exception bindException)
            {
                registry.DetachBinding(this);
                boundRegistry = null;
                this.contextResolver = null;

                try
                {
                    ReleaseAllCore(DespawnReason.RegistryCleanup);
                }
                catch (Exception rollbackException)
                {
                    throw new AggregateException
                    (
                        "Entity Actor Registry 바인딩과 rollback이 모두 실패했습니다.",
                        bindException,
                        rollbackException
                    );
                }

                throw;
            }
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// Entity Registry 자동 동기화를 해제하고 소유 중인 모든 Actor를 반환한다.
        /// </summary>
        // ----------------------------------------------------------------------
        public void Unbind()
        {
            var entityRegistry = boundRegistry;

            if (entityRegistry != null)
            {
                entityRegistry.DetachBinding(this);
            }

            boundRegistry = null;
            contextResolver = null;
            ReleaseAllCore(DespawnReason.RegistryCleanup);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 현재 Entity Registry 상태를 기준으로 자동 Actor를 다시 생성한다.
        /// </summary>
        // ------------------------------------------------------------
        public void RecreateAll()
        {
            var entityRegistry = boundRegistry ??
                throw new InvalidOperationException("Entity Registry가 바인딩되어 있지 않습니다.");

            ReleaseAllCore(DespawnReason.RegistryCleanup);

            foreach (var (key, entity) in entityRegistry.Spawned)
            {
                var context = ResolveContext(key, entity);
                CreateBoundCore(entity, context);
            }
        }

    #endregion

    #region 조회

        // ------------------------------------------------------------
        /// <summary>
        /// Entity에 대응하는 모든 Bound Actor를 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        public IReadOnlyList<TEntityActor> FindActors(TEntity entity) => registry.FindAll(entity);

        // ------------------------------------------------------------
        /// <summary>
        /// Entity에 대응하는 Bound Actor가 하나일 때만 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        public bool TryFindSingleActor(TEntity entity, out TEntityActor actor) =>
            registry.TryFindSingle(entity, out actor);

        // ------------------------------------------------------------
        /// <summary>
        /// 동일 Actor 객체 수명을 현재 Controller가 추적하는지 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        public bool ContainsActor(TEntityActor actor) => registry.Contains(actor);

        // ------------------------------------------------------------
        /// <summary>
        /// 동일 Actor가 현재 Entity에 연결되어 있는지 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        public bool IsActorBound(TEntityActor actor) => registry.IsBound(actor);

        // ------------------------------------------------------------
        /// <summary>
        /// 동일 Actor의 현재 Entity owner를 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        public bool TryGetEntity(TEntityActor actor, out TEntity entity) =>
            registry.TryGetEntity(actor, out entity);

    #endregion

    #region 객체 수명 내부

        // ------------------------------------------------------------
        /// <summary>
        /// Detached Actor를 생성하고 Registry에 객체 수명을 등록한다.
        /// </summary>
        // ------------------------------------------------------------
        private TEntityActor CreateCore
        (
            TEntity entity,
            in TContext context
        )
        {
            var actor = factory.Create(entity, context);

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
        /// Detached Actor를 만들고 같은 transaction에서 Entity에 연결한다.
        /// </summary>
        // ----------------------------------------------------------------------
        private TEntityActor CreateBoundCore
        (
            TEntity entity,
            in TContext context
        )
        {
            var actor = CreateCore(entity, context);

            try
            {
                if (!AttachCore(actor, entity))
                {
                    throw new InvalidOperationException("새 Entity Actor를 생성 직후 Attach할 수 없습니다.");
                }

                return actor;
            }
            catch (Exception attachException)
            {
                try
                {
                    ReleaseCore(actor, DespawnReason.SpawnRollback);
                }
                catch (Exception releaseException)
                {
                    throw new AggregateException
                    (
                        "Entity Actor Attach와 rollback 반환이 모두 실패했습니다.",
                        attachException,
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
        private bool ReleaseCore(TEntityActor actor, DespawnReason reason)
        {
            if (actor == null || !registry.Contains(actor))
            {
                return false;
            }

            List<Exception> failures = null;

            if (registry.IsBound(actor))
            {
                try
                {
                    DetachCore(actor);
                }
                catch (Exception exception)
                {
                    failures = new List<Exception>
                    {
                        exception
                    };
                }
            }

            try
            {
                registry.Unregister(actor);
            }
            catch (Exception exception)
            {
                failures ??= new List<Exception>();
                failures.Add(exception);
            }

            try
            {
                factory.Release(actor, reason);
            }
            catch (Exception exception)
            {
                failures ??= new List<Exception>();
                failures.Add(exception);
            }

            if (failures == null)
            {
                return true;
            }

            throw failures.Count == 1
                ? failures[0]
                : new AggregateException("Entity Actor terminal 반환 중 일부 정리가 실패했습니다.", failures);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Entity에 연결된 Actor snapshot을 terminal 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        private void ReleaseAllCore(TEntity entity, DespawnReason reason)
        {
            var actors = new List<TEntityActor>(registry.FindAll(entity));
            ReleaseActors(actors, reason);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 모든 Bound/Detached Actor snapshot을 terminal 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        private void ReleaseAllCore(DespawnReason reason)
        {
            var actors = new List<TEntityActor>(registry.Actors);
            ReleaseActors(actors, reason);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Actor snapshot 전체의 terminal 반환을 끝까지 시도한다.
        /// </summary>
        // ------------------------------------------------------------
        private void ReleaseActors
        (
            IReadOnlyList<TEntityActor> actors,
            DespawnReason reason
        )
        {
            List<Exception> failures = null;

            for (var index = 0; index < actors.Count; index++)
            {
                try
                {
                    ReleaseCore(actors[index], reason);
                }
                catch (Exception exception)
                {
                    failures ??= new List<Exception>();
                    failures.Add(exception);
                }
            }

            if (failures == null)
            {
                return;
            }

            throw failures.Count == 1
                ? failures[0]
                : new AggregateException("일부 Entity Actor 반환에 실패했습니다.", failures);
        }

    #endregion

    #region Binding 내부

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/> Detached Actor의 concrete Bind 훅을 먼저 완료하고 Registry owner mapping을
        /// <br/> 확정한다. Registry 반영 실패 시 concrete binding을 rollback한다.
        /// </summary>
        // --------------------------------------------------------------------------------
        private bool AttachCore(TEntityActor actor, TEntity entity)
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
                registry.Attach(entity, actor);
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
        private bool RebindCore(TEntityActor actor, TEntity nextEntity)
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

    #region 상태 검증

        // --------------------------------------------------------------------------------
        /// <summary>
        /// 명시적 Actor lifecycle API를 사용할 수 있는 자동 Registry 미연결 상태인지 확인한다.
        /// </summary>
        // --------------------------------------------------------------------------------
        private void EnsureUnbound()
        {
            if (boundRegistry != null)
            {
                throw new InvalidOperationException("Entity Registry에 바인딩된 동안 명시적 Actor 수명을 변경할 수 없습니다.");
            }
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// 현재 Resolver 또는 기본값으로 Entity Actor 생성 Context를 반환한다.
        /// </summary>
        // ----------------------------------------------------------------------
        private TContext ResolveContext(ulong key, TEntity entity) =>
            contextResolver != null
                ? contextResolver.Invoke(key, entity)
                : default;

    #endregion

    #region ISpawnRegistryBinding 구현

        // ------------------------------------------------------------
        /// <summary>
        /// Entity Spawned transaction에 Bound Actor 생성을 동기화한다.
        /// </summary>
        // ------------------------------------------------------------
        void ISpawnRegistryBinding<ulong, TEntity>.OnSpawned(ulong key, TEntity entity)
        {
            var context = ResolveContext(key, entity);
            CreateBoundCore(entity, context);
        }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// Entity Despawning transaction에서 해당 Entity의 Bound Actor를 반환한다.
        /// </summary>
        // --------------------------------------------------------------------------------
        void ISpawnRegistryBinding<ulong, TEntity>.OnDespawning
        (
            ulong key,
            TEntity entity,
            DespawnReason reason
        )
        {
            if (boundRegistry == null) return;

            if
            (
                !boundRegistry.Spawned.TryGetValue(key, out var current) ||
                !ReferenceEquals(current, entity)
            )
            {
                return;
            }

            ReleaseAllCore(entity, reason);
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
