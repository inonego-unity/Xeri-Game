/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : TEST_ObjectiveScenarios.cs
수정일 : 2026-11-25

# 설명
대표 플레이 시나리오를 Composite·Flow·Fact·Progress 명령의 실제 순서로 검증한다.

# 특이사항, 제약사항
외부 게임 시스템 대신 읽기 전용 Fact를 사용하며 실제 Domain 거래·보상 통합은 검증하지 않는다.
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
using inonego.Xeri.Game.Progress;

namespace inonego.Xeri.Game.TEST.Objective
{
    // ============================================================
    /// <summary>
    /// 여러 Core 계약을 조합한 플레이 시나리오 실행을 검증한다.
    /// </summary>
    // ============================================================
    public sealed class TEST_ObjectiveScenarios
    {

    #region 테스트 Fact·Condition

        private sealed class FailedAtLeast : ICond<TaskGroupContext>
        {
            private readonly int threshold;

            internal FailedAtLeast(int threshold)
            {
                this.threshold = threshold;
            }

            // ------------------------------------------------------------
            /// <summary>
            /// 작성자가 명시한 실패 자식 수에 도달했는지 판단한다.
            /// </summary>
            // ------------------------------------------------------------
            public bool Check(TaskGroupContext context) => context.FailedCount >= threshold;
        }

        private sealed class OtherFailed : ICond<ObjectiveContext>
        {
            private readonly string specID;

            internal OtherFailed(string specID)
            {
                this.specID = specID;
            }

            // --------------------------------------------------------------------------------
            /// <summary>
            /// 같은 Scope의 다른 Objective가 명시적으로 Failed 확정된 사실만 완료 사실로 조회한다.
            /// </summary>
            // --------------------------------------------------------------------------------
            public bool Check(ObjectiveContext context) =>
                context.TryGetObjective(specID, out var other) && other.Status == ObjectiveStatus.Failed;
        }

        private sealed class SharedCountAtLeast : ICond<ObjectiveContext>
        {
            private readonly SharedCount owner;
            private readonly int threshold;

            internal SharedCountAtLeast(SharedCount owner, int threshold)
            {
                this.owner = owner;
                this.threshold = threshold;
            }

            // ------------------------------------------------------------
            /// <summary>
            /// 동일 외부 Owner의 누적값을 복제하지 않고 조회한다.
            /// </summary>
            // ------------------------------------------------------------
            public bool Check(ObjectiveContext context) => owner.Count >= threshold;
        }

        private sealed class SharedCount
        {
            internal int Count;
        }

    #endregion

    #region 테스트 정의

        private static TaskSpec Counter(string nodeID, string progressID)
        {
            var owned = new string[]
            {
                progressID
            };
            return new TaskSpec(nodeID, new CounterCond(progressID, 3), localProgressIDs: owned);
        }

        private static TaskSpec Manual(string nodeID, string progressID)
        {
            var owned = new string[]
            {
                progressID
            };
            return new TaskSpec(nodeID, new FlagCond(progressID), localProgressIDs: owned);
        }

        private static TaskGroupSpec Sequence(string nodeID, params TaskChildEntry[] entries) =>
            new(nodeID, TaskExecutionMode.Sequential, TaskGroupCond.All(), entries);

        private static TaskGroupSpec Parallel
        (
            string nodeID, int minimum, TaskContinuationMode continuation,
            params TaskChildEntry[] entries
        )
        {
            return new TaskGroupSpec
            (
                nodeID, TaskExecutionMode.Parallel,
                TaskGroupCond.MinSucceeded(minimum), entries, continuation
            );
        }

        private static ObjectiveEngine Create
        (
            string id, TaskNodeSpec root, string[] progress = null,
            ObjectiveCommitMode success = ObjectiveCommitMode.Automatic,
            ObjectiveCommitMode failure = ObjectiveCommitMode.Automatic,
            ObjectiveDirectory directory = null
        )
        {
            var spec = new ObjectiveSpec(id, 1, root, success, failure);
            var instance = new ObjectiveInstance(id, 1, "run-" + id, "world");
            var engine = new ObjectiveEngine(spec, instance, directory: directory);
            directory?.Register(engine);
            return engine;
        }

