/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : MotionModifierCommand.cs
수정일 : 2026-11-22

# 설명
채널별 MValue에 Order 순서로 접히는 Modifier 항목의 적용·종료·전체 제거 명령을 정의한다.
대시(SET), 넉백·바람(ADD·SUB), 둔화·스턴(MUL), 한 Tick 충격(Impulse)을 모두 같은 명령으로 표현한다.

# 특이사항, 제약사항
값은 사용처가 만든 결과이며 Motion은 값을 만들거나 해석하지 않는다.
수명(지속 시간), 시간에 따라 바뀌는 값(Track 재생, 감쇠)은 사용처가 갖고 매 Tick Apply로 갱신하거나 End로 끝낸다.
무엇이 무엇을 가리는지는 Order로만 정해진다. 효과의 의미와 Order 배치는 사용처 책임이다.
========================================================================= BLOCK_HEADER_END */

using System;

using UnityEngine;

using inonego;
using inonego.Xeri;
using inonego.Xeri.Serializable;

namespace inonego.Xeri.Game.Motion
{
    // ============================================================
    /// <summary>
    /// Modifier 항목의 적용·종료·전체 제거 명령.
    /// </summary>
    // ============================================================
    public readonly struct MotionModifierCommand
    {

    #region 필드

        // ------------------------------------------------------------
        /// <summary>
        /// 명령 동작.
        /// </summary>
        // ------------------------------------------------------------
        public MotionCommandAction Action { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 항목 키와 수명 세대.
        /// </summary>
        // ------------------------------------------------------------
        public MotionSourceKey Source { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 적용 순서. 클수록 나중에 적용되고 같으면 나중에 시작한 항목이 뒤다.
        /// </summary>
        // ------------------------------------------------------------
        public int Order { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 채널 값에 적용할 연산.
        /// </summary>
        // ------------------------------------------------------------
        public NumericFOperation Operation { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 적용 채널과 채널별 값.
        /// </summary>
        // ------------------------------------------------------------
        public MotionChannelValues Values { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 적용 채널.
        /// </summary>
        // ------------------------------------------------------------
        public MotionChannel Channels => Values.Channels;

        // ----------------------------------------------------------------------
        /// <summary>
        /// 사용처가 해석하는 동작 식별자. 보이는 맨 위 SET 항목의 값이 Frame에 실린다.
        /// </summary>
        // ----------------------------------------------------------------------
        public int ActionID { get; }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// 한 Tick만 적용하고 Free 채널에서는 Body 속도를 한 번만 바꾸는 충격인지 나타낸다.
        /// </summary>
        // --------------------------------------------------------------------------------
        public bool IsImpulse { get; }

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> 종료 명령에서 이 항목이 Body에 남긴 속도를 그대로 둘지 나타낸다.
        /// <br/> Free 채널에 되돌리는 충격을 만들지 않는다. Set 채널은 Set 값이 정한다.
        /// </summary>
        // ----------------------------------------------------------------------
        public bool KeepVelocity { get; }

    #endregion

    #region 생성

        private MotionModifierCommand
        (
            MotionCommandAction action, MotionSourceKey source, int order, NumericFOperation operation,
            in MotionChannelValues values, int actionID, bool isImpulse, bool keepVelocity = false
        )
        {
            Action = action;
            Source = source;
            Order = order;
            Operation = operation;
            Values = values;
            ActionID = actionID;
            IsImpulse = isImpulse;
            KeepVelocity = keepVelocity;
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> 항목을 시작하거나 갱신한다.
        /// <br/> 같은 키가 없으면 시작하고, 같은 세대면 시작 순서·시작 Tick을 유지한 채
        /// <br/> 연산과 값을 바꾸며, 더 큰 세대면 이전 항목을 대신한다.
        /// </summary>
        // ----------------------------------------------------------------------
        public static MotionModifierCommand Apply
        (
            MotionSourceKey source, int order, NumericFOperation operation,
            in MotionChannelValues values, int actionID = 0
        ) => new(MotionCommandAction.Apply, source, order, operation, values, actionID, false);

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> 한 Tick만 적용하는 충격 ADD 항목을 넣는다.
        /// <br/> 뒤에 보이는 SET이 있으면 가려진다.
        /// </summary>
        // ------------------------------------------------------------
        public static MotionModifierCommand Impulse(MotionSourceKey source, int order, in MotionChannelValues values) =>
            new(MotionCommandAction.Apply, source, order, NumericFOperation.ADD, values, 0, true);

        // ------------------------------------------------------------
        /// <summary>
        /// 같은 키·Generation의 항목을 끝낸다.
        /// </summary>
        // ------------------------------------------------------------
        public static MotionModifierCommand End(MotionSourceKey source, bool keepVelocity = false) =>
            new(MotionCommandAction.End, source, 0, NumericFOperation.SET, default, 0, false, keepVelocity);

        // ------------------------------------------------------------
        /// <summary>
        /// 모든 항목을 끝낸다.
        /// </summary>
        // ------------------------------------------------------------
        public static MotionModifierCommand Clear() =>
            new(MotionCommandAction.Clear, default, 0, NumericFOperation.SET, default, 0, false);

    #endregion

    }
}
