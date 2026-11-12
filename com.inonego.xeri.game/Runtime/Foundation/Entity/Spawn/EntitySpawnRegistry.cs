/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : EntitySpawnRegistry.cs
수정일 : 2026-09-21

# 설명
엔티티 전용 스폰 레지스트리 베이스·구현체.

- EntitySpawnRegistryBase<TEntity>     : 엔티티 키 생성기와 키 부여 책임
- EntitySpawnRegistry<TEntity>         : 파라미터 없는 Spawn
- EntitySpawnRegistry<TEntity, TParam> : 파라미터 받는 Spawn (INeedToInit<TParam>)

구체 레지스트리의 복제 정책은 해당 파생 클래스가 소유한다.
========================================================================= BLOCK_HEADER_END */

using System;

using UnityEngine;

namespace inonego.Xeri.Game
{
    // ============================================================
    /// <summary>
    /// 엔티티 스폰 레지스트리 베이스. 키 생성기와 스폰 전 키 부여를 담당한다.
    /// </summary>
    // ============================================================
    [Serializable]
    public abstract class EntitySpawnRegistryBase<TEntity> : SpawnRegistryBase<ulong, TEntity>
    where TEntity : class, IEntity
    {

    #region 필드

        // ------------------------------------------------------------
        /// <summary>
        /// 키 생성기. 기본은 IncreKeyGenerator.
        /// </summary>
        // ------------------------------------------------------------
        public IKeyGenerator<ulong> KeyGenerator => keyGenerator;

        [SerializeReference]
        protected IKeyGenerator<ulong> keyGenerator = new IncreKeyGenerator();

    #endregion

    #region 생성자

        // ------------------------------------------------------------
        /// <summary>
        /// 기본 생성자. IncreKeyGenerator 를 사용한다.
        /// </summary>
        // ------------------------------------------------------------
        public EntitySpawnRegistryBase() : base()
        {
            // NONE
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 외부 키 생성기를 주입받는 생성자.
        /// </summary>
        // ------------------------------------------------------------
        public EntitySpawnRegistryBase(IKeyGenerator<ulong> keyGenerator) : this()
        {
            if (keyGenerator == null)
            {
                throw new ArgumentNullException(nameof(keyGenerator));
            }

            this.keyGenerator = keyGenerator;
        }

    #endregion

    #region 키 부여

        // ------------------------------------------------------------
        /// <summary>
        /// Despawned 엔티티에 새 Registry Key를 부여한다.
        /// </summary>
        // ------------------------------------------------------------
        protected void AssignKey(TEntity entity)
        {
            if (entity == null)
            {
                throw new InvalidOperationException("스폰할 엔티티를 가져올 수 없습니다.");
            }

            if (entity.SpawnState != SpawnState.Despawned)
            {
                throw new InvalidOperationException
                (
                    $"Despawned 상태의 엔티티에만 키를 부여할 수 있습니다. 현재 상태: {entity.SpawnState}"
                );
            }

            entity.SetKey(keyGenerator.Generate());
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// 공통 Spawn 흐름에 진입하기 전에 실패한 Entity에서 할당된 Key를 제거한다.
        /// </summary>
        // ----------------------------------------------------------------------
        protected static void ClearAssignedKey(TEntity entity)
        {
            if (entity != null && entity.SpawnState == SpawnState.Despawned && entity.HasKey)
            {
                entity.ClearKey();
            }
        }

    #endregion

    }

    // ============================================================
    /// <summary>
    /// 파라미터 없는 Acquire/Spawn 을 제공하는 엔티티 스폰 레지스트리.
    /// </summary>
    // ============================================================
    [Serializable]
    public abstract class EntitySpawnRegistry<TEntity> : EntitySpawnRegistryBase<TEntity>
    where TEntity : class, IEntity
    {

    #region 추상 메서드

        // ------------------------------------------------------------
        /// <summary>
        /// 새 엔티티를 풀·생성 등으로 가져온다. 자식이 구현.
        /// </summary>
        // ------------------------------------------------------------
        protected abstract TEntity Acquire();

    #endregion

    #region 스폰

        // ------------------------------------------------------------
        /// <summary>
        /// 엔티티를 스폰한다.
        /// </summary>
        // ------------------------------------------------------------
        protected TEntity Spawn()
        {
            ValidateSpawnAllowed();
            var entity = Acquire();
            var wasSpawnStarted = false;

            try
            {
                AssignKey(entity);
                wasSpawnStarted = true;
                return Spawn(entity);
            }
            catch
            {
                try
                {
                    ClearAssignedKey(entity);
                }
                finally
                {
                    // 공통 Spawn에 진입하기 전 실패한 유효 후보만 획득 출처에 직접 반환한다.
                    if (!wasSpawnStarted && entity != null && entity.SpawnState == SpawnState.Despawned)
                    {
                        Release(entity, DespawnReason.SpawnRollback);
                    }
                }

                throw;
            }
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 엔티티를 스폰한다(외부 호출용).
        /// </summary>
        // ------------------------------------------------------------
        public virtual bool TrySpawn(out TEntity spawned)
        {
            spawned = Spawn();

            return spawned != null;
        }

    #endregion

    }

    // ============================================================
    /// <summary>
    /// 파라미터를 받는 Acquire/Spawn 을 제공하는 엔티티 스폰 레지스트리.
    /// </summary>
    // ============================================================
    [Serializable]
    public abstract class EntitySpawnRegistry<TEntity, TParam> : EntitySpawnRegistryBase<TEntity>
    where TEntity : class, IEntity, INeedToInit<TParam>
    {

    #region 파생 훅

        // ------------------------------------------------------------
        /// <summary>
        /// 파생 클래스가 Init 전에 수행할 작업을 정의하는 훅.
        /// </summary>
        // ------------------------------------------------------------
        protected virtual void OnInit(TEntity spawnable, TParam param)
        {
            // NONE
        }

    #endregion

    #region 추상 메서드

        // ------------------------------------------------------------
        /// <summary>
        /// 새 엔티티를 가져온다. 자식이 구현.
        /// </summary>
        // ------------------------------------------------------------
        protected abstract TEntity Acquire(TParam param);

    #endregion

    #region 스폰

        // ------------------------------------------------------------
        /// <summary>
        /// 엔티티를 스폰한다.
        /// </summary>
        // ------------------------------------------------------------
        protected TEntity Spawn(TParam param)
        {
            ValidateSpawnAllowed();
            var entity = Acquire(param);
            var wasSpawnStarted = false;

            try
            {
                AssignKey(entity);
                wasSpawnStarted = true;

                void InitAction(TEntity spawnable)
                {
                    OnInit(spawnable, param);
                    spawnable.Init(param);
                }

                return Spawn(entity, InitAction);
            }
            catch
            {
                try
                {
                    ClearAssignedKey(entity);
                }
                finally
                {
                    // 공통 Spawn에 진입하기 전 실패한 유효 후보만 획득 출처에 직접 반환한다.
                    if (!wasSpawnStarted && entity != null && entity.SpawnState == SpawnState.Despawned)
                    {
                        Release(entity, DespawnReason.SpawnRollback);
                    }
                }

                throw;
            }
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 엔티티를 스폰한다(외부 호출용).
        /// </summary>
        // ------------------------------------------------------------
        public virtual bool TrySpawn(TParam param, out TEntity spawned)
        {
            spawned = Spawn(param);

            return spawned != null;
        }

    #endregion

    }
}
