/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : TEST_ObjectiveSpecValidator.cs
수정일 : 2026-11-25

# 설명
Spec 검증이 첫 오류에서 멈추지 않고 경로가 붙은 오류를 모아 주는지, 표준 Condition의 참조를 정적으로 검사하는지 검증한다.

# 특이사항, 제약사항
Validate가 던지는 예외 형식(ArgumentException)은 기존 계약을 유지한다.
========================================================================= BLOCK_HEADER_END */

using System;
using System.Collections;
using System.Collections.Generic;

using NUnit;
using NUnit.Framework;

using inonego;
using inonego.Xeri;
using inonego.Xeri.Game;
using inonego.Xeri.Game.Objective;

namespace inonego.Xeri.Game.TEST.Objective
{
    // ============================================================
    /// <summary>
    /// Spec 검증 오류 수집과 정적 참조 검사를 검증한다.
    /// </summary>
    // ============================================================
    public sealed class TEST_ObjectiveSpecValidator
    {

    #region 테스트 정의

        private static TaskSpec Task(string nodeID, params string[] owned) =>
            new(nodeID, new ToggleCond(true), localProgressIDs: owned);

    #endregion

    #region 오류 수집

        // ------------------------------------------------------------
        /// <summary>
        /// 여러 오류를 한 번에 모으고 각 항목에 NodeID·EntryID 경로가 붙는다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveSpecValidator_여러오류를경로와함께모은다()
        {
            var entries = new TaskChildEntry[]
            {
                new("first", Task("a-node", "kills")),
                new("first", Task("b-node", "kills")),
                new("third", Task("a-node"))
            };
            var root = new TaskGroupSpec("root", TaskExecutionMode.Parallel, TaskGroupCond.MinSucceeded(5), entries);
            var spec = new ObjectiveSpec("quest", 1, root);

            var issues = ObjectiveSpecValidator.Collect(spec);

            Assert.That(issues.Count, Is.EqualTo(4));
            Assert.That(string.Join("\n", issues), Does.Contain("root: MinSucceeded threshold 5 exceeds the 3 child Entries."));
            Assert.That(string.Join("\n", issues), Does.Contain("root#first: EntryID 'first' is duplicated."));
            Assert.That(string.Join("\n", issues), Does.Contain("root/b-node: Local Progress ID 'kills' is owned by more than one Task."));
            Assert.That(string.Join("\n", issues), Does.Contain("root/a-node: NodeID 'a-node' is duplicated."));
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// Validate는 같은 오류를 ArgumentException 하나의 메시지로 던진다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveSpecValidator_Validate는모은오류를한번에던진다()
        {
            var spec = new ObjectiveSpec("quest", 1, Task("root", "key", "key"));

            var exception = Assert.Throws<ArgumentException>(() => ObjectiveSpecValidator.Validate(spec));

            Assert.That(exception.Message, Does.Contain("Objective spec 'quest' (version 1) has 1 issue(s)"));
            Assert.That(exception.Message, Does.Contain("root: Local Progress ID 'key' is owned by more than one Task."));
            Assert.That(ObjectiveSpecValidator.Collect(new ObjectiveSpec("ok", 1, Task("root"))), Is.Empty);
            Assert.Throws<ArgumentNullException>(() => ObjectiveSpecValidator.Collect(null));
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 순환·공유 참조는 더 내려가지 않고 오류로만 남는다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveSpecValidator_공유된Node는한번만오류로남긴다()
        {
            var shared = Task("shared");
            var entries = new TaskChildEntry[]
            {
                new("one", shared),
                new("two", shared)
            };
            var root = new TaskGroupSpec("root", TaskExecutionMode.Parallel, TaskGroupCond.All(), entries);

            var issues = ObjectiveSpecValidator.Collect(new ObjectiveSpec("quest", 1, root));

            Assert.That(issues.Count, Is.EqualTo(1));
            Assert.That(issues[0], Does.StartWith("root/shared: Task node instance is shared"));
        }

    #endregion

    }
}
