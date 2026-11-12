/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : EntityActorFactory.cs
수정일 : 2026-10-03

# 설명
Entity Actor GameObject의 획득, Context 기반 준비와 terminal 반환을 담당한다.
생성 결과는 Detached Actor이며 Entity Attach/Detach 관계는 EntityActorController가 소유한다.
요청별 Provider와 획득 Actor의 source routing을 terminal 반환 시도까지 유지한다.
========================================================================= BLOCK_HEADER_END */

using System;
using System.Collections;
using System.Collections.Generic;

namespace inonego.Xeri.Game
{
    // ================================================================================
    /// <summary>
    /// <br/> Entity와 생성 Context에 대응하는 Unity Actor를 준비하고 반환한다.
    /// <br/> concrete Factory는 요청별 GameObject Provider와 준비 정책만 정의한다.
    /// </summary>
    // ================================================================================
    [Serializable]
    public abstract class EntityActorFactory<TEntityActor, TEntity, TContext> :
        IEntityActorFactory<TEntityActor, TEntity, TContext>
    where TEntityActor : EntityActorBase<TEntity>
    where TEntity : class, IEntity
    {

    #region 필드

        [NonSerialized]
        private readonly Dictionary<TEntityActor, IGameObjectProvider> sourceProviders = new
        (
            ReferenceEqualityComparer<TEntityActor>.Instance
        );

    #endregion

    #region 생성자

        // ------------------------------------------------------------
        /// <summary>
        /// 파생 Factory 초기화를 위한 기본 생성자.
        /// </summary>
        // ------------------------------------------------------------
        protected EntityActorFactory() : base()
        {
            // NONE
        }

    #endregion

    #region 공개 생성과 반환

        // ------------------------------------------------------------
        /// <summary>
        /// Entity와 생성 Context에 대응하는 Actor를 동기 생성한다.
        /// </summary>
        // ------------------------------------------------------------
        public virtual TEntityActor Create(TEntity entity, in TContext context)
        {
            if (entity == null)
            {
                throw new ArgumentNullException(nameof(entity));
            }

            var provider = ResolveProvider(entity, context) ??
                throw new InvalidOperationException("Entity Actor GameObject Provider를 해석할 수 없습니다.");
            var actor = AcquireActor(provider);

            try
            {
                // 반환 출처를 먼저 고정해 이후 모든 부분 생성 상태를 같은 Provider로 정리한다.
                sourceProviders.Add(actor, provider);

                // Factory는 객체 수명만 준비하고 Entity binding은 Controller에 맡긴다.
                PrepareActor(actor, entity, context);
                return actor;
            }
            catch (Exception spawnException)
            {
                var failures = CleanupFailedCreate(actor, provider);

                if (failures == null)
                {
                    throw;
                }

                failures.Insert(0, spawnException);
                throw new AggregateException("Entity Actor 생성과 rollback 중 오류가 발생했습니다.", failures);
            }
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> Entity Actor를 지정 사유로 terminal 반환한다.
        /// <br/> 정리와 Provider 반환을 모두 시도하고 source routing을 종료한다.
        /// </summary>
        // ----------------------------------------------------------------------
        public virtual void Release(TEntityActor actor, DespawnReason reason)
        {
            if (actor == null) return;

            if (!sourceProviders.TryGetValue(actor, out var provider))
            {
                throw new InvalidOperationException("현재 Factory가 획득한 Entity Actor가 아닙니다.");
            }

            List<Exception> failures = null;

            try
            {
                CleanupActor(actor, reason);
            }
            catch (Exception exception)
            {
                failures = new List<Exception>
                {
                    exception
                };
            }

            try
            {
                // Provider 호출은 terminal 반환 경계이므로 앞선 정리 실패와 무관하게 한 번 수행한다.
                provider.Release(actor.gameObject, worldPositionStays: false);
            }
            catch (Exception exception)
            {
                failures ??= new List<Exception>();
                failures.Add(exception);
            }
            finally
            {
                sourceProviders.Remove(actor);
            }

            if (failures == null) return;

            throw failures.Count == 1
                ? failures[0]
                : new AggregateException("Entity Actor 정리와 Provider 반환 중 오류가 발생했습니다.", failures);
        }

    #endregion

    #region 준비

        // ------------------------------------------------------------
        /// <summary>
        /// GameObject와 Actor 컴포넌트를 동기 획득한다.
        /// </summary>
        // ------------------------------------------------------------
        private static TEntityActor AcquireActor(IGameObjectProvider provider)
        {
            var gameObject = provider.Acquire(worldPositionStays: false);

            if (gameObject == null)
            {
                throw new InvalidOperationException("Entity Actor GameObject를 가져올 수 없습니다.");
            }

            if (gameObject.TryGetComponent(out TEntityActor actor))
            {
                return actor;
            }

            var componentName = typeof(TEntityActor).Name;
            var gameObjectName = gameObject.name;

            try
            {
                provider.Release(gameObject, worldPositionStays: false);
            }
            catch (Exception releaseException)
            {
                var componentException = new InvalidOperationException
                (
                    $"게임 오브젝트 '{gameObjectName}'에서 Entity Actor 컴포넌트({componentName})를 찾을 수 없습니다."
                );
                throw new AggregateException
                (
                    "Entity Actor 컴포넌트 확인과 Provider 반환이 모두 실패했습니다.",
                    componentException,
                    releaseException
                );
            }

            throw new InvalidOperationException
            (
                $"게임 오브젝트 '{gameObjectName}'에서 Entity Actor 컴포넌트({componentName})를 찾을 수 없습니다."
            );
        }

    #endregion

    #region 반환

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> 생성 중 실패한 Actor의 부분 상태와 Provider 반환을 끝까지 처리한다.
        /// <br/> 반환된 목록에는 Spawn 원인을 제외한 rollback 오류만 담긴다.
        /// </summary>
        // ----------------------------------------------------------------------
        private List<Exception> CleanupFailedCreate
        (
            TEntityActor actor,
            IGameObjectProvider provider
        )
        {
            List<Exception> failures = null;

            try
            {
                CleanupActor(actor, DespawnReason.SpawnRollback);
            }
            catch (Exception exception)
            {
                failures = new List<Exception>
                {
                    exception
                };
            }

            try
            {
                // 생성 실패의 Provider 반환도 terminal 경계로 한 번 수행한다.
                provider.Release(actor.gameObject, worldPositionStays: false);
            }
            catch (Exception exception)
            {
                failures ??= new List<Exception>();
                failures.Add(exception);
            }
            finally
            {
                sourceProviders.Remove(actor);
            }

            return failures;
        }

    #endregion

    #region 확장 훅

        // ------------------------------------------------------------
        /// <summary>
        /// 요청에 사용할 GameObject Provider를 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        protected abstract IGameObjectProvider ResolveProvider
        (
            TEntity entity,
            in TContext context
        );

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> Factory가 Entity를 연결하기 전에 필수 종속 표현을 동기 준비한다.
        /// <br/> 분리된 VisualRoot가 필요하면 이 단계에서 연결한다.
        /// </summary>
        // ----------------------------------------------------------------------
        protected virtual void PrepareActor
        (
            TEntityActor actor,
            TEntity entity,
            in TContext context
        )
        {
            // NONE
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Provider 반환 전에 Factory가 공급한 종속 표현을 정리한다.
        /// </summary>
        // ------------------------------------------------------------
        protected virtual void CleanupActor
        (
            TEntityActor actor,
            DespawnReason reason
        )
        {
            // NONE
        }

    #endregion

    }

    // ================================================================================
    /// <summary>
    /// <br/> 생성 Context 없이 하나의 GameObject Provider를 사용하는 Entity Actor
    /// <br/> Factory.
    /// </summary>
    // ================================================================================
    [Serializable]
    public class EntityActorFactory<TEntityActor, TEntity> :
        EntityActorFactory<TEntityActor, TEntity, EntityActorNoContext>
    where TEntityActor : EntityActorBase<TEntity>
    where TEntity : class, IEntity
    {

    #region 필드

        private readonly IGameObjectProvider provider = null;

    #endregion

    #region 생성자

        // ------------------------------------------------------------
        /// <summary>
        /// GameObject Provider를 주입받는다.
        /// </summary>
        // ------------------------------------------------------------
        public EntityActorFactory(IGameObjectProvider provider) : base()
        {
            this.provider = provider ?? throw new ArgumentNullException(nameof(provider));
        }

    #endregion

    #region 공개 생성과 반환

        // ------------------------------------------------------------
        /// <summary>
        /// Entity에 대응하는 Actor를 동기 생성한다.
        /// </summary>
        // ------------------------------------------------------------
        public virtual TEntityActor Create(TEntity entity)
        {
            var context = default(EntityActorNoContext);
            return base.Create(entity, context);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Entity Actor를 일반 제거 사유로 terminal 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        public virtual void Release(TEntityActor actor)
        {
            base.Release(actor, DespawnReason.Removed);
        }

    #endregion

    #region 확장 훅

        // ------------------------------------------------------------
        /// <summary>
        /// 생성자에서 고정한 GameObject Provider를 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        protected sealed override IGameObjectProvider ResolveProvider
        (
            TEntity entity,
            in EntityActorNoContext context
        )
        {
            return provider;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Context 없는 기존 Actor 준비 훅을 호출한다.
        /// </summary>
        // ------------------------------------------------------------
        protected sealed override void PrepareActor
        (
            TEntityActor actor,
            TEntity entity,
            in EntityActorNoContext context
        )
        {
            PrepareActor(actor, entity);
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> Factory가 Entity를 연결하기 전에 필수 종속 표현을 동기 준비한다.
        /// <br/> 분리된 VisualRoot가 필요하면 이 단계에서 연결한다.
        /// </summary>
        // ----------------------------------------------------------------------
        protected virtual void PrepareActor(TEntityActor actor, TEntity entity)
        {
            // NONE
        }

    #endregion

    }
}
