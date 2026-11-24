/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : ValidationCheck.cs
수정일 : 2026-11-22

# 설명
카드가 매 Tick 자동으로 판정하는 점검 항목과 Core 연산 규칙으로 기대 Planar 값을 다시 계산하는 오라클.

# 특이사항, 제약사항
점검은 해당 상황이 아닐 때 NotApplicable로 건너뛰고, 결과 상태와 누적 통과·실패 횟수를 보여준다.
오라클은 Motion 구현이 아니라 Core NumericFOperation 규칙으로 계산해 접기 결과와 독립으로 비교한다.
========================================================================= BLOCK_HEADER_END */

using System;
using System.Collections.Generic;

using UnityEngine;

using inonego;
using inonego.Xeri;
using inonego.Xeri.Game;
using inonego.Xeri.Game.Motion;
using inonego.Xeri.Serializable;

namespace inonego.Xeri.Game.Validation.Locomotion
{
    // ============================================================
    /// <summary>
    /// 점검 결과 상태.
    /// </summary>
    // ============================================================
    internal enum ValidationCheckState { NotApplicable, Pass, Fail }

    // ============================================================
    /// <summary>
    /// 점검 한 번의 결과.
    /// </summary>
    // ============================================================
    internal readonly struct ValidationCheckResult
    {
        public readonly ValidationCheckState State;
        public readonly string Detail;

        private ValidationCheckResult(ValidationCheckState state, string detail)
        {
            State = state;
            Detail = detail;
        }

        public static ValidationCheckResult Skip => new ValidationCheckResult(ValidationCheckState.NotApplicable, null);

        public static ValidationCheckResult Pass(string detail = null) => new ValidationCheckResult(ValidationCheckState.Pass, detail);

        public static ValidationCheckResult Fail(string detail) => new ValidationCheckResult(ValidationCheckState.Fail, detail);

        public static ValidationCheckResult Of(bool ok, string detail) => ok ? Pass(detail) : Fail(detail);
    }

    // ============================================================
    /// <summary>
    /// 이름과 판정 함수를 가진 자동 점검 항목.
    /// </summary>
    // ============================================================
    internal sealed class ValidationCheck
    {

    #region 필드

        private readonly Func<ValidationCheckResult> evaluate;

        public string Name { get; }
        public ValidationCheckState State { get; private set; }
        public string Detail { get; private set; }
        public int PassCount { get; private set; }
        public int FailCount { get; private set; }
        public string LastFailure { get; private set; }

    #endregion

    #region 생성자

        // ------------------------------------------------------------
        /// <summary>
        /// 이름과 판정 함수로 점검을 만든다.
        /// </summary>
        // ------------------------------------------------------------
        public ValidationCheck(string name, Func<ValidationCheckResult> evaluate)
        {
            Name = name;
            this.evaluate = evaluate;
        }

    #endregion

    #region 판정

        // ------------------------------------------------------------
        /// <summary>
        /// 점검을 한 번 판정하고 횟수를 누적한다.
        /// </summary>
        // ------------------------------------------------------------
        public void Evaluate(long tick)
        {
            var result = evaluate();

            State = result.State;
            Detail = result.Detail;

            if (result.State == ValidationCheckState.Pass)
            {
                PassCount++;
            }
            else if (result.State == ValidationCheckState.Fail)
            {
                FailCount++;
                LastFailure = $"t{tick} {result.Detail}";
            }
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 누적 횟수와 마지막 실패를 지운다.
        /// </summary>
        // ------------------------------------------------------------
        public void ResetCounts()
        {
            State = ValidationCheckState.NotApplicable;
            Detail = null;
            PassCount = 0;
            FailCount = 0;
            LastFailure = null;
        }

    #endregion

    }

    // ============================================================
    /// <summary>
    /// Core 연산 규칙으로 Planar 합성 결과를 다시 계산하는 오라클.
    /// </summary>
    // ============================================================
    internal static class ValidationOracle
    {

    #region Planar 접기

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> 의도와 활성 효과를 Order 순서(같으면 시작 순서)로 접은 기대 Planar 값을 구한다.
        /// <br/> 의도도 SET 효과도 없으면 Free라 값이 없고 false다.
        /// </summary>
        // ------------------------------------------------------------
        public static bool TryExpectPlanar
        (
            IReadOnlyList<ValidationEffect> effects, bool hasIntent, Vector3 intent, Vector3 up, out Vector3 expected
        )
        {
            var active = new List<ValidationEffect>();
            var hasSet = false;

            for (var index = 0; index < effects.Count; index++)
            {
                var effect = effects[index];

                if (effect.IsActive && (effect.Channels & MotionChannel.Planar) != 0)
                {
                    active.Add(effect);
                    hasSet |= effect.Operation == NumericFOperation.SET;
                }
            }

            expected = Vector3.zero;

            if (!hasIntent && !hasSet)
            {
                return false;
            }

            active.Sort(Compare);

            var value = hasIntent ? Project(intent, up) : Vector3.zero;

            for (var index = 0; index < active.Count; index++)
            {
                value = active[index].Operation.Apply(value, active[index].PlanarOperand);
            }

            expected = Project(value, up);

            return true;
        }

        private static int Compare(ValidationEffect a, ValidationEffect b) =>
            a.Order != b.Order ? a.Order.CompareTo(b.Order) : a.Sequence.CompareTo(b.Sequence);

        // ------------------------------------------------------------
        /// <summary>
        /// 벡터에서 Up 방향 성분을 뺀다.
        /// </summary>
        // ------------------------------------------------------------
        public static Vector3 Project(Vector3 value, Vector3 up) => value - up * Vector3.Dot(value, up);

    #endregion

    }
}
