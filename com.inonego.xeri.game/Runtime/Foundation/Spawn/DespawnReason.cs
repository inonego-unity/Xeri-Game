/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : DespawnReason.cs
수정일 : 2026-07-28

# 설명
디스폰의 공통 분류와 선택적인 세부 식별자를 함께 전달하는 직렬화 가능 값 계약.
Reason은 분류만 표현하며 공격자, 방향, 속도 같은 도메인 데이터는 포함하지 않는다.
========================================================================= BLOCK_HEADER_END */

using System;

using UnityEngine;

namespace inonego.Xeri.Game
{
    // ============================================================
    /// <summary>
    /// 디스폰의 공통 분류.
    /// </summary>
    // ============================================================
    public enum DespawnKind
    {
        Invalid = 0,
        Dead,
        Removed,
        Cleanup,
    }

    // ============================================================
    /// <summary>
    /// 디스폰 공통 분류와 선택적인 세부 코드를 함께 전달하는 값.
    /// </summary>
    // ============================================================
    [Serializable]
    public struct DespawnReason : IEquatable<DespawnReason>
    {

    #region 필드

        // ------------------------------------------------------------
        /// <summary>
        /// HP 사망 등 죽음으로 인한 공통 디스폰 사유.
        /// </summary>
        // ------------------------------------------------------------
        public static readonly DespawnReason Dead = new(DespawnKind.Dead);

        // ------------------------------------------------------------
        /// <summary>
        /// 사망이 아닌 일반적인 제거 사유.
        /// </summary>
        // ------------------------------------------------------------
        public static readonly DespawnReason Removed = new(DespawnKind.Removed);

        // ------------------------------------------------------------
        /// <summary>
        /// 스폰 실패 후 내부 자원을 정리하는 Xeri 롤백 사유.
        /// </summary>
        // ------------------------------------------------------------
        internal static readonly DespawnReason SpawnRollback = new
        (
            DespawnKind.Cleanup,
            "XERI_SPAWN_ROLLBACK"
        );

        // ------------------------------------------------------------
        /// <summary>
        /// 레지스트리 전체를 정리하며 사용하는 Xeri 내부 사유.
        /// </summary>
        // ------------------------------------------------------------
        internal static readonly DespawnReason RegistryCleanup = new
        (
            DespawnKind.Cleanup,
            "XERI_REGISTRY_CLEANUP"
        );

        // ------------------------------------------------------------
        /// <summary>
        /// 공통 디스폰 분류.
        /// </summary>
        // ------------------------------------------------------------
        public DespawnKind Kind => kind;

        [SerializeField]
        private DespawnKind kind;

        // ------------------------------------------------------------
        /// <summary>
        /// 선택적인 안정 문자열 식별자.
        /// </summary>
        // ------------------------------------------------------------
        public string Code => string.IsNullOrEmpty(code) ? null : code;

        [SerializeField]
        private string code;

        // ------------------------------------------------------------
        /// <summary>
        /// 정상 디스폰 요청에 사용할 수 있는 값인지 여부.
        /// </summary>
        // ------------------------------------------------------------
        public bool IsValid
        {
            get
            {
                return
                    kind != DespawnKind.Invalid &&
                    Enum.IsDefined(typeof(DespawnKind), kind) &&
                    !(!string.IsNullOrEmpty(Code) && string.IsNullOrWhiteSpace(Code));
            }
        }

    #endregion

    #region 생성자

        // ------------------------------------------------------------
        /// <summary>
        /// 공통 분류만 사용하는 디스폰 사유를 생성한다.
        /// </summary>
        // ------------------------------------------------------------
        public DespawnReason(DespawnKind kind) : this(kind, null) {}

        // ------------------------------------------------------------
        /// <summary>
        /// 공통 분류와 세부 식별자를 사용하는 디스폰 사유를 생성한다.
        /// </summary>
        // ------------------------------------------------------------
        public DespawnReason(DespawnKind kind, string code) : this()
        {
            if (!string.IsNullOrEmpty(code) && string.IsNullOrWhiteSpace(code))
            {
                throw new ArgumentException("디스폰 사유 코드는 공백일 수 없습니다.", nameof(code));
            }

            this.kind = kind;
            this.code = string.IsNullOrEmpty(code) ? null : code;
        }

    #endregion

    #region 동등성

        // ------------------------------------------------------------
        /// <summary>
        /// 공통 분류와 세부 코드가 동일한지 비교한다.
        /// </summary>
        // ------------------------------------------------------------
        public bool Equals(DespawnReason other)
        {
            return
                kind == other.kind &&
                StringComparer.Ordinal.Equals(Code, other.Code);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 동일한 디스폰 사유인지 비교한다.
        /// </summary>
        // ------------------------------------------------------------
        public override bool Equals(object obj)
        {
            return obj is DespawnReason other && Equals(other);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 공통 분류와 세부 코드의 해시 코드를 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        public override int GetHashCode()
        {
            return HashCode.Combine
            (
                kind,
                Code == null ? 0 : StringComparer.Ordinal.GetHashCode(Code)
            );
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 두 디스폰 사유가 동일한지 비교한다.
        /// </summary>
        // ------------------------------------------------------------
        public static bool operator ==(DespawnReason left, DespawnReason right)
        {
            return left.Equals(right);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 두 디스폰 사유가 다른지 비교한다.
        /// </summary>
        // ------------------------------------------------------------
        public static bool operator !=(DespawnReason left, DespawnReason right)
        {
            return !left.Equals(right);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 공통 분류와 선택적인 세부 코드를 문자열로 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        public override string ToString()
        {
            return Code == null ? kind.ToString() : $"{kind} ({Code})";
        }

    #endregion

    }
}
