/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : ISpawnable.cs
수정일 : 2026-07-28

# 설명
스폰 처리 상태와 스폰 시스템 기본 인터페이스 모음.

- SpawnState                 : Registry가 관리하는 스폰 처리 상태
- ISpawnRegistryObject<TKey> : SpawnRegistry 등록 가능한 객체 (IKeyable + ISpawnable + IDespawnable)
- ISpawnable                 : Registry 전용 Spawning/Spawned 훅
- IDespawnable               : Registry 전용 Despawning/Despawned 훅 + 디스폰 요청 콜백 슬롯
========================================================================= BLOCK_HEADER_END */

using System;

namespace inonego.Xeri.Game
{
    // ============================================================
    /// <summary>
    /// Registry가 관리하는 객체의 스폰 처리 상태.
    /// </summary>
    // ============================================================
    public enum SpawnState
    {
        Despawned = 0,
        Spawning,
        Spawned,
        Despawning,
    }

    // ============================================================
    /// <summary>
    /// 스폰 등록 가능한 객체를 위한 인터페이스입니다.
    /// </summary>
    /// <typeparam name="TKey">스폰 등록 가능한 객체의 키 타입입니다.</typeparam>
    // ============================================================
    public interface ISpawnRegistryObject<TKey> : IKeyable<TKey>, ISpawnable, IDespawnable
    where TKey : IEquatable<TKey>
    {
        // ------------------------------------------------------------
        /// <summary>
        /// Registry가 소유하는 현재 스폰 처리 상태.
        /// </summary>
        // ------------------------------------------------------------
        public SpawnState SpawnState
        {
            get; protected internal set;
        }
    }

    // ============================================================
    /// <summary>
    /// 스폰 가능한 객체를 위한 인터페이스입니다.
    /// </summary>
    // ============================================================
    public interface ISpawnable
    {
        // ------------------------------------------------------------
        /// <summary>
        /// Registry가 Spawning 상태에서 등록 전에 호출한다.
        /// </summary>
        // ------------------------------------------------------------
        protected internal void OnSpawning();

        // ------------------------------------------------------------
        /// <summary>
        /// Registry 등록과 Spawned 상태 전환이 끝난 뒤 호출한다.
        /// </summary>
        // ------------------------------------------------------------
        protected internal void OnSpawned();
    }

    // ============================================================
    /// <summary>
    /// 디스폰 가능한 객체를 위한 인터페이스입니다.
    /// </summary>
    // ============================================================
    public interface IDespawnable
    {
        // ------------------------------------------------------------
        /// <summary>
        /// Registry가 Despawning 상태에서 등록 해제 또는 Spawn 실패 정리 전에 호출한다.
        /// </summary>
        // ------------------------------------------------------------
        protected internal void OnDespawning(DespawnReason reason);

        // ------------------------------------------------------------
        /// <summary>
        /// Registry 등록 해제 또는 Spawn 실패 정리 뒤 호출하며 반환되면 Despawned 상태로 전환한다.
        /// </summary>
        // ------------------------------------------------------------
        protected internal void OnDespawned(DespawnReason reason);

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/> Registry에 등록된 Spawned 객체의 디스폰 요청을 현재 Registry에 전달하는 콜백.
        /// <br/> Spawned 훅과 이벤트를 발행하는 동안에는 디스폰을 요청할 수 없다.
        /// </summary>
        // --------------------------------------------------------------------------------
        protected internal Action<DespawnReason> DespawnFromRegistry { get; set; }
    }
}
