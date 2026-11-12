/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : SpawnRegistry.cs
수정일 : 2026-09-21

# 설명
스폰 레지스트리 베이스·구현체와 스폰된 객체 사전, 디스폰 확장 메서드 정의.

- SpawnRegistryUtility       : IDespawnable.Despawn과 Despawn(reason) 확장 메서드
- SpawnedDictionary<,>        : XDictionary_VR 기반 스폰 사전
- SpawnRegistryBase<,>        : 핵심 스폰/디스폰/검색/0..N 필수 바인딩/이벤트 로직
- SpawnRegistry<,>            : 파라미터 없는 Acquire/Spawn
- SpawnRegistry<,,>           : 파라미터 받는 Acquire/Spawn (INeedToInit<TParam> 연동)

등록 사전은 직렬화하며, 진행 중 Spawn·외부 바인딩·전체 디스폰 플래그와 공개 이벤트는 비직렬화 런타임 상태다.
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
    /// 스폰된 객체를 보관하는 직렬화 가능한 사전.
    /// </summary>
    // ============================================================
    [Serializable]
    public class SpawnedDictionary<TKey, T> : XDictionary_VR<TKey, T>, ISpawnedDictionary<TKey, T>
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
        /// 현재 스폰된 객체들의 사전(읽기 전용 노출).
        /// </summary>
        // ------------------------------------------------------------
        public ISpawnedDictionary<TKey, T> Spawned => _Spawned;

        // ------------------------------------------------------------
        /// <summary>
        /// 현재 Registry가 소유하는 스폰 객체 사전.
        /// </summary>
        // ------------------------------------------------------------
        private SpawnedDictionary<TKey, T> _Spawned
        {
            get
            {
                spawned ??= new();
                return spawned;
            }
        }

        [SerializeField]
        private SpawnedDictionary<TKey, T> spawned = new();

        // ------------------------------------------------------------
        /// <summary>
        /// 현재 Spawn 처리를 진행 중인 객체 집합.
        /// </summary>
        // ------------------------------------------------------------
        private HashSet<T> _Spawning
        {
            get
            {
                spawning ??= new(ReferenceEqualityComparer<T>.Instance);
                return spawning;
            }
        }

        [NonSerialized]
        private HashSet<T> spawning = new(ReferenceEqualityComparer<T>.Instance);

        [NonSerialized]
        private List<ISpawnRegistryBinding<TKey, T>> bindings = new();

        [NonSerialized]
        private bool isDespawningAll = false;

    #endregion

    #region 이벤트

        // ------------------------------------------------------------
        /// <summary>
        /// 객체가 Spawning 상태로 진입한 뒤 Registry 등록 전에 호출된다.
        /// </summary>
        // ------------------------------------------------------------
        [field: NonSerialized]
        public event Action<TKey, T> OnSpawning = null;

        // ------------------------------------------------------------
        /// <summary>
        /// 객체의 Registry 등록과 Spawned 상태 전환이 완료된 뒤 호출된다.
        /// </summary>
        // ------------------------------------------------------------
        [field: NonSerialized]
        public event Action<TKey, T> OnSpawned = null;

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> 객체가 등록 해제 또는 Spawn 실패 정리를 시작하기 전에 호출된다.
        /// <br/> Spawn 실패 정리에서는 객체가 Registry에 등록되지 않았을 수 있다.
        /// </summary>
        // ----------------------------------------------------------------------
        [field: NonSerialized]
        public event Action<TKey, T, DespawnReason> OnDespawning = null;

        // ----------------------------------------------------------------------
        /// <summary>
        /// 객체의 등록 해제 또는 Spawn 실패 정리와 상태 전환이 완료된 뒤 호출된다.
        /// </summary>
        // ----------------------------------------------------------------------
        [field: NonSerialized]
        public event Action<TKey, T, DespawnReason> OnDespawned = null;

    #endregion

    #region 직렬화

        // ------------------------------------------------------------
        /// <summary>
        /// Registry의 현재 Spawned 사전을 Unity 직렬화 표현으로 동기화한다.
        /// </summary>
        // ------------------------------------------------------------
        public virtual void OnBeforeSerialize()
        {
            // SpawnRegistry는 등록 상태 영속화를 소유하므로 이 경계에서 명시적으로 적용한다.
            _Spawned.ApplyRuntime();
        }

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> Unity 역직렬화 뒤 Spawned 사전을 우선 재구성하고,
        /// <br/> 각 등록 객체의 Registry 내부 런타임 연결만 복구한다.
        /// </summary>
        // ------------------------------------------------------------
        public virtual void OnAfterDeserialize()
        {
            spawned ??= new SpawnedDictionary<TKey, T>();

            // 상위/하위 callback 호출 순서에 의존하지 않도록 사전 복원을 명시적으로 보장한다.
            spawned.UpdateRuntime();

            spawning = new HashSet<T>(ReferenceEqualityComparer<T>.Instance);
            bindings = new List<ISpawnRegistryBinding<TKey, T>>();
            isDespawningAll = false;

            foreach (var (key, spawnable) in spawned)
            {
                ValidateRegistrationEntry(key, spawnable);
                AttachRegistrationRuntime(spawnable);
            }
        }

    #endregion

    #region 바인딩

        // ------------------------------------------------------------
        /// <summary>
        /// 필수 lifecycle 바인딩을 실행 순서의 마지막에 연결한다.
        /// </summary>
        // ------------------------------------------------------------
        public void AttachBinding(ISpawnRegistryBinding<TKey, T> binding)
        {
            if (binding == null)
            {
                throw new ArgumentNullException(nameof(binding));
            }

            ValidateBindingMutationAllowed();

            for (var index = 0; index < bindings.Count; index++)
            {
                if (ReferenceEquals(bindings[index], binding))
                {
                    throw new InvalidOperationException("동일 SpawnRegistry Binding을 중복 연결할 수 없습니다.");
                }
            }

            bindings.Add(binding);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 현재 연결된 동일 lifecycle 바인딩을 해제한다.
        /// </summary>
        // ------------------------------------------------------------
        public bool DetachBinding(ISpawnRegistryBinding<TKey, T> binding)
        {
            if (binding == null)
            {
                return false;
            }

            ValidateBindingMutationAllowed();

            for (var index = bindings.Count - 1; index >= 0; index--)
            {
                if (!ReferenceEquals(bindings[index], binding))
                {
                    continue;
                }

                bindings.RemoveAt(index);
                return true;
            }

            return false;
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// lifecycle transaction 중에는 필수 바인딩 구성을 변경하지 못하게 막는다.
        /// </summary>
        // ----------------------------------------------------------------------
        private void ValidateBindingMutationAllowed()
        {
            if (_Spawning.Count > 0 || isDespawningAll)
            {
                throw new InvalidOperationException
                (
                    "Spawn/Despawn transaction 중에는 SpawnRegistry Binding 구성을 변경할 수 없습니다."
                );
            }

            foreach (var spawnable in _Spawned.Values)
            {
                if (spawnable.SpawnState == SpawnState.Despawning)
                {
                    throw new InvalidOperationException
                    (
                        "Spawn/Despawn transaction 중에는 SpawnRegistry Binding 구성을 변경할 수 없습니다."
                    );
                }
            }
        }

    #endregion

    #region 파생 훅

        // ------------------------------------------------------------
        /// <summary>
        /// Registry가 Spawned 객체의 내부 런타임 연결을 확정한 뒤 호출한다.
        /// </summary>
        // ------------------------------------------------------------
        protected virtual void OnRegistrationAttached(T spawnable)
        {
            // NONE
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// Registry가 Spawned 소유 관계의 내부 런타임 연결을 해제할 때 호출한다.
        /// </summary>
        // ----------------------------------------------------------------------
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

    #endregion

    #region 검색 메서드

        // ------------------------------------------------------------
        /// <summary>
        /// 해당 키를 가지는 객체를 찾는다.
        /// </summary>
        // ------------------------------------------------------------
        public T Find(TKey key)
        {
            return _Spawned.TryGetValue(key, out var value) ? value : null;
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
                if (keyable.HasKey && _Spawned.TryGetValue(keyable.Key, out var value))
                {
                    return value;
                }
            }

            return null;
        }

    #endregion

    #region 등록 런타임 연결

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

    #endregion

    #region 스폰

        // ----------------------------------------------------------------------
        /// <summary>
        /// 새 객체를 획득하기 전에 Registry가 Spawn을 시작할 수 있는지 확인한다.
        /// </summary>
        // ----------------------------------------------------------------------
        protected void ValidateSpawnAllowed()
        {
            if (isDespawningAll)
            {
                throw new InvalidOperationException("DespawnAll 처리 중에는 새 객체를 스폰할 수 없습니다.");
            }
        }

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

            if (!spawnable.HasKey)
            {
                throw new InvalidOperationException
                (
                    "기존 객체를 Spawn하려면 미리 확정된 Key가 필요합니다."
                );
            }

            if (_Spawned.ContainsKey(spawnable.Key))
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
            ValidateSpawnAllowed();

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
            var didBeginSpawning = false;
            var wasRegistered = false;
            var attachedBindingCount = 0;

            // Spawn의 모든 훅과 이벤트가 끝날 때까지 일괄 디스폰과 개별 디스폰의 진입을 차단한다.
            _Spawning.Add(spawnable);

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

                if (_Spawned.ContainsKey(spawningKey))
                {
                    throw new InvalidOperationException($"이미 동일 키({spawningKey})가 등록되어 있습니다.");
                }

                // 외부 관찰자는 등록 직전의 확정된 Key와 Spawning 상태를 받는다.
                spawnKey = spawningKey;
                didBeginSpawning = true;
                OnSpawning?.Invoke(spawningKey, spawnable);

                if (!spawnable.HasKey || !EqualityComparer<TKey>.Default.Equals(spawnable.Key, spawningKey))
                {
                    throw new InvalidOperationException
                    (
                        "Spawning 이벤트 중에는 Registry가 확정한 key를 변경할 수 없습니다."
                    );
                }

                // Spawning 구독자가 같은 Key의 다른 객체를 등록했는지 최종 확인한다.
                if (_Spawned.ContainsKey(spawnKey))
                {
                    throw new InvalidOperationException($"이미 동일 키({spawnKey})가 등록되어 있습니다.");
                }

                // Required binding과 완료 훅은 Registry에서 자신을 조회할 수 있는 객체를 받는다.
                _Spawned.Add(spawnKey, spawnable);
                wasRegistered = true;
                AttachRegistrationRuntime(spawnable);

                for (var index = 0; index < bindings.Count; index++)
                {
                    // 실패한 binding도 부분 작업을 남길 수 있으므로 rollback 대상에 포함한다.
                    attachedBindingCount = index + 1;
                    bindings[index].OnSpawned(spawnKey, spawnable);
                }

                // 필수 binding이 모두 확정된 뒤 Entity 완료 훅을 호출한다.
                spawnable.OnSpawned();
            }
            catch
            {
                _Spawning.Remove(spawnable);

                // OnSpawning 발행을 시작한 객체는 공개 실패 정리 알림을 시도한다.
                if (wasRegistered)
                {
                    Despawn
                    (
                        spawnable,
                        DespawnReason.SpawnRollback,
                        attachedBindingCount
                    );
                }
                else
                {
                    CleanupUnregisteredSpawn
                    (
                        spawnable,
                        DespawnReason.SpawnRollback,
                        didBeginSpawning,
                        spawnKey
                    );
                }

                throw;
            }

            _Spawning.Remove(spawnable);

            // 일반 observer는 commit 이후에 알리며 실패해도 완료된 Spawn을 rollback하지 않는다.
            OnSpawned?.Invoke(spawnKey, spawnable);

            return spawnable;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Registry 등록 전 실패한 객체의 정리 훅과 공개 알림을 수행한다.
        /// </summary>
        // ------------------------------------------------------------
        private void CleanupUnregisteredSpawn
        (
            T spawnable,
            DespawnReason reason,
            bool didBeginSpawning,
            TKey spawnKey
        )
        {
            spawnable.SpawnState = SpawnState.Despawning;

            try
            {
                InvokeDespawning
                (
                    spawnKey,
                    spawnable,
                    reason,
                    bindingCount: 0,
                    publishRegistry: didBeginSpawning
                );
            }
            finally
            {
                spawnable.DespawnFromRegistry = null;

                try
                {
                    spawnable.OnDespawned(reason);
                }
                finally
                {
                    // 완료 훅 반환 전까지 Despawning을 유지해 같은 객체의 재스폰을 차단한다.
                    spawnable.SpawnState = SpawnState.Despawned;

                    try
                    {
                        // OnSpawning 발행을 시작한 실패 객체의 Despawned 상태 확정을 알린다.
                        if (didBeginSpawning)
                        {
                            OnDespawned?.Invoke(spawnKey, spawnable, reason);
                        }
                    }
                    finally
                    {
                        // 완료 알림의 성공 여부와 관계없이 획득 출처에 객체를 반환한다.
                        Release(spawnable, reason);
                    }
                }
            }
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> 객체 훅과 필수 바인딩을 정리하고 일반 Despawning observer까지 끝까지
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
            List<Exception> failures = null;

            try
            {
                despawnable.OnDespawning(reason);
            }
            catch (Exception exception)
            {
                failures = new List<Exception>
                {
                    exception
                };
            }

            var count = Math.Min(bindingCount, bindings.Count);

            for (var index = count - 1; index >= 0; index--)
            {
                try
                {
                    bindings[index].OnDespawning(key, despawnable, reason);
                }
                catch (Exception exception)
                {
                    failures ??= new List<Exception>();
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
                    failures ??= new List<Exception>();
                    failures.Add(exception);
                }
            }

            if (publishRegistry)
            {
                try
                {
                    OnDespawning?.Invoke(key, despawnable, reason);
                }
                catch (Exception exception)
                {
                    failures ??= new List<Exception>();
                    failures.Add(exception);
                }
            }

            if (failures == null)
            {
                return;
            }

            throw failures.Count == 1
                ? failures[0]
                : new AggregateException("SpawnRegistry Despawning transaction 중 일부 정리가 실패했습니다.", failures);
        }

    #endregion

    #region 디스폰

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
            int bindingCount
        )
        {
            if (despawnable == null)
            {
                throw new ArgumentNullException(nameof(despawnable));
            }

            SpawnRegistryUtility.ValidateReason(reason);

            if (_Spawning.Contains(despawnable))
            {
                throw new InvalidOperationException
                (
                    "Spawn 처리를 진행하는 동안 해당 객체를 디스폰할 수 없습니다."
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
                !_Spawned.TryGetValue(despawnable.Key, out var registered) ||
                !ReferenceEquals(registered, despawnable)
            )
            {
                throw new KeyNotFoundException
                (
                    $"등록된 동일 객체가 아닌 키({despawnable.Key})로 디스폰할 수 없습니다."
                );
            }

            var key = despawnable.Key;
            despawnable.SpawnState = SpawnState.Despawning;

            try
            {
                // 사전 단계에서는 key와 Registry 항목이 아직 유효하며 내부 registration runtime을 해제한다.
                InvokeDespawning
                (
                    key,
                    despawnable,
                    reason,
                    bindingCount,
                    detachRegistration: true
                );
            }
            finally
            {
                // 사전 훅이나 이벤트가 실패해도 Registry 소유 상태는 반드시 해제한다.
                _Spawned.Remove(key);

                try
                {
                    despawnable.OnDespawned(reason);
                }
                finally
                {
                    // 객체 완료 훅이 반환된 뒤에만 새 스폰을 허용한다.
                    despawnable.SpawnState = SpawnState.Despawned;

                    try
                    {
                        OnDespawned?.Invoke(key, despawnable, reason);
                    }
                    finally
                    {
                        // 완료 알림의 성공 여부와 관계없이 획득 출처에 객체를 반환한다.
                        Release(despawnable, reason);
                    }
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

            // 진행 중인 Spawn이 이후 등록을 재개하지 못하도록 전체 처리 구간에서 일괄 제거를 차단한다.
            if (_Spawning.Count > 0)
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
            foreach (var entity in _Spawned.Values)
            {
                if (entity.SpawnState == SpawnState.Despawning)
                {
                    throw new InvalidOperationException
                    (
                        "개별 Despawn 처리를 진행하는 동안 DespawnAll을 실행할 수 없습니다."
                    );
                }
            }

            var snapshot = new List<KeyValuePair<TKey, T>>(_Spawned);
            List<Exception> failures = null;

            isDespawningAll = true;

            try
            {
                foreach (var (key, entity) in snapshot)
                {
                    // 앞선 구독자가 이미 처리한 항목은 중복 디스폰하지 않는다.
                    if
                    (
                        !_Spawned.TryGetValue(key, out var current) ||
                        !ReferenceEquals(current, entity) ||
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
                        failures ??= new List<Exception>();
                        failures.Add(exception);
                    }
                }
            }
            finally
            {
                isDespawningAll = false;
            }

            if (failures != null)
            {
                throw new AggregateException("일부 객체를 디스폰하지 못했습니다.", failures);
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
            ValidateSpawnAllowed();
            var spawnable = Acquire();

            return Spawn(spawnable);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> 객체를 스폰한다(외부 호출용).
        /// <br/> true는 스폰 절차와 완료 알림을 수행했음을 뜻한다.
        /// </summary>
        // ------------------------------------------------------------
        public virtual bool TrySpawn(out T spawned)
        {
            spawned = Spawn();

            return spawned != null;
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
            ValidateSpawnAllowed();
            var spawnable = Acquire(param);

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
        public virtual bool TrySpawn(TParam param, out T spawned)
        {
            spawned = Spawn(param);

            return spawned != null;
        }

    #endregion

    }
}
