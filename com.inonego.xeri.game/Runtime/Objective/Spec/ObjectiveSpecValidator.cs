/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : ObjectiveSpecValidator.cs
수정일 : 2026-11-25

# 설명
Objective 등록 전 Root/Entry/Progress/정적 정책 오류를 거부한다.

# 특이사항, 제약사항
임의 사용자 ICond의 내부 부작용·외부 Fact 의존성은 정적 분석하지 않는다.
트리 순회는 SpecWalker를 상속한 ValidationWalk가 하고, 정적 검사를 선언한 Condition(IValidatableCond)은 Core 논리 조합(AND/OR/NOT) 안쪽까지 방문해 스스로 참조와 임계값을 검사한다.
첫 오류에서 멈추지 않고 트리 전체의 오류를 경로(NodeID/EntryID)와 함께 모은다. Validate는 모인 오류를 한 번에 던진다.
========================================================================= BLOCK_HEADER_END */

using System;
using System.Collections;
using System.Collections.Generic;

using inonego;
using inonego.Xeri;
using inonego.Xeri.Game;

namespace inonego.Xeri.Game.Objective
{
    // ============================================================
    /// <summary>
    /// 정의의 식별자와 Composite 구조 불변식을 검사한다.
    /// </summary>
    // ============================================================
    public static class ObjectiveSpecValidator
    {

    #region 검증 진입

        // ------------------------------------------------------------
        /// <summary>
        /// 잘못된 정의를 등록하기 전에 모든 오류를 모아 한 번에 예외로 거부한다.
        /// </summary>
        // ------------------------------------------------------------
        public static void Validate(ObjectiveSpec spec)
        {
            // Spec은 불변이므로 한 번 통과하면 다시 검증하지 않는다.
            if (spec != null && spec.IsValidated)
            {
                return;
            }

            var issues = Collect(spec);
            if (issues.Count > 0)
            {
                string header = $"Objective spec '{spec.SpecID}' (version {spec.Version}) has {issues.Count} issue(s):";
                throw new ArgumentException(header + Environment.NewLine + string.Join(Environment.NewLine, issues));
            }

            spec.IsValidated = true;
        }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/>정의의 모든 오류를 던지지 않고 모아 돌려준다. 각 항목은 "경로: 내용" 형식이다.
        /// <br/>경로는 NodeID를 /로 잇고, Entry 항목은 부모 경로 뒤에 #EntryID를 붙인다.
        /// </summary>
        // --------------------------------------------------------------------------------
        public static IReadOnlyList<string> Collect(ObjectiveSpec spec)
        {
            if (spec == null)
            {
                throw new ArgumentNullException(nameof(spec));
            }

            var walk = new ValidationWalk();
            if (string.IsNullOrWhiteSpace(spec.SpecID))
            {
                walk.Add("spec", "SpecID must not be blank.");
            }

            if (spec.Version <= 0)
            {
                walk.Add("spec", $"Version must be positive but was {spec.Version}.");
            }

            if (!spec.SuccessCommitMode.IsDefined()
                || !spec.FailureCommitMode.IsDefined())
            {
                walk.Add("spec", "Commit mode is not a defined value.");
            }

            if (!spec.RepeatMode.IsDefined())
            {
                walk.Add("spec", "Repeat mode is not a defined value.");
            }

            if (spec.Root == null)
            {
                walk.Add("spec", "Root node is required.");
            }
            else
            {
                walk.Walk(spec.Root, SpecWalker.Name(spec.Root.NodeID));
                walk.ValidateConds();
            }

            return walk.Issues;
        }

    #endregion

    #region 트리 검증

        // 한 번의 순회에서 규칙을 검사하고 오류를 모은다. 순회는 SpecWalker가 한다.
        private sealed class ValidationWalk : SpecWalker
        {

        #region 상태

            internal List<string> Issues { get; } = new();

