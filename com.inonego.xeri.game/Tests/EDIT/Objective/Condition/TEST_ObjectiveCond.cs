/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : TEST_ObjectiveCond.cs
수정일 : 2026-11-25

# 설명
표준 Condition(ObjectiveCond·대상 Entry를 지정한 TaskGroupCond)의 판정, 닫힌 사유, 정적 검사(참조·임계값)를 검증한다.

# 특이사항, 제약사항
판정은 ICond가 한다.
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
using inonego.Xeri.Serializable;

namespace inonego.Xeri.Game.TEST.Objective
{
    // ============================================================
    /// <summary>
    /// 표준 Condition과 평가 부가 정보를 검증한다.
    /// </summary>
    // ============================================================
    public sealed class TEST_ObjectiveCond
    {

    #region 테스트 정의

        private static ObjectiveEngine Create(TaskNodeSpec root, ObjectiveDirectory directory = null)
        {
            var spec = new ObjectiveSpec("quest", 1, root);
            var engine = new ObjectiveEngine(spec, new ObjectiveInstance("quest", 1, "run", "world"), directory: directory);
            directory?.Register(engine);
            return engine;
        }

        private static TaskSpec Task(string nodeID, ICond<ObjectiveContext> success, params string[] owned) =>
            new(nodeID, success, localProgressIDs: owned);

    #endregion

    #region 판정

