/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : EntityPresentationRegistry.cs
수정일 : 2026-10-05

# 설명
Presentation 객체 수명과 Owner binding 관계를 분리해 추적한다.
등록된 Presentation은 Detached 또는 Bound(Owner)일 수 있으며 Attach/Detach/Rebind 관계 알림을 제공한다.
구체 Presentation의 생성·Bind·Despawn·Pool 수명은 소유하지 않는다.
========================================================================= BLOCK_HEADER_END */

using System;
using System.Collections;
using System.Collections.Generic;

using UnityEngine;

namespace inonego.Xeri.Game
{
    // ======================================================================
    /// <summary>
    /// Presentation Registry의 단일 Attach/Detach 사실을 전달하는 핸들러.
    /// </summary>
    // ======================================================================
    public delegate void EntityPresentationRegistryEventHandler<TOwner, TPresentation>
    (
        object sender,
        EntityPresentationRegistryEventArgs<TOwner, TPresentation> e
    )
    where TOwner : class
    where TPresentation : class;

    // ============================================================
    /// <summary>
    /// Presentation Registry의 Owner 관계 변경 사실을 전달한다.
    /// </summary>
    // ============================================================
    public sealed class EntityPresentationRegistryEventArgs<TOwner, TPresentation> : EventArgs
    where TOwner : class
    where TPresentation : class
    {

    #region 필드

        // ------------------------------------------------------------
        /// <summary>
        /// 관계가 연결되거나 종료된 owner.
        /// </summary>
        // ------------------------------------------------------------
        public TOwner Owner { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 관계 또는 표시 수명을 연결하는 Presentation.
        /// </summary>
        // ------------------------------------------------------------
        public TPresentation Presentation { get; }

    #endregion

    #region 생성자

        // ------------------------------------------------------------
        /// <summary>
        /// 확정된 owner 관계와 Presentation을 알림 값으로 보존한다.
        /// </summary>
        // ------------------------------------------------------------
        public EntityPresentationRegistryEventArgs
        (
            TOwner owner,
            TPresentation presentation
        )
        {
            Owner = owner ?? throw new ArgumentNullException(nameof(owner));
            Presentation = presentation ?? throw new ArgumentNullException(nameof(presentation));
        }

    #endregion

    }

    // ================================================================================
    /// <summary>
    /// <br/> Presentation 객체 수명과 Owner reference 기준 0..N binding 관계를
    /// <br/> Detached/Bound 상태로 분리 관리한다.
    /// </summary>
    // ================================================================================
    [Serializable]
    public sealed class EntityPresentationRegistry<TOwner, TPresentation>
    where TOwner : class
    where TPresentation : class
    {

    #region 필드

        // ------------------------------------------------------------
        /// <summary>
        /// 현재 객체 수명을 추적하는 모든 Presentation.
        /// </summary>
        // ------------------------------------------------------------
        public IEnumerable<TPresentation> Presentations => registered;

        [NonSerialized]
        private readonly HashSet<TPresentation> registered = new
        (
            ReferenceEqualityComparer<TPresentation>.Instance
        );

        [NonSerialized]
        private readonly Dictionary<TOwner, List<TPresentation>> presentations = new
        (
            ReferenceEqualityComparer<TOwner>.Instance
        );

        [NonSerialized]
        private readonly Dictionary<TPresentation, TOwner> owners = new
        (
            ReferenceEqualityComparer<TPresentation>.Instance
        );

        // ------------------------------------------------------------
        /// <summary>
        /// 현재 Owner에 연결된 Presentation.
        /// </summary>
        // ------------------------------------------------------------
        public IEnumerable<TPresentation> BoundPresentations => owners.Keys;

        // ------------------------------------------------------------
        /// <summary>
        /// 현재 등록되어 있지만 Owner가 없는 Detached Presentation.
        /// </summary>
        // ------------------------------------------------------------
        public IEnumerable<TPresentation> DetachedPresentations
        {
            get
            {
                foreach (var presentation in registered)
                {
                    if (!owners.ContainsKey(presentation))
                    {
                        yield return presentation;
                    }
                }
            }
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 현재 추적 중인 전체 Presentation 수.
        /// </summary>
        // ------------------------------------------------------------
        public int Count => registered.Count;

        // ------------------------------------------------------------
        /// <summary>
        /// 현재 Bound Presentation 수.
        /// </summary>
        // ------------------------------------------------------------
        public int BoundCount => owners.Count;

    #endregion

    #region 이벤트

        // ------------------------------------------------------------
        /// <summary>
        /// Presentation owner 관계의 연결 완료를 알린다.
        /// </summary>
        // ------------------------------------------------------------
        public event EntityPresentationRegistryEventHandler<TOwner, TPresentation> OnAttach = null;
        // ------------------------------------------------------------
        /// <summary>
        /// Presentation owner 관계의 종료 완료를 알린다.
        /// </summary>
        // ------------------------------------------------------------
        public event EntityPresentationRegistryEventHandler<TOwner, TPresentation> OnDetach = null;
        // ------------------------------------------------------------
        /// <summary>
        /// 모든 객체 등록과 owner 관계의 제거 완료를 알린다.
        /// </summary>
        // ------------------------------------------------------------
        public event EventHandler OnClear = null;

    #endregion

    #region 객체 등록

        // ------------------------------------------------------------
        /// <summary>
        /// Detached Presentation 객체 수명을 Registry에 등록한다.
        /// </summary>
        // ------------------------------------------------------------
        public void Register(TPresentation presentation)
        {
            if (presentation == null)
            {
                throw new ArgumentNullException(nameof(presentation));
            }

            if (!registered.Add(presentation))
            {
                throw new InvalidOperationException("이미 Registry에 등록된 Presentation입니다.");
            }
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Detached Presentation의 객체 수명 등록을 제거한다.
        /// </summary>
        // ------------------------------------------------------------
        public bool Unregister(TPresentation presentation)
        {
            if (presentation == null || !registered.Contains(presentation))
            {
                return false;
            }

            if (owners.ContainsKey(presentation))
            {
                throw new InvalidOperationException("Bound Presentation은 Detach한 뒤 Registry에서 제거해야 합니다.");
            }

            return registered.Remove(presentation);
        }

    #endregion

    #region 관계 연결과 해제

        // ------------------------------------------------------------
        /// <summary>
        /// 등록된 Detached Presentation을 Owner에 연결한다.
        /// </summary>
        // ------------------------------------------------------------
        public void Attach(TOwner owner, TPresentation presentation)
        {
            ValidateOwnerAndPresentation(owner, presentation);

            if (owners.ContainsKey(presentation))
            {
                throw new InvalidOperationException("이미 Owner에 연결된 Presentation입니다.");
            }

            AddBinding(owner, presentation);
            PublishAttach(owner, presentation);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Bound Presentation의 Owner 관계를 제거하고 객체 등록은 유지한다.
        /// </summary>
        // ------------------------------------------------------------
        public bool Detach(TPresentation presentation)
        {
            if (presentation == null || !owners.Remove(presentation, out var owner))
            {
                return false;
            }

            RemoveOwnerPresentation(owner, presentation);
            PublishDetach(owner, presentation);
            return true;
        }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/> 동일 Presentation을 terminal 반환하지 않고 old Owner에서 next Owner로
        /// <br/> 원자적으로 재연결한다.
        /// </summary>
        // --------------------------------------------------------------------------------
        public bool Rebind(TPresentation presentation, TOwner nextOwner)
        {
            if (nextOwner == null)
            {
                throw new ArgumentNullException(nameof(nextOwner));
            }

            if
            (
                presentation == null ||
                !registered.Contains(presentation) ||
                !owners.TryGetValue(presentation, out var previousOwner)
            )
            {
                return false;
            }

            if (ReferenceEquals(previousOwner, nextOwner))
            {
                return true;
            }

            RemoveOwnerPresentation(previousOwner, presentation);
            owners[presentation] = nextOwner;
            AddOwnerPresentation(nextOwner, presentation);

            PublishDetach(previousOwner, presentation);
            PublishAttach(nextOwner, presentation);
            return true;
        }

    #endregion

    #region 조회

        // ----------------------------------------------------------------------
        /// <summary>
        /// 동일 Presentation 객체 수명을 현재 Registry가 추적하는지 반환한다.
        /// </summary>
        // ----------------------------------------------------------------------
        public bool Contains(TPresentation presentation) =>
            presentation != null && registered.Contains(presentation);

        // ------------------------------------------------------------
        /// <summary>
        /// 동일 Presentation이 현재 Owner에 연결되어 있는지 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        public bool IsBound(TPresentation presentation) =>
            presentation != null && owners.ContainsKey(presentation);

        // ------------------------------------------------------------
        /// <summary>
        /// 동일 Presentation의 현재 Owner를 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        public bool TryGetOwner(TPresentation presentation, out TOwner owner)
        {
            owner = null;
            return presentation != null && owners.TryGetValue(presentation, out owner);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Owner에 대응하는 모든 Bound Presentation을 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        public IReadOnlyList<TPresentation> FindAll(TOwner owner)
        {
            if (owner == null)
            {
                return Array.Empty<TPresentation>();
            }

            return presentations.TryGetValue(owner, out var values)
                ? values
                : Array.Empty<TPresentation>();
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 지정 concrete 타입의 Presentation이 하나일 때 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        public bool TryFindSingle<TConcrete>
        (
            TOwner owner,
            out TConcrete presentation
        )
        where TConcrete : class
        {
            presentation = null;

            if (owner == null || !presentations.TryGetValue(owner, out var values))
            {
                return false;
            }

            for (var index = 0; index < values.Count; index++)
            {
                if (values[index] is not TConcrete candidate)
                {
                    continue;
                }

                if (presentation != null)
                {
                    throw new InvalidOperationException
                    (
                        $"동일 Owner에 {typeof(TConcrete).Name} Presentation이 둘 이상 연결되어 있습니다."
                    );
                }

                presentation = candidate;
            }

            return presentation != null;
        }

    #endregion

    #region 정리

        // ------------------------------------------------------------
        /// <summary>
        /// 객체 등록과 owner 관계를 비우고 완료 observer를 모두 호출한다.
        /// </summary>
        // ------------------------------------------------------------
        public void Clear()
        {
            if (registered.Count == 0)
            {
                return;
            }

            presentations.Clear();
            owners.Clear();
            registered.Clear();

            // Clear도 commit 이후 observer 실패를 격리해 모든 소비자의 정리가 진행되게 한다.
            var handlers = OnClear?.GetInvocationList();
            if (handlers == null) return;

            for (var index = 0; index < handlers.Length; index++)
            {
                try
                {
                    ((EventHandler)handlers[index]).Invoke(this, EventArgs.Empty);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
            }
        }

    #endregion

    #region 관계 인덱스와 알림

        // ------------------------------------------------------------
        /// <summary>
        /// 관계 변경에 필요한 owner와 객체 등록을 검증한다.
        /// </summary>
        // ------------------------------------------------------------
        private void ValidateOwnerAndPresentation
        (
            TOwner owner,
            TPresentation presentation
        )
        {
            if (owner == null)
            {
                throw new ArgumentNullException(nameof(owner));
            }

            if (presentation == null)
            {
                throw new ArgumentNullException(nameof(presentation));
            }

            if (!registered.Contains(presentation))
            {
                throw new InvalidOperationException("Registry에 등록되지 않은 Presentation은 Attach할 수 없습니다.");
            }
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 동일 Presentation의 owner와 역방향 조회를 함께 추가한다.
        /// </summary>
        // ------------------------------------------------------------
        private void AddBinding(TOwner owner, TPresentation presentation)
        {
            AddOwnerPresentation(owner, presentation);
            owners.Add(presentation, owner);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// owner의 Presentation 목록에 동일 객체를 추가한다.
        /// </summary>
        // ------------------------------------------------------------
        private void AddOwnerPresentation(TOwner owner, TPresentation presentation)
        {
            if (!presentations.TryGetValue(owner, out var values))
            {
                values = new List<TPresentation>();
                presentations.Add(owner, values);
            }

            values.Add(presentation);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// owner의 Presentation 목록에서 동일 객체를 제거한다.
        /// </summary>
        // ------------------------------------------------------------
        private void RemoveOwnerPresentation(TOwner owner, TPresentation presentation)
        {
            if (!presentations.TryGetValue(owner, out var values))
            {
                return;
            }

            for (var index = values.Count - 1; index >= 0; index--)
            {
                if (!ReferenceEquals(values[index], presentation))
                {
                    continue;
                }

                values.RemoveAt(index);
                break;
            }

            if (values.Count == 0)
            {
                presentations.Remove(owner);
            }
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 확정된 Attach 관계를 observer에 전달한다.
        /// </summary>
        // ------------------------------------------------------------
        private void PublishAttach(TOwner owner, TPresentation presentation)
        {
            PublishObservers
            (
                OnAttach,
                new EntityPresentationRegistryEventArgs<TOwner, TPresentation>
                (
                    owner,
                    presentation
                )
            );
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 확정된 Detach 관계를 observer에 전달한다.
        /// </summary>
        // ------------------------------------------------------------
        private void PublishDetach(TOwner owner, TPresentation presentation)
        {
            PublishObservers
            (
                OnDetach,
                new EntityPresentationRegistryEventArgs<TOwner, TPresentation>
                (
                    owner,
                    presentation
                )
            );
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 관계 observer의 개별 실패를 격리하고 나머지 알림을 계속한다.
        /// </summary>
        // ------------------------------------------------------------
        private void PublishObservers
        (
            EntityPresentationRegistryEventHandler<TOwner, TPresentation> observers,
            EntityPresentationRegistryEventArgs<TOwner, TPresentation> args
        )
        {
            var handlers = observers?.GetInvocationList();
            if (handlers == null) return;

            for (var index = 0; index < handlers.Length; index++)
            {
                try
                {
                    ((EntityPresentationRegistryEventHandler<TOwner, TPresentation>)handlers[index]).Invoke
                    (
                        this,
                        args
                    );
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
            }
        }

    #endregion

    }
}