        private static string[] Counters(params string[] ids)
        {
            var result = new string[ids.Length];
            for (int index = 0; index < ids.Length; index++)
            {
                result[index] = ids[index];
            }

            return result;
        }

        private static string[] Manuals(params string[] ids)
        {
            var result = new string[ids.Length];
            for (int index = 0; index < ids.Length; index++)
            {
                result[index] = ids[index];
            }

            return result;
        }

        private static void AssertCounter(ObjectiveEngine engine, string id, int expected)
        {
            int current = engine.Instance.Progress.Values.GetI(id);
            Assert.That(current, Is.EqualTo(expected));
        }

    #endregion

    #region 플레이 시나리오

        // ------------------------------------------------------------
        /// <summary>
        /// S01: 셋 중 둘 완료 전 보고를 거부하고 보고 후에만 성공한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveScenarios_S01_둘중둘완료뒤보고()
        {
            var herb = new ToggleCond();
            var ruin = new ToggleCond();
            var goals = Parallel
            (
                "goals", 2, TaskContinuationMode.StopOnSucceeded,
                new TaskChildEntry("kills-entry", Counter("kills-node", "kills")),
                new TaskChildEntry("herb-entry", new TaskSpec("herb-node", herb)),
                new TaskChildEntry("ruin-entry", new TaskSpec("ruin-node", ruin))
            );
            var root = Sequence
            (
                "root", new TaskChildEntry("goals-entry", goals),
                new TaskChildEntry("report-entry", Manual("report-node", "report"))
            );
            var progress = new string[]
            {
                "kills",
                "report"
            };
            var engine = Create("village", root, progress);
            Assert.That(engine.Activate(), Is.True);
            Assert.That(engine.EditProgress("report", values => values.SetFlag("report")), Is.False);
            engine.EditProgress("kills", values => values.AddI("kills", 3));
            Assert.That(engine.EditProgress("report", values => values.SetFlag("report")), Is.False);

            herb.Value = true;
            // 후속 보고 Entry가 처음 열리면 Sequential 경계가 영속 변경된다.
            Assert.That(engine.Refresh(), Is.True);
            Assert.That(engine.Instance.Status, Is.EqualTo(ObjectiveStatus.Active));
            Assert.That(engine.Evaluate().TryGetEntry("report-entry", out var opened), Is.True);
            Assert.That(opened.CanAcceptInput, Is.True);
            Assert.That(engine.EditProgress("report", values => values.SetFlag("report")), Is.True);
            Assert.That(engine.Instance.Status, Is.EqualTo(ObjectiveStatus.Succeeded));
            Assert.That(ruin.Value, Is.False);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// S05: 선택지 하나가 실패해도 다른 세 경로가 성공할 수 있다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveScenarios_S05_실패한대안비전파()
        {
            var negotiation = new ToggleCond();
            var negotiationFailed = new ToggleCond();
            var support = new ToggleCond();
            var defense = new ToggleCond();
            var evacuation = new ToggleCond();
            var entries = new TaskChildEntry[]
            {
                new("negotiate", new TaskSpec("n", negotiation, negotiationFailed)),
                new("supply", new TaskSpec("s", support)),
                new("defense", new TaskSpec("d", defense)),
                new("evacuate", new TaskSpec("e", evacuation))
            };
            var root = new TaskGroupSpec
            (
                "root", TaskExecutionMode.Parallel, TaskGroupCond.MinSucceeded(3), entries,
                failureCond: new FailedAtLeast(2)
            );
            var engine = Create("defend", root);
            engine.Activate();
            negotiationFailed.Value = true;
            engine.Refresh();
            Assert.That(engine.Instance.Status, Is.EqualTo(ObjectiveStatus.Active));
            support.Value = true;
            defense.Value = true;
            evacuation.Value = true;
            engine.Refresh();
            Assert.That(engine.Instance.Status, Is.EqualTo(ObjectiveStatus.Succeeded));

            var second = new ToggleCond();
            var twoFailed = new TaskChildEntry[]
            {
                new("a", new TaskSpec("a-node", second, negotiationFailed)),
                new("b", new TaskSpec("b-node", second, negotiationFailed)),
                new("c", new TaskSpec("c-node", second)),
                new("d", new TaskSpec("d-node", second))
            };
            var failingRoot = new TaskGroupSpec
            (
                "failed-root", TaskExecutionMode.Parallel, TaskGroupCond.MinSucceeded(3),
                twoFailed, failureCond: new FailedAtLeast(2)
            );
            var failed = Create("cannot-defend", failingRoot);
            failed.Activate();
            Assert.That(failed.Instance.Status, Is.EqualTo(ObjectiveStatus.Failed));
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// S21: 다른 Objective의 Failed 확정을 Directory로 조회해 비밀 의뢰가 열린다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveScenarios_S21_타Objective실패조회()
        {
            var impossible = new ToggleCond();
            var directory = new ObjectiveDirectory();
            var source = Create("ordinary", new TaskSpec("ordinary-root", impossible), directory: directory);
            var secret = Create("secret", new TaskSpec("secret-root", new OtherFailed("ordinary")), directory: directory);
            source.Activate();
            Assert.That(secret.Evaluate().RootResult, Is.EqualTo(TaskResult.Pending));
            Assert.That(source.Force(ObjectiveStatus.Failed), Is.True);
            Assert.That(secret.Activate(), Is.True);
            Assert.That(secret.Instance.Status, Is.EqualTo(ObjectiveStatus.Succeeded));
        }

        // ------------------------------------------------------------
        /// <summary>
        /// S26: 오답 세 번은 실패하고 정답이 먼저면 성공으로 종료한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveScenarios_S26_오답세번과정답()
        {
            var unlocked = new ToggleCond();
            var owned = new string[]
            {
                "wrong"
            };
            var root = new TaskSpec
            (
                "door", unlocked, new CounterCond("wrong", 3),
                localProgressIDs: owned
            );
            var progress = Counters("wrong");
            var failed = Create("password-fail", root, progress);
            failed.Activate();
            failed.EditProgress("wrong", values => values.AddI("wrong", 2));
            Assert.That(failed.Instance.Status, Is.EqualTo(ObjectiveStatus.Active));
            failed.EditProgress("wrong", values => values.AddI("wrong", 1));
            Assert.That(failed.Instance.Status, Is.EqualTo(ObjectiveStatus.Failed));

            var succeeded = Create("password-success", root, progress);
            succeeded.Activate();
            succeeded.EditProgress("wrong", values => values.AddI("wrong", 2));
            unlocked.Value = true;
            succeeded.Refresh();
            Assert.That(succeeded.Instance.Status, Is.EqualTo(ObjectiveStatus.Succeeded));
            Assert.That(succeeded.EditProgress("wrong", values => values.AddI("wrong", 1)), Is.False);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// S32: 기존 지속 Fact는 즉시 인정하되 과거 사건은 소급하지 않는다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveScenarios_S32_기존Fact와사건의차이()
        {
            var swordOwned = new ToggleCond(true);
            var swordEquipped = new ToggleCond(true);
            var root = Sequence
            (
                "root",
                new TaskChildEntry("own", new TaskSpec("own-node", swordOwned)),
                new TaskChildEntry("open", Manual("open-node", "opened")),
                new TaskChildEntry("equip", new TaskSpec("equip-node", swordEquipped))
            );
            var engine = Create("tutorial", root, Manuals("opened"));
            engine.Activate();
            Assert.That(engine.Instance.Status, Is.EqualTo(ObjectiveStatus.Active));
            Assert.That(engine.Evaluate().TryGetEntry("open", out var opened), Is.True);
            Assert.That(opened.CanAcceptInput, Is.True);
            Assert.That(engine.Evaluate().TryGetEntry("equip", out var equipped), Is.True);
            Assert.That(equipped.Participating, Is.False);
            engine.EditProgress("opened", values => values.SetFlag("opened"));
            Assert.That(engine.Instance.Status, Is.EqualTo(ObjectiveStatus.Succeeded));
        }

        // ------------------------------------------------------------
        /// <summary>
        /// S42: 앞 웨이브 완료 전의 뒷 웨이브 사건을 수락하지 않는다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveScenarios_S42_웨이브별독점진행()
        {
            var root = Sequence
            (
                "root", new TaskChildEntry("wave-one", Counter("one-node", "one")),
                new TaskChildEntry("wave-two", Counter("two-node", "two"))
            );
            var engine = Create("waves", root, Counters("one", "two"));
            engine.Activate();
            Assert.That(engine.EditProgress("two", values => values.AddI("two", 1)), Is.False);
            AssertCounter(engine, "two", 0);
            engine.EditProgress("one", values => values.AddI("one", 3));
            Assert.That(engine.EditProgress("two", values => values.AddI("two", 2)), Is.True);
            AssertCounter(engine, "two", 2);
            engine.EditProgress("two", values => values.AddI("two", 1));
            Assert.That(engine.Instance.Status, Is.EqualTo(ObjectiveStatus.Succeeded));
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// S50: 같은 권위 Owner의 누적 Fact를 두 Objective가 독립적으로 조회한다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveScenarios_S50_공유누적Fact조회()
        {
            var owner = new SharedCount();
            var first = Create
            (
                "milestone-100", new TaskSpec("root100", new SharedCountAtLeast(owner, 100))
            );
            var second = Create
            (
                "milestone-500", new TaskSpec("root500", new SharedCountAtLeast(owner, 500))
            );
            first.Activate();
            second.Activate();
            owner.Count = 100;
            first.Refresh();
            second.Refresh();
            Assert.That(first.Instance.Status, Is.EqualTo(ObjectiveStatus.Succeeded));
            Assert.That(second.Instance.Status, Is.EqualTo(ObjectiveStatus.Active));
            owner.Count = 500;
            second.Refresh();
            Assert.That(second.Instance.Status, Is.EqualTo(ObjectiveStatus.Succeeded));
            Assert.That(owner.Count, Is.EqualTo(500));
        }

        // ------------------------------------------------------------
        /// <summary>
        /// S77: 비선택 경로 입력은 막고 분기 변경 후 현재 경로로만 합류한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveScenarios_S77_대화경로배타적합류()
        {
            var routeA = new ToggleCond(true);
            var routeB = new ToggleCond();
            var branches = Parallel
            (
                "branch", 1, TaskContinuationMode.ContinueWhileActive,
                new TaskChildEntry("route-a", Manual("a-node", "a"), routeA),
                new TaskChildEntry("route-b", Manual("b-node", "b"), routeB)
            );
            var root = Sequence
            (
                "root", new TaskChildEntry("branch-entry", branches),
                new TaskChildEntry("join-entry", Manual("join-node", "join"))
            );
            var engine = Create("dialogue", root, Manuals("a", "b", "join"));
            engine.Activate();
            Assert.That(engine.EditProgress("b", values => values.SetFlag("b")), Is.False);
            Assert.That(engine.EditProgress("a", values => values.SetFlag("a")), Is.True);
            Assert.That(engine.Evaluate().TryGetEntry("join-entry", out var joined), Is.True);
            Assert.That(joined.CanAcceptInput, Is.True);

            routeA.Value = false;
            routeB.Value = true;
            engine.Refresh();
            Assert.That(engine.Evaluate().TryGetEntry("join-entry", out joined), Is.True);
            Assert.That(joined.CanAcceptInput, Is.False);
            bool aDone = engine.Instance.Progress.Values.HasFlag("a");
            Assert.That(aDone, Is.True);
            Assert.That(engine.EditProgress("join", values => values.SetFlag("join")), Is.False);
            Assert.That(engine.EditProgress("b", values => values.SetFlag("b")), Is.True);
            Assert.That(engine.EditProgress("join", values => values.SetFlag("join")), Is.True);
            Assert.That(engine.Instance.Status, Is.EqualTo(ObjectiveStatus.Succeeded));
        }

    #endregion

    }
}