            private readonly HashSet<string> nodeIDs = new(StringComparer.Ordinal);
            private readonly HashSet<string> entryIDs = new(StringComparer.Ordinal);
            private readonly HashSet<string> ownedProgress = new(StringComparer.Ordinal);
            private readonly HashSet<string> timedEntries = new(StringComparer.Ordinal);
            private readonly List<string> entryOrder = new();
            private readonly List<CondSite> sites = new();

            internal void Add(string path, string message) => Issues.Add($"{path}: {message}");

        #endregion

        #region 노드 규칙

            protected override void Enter(TaskNodeSpec node, string path)
            {
                if (string.IsNullOrWhiteSpace(node.NodeID))
                {
                    Add(path, "NodeID must not be blank.");
                }
                else if (!nodeIDs.Add(node.NodeID))
                {
                    Add(path, $"NodeID '{node.NodeID}' is duplicated.");
                }
            }

            protected override void VisitTask(TaskSpec task, string path)
            {
                if (task.SuccessCond == null)
                {
                    Add(path, "Task SuccessCond is required.");
                }

                Queue(path, task.SuccessCond, null, 0, null);
                Queue(path, task.FailureCond, null, 0, null);

                foreach (var id in task.LocalProgressIDs)
                {
                    if (string.IsNullOrWhiteSpace(id))
                    {
                        Add(path, "Local Progress ID must not be blank.");
                    }
                    else if (!ownedProgress.Add(id))
                    {
                        Add(path, $"Local Progress ID '{id}' is owned by more than one Task.");
                    }
                }
            }

            protected override void VisitGroup(TaskGroupSpec group, string path)
            {
                if (group.SuccessCond == null)
                {
                    Add(path, "Group SuccessCond is required.");
                }

                if (group.Entries.Count == 0)
                {
                    Add(path, "Group needs at least one Entry.");
                }

                if (!group.ExecutionMode.IsDefined()
                    || !group.ContinuationMode.IsDefined())
                {
                    Add(path, "Group execution or continuation mode is not a defined value.");
                }

                // 지정한 Entry와 임계값은 직접 자식 기준이므로 자식 ID를 함께 넘겨 순회 뒤 검사한다.
                var children = new HashSet<string>(StringComparer.Ordinal);
                foreach (var entry in group.Entries)
                {
                    if (entry != null && !string.IsNullOrWhiteSpace(entry.EntryID))
                    {
                        children.Add(entry.EntryID);
                    }
                }

                Queue(path, group.SuccessCond, children, group.Entries.Count, null);
                Queue(path, group.FailureCond, children, group.Entries.Count, null);

                base.VisitGroup(group, path);
            }

            protected override void VisitEntry(TaskGroupSpec group, TaskChildEntry entry, int index, string path)
            {
                string entryPath = EntryPath(path, entry);
                if (string.IsNullOrWhiteSpace(entry.EntryID))
                {
                    Add(entryPath, "EntryID must not be blank.");
                }
                else if (!entryIDs.Add(entry.EntryID))
                {
                    Add(entryPath, $"EntryID '{entry.EntryID}' is duplicated.");
                }

                if (!entry.RevalidationMode.IsDefined()
                    || !entry.RollbackMode.IsDefined())
                {
                    Add(entryPath, "Entry revalidation or rollback mode is not a defined value.");
                }

                if (entry.TracksOpenTime && !string.IsNullOrWhiteSpace(entry.EntryID))
                {
                    timedEntries.Add(entry.EntryID);
                }

                if (group.ExecutionMode == TaskExecutionMode.Parallel
                    && (entry.RevalidationMode != TaskRevalidationMode.WhileValid
                        || entry.RollbackMode != TaskRollbackMode.Preserve))
                {
                    Add(entryPath, "Sequential-only Entry policy is not valid in a Parallel Group.");
                }

                // 하위 Entry는 이 Entry를 방문하는 동안 순서대로 이어서 쌓이므로 그 구간이 이 Entry와 모든 하위 Entry다.
                int begin = entryOrder.Count;
                if (!string.IsNullOrWhiteSpace(entry.EntryID))
                {
                    entryOrder.Add(entry.EntryID);
                }

                Descend(entry, path);

                var owned = new HashSet<string>(entryOrder.GetRange(begin, entryOrder.Count - begin), StringComparer.Ordinal);
                Queue(entryPath, entry.EligibilityCond, null, 0, owned);
            }

