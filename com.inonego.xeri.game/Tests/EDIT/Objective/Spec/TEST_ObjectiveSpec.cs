/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : TEST_ObjectiveSpec.cs
수정일 : 2026-11-25

# 설명
재귀 Objective Spec, Registry 및 Validator의 정적 실행 계약을 검증한다.

# 특이사항, 제약사항
Group 실행/Flow 평가가 아니라 구성·등록·Condition 집계의 입력 계약을 검증한다.
========================================================================= BLOCK_HEADER_END */

using System;
using System.Collections;
using System.Collections.Generic;

using NUnit;
using NUnit.Framework;

using inonego;
using inonego.Xeri;
using inonego.Xeri.Game;
using inonego.Xeri.Game.Progress;
using inonego.Xeri.Game.Objective;

namespace inonego.Xeri.Game.TEST.Objective
{
    // ============================================================
    /// <summary>
    /// 정적 Composite Spec과 등록의 불변식 테스트.
    /// </summary>
    // ============================================================
    public sealed class TEST_ObjectiveSpec
    {

    #region 테스트 자료

        private sealed class Accepted : ICond<ObjectiveContext>
        {
            // ------------------------------------------------------------
            /// <summary>
            /// 읽기 전용 평가 Context의 존재 여부만 검사한다.
            /// </summary>
            // ------------------------------------------------------------
            public bool Check(ObjectiveContext context) => context != null;
        }

        private static readonly ICond<ObjectiveContext> acceptedCond = new Accepted();

        private static TaskSpec Task(string id, params string[] owned) =>
            new(id, acceptedCond, localProgressIDs: owned);

        private static TaskGroupSpec Parallel(params TaskChildEntry[] entries) =>
            new("root", TaskExecutionMode.Parallel, TaskGroupCond.All(), entries);

        private static ObjectiveSpec Wrap(TaskNodeSpec root) =>
            new("test", 1, root);

        private static ObjectiveSpec Valid(int version = 1)
        {
            var inner = new TaskChildEntry[]
            {
                new("a", Task("task-a", "counter")),
                new("b", Task("task-b")),
                new("c", Task("task-c"))
            };
            var abc = new TaskGroupSpec
            (
                "group-abc", TaskExecutionMode.Parallel, TaskGroupCond.MinSucceeded(2),
                inner, TaskContinuationMode.ContinueWhileActive
            );
            var outer = new TaskChildEntry[]
            {
                new("group-entry", abc, revalidationMode: TaskRevalidationMode.OncePassed),
                new("report-entry", Task("report", "manual"), rollbackMode: TaskRollbackMode.ResetOnRollback)
            };
            var root = new TaskGroupSpec("root", TaskExecutionMode.Sequential, TaskGroupCond.All(), outer);
            var progress = new string[]
            {
                "counter",
                "manual"
            };
            return new ObjectiveSpec
            (
                "s04", version, root,
                failureCommitMode: ObjectiveCommitMode.Manual,
                repeatMode: ObjectiveRepeatMode.Repeatable
            );
        }

    #endregion

    #region 정의·소유권

