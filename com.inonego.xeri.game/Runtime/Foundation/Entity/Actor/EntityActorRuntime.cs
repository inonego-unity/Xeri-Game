/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : EntityActorRuntime.cs
수정일 : 2026-11-16

# 설명
EntityActorController와 EntityPresentationCoordinator를 조합해 Actor 생성·반환·Rebind transaction을 관리한다.
Actor state persistence나 gameplay activation 정책은 알지 않으며 관계 수명과 Presentation participant의 rollback만 소유한다.
Registry Binding 소유 Actor는 독립 Rebind 및 Release를 허용하지 않는다.
수명 transaction의 단위는 개별 Actor이며 같은 Actor의 중첩 변경만 거절한다.
서로 다른 Actor의 동기 수명 작업은 Provider·Factory 콜백 중에도 허용한다.
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

    #region Actor 수명 상태

        // ============================================================
        /// <summary>
        /// Actor를 생성한 수명 소유 경로.
        /// </summary>
        // ============================================================
        private enum ActorSource
        {
            Manual,
            Registry
        }

        // ============================================================
        /// <summary>
        /// 결합 Actor 수명의 현재 작업 단계.
        /// </summary>
        // ============================================================
        private enum ActorPhase
        {
            Creating,
            Active,
            Rebinding,
            Releasing
        }

        // ============================================================
        /// <summary>
        /// 개별 Actor의 출처와 Runtime 수명 진행 상태.
        /// </summary>
        // ============================================================
        private sealed class ActorEntry
        {
            internal readonly ActorSource Source;
            internal ActorPhase Phase;

            internal ActorEntry(ActorSource source)
            {
                Source = source;
                Phase = ActorPhase.Creating;
            }
        }

    #endregion

    #region 필드

        [NonSerialized]
        private readonly EntityActorController<TEntityActor, TEntity, TContext> actorController = null;

        [NonSerialized]
        private readonly EntityPresentationCoordinator<TEntityActor, TEntity> presentationCoordinator = null;

        // Actor별 소유권과 진행 상태를 하나의 기록에서 판단한다.
        [NonSerialized]
        private readonly Dictionary<TEntityActor, ActorEntry> entries = new
        (
            ReferenceEqualityComparer<TEntityActor>.Instance
        );

    #endregion

    #region 생성자

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/> 기존 Actor Controller와 Presentation Coordinator를 한 Runtime에 귀속한다.
        /// <br/> 이미 결합된 객체 또는 기존 Actor 수명이 남아 있는 실행기는 허용하지 않는다.
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

            this.actorController.ClaimRuntime();

            try
            {
                this.presentationCoordinator.ClaimRuntime();
            }
            catch
            {
                this.actorController.ReleaseRuntimeClaim();
                throw;
            }
        }

    #endregion

    #region Actor 생성

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> 수동 Actor를 생성하고 Presentation까지 같은 수명으로 구성한다.
        /// <br/> 실패 시 Actor 자원을 Spawn rollback으로 반환한다.
        /// </summary>
        // ----------------------------------------------------------------------
        public TEntityActor Create(TEntity entity, in TContext context)
        {
            return CreateCore(entity, context, ActorSource.Manual);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Registry 등록 수명이 관리할 Actor를 생성한다.
        /// </summary>
        // ------------------------------------------------------------
        internal TEntityActor CreateRegistryOwned(TEntity entity, in TContext context)
        {
            return CreateCore(entity, context, ActorSource.Registry);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 출처가 확정된 Actor를 생성하고 Provider 수명까지 결합한다.
        /// </summary>
        // ------------------------------------------------------------
        private TEntityActor CreateCore(TEntity entity, in TContext context, ActorSource source)
        {
            // 새 Actor는 자신의 수명 기록으로만 보호되므로 다른 Actor 작업과 독립적으로 생성한다.
            var actor = actorController.CreateCore(entity, context);
            var entry = new ActorEntry(source);

            try
            {
                entries.Add(actor, entry);

                if (!actorController.AttachCore(actor, entity))
                {
                    throw new InvalidOperationException("생성한 Detached Actor를 Entity에 Attach할 수 없습니다.");
                }

                presentationCoordinator.CreateCore(actor, entity);
                entry.Phase = ActorPhase.Active;
                return actor;
            }
            catch (Exception createException)
            {
                try
                {
                    // Coordinator는 자신의 부분 생성 실패를 별도로 rollback한다.
                    actorController.ReleaseCore(actor, DespawnReason.SpawnRollback);
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
                finally
                {
                    RemoveEntry(actor, entry);
                }

                throw;
            }
        }

    #endregion

    #region Actor 반환

        // ------------------------------------------------------------
        /// <summary>
        /// commit 전 수동 Actor transaction을 rollback한다.
        /// </summary>
        // ------------------------------------------------------------
        public bool Rollback(TEntityActor actor)
        {
            return Release(actor, DespawnReason.SpawnRollback);
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> 수동 Actor의 결합 Presentation과 Actor를 terminal 반환한다.
        /// <br/> Registry 소유 Actor는 Registry 해제 경로에서만 반환할 수 있다.
        /// </summary>
        // ----------------------------------------------------------------------
        public bool Release(TEntityActor actor, DespawnReason reason)
        {
            return ReleaseCore(actor, reason, ActorSource.Manual);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Registry Binding이 소유한 Actor를 terminal 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        internal bool ReleaseRegistryOwned(TEntityActor actor, DespawnReason reason)
        {
            return ReleaseCore(actor, reason, ActorSource.Registry);
        }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/> 진입 검증 뒤 Presentation과 Actor 반환을 끝까지 실행한다.
        /// <br/> 시작 전 거절은 상태를 바꾸지 않고, 시작한 반환은 실패해도 소유권을 종료한다.
        /// </summary>
        // --------------------------------------------------------------------------------
        private bool ReleaseCore(TEntityActor actor, DespawnReason reason, ActorSource source)
        {
            if (ReferenceEquals(actor, null) || !entries.TryGetValue(actor, out var entry))
            {
                if (source == ActorSource.Registry)
                {
                    throw new InvalidOperationException("Registry Binding이 소유하는 Actor 수명을 찾을 수 없습니다.");
                }

                return false;
            }

            if (entry.Source != source)
            {
                throw new InvalidOperationException
                (
                    "Actor를 생성한 수명 경로에서만 해당 Actor를 반환할 수 있습니다."
                );
            }

            if (entry.Phase != ActorPhase.Active)
            {
                throw new InvalidOperationException("활성 상태의 Actor만 terminal 반환할 수 있습니다.");
            }

            entry.Phase = ActorPhase.Releasing;

            try
            {
                List<Exception> failures = null;
                actorController.TryGetEntity(actor, out var entity);

                try
                {
                    presentationCoordinator.ReleaseCore(actor, entity, reason);
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
                    actorController.ReleaseCore(actor, reason);
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
            finally
            {
                RemoveEntry(actor, entry);
            }
        }

    #endregion

    #region Actor Rebind

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/> 수동 Actor의 Entity 관계와 Presentation을 하나의 transaction으로 변경한다.
        /// <br/> Presentation 단계 실패 시 기존 Entity 관계 복원을 시도한다.
        /// </summary>
        // --------------------------------------------------------------------------------
        public bool Rebind(TEntityActor actor, TEntity nextEntity)
        {
            if (nextEntity == null)
            {
                throw new ArgumentNullException(nameof(nextEntity));
            }

            if
            (
                ReferenceEquals(actor, null) ||
                !entries.TryGetValue(actor, out var entry) ||
                !actorController.TryGetEntity(actor, out var previousEntity)
            )
            {
                return false;
            }

            if (entry.Phase != ActorPhase.Active)
            {
                throw new InvalidOperationException("활성 상태의 Actor만 Entity를 Rebind할 수 있습니다.");
            }

            if (ReferenceEquals(previousEntity, nextEntity))
            {
                return true;
            }

            if (entry.Source != ActorSource.Manual)
            {
                throw new InvalidOperationException
                (
                    "Registry Binding이 소유하는 Actor는 독립적으로 Entity를 Rebind할 수 없습니다."
                );
            }

            entry.Phase = ActorPhase.Rebinding;

            try
            {
                if (!actorController.RebindCore(actor, nextEntity))
                {
                    return false;
                }

                try
                {
                    presentationCoordinator.RebindEntityCore
                    (
                        actor,
                        previousEntity,
                        nextEntity
                    );
                    return true;
                }
                catch (Exception presentationException)
                {
                    try
                    {
                        if (!actorController.RebindCore(actor, previousEntity))
                        {
                            throw new InvalidOperationException
                            (
                                "Presentation Rebind 실패 뒤 Actor를 이전 Entity에 복구할 수 없습니다."
                            );
                        }
                    }
                    catch (Exception rollbackException)
                    {
                        throw new AggregateException
                        (
                            "Presentation Rebind 실패와 Entity Actor rollback이 모두 실패했습니다.",
                            presentationException,
                            rollbackException
                        );
                    }

                    throw;
                }
            }
            finally
            {
                entry.Phase = ActorPhase.Active;
            }
        }

    #endregion

    #region Actor 수명 기록

        // ------------------------------------------------------------
        /// <summary>
        /// 이 Runtime이 Actor의 수명을 소유 중인지 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        internal bool OwnsActor(TEntityActor actor) =>
            !ReferenceEquals(actor, null) && entries.ContainsKey(actor);

        // ------------------------------------------------------------
        /// <summary>
        /// 같은 수명 기록일 때만 제거해 다른 수명 기록을 침범하지 않는다.
        /// </summary>
        // ------------------------------------------------------------
        private void RemoveEntry(TEntityActor actor, ActorEntry entry)
        {
            if (entries.TryGetValue(actor, out var current) && ReferenceEquals(current, entry))
            {
                entries.Remove(actor);
            }
        }

    #endregion

    }
}
