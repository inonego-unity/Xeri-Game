/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : EntitySpawnRegistry.cs
수정일 : 2026-11-13

# 설명
엔티티 전용 스폰 레지스트리 베이스·구현체.

- EntitySpawnRegistryBase<TEntity>     : 엔티티 Key 발급과 외부 확정 Key 등록 책임
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
    /// 엔티티의 Key 발급과 외부 확정 Key의 등록을 담당한다.
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

            if (entity.HasKey)
            {
                throw new InvalidOperationException("새 Registry Key를 발급할 Entity에 이미 Key가 설정되어 있습니다.");
            }

            var key = keyGenerator.Generate();

            if (Spawned.ContainsKey(key))
            {
                throw new InvalidOperationException
                (
                    $"KeyGenerator가 이미 등록된 Key({key})를 생성했습니다. 발급 범위를 분리하거나 생성기 정책을 조정해야 합니다."
                );
            }

            SetAndValidateKey(entity, key);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Key 설정 훅의 부분 실패를 정리하고 Key를 검증한다.
        /// </summary>
        // ------------------------------------------------------------
        private static void SetAndValidateKey(TEntity entity, ulong key)
        {
            try
            {
                entity.SetKey(key);

                if (!entity.HasKey || entity.Key != key)
                {
                    throw new InvalidOperationException("Entity가 Registry에서 확정한 Key를 설정하지 않았습니다.");
                }
            }
            catch (Exception assignException)
            {
                // 진입 전에 Key가 없던 Entity만 호출하므로 이번 부분 할당만 정리한다.
                if (entity.HasKey)
                {
                    try
                    {
                        entity.ClearKey();
                    }
                    catch (Exception clearException)
                    {
                        throw new AggregateException
                        (
                            "Entity Key 설정 및 부분 할당 정리에 모두 실패했습니다.",
                            assignException,
                            clearException
                        );
                    }
                }

                throw;
            }
        }

    #endregion

    #region 외부 확정 Key 등록

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/> 외부에서 확정한 Key로 Despawned Entity를 Registry에 등록한다.
        /// <br/> Key의 유일 범위는 이 Registry이며, 생성기와의 충돌 정책은 호출자가 정한다.
        /// </summary>
        // --------------------------------------------------------------------------------
        public TEntity SpawnWithKey(TEntity entity, ulong key)
        {
            ValidateSpawnAllowed();

            if (entity == null)
            {
                throw new ArgumentNullException(nameof(entity));
            }

            if (entity.SpawnState != SpawnState.Despawned)
            {
                throw new InvalidOperationException
                (
                    $"Despawned 상태의 Entity만 등록할 수 있습니다. 현재 상태: {entity.SpawnState}"
                );
            }

            if (entity.HasKey)
            {
                if (entity.Key != key)
                {
                    throw new InvalidOperationException("Entity의 기존 Key와 외부 확정 Key가 일치하지 않습니다.");
                }
            }
            else
            {
                // 신규 Key를 설정하기 전에 충돌을 거절해 후보의 기존 상태를 보존한다.
                if (Spawned.ContainsKey(key))
                {
                    throw new InvalidOperationException($"이미 동일 Key({key})가 등록되어 있습니다.");
                }

                SetAndValidateKey(entity, key);
            }

            // 등록·Binding·lifecycle·rollback은 공통 Registry의 기존 객체 경로를 사용한다.
            return base.SpawnExisting(entity);
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

            // 획득 출처가 객체를 제공하지 못한 경우는 정상적인 TrySpawn 실패다.
            if (entity == null)
            {
                return null;
            }

            var didBeginSpawn = false;

            try
            {
                AssignKey(entity);
                didBeginSpawn = true;
                return Spawn(entity);
            }
            catch (Exception spawnException)
            {
                // 공통 Spawn 진입 후의 Key·수명 정리는 SpawnRegistry가 소유한다.
                // 진입 전 발급 실패는 기존 Entity Key를 변경하지 않고 획득 출처에만 반환한다.
                if (!didBeginSpawn && entity != null && entity.SpawnState == SpawnState.Despawned)
                {
                    try
                    {
                        Release(entity, DespawnReason.SpawnRollback);
                    }
                    catch (Exception releaseException)
                    {
                        throw new AggregateException
                        (
                            "Entity Key 발급과 획득 객체 반환에 모두 실패했습니다.",
                            spawnException,
                            releaseException
                        );
                    }
                }

                throw;
            }
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Acquire가 null이면 false, Key 계약 위반은 예외다.
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

            // 획득 출처가 객체를 제공하지 못한 경우는 정상적인 TrySpawn 실패다.
            if (entity == null)
            {
                return null;
            }

            var didBeginSpawn = false;

            try
            {
                AssignKey(entity);
                didBeginSpawn = true;

                void InitAction(TEntity spawnable)
                {
                    OnInit(spawnable, param);
                    spawnable.Init(param);
                }

                return Spawn(entity, InitAction);
            }
            catch (Exception spawnException)
            {
                // 공통 Spawn 진입 후의 Key·수명 정리는 SpawnRegistry가 소유한다.
                // 진입 전 발급 실패는 기존 Entity Key를 변경하지 않고 획득 출처에만 반환한다.
                if (!didBeginSpawn && entity != null && entity.SpawnState == SpawnState.Despawned)
                {
                    try
                    {
                        Release(entity, DespawnReason.SpawnRollback);
                    }
                    catch (Exception releaseException)
                    {
                        throw new AggregateException
                        (
                            "Entity Key 발급과 획득 객체 반환에 모두 실패했습니다.",
                            spawnException,
                            releaseException
                        );
                    }
                }

                throw;
            }
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Acquire가 null이면 false, Key 계약 위반은 예외다.
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
