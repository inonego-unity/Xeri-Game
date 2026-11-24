/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : ObjectiveCond.cs
수정일 : 2026-11-24

# 설명
Task·Eligibility에서 자주 쓰는 Objective용 표준 Condition을 선언형으로 제공한다.

# 특이사항, 제약사항
일부는 내부 구조를 알아 스스로 정적 검사를 선언한다(IValidatableCond, Entry 참조 등).
source를 지정하면 Directory에 등록한 이름 붙은 Fact 소스를, 비우면 이 실행의 Progress를 읽는다.
조합은 Core Cond.AND/OR/NOT을 쓴다.
========================================================================= BLOCK_HEADER_END */

using System;
using System.Collections;
using System.Collections.Generic;

using inonego;
using inonego.Xeri;
using inonego.Xeri.Serializable;

namespace inonego.Xeri.Game.Objective
{
    // ============================================================
    /// <summary>
    /// Task Condition을 위한 재사용 생성 API.
    /// </summary>
    // ============================================================
    public static class ObjectiveCond
    {

    #region Progress·Fact 값

        // ------------------------------------------------------------
        /// <summary>
        /// int 값이 value 이상이면 참. 값이 없으면 0으로 본다.
        /// </summary>
        // ------------------------------------------------------------
        public static ICond<ObjectiveContext> MinValueI(string key, int value, string source = null)
        {
            RequireKey(key);
            return new MinValueICond(key, value, source);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// float 값이 value 이상이면 참. 값이 없으면 0으로 본다.
        /// </summary>
        // ------------------------------------------------------------
        public static ICond<ObjectiveContext> MinValueF(string key, float value, string source = null)
        {
            RequireKey(key);
            if (float.IsNaN(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            return new MinValueFCond(key, value, source);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// flag가 켜져 있으면 참.
        /// </summary>
        // ------------------------------------------------------------
        public static ICond<ObjectiveContext> HasFlag(string key, string source = null)
        {
            RequireKey(key);
            return new HasFlagCond(key, source);
        }

        private static void RequireKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("Progress key must be specified.", nameof(key));
            }
        }

        // 소스를 비우면 이 실행의 Progress, 아니면 이름 붙은 Fact 소스를 읽는다.
        private static IValueLookup Values(ObjectiveContext context, string source) =>
            source == null ? context.Progress : context.Source(source);

        private sealed class MinValueICond : ICond<ObjectiveContext>
        {
            private readonly string key;
            private readonly int value;
            private readonly string source;

            internal MinValueICond(string key, int value, string source)
            {
                this.key = key;
                this.value = value;
                this.source = source;
            }

            // ------------------------------------------------------------
            /// <summary>
            /// int 값이 기준 이상인지 판정한다.
            /// </summary>
            // ------------------------------------------------------------
            public bool Check(ObjectiveContext context) => Values(context, source).GetI(key) >= value;
        }

        private sealed class MinValueFCond : ICond<ObjectiveContext>
        {
            private readonly string key;
            private readonly float value;
            private readonly string source;

            internal MinValueFCond(string key, float value, string source)
            {
                this.key = key;
                this.value = value;
                this.source = source;
            }

            // ------------------------------------------------------------
            /// <summary>
            /// float 값이 기준 이상인지 판정한다.
            /// </summary>
            // ------------------------------------------------------------
            public bool Check(ObjectiveContext context) => Values(context, source).GetF(key) >= value;
        }

        private sealed class HasFlagCond : ICond<ObjectiveContext>
        {
            private readonly string key;
            private readonly string source;

            internal HasFlagCond(string key, string source)
            {
                this.key = key;
                this.source = source;
            }

            // ------------------------------------------------------------
            /// <summary>
            /// flag가 켜져 있는지 판정한다.
            /// </summary>
            // ------------------------------------------------------------
            public bool Check(ObjectiveContext context) => Values(context, source).HasFlag(key);
        }

    #endregion

    #region 시간

        // ------------------------------------------------------------
        /// <summary>
        /// 이 실행의 누적 경과 시간이 seconds 이상이면 참.
        /// </summary>
        // ------------------------------------------------------------
        public static ICond<ObjectiveContext> MinElapsed(double seconds)
        {
            RequireSeconds(seconds);
            return new MinElapsedCond(seconds);
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/>시간을 재는 Entry가 참여를 시작한 뒤 seconds 이상 지났으면 참.
        /// <br/>참여 중이 아니면 거짓이다.
        /// <br/>그 Entry는 TracksOpenTime이 켜져 있어야 하며 Validator가 확인한다.
        /// </summary>
        // ----------------------------------------------------------------------
        public static ICond<ObjectiveContext> MinOpenElapsed(string entryID, double seconds)
        {
            if (string.IsNullOrWhiteSpace(entryID))
            {
                throw new ArgumentException("Entry ID must be specified.", nameof(entryID));
            }

            RequireSeconds(seconds);
            return new MinOpenElapsedCond(entryID, seconds);
        }

        private static void RequireSeconds(double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(seconds));
            }
        }

        private sealed class MinElapsedCond : ICond<ObjectiveContext>
        {
            private readonly double seconds;

            internal MinElapsedCond(double seconds)
            {
                this.seconds = seconds;
            }

            // ------------------------------------------------------------
            /// <summary>
            /// 누적 경과 시간이 기준 이상인지 판정한다.
            /// </summary>
            // ------------------------------------------------------------
            public bool Check(ObjectiveContext context) => context.ElapsedTime >= seconds;
        }

        private sealed class MinOpenElapsedCond : ICond<ObjectiveContext>, IValidatableCond
        {
            private readonly string entryID;
            private readonly double seconds;

            internal MinOpenElapsedCond(string entryID, double seconds)
            {
                this.entryID = entryID;
                this.seconds = seconds;
            }

            // ------------------------------------------------------------
            /// <summary>
            /// Entry가 참여를 시작한 뒤 기준 시간이 지났는지 판정한다.
            /// </summary>
            // ------------------------------------------------------------
            public bool Check(ObjectiveContext context) =>
                context.TryGetOpenElapsed(entryID, out double elapsed) && elapsed >= seconds;

            // --------------------------------------------------------------------------------
            /// <summary>
            /// <br/>가리키는 Entry가 있고 TracksOpenTime을 켰는지 확인한다.
            /// <br/>Entry의 Eligibility가 그 Entry나 하위 Entry의 시각을 읽으면 참여 여부가
            /// <br/>Eligibility에 되먹임되어 열림을 정할 수 없으므로 거부한다.
            /// </summary>
            // --------------------------------------------------------------------------------
            public void Validate(CondValidateScope scope)
            {
                if (!scope.EntryIDs.Contains(entryID))
                {
                    scope.Add($"MinOpenElapsed refers to unknown Entry '{entryID}'.");
                }
                else if (!scope.TimedEntryIDs.Contains(entryID))
                {
                    scope.Add($"MinOpenElapsed refers to Entry '{entryID}' which does not set TracksOpenTime.");
                }
                else if (scope.OwnerEntryIDs.Contains(entryID))
                {
                    scope.Add($"MinOpenElapsed in the Eligibility of an Entry refers to Entry '{entryID}', which is that Entry or inside it, so the open time depends on the Eligibility itself.");
                }
            }
        }

    #endregion

    #region 다른 Objective

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/>다른 Objective의 현재 회차가 해당 상태이면 참.
        /// <br/>scopeID를 비우면 같은 Scope다.
        /// <br/>없으면 거짓이다.
        /// <br/>의존을 선언해 두는 것이 아니라 평가할 때 현재 상태를 읽으며, 읽은 대상이 바뀌면 다시
        /// <br/>평가한다.
        /// </summary>
        // --------------------------------------------------------------------------------
        public static ICond<ObjectiveContext> DependsOn(string specID, ObjectiveStatus status, string scopeID = null)
        {
            if (string.IsNullOrWhiteSpace(specID))
            {
                throw new ArgumentException("Spec ID must be specified.", nameof(specID));
            }

            if (!Enum.IsDefined(typeof(ObjectiveStatus), status))
            {
                throw new ArgumentOutOfRangeException(nameof(status));
            }

            return new DependsOnCond(specID, status, scopeID);
        }

        private sealed class DependsOnCond : ICond<ObjectiveContext>
        {
            private readonly string specID;
            private readonly ObjectiveStatus status;
            private readonly string scopeID;

            internal DependsOnCond(string specID, ObjectiveStatus status, string scopeID)
            {
                this.specID = specID;
                this.status = status;
                this.scopeID = scopeID;
            }

            // ------------------------------------------------------------
            /// <summary>
            /// 다른 Objective의 현재 회차가 기대한 상태인지 판정한다.
            /// </summary>
            // ------------------------------------------------------------
            public bool Check(ObjectiveContext context)
            {
                bool found = scopeID == null
                    ? context.TryGetObjective(specID, out var other)
                    : context.TryGetObjective(scopeID, specID, out other);
                return found && other.Status == status;
            }
        }

    #endregion

    }
}
