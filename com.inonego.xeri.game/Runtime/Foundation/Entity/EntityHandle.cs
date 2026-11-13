/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : EntityHandle.cs
수정일 : 2026-11-13

# 설명
Spawned Entity의 객체 참조·Key·등록 Revision을 함께 보존하는 비소유 Runtime Handle.
같은 객체와 Key가 다시 등록되어도 이전 등록 수명을 가리키는 Handle은 무효로 남는다.
========================================================================= BLOCK_HEADER_END */

using System;

namespace inonego.Xeri.Game
{
    // ======================================================================
    /// <summary>
    /// Spawn 수명과 Key가 유지되는 동안 같은 Entity를 가리키는 비소유 Handle.
    /// </summary>
    // ======================================================================
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

        // ------------------------------------------------------------
        /// <summary>
        /// Handle 생성 시점의 등록 Revision.
        /// </summary>
        // ------------------------------------------------------------
        public ulong RegistrationRevision { get; }

    #endregion

    #region 생성자

        // ------------------------------------------------------------
        /// <summary>
        /// 유효한 Spawned Entity의 Handle을 생성한다.
        /// </summary>
        // ------------------------------------------------------------
        public EntityHandle(IReadOnlyEntity entity) : this()
        {
            if (entity == null)
            {
                throw new ArgumentNullException(nameof(entity));
            }

            if
            (
                !entity.HasKey ||
                entity.SpawnState != SpawnState.Spawned ||
                entity.RegistrationRevision == 0
            )
            {
                throw new InvalidOperationException
                (
                    "Spawned 상태이며 Key가 설정된 Entity만 Handle로 만들 수 있습니다."
                );
            }

            Entity = entity;
            Key = entity.Key;
            RegistrationRevision = entity.RegistrationRevision;
        }

    #endregion

    #region 생성

        // ------------------------------------------------------------
        /// <summary>
        /// Spawned Entity에서 Handle 생성을 시도한다.
        /// </summary>
        // ------------------------------------------------------------
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
                entity.SpawnState != SpawnState.Spawned ||
                entity.RegistrationRevision == 0
            )
            {
                return false;
            }

            handle = new EntityHandle(entity);
            return true;
        }

    #endregion

    #region 검증

        // ------------------------------------------------------------
        /// <summary>
        /// Handle의 등록 수명이 여전히 유효한지 확인한다.
        /// </summary>
        // ------------------------------------------------------------
        public bool IsValid()
        {
            return
                Entity != null &&
                Entity.HasKey &&
                Entity.Key == Key &&
                Entity.RegistrationRevision == RegistrationRevision &&
                Entity.SpawnState == SpawnState.Spawned;
        }

    #endregion

    }
}