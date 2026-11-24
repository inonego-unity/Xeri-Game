/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : LevelException.cs
수정일 : 2026-05-01

# 설명
Level 및 LevelxEXP에서 발생하는 예외 클래스 정의.
각 partial class 내부에 nested class로 선언한다.
========================================================================= BLOCK_HEADER_END */

using System;

namespace inonego.Xeri.Game
{
    // ==================================================================
    /// <summary>
    /// Level 클래스 내부의 Exception들을 정의합니다.
    /// </summary>
    // ==================================================================
    public partial class Level
    {
        // ==================================================================
        /// <summary>
        /// Level 관련 예외의 기본 클래스입니다.
        /// </summary>
        // ==================================================================
        [Serializable]
        public class LevelException : Exception
        {
            public LevelException() : base() { }
            public LevelException(string message) : base(message) { }
            public LevelException(string message, Exception innerException) : base(message, innerException) { }
        }

        // ==================================================================
        /// <summary>
        /// Level의 최대 레벨이 잘못되었을 때 발생하는 예외입니다.
        /// </summary>
        // ==================================================================
        [Serializable]
        public class InvalidMaxLevelException : LevelException
        {
            public InvalidMaxLevelException() : base("최대 레벨은 0 이상이어야 합니다.") { }
            public InvalidMaxLevelException(string message) : base(message) { }
        }
    }


    // ==================================================================
    /// <summary>
    /// LevelxEXP 클래스 내부의 Exception들을 정의합니다.
    /// </summary>
    // ==================================================================
    public partial class LevelxEXP
    {
        // ==================================================================
        /// <summary>
        /// LevelxEXP 관련 예외의 기본 클래스입니다.
        /// </summary>
        // ==================================================================
        [Serializable]
        public class LevelxEXPException : Exception
        {
            public LevelxEXPException() : base() { }
            public LevelxEXPException(string message) : base(message) { }
            public LevelxEXPException(string message, Exception innerException) : base(message, innerException) { }
        }

        // ==================================================================
        /// <summary>
        /// 경험치가 음수일 때 발생하는 예외입니다.
        /// </summary>
        // ==================================================================
        [Serializable]
        public class InvalidEXPException : LevelxEXPException
        {
            public InvalidEXPException() : base("경험치는 0 이상이어야 합니다.") { }
            public InvalidEXPException(string message) : base(message) { }
        }

        // ==================================================================
        /// <summary>
        /// 경험치 테이블이 null일 때 발생하는 예외입니다.
        /// </summary>
        // ==================================================================
        [Serializable]
        public class NullEXPTableException : LevelxEXPException
        {
            public NullEXPTableException() : base("경험치 테이블은 null일 수 없습니다.") { }
            public NullEXPTableException(string message) : base(message) { }
        }

        // ==================================================================
        /// <summary>
        /// 경험치 테이블에 음수 값이 포함되어 있을 때 발생하는 예외입니다.
        /// </summary>
        // ==================================================================
        [Serializable]
        public class InvalidEXPTableException : LevelxEXPException
        {
            public InvalidEXPTableException() : base("경험치 테이블의 모든 값은 0 이상이어야 합니다.") { }
            public InvalidEXPTableException(string message) : base(message) { }
        }

        // ==================================================================
        /// <summary>
        /// 최대 EXP 값이 잘못되었을 때 발생하는 예외입니다.
        /// </summary>
        // ==================================================================
        [Serializable]
        public class InvalidMaxEXPException : LevelxEXPException
        {
            public InvalidMaxEXPException() : base("최대 EXP 값은 0보다 작을 수 없습니다.") { }
            public InvalidMaxEXPException(string message) : base(message) { }
        }
    }
}
