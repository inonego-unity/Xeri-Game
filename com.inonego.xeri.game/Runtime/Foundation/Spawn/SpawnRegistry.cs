/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : SpawnRegistry.cs
수정일 : 2026-11-14

# 설명
스폰 레지스트리 베이스·구현체와 등록 객체 사전, 디스폰 확장 메서드 정의.

- SpawnRegistryUtility       : IDespawnable.Despawn과 Despawn(reason) 확장 메서드
- RegisteredDictionary<,>        : XDictionary_VR 기반 등록 사전
- SpawnRegistryBase<,>        : 핵심 스폰/디스폰/검색/0..N 필수 바인딩/이벤트 로직
- SpawnRegistry<,>            : 파라미터 없는 Acquire/Spawn
- SpawnRegistry<,,>           : 파라미터 받는 Acquire/Spawn (INeedToInit<TParam> 연동)

등록 사전은 직렬화하며, 진행 중 Spawn·Binding 동기화·전체 디스폰 플래그와 공개 이벤트는 비직렬화 런타임 상태다.
========================================================================= BLOCK_HEADER_END */

using System;
using System.Collections;
using System.Collections.Generic;

using UnityEngine;

namespace inonego.Xeri.Game
{
    using Xeri;
    using Xeri.Serializable;

    // ============================================================
    /// <summary>
    /// IDespawnable 디스폰 확장 메서드.
    /// </summary>
    // ============================================================
    public static class SpawnRegistryUtility
    {
        // ------------------------------------------------------------
        /// <summary>
        /// 객체를 일반 제거 사유로 디스폰한다.
        /// </summary>
        // ------------------------------------------------------------
        public static void Despawn(this IDespawnable despawnable)
        {
            Despawn(despawnable, DespawnReason.Removed);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 객체를 디스폰한다.
        /// </summary>
        // ------------------------------------------------------------
        public static void Despawn(this IDespawnable despawnable, DespawnReason reason)
        {
            if (despawnable == null)
            {
                throw new ArgumentNullException(nameof(despawnable));
            }

            var despawnFromRegistry = despawnable.DespawnFromRegistry;

            if (despawnFromRegistry == null)
            {
                throw new InvalidOperationException("객체가 SpawnRegistry에 등록되어 있지 않습니다.");
            }

            despawnFromRegistry.Invoke(reason);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 정상 디스폰 요청에 사용할 수 있는 사유인지 확인한다.
        /// </summary>
        // ------------------------------------------------------------
        internal static void ValidateReason(DespawnReason reason)
        {
            if (!reason.IsValid)
            {
                throw new ArgumentException("유효하지 않은 디스폰 사유입니다.", nameof(reason));
            }
        }
    }

    // ============================================================
    /// <summary>
    /// 등록된 객체를 보관하는 직렬화 가능한 사전.
    /// </summary>
    // ============================================================
    [Serializable]
    public class RegisteredDictionary<TKey, T> : XDictionary_VR<TKey, T>, IReadOnlyDictionary<TKey, T>
    where TKey : IEquatable<TKey>
    where T : class, ISpawnRegistryObject<TKey>
    {
        // NONE
    }

    // ============================================================
    /// <summary>
    /// 스폰을 관리하기 위한 베이스 클래스.
    /// </summary>
    // ============================================================
    [Serializable]
    public abstract class SpawnRegistryBase<TKey, T>
        : ISpawnRegistry<TKey, T>, ISerializationCallbackReceiver
    where TKey : IEquatable<TKey>
    where T : class, ISpawnRegistryObject<TKey>
    {

    #region 필드

        // ------------------------------------------------------------
        /// <summary>
        /// 현재 등록된 모든 객체의 사전(읽기 전용 노출).
        /// </summary>
        // ------------------------------------------------------------
        public IReadOnlyDictionary<TKey, T> Registered
        {
            get
            {
                registeredView ??= new RegisteredReadOnlyView(this);
                return registeredView;
            }
        }

        [NonSerialized]
        private IReadOnlyDictionary<TKey, T> registeredView = null;

        // ======================================================================
        /// <summary>
        /// Registry 내부 Dictionary의 수정 인터페이스를 공개하지 않는 조회 View.
        /// </summary>
        // ======================================================================
        private sealed class RegisteredReadOnlyView : IReadOnlyDictionary<TKey, T>
        {
            private readonly SpawnRegistryBase<TKey, T> owner;

            internal RegisteredReadOnlyView(SpawnRegistryBase<TKey, T> owner)
            {
                this.owner = owner;
            }

            int IReadOnlyCollection<KeyValuePair<TKey, T>>.Count => owner._Registered.Count;

            IEnumerable<TKey> IReadOnlyDictionary<TKey, T>.Keys => owner._Registered.Keys;

            IEnumerable<T> IReadOnlyDictionary<TKey, T>.Values => owner._Registered.Values;

            T IReadOnlyDictionary<TKey, T>.this[TKey key] => owner._Registered[key];

            bool IReadOnlyDictionary<TKey, T>.ContainsKey(TKey key) => owner._Registered.ContainsKey(key);

            bool IReadOnlyDictionary<TKey, T>.TryGetValue(TKey key, out T value) => owner._Registered.TryGetValue(key, out value);

            IEnumerator<KeyValuePair<TKey, T>> IEnumerable<KeyValuePair<TKey, T>>.GetEnumerator() => owner._Registered.GetEnumerator();

            IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable<KeyValuePair<TKey, T>>)this).GetEnumerator();
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 현재 Registry가 소유하는 등록 객체 사전.
        /// </summary>
        // ------------------------------------------------------------
        private RegisteredDictionary<TKey, T> _Registered
        {
            get
            {
                registered ??= new();
                return registered;
            }
        }

        [SerializeField]
        private RegisteredDictionary<TKey, T> registered = new();

        // ------------------------------------------------------------
        /// <summary>
        /// 현재 등록·해제 수명 작업이 진행 중인 객체 집합.
        /// </summary>
        // ------------------------------------------------------------
        private HashSet<T> _OperatingEntities
        {
            get
            {
                operatingEntities ??= new(ReferenceEqualityComparer<T>.Instance);
                return operatingEntities;
            }
        }

        [NonSerialized]
        private HashSet<T> operatingEntities = new(ReferenceEqualityComparer<T>.Instance);

        [NonSerialized]
        private List<ISpawnRegistryBinding<TKey, T>> bindings = new();

        [NonSerialized]
        private bool isDespawningAll = false;

        [NonSerialized]
        private bool isUpdatingBindings = false;

        // 직접 등록한 항목은 게임 Spawn/Despawn 훅 없이 관리한다.
        [SerializeField]
        private List<TKey> directEntryKeys = new();

        [NonSerialized]
        private HashSet<TKey> directEntries = new();

    #endregion

    #region 등록 수명 이벤트

        // ------------------------------------------------------------
        /// <summary>
        /// Registry 등록 직전에 호출한다.
        /// </summary>
        // ------------------------------------------------------------
        [field: NonSerialized]
        public event Action<TKey, T> OnRegistering = null;

        // ------------------------------------------------------------
        /// <summary>
        /// 등록과 필수 Binding이 완료된 후 호출한다.
        /// </summary>
        // ------------------------------------------------------------
        [field: NonSerialized]
        public event Action<TKey, T> OnRegistered = null;

        // ------------------------------------------------------------
        /// <summary>
        /// Registry 등록 해제 전에 호출한다.
        /// </summary>
        // ------------------------------------------------------------
        [field: NonSerialized]
        public event Action<TKey, T, DespawnReason> OnUnregistering = null;

        // ------------------------------------------------------------
        /// <summary>
        /// Registry membership을 제거한 후 호출한다.
        /// </summary>
        // ------------------------------------------------------------
        [field: NonSerialized]
        public event Action<TKey, T, DespawnReason> OnUnregistered = null;

    #endregion

    #region 게임 Spawn 이벤트

        // ------------------------------------------------------------
        /// <summary>
        /// 게임 Spawn 진입 후 Registry 등록 전에 호출된다.
        /// </summary>
        // ------------------------------------------------------------
        [field: NonSerialized]
        public event Action<TKey, T> OnSpawning = null;

        // ------------------------------------------------------------
        /// <summary>
        /// 게임 Spawn의 등록 및 상태 전환이 완료된 후 호출된다.
        /// </summary>
        // ------------------------------------------------------------
        [field: NonSerialized]
        public event Action<TKey, T> OnSpawned = null;

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> 완료된 게임 Spawn의 해제 전에 호출된다.
        /// <br/> 직접 등록 및 미완료 Spawn Rollback에는 호출하지 않는다.
        /// </summary>
        // ------------------------------------------------------------
        [field: NonSerialized]
        public event Action<TKey, T, DespawnReason> OnDespawning = null;

        // ------------------------------------------------------------
        /// <summary>
        /// 완료된 게임 Spawn의 해제 작업 종료 후 호출된다.
        /// </summary>
        // ------------------------------------------------------------
        [field: NonSerialized]
        public event Action<TKey, T, DespawnReason> OnDespawned = null;

    #endregion

    #region 직렬화 복원

        // ------------------------------------------------------------
        /// <summary>
        /// Registry의 현재 등록 사전을 Unity 직렬화 표현으로 동기화한다.
        /// </summary>
        // ------------------------------------------------------------
        public virtual void OnBeforeSerialize()
        {
            // SpawnRegistry는 등록 상태 영속화를 소유하므로 이 경계에서 명시적으로 적용한다.
            _Registered.ApplyRuntime();
        }

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> Unity 역직렬화 뒤 등록 사전을 우선 재구성하고,
        /// <br/> 각 등록 객체의 Registry 내부 런타임 연결만 복구한다.
        /// </summary>
        // ------------------------------------------------------------
        public virtual void OnAfterDeserialize()
        {
            registered ??= new RegisteredDictionary<TKey, T>();

            // 상위/하위 callback 호출 순서에 의존하지 않도록 사전 복원을 명시적으로 보장한다.
            registered.UpdateRuntime();

            operatingEntities = new HashSet<T>(ReferenceEqualityComparer<T>.Instance);
            bindings = new List<ISpawnRegistryBinding<TKey, T>>();
            isDespawningAll = false;
            isUpdatingBindings = false;
            directEntryKeys ??= new List<TKey>();
            directEntries = new HashSet<TKey>(directEntryKeys);

            foreach (var key in directEntries)
            {
                if (!registered.ContainsKey(key))
                {
                    throw new InvalidOperationException("복원된 직접 등록 Entry가 Registry에 존재하지 않습니다.");
                }
            }

            foreach (var (key, spawnable) in registered)
            {
                ValidateRegistrationEntry(key, spawnable);
                AttachRegistrationRuntime(spawnable);

                if (directEntries.Contains(key))
                {
                    spawnable.DespawnFromRegistry = reason => UnregisterEntry(spawnable, reason);
                }
            }
        }

    #endregion

    #region 필수 Binding 연결·해제

        // ----------------------------------------------------------------------
        /// <summary>
        /// 이미 등록된 객체에도 필수 Binding을 연결하고, 실패하면 연결을 롤백한다.
        /// </summary>
        // ----------------------------------------------------------------------
        public void AttachBinding(ISpawnRegistryBinding<TKey, T> binding)
        {
            if (binding == null)
            {
                throw new ArgumentNullException(nameof(binding));
            }

            ValidateBindingMutationAllowed();

            if (IsBindingAttached(binding))
            {
                throw new InvalidOperationException("동일 SpawnRegistry Binding을 중복 연결할 수 없습니다.");
            }

            var existing = new List<KeyValuePair<TKey, T>>(_Registered);

            var attachedCount = 0;
            isUpdatingBindings = true;

            try
            {
                try
                {
                    foreach (var (key, spawnable) in existing)
                    {
                        // 예외가 발생한 Binding에도 부분 자원이 남을 수 있으므로 정리에 포함한다.
                        attachedCount++;

                        binding.OnRegistered(key, spawnable);
                    }

                    bindings.Add(binding);
                }
                catch (Exception attachException)
                {
                    var failures = new ExceptionBuffer();

                    for (var index = attachedCount - 1; index >= 0; index--)
                    {
                        try
                        {
                            var (key, spawnable) = existing[index];

                            binding.OnUnregistering(key, spawnable, DespawnReason.SpawnRollback);
                        }
                        catch (Exception exception)
                        {
                            failures.Add(exception);
                        }
                    }

                    if (failures.Count > 0)
                    {
                        failures.Prepend(attachException);
                        throw failures.CreateAggregateException("SpawnRegistry Binding 연결 및 rollback에 실패했습니다.");
                    }

                    throw;
                }
            }
            finally
            {
                isUpdatingBindings = false;
            }
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 등록된 객체의 Binding 자원을 정리하고 연결을 해제한다.
        /// </summary>
        // ------------------------------------------------------------
        public bool DetachBinding(ISpawnRegistryBinding<TKey, T> binding)
        {
            if (binding == null)
            {
                return false;
            }

            ValidateBindingMutationAllowed();

            var bindingIndex = bindings.FindIndex(item => ReferenceEquals(item, binding));

            if (bindingIndex < 0)
            {
                return false;
            }

            var existing = new List<KeyValuePair<TKey, T>>(_Registered);
            var failures = new ExceptionBuffer();

            isUpdatingBindings = true;

            try
            {
                for (var index = existing.Count - 1; index >= 0; index--)
                {
                    try
                    {
                        var (key, spawnable) = existing[index];

                        binding.OnUnregistering(key, spawnable, DespawnReason.RegistryCleanup);
                    }
                    catch (Exception exception)
                    {
                        failures.Add(exception);
                    }
                }

                // 일부 정리가 실패해도 이후 Spawn에 해당 Binding을 다시 전달하지 않는다.
                bindings.RemoveAt(bindingIndex);
            }
            finally
            {
                isUpdatingBindings = false;
            }

            if (failures.Count > 0)
            {
                throw failures.CreateAggregateException("SpawnRegistry Binding 해제 중 일부 정리가 실패했습니다.");
            }

            return true;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 동일 Binding이 현재 Registry에 연결돼 있는지 확인한다.
        /// </summary>
        // ------------------------------------------------------------
        internal bool IsBindingAttached(ISpawnRegistryBinding<TKey, T> binding) =>
            bindings.Exists(item => ReferenceEquals(item, binding));

        // ----------------------------------------------------------------------
        /// <summary>
        /// lifecycle transaction 중에는 필수 바인딩 구성을 변경하지 못하게 막는다.
        /// </summary>
        // ----------------------------------------------------------------------
        private void ValidateBindingMutationAllowed()
        {
            if (_OperatingEntities.Count > 0 || isDespawningAll || isUpdatingBindings)
            {
                throw new InvalidOperationException
                (
                    "Registry 수명 transaction 중에는 Binding 구성을 변경할 수 없습니다."
                );
            }

            foreach (var spawnable in _Registered.Values)
            {
                if (spawnable.SpawnState == SpawnState.Despawning)
                {
                    throw new InvalidOperationException
                    (
                        "Registry 수명 transaction 중에는 Binding 구성을 변경할 수 없습니다."
                    );
                }
            }
        }

    #endregion

    #region 확장 훅

        // ------------------------------------------------------------
        /// <summary>
        /// Registry가 등록 객체의 내부 런타임 연결을 확정한 뒤 호출한다.
        /// </summary>
        // ------------------------------------------------------------
        protected virtual void OnRegistrationAttached(T spawnable)
        {
            // NONE
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Registry가 등록 소유 관계의 내부 런타임 연결을 해제할 때 호출한다.
        /// </summary>
        // ------------------------------------------------------------
        protected virtual void OnRegistrationDetached(T spawnable, DespawnReason reason)
        {
            // NONE
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> Registry의 소유가 끝난 객체를 획득 출처에 반환한다.
        /// <br/> 풀을 사용하지 않는 Registry는 기본 구현을 그대로 사용할 수 있다.
        /// </summary>
        // ----------------------------------------------------------------------
        protected virtual void Release(T spawnable, DespawnReason reason)
        {
            // NONE
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 직접 등록이 끝난 뒤 로컬 Key를 해제한다.
        /// </summary>
        // ------------------------------------------------------------
        protected virtual void OnDirectEntryRemoved(T spawnable)
        {
            // NONE
        }

    #endregion

    #region Registry 조회

        // ------------------------------------------------------------
        /// <summary>
        /// 해당 키를 가지는 객체를 찾는다.
        /// </summary>
        // ------------------------------------------------------------
        public T Find(TKey key)
        {
            return _Registered.TryGetValue(key, out var value) ? value : null;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 동일 키를 가지는 객체를 찾는다.
        /// </summary>
        // ------------------------------------------------------------
        public T Find(IKeyable<TKey> keyable)
        {
            if (keyable != null)
            {
                if (keyable.HasKey && _Registered.TryGetValue(keyable.Key, out var value))
                {
                    return value;
                }
            }

            return null;
        }

    #endregion

    #region 직접 등록·해제

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> 게임 Spawn 훅 없이 준비된 객체를 Registry에 직접 등록한다.
        /// <br/> 게임 Spawn 훅 없이 Binding을 구성한다.
        /// </summary>
        // ------------------------------------------------------------
        protected T RegisterEntry(T spawnable)
        {
            ValidateRegistrationAllowed();

            if (spawnable == null)
            {
                throw new ArgumentNullException(nameof(spawnable));
            }

            if (spawnable.SpawnState != SpawnState.Despawned || !spawnable.HasKey)
            {
                throw new InvalidOperationException("직접 등록할 객체는 Key가 있는 Despawned 상태여야 합니다.");
            }

            var key = spawnable.Key;

            if (_Registered.ContainsKey(key) || directEntries.Contains(key))
            {
                throw new InvalidOperationException("이미 해당 Key가 Registry에 등록돼 있습니다.");
            }

            if (!_OperatingEntities.Add(spawnable))
            {
                throw new InvalidOperationException("동일 객체의 등록 작업이 진행 중입니다.");
            }

            var registered = false;
            var attachedCount = 0;

            try
            {
                if
                (
                    spawnable.SpawnState != SpawnState.Despawned ||
                    !spawnable.HasKey ||
                    !EqualityComparer<TKey>.Default.Equals(spawnable.Key, key) ||
                    _Registered.ContainsKey(key)
                )
                {
                    throw new InvalidOperationException("상태 적용 중 Registry Key 또는 등록 상태가 변경됐습니다.");
                }

                // 직접 등록도 일반 Spawn과 동일한 Registry 등록 시작 알림을 제공한다.
                NotifyHandlers(OnRegistering, key, spawnable).ThrowWhenAny("직접 등록 시작 알림에 실패했습니다.");

                if
                (
                    spawnable.SpawnState != SpawnState.Despawned ||
                    !spawnable.HasKey ||
                    !EqualityComparer<TKey>.Default.Equals(spawnable.Key, key) ||
                    _Registered.ContainsKey(key)
                )
                {
                    throw new InvalidOperationException("등록 시작 알림 중 Entity의 Key 또는 수명이 변경됐습니다.");
                }

                _Registered.Add(key, spawnable);
                registered = true;
                directEntries.Add(key);
                directEntryKeys.Add(key);

                // 등록 자체는 기존 Registry와 같은 상태·Revision을 갖되 게임 Spawn은 실행하지 않는다.
                AttachRegistrationRuntime(spawnable);
                spawnable.DespawnFromRegistry = reason => UnregisterEntry(spawnable, reason);

                for (var index = 0; index < bindings.Count; index++)
                {
                    attachedCount = index + 1;
                    bindings[index].OnRegistered(key, spawnable);
                }
            }
            catch (Exception presentException)
            {
                try
                {
                    if (registered)
                    {
                        try
                        {
                            CompleteDirectEntry
                            (
                                key,
                                spawnable,
                                DespawnReason.SpawnRollback,
                                attachedCount,
                                publishRegistration: false
                            );
                        }
                        catch (Exception rollbackException)
                        {
                            throw new AggregateException
                            (
                                "직접 등록과 rollback이 모두 실패했습니다.",
                                presentException,
                                rollbackException
                            );
                        }
                    }

                    throw;
                }
                finally
                {
                    _OperatingEntities.Remove(spawnable);
                }
            }

            try
            {
                // 완료 알림 예외는 확정된 등록을 Rollback하지 않는다.
                NotifyHandlers(OnRegistered, key, spawnable).ThrowWhenAny("직접 등록 완료 알림에 실패했습니다.");

                return spawnable;
            }
            finally
            {
                _OperatingEntities.Remove(spawnable);
            }
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 직접 등록한 Key인지 조회한다.
        /// </summary>
        // ------------------------------------------------------------
        protected bool IsDirectEntry(TKey key) =>
            directEntries.Contains(key) && _Registered.ContainsKey(key);

        // ------------------------------------------------------------
        /// <summary>
        /// 직접 등록한 Entry를 게임 Despawn 훅 없이 Registry에서 제거한다.
        /// </summary>
        // ------------------------------------------------------------
        protected bool UnregisterEntry
        (
            T spawnable,
            DespawnReason reason
        )
        {
            if (spawnable == null)
            {
                return false;
            }

            SpawnRegistryUtility.ValidateReason(reason);

            if (isUpdatingBindings || _OperatingEntities.Contains(spawnable))
            {
                throw new InvalidOperationException("Registry 수명 작업 또는 Binding 동기화 중에는 직접 등록을 해제할 수 없습니다.");
            }

            if (!spawnable.HasKey || !directEntries.Contains(spawnable.Key))
            {
                // Authority의 일반 Spawn 항목을 이 경로로 삭제하는 것은 허용하지 않는다.
                return false;
            }

            var key = spawnable.Key;

            if
            (
                spawnable.SpawnState != SpawnState.Spawned ||
                !_Registered.TryGetValue(key, out var registeredEntry) ||
                !ReferenceEquals(registeredEntry, spawnable)
            )
            {
                throw new InvalidOperationException("현재 Registry에 등록된 같은 수명만 제거할 수 있습니다.");
            }

            if (!_OperatingEntities.Add(spawnable))
            {
                throw new InvalidOperationException("동일 객체의 수명 작업이 진행 중입니다.");
            }

            try
            {
                CompleteDirectEntry(key, spawnable, reason, bindings.Count);
                return true;
            }
            finally
            {
                _OperatingEntities.Remove(spawnable);
            }
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// Binding과 내부 소유 관계를 끝까지 정리하고 게임 훅 없이 수명을 종료한다.
        /// </summary>
        // ----------------------------------------------------------------------
        private void CompleteDirectEntry
        (
            TKey key,
            T spawnable,
            DespawnReason reason,
            int bindingCount,
            bool publishRegistration = true
        )
        {
            spawnable.SpawnState = SpawnState.Despawning;
            var failures = new ExceptionBuffer();

            if (publishRegistration)
            {
                failures = NotifyHandlers(OnUnregistering, key, spawnable, reason);
            }

            for (var index = Math.Min(bindingCount, bindings.Count) - 1; index >= 0; index--)
            {
                try
                {
                    bindings[index].OnUnregistering(key, spawnable, reason);
                }
                catch (Exception exception)
                {
                    failures.Add(exception);
                }
            }

            try
            {
                DetachRegistrationRuntime(spawnable, reason);
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }

            // 훅 예외와 무관하게 Registry membership과 Local Key를 terminal 종료한다.
            _Registered.Remove(key);
            directEntries.Remove(key);
            directEntryKeys.Remove(key);

            try
            {
                OnDirectEntryRemoved(spawnable);
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
            finally
            {
                spawnable.DespawnFromRegistry = null;
                spawnable.SpawnState = SpawnState.Despawned;
            }

            if (publishRegistration)
            {
                failures = NotifyHandlers(OnUnregistered, key, spawnable, reason, failures);
            }

            failures.ThrowWhenAny("직접 등록 해제 중 일부 정리가 실패했습니다.");
        }

    #endregion

    #region 등록 런타임 연결·검증

        // ----------------------------------------------------------------------
        /// <summary>
        /// 직렬화된 Registry 항목의 Key와 객체 Key가 같은 등록 관계인지 검증한다.
        /// </summary>
        // ----------------------------------------------------------------------
        private static void ValidateRegistrationEntry(TKey key, T spawnable)
        {
            if (spawnable == null)
            {
                throw new InvalidOperationException("직렬화된 SpawnRegistry에 null 객체가 등록되어 있습니다.");
            }

            if (!spawnable.HasKey)
            {
                throw new InvalidOperationException("직렬화된 SpawnRegistry 객체에 Key가 설정되어 있지 않습니다.");
            }

            if (!EqualityComparer<TKey>.Default.Equals(key, spawnable.Key))
            {
                throw new InvalidOperationException
                (
                    $"직렬화된 Registry Key({key})와 객체 Key({spawnable.Key})가 일치하지 않습니다."
                );
            }
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 등록된 객체의 Spawned 상태, 디스폰 콜백과 파생 내부 연결을 구성한다.
        /// </summary>
        // ------------------------------------------------------------
        private void AttachRegistrationRuntime(T spawnable)
        {
            spawnable.SpawnState = SpawnState.Spawned;
            spawnable.DespawnFromRegistry = reason => Despawn(spawnable, reason);
            OnRegistrationAttached(spawnable);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 등록 객체의 파생 내부 연결과 디스폰 콜백을 해제한다.
        /// </summary>
        // ------------------------------------------------------------
        private void DetachRegistrationRuntime(T spawnable, DespawnReason reason)
        {
            try
            {
                OnRegistrationDetached(spawnable, reason);
            }
            finally
            {
                spawnable.DespawnFromRegistry = null;
            }
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 등록·Spawn 공개 이벤트의 구독자를 모두 실행하고 실패를 수집한다.
        /// </summary>
        // ------------------------------------------------------------
        private static ExceptionBuffer NotifyHandlers
        (
            Action<TKey, T> handlers,
            TKey key,
            T spawnable,
            ExceptionBuffer failures = default
        )
        {
            if (handlers == null)
            {
                return failures;
            }

            foreach (Action<TKey, T> handler in handlers.GetInvocationList())
            {
                try
                {
                    handler(key, spawnable);
                }
                catch (Exception exception)
                {
                    failures.Add(exception);
                }
            }

            return failures;
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// 등록 해제·Despawn 공개 이벤트의 구독자를 모두 실행하고 실패를 수집한다.
        /// </summary>
        // ----------------------------------------------------------------------
        private static ExceptionBuffer NotifyHandlers
        (
            Action<TKey, T, DespawnReason> handlers,
            TKey key,
            T spawnable,
            DespawnReason reason,
            ExceptionBuffer failures = default
        )
        {
            if (handlers == null)
            {
                return failures;
            }

            foreach (Action<TKey, T, DespawnReason> handler in handlers.GetInvocationList())
            {
                try
                {
                    handler(key, spawnable, reason);
                }
                catch (Exception exception)
                {
                    failures.Add(exception);
                }
            }

            return failures;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Spawn 또는 직접 등록 수명을 시작할 수 있는지 검사한다.
        /// </summary>
        // ------------------------------------------------------------
        protected void ValidateRegistrationAllowed(T spawnable = null)
        {
            if (isDespawningAll || isUpdatingBindings)
            {
                throw new InvalidOperationException("Registry 일괄 종료 또는 Binding 동기화 중에는 객체를 등록할 수 없습니다.");
            }

            if (spawnable != null && _OperatingEntities.Contains(spawnable))
            {
                throw new InvalidOperationException("동일 객체의 등록 또는 해제 수명이 진행 중입니다.");
            }
        }

    #endregion

    #region 게임 Spawn

        // ----------------------------------------------------------------------
        /// <summary>
        /// 기존 Key와 논리 상태를 유지한 객체를 현재 Registry Spawn 수명에 편입한다.
        /// </summary>
        // ----------------------------------------------------------------------
        public T SpawnExisting(T spawnable)
        {
            if (spawnable == null)
            {
                throw new ArgumentNullException(nameof(spawnable));
            }

            ValidateRegistrationAllowed(spawnable);

            if (!spawnable.HasKey)
            {
                throw new InvalidOperationException
                (
                    "기존 객체를 Spawn하려면 미리 확정된 Key가 필요합니다."
                );
            }

            if (_Registered.ContainsKey(spawnable.Key))
            {
                throw new InvalidOperationException
                (
                    $"이미 동일 키({spawnable.Key})가 등록되어 있습니다."
                );
            }

            return Spawn(spawnable);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 공통 스폰 등록 로직을 수행한다.
        /// </summary>
        // ------------------------------------------------------------
        protected T Spawn(T spawnable, Action<T> initAction = null)
        {
            ValidateRegistrationAllowed();

            if (spawnable == null)
            {
                throw new InvalidOperationException("스폰할 객체를 가져올 수 없습니다.");
            }

            if (spawnable.SpawnState != SpawnState.Despawned)
            {
                throw new InvalidOperationException
                (
                    $"Despawned 상태의 객체만 스폰할 수 있습니다. 현재 상태: {spawnable.SpawnState}"
                );
            }

            var spawnKey = default(TKey);
            var wasRegistered = false;
            var attachedBindingCount = 0;

            // Spawn의 모든 훅과 완료 알림이 끝날 때까지 동일 객체의 재진입을 차단한다.
            if (!_OperatingEntities.Add(spawnable))
            {
                throw new InvalidOperationException("동일 객체의 등록 작업이 진행 중입니다.");
            }

            try
            {
                spawnable.SpawnState = SpawnState.Spawning;

                // 파라미터 초기화를 끝낸 뒤 객체와 Registry의 Spawning 알림을 순서대로 호출한다.
                initAction?.Invoke(spawnable);
                spawnable.OnSpawning();

                if (!spawnable.HasKey)
                {
                    throw new InvalidOperationException("스폰할 객체에 키가 설정되어 있지 않습니다.");
                }

                var spawningKey = spawnable.Key;

                if (_Registered.ContainsKey(spawningKey))
                {
                    throw new InvalidOperationException($"이미 동일 키({spawningKey})가 등록되어 있습니다.");
                }

                // 외부 관찰자는 등록 직전의 확정된 Key와 Spawning 상태를 받는다.
                spawnKey = spawningKey;
                NotifyHandlers(OnSpawning, spawningKey, spawnable).ThrowWhenAny("게임 Spawn 시작 알림에 실패했습니다.");
                NotifyHandlers(OnRegistering, spawningKey, spawnable).ThrowWhenAny("Registry 등록 시작 알림에 실패했습니다.");

                if (!spawnable.HasKey || !EqualityComparer<TKey>.Default.Equals(spawnable.Key, spawningKey))
                {
                    throw new InvalidOperationException
                    (
                        "Spawning 이벤트 중에는 Registry가 확정한 key를 변경할 수 없습니다."
                    );
                }

                // Spawning 구독자가 같은 Key의 다른 객체를 등록했는지 최종 확인한다.
                if (_Registered.ContainsKey(spawnKey))
                {
                    throw new InvalidOperationException($"이미 동일 키({spawnKey})가 등록되어 있습니다.");
                }

                // Required binding과 완료 훅은 Registry에서 자신을 조회할 수 있는 객체를 받는다.
                _Registered.Add(spawnKey, spawnable);
                wasRegistered = true;
                AttachRegistrationRuntime(spawnable);

                for (var index = 0; index < bindings.Count; index++)
                {
                    // 실패한 binding도 부분 작업을 남길 수 있으므로 rollback 대상에 포함한다.
                    attachedBindingCount = index + 1;
                    bindings[index].OnRegistered(spawnKey, spawnable);
                }

                // 필수 binding이 모두 확정된 뒤 Entity 완료 훅을 호출한다.
                spawnable.OnSpawned();
            }
            catch (Exception spawnException)
            {
                try
                {
                    // 실패 정리를 마칠 때까지 동일 객체의 등록·해제 재진입을 차단한다.
                    if (wasRegistered)
                    {
                        Despawn
                        (
                            spawnable,
                            DespawnReason.SpawnRollback,
                            attachedBindingCount,
                            publishLifecycle: false
                        );
                    }
                    else
                    {
                        CleanupUnregisteredSpawn
                        (
                            spawnable,
                            DespawnReason.SpawnRollback,
                            spawnKey
                        );
                    }
                }
                catch (Exception rollbackException)
                {
                    // 정리 실패가 최초 Spawn 오류를 가리지 않도록 두 원인을 함께 전파한다.
                    throw new AggregateException
                    (
                        "SpawnRegistry Spawn과 rollback이 모두 실패했습니다.",
                        spawnException,
                        rollbackException
                    );
                }
                finally
                {
                    _OperatingEntities.Remove(spawnable);
                }

                throw;
            }

            // 두 완료 알림이 끝날 때까지 같은 객체의 제거·일괄 제거를 막는다.
            // Commit 이후 알림 실패는 완료된 Spawn을 되돌리지 않는다.
            var notificationFailures = new ExceptionBuffer();

            try
            {
                notificationFailures = NotifyHandlers(OnRegistered, spawnKey, spawnable);
                notificationFailures = NotifyHandlers
                (
                    OnSpawned,
                    spawnKey,
                    spawnable,
                    notificationFailures
                );
            }
            finally
            {
                _OperatingEntities.Remove(spawnable);
            }

            notificationFailures.ThrowWhenAny("Registry 등록 및 Spawn 완료 알림에 실패했습니다.");

            return spawnable;
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// 등록되지 못한 객체의 내부 정리 훅을 수행한다. 완료 알림은 발행하지 않는다.
        /// </summary>
        // ----------------------------------------------------------------------
        private void CleanupUnregisteredSpawn
        (
            T spawnable,
            DespawnReason reason,
            TKey spawnKey
        )
        {
            spawnable.SpawnState = SpawnState.Despawning;
            var failures = new ExceptionBuffer();

            try
            {
                InvokeDespawning
                (
                    spawnKey,
                    spawnable,
                    reason,
                    bindingCount: 0,
                    publishRegistry: false
                );
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }

            spawnable.DespawnFromRegistry = null;
            CompleteDespawn
            (
                spawnKey, spawnable, reason,
                publishRegistry: false,
                publishRegistration: false,
                failures: failures
            );
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> 객체 훅과 필수 바인딩을 정리하고 일반 Despawning handler까지 끝까지
        /// <br/> 호출한다.
        /// </summary>
        // ----------------------------------------------------------------------
        private void InvokeDespawning
        (
            TKey key,
            T despawnable,
            DespawnReason reason,
            int bindingCount,
            bool publishRegistry = true,
            bool detachRegistration = false
        )
        {
            var failures = new ExceptionBuffer();

            try
            {
                despawnable.OnDespawning(reason);
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }

            if (publishRegistry)
            {
                failures = NotifyHandlers(OnDespawning, key, despawnable, reason, failures);
            }

            if (detachRegistration && publishRegistry)
            {
                failures = NotifyHandlers(OnUnregistering, key, despawnable, reason, failures);
            }

            var count = Math.Min(bindingCount, bindings.Count);

            for (var index = count - 1; index >= 0; index--)
            {
                try
                {
                    bindings[index].OnUnregistering(key, despawnable, reason);
                }
                catch (Exception exception)
                {
                    failures.Add(exception);
                }
            }

            if (detachRegistration)
            {
                try
                {
                    DetachRegistrationRuntime(despawnable, reason);
                }
                catch (Exception exception)
                {
                    failures.Add(exception);
                }
            }

            failures.ThrowWhenAny("SpawnRegistry Despawning transaction 중 일부 정리가 실패했습니다.");
        }

    #endregion

    #region 게임 Despawn 및 전체 정리

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> 종료 훅·완료 알림·획득 출처 반환을 끝까지 수행한다.
        /// <br/> 앞 단계에서 발생한 오류까지 보존해 terminal 정리 결과를 전달한다.
        /// </summary>
        // ----------------------------------------------------------------------
        private void CompleteDespawn
        (
            TKey key,
            T spawnable,
            DespawnReason reason,
            bool publishRegistry,
            bool publishRegistration,
            ExceptionBuffer failures
        )
        {
            try
            {
                spawnable.OnDespawned(reason);
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
            finally
            {
                // 완료 훅이 종료된 뒤에만 같은 객체의 새 Spawn 수명을 허용한다.
                spawnable.SpawnState = SpawnState.Despawned;
            }

            if (publishRegistration)
            {
                failures = NotifyHandlers(OnUnregistered, key, spawnable, reason, failures);
            }

            if (publishRegistry)
            {
                failures = NotifyHandlers(OnDespawned, key, spawnable, reason, failures);
            }

            try
            {
                Release(spawnable, reason);
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }

            failures.ThrowWhenAny("SpawnRegistry 종료 중 여러 단계가 실패했습니다.");
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 공통 디스폰 로직을 수행한다.
        /// </summary>
        // ------------------------------------------------------------
        protected void Despawn
        (
            T despawnable,
            DespawnReason reason
        )
        {
            Despawn(despawnable, reason, bindings.Count);
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// 지정 수의 필수 binding만 rollback 대상으로 포함해 객체를 디스폰한다.
        /// </summary>
        // ----------------------------------------------------------------------
        private void Despawn
        (
            T despawnable,
            DespawnReason reason,
            int bindingCount,
            bool publishLifecycle = true
        )
        {
            if (despawnable == null)
            {
                throw new ArgumentNullException(nameof(despawnable));
            }

            SpawnRegistryUtility.ValidateReason(reason);

            // 직접 등록된 Entry는 게임 Despawn 훅 없이 등록 관계만 종료한다.
            if (despawnable.HasKey && directEntries.Contains(despawnable.Key))
            {
                UnregisterEntry(despawnable, reason);
                return;
            }

            if (isUpdatingBindings)
            {
                throw new InvalidOperationException("Binding 동기화 중에는 객체를 디스폰할 수 없습니다.");
            }

            if (publishLifecycle && _OperatingEntities.Contains(despawnable))
            {
                throw new InvalidOperationException
                (
                    "동일 객체의 등록 또는 해제 작업 중에는 디스폰할 수 없습니다."
                );
            }

            if (despawnable.SpawnState != SpawnState.Spawned)
            {
                throw new InvalidOperationException
                (
                    $"Spawned 상태의 객체만 디스폰할 수 있습니다. 현재 상태: {despawnable.SpawnState}"
                );
            }

            if (!despawnable.HasKey)
            {
                throw new InvalidOperationException("디스폰할 객체에 키가 설정되어 있지 않습니다.");
            }

            if
            (
                !_Registered.TryGetValue(despawnable.Key, out var registered) ||
                !ReferenceEquals(registered, despawnable)
            )
            {
                throw new KeyNotFoundException
                (
                    $"등록된 동일 객체가 아닌 키({despawnable.Key})로 디스폰할 수 없습니다."
                );
            }

            var key = despawnable.Key;

            if (publishLifecycle)
            {
                if (!_OperatingEntities.Add(despawnable))
                {
                    throw new InvalidOperationException("동일 객체의 수명 작업이 진행 중입니다.");
                }
            }
            else if (!_OperatingEntities.Contains(despawnable))
            {
                throw new InvalidOperationException("내부 Spawn Rollback에 등록 작업 보호가 없습니다.");
            }

            try
            {
                despawnable.SpawnState = SpawnState.Despawning;
                var failures = new ExceptionBuffer();

                try
                {
                    // 사전 훅 실행 중에는 Registry의 Key와 내부 연결을 유지한다.
                    InvokeDespawning
                    (
                        key,
                        despawnable,
                        reason,
                        bindingCount,
                        publishRegistry: publishLifecycle,
                        detachRegistration: true
                    );
                }
                catch (Exception exception)
                {
                    failures.Add(exception);
                }

                // 사전 훅이 실패해도 membership과 객체 수명을 terminal 정리한다.
                try
                {
                    _Registered.Remove(key);
                }
                catch (Exception exception)
                {
                    failures.Add(exception);
                }

                CompleteDespawn
                (
                    key, despawnable, reason,
                    publishRegistry: publishLifecycle,
                    publishRegistration: publishLifecycle,
                    failures: failures
                );
            }
            finally
            {
                if (publishLifecycle)
                {
                    _OperatingEntities.Remove(despawnable);
                }
            }
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 모든 객체를 디스폰한다.
        /// </summary>
        // ------------------------------------------------------------
        public void DespawnAll()
        {
            DespawnAll(DespawnReason.RegistryCleanup);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 지정한 사유로 모든 객체를 디스폰한다.
        /// </summary>
        // ------------------------------------------------------------
        public void DespawnAll(DespawnReason reason)
        {
            SpawnRegistryUtility.ValidateReason(reason);

            if (isUpdatingBindings)
            {
                throw new InvalidOperationException("Binding 동기화 중에는 일괄 디스폰할 수 없습니다.");
            }

            // 진행 중인 Spawn이 이후 등록을 재개하지 못하도록 전체 처리 구간에서 일괄 제거를 차단한다.
            if (_OperatingEntities.Count > 0)
            {
                throw new InvalidOperationException
                (
                    "Spawn 처리를 진행하는 동안 DespawnAll을 실행할 수 없습니다."
                );
            }

            if (isDespawningAll)
            {
                throw new InvalidOperationException("DespawnAll 처리가 이미 진행 중입니다.");
            }

            // 개별 Despawn의 사전 훅에서 호출되면 현재 객체를 남긴 채 반환하므로 일괄 제거를 시작하지 않는다.
            foreach (var entity in _Registered.Values)
            {
                if (entity.SpawnState == SpawnState.Despawning)
                {
                    throw new InvalidOperationException
                    (
                        "개별 Despawn 처리를 진행하는 동안 DespawnAll을 실행할 수 없습니다."
                    );
                }
            }

            var snapshot = new List<KeyValuePair<TKey, T>>(_Registered);
            var failures = new ExceptionBuffer();

            isDespawningAll = true;

            try
            {
                foreach (var (key, entity) in snapshot)
                {
                    // 앞선 구독자가 이미 처리한 항목은 중복 디스폰하지 않는다.
                    if
                    (
                        !_Registered.TryGetValue(key, out var registeredEntry) ||
                        !ReferenceEquals(registeredEntry, entity) ||
                        entity.SpawnState != SpawnState.Spawned
                    )
                    {
                        continue;
                    }

                    try
                    {
                        // 항목별 제거를 완료한 뒤 OnDespawned를 발행해 완료 이벤트 계약을 유지한다.
                        Despawn(entity, reason);
                    }
                    catch (Exception exception)
                    {
                        // 일부 디스폰 실패해도 나머지는 계속 처리한다.
                        failures.Add(exception);
                    }
                }
            }
            finally
            {
                isDespawningAll = false;
            }

            if (failures.Count > 0)
            {
                throw failures.CreateAggregateException("일부 객체를 디스폰하지 못했습니다.");
            }
        }

    #endregion

    }

    // ============================================================
    /// <summary>
    /// 파라미터 없는 Acquire/Spawn 을 제공하는 스폰 레지스트리.
    /// </summary>
    // ============================================================
    [Serializable]
    public abstract class SpawnRegistry<TKey, T> : SpawnRegistryBase<TKey, T>
    where TKey : IEquatable<TKey>
    where T : class, ISpawnRegistryObject<TKey>
    {

    #region 추상 메서드

        // ------------------------------------------------------------
        /// <summary>
        /// 새 객체를 풀·생성 등으로 가져온다. 자식이 구현.
        /// </summary>
        // ------------------------------------------------------------
        protected abstract T Acquire();

    #endregion

    #region 스폰

        // ------------------------------------------------------------
        /// <summary>
        /// 객체를 스폰한다.
        /// </summary>
        // ------------------------------------------------------------
        protected T Spawn()
        {
            ValidateRegistrationAllowed();
            var spawnable = Acquire();

            if (spawnable == null)
            {
                return null;
            }

            return Spawn(spawnable);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> 객체를 스폰한다(외부 호출용).
        /// <br/> true는 스폰 절차와 완료 알림을 수행했음을 뜻한다.
        /// </summary>
        // ------------------------------------------------------------
        public virtual bool TrySpawn(out T registered)
        {
            registered = Spawn();

            return registered != null;
        }

    #endregion

    }

    // ============================================================
    /// <summary>
    /// 파라미터를 받는 Acquire/Spawn 을 제공하는 스폰 레지스트리.
    /// </summary>
    // ============================================================
    [Serializable]
    public abstract class SpawnRegistry<TKey, T, TParam> : SpawnRegistryBase<TKey, T>
    where TKey : IEquatable<TKey>
    where T : class, ISpawnRegistryObject<TKey>, INeedToInit<TParam>
    {

    #region 파생 훅

        // ------------------------------------------------------------
        /// <summary>
        /// 파생 클래스가 Init 전에 수행할 작업을 정의하는 훅.
        /// </summary>
        // ------------------------------------------------------------
        protected virtual void OnInit(T spawnable, TParam param)
        {
            // NONE
        }

    #endregion

    #region 추상 메서드

        // ------------------------------------------------------------
        /// <summary>
        /// 새 객체를 가져온다. 자식이 구현.
        /// </summary>
        // ------------------------------------------------------------
        protected abstract T Acquire(TParam param);

    #endregion

    #region 스폰

        // ------------------------------------------------------------
        /// <summary>
        /// 객체를 스폰한다.
        /// </summary>
        // ------------------------------------------------------------
        protected T Spawn(TParam param)
        {
            ValidateRegistrationAllowed();
            var spawnable = Acquire(param);

            if (spawnable == null)
            {
                return null;
            }

            void InitAction(T s)
            {
                OnInit(s, param);
                s.Init(param);
            }

            return Spawn(spawnable, InitAction);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> 객체를 스폰한다(외부 호출용).
        /// <br/> true는 스폰 절차와 완료 알림을 수행했음을 뜻한다.
        /// </summary>
        // ------------------------------------------------------------
        public virtual bool TrySpawn(TParam param, out T registered)
        {
            registered = Spawn(param);

            return registered != null;
        }

    #endregion

    }
}
