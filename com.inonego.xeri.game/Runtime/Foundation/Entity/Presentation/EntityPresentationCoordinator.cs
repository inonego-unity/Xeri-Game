/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : EntityPresentationCoordinator.cs
수정일 : 2026-10-03

# 설명
여러 Entity Presentation Provider의 정순 생성·Transfer와 역순 rollback·release를 조정한다.
concrete Presentation 인스턴스와 Registry, Pool, 독립 Despawn 정책은 소유하지 않는다.
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
        public IReadOnlyList<IEntityPresentationProvider<TEntityActor, TEntity>> Providers => providers;

        [NonSerialized]
        private readonly List<IEntityPresentationProvider<TEntityActor, TEntity>> providers = new();

    #endregion

    #region Provider 조립

        // ------------------------------------------------------------
        /// <summary>
        /// Presentation Provider를 실행 순서의 마지막에 등록한다.
        /// </summary>
        // ------------------------------------------------------------
        public void Register(IEntityPresentationProvider<TEntityActor, TEntity> provider)
        {
            if (provider == null)
            {
                throw new ArgumentNullException(nameof(provider));
            }

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
        public bool Unregister(IEntityPresentationProvider<TEntityActor, TEntity> provider)
        {
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

    #endregion

    #region Actor Transfer

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/> 동일 Actor의 Entity Transfer를 모든 Provider에 정순 적용한다.
        /// <br/> 실패 시 완료된 Provider만 역순 rollback하고 최초 오류와 rollback 오류를 보존한다.
        /// </summary>
        // --------------------------------------------------------------------------------
        public void Transfer
        (
            TEntity previousEntity,
            TEntity nextEntity,
            TEntityActor actor
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

            var completed = 0;

            try
            {
                for (var index = 0; index < providers.Count; index++)
                {
                    providers[index].OnActorTransferred
                    (
                        previousEntity,
                        nextEntity,
                        actor
                    );
                    completed++;
                }
            }
            catch (Exception transferException)
            {
                List<Exception> failures = null;

                for (var index = completed - 1; index >= 0; index--)
                {
                    try
                    {
                        providers[index].OnActorTransferRollback
                        (
                            previousEntity,
                            nextEntity,
                            actor
                        );
                    }
                    catch (Exception rollbackException)
                    {
                        failures ??= new List<Exception>();
                        failures.Add(rollbackException);
                    }
                }

                if (failures == null)
                {
                    throw;
                }

                failures.Insert(0, transferException);
                throw new AggregateException
                (
                    "Presentation Transfer와 rollback 중 오류가 발생했습니다.",
                    failures
                );
            }
        }

    #endregion

    #region Presentation Owner Transfer

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/> 각 Provider가 소유한 concrete Presentation owner를 previous Actor에서
        /// <br/> next Actor로 정순 이전한다. 실패 시 완료된 Provider만 역순 rollback한다.
        /// </summary>
        // --------------------------------------------------------------------------------
        public void TransferOwner
        (
            TEntityActor previousOwner,
            TEntityActor nextOwner
        )
        {
            if (previousOwner == null)
            {
                throw new ArgumentNullException(nameof(previousOwner));
            }

            if (nextOwner == null)
            {
                throw new ArgumentNullException(nameof(nextOwner));
            }

            if (ReferenceEquals(previousOwner, nextOwner))
            {
                return;
            }

            var completed = 0;

            try
            {
                for (var index = 0; index < providers.Count; index++)
                {
                    providers[index].OnPresentationOwnerTransferred
                    (
                        previousOwner,
                        nextOwner
                    );
                    completed++;
                }
            }
            catch (Exception transferException)
            {
                List<Exception> failures = null;

                for (var index = completed - 1; index >= 0; index--)
                {
                    try
                    {
                        providers[index].OnPresentationOwnerTransferRollback
                        (
                            previousOwner,
                            nextOwner
                        );
                    }
                    catch (Exception rollbackException)
                    {
                        failures ??= new List<Exception>();
                        failures.Add(rollbackException);
                    }
                }

                if (failures == null)
                {
                    throw;
                }

                failures.Insert(0, transferException);
                throw new AggregateException
                (
                    "Presentation owner Transfer와 rollback 중 오류가 발생했습니다.",
                    failures
                );
            }
        }

    #endregion

    #region Actor 수명

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> 모든 Provider를 정순 실행한다.
        /// <br/> 실패 시 완료된 Provider만 역순 rollback하고 모든 오류를 전달한다.
        /// </summary>
        // ----------------------------------------------------------------------
        public void CreateForActor(TEntity entity, TEntityActor actor)
        {
            if (entity == null)
            {
                throw new ArgumentNullException(nameof(entity));
            }

            if (actor == null)
            {
                throw new ArgumentNullException(nameof(actor));
            }

            var completed = 0;

            try
            {
                for (var index = 0; index < providers.Count; index++)
                {
                    providers[index].OnActorCreated(entity, actor);
                    completed++;
                }
            }
            catch (Exception createException)
            {
                List<Exception> failures = null;

                for (var index = completed - 1; index >= 0; index--)
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
                        failures ??= new List<Exception>();
                        failures.Add(rollbackException);
                    }
                }

                if (failures == null)
                {
                    throw;
                }

                failures.Insert(0, createException);
                throw new AggregateException("Presentation 생성과 rollback 중 오류가 발생했습니다.", failures);
            }
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 모든 Provider의 Actor 분리 처리를 역순으로 끝까지 시도한다.
        /// </summary>
        // ------------------------------------------------------------
        public void Release
        (
            TEntity entity,
            TEntityActor actor,
            DespawnReason reason
        )
        {
            if (actor == null)
            {
                throw new ArgumentNullException(nameof(actor));
            }

            // Detached Actor의 terminal 반환에서는 entity가 null일 수 있다.
            List<Exception> failures = null;

            for (var index = providers.Count - 1; index >= 0; index--)
            {
                try
                {
                    providers[index].OnActorReleasing(entity, actor, reason);
                }
                catch (Exception exception)
                {
                    failures ??= new List<Exception>();
                    failures.Add(exception);
                }
            }

            if (failures == null) return;

            throw failures.Count == 1
                ? failures[0]
                : new AggregateException("일부 Presentation Provider 반환에 실패했습니다.", failures);
        }

    #endregion

    }
}
