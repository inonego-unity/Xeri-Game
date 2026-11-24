/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : FactionChecker.cs
수정일 : 2026-09-21

# 설명
엔티티 그룹 인덱스(int) 기반 진영 관계 판정 추상 클래스 및 관련 열거형.

- RelativeFaction       : 상대 진영 관계 (Me/Ally/Enemy/Neutral).
- RelativeFactionGroup  : 그룹 단위 관계 (Me/Ally/All/AllyNotMe/AllNotMe/Neutral/Enemy).
- FactionChecker        : 두 그룹 인덱스 또는 두 IReadOnlyGroupMember 의 관계를 판정한다.
========================================================================= BLOCK_HEADER_END */

using System;

namespace inonego.Xeri.Game
{
    // ============================================================
    /// <summary>
    /// 상대 진영 관계.
    /// </summary>
    // ============================================================
    public enum RelativeFaction : int
    {
        Me, Ally, Enemy, Neutral = -1,
    }

    // ============================================================
    /// <summary>
    /// 그룹 단위 상대 진영 관계.
    /// </summary>
    // ============================================================
    public enum RelativeFactionGroup
    {
        Me,
        Ally,
        All,
        AllyNotMe,
        AllNotMe,
        Neutral,
        Enemy,
    }

    // ============================================================
    /// <summary>
    /// 그룹 인덱스 기반 진영 관계 판정 추상 클래스.
    /// </summary>
    // ============================================================
    [Serializable]
    public abstract class FactionChecker
    {

    #region 추상 메서드

        // ------------------------------------------------------------
        /// <summary>
        /// 두 그룹 인덱스의 상대 진영 관계를 판정한다.
        /// </summary>
        // ------------------------------------------------------------
        public abstract RelativeFaction Check(int self, int other);

        // ------------------------------------------------------------
        /// <summary>
        /// 두 그룹 인덱스가 주어진 그룹 관계에 속하는지 판정한다.
        /// </summary>
        // ------------------------------------------------------------
        public abstract bool CheckIsInGroup(int self, int other, RelativeFactionGroup group);

    #endregion

    #region 편의 오버로드

        // ------------------------------------------------------------
        /// <summary>
        /// 두 엔티티의 그룹 인덱스로부터 상대 진영 관계를 판정한다.
        /// </summary>
        // ------------------------------------------------------------
        public RelativeFaction Check(IReadOnlyGroupMember self, IReadOnlyGroupMember other)
        {
            return Check(self.Group.Base, other.Group.Base);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 두 엔티티가 주어진 그룹 관계에 속하는지 판정한다.
        /// </summary>
        // ------------------------------------------------------------
        public bool CheckIsInGroup(IReadOnlyGroupMember self, IReadOnlyGroupMember other, RelativeFactionGroup group)
        {
            return CheckIsInGroup(self.Group.Base, other.Group.Base, group);
        }

    #endregion

    }
}
