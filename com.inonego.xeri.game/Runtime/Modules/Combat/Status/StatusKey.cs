/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : StatusKey.cs
수정일 : 2026-11-20

# 설명
Status 종류를 구분하는 Key의 값·동등성 계약을 정의한다.
직렬화된 값도 생성자 입력과 동일하게 공백을 정규화해 비교한다.
========================================================================= BLOCK_HEADER_END */

using System;

using UnityEngine;

namespace inonego.Xeri.Game
{
    // ============================================================
    /// <summary>
    /// Status Spec을 구분하는 논리 Key.
    /// </summary>
    // ============================================================
    [Serializable]
    public struct StatusKey : IEquatable<StatusKey>
    {

    #region 값 및 생성자

        // ------------------------------------------------------------
        /// <summary>
        /// 앞뒤 공백을 정규화한 Status Key 값을 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        public string Value => value?.Trim() ?? string.Empty;

        [SerializeField]
        private string value;

        // ------------------------------------------------------------
        /// <summary>
        /// 유효한 Status Key인지 확인한다.
        /// </summary>
        // ------------------------------------------------------------
        public bool IsValid => !string.IsNullOrWhiteSpace(Value);

        // ------------------------------------------------------------
        /// <summary>
        /// 유효한 문자열로 Status Key를 생성한다.
        /// </summary>
        // ------------------------------------------------------------
        public StatusKey(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("Status Key가 비어 있습니다.", nameof(value));
            }

            this.value = value.Trim();
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 정규화된 Status Key 문자열을 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        public override string ToString() => Value;

    #endregion

    #region 동등성

        // ------------------------------------------------------------
        /// <summary>
        /// 다른 Status Key와 값 동등성을 비교한다.
        /// </summary>
        // ------------------------------------------------------------
        public bool Equals(StatusKey other) => string.Equals(Value, other.Value, StringComparison.Ordinal);

        // ------------------------------------------------------------
        /// <summary>
        /// 객체가 동일한 Status Key인지 비교한다.
        /// </summary>
        // ------------------------------------------------------------
        public override bool Equals(object obj) => obj is StatusKey other && Equals(other);

        // ------------------------------------------------------------
        /// <summary>
        /// 정규화된 Status Key의 해시 코드를 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);

        // ------------------------------------------------------------
        /// <summary>
        /// 두 Status Key의 값이 같은지 비교한다.
        /// </summary>
        // ------------------------------------------------------------
        public static bool operator ==(StatusKey left, StatusKey right) => left.Equals(right);

        // ------------------------------------------------------------
        /// <summary>
        /// 두 Status Key의 값이 다른지 비교한다.
        /// </summary>
        // ------------------------------------------------------------
        public static bool operator !=(StatusKey left, StatusKey right) => !left.Equals(right);

    #endregion

    }
}
