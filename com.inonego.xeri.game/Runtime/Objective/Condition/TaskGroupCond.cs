/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : TaskGroupCond.cs
수정일 : 2026-11-25

# 설명
Group 자식 완료·실패 집계를 조합하는 최소 기본 Condition을 제공한다.

# 특이사항, 제약사항
참여하지 않는 Entry는 완료·실패 수에 기여하지 않는다. 모든 집계는 기본적으로 직접 자식 전체를 대상으로 한다.
entryIDs를 주면 지정한 직접 자식만 대상으로 삼고, 나머지 자식의 결과는 보지 않는다(필수·선택 목표가 섞인 Group).
지정한 ID가 직접 자식인지와 임계값이 대상 자식 수를 넘지 않는지는 Validator가 정적으로 확인한다(IValidatableCond).
실패는 자동 전파하지 않으므로 실패 보조도 저자가 FailureCond에 명시해 쓴다.
========================================================================= BLOCK_HEADER_END */

using System;
using System.Collections;
using System.Collections.Generic;

using inonego;
using inonego.Xeri;

namespace inonego.Xeri.Game.Objective
{
    // ============================================================
    /// <summary>
    /// 재귀 Group 완료 판정을 위한 재사용 Condition 생성 API.
    /// </summary>
    // ============================================================
    public static class TaskGroupCond
    {

    #region 기본 집계

        // ------------------------------------------------------------
        /// <summary>
        /// <br/>대상 자식이 모두 참여하면서 Succeeded이면 참.
        /// <br/>entryIDs를 비우면 등록된 모든 직접 자식이 대상이다.
        /// <br/>entryIDs를 주면 지정한 직접 자식만 보고 나머지는 보지 않는다.
        /// </summary>
        // ------------------------------------------------------------
        public static ICond<TaskGroupContext> All(params string[] entryIDs) => new AllSucceededCond(Scope(entryIDs));

        // ------------------------------------------------------------
        /// <summary>
        /// <br/>대상 자식 중 유효한 Succeeded가 하나 이상 있으면 참.
        /// <br/>entryIDs를 주면 지정한 직접 자식만 대상이다.
        /// </summary>
        // ------------------------------------------------------------
        public static ICond<TaskGroupContext> Any(params string[] entryIDs) => MinSucceeded(1, entryIDs);

        // ------------------------------------------------------------
        /// <summary>
        /// <br/>대상 자식 중 유효 완료한 수가 threshold 이상이면 참.
        /// <br/>entryIDs를 주면 지정한 직접 자식만 대상이다.
        /// </summary>
        // ------------------------------------------------------------
        public static ICond<TaskGroupContext> MinSucceeded(int threshold, params string[] entryIDs)
        {
            if (threshold <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(threshold));
            }

            return new MinSucceededCond(Scope(entryIDs), threshold);
        }

        private sealed class AllSucceededCond : ScopedCond
        {
            internal AllSucceededCond(string[] entryIDs) : base(entryIDs)
            {
                // NONE
            }

            // ------------------------------------------------------------
            /// <summary>
            /// 모든 대상 자식이 참여하면서 완료됐는지 판정한다.
            /// </summary>
            // ------------------------------------------------------------
            public override bool Check(TaskGroupContext context)
            {
                Tally(context, out int size, out int succeeded, out _);
                return size > 0 && succeeded == size;
            }
        }

        private sealed class MinSucceededCond : ScopedCond
        {
            private readonly int threshold;

            internal MinSucceededCond(string[] entryIDs, int threshold) : base(entryIDs)
            {
                this.threshold = threshold;
            }

            // ------------------------------------------------------------
            /// <summary>
            /// 참여하는 대상 자식의 완료 수가 설정한 임계값 이상인지 판정한다.
            /// </summary>
            // ------------------------------------------------------------
            public override bool Check(TaskGroupContext context)
            {
                Tally(context, out _, out int succeeded, out _);
                return succeeded >= threshold;
            }

            // ------------------------------------------------------------
            /// <summary>
            /// 임계값이 대상 자식 수를 넘으면 영영 참이 될 수 없으므로 거부한다.
            /// </summary>
            // ------------------------------------------------------------
            protected override void ValidateThreshold(CondValidateScope scope, int targetCount)
            {
                if (threshold > targetCount)
                {
                    scope.Add($"MinSucceeded threshold {threshold} exceeds the {targetCount} child Entries.");
                }
            }
        }

    #endregion

    #region 실패 보조

        // ------------------------------------------------------------
        /// <summary>
        /// <br/>대상 자식 중 참여 중 실패한 수가 threshold 이상이면 참.
        /// <br/>entryIDs를 주면 지정한 직접 자식만 대상이다.
        /// </summary>
        // ------------------------------------------------------------
        public static ICond<TaskGroupContext> MinFailed(int threshold, params string[] entryIDs)
        {
            if (threshold <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(threshold));
            }

