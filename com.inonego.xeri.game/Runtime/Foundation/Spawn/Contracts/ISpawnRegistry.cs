/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : ISpawnRegistry.cs
수정일 : 2026-11-14

# 설명
스폰과 직접 등록에 공통 적용되는 필수 Binding, 조회 사전 및 수명 이벤트 계약.
실패한 연결 콜백도 정리 대상으로 포함하며, 해제 실패는 terminal 오류로 전달한다.
========================================================================= BLOCK_HEADER_END */

using System;
using System.Collections;
using System.Collections.Generic;

namespace inonego.Xeri.Game
{
    // ======================================================================
    /// <summary>
    /// <br/> Registry의 등록·해제 transaction에 참여하는 필수 lifecycle
    /// <br/> 바인딩.
    /// </summary>
    // ======================================================================
    public interface ISpawnRegistryBinding<TKey, T>
    where TKey : IEquatable<TKey>
    where T : class, ISpawnRegistryObject<TKey>
    {
        // ------------------------------------------------------------
        /// <summary>
        /// <br/> Registry 등록 뒤 필수 자원을 연결한다.
        /// <br/> 실패한 호출도 Rollback 대상이다.
        /// </summary>
        // ------------------------------------------------------------
        void OnRegistered(TKey key, T spawnable);

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> Registry 해제 전 필수 자원을 정리한다.
        /// <br/> 해제 실패도 terminal이며 재시도하지 않는다.
        /// </summary>
        // ------------------------------------------------------------
        void OnUnregistering(TKey key, T despawnable, DespawnReason reason);
    }

    // ======================================================================
    /// <summary>
    /// <br/> 스폰 레지스트리 인터페이스.
    /// <br/> 공개 이벤트는 모든 구독자의 호출을 시도하고 예외를 모아 전달한다.
    /// <br/> 시작 이벤트에 오류가 있으면 등록은 Rollback한다.
    /// </summary>
    // ======================================================================
    public interface ISpawnRegistry<TKey, T>
    where TKey : IEquatable<TKey>
    where T : class, ISpawnRegistryObject<TKey>
    {
        // ------------------------------------------------------------
        /// <summary>
        /// 현재 등록된 모든 객체의 읽기 전용 조회 사전.
        /// </summary>
        // ------------------------------------------------------------
        public IReadOnlyDictionary<TKey, T> Registered { get; }

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
        /// Registry 등록 직전에 호출한다. Spawn 및 직접 등록에 공통 적용된다.
        /// </summary>
        // ------------------------------------------------------------
        public event Action<TKey, T> OnRegistering;

        // ------------------------------------------------------------
        /// <summary>
        /// 필수 Binding과 등록 작업 완료 후 호출한다.
        /// </summary>
        // ------------------------------------------------------------
        public event Action<TKey, T> OnRegistered;

        // ------------------------------------------------------------
        /// <summary>
        /// 등록 해제 전 호출한다. Spawn 및 직접 등록에 공통 적용된다.
        /// </summary>
        // ------------------------------------------------------------
        public event Action<TKey, T, DespawnReason> OnUnregistering;

        // ------------------------------------------------------------
        /// <summary>
        /// Registry membership 해제 완료 후 호출한다.
        /// </summary>
        // ------------------------------------------------------------
        public event Action<TKey, T, DespawnReason> OnUnregistered;

        // ------------------------------------------------------------
        /// <summary>
        /// 게임 Spawn 진입 후 Registry 등록 전에 호출된다.
        /// </summary>
        // ------------------------------------------------------------
        public event Action<TKey, T> OnSpawning;

        // ------------------------------------------------------------
        /// <summary>
        /// 게임 Spawn의 등록 및 상태 전환이 완료된 후 호출된다.
        /// </summary>
        // ------------------------------------------------------------
        public event Action<TKey, T> OnSpawned;

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> 완료된 게임 Spawn의 해제 전에 호출된다.
        /// <br/> 직접 등록 및 미완료 Spawn Rollback에는 호출하지 않는다.
        /// </summary>
        // ------------------------------------------------------------
        public event Action<TKey, T, DespawnReason> OnDespawning;

        // ------------------------------------------------------------
        /// <summary>
        /// 완료된 게임 Spawn의 해제 작업 종료 후 호출된다.
        /// </summary>
        // ------------------------------------------------------------
        public event Action<TKey, T, DespawnReason> OnDespawned;
    }
}
