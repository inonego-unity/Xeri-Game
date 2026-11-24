/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : ValidationInput.cs
수정일 : 2026-11-22

# 설명
검증 씬이 쓰는 키보드 입력을 Input System에서 읽는 얇은 도구.

# 특이사항, 제약사항
KnackH가 Input System만 쓰므로 레거시 Input은 쓰지 않는다.
키 눌림은 프레임 단위(Update)로, 누르고 있는 상태는 Tick마다 읽는다.
========================================================================= BLOCK_HEADER_END */

using UnityEngine;
using UnityEngine.InputSystem;

namespace inonego.Xeri.Game.Validation.Locomotion
{
    // ============================================================
    /// <summary>
    /// 키보드 입력 도우미.
    /// </summary>
    // ============================================================
    internal static class ValidationInput
    {

    #region 내부 데이터

        private static readonly Key[] digits =
        {
            Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4, Key.Digit5,
            Key.Digit6, Key.Digit7, Key.Digit8, Key.Digit9
        };

    #endregion

    #region 키 상태

        // ------------------------------------------------------------
        /// <summary>
        /// 이번 프레임에 키를 눌렀는지 확인한다.
        /// </summary>
        // ------------------------------------------------------------
        public static bool Pressed(Key key)
        {
            var keyboard = Keyboard.current;

            return keyboard != null && keyboard[key].wasPressedThisFrame;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 키를 누르고 있는지 확인한다.
        /// </summary>
        // ------------------------------------------------------------
        public static bool Held(Key key)
        {
            var keyboard = Keyboard.current;

            return keyboard != null && keyboard[key].isPressed;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// WASD를 월드 평면 방향(x, z)으로 읽는다. 누른 키가 없으면 영벡터다.
        /// </summary>
        // ------------------------------------------------------------
        public static Vector3 Move()
        {
            var x = (Held(Key.D) ? 1f : 0f) - (Held(Key.A) ? 1f : 0f);
            var z = (Held(Key.W) ? 1f : 0f) - (Held(Key.S) ? 1f : 0f);
            var move = new Vector3(x, 0f, z);

            return move.sqrMagnitude > 1f ? move.normalized : move;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 숫자 키 1~9의 눌림을 읽는다. 눌린 키가 없으면 0이다.
        /// </summary>
        // ------------------------------------------------------------
        public static int Digit()
        {
            for (var index = 0; index < digits.Length; index++)
            {
                if (Pressed(digits[index]))
                {
                    return index + 1;
                }
            }

            return 0;
        }

    #endregion

    }
}