        // ------------------------------------------------------------
        /// <summary>
        /// 중첩 Group과 순서 정책·성공 확정 정책을 등록할 수 있다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveSpec_중첩Composite정책을등록한다()
        {
            var spec = Valid();
            ObjectiveSpecValidator.Validate(spec);
            var root = (TaskGroupSpec)spec.Root;
            var inner = (TaskGroupSpec)root.Entries[0].Node;

            Assert.That(root.ExecutionMode, Is.EqualTo(TaskExecutionMode.Sequential));
            Assert.That(root.Entries[0].RevalidationMode, Is.EqualTo(TaskRevalidationMode.OncePassed));
            Assert.That(root.Entries[1].RollbackMode, Is.EqualTo(TaskRollbackMode.ResetOnRollback));
            Assert.That(inner.ContinuationMode, Is.EqualTo(TaskContinuationMode.ContinueWhileActive));
            Assert.That(spec.FailureCommitMode, Is.EqualTo(ObjectiveCommitMode.Manual));
            Assert.That(spec.RepeatMode, Is.EqualTo(ObjectiveRepeatMode.Repeatable));
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Group Entry 목록의 방어 복사가 원본 변경을 막는다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveSpec_리스트방어복사()
        {
            var entries = new List<TaskChildEntry>();
            entries.Add(new TaskChildEntry("a", Task("task", "counter")));
            var group = new TaskGroupSpec("root", TaskExecutionMode.Parallel, TaskGroupCond.All(), entries);
            var spec = new ObjectiveSpec("test", 1, group);
            entries.Clear();

            Assert.That(group.Entries.Count, Is.EqualTo(1));
            Assert.Throws<NotSupportedException>(() => ((IList<TaskChildEntry>)group.Entries).Clear());
            ObjectiveSpecValidator.Validate(spec);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 누락 Root/Condition과 자식이 없는 Group을 거부한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveSpec_필수Root와Condition()
        {
            var empty = new ObjectiveSpec("none", 1, null);
            var missing = Wrap(new TaskSpec("task", null));
            var emptyGroup = Wrap(Parallel(Array.Empty<TaskChildEntry>()));

            Assert.Throws<ArgumentException>(() => ObjectiveSpecValidator.Validate(empty));
            Assert.Throws<ArgumentException>(() => ObjectiveSpecValidator.Validate(missing));
            Assert.Throws<ArgumentException>(() => ObjectiveSpecValidator.Validate(emptyGroup));
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 중복 NodeID·EntryID 또는 같은 Node 객체 공유를 거부한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveSpec_중복과공유Node거부()
        {
            var duplicateNodes = Parallel(new TaskChildEntry("a", Task("same")), new TaskChildEntry("b", Task("same")));
            var duplicateEntries = Parallel(new TaskChildEntry("a", Task("first")), new TaskChildEntry("a", Task("second")));
            var shared = Task("shared");
            var sharedNodes = Parallel(new TaskChildEntry("a", shared), new TaskChildEntry("b", shared));

            Assert.Throws<ArgumentException>(() => ObjectiveSpecValidator.Validate(Wrap(duplicateNodes)));
            Assert.Throws<ArgumentException>(() => ObjectiveSpecValidator.Validate(Wrap(duplicateEntries)));
            Assert.Throws<ArgumentException>(() => ObjectiveSpecValidator.Validate(Wrap(sharedNodes)));
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 동일한 Registry Key를 여러 Task가 소유하면 후퇴 정책이 모호해진다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveSpec_로컬Progress소유권검사()
        {
            var shared = Parallel(new TaskChildEntry("a", Task("a", "x")), new TaskChildEntry("b", Task("b", "x")));
            Assert.Throws<ArgumentException>(() => ObjectiveSpecValidator.Validate(new ObjectiveSpec("id", 1, shared)));
            Assert.DoesNotThrow(() => ObjectiveSpecValidator.Validate(new ObjectiveSpec("id", 1, Task("a", "free-key"))));
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 잘못된 enum·Parallel용 Sequential 정책·불가능한 목표는 거부한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveSpec_정적정책조합거부()
        {
            var badEntry = new TaskChildEntry("a", Task("task"), rollbackMode: TaskRollbackMode.ResetOnRollback);
            var invalidPolicy = Parallel(badEntry);
            var single = new TaskChildEntry[]
            {
                new("a", Task("task"))
            };
            var impossible = new TaskGroupSpec("root", TaskExecutionMode.Parallel, TaskGroupCond.MinSucceeded(2), single);
            var invalidMode = new TaskGroupSpec("root", (TaskExecutionMode)99, TaskGroupCond.All(), single);
            var invalidFailure = new TaskGroupSpec
            (
                "root", TaskExecutionMode.Parallel, TaskGroupCond.All(), single,
                failureCond: TaskGroupCond.MinSucceeded(2)
            );
            var invalidMinFailed = new TaskGroupSpec
            (
                "root", TaskExecutionMode.Parallel, TaskGroupCond.All(), single,
                failureCond: TaskGroupCond.MinFailed(2)
            );
            var invalidUnreachable = new TaskGroupSpec
            (
                "root", TaskExecutionMode.Parallel, TaskGroupCond.All(), single,
                failureCond: TaskGroupCond.Unreachable(2)
            );
            var invalidVersion = new ObjectiveSpec("bad", 0, Task("task"));

            Assert.Throws<ArgumentException>(() => ObjectiveSpecValidator.Validate(Wrap(invalidPolicy)));
            Assert.Throws<ArgumentException>(() => ObjectiveSpecValidator.Validate(Wrap(impossible)));
            Assert.Throws<ArgumentException>(() => ObjectiveSpecValidator.Validate(Wrap(invalidMode)));
            Assert.Throws<ArgumentException>(() => ObjectiveSpecValidator.Validate(Wrap(invalidFailure)));
            Assert.Throws<ArgumentException>(() => ObjectiveSpecValidator.Validate(Wrap(invalidMinFailed)));
            Assert.Throws<ArgumentException>(() => ObjectiveSpecValidator.Validate(Wrap(invalidUnreachable)));
            Assert.Throws<ArgumentException>(() => ObjectiveSpecValidator.Validate(invalidVersion));
            Assert.Throws<ArgumentOutOfRangeException>(() => TaskGroupCond.MinSucceeded(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => TaskGroupCond.MinFailed(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => TaskGroupCond.Unreachable(0));
        }

    #endregion

    #region Condition·Registry

        // ------------------------------------------------------------
        /// <summary>
        /// Disabled Entry는 완료 수에 포함하지 않는다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveSpec_Disabled의집계의미()
        {
            var progress = new ProgressState();
            var objective = new ObjectiveContext(progress.Values, 2, ObjectiveStatus.Active);
            var values = new TaskEntryResult[]
            {
                new("a", true, TaskResult.Succeeded),
                new("b", false, TaskResult.Succeeded),
                new("c", true, TaskResult.Pending)
            };
            var context = new TaskGroupContext(objective, values);

            Assert.That(context.EntryCount, Is.EqualTo(3));
            Assert.That(context.SucceededCount, Is.EqualTo(1));
            Assert.That(TaskGroupCond.Any().Check(context), Is.True);
            Assert.That(TaskGroupCond.All().Check(context), Is.False);
            Assert.That(TaskGroupCond.MinSucceeded(2).Check(context), Is.False);
            Assert.That(context.TryGetEntry("b", out var disabled), Is.True);
            Assert.That(disabled.Participating, Is.False);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// <br/>실패 보조는 참여 중 실패만 센다.
        /// <br/>비참여 자식은 나중에 참여할 수 있으므로 완료 가능으로 본다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveSpec_실패보조의집계의미()
        {
            var objective = new ObjectiveContext(new ProgressState().Values, 0, ObjectiveStatus.Active);
            var values = new TaskEntryResult[]
            {
                new("a", true, TaskResult.Failed),
                new("b", false, TaskResult.Failed),
                new("c", true, TaskResult.Succeeded)
            };
            var context = new TaskGroupContext(objective, values);

            Assert.That(context.FailedCount, Is.EqualTo(1));
            Assert.That(TaskGroupCond.MinFailed(1).Check(context), Is.True);
            Assert.That(TaskGroupCond.MinFailed(2).Check(context), Is.False);
            Assert.That(TaskGroupCond.Unreachable(2).Check(context), Is.False);
            Assert.That(TaskGroupCond.Unreachable(3).Check(context), Is.True);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Registry는 같은 ID의 여러 Version을 정확히 구별한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveSpec_Registry정확한Version조회()
        {
            var registry = new ObjectiveSpecRegistry();
            registry.Register(Valid(1));
            registry.Register(Valid(2));

            Assert.That(registry.TryResolve("s04", 1, out var first), Is.True);
            Assert.That(first.Version, Is.EqualTo(1));
            Assert.That(registry.TryResolve("s04", 2, out var second), Is.True);
            Assert.That(second.Version, Is.EqualTo(2));
            Assert.That(registry.TryResolve("s04", 3, out _), Is.False);
            Assert.That(registry.TryResolve("missing", 1, out _), Is.False);
            Assert.Throws<ArgumentException>(() => registry.Register(Valid(1)));
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 오류가 포함된 배치에서 정상 항목도 부분 등록되지 않는다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveSpec_등록배치는원자적이다()
        {
            var registry = new ObjectiveSpecRegistry();
            registry.Register(Valid(1));
            var invalid = new ObjectiveSpec("invalid", 1, new TaskSpec("bad", null));
            var batch = new ObjectiveSpec[]
            {
                Valid(2),
                invalid
            };
            Assert.Throws<ArgumentException>(() => registry.RegisterRange(batch));
            Assert.That(registry.TryResolve("s04", 1, out _), Is.True);
            Assert.That(registry.TryResolve("s04", 2, out _), Is.False);
            Assert.That(registry.TryResolve("invalid", 1, out _), Is.False);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 서로 다른 저작 공급자는 동일한 Spec 계약을 사용한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveSpec_저작소스에독립적이다()
        {
            IEnumerable<ObjectiveSpec> code = new ObjectiveSpec[]
            {
                Valid(1)
            };
            IEnumerable<ObjectiveSpec> generated = new ObjectiveSpec[]
            {
                new("generated", 7, Task("root"))
            };
            var registry = new ObjectiveSpecRegistry();
            registry.RegisterRange(code);
            registry.RegisterRange(generated);
            IObjectiveSpecResolver resolver = registry;

            Assert.That(resolver.TryResolve("s04", 1, out _), Is.True);
            Assert.That(resolver.TryResolve("generated", 7, out _), Is.True);
        }

    #endregion

    }
}