        // ------------------------------------------------------------
        /// <summary>
        /// 값·flag·경과 시간 Condition이 Progress와 시간으로 판정한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveCond_값과flag와경과시간으로판정한다()
        {
            var steps = new TaskChildEntry[]
            {
                new("count", Task("count-node", ObjectiveCond.MinValueI("kills", 3), "kills")),
                new("ratio", Task("ratio-node", ObjectiveCond.MinValueF("charge", 0.5f), "charge")),
                new("flag", Task("flag-node", ObjectiveCond.HasFlag("done"), "done")),
                new("time", Task("time-node", ObjectiveCond.MinElapsed(10)))
            };
            var root = new TaskGroupSpec("root", TaskExecutionMode.Parallel, TaskGroupCond.All(), steps, TaskContinuationMode.ContinueWhileActive);
            var engine = Create(root);
            engine.Activate();

            engine.EditProgress("kills", values => values.SetI("kills", 2));
            Assert.That(engine.Evaluate().TryGetNode("count-node", out var node), Is.True);
            Assert.That(node.RawResult, Is.EqualTo(TaskResult.Pending));

            engine.EditProgress("kills", values => values.SetI("kills", 3));
            engine.EditProgress("charge", values => values.SetF("charge", 0.5f));
            engine.EditProgress("done", values => values.SetFlag("done"));
            engine.AdvanceTime(9);
            Assert.That(engine.Instance.Status, Is.EqualTo(ObjectiveStatus.Active));
            engine.AdvanceTime(1);

            Assert.That(engine.Instance.Status, Is.EqualTo(ObjectiveStatus.Succeeded));
        }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// 이름 붙은 Fact 소스를 읽고, 등록되지 않았으면 false로 위장하지 않고 예외로 알려 변경이 없다.
        /// </summary>
        // --------------------------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveCond_Fact소스를읽고없으면예외이다()
        {
            var directory = new ObjectiveDirectory();
            var engine = Create(Task("root", ObjectiveCond.MinValueI("total", 5, source: "account")), directory);

            Assert.Throws<InvalidOperationException>(() => engine.Activate());
            Assert.That(engine.Instance.Status, Is.EqualTo(ObjectiveStatus.Inactive));

            var account = new ValueRegistry();
            account.SetI("total", 4);
            directory.RegisterSource("account", account);
            engine.Activate();
            Assert.That(engine.Instance.Status, Is.EqualTo(ObjectiveStatus.Active));

            account.SetI("total", 5);
            directory.RefreshDirty();
            Assert.That(engine.Instance.Status, Is.EqualTo(ObjectiveStatus.Succeeded));
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 다른 Objective의 상태 Condition은 Core 논리 조합과 함께 쓴다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveCond_다른Objective상태를논리조합으로판정한다()
        {
            var directory = new ObjectiveDirectory();
            var first = Create(Task("first-root", new ToggleCond()), directory);
            var other = new ObjectiveSpec("other", 1, Task("other-root", new ToggleCond()));
            var second = new ObjectiveEngine(other, new ObjectiveInstance("other", 1, "run-other", "world"), directory: directory);
            directory.Register(second);
            var gate = new ObjectiveSpec
            (
                "gate", 1,
                Task("gate-root", Cond.OR(ObjectiveCond.DependsOn("quest", ObjectiveStatus.Failed), ObjectiveCond.DependsOn("other", ObjectiveStatus.Succeeded)))
            );
            var engine = new ObjectiveEngine(gate, new ObjectiveInstance("gate", 1, "run-gate", "world"), directory: directory);
            directory.Register(engine);
            first.Activate();
            second.Activate();
            engine.Activate();
            Assert.That(engine.Instance.Status, Is.EqualTo(ObjectiveStatus.Active));

            second.Force(ObjectiveStatus.Succeeded);
            directory.RefreshDirty();

            Assert.That(engine.Instance.Status, Is.EqualTo(ObjectiveStatus.Succeeded));
        }

    #endregion

    #region 닫힌 사유

        // ------------------------------------------------------------
        /// <summary>
        /// 부모·앞 Entry·Eligibility 중 첫 번째 사유만 기록한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveCond_닫힌사유를구분한다()
        {
            var fact = new ToggleCond();
            var inner = new TaskChildEntry[]
            {
                new("inner-a", Task("inner-a-node", new ToggleCond())),
                new("inner-b", Task("inner-b-node", new ToggleCond()))
            };
            var group = new TaskGroupSpec("group", TaskExecutionMode.Sequential, TaskGroupCond.All(), inner);
            var entries = new TaskChildEntry[]
            {
                new("first", Task("first-node", new ToggleCond())),
                new("second", group),
                new("locked", Task("locked-node", new ToggleCond()), eligibilityCond: fact)
            };
            var root = new TaskGroupSpec("root", TaskExecutionMode.Parallel, TaskGroupCond.MinSucceeded(1), entries);
            var engine = Create(root);
            engine.Activate();

            var evaluation = engine.Evaluate();

            Assert.That(evaluation.TryGetEntry("first", out var first), Is.True);
            Assert.That(first.ClosedBy, Is.EqualTo(TaskClosedBy.None));
            Assert.That(evaluation.TryGetEntry("locked", out var locked), Is.True);
            Assert.That(locked.ClosedBy, Is.EqualTo(TaskClosedBy.Eligibility));
            Assert.That(evaluation.TryGetEntry("inner-a", out var innerA), Is.True);
            Assert.That(innerA.ClosedBy, Is.EqualTo(TaskClosedBy.None));
            Assert.That(evaluation.TryGetEntry("inner-b", out var innerB), Is.True);
            Assert.That(innerB.ClosedBy, Is.EqualTo(TaskClosedBy.Predecessor));
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 닫힌 Group 아래의 Entry는 부모가 닫힌 사유로 기록한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveCond_닫힌부모아래는부모사유이다()
        {
            var inner = new TaskChildEntry[]
            {
                new("inner", Task("inner-node", new ToggleCond()))
            };
            var group = new TaskGroupSpec("group", TaskExecutionMode.Parallel, TaskGroupCond.All(), inner);
            var entries = new TaskChildEntry[]
            {
                new("outer", group, eligibilityCond: new ToggleCond())
            };
            var engine = Create(new TaskGroupSpec("root", TaskExecutionMode.Parallel, TaskGroupCond.All(), entries));
            engine.Activate();

            var evaluation = engine.Evaluate();

            Assert.That(evaluation.TryGetEntry("outer", out var outer), Is.True);
            Assert.That(outer.ClosedBy, Is.EqualTo(TaskClosedBy.Eligibility));
            Assert.That(evaluation.TryGetEntry("inner", out var child), Is.True);
            Assert.That(child.ClosedBy, Is.EqualTo(TaskClosedBy.Parent));
        }

    #endregion

    #region Group 지정 집계·정적 검사

        // Group 집계 Condition을 직접 판정해 보는 Context. a·b·c의 결과를 지정한다.
        private static TaskGroupContext Context(TaskResult a, TaskResult b, TaskResult c, bool cParticipating = true)
        {
            var objective = new ObjectiveInstance("quest", 1, "run", "world").CreateContext(null, null);
            var results = new TaskEntryResult[]
            {
                new("a", true, a),
                new("b", true, b),
                new("c", cParticipating, c)
            };
            return new TaskGroupContext(objective, results);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 대상 Entry를 지정하면 그 자식만 보고 나머지는 보지 않는다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveCond_대상Entry를지정하면그것만본다()
        {
            var first = new ToggleCond
            {
                Value = true
            };
            var second = new ToggleCond
            {
                Value = true
            };
            var entries = new TaskChildEntry[]
            {
                new("a", Task("a-node", first)),
                new("b", Task("b-node", second)),
                new("c", Task("c-node", new ToggleCond()))
            };
            var root = new TaskGroupSpec("root", TaskExecutionMode.Parallel, TaskGroupCond.All("a", "b"), entries);
            var engine = Create(root);

            engine.Activate();

            Assert.That(engine.Instance.Status, Is.EqualTo(ObjectiveStatus.Succeeded));
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 다섯 집계 Condition 모두 대상 Entry를 지정하면 지정한 자식만 센다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveCond_모든집계가지정한대상만센다()
        {
            var succeeded = TaskResult.Succeeded;
            var failed = TaskResult.Failed;
            var pending = TaskResult.Pending;

            // 지정하지 않으면 모든 자식이 대상이라 c가 Pending이면 All은 거짓이다.
            Assert.That(TaskGroupCond.All().Check(Context(succeeded, succeeded, pending)), Is.False);
            Assert.That(TaskGroupCond.All("a", "b").Check(Context(succeeded, succeeded, pending)), Is.True);
            Assert.That(TaskGroupCond.All("a", "b").Check(Context(succeeded, pending, succeeded)), Is.False);

            // Any는 지정한 자식 중 하나만 성공해도 참이다. 지정하지 않은 c의 성공은 세지 않는다.
            Assert.That(TaskGroupCond.Any("a", "b").Check(Context(pending, succeeded, pending)), Is.True);
            Assert.That(TaskGroupCond.Any("a", "b").Check(Context(pending, pending, succeeded)), Is.False);
            Assert.That(TaskGroupCond.Any().Check(Context(pending, pending, succeeded)), Is.True);

            Assert.That(TaskGroupCond.MinSucceeded(2, "a", "c").Check(Context(succeeded, succeeded, pending)), Is.False);
            Assert.That(TaskGroupCond.MinSucceeded(2, "a", "c").Check(Context(succeeded, pending, succeeded)), Is.True);

            // 실패는 지정한 필수 자식만 본다. 선택 c가 실패해도 Group은 실패하지 않는다.
            Assert.That(TaskGroupCond.MinFailed(1, "a", "b").Check(Context(pending, pending, failed)), Is.False);
            Assert.That(TaskGroupCond.MinFailed(1, "a", "b").Check(Context(pending, failed, pending)), Is.True);
            Assert.That(TaskGroupCond.MinFailed(1).Check(Context(pending, pending, failed)), Is.True);

            // 필수 둘 중 하나가 실패하면 둘 다 성공할 수 없다. 선택 c의 실패는 무관하다.
            Assert.That(TaskGroupCond.Unreachable(2, "a", "b").Check(Context(failed, pending, pending)), Is.True);
            Assert.That(TaskGroupCond.Unreachable(2, "a", "b").Check(Context(pending, pending, failed)), Is.False);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 대상 Entry ID는 비어 있거나 중복이면 만들 때 거부한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveCond_잘못된대상Entry는만들때거부한다()
        {
            Assert.Throws<ArgumentException>(() => TaskGroupCond.All("a", " "));
            Assert.Throws<ArgumentException>(() => TaskGroupCond.Any("a", "a"));
            Assert.Throws<ArgumentNullException>(() => TaskGroupCond.MinSucceeded(1, null));
            Assert.Throws<ArgumentOutOfRangeException>(() => TaskGroupCond.MinFailed(0, "a"));
        }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// 존재하지 않거나 시간을 재지 않는 Entry를 가리키는 표준 Condition은 등록 때 거부한다.
        /// </summary>
        // --------------------------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveCond_잘못된Entry참조를등록때거부한다()
        {
            var entries = new TaskChildEntry[]
            {
                new("timed", Task("timed-node", ObjectiveCond.MinOpenElapsed("untimed", 5))),
                new("untimed", Task("untimed-node", new ToggleCond())),
                new("ghost", Task("ghost-node", Cond.NOT(ObjectiveCond.MinOpenElapsed("missing", 5)))),
                new("tracked", Task("tracked-node", ObjectiveCond.MinOpenElapsed("tracked", 5)), tracksOpenTime: true)
            };
            var root = new TaskGroupSpec("root", TaskExecutionMode.Parallel, TaskGroupCond.All("timed", "nobody"), entries);

            var issues = string.Join("\n", ObjectiveSpecValidator.Collect(new ObjectiveSpec("quest", 1, root)));

            Assert.That(issues, Does.Contain("root/timed-node: MinOpenElapsed refers to Entry 'untimed' which does not set TracksOpenTime."));
            Assert.That(issues, Does.Contain("root/ghost-node: MinOpenElapsed refers to unknown Entry 'missing'."));
            Assert.That(issues, Does.Contain("root: Group Condition refers to Entry 'nobody' which is not a direct child of this Group."));
            Assert.That(issues, Does.Not.Contain("tracked"));
        }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// 집계 Condition을 AND/OR/NOT으로 묶어도 임계값이 대상 자식 수를 넘으면 등록 때 거부한다.
        /// </summary>
        // --------------------------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveCond_조건을묶어도임계값을등록때거부한다()
        {
            var entries = new TaskChildEntry[]
            {
                new("a", Task("a-node", new ToggleCond())),
                new("b", Task("b-node", new ToggleCond()))
            };
            var success = Cond.AND(TaskGroupCond.All(), Cond.OR(TaskGroupCond.MinSucceeded(5), TaskGroupCond.Any()));
            var failure = Cond.NOT(TaskGroupCond.MinFailed(3, "a", "b"));
            var root = new TaskGroupSpec
            (
                "root", TaskExecutionMode.Parallel, success, entries, failureCond: failure
            );

            var issues = string.Join("\n", ObjectiveSpecValidator.Collect(new ObjectiveSpec("quest", 1, root)));

            Assert.That(issues, Does.Contain("root: MinSucceeded threshold 5 exceeds the 2 child Entries."));
            Assert.That(issues, Does.Contain("root: MinFailed threshold 3 exceeds the 2 child Entries."));
        }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/>Entry의 Eligibility가 그 Entry나 하위 Entry의 참여 시간을 읽으면 열림을 정할 수
        /// <br/>없으므로 등록 때 거부한다.
        /// </summary>
        // --------------------------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveCond_Eligibility가자기와하위Entry의참여시간을읽으면등록때거부한다()
        {
            var children = new TaskChildEntry[]
            {
                new("child", Task("child-node", new ToggleCond()), tracksOpenTime: true)
            };
            var inner = new TaskGroupSpec
            (
                "inner", TaskExecutionMode.Parallel, TaskGroupCond.All(), children
            );
            var entries = new TaskChildEntry[]
            {
                new("self", Task("self-node", new ToggleCond()), ObjectiveCond.MinOpenElapsed("self", 5), tracksOpenTime: true),
                new("outer", inner, ObjectiveCond.MinOpenElapsed("child", 5)),
                new("other", Task("other-node", new ToggleCond()), ObjectiveCond.MinOpenElapsed("self", 5))
            };
            var root = new TaskGroupSpec("root", TaskExecutionMode.Parallel, TaskGroupCond.All(), entries);

            var issues = string.Join("\n", ObjectiveSpecValidator.Collect(new ObjectiveSpec("quest", 1, root)));

            string selfIssue = "root#self: MinOpenElapsed in the Eligibility of an Entry refers to Entry 'self', "
                + "which is that Entry or inside it, so the open time depends on the Eligibility itself.";
            string outerIssue = "root#outer: MinOpenElapsed in the Eligibility of an Entry refers to Entry 'child', "
                + "which is that Entry or inside it, so the open time depends on the Eligibility itself.";
            Assert.That(issues, Does.Contain(selfIssue));
            Assert.That(issues, Does.Contain(outerIssue));
            Assert.That(issues, Does.Not.Contain("root#other"));
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 표준 Condition의 인자는 만들 때 검사한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveCond_잘못된인자는만들때거부한다()
        {
            Assert.Throws<ArgumentException>(() => ObjectiveCond.MinValueI(" ", 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => ObjectiveCond.MinElapsed(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => ObjectiveCond.MinOpenElapsed("entry", double.NaN));
            Assert.Throws<ArgumentException>(() => ObjectiveCond.DependsOn("", ObjectiveStatus.Failed));
            Assert.Throws<ArgumentOutOfRangeException>(() => ObjectiveCond.DependsOn("quest", (ObjectiveStatus)99));
        }

    #endregion

    }
}
