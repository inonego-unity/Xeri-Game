/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : ISpawnRegistry.cs
수정일 : 2026-09-21

# 설명
스폰 레지스트리, 필수 소유권 바인딩 및 스폰된 객체 사전 인터페이스.
========================================================================= BLOCK_HEADER_END */

using System;
using System.Collections;
using System.Collections.Generic;

namespace inonego.Xeri.Game
{
    // ============================================================
    /// <summary>
    /// 스폰된 객체를 키로 보관하는 사전 인터페이스.
    /// </summary>
    // ============================================================
    public interface ISpawnedDictionary<TKey, T> : IReadOnlyDictionary<TKey, T>
    where TKey : IEquatable<TKey>
    where T : class, ISpawnRegistryObject<TKey>
    {
        // NONE
    }

    // ================================================================================
    /// <summary>
    /// <br/> Registry의 Spawned·Despawning transaction에 참여하는 필수 lifecycle
    /// <br/> 바인딩.
    /// </summary>
    // ================================================================================
    public interface ISpawnRegistryBinding<TKey, T>
    where TKey : IEquatable<TKey>
    where T : class, ISpawnRegistryObject<TKey>
    {
        // ----------------------------------------------------------------------
        /// <summary>
        /// 객체의 Spawned 상태와 Registry 등록이 완료된 뒤 필수 소유권을 동기화한다.
        /// </summary>
        // ----------------------------------------------------------------------
        void OnSpawned(TKey key, T spawnable);

        // ------------------------------------------------------------
        /// <summary>
        /// 객체가 Registry에서 제거되기 전에 필수 소유권을 정리한다.
        /// </summary>
        // ------------------------------------------------------------
        void OnDespawning(TKey key, T despawnable, DespawnReason reason);
    }

    // ================================================================================
    /// <summary>
    /// <br/> 스폰 레지스트리 인터페이스.
    /// <br/> 공개 이벤트는 일반 멀티캐스트 알림이며 구독자 예외는 이후 이벤트 전달을 중단할 수
    /// <br/> 있다.
    /// </summary>
    // ================================================================================
    public interface ISpawnRegistry<TKey, T>
    where TKey : IEquatable<TKey>
    where T : class, ISpawnRegistryObject<TKey>
    {
        // ------------------------------------------------------------
        /// <summary>
        /// 현재 스폰된 객체들의 사전.
        /// </summary>
        // ------------------------------------------------------------
        public ISpawnedDictionary<TKey, T> Spawned { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 해당 키를 가지는 객체를 찾는다.
        /// </summary>
        // ------------------------------------------------------------
        public T Find(TKey key);

        // ------------------------------------------------------------
        /// <summary>
        /// 동일 키를 가지는 객체를 찾는다.
        /// </summary>
        // ------------------------------------------------------------
        public T Find(IKeyable<TKey> keyable);

        // ------------------------------------------------------------
        /// <summary>
        /// 객체가 Spawning 상태로 진입한 뒤 Registry 등록 전에 호출된다.
        /// </summary>
        // ------------------------------------------------------------
        public event Action<TKey, T> OnSpawning;

        // ------------------------------------------------------------
        /// <summary>
        /// 객체의 Registry 등록과 Spawned 상태 전환이 완료된 뒤 호출된다.
        /// </summary>
        // ------------------------------------------------------------
        public event Action<TKey, T> OnSpawned;

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> 객체가 등록 해제 또는 Spawn 실패 정리를 시작하기 전에 호출된다.
        /// <br/> Spawn 실패 정리에서는 객체가 Registry에 등록되지 않았을 수 있다.
        /// </summary>
        // ----------------------------------------------------------------------
        public event Action<TKey, T, DespawnReason> OnDespawning;

        // ----------------------------------------------------------------------
        /// <summary>
        /// 객체의 등록 해제 또는 Spawn 실패 정리와 상태 전환이 완료된 뒤 호출된다.
        /// </summary>
        // ----------------------------------------------------------------------
        public event Action<TKey, T, DespawnReason> OnDespawned;
    }
}
