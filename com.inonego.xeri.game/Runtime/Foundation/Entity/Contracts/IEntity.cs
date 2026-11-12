/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : IEntity.cs
수정일 : 2026-09-21

# 설명
Entity Core의 식별·Registry lifecycle 계약과 HP·Group 선택 capability를 정의한다.
Entity 자체는 HP/Group을 강제하지 않으며 필요한 concrete Entity만 capability를 구현한다.
========================================================================= BLOCK_HEADER_END */

namespace inonego.Xeri.Game
{
    using Xeri;
    using Xeri.Serializable;

    // ============================================================
    /// <summary>
    /// Entity Core의 읽기 전용 식별·lifecycle 계약.
    /// </summary>
    // ============================================================
    public interface IReadOnlyEntity : IKeyable<ulong>
    {
        // ------------------------------------------------------------
        /// <summary>
        /// Registry가 관리하는 현재 스폰 처리 상태.
        /// </summary>
        // ------------------------------------------------------------
        SpawnState SpawnState { get; }
    }

    // ============================================================
    /// <summary>
    /// 시스템 내부에서 SpawnRegistry lifecycle과 Key 변경을 허용한다.
    /// </summary>
    // ============================================================
    public interface IEntity : IReadOnlyEntity, ISpawnRegistryObject<ulong>
    {
        // ------------------------------------------------------------
        /// <summary>
        /// 두 상위 계약의 동일한 스폰 상태를 읽기 전용으로 노출한다.
        /// </summary>
        // ------------------------------------------------------------
        new SpawnState SpawnState { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// Registry가 확정한 Entity Key를 설정한다.
        /// </summary>
        // ------------------------------------------------------------
        protected internal void SetKey(ulong key);

        // ------------------------------------------------------------
        /// <summary>
        /// Registry가 관리하던 Entity Key를 해제한다.
        /// </summary>
        // ------------------------------------------------------------
        protected internal void ClearKey();
    }

    // ============================================================
    /// <summary>
    /// 읽기 전용 HP capability를 제공한다.
    /// </summary>
    // ============================================================
    public interface IReadOnlyHPOwner
    {
        IReadOnlyHP HP { get; }
    }

    // ============================================================
    /// <summary>
    /// 변경 가능한 HP capability를 제공한다.
    /// </summary>
    // ============================================================
    public interface IHPOwner : IReadOnlyHPOwner
    {
        new IHP HP { get; }
    }

    // ============================================================
    /// <summary>
    /// 값 타입이 지정된 읽기 전용 HP capability를 제공한다.
    /// </summary>
    // ============================================================
    public interface IReadOnlyHPOwner<TValue> : IReadOnlyHPOwner
    {
        new IReadOnlyHP<TValue> HP { get; }
    }

    // ============================================================
    /// <summary>
    /// 값 타입이 지정된 변경 가능한 HP capability를 제공한다.
    /// </summary>
    // ============================================================
    public interface IHPOwner<TValue> :
        IHPOwner,
        IReadOnlyHPOwner<TValue>
    {
        new IHP<TValue> HP { get; }
    }

    // ============================================================
    /// <summary>
    /// 읽기 전용 Group membership capability를 제공한다.
    /// </summary>
    // ============================================================
    public interface IReadOnlyGroupMember
    {
        IReadOnlyValue<int> Group { get; }
    }

    // ============================================================
    /// <summary>
    /// 변경 가능한 Group membership capability를 제공한다.
    /// </summary>
    // ============================================================
    public interface IGroupMember : IReadOnlyGroupMember
    {
        new IValue<int> Group { get; }
    }
}
