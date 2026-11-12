/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : AbilityKeys.cs
수정일 : 2026-11-08

# 설명
Ability Definition Key와 Binding Entry Key의 값·동등성 계약을 정의한다.
직렬화된 값도 생성자 입력과 동일하게 공백을 정규화해 비교한다.
========================================================================= BLOCK_HEADER_END */

using System;

using UnityEngine;

namespace inonego.Xeri.Game
{
    // ============================================================
    /// <summary>
    /// Ability Definition을 구분하는 논리 Key.
    /// </summary>
    // ============================================================
    [Serializable]
    public struct AbilityKey : IEquatable<AbilityKey>
    {

    #region 값 및 생성자

        // ------------------------------------------------------------
        /// <summary>
        /// 앞뒤 공백을 정규화한 Ability Definition Key 값을 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        public string Value => value?.Trim() ?? string.Empty;

        [SerializeField]
        private string value;

        // ------------------------------------------------------------
        /// <summary>
        /// 유효한 Definition Key인지 확인한다.
        /// </summary>
        // ------------------------------------------------------------
        public bool IsValid => !string.IsNullOrWhiteSpace(Value);

        // ------------------------------------------------------------
        /// <summary>
        /// 유효한 문자열로 Ability Definition Key를 생성한다.
        /// </summary>
        // ------------------------------------------------------------
        public AbilityKey(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("Ability Key가 비어 있습니다.", nameof(value));
            }

            this.value = value.Trim();
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 정규화된 Definition Key 문자열을 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        public override string ToString() => Value;

    #endregion

    #region 동등성

        // ------------------------------------------------------------
        /// <summary>
        /// 다른 Definition Key와 값 동등성을 비교한다.
        /// </summary>
        // ------------------------------------------------------------
        public bool Equals(AbilityKey other) => string.Equals(Value, other.Value, StringComparison.Ordinal);

        // ------------------------------------------------------------
        /// <summary>
        /// 객체가 동일한 Definition Key인지 비교한다.
        /// </summary>
        // ------------------------------------------------------------
        public override bool Equals(object obj) => obj is AbilityKey other && Equals(other);

        // ------------------------------------------------------------
        /// <summary>
        /// 정규화된 Definition Key의 해시 코드를 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);

        // ------------------------------------------------------------
        /// <summary>
        /// 두 Definition Key의 값이 같은지 비교한다.
        /// </summary>
        // ------------------------------------------------------------
        public static bool operator ==(AbilityKey left, AbilityKey right) => left.Equals(right);

        // ------------------------------------------------------------
        /// <summary>
        /// 두 Definition Key의 값이 다른지 비교한다.
        /// </summary>
        // ------------------------------------------------------------
        public static bool operator !=(AbilityKey left, AbilityKey right) => !left.Equals(right);

    #endregion

    }

    // ============================================================
    /// <summary>
    /// AbilityBindings에서 현재 연결을 조회하는 Entry의 논리 Key.
    /// </summary>
    // ============================================================
    [Serializable]
    public struct AbilityEntryKey : IEquatable<AbilityEntryKey>
    {

    #region 값 및 생성자

        // ------------------------------------------------------------
        /// <summary>
        /// 앞뒤 공백을 정규화한 입력 Entry Key 값을 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        public string Value => value?.Trim() ?? string.Empty;

        [SerializeField]
        private string value;

        // ------------------------------------------------------------
        /// <summary>
        /// 유효한 입력 Entry Key인지 확인한다.
        /// </summary>
        // ------------------------------------------------------------
        public bool IsValid => !string.IsNullOrWhiteSpace(Value);

        // ------------------------------------------------------------
        /// <summary>
        /// 유효한 문자열로 입력 Entry Key를 생성한다.
        /// </summary>
        // ------------------------------------------------------------
        public AbilityEntryKey(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("Ability Entry Key가 비어 있습니다.", nameof(value));
            }

            this.value = value.Trim();
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 정규화된 Entry Key 문자열을 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        public override string ToString() => Value;

    #endregion

    #region 동등성

        // ------------------------------------------------------------
        /// <summary>
        /// 다른 Entry Key와 값 동등성을 비교한다.
        /// </summary>
        // ------------------------------------------------------------
        public bool Equals(AbilityEntryKey other) => string.Equals(Value, other.Value, StringComparison.Ordinal);

        // ------------------------------------------------------------
        /// <summary>
        /// 객체가 동일한 Entry Key인지 비교한다.
        /// </summary>
        // ------------------------------------------------------------
        public override bool Equals(object obj) => obj is AbilityEntryKey other && Equals(other);

        // ------------------------------------------------------------
        /// <summary>
        /// 정규화된 Entry Key의 해시 코드를 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);

        // ------------------------------------------------------------
        /// <summary>
        /// 두 Entry Key의 값이 같은지 비교한다.
        /// </summary>
        // ------------------------------------------------------------
        public static bool operator ==(AbilityEntryKey left, AbilityEntryKey right) => left.Equals(right);

        // ------------------------------------------------------------
        /// <summary>
        /// 두 Entry Key의 값이 다른지 비교한다.
        /// </summary>
        // ------------------------------------------------------------
        public static bool operator !=(AbilityEntryKey left, AbilityEntryKey right) => !left.Equals(right);

    #endregion

    }
}