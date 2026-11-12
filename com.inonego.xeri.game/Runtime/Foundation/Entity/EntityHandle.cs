/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : EntityHandle.cs
수정일 : 2026-08-18

# 설명
Spawned Entity와 생성 시점 Key를 함께 보존하는 비소유 Runtime Handle.
Entity 객체가 남아 있어도 Despawn 또는 다른 수명으로 바뀐 stale reference를 검증한다.
========================================================================= BLOCK_HEADER_END */

using System;

namespace inonego.Xeri.Game
{
    // ======================================================================
    /// <summary>
    /// Spawn 수명과 Key가 유지되는 동안 같은 Entity를 가리키는 비소유 Handle.
    /// </summary>
    // ======================================================================
    [Serializable]
    public readonly struct EntityHandle
    {

    #region 필드

        // ------------------------------------------------------------
        /// <summary>
        /// Handle 생성 시 가리키던 Entity.
        /// </summary>
        // ------------------------------------------------------------
        public IReadOnlyEntity Entity { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// Handle 생성 시 Entity가 가지고 있던 Key.
        /// </summary>
        // ------------------------------------------------------------
        public ulong Key { get; }

    #endregion

    #region 생성자

        // ----------------------------------------------------------------------
        /// <summary>
        /// 현재 Spawned 상태이며 Key가 설정된 Entity의 Handle을 생성한다.
        /// </summary>
        // ----------------------------------------------------------------------
        public EntityHandle(IReadOnlyEntity entity) : this()
        {
            if (entity == null)
            {
                throw new ArgumentNullException(nameof(entity));
            }

            if (!entity.HasKey || entity.SpawnState != SpawnState.Spawned)
            {
                throw new InvalidOperationException
                (
                    "Spawned 상태이며 Key가 설정된 Entity만 Handle로 만들 수 있습니다."
                );
            }

            Entity = entity;
            Key = entity.Key;
        }

    #endregion

    #region 생성

        // ----------------------------------------------------------------------
        /// <summary>
        /// 유효한 Spawned Entity에서 Handle 생성을 시도한다.
        /// </summary>
        // ----------------------------------------------------------------------
        public static bool TryCreate
        (
            IReadOnlyEntity entity,
            out EntityHandle handle
        )
        {
            handle = default;

            if
            (
                entity == null ||
                !entity.HasKey ||
                entity.SpawnState != SpawnState.Spawned
            )
            {
                return false;
            }

            handle = new EntityHandle(entity);
            return true;
        }

    #endregion

    #region 검증

        // ----------------------------------------------------------------------
        /// <summary>
        /// 현재도 생성 시점과 같은 Spawned Entity 수명을 가리키는지 반환한다.
        /// </summary>
        // ----------------------------------------------------------------------
        public bool IsValid()
        {
            return
                Entity != null &&
                Entity.HasKey &&
                Entity.Key == Key &&
                Entity.SpawnState == SpawnState.Spawned;
        }

    #endregion

    }
}