            return new MinFailedCond(Scope(entryIDs), threshold);
        }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/>아직 실패하지 않은 대상 자식이 required보다 적어 완료 수를 채울 수 없으면 참.
        /// <br/>비참여 자식은 나중에 참여할 수 있으므로 완료 가능으로 센다.
        /// <br/>경로가 정해지지 않은 분기에서 일찍 실패하지 않는다.
        /// <br/>entryIDs를 주면 지정한 직접 자식만 대상이다.
        /// </summary>
        // --------------------------------------------------------------------------------
        public static ICond<TaskGroupContext> Unreachable(int required, params string[] entryIDs)
        {
            if (required <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(required));
            }

            return new UnreachableCond(Scope(entryIDs), required);
        }

        private sealed class MinFailedCond : ScopedCond
        {
            private readonly int threshold;

            internal MinFailedCond(string[] entryIDs, int threshold) : base(entryIDs)
            {
                this.threshold = threshold;
            }

            // ------------------------------------------------------------
            /// <summary>
            /// 참여하는 대상 자식의 실패 수가 설정한 임계값 이상인지 판정한다.
            /// </summary>
            // ------------------------------------------------------------
            public override bool Check(TaskGroupContext context)
            {
                Tally(context, out _, out _, out int failed);
                return failed >= threshold;
            }

            // ------------------------------------------------------------
            /// <summary>
            /// 임계값이 대상 자식 수를 넘으면 영영 참이 될 수 없으므로 거부한다.
            /// </summary>
            // ------------------------------------------------------------
            protected override void ValidateThreshold(CondValidateScope scope, int targetCount)
            {
                if (threshold > targetCount)
                {
                    scope.Add($"MinFailed threshold {threshold} exceeds the {targetCount} child Entries.");
                }
            }
        }

        private sealed class UnreachableCond : ScopedCond
        {
            private readonly int required;

            internal UnreachableCond(string[] entryIDs, int required) : base(entryIDs)
            {
                this.required = required;
            }

            // ------------------------------------------------------------
            /// <summary>
            /// 실패하지 않은 대상 자식 수가 필요한 완료 수보다 적은지 판정한다.
            /// </summary>
            // ------------------------------------------------------------
            public override bool Check(TaskGroupContext context)
            {
                Tally(context, out int size, out _, out int failed);
                return size - failed < required;
            }

            // ------------------------------------------------------------
            /// <summary>
            /// 필요한 완료 수가 대상 자식 수를 넘으면 시작부터 참이므로 거부한다.
            /// </summary>
            // ------------------------------------------------------------
            protected override void ValidateThreshold(CondValidateScope scope, int targetCount)
            {
                if (required > targetCount)
                {
                    scope.Add($"Unreachable requirement {required} exceeds the {targetCount} child Entries.");
                }
            }
        }

    #endregion

    #region 대상 범위

        // 지정한 Entry ID를 검증해 복사한다. 비어 있으면 모든 직접 자식을 대상으로 한다.
        private static string[] Scope(string[] entryIDs)
        {
            if (entryIDs == null)
            {
                throw new ArgumentNullException(nameof(entryIDs));
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            var copy = new string[entryIDs.Length];
            for (int index = 0; index < entryIDs.Length; index++)
            {
                if (string.IsNullOrWhiteSpace(entryIDs[index]))
                {
                    throw new ArgumentException("Entry ID must not be blank.", nameof(entryIDs));
                }

                if (!seen.Add(entryIDs[index]))
                {
                    throw new ArgumentException($"Duplicate Entry ID '{entryIDs[index]}'.", nameof(entryIDs));
                }

                copy[index] = entryIDs[index];
            }

            return copy;
        }

        // 대상 범위를 가진 집계 Condition의 공통 기반. 지정한 Entry 참조와 임계값을 스스로 정적 검사한다.
        private abstract class ScopedCond : ICond<TaskGroupContext>, IValidatableCond
        {
            // 지정한 직접 자식 ID. 비어 있으면 모든 직접 자식이 대상이다.
            protected IReadOnlyList<string> EntryIDs { get; }

            protected ScopedCond(string[] entryIDs)
            {
                EntryIDs = Array.AsReadOnly(entryIDs);
            }

            // ------------------------------------------------------------
            /// <summary>
            /// 대상 Entry의 결과를 집계해 참거짓을 돌려준다.
            /// </summary>
            // ------------------------------------------------------------
            public abstract bool Check(TaskGroupContext context);

            // ----------------------------------------------------------------------
            /// <summary>
            /// 지정한 Entry가 Group의 직접 자식인지 확인하고 임계값을 대상 자식 수와 비교한다.
            /// </summary>
            // ----------------------------------------------------------------------
            public void Validate(CondValidateScope scope)
            {
                foreach (var id in EntryIDs)
                {
                    if (!scope.ChildEntryIDs.Contains(id))
                    {
                        scope.Add($"Group Condition refers to Entry '{id}' which is not a direct child of this Group.");
                    }
                }

                ValidateThreshold(scope, EntryIDs.Count > 0 ? EntryIDs.Count : scope.ChildCount);
            }

            // --------------------------------------------------------------------------------
            /// <summary>
            /// 임계값이 있는 집계가 대상 자식 수와 비교해 검사한다. 임계값이 없는 집계는 검사하지 않는다.
            /// </summary>
            // --------------------------------------------------------------------------------
            protected virtual void ValidateThreshold(CondValidateScope scope, int targetCount)
            {
                // NONE
            }

            // ------------------------------------------------------------
            /// <summary>
            /// 대상 자식 수, 참여하며 성공한 수, 참여하며 실패한 수를 센다.
            /// </summary>
            // ------------------------------------------------------------
            protected void Tally(TaskGroupContext context, out int size, out int succeeded, out int failed)
            {
                if (context == null)
                {
                    throw new ArgumentNullException(nameof(context));
                }

                if (EntryIDs.Count == 0)
                {
                    size = context.EntryCount;
                    succeeded = context.SucceededCount;
                    failed = context.FailedCount;
                    return;
                }

                size = EntryIDs.Count;
                succeeded = 0;
                failed = 0;
                foreach (var id in EntryIDs)
                {
                    if (context.TryGetEntry(id, out var entry) && entry.Participating)
                    {
                        if (entry.Result == TaskResult.Succeeded)
                        {
                            succeeded++;
                        }
                        else if (entry.Result == TaskResult.Failed)
                        {
                            failed++;
                        }
                    }
                }
            }
        }

    #endregion

    }
}
