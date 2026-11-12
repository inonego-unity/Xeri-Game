/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : EntityBase.cs
수정일 : 2026-09-21

# 설명
Entity Core의 Key·SpawnState와 Registry lifecycle hook을 제공한다.
HP·Group 같은 gameplay capability와 lifetime policy는 concrete Entity/runtime composition이 소유한다.
Key는 Inspector에서 확인할 수 있도록 직렬화하며 SpawnState는 현재 실행에만 속한다.
========================================================================= BLOCK_HEADER_END */

using System;

using UnityEngine;

namespace inonego.Xeri.Game
{
    using Xeri;
    using Xeri.Serializable;

    // ============================================================
    /// <summary>
    /// 엔티티 추상 베이스 클래스.
    /// </summary>
    // ============================================================
    [Serializable]
    public abstract class EntityBase : IEntity
    {

    #region 키 설정

        // ------------------------------------------------------------
        /// <summary>
        /// 현재 키. 미설정 시 접근하면 예외.
        /// </summary>
        // ------------------------------------------------------------
        public ulong Key
        {
            get
            {
                if (key.HasValue)
                {
                    return key.Value;
                }

                throw new InvalidOperationException("키가 설정되어 있지 않습니다.");
            }
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 키 설정 여부.
        /// </summary>
        // ------------------------------------------------------------
        public bool HasKey => key.HasValue;

        [SerializeField, ReadOnly]
        protected XNullable<ulong> key = null;

        // ------------------------------------------------------------
        /// <summary>
        /// Registry가 확정한 Entity key를 설정한다.
        /// </summary>
        // ------------------------------------------------------------
        protected internal virtual void SetKey(ulong key)
        {
            this.key = key;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Registry가 관리하던 Entity key를 해제한다.
        /// </summary>
        // ------------------------------------------------------------
        protected internal virtual void ClearKey()
        {
            key = null;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// IEntity 계약을 통해 Registry가 확정한 Entity key를 설정한다.
        /// </summary>
        // ------------------------------------------------------------
        void IEntity.SetKey(ulong key) => SetKey(key);

        // ------------------------------------------------------------
        /// <summary>
        /// IEntity 계약을 통해 Registry가 관리하던 Entity key를 해제한다.
        /// </summary>
        // ------------------------------------------------------------
        void IEntity.ClearKey() => ClearKey();

    #endregion

    #region 필드

        // ------------------------------------------------------------
        /// <summary>
        /// Registry가 관리하는 현재 스폰 처리 상태.
        /// </summary>
        // ------------------------------------------------------------
        public SpawnState SpawnState => spawnState;

        [NonSerialized]
        protected SpawnState spawnState = SpawnState.Despawned;

    #endregion

    #region 생성자

        // ------------------------------------------------------------
        /// <summary>
        /// 기본 생성자.
        /// </summary>
        // ------------------------------------------------------------
        protected EntityBase() : base()
        {
            // NONE
        }

    #endregion

    #region 인터페이스 구현

        // ------------------------------------------------------------
        /// <summary>
        /// SpawnRegistry가 관리하는 스폰 처리 상태를 읽거나 변경한다.
        /// </summary>
        // ------------------------------------------------------------
        SpawnState ISpawnRegistryObject<ulong>.SpawnState
        {
            get => spawnState;
            set => spawnState = value;
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// Entity의 디스폰 요청을 현재 Registry에 전달하는 콜백을 읽거나 변경한다.
        /// </summary>
        // ----------------------------------------------------------------------
        Action<DespawnReason> IDespawnable.DespawnFromRegistry
        {
            get => despawnFromRegistry;
            set => despawnFromRegistry = value;
        }

        [NonSerialized]
        private Action<DespawnReason> despawnFromRegistry = null;

        // ------------------------------------------------------------
        /// <summary>
        /// Spawning 상태에서 호출되는 파생 Entity 훅.
        /// </summary>
        // ------------------------------------------------------------
        protected virtual void OnSpawning()
        {
            // NONE
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Registry 등록 전에 파생 Entity의 Spawning 훅을 호출한다.
        /// </summary>
        // ------------------------------------------------------------
        void ISpawnable.OnSpawning()
        {
            OnSpawning();
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Registry 등록이 완료된 Spawned 상태에서 호출되는 파생 Entity 훅.
        /// </summary>
        // ------------------------------------------------------------
        protected virtual void OnSpawned()
        {
            // NONE
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// Registry 등록 완료 뒤 파생 Entity의 실제 Spawn 완료 훅을 호출한다.
        /// </summary>
        // ----------------------------------------------------------------------
        void ISpawnable.OnSpawned()
        {
            OnSpawned();
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// Registry 등록 해제 전에 Despawning 상태에서 호출되는 파생 Entity 훅.
        /// </summary>
        // ----------------------------------------------------------------------
        protected virtual void OnDespawning(DespawnReason reason)
        {
            // NONE
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Registry 등록 해제 전에 파생 Entity 훅을 호출한다.
        /// </summary>
        // ------------------------------------------------------------
        void IDespawnable.OnDespawning(DespawnReason reason)
        {
            OnDespawning(reason);
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// Registry 등록 해제 뒤 Despawning 상태에서 호출되는 파생 Entity 훅.
        /// </summary>
        // ----------------------------------------------------------------------
        protected virtual void OnDespawned(DespawnReason reason)
        {
            // NONE
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// Registry 등록 해제 뒤 파생 Entity의 완료 훅과 Key 해제를 수행한다.
        /// </summary>
        // ----------------------------------------------------------------------
        void IDespawnable.OnDespawned(DespawnReason reason)
        {
            try
            {
                OnDespawned(reason);
            }
            finally
            {
                // 파생 훅이 실패해도 Despawned Entity에 이전 Registry Key를 남기지 않는다.
                ClearKey();
            }
        }

    #endregion

    }
}
