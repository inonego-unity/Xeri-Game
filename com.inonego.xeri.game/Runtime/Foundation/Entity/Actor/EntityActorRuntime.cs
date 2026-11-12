/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : EntityActorRuntime.cs
수정일 : 2026-10-03

# 설명
EntityActorController와 EntityPresentationCoordinator를 조합해 Actor 생성·반환·Transfer transaction을 관리한다.
Actor state persistence나 gameplay activation 정책은 알지 않으며 관계 수명과 Presentation participant의 rollback만 소유한다.
========================================================================= BLOCK_HEADER_END */

using System;
using System.Collections;
using System.Collections.Generic;

namespace inonego.Xeri.Game
{
    // ================================================================================
    /// <summary>
    /// <br/> Actor object lifecycle과 Presentation Provider transaction을 조합한다.
    /// <br/> persistent state와 gameplay activation은 caller 책임으로 남긴다.
    /// </summary>
    // ================================================================================
    [Serializable]
    public sealed class EntityActorRuntime<TEntityActor, TEntity, TContext>
    where TEntityActor : EntityActorBase<TEntity>
    where TEntity : class, IEntity
    {

    #region 필드

        [NonSerialized]
        private readonly EntityActorController<TEntityActor, TEntity, TContext> actorController = null;

        [NonSerialized]
        private readonly EntityPresentationCoordinator<TEntityActor, TEntity> presentationCoordinator = null;

    #endregion

    #region 생성자

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/> 기존 Actor Controller와 Presentation Coordinator를 같은 runtime
        /// <br/> transaction 경계로 조립한다.
        /// </summary>
        // --------------------------------------------------------------------------------
        public EntityActorRuntime
        (
            EntityActorController<TEntityActor, TEntity, TContext> actorController,
            EntityPresentationCoordinator<TEntityActor, TEntity> presentationCoordinator
        )
        {
            this.actorController = actorController ??
                throw new ArgumentNullException(nameof(actorController));
            this.presentationCoordinator = presentationCoordinator ??
                throw new ArgumentNullException(nameof(presentationCoordinator));
        }

    #endregion

    #region Actor 수명

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> Actor를 생성한 뒤 Presentation Provider를 정순 조립한다.
        /// <br/> Presentation 생성 실패 시 Actor까지 Spawn rollback으로 반환한다.
        /// </summary>
        // ----------------------------------------------------------------------
        public TEntityActor Create
        (
            TEntity entity,
            in TContext context
        )
        {
            var actor = actorController.Create(entity, context);

            try
            {
                if (!actorController.Attach(actor, entity))
                {
                    throw new InvalidOperationException("생성한 Detached Actor를 Entity에 Attach할 수 없습니다.");
                }

                presentationCoordinator.CreateForActor(entity, actor);
                return actor;
            }
            catch (Exception createException)
            {
                try
                {
                    actorController.Release(actor, DespawnReason.SpawnRollback);
                }
                catch (Exception rollbackException)
                {
                    throw new AggregateException
                    (
                        "Entity Actor 생성 transaction과 rollback이 모두 실패했습니다.",
                        createException,
                        rollbackException
                    );
                }

                throw;
            }
        }

        // ------------------------------------------------------------
        /// <summary>
        /// commit 전 실패한 Actor transaction을 rollback한다.
        /// </summary>
        // ------------------------------------------------------------
        public bool Rollback(TEntityActor actor)
        {
            return Release(actor, DespawnReason.SpawnRollback);
        }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/> Presentation을 역순 분리한 뒤 Actor를 terminal 반환한다.
        /// <br/> Presentation 오류가 발생해도 Actor 반환을 시도하고 모든 실패를 집계한다.
        /// </summary>
        // --------------------------------------------------------------------------------
        public bool Release
        (
            TEntityActor actor,
            DespawnReason reason
        )
        {
            if
            (
                actor == null ||
                !actorController.ContainsActor(actor)
            )
            {
                return false;
            }

            List<Exception> failures = null;
            actorController.TryGetEntity(actor, out var entity);

            try
            {
                presentationCoordinator.Release(entity, actor, reason);
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
                actorController.Release(actor, reason);
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
                : new AggregateException
                (
                    "Presentation과 Entity Actor 반환 중 오류가 발생했습니다.",
                    failures
                );
        }

    #endregion

    #region Actor Transfer

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/> 동일 Actor instance를 previous Entity에서 next Entity로 재연결하고
        /// <br/> Presentation Provider Transfer를 같은 transaction으로 적용한다.
        /// <br/> Presentation 단계 실패 시 Actor binding도 previous Entity로 rollback한다.
        /// </summary>
        // --------------------------------------------------------------------------------
        public bool Transfer(TEntityActor actor, TEntity nextEntity)
        {
            if (nextEntity == null)
            {
                throw new ArgumentNullException(nameof(nextEntity));
            }

            if
            (
                actor == null ||
                !actorController.TryGetEntity(actor, out var previousEntity)
            )
            {
                return false;
            }

            if (ReferenceEquals(previousEntity, nextEntity))
            {
                return true;
            }

            if (!actorController.Rebind(actor, nextEntity))
            {
                return false;
            }

            try
            {
                presentationCoordinator.Transfer
                (
                    previousEntity,
                    nextEntity,
                    actor
                );
                return true;
            }
            catch (Exception presentationException)
            {
                try
                {
                    if (!actorController.Rebind(actor, previousEntity))
                    {
                        throw new InvalidOperationException
                        (
                            "Presentation Transfer 실패 뒤 Actor를 previous Entity에 복구할 수 없습니다."
                        );
                    }
                }
                catch (Exception rollbackException)
                {
                    throw new AggregateException
                    (
                        "Presentation Transfer 실패와 Entity Actor rollback이 모두 실패했습니다.",
                        presentationException,
                        rollbackException
                    );
                }

                throw;
            }
        }

    #endregion

    }
}
