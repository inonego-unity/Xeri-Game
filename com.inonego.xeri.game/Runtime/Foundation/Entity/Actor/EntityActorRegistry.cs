/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : EntityActorRegistry.cs
수정일 : 2026-11-16

# 설명
EntityActorController가 소유하는 Actor 객체와 Entity binding 관계를 분리해 추적한다.
Actor는 Registered 상태에서 Detached 또는 Bound(Entity)일 수 있으며 Registry는 생성/반환을 수행하지 않는다.
외부 조회는 원본 컬렉션 수정 권한 없이 Xeri Core ReadOnlySetView를 재사용한다.

# 특이사항, 제약사항
등록·연결 변경 API는 어셈블리 내부(Controller)에서만 호출한다. 외부에서는 Actor의 실제 Entity binding을
만들 수 없어 Registry만 단독으로 변경해도 정합성이 성립하지 않기 때문이다.
하나의 Registry는 하나의 Controller만 소유할 수 있다.
같은 Actor 인스턴스가 풀에서 재사용돼도 등록 수명마다 새 식별자를 부여해 이전 수명과 구분한다.
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

    #region 내부 데이터

        // ============================================================
        /// <summary>
        /// Entity별 Actor 목록과 지연 생성되는 읽기 전용 View를 보관한다.
        /// </summary>
        // ============================================================
        private sealed class ActorGroup
        {
            internal readonly List<TEntityActor> actors = new();

            private IReadOnlyList<TEntityActor> view = null;

            internal IReadOnlyList<TEntityActor> View
            {
                get
                {
                    view ??= actors.AsReadOnly();
                    return view;
                }
            }
        }

    #endregion

    #region 필드

        // ------------------------------------------------------------
        /// <summary>
        /// 현재 Registry가 수명을 추적하는 모든 Actor.
        /// </summary>
        // ------------------------------------------------------------
        public ReadOnlySetView<TEntityActor> Actors => new(actors);

        // 하나의 Registry는 하나의 Controller만 소유한다.
        [NonSerialized]
        private bool isClaimed = false;

        [NonSerialized]
        private readonly HashSet<TEntityActor> actors = new
        (
            ReferenceEqualityComparer<TEntityActor>.Instance
        );

        // 같은 인스턴스가 풀에서 재사용돼도 등록 수명을 구분하는 단조 증가 번호.
        [NonSerialized]
        private readonly Dictionary<TEntityActor, Revision> registrationGenerations = new
        (
            ReferenceEqualityComparer<TEntityActor>.Instance
        );

        [NonSerialized]
        private Revision lastRegistrationGeneration = Revision.None;

        [NonSerialized]
        private readonly Dictionary<TEntity, ActorGroup> actorsByEntity = new
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

    #region Controller 소유권

        // ------------------------------------------------------------
        /// <summary>
        /// Controller가 객체 등록과 관계 변경의 독점 권한을 획득한다.
        /// </summary>
        // ------------------------------------------------------------
        internal void ClaimController()
        {
            if (isClaimed || actors.Count != 0)
            {
                throw new InvalidOperationException("등록된 Actor가 없고 소유자가 없는 Registry만 Controller에 연결할 수 있습니다.");
            }

            isClaimed = true;
        }

    #endregion

    #region 객체 등록

        // ------------------------------------------------------------
        /// <summary>
        /// Detached Actor 객체 수명을 Registry에 등록한다.
        /// </summary>
        // ------------------------------------------------------------
        internal void Register(TEntityActor actor)
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

            lastRegistrationGeneration = lastRegistrationGeneration.Inc();
            registrationGenerations[actor] = lastRegistrationGeneration;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Detached Actor의 객체 수명 등록을 제거한다.
        /// </summary>
        // ------------------------------------------------------------
        internal bool Unregister(TEntityActor actor)
        {
            if (actor == null || !actors.Contains(actor))
            {
                return false;
            }

            if (entitiesByActor.ContainsKey(actor) || actor.IsBound)
            {
                throw new InvalidOperationException("Bound Actor는 Detach한 뒤 Registry에서 제거해야 합니다.");
            }

            registrationGenerations.Remove(actor);
            return actors.Remove(actor);
        }

    #endregion

    #region Entity 관계

        // ----------------------------------------------------------------------
        /// <summary>
        /// 등록된 Detached Actor와 이미 연결된 Entity 관계를 Registry에 반영한다.
        /// </summary>
        // ----------------------------------------------------------------------
        internal void Attach(TEntityActor actor, TEntity entity)
        {
            if (actor == null)
            {
                throw new ArgumentNullException(nameof(actor));
            }

            if (entity == null)
            {
                throw new ArgumentNullException(nameof(entity));
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

            if (!actorsByEntity.TryGetValue(entity, out var group))
            {
                group = new ActorGroup();
                actorsByEntity.Add(entity, group);
            }

            group.actors.Add(actor);
            entitiesByActor.Add(actor, entity);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Actor의 Entity binding mapping만 제거하고 객체 등록은 유지한다.
        /// </summary>
        // ------------------------------------------------------------
        internal bool Detach(TEntityActor actor)
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

        // ----------------------------------------------------------------------
        /// <summary>
        /// Entity별 Bound Actor 목록에서 동일 Actor reference를 제거한다.
        /// </summary>
        // ----------------------------------------------------------------------
        private void RemoveEntityActor(TEntity entity, TEntityActor actor)
        {
            if (!actorsByEntity.TryGetValue(entity, out var group))
            {
                return;
            }

            var values = group.actors;

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

            return actorsByEntity.TryGetValue(entity, out var group)
                ? group.View
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

            if (entity == null || !actorsByEntity.TryGetValue(entity, out var group))
            {
                return false;
            }

            if (group.actors.Count != 1)
            {
                return false;
            }

            actor = group.actors[0];
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
        /// 현재 등록 수명 번호를 반환한다. 등록되어 있지 않으면 None이다.
        /// </summary>
        // ------------------------------------------------------------
        internal Revision GetRegistrationGeneration(TEntityActor actor)
        {
            return actor != null && registrationGenerations.TryGetValue(actor, out var generation)
                ? generation : Revision.None;
        }

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

    }
}
