/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : EntityActorRuntimeBinding.cs
수정일 : 2026-11-16

# 설명
기존 EntityActorRuntime의 Actor·Presentation 수명을 Entity SpawnRegistry에 선택적으로 연결한다.
이 Binding이 생성한 Actor만 반환하며 같은 Runtime에서 수동 생성한 Actor는 소유하지 않는다.
자동 Actor는 Registry 등록 종료 또는 Binding 정리로만 반환되며 독립적인 이전을 허용하지 않는다.

# 특이사항, 제약사항
Registry와 Entity의 논리 수명을 소유하지 않는다. Provider Release는 terminal 계약을 따른다.
Actor 추적은 Runtime이 해당 Actor의 반환을 시작해 소유권을 종료한 뒤에만 제거한다.
Runtime이 반환 시작 자체를 거절한 Actor는 추적을 유지해 Binding 해제 때 다시 정리한다.
========================================================================= BLOCK_HEADER_END */

using System;
using System.Collections;
using System.Collections.Generic;

namespace inonego.Xeri.Game
{
    // ================================================================================
    /// <summary>
    /// <br/> EntityActorRuntime을 Registry의 기존·신규 Entity 수명과 연결한다.
    /// <br/> 자동 Actor와 Presentation을 함께 생성하되 Registry 자체는 소유하지 않는다.
    /// </summary>
    // ================================================================================
    public class EntityActorRuntimeBinding<TEntityActor, TEntity, TContext> :
        ISpawnRegistryBinding<ulong, TEntity>
    where TEntityActor : EntityActorBase<TEntity>
    where TEntity : class, IEntity
    {

    #region 내부 데이터

        private enum BindingPhase
        {
            Unbound,
            Binding,
            Bound,
            Unbinding
        }

    #endregion

    #region 필드

        // ------------------------------------------------------------
        /// <summary>
        /// 자동 동기화가 완료된 Registry를 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        public EntitySpawnRegistryBase<TEntity> BoundRegistry =>
            phase == BindingPhase.Bound ? boundRegistry : null;

        private BindingPhase phase = BindingPhase.Unbound;
        private EntitySpawnRegistryBase<TEntity> boundRegistry = null;
        private Func<ulong, TEntity, TContext> contextResolver = null;

        private readonly EntityActorRuntime<TEntityActor, TEntity, TContext> runtime;

        // 이 Binding이 생성한 Actor만 추적한다. 같은 Runtime의 명시적 Actor는 별도 소유다.
        private readonly Dictionary<TEntity, TEntityActor> actorsByEntity = new
        (
            ReferenceEqualityComparer<TEntity>.Instance
        );

    #endregion

    #region 생성자

        // ----------------------------------------------------------------------
        /// <summary>
        /// 기존 Actor·Presentation 조합을 Registry Binding에 연결할 준비를 한다.
        /// </summary>
        // ----------------------------------------------------------------------
        public EntityActorRuntimeBinding
        (
            EntityActorRuntime<TEntityActor, TEntity, TContext> runtime
        )
        {
            this.runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        }

    #endregion

    #region 등록·해제

        // ----------------------------------------------------------------------
        /// <summary>
        /// 현재 Registry 개체와 이후 Spawn을 Actor·Presentation에 동기화한다.
        /// </summary>
        // ----------------------------------------------------------------------
        public void Bind
        (
            EntitySpawnRegistryBase<TEntity> registry,
            Func<ulong, TEntity, TContext> contextResolver = null
        )
        {
            if (registry == null)
            {
                throw new ArgumentNullException(nameof(registry));
            }

            if (phase != BindingPhase.Unbound)
            {
                throw new InvalidOperationException("Entity Actor Runtime Binding이 이미 진행 중이거나 연결돼 있습니다.");
            }

            phase = BindingPhase.Binding;
            boundRegistry = registry;
            this.contextResolver = contextResolver;

            try
            {
                registry.AttachBinding(this);
                phase = BindingPhase.Bound;
            }
            catch (Exception bindException)
            {
                // Registry가 시도한 rollback이 실패했더라도 남은 Actor를 terminal 반환한다.
                try
                {
                    ReleaseOwned(DespawnReason.SpawnRollback);
                }
                catch (Exception releaseException)
                {
                    throw new AggregateException
                    (
                        "Entity Actor Runtime Binding과 잔여 Actor 정리가 모두 실패했습니다.",
                        bindException,
                        releaseException
                    );
                }

                throw;
            }
            finally
            {
                if (phase == BindingPhase.Binding)
                {
                    boundRegistry = null;
                    this.contextResolver = null;
                    phase = BindingPhase.Unbound;
                }
            }
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// Registry 연결을 해제하고 이 Binding이 생성한 Actor만 terminal 반환한다.
        /// </summary>
        // ----------------------------------------------------------------------
        public void Unbind()
        {
            if (phase == BindingPhase.Unbound)
            {
                return;
            }

            if (phase != BindingPhase.Bound)
            {
                throw new InvalidOperationException("Entity Actor Runtime Binding 전환 중에는 해제할 수 없습니다.");
            }

            var registry = boundRegistry;
            var detached = false;
            Exception detachFailure = null;
            phase = BindingPhase.Unbinding;

            try
            {
                try
                {
                    registry.DetachBinding(this);
                    detached = true;
                }
                catch (Exception exception)
                {
                    if (registry.IsBindingAttached(this))
                    {
                        throw;
                    }

                    // Registry에서 이미 terminal 해제됐으므로 남은 Actor까지 끝까지 정리한다.
                    detached = true;
                    detachFailure = exception;
                }

                try
                {
                    ReleaseOwned(DespawnReason.RegistryCleanup);
                }
                catch (Exception releaseFailure)
                {
                    if (detachFailure != null)
                    {
                        throw new AggregateException
                        (
                            "Entity Actor Runtime Binding 해제와 잔여 Actor 정리가 모두 실패했습니다.",
                            detachFailure,
                            releaseFailure
                        );
                    }

                    throw;
                }

                if (detachFailure != null)
                {
                    throw detachFailure;
                }
            }
            finally
            {
                if (detached)
                {
                    boundRegistry = null;
                    contextResolver = null;
                    phase = BindingPhase.Unbound;
                }
                else
                {
                    phase = BindingPhase.Bound;
                }
            }
        }

    #endregion

    #region 조회

        // ------------------------------------------------------------
        /// <summary>
        /// 자동으로 생성한 Actor를 조회한다.
        /// </summary>
        // ------------------------------------------------------------
        public bool TryFind(TEntity entity, out TEntityActor actor)
        {
            actor = null;
            return entity != null && actorsByEntity.TryGetValue(entity, out actor);
        }

    #endregion

    #region 잔여 Actor 정리

        // ------------------------------------------------------------
        /// <summary>
        /// 자동 생성 Actor만 정리하고 수동 Actor는 유지한다.
        /// </summary>
        // ------------------------------------------------------------
        private void ReleaseOwned(DespawnReason reason)
        {
            var failures = new ExceptionBuffer();

            // 반환마다 추적을 제거하므로 snapshot을 순회한다.
            var snapshot = new List<KeyValuePair<TEntity, TEntityActor>>(actorsByEntity);

            foreach (var (entity, actor) in snapshot)
            {
                try
                {
                    runtime.ReleaseRegistryOwned(actor, reason);
                }
                catch (Exception exception)
                {
                    failures.Add(exception);
                }
                finally
                {
                    PruneReleasedActor(entity, actor);
                }
            }

            if (failures.Count > 0)
            {
                throw failures.CreateAggregateException("일부 자동 Actor와 Presentation 반환에 실패했습니다.");
            }
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Runtime이 더 이상 소유하지 않는 Actor의 추적만 제거한다.
        /// </summary>
        // ------------------------------------------------------------
        private void PruneReleasedActor(TEntity entity, TEntityActor actor)
        {
            if (!runtime.OwnsActor(actor))
            {
                actorsByEntity.Remove(entity);
            }
        }

    #endregion

    #region Registry 등록 Binding

        // ------------------------------------------------------------
        /// <summary>
        /// Registry에 등록된 Entity를 Actor와 Presentation으로 구성한다.
        /// </summary>
        // ------------------------------------------------------------
        void ISpawnRegistryBinding<ulong, TEntity>.OnRegistered(ulong key, TEntity entity)
        {
            if (phase != BindingPhase.Binding && phase != BindingPhase.Bound)
            {
                throw new InvalidOperationException("연결되지 않은 Registry Binding에서 Actor를 생성할 수 없습니다.");
            }

            if (actorsByEntity.ContainsKey(entity))
            {
                throw new InvalidOperationException("동일 Entity에 대한 자동 Actor가 이미 등록돼 있습니다.");
            }

            var context = contextResolver != null
                ? contextResolver(key, entity)
                : default;

            // 생성 전에 Registry 출처를 확정해 외부 호출이 끼어들 수 없게 한다.
            var actor = runtime.CreateRegistryOwned(entity, context);

            // 같은 Entity의 중복 등록은 위에서 거절했고 Registry가 같은 Entity의 중첩 등록을 막는다.
            actorsByEntity.Add(entity, actor);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Registry 등록 해제 시 Actor와 Presentation을 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        void ISpawnRegistryBinding<ulong, TEntity>.OnUnregistering
        (
            ulong key,
            TEntity entity,
            DespawnReason reason
        )
        {
            if (!actorsByEntity.TryGetValue(entity, out var actor))
            {
                return;
            }

            try
            {
                runtime.ReleaseRegistryOwned(actor, reason);
            }
            finally
            {
                PruneReleasedActor(entity, actor);
            }
        }

    #endregion

    }

    // ============================================================
    /// <summary>
    /// Context 없는 Actor Runtime을 Registry에 연결한다.
    /// </summary>
    // ============================================================
    public sealed class EntityActorRuntimeBinding<TEntityActor, TEntity> :
        EntityActorRuntimeBinding<TEntityActor, TEntity, EntityActorNoContext>
    where TEntityActor : EntityActorBase<TEntity>
    where TEntity : class, IEntity
    {

    #region 생성자

        // ------------------------------------------------------------
        /// <summary>
        /// Context 없는 Runtime으로 Binding을 생성한다.
        /// </summary>
        // ------------------------------------------------------------
        public EntityActorRuntimeBinding
        (
            EntityActorRuntime<TEntityActor, TEntity, EntityActorNoContext> runtime
        ) : base(runtime)
        {
            // NONE
        }

    #endregion

    #region 로컬 기본 조합

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/> 기존 Actor Factory와 Presentation Provider를 로컬 자동 Binding으로
        /// <br/> 조립한다. Registry·Factory·Provider의 소유권은 호출자에게 남긴다.
        /// </summary>
        // --------------------------------------------------------------------------------
        public static EntityActorRuntimeBinding<TEntityActor, TEntity> CreateAndBind
        (
            EntitySpawnRegistryBase<TEntity> registry,
            IEntityActorFactory<TEntityActor, TEntity, EntityActorNoContext> actorFactory,
            params IEntityPresentationProvider<TEntityActor, TEntity>[] providers
        )
        {
            if (registry == null)
            {
                throw new ArgumentNullException(nameof(registry));
            }

            if (actorFactory == null)
            {
                throw new ArgumentNullException(nameof(actorFactory));
            }

            if (providers == null)
            {
                throw new ArgumentNullException(nameof(providers));
            }

            var controller =
                new EntityActorController<TEntityActor, TEntity, EntityActorNoContext>(actorFactory);
            var coordinator = new EntityPresentationCoordinator<TEntityActor, TEntity>();

            foreach (var provider in providers)
            {
                coordinator.AddProvider(provider);
            }

            var runtime = new EntityActorRuntime<TEntityActor, TEntity, EntityActorNoContext>
            (
                controller,
                coordinator
            );
            var binding = new EntityActorRuntimeBinding<TEntityActor, TEntity>(runtime);
            binding.Bind(registry);
            return binding;
        }

    #endregion

    }
}
