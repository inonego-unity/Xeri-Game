/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : EntityActorRegistry.cs
수정일 : 2026-10-03

# 설명
EntityActorController가 소유하는 Actor 객체와 Entity binding 관계를 분리해 추적한다.
Actor는 Registered 상태에서 Detached 또는 Bound(Entity)일 수 있으며 Registry는 생성/반환을 수행하지 않는다.
========================================================================= BLOCK_HEADER_END */

using System;
using System.Collections;
using System.Collections.Generic;

namespace inonego.Xeri.Game
{
    // ================================================================================
    /// <summary>
    /// <br/> Actor 객체 수명과 Entity reference 기준 0..N binding 관계를 저장한다.
    /// <br/> 등록된 Actor는 Bound 또는 Detached 상태 중 하나다.
    /// </summary>
    // ================================================================================
    [Serializable]
    public sealed class EntityActorRegistry<TEntityActor, TEntity>
    where TEntityActor : EntityActorBase<TEntity>
    where TEntity : class, IEntity
    {

    #region 필드

        // ------------------------------------------------------------
        /// <summary>
        /// 현재 Registry가 수명을 추적하는 모든 Actor.
        /// </summary>
        // ------------------------------------------------------------
        public IEnumerable<TEntityActor> Actors => actors;

        [NonSerialized]
        private readonly HashSet<TEntityActor> actors = new
        (
            ReferenceEqualityComparer<TEntityActor>.Instance
        );

        [NonSerialized]
        private readonly Dictionary<TEntity, List<TEntityActor>> actorsByEntity = new
        (
            ReferenceEqualityComparer<TEntity>.Instance
        );

        [NonSerialized]
        private readonly Dictionary<TEntityActor, TEntity> entitiesByActor = new
        (
            ReferenceEqualityComparer<TEntityActor>.Instance
        );

        // ------------------------------------------------------------
        /// <summary>
        /// 현재 Entity에 연결된 Actor.
        /// </summary>
        // ------------------------------------------------------------
        public IEnumerable<TEntityActor> BoundActors => entitiesByActor.Keys;

        // ----------------------------------------------------------------------
        /// <summary>
        /// 현재 등록되어 있지만 Entity에 연결되지 않은 Detached Actor를 열거한다.
        /// </summary>
        // ----------------------------------------------------------------------
        public IEnumerable<TEntityActor> DetachedActors
        {
            get
            {
                foreach (var actor in actors)
                {
                    if (!entitiesByActor.ContainsKey(actor))
                    {
                        yield return actor;
                    }
                }
            }
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 현재 추적 중인 전체 Actor 수.
        /// </summary>
        // ------------------------------------------------------------
        public int Count => actors.Count;

        // ------------------------------------------------------------
        /// <summary>
        /// 현재 Bound Actor 수.
        /// </summary>
        // ------------------------------------------------------------
        public int BoundCount => entitiesByActor.Count;

    #endregion

    #region 객체 등록

        // ------------------------------------------------------------
        /// <summary>
        /// Detached Actor 객체 수명을 Registry에 등록한다.
        /// </summary>
        // ------------------------------------------------------------
        public void Register(TEntityActor actor)
        {
            if (actor == null)
            {
                throw new ArgumentNullException(nameof(actor));
            }

            if (actor.IsBound)
            {
                throw new InvalidOperationException("Entity에 연결된 Actor는 Detached 등록할 수 없습니다.");
            }

            if (!actors.Add(actor))
            {
                throw new InvalidOperationException("동일 Entity Actor를 중복 등록할 수 없습니다.");
            }
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Detached Actor의 객체 수명 등록을 제거한다.
        /// </summary>
        // ------------------------------------------------------------
        public bool Unregister(TEntityActor actor)
        {
            if (actor == null || !actors.Contains(actor))
            {
                return false;
            }

            if (entitiesByActor.ContainsKey(actor) || actor.IsBound)
            {
                throw new InvalidOperationException("Bound Actor는 Detach한 뒤 Registry에서 제거해야 합니다.");
            }

            return actors.Remove(actor);
        }

    #endregion

    #region Binding

        // ----------------------------------------------------------------------
        /// <summary>
        /// 등록된 Detached Actor와 이미 연결된 Entity 관계를 Registry에 반영한다.
        /// </summary>
        // ----------------------------------------------------------------------
        public void Attach(TEntity entity, TEntityActor actor)
        {
            if (entity == null)
            {
                throw new ArgumentNullException(nameof(entity));
            }

            if (actor == null)
            {
                throw new ArgumentNullException(nameof(actor));
            }

            if (!actors.Contains(actor))
            {
                throw new InvalidOperationException("Registry에 등록되지 않은 Actor는 Attach할 수 없습니다.");
            }

            if (!ReferenceEquals(actor.Entity, entity))
            {
                throw new InvalidOperationException("Actor의 실제 Entity binding과 Registry owner가 일치하지 않습니다.");
            }

            if (entitiesByActor.ContainsKey(actor))
            {
                throw new InvalidOperationException("이미 Entity에 연결된 Actor입니다.");
            }

            if (!actorsByEntity.TryGetValue(entity, out var values))
            {
                values = new List<TEntityActor>();
                actorsByEntity.Add(entity, values);
            }

            values.Add(actor);
            entitiesByActor.Add(actor, entity);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Actor의 Entity binding mapping만 제거하고 객체 등록은 유지한다.
        /// </summary>
        // ------------------------------------------------------------
        public bool Detach(TEntityActor actor)
        {
            if (actor == null || !entitiesByActor.Remove(actor, out var entity))
            {
                return false;
            }

            if (actor.IsBound)
            {
                entitiesByActor.Add(actor, entity);
                throw new InvalidOperationException("Actor의 실제 Entity binding을 먼저 해제해야 합니다.");
            }

            RemoveEntityActor(entity, actor);
            return true;
        }

    #endregion

    #region 조회

        // ------------------------------------------------------------
        /// <summary>
        /// Entity에 대응하는 모든 Bound Actor를 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        public IReadOnlyList<TEntityActor> FindAll(TEntity entity)
        {
            if (entity == null)
            {
                return Array.Empty<TEntityActor>();
            }

            return actorsByEntity.TryGetValue(entity, out var values)
                ? values
                : Array.Empty<TEntityActor>();
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Entity에 대응하는 Bound Actor가 하나일 때만 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        public bool TryFindSingle(TEntity entity, out TEntityActor actor)
        {
            actor = null;

            if (entity == null || !actorsByEntity.TryGetValue(entity, out var values))
            {
                return false;
            }

            if (values.Count != 1)
            {
                return false;
            }

            actor = values[0];
            return true;
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// 동일 Actor reference의 객체 수명을 현재 Registry가 추적하는지 반환한다.
        /// </summary>
        // ----------------------------------------------------------------------
        public bool Contains(TEntityActor actor) => actor != null && actors.Contains(actor);

        // ------------------------------------------------------------
        /// <summary>
        /// 동일 Actor가 현재 Entity에 연결되어 있는지 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        public bool IsBound(TEntityActor actor) => actor != null && entitiesByActor.ContainsKey(actor);

        // ------------------------------------------------------------
        /// <summary>
        /// 동일 Actor의 현재 Entity owner를 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        public bool TryGetEntity(TEntityActor actor, out TEntity entity)
        {
            entity = null;
            return actor != null && entitiesByActor.TryGetValue(actor, out entity);
        }

    #endregion

    #region 정리

        // ------------------------------------------------------------
        /// <summary>
        /// 모든 Actor 객체 등록과 binding mapping을 제거한다.
        /// </summary>
        // ------------------------------------------------------------
        public void Clear()
        {
            actorsByEntity.Clear();
            entitiesByActor.Clear();
            actors.Clear();
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// Entity별 Bound Actor 목록에서 동일 Actor reference를 제거한다.
        /// </summary>
        // ----------------------------------------------------------------------
        private void RemoveEntityActor(TEntity entity, TEntityActor actor)
        {
            if (!actorsByEntity.TryGetValue(entity, out var values))
            {
                return;
            }

            for (var index = values.Count - 1; index >= 0; index--)
            {
                if (!ReferenceEquals(values[index], actor))
                {
                    continue;
                }

                values.RemoveAt(index);
                break;
            }

            if (values.Count == 0)
            {
                actorsByEntity.Remove(entity);
            }
        }

    #endregion

    }
}