        #endregion

        #region 구조 오류

            protected override void OnNullNode(string path) => Add(path, "Task node is null.");

            protected override void OnCycle(TaskNodeSpec node, string path) => Add(path, "TaskGroup spec is cyclic.");

            protected override void OnShared(TaskNodeSpec node, string path) =>
                Add(path, "Task node instance is shared by more than one Entry.");

            protected override void OnUnknown(TaskNodeSpec node, string path) => Add(path, "Task node type is unknown.");

            protected override void OnNullEntry(int index, string path) => Add($"{path}#[{index}]", "ChildEntry is null.");

        #endregion

        #region 검사 대기

            // Condition 트리를 펼쳐 정적 검사를 선언한 Condition마다 놓인 위치와 함께 대기열에 둔다. 검사는 순회가 끝난 뒤에 한다.
            private void Queue<T>
            (
                string path, ICond<T> cond, HashSet<string> childEntryIDs, int childCount, HashSet<string> ownerEntryIDs
            )
            {
                foreach (var inner in Flatten(cond))
                {
                    if (inner is IValidatableCond validatable)
                    {
                        sites.Add(new CondSite(path, validatable, childEntryIDs, childCount, ownerEntryIDs));
                    }
                }
            }

            // 순회 뒤에 검사할 Condition 하나와 그 Condition이 놓인 위치.
            private sealed class CondSite
            {
                internal string Path { get; }
                internal IValidatableCond Cond { get; }
                internal HashSet<string> ChildEntryIDs { get; }
                internal int ChildCount { get; }
                internal HashSet<string> OwnerEntryIDs { get; }

                internal CondSite
                (
                    string path, IValidatableCond cond, HashSet<string> childEntryIDs,
                    int childCount, HashSet<string> ownerEntryIDs
                )
                {
                    Path = path;
                    Cond = cond;
                    ChildEntryIDs = childEntryIDs;
                    ChildCount = childCount;
                    OwnerEntryIDs = ownerEntryIDs;
                }
            }

        #endregion

        #region 순회 뒤 검사

            // 모은 Entry 정보를 범위로 만들어 대기열의 Condition이 스스로 검사하게 한다.
            internal void ValidateConds()
            {
                var none = new HashSet<string>(StringComparer.Ordinal);
                foreach (var site in sites)
                {
                    var scope = new CondValidateScope
                    (
                        site.Path, entryIDs, timedEntries, site.ChildEntryIDs ?? none, site.ChildCount,
                        site.OwnerEntryIDs ?? none, Add
                    );
                    site.Cond.Validate(scope);
                }
            }

        #endregion

        }

    #endregion

    #region 공용 보조

        // Core 논리 조합(AND/OR/NOT) 안쪽까지 내려가며 모든 Condition을 순서대로 돌려준다.
        private static IEnumerable<ICond<T>> Flatten<T>(ICond<T> cond)
        {
            if (cond == null)
            {
                yield break;
            }

            yield return cond;
            switch (cond)
            {
                case Cond_AND<T> both:
                    foreach (var inner in Flatten(both.Left))
                    {
                        yield return inner;
                    }

                    foreach (var inner in Flatten(both.Right))
                    {
                        yield return inner;
                    }

                    break;
                case Cond_OR<T> either:
                    foreach (var inner in Flatten(either.Left))
                    {
                        yield return inner;
                    }

                    foreach (var inner in Flatten(either.Right))
                    {
                        yield return inner;
                    }

                    break;
                case Cond_NOT<T> negated:
                    foreach (var inner in Flatten(negated.Condition))
                    {
                        yield return inner;
                    }

                    break;
            }
        }

    #endregion

    }
}
