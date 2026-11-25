/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : EntityPresentationCoordinator.cs
수정일 : 2026-11-16

# 설명
여러 Entity Presentation Provider의 정순 생성·Rebind와 역순 rollback·release를 조정한다.
concrete Presentation 인스턴스와 Registry, Pool, 독립 Despawn 정책은 소유하지 않는다.
Actor 수명 또는 Provider transaction 중에는 Provider 목록을 변경하지 않는다.
Provider 공개 목록은 실제 변경 가능한 List를 노출하지 않는다.
Provider transaction의 단위는 개별 Actor이며 같은 Actor의 중첩 수명 작업만 거절한다.
다른 Actor의 수명 작업은 Provider 콜백 중에도 허용하므로 Provider는 Actor 간 재진입에 안전해야 한다.
Runtime 결합 시 Provider 수명 변경 권한을 해당 Runtime에만 허용한다.
========================================================================= BLOCK_HEADER_END */

using System;
using System.Collections;
using System.Collections.Generic;

namespace inonego.Xeri.Game
{
    // ======================================================================
    /// <summary>
    /// 여러 Presentation Provider의 Actor 수명 transaction을 조정한다.
    /// </summary>
    // ======================================================================
    [Serializable]
    public sealed class EntityPresentationCoordinator<TEntityActor, TEntity>
    where TEntityActor : EntityActorBase<TEntity>
    where TEntity : class, IEntity
    {

    #region 필드

        // ------------------------------------------------------------
        /// <summary>
        /// 현재 조립된 Presentation Provider 목록.
        /// </summary>
        // ------------------------------------------------------------
        public IReadOnlyList<IEntityPresentationProvider<TEntityActor, TEntity>> Providers
        {
            get
            {
                providerView ??= providers.AsReadOnly();
                return providerView;
            }
        }

        [NonSerialized]
        private IReadOnlyList<IEntityPresentationProvider<TEntityActor, TEntity>> providerView = null;

        [NonSerialized]
        private readonly List<IEntityPresentationProvider<TEntityActor, TEntity>> providers = new();

        [NonSerialized]
        private readonly HashSet<TEntityActor> activeActors = new
        (
            ReferenceEqualityComparer<TEntityActor>.Instance
        );

        // 결합된 Runtime 외부에서는 Provider 수명을 직접 변경하지 않는다.
        [NonSerialized]
        private bool isRuntimeOwned = false;

        // Provider 콜백이 진행 중인 Actor. 같은 Actor의 중첩 작업만 거절한다.
        [NonSerialized]
        private readonly HashSet<TEntityActor> processingActors = new
        (
            ReferenceEqualityComparer<TEntityActor>.Instance
        );

    #endregion

    #region Provider 구성

        // ------------------------------------------------------------
        /// <summary>
        /// Presentation Provider를 실행 순서의 마지막에 등록한다.
        /// </summary>
        // ------------------------------------------------------------
        public void AddProvider(IEntityPresentationProvider<TEntityActor, TEntity> provider)
        {
            if (provider == null)
            {
                throw new ArgumentNullException(nameof(provider));
            }

            EnsureProviderChangesAllowed();

            for (var index = 0; index < providers.Count; index++)
            {
                if (ReferenceEquals(providers[index], provider))
                {
                    throw new InvalidOperationException("동일 Presentation Provider를 중복 등록할 수 없습니다.");
                }
            }

            providers.Add(provider);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 동일 Presentation Provider 등록을 제거한다.
        /// </summary>
        // ------------------------------------------------------------
        public bool RemoveProvider(IEntityPresentationProvider<TEntityActor, TEntity> provider)
        {
            EnsureProviderChangesAllowed();

            for (var index = providers.Count - 1; index >= 0; index--)
            {
                if (!ReferenceEquals(providers[index], provider))
                {
                    continue;
                }

                providers.RemoveAt(index);
                return true;
            }

            return false;
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// 살아 있는 Actor와 Provider transaction 동안의 구성 변경을 방지한다.
        /// </summary>
        // ----------------------------------------------------------------------
        private void EnsureProviderChangesAllowed()
        {
            if (activeActors.Count != 0 || processingActors.Count != 0)
            {
                throw new InvalidOperationException
                (
                    "활성 Actor 또는 Presentation transaction 중에는 Provider 구성을 변경할 수 없습니다."
                );
            }
        }

    #endregion

    #region Presentation 수명 재진입 보호

        // ----------------------------------------------------------------------
        /// <summary>
        /// 같은 Actor의 진행 중인 Presentation 수명 작업에 재진입하지 못하게 한다.
        /// </summary>
        // ----------------------------------------------------------------------
        private void EnsureNotProcessing(TEntityActor actor)
        {
            if (processingActors.Contains(actor))
            {
                throw new InvalidOperationException
                (
                    "Presentation Provider transaction 중인 Actor에는 다른 Presentation 수명 작업을 시작할 수 없습니다."
                );
            }
        }

    #endregion

    #region Runtime 소유권

        // ------------------------------------------------------------
        /// <summary>
        /// Provider 수명 변경 권한을 하나의 Runtime에 귀속한다.
        /// </summary>
        // ------------------------------------------------------------
        internal void ClaimRuntime()
        {
            if (isRuntimeOwned || activeActors.Count != 0 || processingActors.Count != 0)
            {
                throw new InvalidOperationException("활성 Actor가 없는 독립 Coordinator만 Runtime에 결합할 수 있습니다.");
            }

            isRuntimeOwned = true;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Runtime 생성 실패 시 Coordinator의 결합만 취소한다.
        /// </summary>
        // ------------------------------------------------------------
        internal void ReleaseRuntimeClaim()
        {
            if (activeActors.Count == 0)
            {
                isRuntimeOwned = false;
            }
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// Runtime에 결합한 Coordinator의 직접 Provider 수명 변경을 거절한다.
        /// </summary>
        // ----------------------------------------------------------------------
        private void EnsureStandalone()
        {
            if (isRuntimeOwned)
            {
                throw new InvalidOperationException("Runtime에 결합한 Presentation Coordinator는 직접 수명을 변경할 수 없습니다.");
            }
        }

    #endregion

    #region Actor Presentation 생성·반환

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> 모든 Provider를 정순 실행한다.
        /// <br/> 실패한 Provider까지 역순 rollback하고 모든 오류를 전달한다.
        /// </summary>
        // ----------------------------------------------------------------------
        public void Create(TEntityActor actor, TEntity entity)
        {
            EnsureStandalone();
            CreateCore(actor, entity);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Presentation 생성의 Provider transaction을 수행한다.
        /// </summary>
        // ------------------------------------------------------------
        internal void CreateCore(TEntityActor actor, TEntity entity)
        {
            if (entity == null)
            {
                throw new ArgumentNullException(nameof(entity));
            }

            if (actor == null)
            {
                throw new ArgumentNullException(nameof(actor));
            }

            EnsureNotProcessing(actor);

            if (!activeActors.Add(actor))
            {
                throw new InvalidOperationException("같은 Actor의 Presentation 수명을 중복 생성할 수 없습니다.");
            }

            var attempted = 0;
            processingActors.Add(actor);

            try
            {
                for (var index = 0; index < providers.Count; index++)
                {
                    // 부분 획득 후 예외가 발생한 Provider도 역순 정리 대상으로 포함한다.
                    attempted = index + 1;
                    providers[index].OnActorCreated(entity, actor);
                }
            }
            catch (Exception createException)
            {
                // 생성 실패한 Actor의 Provider 수명은 rollback 이후 terminal 종료된다.
                activeActors.Remove(actor);
                var failures = new ExceptionBuffer();

                for (var index = attempted - 1; index >= 0; index--)
                {
                    try
                    {
                        providers[index].OnActorReleasing
                        (
                            entity,
                            actor,
                            DespawnReason.SpawnRollback
                        );
                    }
                    catch (Exception rollbackException)
                    {
                        failures.Add(rollbackException);
                    }
                }

                if (failures.Count == 0)
                {
                    throw;
                }

                failures.Prepend(createException);
                throw failures.CreateAggregateException("Presentation 생성과 rollback 중 오류가 발생했습니다.");
            }
            finally
            {
                processingActors.Remove(actor);
            }
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 모든 Provider의 Actor 분리 처리를 역순으로 끝까지 시도한다.
        /// </summary>
        // ------------------------------------------------------------
        public void Release
        (
            TEntityActor actor,
            TEntity entity,
            DespawnReason reason
        )
        {
            EnsureStandalone();
            ReleaseCore(actor, entity, reason);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Presentation 반환의 Provider transaction을 수행한다.
        /// </summary>
        // ------------------------------------------------------------
        internal void ReleaseCore
        (
            TEntityActor actor,
            TEntity entity,
            DespawnReason reason
        )
        {
            if (actor == null)
            {
                throw new ArgumentNullException(nameof(actor));
            }

            EnsureNotProcessing(actor);

            // 이미 terminal 반환된 Actor에는 Provider의 Release를 다시 호출하지 않는다.
            if (!activeActors.Remove(actor))
            {
                return;
            }

            processingActors.Add(actor);

            try
            {
                // Detached Actor의 terminal 반환에서는 entity가 null일 수 있다.
                var failures = new ExceptionBuffer();

                for (var index = providers.Count - 1; index >= 0; index--)
                {
                    try
                    {
                        providers[index].OnActorReleasing(entity, actor, reason);
                    }
                    catch (Exception exception)
                    {
                        failures.Add(exception);
                    }
                }

                failures.ThrowWhenAny("일부 Presentation Provider 반환에 실패했습니다.");
            }
            finally
            {
                processingActors.Remove(actor);
            }
        }

    #endregion

    #region Actor Entity Rebind

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> 활성 Presentation 수명의 Actor에 Entity Rebind를 정순 적용한다.
        /// <br/> 실패한 Provider까지 역순 rollback하고 최초 오류와 정리 오류를 보존한다.
        /// </summary>
        // ----------------------------------------------------------------------
        public void RebindEntity
        (
            TEntityActor actor,
            TEntity previousEntity,
            TEntity nextEntity
        )
        {
            EnsureStandalone();
            RebindEntityCore(actor, previousEntity, nextEntity);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Presentation Entity 관계 이전 transaction을 실행한다.
        /// </summary>
        // ------------------------------------------------------------
        internal void RebindEntityCore
        (
            TEntityActor actor,
            TEntity previousEntity,
            TEntity nextEntity
        )
        {
            if (previousEntity == null)
            {
                throw new ArgumentNullException(nameof(previousEntity));
            }

            if (nextEntity == null)
            {
                throw new ArgumentNullException(nameof(nextEntity));
            }

            if (actor == null)
            {
                throw new ArgumentNullException(nameof(actor));
            }

            EnsureNotProcessing(actor);

            if (!activeActors.Contains(actor))
            {
                throw new InvalidOperationException("Presentation 수명이 없는 Actor는 Entity를 Rebind할 수 없습니다.");
            }

            var attempted = 0;
            processingActors.Add(actor);

            try
            {
                for (var index = 0; index < providers.Count; index++)
                {
                    attempted = index + 1;
                    providers[index].OnActorRebound
                    (
                        previousEntity,
                        nextEntity,
                        actor
                    );
                }
            }
            catch (Exception rebindException)
            {
                var failures = new ExceptionBuffer();

                for (var index = attempted - 1; index >= 0; index--)
                {
                    try
                    {
                        providers[index].OnActorRebindRollback
                        (
                            previousEntity,
                            nextEntity,
                            actor
                        );
                    }
                    catch (Exception rollbackException)
                    {
                        failures.Add(rollbackException);
                    }
                }

                if (failures.Count == 0)
                {
                    throw;
                }

                failures.Prepend(rebindException);
                throw failures.CreateAggregateException("Presentation Rebind와 rollback 중 오류가 발생했습니다.");
            }
            finally
            {
                processingActors.Remove(actor);
            }
        }

    #endregion

    #region Presentation Owner Rebind

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/> 활성 Presentation Owner를 미등록 Actor로 이전하고 성공 시 수명 추적을 옮긴다.
        /// <br/> 실패한 Provider까지 역순 rollback하며 기존 Owner 수명을 유지한다.
        /// </summary>
        // --------------------------------------------------------------------------------
        public void RebindOwner
        (
            TEntityActor previousOwner,
            TEntityActor nextOwner
        )
        {
            EnsureStandalone();

            if (previousOwner == null)
            {
                throw new ArgumentNullException(nameof(previousOwner));
            }

            if (nextOwner == null)
            {
                throw new ArgumentNullException(nameof(nextOwner));
            }

            EnsureNotProcessing(previousOwner);
            EnsureNotProcessing(nextOwner);

            if (!activeActors.Contains(previousOwner))
            {
                throw new InvalidOperationException("Presentation 수명이 없는 Actor의 Owner를 Rebind할 수 없습니다.");
            }

            if (ReferenceEquals(previousOwner, nextOwner))
            {
                return;
            }

            if (activeActors.Contains(nextOwner))
            {
                throw new InvalidOperationException("이미 Presentation 수명이 있는 Actor를 새 Owner로 지정할 수 없습니다.");
            }

            var attempted = 0;
            processingActors.Add(previousOwner);
            processingActors.Add(nextOwner);

            try
            {
                for (var index = 0; index < providers.Count; index++)
                {
                    attempted = index + 1;
                    providers[index].OnPresentationOwnerRebound
                    (
                        previousOwner,
                        nextOwner
                    );
                }

                // 모든 Provider가 성공한 뒤에만 Coordinator의 활성 Owner를 교체한다.
                activeActors.Remove(previousOwner);
                activeActors.Add(nextOwner);
            }
            catch (Exception rebindException)
            {
                var failures = new ExceptionBuffer();

                for (var index = attempted - 1; index >= 0; index--)
                {
                    try
                    {
                        providers[index].OnPresentationOwnerRebindRollback
                        (
                            previousOwner,
                            nextOwner
                        );
                    }
                    catch (Exception rollbackException)
                    {
                        failures.Add(rollbackException);
                    }
                }

                if (failures.Count == 0)
                {
                    throw;
                }

                failures.Prepend(rebindException);
                throw failures.CreateAggregateException("Presentation owner Rebind와 rollback 중 오류가 발생했습니다.");
            }
            finally
            {
                processingActors.Remove(previousOwner);
                processingActors.Remove(nextOwner);
            }
        }

    #endregion

    }
}
