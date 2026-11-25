/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : TEST_ObjectiveEngine.cs
수정일 : 2026-11-25

# 설명
ObjectiveEngine의 평가·입력 명령·순차 개방·종료 Commit·참여 시각·알림을 검증한다.

# 특이사항, 제약사항
종료 확정을 수동으로 둔 Engine으로 평가를 검증하고, 종료·알림·Fact Scope 실패는 자동 확정 Engine으로 검증한다. 공용 Condition은 Support/ObjectiveTestConds에 있다.
========================================================================= BLOCK_HEADER_END */

using System;
using System.Collections;
using System.Collections.Generic;

using UnityEngine;

using NUnit;
using NUnit.Framework;

using inonego;
using inonego.Xeri;
using inonego.Xeri.Game;
using inonego.Xeri.Game.Objective;
using inonego.Xeri.Game.Progress;
using inonego.Xeri.Serializable;

namespace inonego.Xeri.Game.TEST.Objective
{
    // ================================================================================
    /// <summary>
    /// ObjectiveEngine의 평가·입력 명령·순차 개방·종료 Commit·참여 시각·알림을 검증한다.
    /// </summary>
    // ================================================================================
    public sealed class TEST_ObjectiveEngine
    {

    #region 테스트 도우미

        private sealed class CountException : ICond<ObjectiveContext>
        {
            // ------------------------------------------------------------
            /// <summary>
            /// 사건 후 Counter가 변경되면 테스트 예외를 던진다.
            /// </summary>
            // ------------------------------------------------------------
            public bool Check(ObjectiveContext context)
            {
                int current = context.Progress.GetI("a");
                if (current > 0)
                {
                    throw new InvalidOperationException("Injected post-mutation failure.");
                }

                return false;
            }
        }

        private sealed class GroupFailure : ICond<TaskGroupContext>
        {
            // ------------------------------------------------------------
            /// <summary>
            /// 실패한 직접 자식이 있을 때 Group을 실패시킨다.
            /// </summary>
            // ------------------------------------------------------------
            public bool Check(TaskGroupContext context) => context.FailedCount > 0;
        }

        private sealed class Capture : ICond<ObjectiveContext>
        {
            // ------------------------------------------------------------
            /// <summary>
            /// 마지막 평가에서 받은 Progress Snapshot.
            /// </summary>
            // ------------------------------------------------------------
            public IValueLookup Last { get; private set; }

            // ------------------------------------------------------------
            /// <summary>
            /// 받은 Snapshot을 보관하고 완료하지 않는다.
            /// </summary>
            // ------------------------------------------------------------
            public bool Check(ObjectiveContext context)
            {
                Last = context.Progress;
                return false;
            }
        }

        private sealed class Elapsed : ICond<ObjectiveContext>
        {
            // ------------------------------------------------------------
            /// <summary>
            /// 경과 시간이 두 초 이상인지 검사한다.
            /// </summary>
            // ------------------------------------------------------------
            public bool Check(ObjectiveContext context) => context.ElapsedTime >= 2.0;
        }

        private static TaskSpec Task(string nodeID, string progressID, int required = 1)
        {
            var owned = new string[]
            {
                progressID
            };
            return new TaskSpec(nodeID, new CounterCond(progressID, required), localProgressIDs: owned);
        }

        private static string[] Counters(params string[] ids)
        {
            var specs = new string[ids.Length];
            for (int index = 0; index < ids.Length; index++)
            {
                specs[index] = ids[index];
            }

            return specs;
        }

        private static ObjectiveSpec Triple(TaskContinuationMode continuation)
        {
            var entries = new TaskChildEntry[]
            {
                new("entry-a", Task("task-a", "a")),
                new("entry-b", Task("task-b", "b")),
                new("entry-c", Task("task-c", "c"))
            };
            var group = new TaskGroupSpec
            (
                "root", TaskExecutionMode.Parallel, TaskGroupCond.MinSucceeded(2),
                entries, continuation
            );
            return new ObjectiveSpec("triple", 1, group);
        }

        private static ObjectiveEngine Create(ObjectiveSpec spec)
        {
            var instance = new ObjectiveInstance
            (
                spec.SpecID, spec.Version, "run", "scope"
            );
            // 이 파일의 테스트는 종료가 아닌 Raw/Effective 평가를 검증하므로 최종 종료는 수동으로 둔다.
            var evaluationOnly = new ObjectiveSpec
            (
                spec.SpecID, spec.Version, spec.Root, ObjectiveCommitMode.Manual,
                ObjectiveCommitMode.Manual, spec.RepeatMode
            );
            return new ObjectiveEngine(evaluationOnly, instance);
        }

        private sealed class ScopedFact : ICond<ObjectiveContext>, IObjectiveFactScope
        {
            private sealed class Lease : IDisposable
            {
                private readonly ScopedFact owner;

                // ------------------------------------------------------------
                /// <summary>
                /// 평가 범위가 종료되었음을 소유자에게 알린다.
                /// </summary>
                // ------------------------------------------------------------
                public void Dispose()
                {
                    owner.active = false;
                    owner.Closed++;
                }

                internal Lease(ScopedFact owner)
                {
                    this.owner = owner;
                }
            }

            private bool active;
            private bool frozen;

            // ------------------------------------------------------------
            /// <summary>
            /// 외부 Owner의 현재 사실.
            /// </summary>
            // ------------------------------------------------------------
            public bool Value { get; set; }

            // ------------------------------------------------------------
            /// <summary>
            /// 열린 평가 범위 수.
            /// </summary>
            // ------------------------------------------------------------
            public int Opened { get; private set; }

            // ------------------------------------------------------------
            /// <summary>
            /// 종료된 평가 범위 수.
            /// </summary>
            // ------------------------------------------------------------
            public int Closed { get; private set; }

            // ------------------------------------------------------------
            /// <summary>
            /// 한 명령 동안 사용할 Fact를 고정한다.
            /// </summary>
            // ------------------------------------------------------------
            public IDisposable Begin()
            {
                if (active)
                {
                    throw new InvalidOperationException("Overlapping Fact evaluation.");
                }

                active = true;
                frozen = Value;
                Opened++;
                return new Lease(this);
            }

            // ------------------------------------------------------------
            /// <summary>
            /// 반드시 열린 Scope의 고정 Fact만 읽는다.
            /// </summary>
            // ------------------------------------------------------------
            public bool Check(ObjectiveContext context)
            {
                if (!active)
                {
                    throw new InvalidOperationException("Fact read without scope.");
                }

                return frozen;
            }
        }

        private sealed class ClosingFailure : IObjectiveFactScope, IDisposable
        {
            // ------------------------------------------------------------
            /// <summary>
            /// 참이면 다음 Scope 닫기 한 번이 실패한다.
            /// </summary>
            // ------------------------------------------------------------
            public bool FailNextClose { get; set; }

            // ------------------------------------------------------------
            /// <summary>
            /// 같은 객체를 Scope로 연다.
            /// </summary>
            // ------------------------------------------------------------
            public IDisposable Begin() => this;

            // ------------------------------------------------------------
            /// <summary>
            /// 설정된 경우 닫기를 한 번 실패시킨다.
            /// </summary>
            // ------------------------------------------------------------
            public void Dispose()
            {
                if (FailNextClose)
                {
                    FailNextClose = false;
                    throw new InvalidOperationException("Fact Scope close failed.");
                }
            }
        }

        private static ObjectiveEngine CreateSample
        (
            ICond<ObjectiveContext> completion, ICond<ObjectiveContext> failure = null,
            ObjectiveCommitMode successMode = ObjectiveCommitMode.Automatic,
            ObjectiveCommitMode failureMode = ObjectiveCommitMode.Automatic,
            ObjectiveRepeatMode repeatMode = ObjectiveRepeatMode.Once,
            IObjectiveFactScope factScope = null
        )
        {
            var task = new TaskSpec("root", completion, failure);
            var spec = new ObjectiveSpec
            (
                "sample", 1, task, successMode, failureMode, repeatMode
            );
            var instance = new ObjectiveInstance("sample", 1, "first", "scope");
            return new ObjectiveEngine(spec, instance, factScope);
        }

        private sealed class ThrowsAfterReset : ICond<ObjectiveContext>
        {
            // ------------------------------------------------------------
            /// <summary>
            /// 리셋 직후 예외를 유발할지 선택한다.
            /// </summary>
            // ------------------------------------------------------------
            public bool Enabled { get; set; }

            // ------------------------------------------------------------
            /// <summary>
            /// B Progress가 0인 경우 검사 실패를 발생시킨다.
            /// </summary>
            // ------------------------------------------------------------
            public bool Check(ObjectiveContext context)
            {
                int current = context.Progress.GetI("b");
                if (Enabled && current == 0)
                {
                    throw new InvalidOperationException("Injected failure after rollback.");
                }

                return current >= 2;
            }
        }

        private static TaskSpec FactTask(string id, ToggleCond fact) => new(id, fact);

        private static ObjectiveSpec Sequence
        (
            TaskChildEntry first, TaskChildEntry second,
            string[] progress = null,
            TaskContinuationMode continuation = TaskContinuationMode.StopOnSucceeded
        )
        {
            var entries = new TaskChildEntry[]
            {
                first,
                second
            };
            var root = new TaskGroupSpec
            (
                "root", TaskExecutionMode.Sequential, TaskGroupCond.All(),
                entries, continuation
            );
            return new ObjectiveSpec("sequence", 1, root);
        }

        private static void AssertProgress(ObjectiveEngine engine, string progressID, int expected)
        {
            int current = engine.Instance.Progress.Values.GetI(progressID);
            Assert.That(current, Is.EqualTo(expected));
        }

        private sealed class OpenFor : ICond<ObjectiveContext>
        {
            private readonly string entryID;
            private readonly double seconds;

            // ------------------------------------------------------------
            /// <summary>
            /// 시간을 잴 Entry와 제한 시간을 보관한다.
            /// </summary>
            // ------------------------------------------------------------
            public OpenFor(string entryID, double seconds)
            {
                this.entryID = entryID;
                this.seconds = seconds;
            }

            // ------------------------------------------------------------
            /// <summary>
            /// Entry가 참여를 시작한 뒤 제한 시간이 지났는지 평가한다.
            /// </summary>
            // ------------------------------------------------------------
            public bool Check(ObjectiveContext context) =>
                context.TryGetOpenElapsed(entryID, out double elapsed) && elapsed >= seconds;
        }

        // a를 끝내야 열리는 b 구간. b는 열린 지 5초가 지나면 실패한다.
        private static ObjectiveEngine CreateRace(ICond<ObjectiveContext> aFact, bool track = true)
        {
            var owned = new string[]
            {
                "b"
            };
            var b = new TaskSpec("b-node", new FlagCond("b"), new OpenFor("b-entry", 5), owned);
            var entries = new TaskChildEntry[]
            {
                new("a-entry", new TaskSpec("a-node", aFact)),
                new("b-entry", b, tracksOpenTime: track)
            };
            var root = new TaskGroupSpec
            (
                "root", TaskExecutionMode.Sequential, TaskGroupCond.All(), entries,
                failureCond: TaskGroupCond.MinFailed(1)
            );
            var spec = new ObjectiveSpec("race", 1, root);
            return new ObjectiveEngine(spec, new ObjectiveInstance("race", 1, "run", "world"));
        }

        private static TaskSpec FlagTask(string nodeID, string key)
        {
            var owned = new string[]
            {
                key
            };
            return new TaskSpec(nodeID, new FlagCond(key), localProgressIDs: owned);
        }

        private static ObjectiveEngine CreateFlagChain(ObjectiveCommitMode success = ObjectiveCommitMode.Automatic)
        {
            var entries = new TaskChildEntry[]
            {
                new("a-entry", FlagTask("a-node", "a")),
                new("b-entry", FlagTask("b-node", "b"))
            };
            var root = new TaskGroupSpec("root", TaskExecutionMode.Sequential, TaskGroupCond.All(), entries);
            var spec = new ObjectiveSpec("objective", 1, root, success);
            return new ObjectiveEngine(spec, new ObjectiveInstance("objective", 1, "run", "world"));
        }

        // 첫 Entry가 2번 쌓여야 성공하고, 그 뒤에 두 번째 Entry가 열리는 순차 Objective.
        private static ObjectiveEngine CreateChain()
        {
            var firstOwned = new string[]
            {
                "first"
            };
            var secondOwned = new string[]
            {
                "second"
            };
            var entries = new TaskChildEntry[]
            {
                new("first-entry", new TaskSpec("first-node", ObjectiveCond.MinValueI("first", 2), localProgressIDs: firstOwned)),
                new("second-entry", new TaskSpec("second-node", ObjectiveCond.MinValueI("second", 1), localProgressIDs: secondOwned))
            };
            var root = new TaskGroupSpec("root", TaskExecutionMode.Sequential, TaskGroupCond.All(), entries);
            var spec = new ObjectiveSpec("chain", 1, root);
            return new ObjectiveEngine(spec, new ObjectiveInstance("chain", 1, "run", "world"));
        }

        private static List<string> RecordEntries(ObjectiveEngine engine)
        {
            var log = new List<string>();
            engine.Changed += () => log.Add("Changed");
            engine.StatusChanged += (before, after) => log.Add($"{before}->{after}");
            engine.EntryEvaluationChanged += transition =>
                log.Add($"{transition.EntryID}:{transition.Previous.EffectiveResult}->{transition.Next.EffectiveResult}");
            engine.EvaluationChanged += evaluation => log.Add("Evaluation");
            return log;
        }

        private static ObjectiveEngine CreateCounting(string id = "quest", ObjectiveCommitMode success = ObjectiveCommitMode.Automatic)
        {
            var localProgressIDs2 = new[]
            {
                "count"
            };
            var root = new TaskSpec("root", ObjectiveCond.MinValueI("count", 2), localProgressIDs: localProgressIDs2);
            var spec = new ObjectiveSpec(id, 1, root, success);
            return new ObjectiveEngine(spec, new ObjectiveInstance(id, 1, "run-" + id, "world"));
        }

        private static List<string> RecordStatus(ObjectiveEngine engine)
        {
            var log = new List<string>();
            engine.Changed += () => log.Add("Changed");
            engine.StatusChanged += (before, after) => log.Add($"{before}->{after}");
            engine.EvaluationChanged += evaluation => log.Add("Evaluation");
            return log;
        }

    #endregion

    #region Task·Group 결과와 입력 허용

        // ------------------------------------------------------------
        /// <summary>
        /// Root Task 입력과 종료 미확정을 검증한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveEngine_RootTask와원자이벤트()
        {
            var spec = new ObjectiveSpec("one", 1, Task("root", "a"));
            var engine = Create(spec);
            int changed = 0;
            engine.Changed += () => changed++;

            Assert.That(engine.Evaluate().RootResult, Is.EqualTo(TaskResult.Pending));
            Assert.That(engine.EditProgress("a", values => values.AddI("a", 1)), Is.False);
            Assert.That(engine.Activate(), Is.True);
            Assert.That(engine.Evaluate().TryGetNode("root", out var root), Is.True);
            Assert.That(root.CanAcceptInput, Is.True);
            Assert.That(engine.EditProgress("a", values => values.AddI("a", 1)), Is.True);
            Assert.That(engine.Evaluate().RootResult, Is.EqualTo(TaskResult.Succeeded));
            Assert.That(engine.Instance.Status, Is.EqualTo(ObjectiveStatus.Active));
            // 누적 원본은 목표를 넘어도 계속 기록된다.
            Assert.That(engine.EditProgress("a", values => values.AddI("a", 1)), Is.True);
            Assert.That(engine.Activate(), Is.False);
            Assert.That(changed, Is.EqualTo(3));
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Stop은 완료된 Group의 입력만 차단하며 완료 결과를 지우지 않는다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveEngine_Stop완료후집계안정성()
        {
            var engine = Create(Triple(TaskContinuationMode.StopOnSucceeded));
            engine.Activate();
            engine.EditProgress("a", values => values.AddI("a", 1));
            engine.EditProgress("b", values => values.AddI("b", 1));
            var first = engine.Evaluate();

            Assert.That(first.RootResult, Is.EqualTo(TaskResult.Succeeded));
            Assert.That(first.TryGetEntry("entry-c", out var pending), Is.True);
            Assert.That(pending.Participating, Is.True);
            Assert.That(pending.EffectiveResult, Is.EqualTo(TaskResult.Pending));
            Assert.That(pending.CanAcceptInput, Is.False);
            Assert.That(engine.EditProgress("c", values => values.AddI("c", 1)), Is.False);
            Assert.That(engine.Evaluate().RootResult, Is.EqualTo(TaskResult.Succeeded));
            int current = engine.Instance.Progress.Values.GetI("c");
            Assert.That(current, Is.Zero);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Continue는 Group 완료 뒤에도 미완료 C 사건을 수락한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveEngine_Continue완료후선택입력()
        {
            var engine = Create(Triple(TaskContinuationMode.ContinueWhileActive));
            engine.Activate();
            engine.EditProgress("a", values => values.AddI("a", 1));
            engine.EditProgress("b", values => values.AddI("b", 1));
            var first = engine.Evaluate();

            Assert.That(first.RootResult, Is.EqualTo(TaskResult.Succeeded));
            Assert.That(first.TryGetEntry("entry-c", out var pending), Is.True);
            Assert.That(pending.CanAcceptInput, Is.True);
            Assert.That(engine.EditProgress("c", values => values.AddI("c", 1)), Is.True);
            Assert.That(engine.Evaluate().TryGetEntry("entry-c", out var completed), Is.True);
            Assert.That(completed.EffectiveResult, Is.EqualTo(TaskResult.Succeeded));
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Task 실패 우선과 부모 명시 실패를 검증한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveEngine_Task실패우선과부모명시실패()
        {
            var always = new ToggleCond(true);
            var owned = new string[]
            {
                "a"
            };
            var failedTask = new TaskSpec("fail-task", always, always, owned);
            var entries = new TaskChildEntry[]
            {
                new("failed-entry", failedTask)
            };
            var pendingParent = new TaskGroupSpec
            (
                "root", TaskExecutionMode.Parallel, TaskGroupCond.All(), entries
            );
            var groupFailure = new TaskGroupSpec
            (
                "failed-root", TaskExecutionMode.Parallel, TaskGroupCond.All(), entries,
                failureCond: new GroupFailure()
            );
            var pendingEngine = Create(new ObjectiveSpec("pending", 1, pendingParent));
            var failingEngine = Create(new ObjectiveSpec("failed", 1, groupFailure));
            pendingEngine.Activate();
            failingEngine.Activate();

            var pending = pendingEngine.Evaluate();
            Assert.That(pending.TryGetNode("fail-task", out var task), Is.True);
            Assert.That(task.RawResult, Is.EqualTo(TaskResult.Failed));
            Assert.That(pending.RootResult, Is.EqualTo(TaskResult.Pending));
            Assert.That(failingEngine.Evaluate().RootResult, Is.EqualTo(TaskResult.Failed));
        }

    #endregion

    #region 평가 Snapshot·실패 보조

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/>Condition이 받은 Progress는 평가 시점의 독립 Snapshot이다.
        /// <br/>이후 확정이 앞선 평가의 Context를 바꾸지 않는다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveEngine_평가Context는독립Snapshot이다()
        {
            var capture = new Capture();
            var owned = new string[]
            {
                "a"
            };
            var engine = Create(new ObjectiveSpec("snapshot", 1, new TaskSpec("root", capture, localProgressIDs: owned)));
            engine.Activate();

            Assert.That(engine.EditProgress("a", values => values.AddI("a", 1)), Is.True);
            var first = capture.Last;
            Assert.That(engine.EditProgress("a", values => values.AddI("a", 1)), Is.True);

            Assert.That(first.GetI("a"), Is.EqualTo(1));
            Assert.That(capture.Last.GetI("a"), Is.EqualTo(2));
            Assert.That(engine.Instance.Progress.Values.GetI("a"), Is.EqualTo(2));
        }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/>4개 중 3개 완료 Group은 완료할 수 없게 되는 순간 Unreachable로 실패한다(S05).
        /// <br/>그 전까지는 Pending으로 남는다.
        /// </summary>
        // --------------------------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveEngine_완료불가능한Group은실패보조로실패한다()
        {
            var failures = new ToggleCond[]
            {
                new(false),
                new(false),
                new(false),
                new(false)
            };
            var ids = new string[]
            {
                "a",
                "b",
                "c",
                "d"
            };
            var entries = new TaskChildEntry[ids.Length];
            for (int index = 0; index < ids.Length; index++)
            {
                var owned = new string[]
                {
                    ids[index]
                };
                var task = new TaskSpec("task-" + ids[index], new CounterCond(ids[index]), failures[index], owned);
                entries[index] = new TaskChildEntry("entry-" + ids[index], task);
            }

            var group = new TaskGroupSpec
            (
                "root", TaskExecutionMode.Parallel, TaskGroupCond.MinSucceeded(3), entries,
                failureCond: TaskGroupCond.Unreachable(3)
            );
            var engine = Create(new ObjectiveSpec("three-of-four", 1, group));
            engine.Activate();
            engine.EditProgress("a", values => values.AddI("a", 1));

            failures[1].Value = true;
            Assert.That(engine.Evaluate().RootResult, Is.EqualTo(TaskResult.Pending));

            failures[2].Value = true;
            Assert.That(engine.Evaluate().RootResult, Is.EqualTo(TaskResult.Failed));
        }

    #endregion

    #region 계층 Eligibility·분기

        // ------------------------------------------------------------
        /// <summary>
        /// 부모 Disabled는 자식 Condition 평가와 모든 후손 사건을 중지한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveEngine_부모Disabled는손자까지전파()
        {
            var parentEligibility = new ToggleCond(false);
            var nestedEligibility = new ToggleCond(true);
            var children = new TaskChildEntry[]
            {
                new("grandchild", Task("task-a", "a"), nestedEligibility)
            };
            var nested = new TaskGroupSpec
            (
                "nested", TaskExecutionMode.Parallel, TaskGroupCond.All(), children
            );
            var roots = new TaskChildEntry[]
            {
                new("parent-entry", nested, parentEligibility)
            };
            var root = new TaskGroupSpec("root", TaskExecutionMode.Parallel, TaskGroupCond.All(), roots);
            var engine = Create(new ObjectiveSpec("nested", 1, root));
            engine.Activate();
            var closed = engine.Evaluate();

            Assert.That(closed.TryGetEntry("parent-entry", out var parent), Is.True);
            Assert.That(parent.Participating, Is.False);
            Assert.That(closed.TryGetNode("task-a", out var task), Is.True);
            Assert.That(task.Participating, Is.False);
            Assert.That(task.RawResult, Is.EqualTo(TaskResult.Pending));
            Assert.That(task.CanAcceptInput, Is.False);
            Assert.That(nestedEligibility.Calls, Is.Zero);
            Assert.That(engine.EditProgress("a", values => values.AddI("a", 1)), Is.False);

            parentEligibility.Value = true;
            Assert.That(engine.Evaluate().TryGetEntry("grandchild", out var child), Is.True);
            Assert.That(child.CanAcceptInput, Is.True);
            Assert.That(engine.EditProgress("a", values => values.AddI("a", 1)), Is.True);
            parentEligibility.Value = false;
            var disabled = engine.Evaluate();
            Assert.That(disabled.RootResult, Is.EqualTo(TaskResult.Pending));
            Assert.That(disabled.TryGetNode("task-a", out var raw), Is.True);
            Assert.That(raw.Participating, Is.False);
            Assert.That(raw.RawResult, Is.EqualTo(TaskResult.Succeeded));
            int saved = engine.Instance.Progress.Values.GetI("a");
            Assert.That(saved, Is.EqualTo(1));
            parentEligibility.Value = true;
            Assert.That(engine.Evaluate().RootResult, Is.EqualTo(TaskResult.Succeeded));
        }

        // ------------------------------------------------------------
        /// <summary>
        /// All은 Disabled를 자동 완료로 취급하지 않는다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveEngine_All의Disabled는미완료()
        {
            var allowed = new ToggleCond(false);
            var entries = new TaskChildEntry[]
            {
                new("entry-a", Task("task-a", "a")),
                new("entry-b", Task("task-b", "b"), allowed)
            };
            var group = new TaskGroupSpec
            (
                "root", TaskExecutionMode.Parallel, TaskGroupCond.All(), entries,
                TaskContinuationMode.ContinueWhileActive
            );
            var engine = Create(new ObjectiveSpec("all", 1, group));
            engine.Activate();
            engine.EditProgress("a", values => values.AddI("a", 1));

            Assert.That(engine.Evaluate().RootResult, Is.EqualTo(TaskResult.Pending));
            Assert.That(engine.EditProgress("b", values => values.AddI("b", 1)), Is.False);
            allowed.Value = true;
            Assert.That(engine.Evaluate().RootResult, Is.EqualTo(TaskResult.Pending));
            Assert.That(engine.EditProgress("b", values => values.AddI("b", 1)), Is.True);
            Assert.That(engine.Evaluate().RootResult, Is.EqualTo(TaskResult.Succeeded));
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 서로 배타적인 경로를 바꾸어도 미선택 Progress는 유지·비집계한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveEngine_배타경로A_B_A진행보존()
        {
            var routeA = new ToggleCond(true);
            var routeB = new ToggleCond(false);
            var entries = new TaskChildEntry[]
            {
                new("route-a", Task("task-a", "a"), routeA),
                new("route-b", Task("task-b", "b"), routeB)
            };
            var root = new TaskGroupSpec
            (
                "root", TaskExecutionMode.Parallel, TaskGroupCond.Any(), entries,
                TaskContinuationMode.ContinueWhileActive
            );
            var engine = Create(new ObjectiveSpec("route", 1, root));
            engine.Activate();
            Assert.That(engine.EditProgress("b", values => values.AddI("b", 1)), Is.False);
            Assert.That(engine.EditProgress("a", values => values.AddI("a", 1)), Is.True);
            Assert.That(engine.Evaluate().RootResult, Is.EqualTo(TaskResult.Succeeded));

            routeA.Value = false;
            routeB.Value = true;
            var alternate = engine.Evaluate();
            Assert.That(alternate.RootResult, Is.EqualTo(TaskResult.Pending));
            Assert.That(alternate.TryGetEntry("route-a", out var ignored), Is.True);
            Assert.That(ignored.Participating, Is.False);
            Assert.That(ignored.EffectiveResult, Is.EqualTo(TaskResult.Pending));
            Assert.That(alternate.TryGetNode("task-a", out var raw), Is.True);
            Assert.That(raw.Participating, Is.False);
            Assert.That(raw.RawResult, Is.EqualTo(TaskResult.Succeeded));
            Assert.That(engine.EditProgress("a", values => values.AddI("a", 1)), Is.False);

            routeA.Value = true;
            routeB.Value = false;
            Assert.That(engine.Evaluate().RootResult, Is.EqualTo(TaskResult.Succeeded));
            int current = engine.Instance.Progress.Values.GetI("a");
            Assert.That(current, Is.EqualTo(1));
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 상위 Group의 Stop은 하위 Continue보다 입력 제한 우선권을 가진다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveEngine_상위Stop은하위Continue를차단()
        {
            var inner = new TaskGroupSpec
            (
                "inner", TaskExecutionMode.Parallel, TaskGroupCond.Any(),
                new TaskChildEntry[]
                {
                    new("a-entry", Task("task-a", "a")),
                    new("b-entry", Task("task-b", "b"))
                },
                TaskContinuationMode.ContinueWhileActive
            );
            var outer = new TaskChildEntry[]
            {
                new("inner-entry", inner)
            };
            var root = new TaskGroupSpec
            (
                "root", TaskExecutionMode.Parallel, TaskGroupCond.All(),
                outer, TaskContinuationMode.StopOnSucceeded
            );
            var engine = Create(new ObjectiveSpec("stop", 1, root));
            engine.Activate();
            engine.EditProgress("a", values => values.AddI("a", 1));
            var snapshot = engine.Evaluate();

            Assert.That(snapshot.RootResult, Is.EqualTo(TaskResult.Succeeded));
            Assert.That(snapshot.TryGetEntry("inner-entry", out var parent), Is.True);
            Assert.That(parent.EffectiveResult, Is.EqualTo(TaskResult.Succeeded));
            Assert.That(parent.CanAcceptInput, Is.False);
            Assert.That(snapshot.TryGetEntry("b-entry", out var child), Is.True);
            Assert.That(child.Participating, Is.True);
            Assert.That(child.CanAcceptInput, Is.False);
            Assert.That(engine.EditProgress("b", values => values.AddI("b", 1)), Is.False);
            Assert.That(engine.Evaluate().RootResult, Is.EqualTo(TaskResult.Succeeded));
        }

    #endregion

    #region 원자성·재진입·시간

        // ------------------------------------------------------------
        /// <summary>
        /// 사건 후 Condition 예외가 발생해도 영속 값과 알림은 변하지 않는다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveEngine_사후Condition예외는커밋취소()
        {
            var owned = new string[]
            {
                "a"
            };
            var task = new TaskSpec("root", new CountException(), localProgressIDs: owned);
            var engine = Create(new ObjectiveSpec("throw", 1, task));
            int events = 0;
            engine.Changed += () => events++;
            engine.Activate();
            string before = JsonUtility.ToJson(engine.Instance);

            Assert.Throws<InvalidOperationException>(() => engine.EditProgress("a", values => values.AddI("a", 1)));
            Assert.That(JsonUtility.ToJson(engine.Instance), Is.EqualTo(before));
            Assert.That(events, Is.EqualTo(1));
            int current = engine.Instance.Progress.Values.GetI("a");
            Assert.That(current, Is.Zero);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Condition의 Engine 재진입은 원본 변경 없이 거부한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveEngine_Condition재진입보호()
        {
            ObjectiveEngine engine = null;
            var reentry = new Reentrant(() => engine.Evaluate());
            var owned = new string[]
            {
                "a"
            };
            var task = new TaskSpec("root", reentry, localProgressIDs: owned);
            engine = Create(new ObjectiveSpec("reentry", 1, task));

            Assert.Throws<InvalidOperationException>(() => engine.Activate());
            Assert.That(engine.Instance.Status, Is.EqualTo(ObjectiveStatus.Inactive));
        }

        private sealed class Reentrant : ICond<ObjectiveContext>
        {
            private readonly Action action;

            // ------------------------------------------------------------
            /// <summary>
            /// Engine 재진입 콜백을 보관한다.
            /// </summary>
            // ------------------------------------------------------------
            public Reentrant(Action action)
            {
                this.action = action;
            }

            // ------------------------------------------------------------
            /// <summary>
            /// Condition 평가 중 Engine 재진입을 유발한다.
            /// </summary>
            // ------------------------------------------------------------
            public bool Check(ObjectiveContext context)
            {
                action();
                return false;
            }
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 커밋 후 알림에서 최신 값을 조회할 수 있다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveEngine_알림은커밋후발행()
        {
            var engine = Create(Triple(TaskContinuationMode.ContinueWhileActive));
            engine.Activate();
            bool observed = false;
            int notifications = 0;
            engine.Changed += () =>
            {
                notifications++;
                int current = engine.Instance.Progress.Values.GetI("a");
                Assert.That(current, Is.EqualTo(1));
                observed = engine.Evaluate().RootResult == TaskResult.Pending;
            };

            Assert.That(engine.EditProgress("a", values => values.AddI("a", 1)), Is.True);
            Assert.That(observed, Is.True);
            Assert.That(notifications, Is.EqualTo(1));
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 외부 경과 시간은 명시 요청에서만 누적되며 자동 최종 확정은 R6이다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveEngine_경과시간과읽기전용Refresh()
        {
            var task = new TaskSpec("root", new Elapsed());
            var engine = Create(new ObjectiveSpec("time", 1, task));
            int events = 0;
            engine.Changed += () => events++;

            Assert.That(engine.AdvanceTime(1), Is.False);
            Assert.That(engine.Activate(), Is.True);
            Assert.That(engine.AdvanceTime(1), Is.True);
            Assert.That(engine.Evaluate().RootResult, Is.EqualTo(TaskResult.Pending));
            Assert.That(engine.AdvanceTime(1), Is.True);
            Assert.That(engine.Evaluate().RootResult, Is.EqualTo(TaskResult.Succeeded));
            Assert.That(engine.Instance.ElapsedTime, Is.EqualTo(2));
            Assert.That(engine.Instance.Status, Is.EqualTo(ObjectiveStatus.Active));
            Assert.That(events, Is.EqualTo(3));
            Assert.Throws<ArgumentOutOfRangeException>(() => engine.AdvanceTime(double.NaN));
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 잘못된 ID와 다른 Progress 종류 입력은 변경 없이 거부한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveEngine_잘못된명령과Terminal입력차단()
        {
            var owned = new string[]
            {
                "m"
            };
            var task = new TaskSpec("root", new FlagCond("m"), localProgressIDs: owned);
            var progress = new string[]
            {
                "m"
            };
            var engine = Create(new ObjectiveSpec("manual", 1, task));

            Assert.Throws<ArgumentException>(() => engine.EditProgress("missing", values => values.SetFlag("missing")));
            Assert.That(engine.EditProgress("m", values => values.SetFlag("m")), Is.False);
            engine.Activate();
            // 정의되지 않은 Key도 Registry에 자유롭게 기록할 수 있다.
            Assert.That(engine.EditProgress("m", values => values.AddI("m", 1)), Is.True);
            Assert.That(engine.EditProgress("m", values => values.Remove("m")), Is.True);
            Assert.That(engine.EditProgress("m", values => values.SetFlag("m")), Is.True);
            Assert.Throws<InvalidOperationException>(() => engine.EditProgress("m", values => values.AddI("m", 1)));
            Assert.Throws<InvalidOperationException>(() => engine.EditProgress("m", values => values.SetI("unowned", 1)));
            Assert.That(engine.Instance.Progress.Values.Has("unowned"), Is.False);
            Assert.That(engine.EditProgress("m", values => values.SetFlag("m")), Is.False);

            engine.Instance.Apply(candidate => candidate.ChangeStatus(ObjectiveStatus.Succeeded));
            Assert.That(engine.EditProgress("m", values => values.SetFlag("m")), Is.False);
            Assert.That(engine.Instance.IsTerminal, Is.True);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 복원 후 정확한 Registry Spec을 연결하고 재확정하지 않는다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveEngine_정의Resolver와복원재평가()
        {
            var spec = Triple(TaskContinuationMode.ContinueWhileActive);
            var initial = Create(spec);
            initial.Activate();
            initial.EditProgress("a", values => values.AddI("a", 1));
            string json = JsonUtility.ToJson(initial.Instance);
            var restored = JsonUtility.FromJson<ObjectiveInstance>(json);
            var registry = new ObjectiveSpecRegistry();
            registry.Register(spec);
            int changed = 0;
            var loaded = new ObjectiveEngine(registry, restored);
            loaded.Changed += () => changed++;

            Assert.That(loaded.Evaluate().RootResult, Is.EqualTo(TaskResult.Pending));
            Assert.That(changed, Is.Zero);
            Assert.That(loaded.Instance.Status, Is.EqualTo(ObjectiveStatus.Active));
            Assert.That(loaded.EditProgress("b", values => values.AddI("b", 1)), Is.True);
            Assert.That(loaded.Evaluate().RootResult, Is.EqualTo(TaskResult.Succeeded));
            Assert.That(initial.Evaluate().RootResult, Is.EqualTo(TaskResult.Pending));
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// 변화가 없는 명령은 같은 상태를 다시 평가하지 않고 조건을 한 번만 호출한다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveEngine_명령한번에조건은한번만평가한다()
        {
            var toggle = new ToggleCond(false);
            var engine = Create(new ObjectiveSpec("count", 1, new TaskSpec("root", toggle)));
            engine.Activate();
            int before = toggle.Calls;

            Assert.That(engine.Refresh(), Is.False);
            Assert.That(toggle.Calls - before, Is.EqualTo(1));
        }

    #endregion

    #region 종료 성공·실패 Commit

        // ------------------------------------------------------------
        /// <summary>
        /// 성공 Automatic은 처음 활성화한 평가에서 바로 확정한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveEngine_AutomaticSuccess()
        {
            var engine = CreateSample(new ToggleCond(true));
            int changed = 0;
            engine.Changed += () => changed++;

            Assert.That(engine.Activate(), Is.True);
            Assert.That(engine.Instance.Status, Is.EqualTo(ObjectiveStatus.Succeeded));
            Assert.That(engine.Instance.IsTerminal, Is.True);
            Assert.That(changed, Is.EqualTo(1));
            Assert.That(engine.Submit(), Is.EqualTo(ObjectiveSubmitResult.None));
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Root 실패가 완료보다 우선하며 Automatic 실패가 확정된다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveEngine_AutomaticFailure우선()
        {
            var engine = CreateSample(new ToggleCond(true), new ToggleCond(true));
            Assert.That(engine.Activate(), Is.True);
            Assert.That(engine.Instance.Status, Is.EqualTo(ObjectiveStatus.Failed));
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 성공 Manual은 완료 판정을 보존하되 Status를 유지한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveEngine_ManualSuccess()
        {
            var engine = CreateSample(new ToggleCond(true), successMode: ObjectiveCommitMode.Manual);
            Assert.That(engine.Activate(), Is.True);
            Assert.That(engine.Instance.Status, Is.EqualTo(ObjectiveStatus.Active));
            Assert.That(engine.Evaluate().RootResult, Is.EqualTo(TaskResult.Succeeded));
            Assert.That(engine.Submit(), Is.EqualTo(ObjectiveSubmitResult.Succeeded));
            Assert.That(engine.Instance.Status, Is.EqualTo(ObjectiveStatus.Succeeded));
            Assert.That(engine.Submit(), Is.EqualTo(ObjectiveSubmitResult.None));
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 실패 Manual은 실패 원인이 사라지면 다시 성공할 수 있다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveEngine_ManualFailure후Recovery()
        {
            var success = new ToggleCond(false);
            var failure = new ToggleCond(true);
            var engine = CreateSample
            (
                success, failure,
                successMode: ObjectiveCommitMode.Automatic,
                failureMode: ObjectiveCommitMode.Manual
            );
            engine.Activate();

            Assert.That(engine.Instance.Status, Is.EqualTo(ObjectiveStatus.Active));
            Assert.That(engine.Evaluate().RootResult, Is.EqualTo(TaskResult.Failed));
            failure.Value = false;
            success.Value = true;
            Assert.That(engine.Refresh(), Is.True);
            Assert.That(engine.Instance.Status, Is.EqualTo(ObjectiveStatus.Succeeded));
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 양쪽 Manual의 실패 후보는 제출 시 Failed로 확정한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveEngine_ManualBothFailureSubmit()
        {
            var engine = CreateSample
            (
                new ToggleCond(true), new ToggleCond(true),
                successMode: ObjectiveCommitMode.Manual,
                failureMode: ObjectiveCommitMode.Manual
            );
            engine.Activate();
            Assert.That(engine.Instance.Status, Is.EqualTo(ObjectiveStatus.Active));
            Assert.That(engine.Submit(), Is.EqualTo(ObjectiveSubmitResult.Failed));
            Assert.That(engine.Instance.Status, Is.EqualTo(ObjectiveStatus.Failed));
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 두 정책 중 실패 Automatic은 성공 Manual보다 우선한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveEngine_FailureAutoSuccessManual()
        {
            var engine = CreateSample(new ToggleCond(true), new ToggleCond(true), successMode: ObjectiveCommitMode.Manual);
            engine.Activate();
            Assert.That(engine.Instance.Status, Is.EqualTo(ObjectiveStatus.Failed));
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 조건을 만족하지 않는 Submit은 영속값을 변경하지 않는다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveEngine_NoneSubmit()
        {
            var complete = new ToggleCond(false);
            var engine = CreateSample(complete, successMode: ObjectiveCommitMode.Manual);
            Assert.That(engine.Submit(), Is.EqualTo(ObjectiveSubmitResult.None));
            engine.Activate();
            Assert.That(engine.Submit(), Is.EqualTo(ObjectiveSubmitResult.None));
            Assert.That(engine.Instance.Status, Is.EqualTo(ObjectiveStatus.Active));
            complete.Value = true;
            Assert.That(engine.Submit(), Is.EqualTo(ObjectiveSubmitResult.Succeeded));
        }

    #endregion

    #region Force·Repeat

        // ------------------------------------------------------------
        /// <summary>
        /// Force는 Condition을 읽지 않고 Terminal 불변식은 지킨다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveEngine_ForceBypass와Terminal()
        {
            var engine = CreateSample(new ThrowCond("Condition must not run for Force."));
            engine.Instance.Apply(candidate => candidate.ChangeStatus(ObjectiveStatus.Active));
            int changed = 0;
            engine.Changed += () => changed++;

            Assert.That(engine.Force(ObjectiveStatus.Succeeded), Is.True);
            Assert.That(engine.Instance.Status, Is.EqualTo(ObjectiveStatus.Succeeded));
            Assert.That(changed, Is.EqualTo(1));
            Assert.That(engine.Force(ObjectiveStatus.Succeeded), Is.False);
            Assert.Throws<InvalidOperationException>(() => engine.Force(ObjectiveStatus.Failed));
            Assert.Throws<ArgumentOutOfRangeException>(() => engine.Force(ObjectiveStatus.Active));
            Assert.That(engine.Instance.Status, Is.EqualTo(ObjectiveStatus.Succeeded));
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Cancel은 시작 전 허용되지만 Terminal은 재개방하지 않는다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveEngine_CancelInactive()
        {
            var engine = CreateSample(new ToggleCond(false));
            Assert.That(engine.Force(ObjectiveStatus.Cancelled), Is.True);
            Assert.That(engine.Instance.IsTerminal, Is.True);
            Assert.That(engine.Force(ObjectiveStatus.Cancelled), Is.False);
            Assert.Throws<InvalidOperationException>(() => engine.Activate());
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Repeatable은 종료 후 별도 InstanceID를 가진 빈 실행을 만든다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveEngine_RepeatAfterTerminal()
        {
            var engine = CreateSample(new ToggleCond(false), repeatMode: ObjectiveRepeatMode.Repeatable);
            Assert.Throws<InvalidOperationException>(() => engine.CreateRepeat("second"));
            engine.Activate();
            engine.Force(ObjectiveStatus.Failed);
            var repeated = engine.CreateRepeat("second");

            Assert.That(repeated.Instance.InstanceID, Is.EqualTo("second"));
            Assert.That(repeated.Instance.ScopeID, Is.EqualTo("scope"));
            Assert.That(repeated.Instance.Status, Is.EqualTo(ObjectiveStatus.Inactive));
            Assert.That(engine.Instance.Status, Is.EqualTo(ObjectiveStatus.Failed));
            Assert.Throws<ArgumentException>(() => engine.CreateRepeat("first"));
            Assert.That(repeated.Instance, Is.Not.SameAs(engine.Instance));
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Once 정책은 종료 후에도 재생성을 허용하지 않는다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveEngine_Once는Repeat거부()
        {
            var engine = CreateSample(new ToggleCond(true));
            engine.Activate();
            Assert.Throws<InvalidOperationException>(() => engine.CreateRepeat("second"));
        }

    #endregion

    #region 종료 뒤 파생 평가·이벤트

        // ------------------------------------------------------------
        /// <summary>
        /// 외부 사실로 평가가 바뀌면 영속 변경 없이 별도 이벤트가 발생한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveEngine_외부Fact파생평가Changed()
        {
            var complete = new ToggleCond(false);
            var engine = CreateSample(complete, successMode: ObjectiveCommitMode.Manual);
            int stateEvents = 0;
            int viewEvents = 0;
            engine.Changed += () => stateEvents++;
            engine.EvaluationChanged += _ => viewEvents++;
            engine.Activate();
            int baseline = viewEvents;
            stateEvents = 0;

            complete.Value = true;
            Assert.That(engine.Evaluate().RootResult, Is.EqualTo(TaskResult.Succeeded));
            Assert.That(engine.Refresh(), Is.False);
            Assert.That(stateEvents, Is.Zero);
            Assert.That(viewEvents, Is.EqualTo(baseline + 1));
            Assert.That(engine.Refresh(), Is.False);
            Assert.That(viewEvents, Is.EqualTo(baseline + 1));
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Changed에서 후속 명령이 발생해도 이벤트가 재귀로 중첩되지 않는다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveEngine_Changed재진입알림순서()
        {
            var engine = CreateSample(new ToggleCond(false), successMode: ObjectiveCommitMode.Manual);
            var order = new List<string>();
            bool inside = false;
            bool nested = false;
            bool next = true;
            engine.Changed += () =>
            {
                if (inside)
                {
                    nested = true;
                }

                inside = true;
                order.Add("changed");
                if (next)
                {
                    next = false;
                    engine.AdvanceTime(1);
                }

                inside = false;
            };
            engine.EvaluationChanged += _ => order.Add("evaluation");

            engine.Activate();
            Assert.That(nested, Is.False);
            Assert.That(engine.Instance.ElapsedTime, Is.EqualTo(1));
            var expected = new string[]
            {
                "changed",
                "evaluation",
                "changed"
            };
            Assert.That(order, Is.EqualTo(expected));
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 구독 예외가 발생해도 완료된 영속 커밋은 취소되지 않는다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveEngine_구독예외커밋불변()
        {
            var engine = CreateSample(new ToggleCond(false));
            engine.Changed += () => throw new InvalidOperationException("Subscriber failed.");
            Assert.Throws<InvalidOperationException>(() => engine.Activate());
            Assert.That(engine.Instance.Status, Is.EqualTo(ObjectiveStatus.Active));
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 한 구독자가 실패해도 같은 명령의 나머지 알림은 모두 전달된다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveEngine_구독예외에도남은알림을모두전달한다()
        {
            var engine = CreateSample(new ToggleCond(false));
            int evaluations = 0;
            engine.Changed += () => throw new InvalidOperationException("Subscriber failed.");
            engine.EvaluationChanged += _ => evaluations++;

            Assert.Throws<InvalidOperationException>(() => engine.Activate());
            Assert.That(engine.Instance.Status, Is.EqualTo(ObjectiveStatus.Active));
            Assert.That(evaluations, Is.EqualTo(1));
        }

        // ------------------------------------------------------------
        /// <summary>
        /// <br/>구독자 둘이 실패하면 둘 다 AggregateException으로 전달한다.
        /// <br/>커밋은 그대로 유지된다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveEngine_구독예외가둘이면AggregateException으로전달한다()
        {
            var engine = CreateSample(new ToggleCond(false));
            engine.Changed += () => throw new InvalidOperationException("first");
            engine.StatusChanged += (before, after) => throw new InvalidOperationException("second");

            var thrown = Assert.Throws<AggregateException>(() => engine.Activate());

            Assert.That(thrown.InnerExceptions.Count, Is.EqualTo(2));
            Assert.That(engine.Instance.Status, Is.EqualTo(ObjectiveStatus.Active));
        }

        // ------------------------------------------------------------
        /// <summary>
        /// <br/>Fact Scope 닫기가 실패하면 명령은 변경 없이 실패한다.
        /// <br/>나중 명령에 지난 알림이 섞여 나오지 않는다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveEngine_FactScope닫기실패는변경과알림이없다()
        {
            var scope = new ClosingFailure();
            var engine = CreateSample(new ToggleCond(false), factScope: scope);
            int changed = 0;
            engine.Changed += () => changed++;

            scope.FailNextClose = true;
            Assert.Throws<InvalidOperationException>(() => engine.Activate());
            Assert.That(engine.Instance.Status, Is.EqualTo(ObjectiveStatus.Inactive));
            Assert.That(changed, Is.EqualTo(0));

            Assert.That(engine.Refresh(), Is.False);
            Assert.That(changed, Is.EqualTo(0));
            Assert.That(engine.Activate(), Is.True);
            Assert.That(changed, Is.EqualTo(1));
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 입력·평가·제출이 각각 한 번의 Fact Scope를 열고 닫는다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveEngine_외부FactScope의요청단위경계()
        {
            var fact = new ScopedFact();
            var engine = CreateSample
            (
                fact, successMode: ObjectiveCommitMode.Manual,
                factScope: fact
            );

            Assert.That(engine.Activate(), Is.True);
            Assert.That(fact.Opened, Is.EqualTo(1));
            Assert.That(fact.Closed, Is.EqualTo(1));
            fact.Value = true;
            Assert.That(engine.Evaluate().RootResult, Is.EqualTo(TaskResult.Succeeded));
            Assert.That(fact.Opened, Is.EqualTo(2));
            Assert.That(fact.Closed, Is.EqualTo(2));
            Assert.That(engine.Submit(), Is.EqualTo(ObjectiveSubmitResult.Succeeded));
            Assert.That(fact.Opened, Is.EqualTo(3));
            Assert.That(fact.Closed, Is.EqualTo(3));
        }

    #endregion

    #region Sequential 최초 개방·Continue

        // ------------------------------------------------------------
        /// <summary>
        /// A 완료 전에는 B가 닫히며 읽기 평가로 경계가 저장되지 않는다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveEngine_최초개방과입력차단()
        {
            var spec = Sequence
            (
                new TaskChildEntry("a-entry", Task("a-node", "a", 2)),
                new TaskChildEntry("b-entry", Task("b-node", "b", 2)),
                Counters("a", "b")
            );
            var engine = Create(spec);

            Assert.That(engine.Evaluate().TryGetEntry("a-entry", out var first), Is.True);
            Assert.That(first.Participating, Is.True);
            Assert.That(engine.Instance.TryGetBoundary("root", out _), Is.False);
            Assert.That(engine.Activate(), Is.True);
            Assert.That(engine.Instance.TryGetBoundary("root", out string initial), Is.True);
            Assert.That(initial, Is.EqualTo("a-entry"));
            Assert.That(engine.EditProgress("b", values => values.AddI("b", 1)), Is.False);
            engine.EditProgress("a", values => values.AddI("a", 2));
            Assert.That(engine.Instance.TryGetBoundary("root", out string next), Is.True);
            Assert.That(next, Is.EqualTo("b-entry"));
            Assert.That(engine.Evaluate().TryGetEntry("a-entry", out first), Is.True);
            Assert.That(first.Participating, Is.True);
            Assert.That(engine.Evaluate().TryGetEntry("b-entry", out var opened), Is.True);
            Assert.That(opened.CanAcceptInput, Is.True);
            Assert.That(engine.EditProgress("b", values => values.AddI("b", 1)), Is.True);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Group Min2 후에는 D 개방과 선택 C 진행이 병행된다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveEngine_S04_Continue그룹의C와D병행()
        {
            var abc = new TaskGroupSpec
            (
                "abc", TaskExecutionMode.Parallel, TaskGroupCond.MinSucceeded(2),
                new TaskChildEntry[]
                {
                    new("a-entry", Task("a-node", "a", 2)),
                    new("b-entry", Task("b-node", "b", 2)),
                    new("c-entry", Task("c-node", "c", 2))
                },
                TaskContinuationMode.ContinueWhileActive
            );
            var spec = Sequence
            (
                new TaskChildEntry("group-entry", abc),
                new TaskChildEntry("d-entry", Task("d-node", "d", 2)),
                Counters("a", "b", "c", "d")
            );
            var engine = Create(spec);
            engine.Activate();

            Assert.That(engine.EditProgress("d", values => values.AddI("d", 1)), Is.False);
            engine.EditProgress("a", values => values.AddI("a", 2));
            engine.EditProgress("b", values => values.AddI("b", 2));
            var state = engine.Evaluate();

            Assert.That(state.TryGetEntry("group-entry", out var group), Is.True);
            Assert.That(group.EffectiveResult, Is.EqualTo(TaskResult.Succeeded));
            Assert.That(state.TryGetEntry("c-entry", out var c), Is.True);
            Assert.That(c.CanAcceptInput, Is.True);
            Assert.That(state.TryGetEntry("d-entry", out var d), Is.True);
            Assert.That(d.CanAcceptInput, Is.True);
            Assert.That(engine.Instance.TryGetBoundary("root", out string boundary), Is.True);
            Assert.That(boundary, Is.EqualTo("d-entry"));
            Assert.That(engine.EditProgress("c", values => values.AddI("c", 1)), Is.True);
            Assert.That(engine.EditProgress("d", values => values.AddI("d", 1)), Is.True);
            AssertProgress(engine, "c", 1);
            AssertProgress(engine, "d", 1);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 이전 Stop Group의 C 입력만 막고 D의 최초 개방은 유지한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveEngine_S04_Stop은C만차단한다()
        {
            var abc = new TaskGroupSpec
            (
                "abc", TaskExecutionMode.Parallel, TaskGroupCond.MinSucceeded(2),
                new TaskChildEntry[]
                {
                    new("a-entry", Task("a-node", "a", 2)),
                    new("b-entry", Task("b-node", "b", 2)),
                    new("c-entry", Task("c-node", "c", 2))
                }
            );
            var spec = Sequence
            (
                new TaskChildEntry("group-entry", abc),
                new TaskChildEntry("d-entry", Task("d-node", "d", 2)),
                Counters("a", "b", "c", "d")
            );
            var engine = Create(spec);
            engine.Activate();
            engine.EditProgress("a", values => values.AddI("a", 2));
            engine.EditProgress("b", values => values.AddI("b", 2));

            Assert.That(engine.Evaluate().TryGetEntry("c-entry", out var optional), Is.True);
            Assert.That(optional.Participating, Is.True);
            Assert.That(optional.CanAcceptInput, Is.False);
            Assert.That(engine.Evaluate().TryGetEntry("d-entry", out var next), Is.True);
            Assert.That(next.CanAcceptInput, Is.True);
            Assert.That(engine.EditProgress("c", values => values.AddI("c", 1)), Is.False);
            Assert.That(engine.EditProgress("d", values => values.AddI("d", 1)), Is.True);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 선행 Entry 통과 전에는 후속 Eligibility를 검사하지 않는다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveEngine_후속Eligibility는개방전호출하지않는다()
        {
            var prerequisite = new ToggleCond(false);
            var futureEligibility = new ToggleCond(true);
            var second = new TaskChildEntry
            (
                "b-entry", Task("b-node", "b", 2),
                eligibilityCond: futureEligibility
            );
            var spec = Sequence
            (
                new TaskChildEntry("a-entry", FactTask("a-node", prerequisite)),
                second, Counters("b")
            );
            var engine = Create(spec);
            engine.Activate();

            Assert.That(futureEligibility.Calls, Is.Zero);
            Assert.That(engine.Evaluate().TryGetEntry("b-entry", out var before), Is.True);
            Assert.That(before.Participating, Is.False);
            Assert.That(futureEligibility.Calls, Is.Zero);
            prerequisite.Value = true;
            Assert.That(engine.Refresh(), Is.True);
            Assert.That(futureEligibility.Calls, Is.GreaterThan(0));
            Assert.That(engine.Evaluate().TryGetEntry("b-entry", out var after), Is.True);
            Assert.That(after.CanAcceptInput, Is.True);
        }

    #endregion

    #region Sequential OncePassed·Eligibility

        // ------------------------------------------------------------
        /// <summary>
        /// 한 번 유효 통과하면 Raw가 Pending이 돼도 다음 Stage는 유지된다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveEngine_OncePassed는Raw하락에도통과유지()
        {
            var fact = new ToggleCond(true);
            var first = new TaskChildEntry
            (
                "a-entry", FactTask("a-node", fact),
                revalidationMode: TaskRevalidationMode.OncePassed
            );
            var spec = Sequence
            (
                first, new TaskChildEntry("b-entry", Task("b-node", "b", 2)),
                Counters("b")
            );
            var engine = Create(spec);
            engine.Activate();

            Assert.That(engine.Instance.HasPassed("a-entry"), Is.True);
            Assert.That(engine.Instance.TryGetBoundary("root", out string opened), Is.True);
            Assert.That(opened, Is.EqualTo("b-entry"));
            fact.Value = false;
            var evaluated = engine.Evaluate();
            Assert.That(evaluated.TryGetNode("a-node", out var raw), Is.True);
            Assert.That(raw.RawResult, Is.EqualTo(TaskResult.Pending));
            Assert.That(evaluated.TryGetEntry("a-entry", out var effective), Is.True);
            Assert.That(effective.EffectiveResult, Is.EqualTo(TaskResult.Succeeded));
            Assert.That(evaluated.TryGetEntry("b-entry", out var later), Is.True);
            Assert.That(later.CanAcceptInput, Is.True);
            Assert.That(engine.Refresh(), Is.False);
            Assert.That(engine.EditProgress("b", values => values.AddI("b", 1)), Is.True);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Disabled된 OncePassed는 현재 부모 집계에 참가하지 않는다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveEngine_OncePassed의Eligibility변경은기록보존()
        {
            var eligibility = new ToggleCond(true);
            var fact = new ToggleCond(true);
            var first = new TaskChildEntry
            (
                "a-entry", FactTask("a-node", fact),
                eligibility, TaskRevalidationMode.OncePassed
            );
            var spec = Sequence
            (
                first, new TaskChildEntry("b-entry", Task("b-node", "b", 2)),
                Counters("b")
            );
            var engine = Create(spec);
            engine.Activate();
            engine.EditProgress("b", values => values.AddI("b", 1));
            eligibility.Value = false;

            var closed = engine.Evaluate();
            Assert.That(closed.TryGetEntry("a-entry", out var entry), Is.True);
            Assert.That(entry.Participating, Is.False);
            Assert.That(entry.EffectiveResult, Is.EqualTo(TaskResult.Pending));
            Assert.That(closed.TryGetEntry("b-entry", out var child), Is.True);
            Assert.That(child.Participating, Is.False);
            Assert.That(engine.Refresh(), Is.False);
            Assert.That(engine.Instance.HasPassed("a-entry"), Is.True);
            AssertProgress(engine, "b", 1);

            eligibility.Value = true;
            fact.Value = false;
            Assert.That(engine.Evaluate().TryGetEntry("b-entry", out child), Is.True);
            Assert.That(child.CanAcceptInput, Is.True);
            AssertProgress(engine, "b", 1);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 후속 Entry 자신의 Eligibility만 꺼지면 Reset하지 않는다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveEngine_자체Eligibility변경은Reset이아니다()
        {
            var a = new ToggleCond(true);
            var bAllowed = new ToggleCond(true);
            var second = new TaskChildEntry
            (
                "b-entry", Task("b-node", "b", 2),
                bAllowed, rollbackMode: TaskRollbackMode.ResetOnRollback
            );
            var spec = Sequence
            (
                new TaskChildEntry("a-entry", FactTask("a-node", a)),
                second, Counters("b")
            );
            var engine = Create(spec);
            engine.Activate();
            engine.EditProgress("b", values => values.AddI("b", 1));
            bAllowed.Value = false;

            Assert.That(engine.Refresh(), Is.False);
            AssertProgress(engine, "b", 1);
            Assert.That(engine.Instance.TryGetBoundary("root", out string boundary), Is.True);
            Assert.That(boundary, Is.EqualTo("b-entry"));
            bAllowed.Value = true;
            Assert.That(engine.Evaluate().TryGetEntry("b-entry", out var reenabled), Is.True);
            Assert.That(reenabled.CanAcceptInput, Is.True);
            AssertProgress(engine, "b", 1);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// OncePassed 기록이 있더라도 현재 Failed 결과는 완료로 덮지 않는다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveEngine_OncePassed도명시Failed가우선이다()
        {
            var completed = new ToggleCond(true);
            var failed = new ToggleCond(false);
            var task = new TaskSpec("a-node", completed, failed);
            var first = new TaskChildEntry
            (
                "a-entry", task,
                revalidationMode: TaskRevalidationMode.OncePassed
            );
            var second = new TaskChildEntry
            (
                "b-entry", Task("b-node", "b", 2),
                rollbackMode: TaskRollbackMode.ResetOnRollback
            );
            var engine = Create(Sequence(first, second, Counters("b")));
            engine.Activate();
            Assert.That(engine.Instance.HasPassed("a-entry"), Is.True);
            engine.EditProgress("b", values => values.AddI("b", 1));
            failed.Value = true;

            var outcome = engine.Evaluate();
            Assert.That(outcome.TryGetNode("a-node", out var raw), Is.True);
            Assert.That(raw.RawResult, Is.EqualTo(TaskResult.Failed));
            Assert.That(outcome.TryGetEntry("a-entry", out var effective), Is.True);
            Assert.That(effective.EffectiveResult, Is.EqualTo(TaskResult.Failed));
            Assert.That(outcome.TryGetEntry("b-entry", out var closed), Is.True);
            Assert.That(closed.Participating, Is.False);
            Assert.That(engine.Refresh(), Is.True);
            AssertProgress(engine, "b", 0);
            Assert.That(engine.Instance.HasPassed("a-entry"), Is.True);

            failed.Value = false;
            Assert.That(engine.Evaluate().TryGetEntry("b-entry", out var reopened), Is.True);
            Assert.That(reopened.CanAcceptInput, Is.True);
        }

    #endregion

    #region Sequential WhileValid·Rollback

        // ------------------------------------------------------------
        /// <summary>
        /// 적법 선행이 무효화될 때 Reset을 딱 한 번 수행한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveEngine_WhileValid후퇴는Reset한번()
        {
            var fact = new ToggleCond(true);
            var second = new TaskChildEntry
            (
                "b-entry", Task("b-node", "b", 2),
                rollbackMode: TaskRollbackMode.ResetOnRollback
            );
            var spec = Sequence
            (
                new TaskChildEntry("a-entry", FactTask("a-node", fact)),
                second, Counters("b")
            );
            var engine = Create(spec);
            engine.Activate();
            engine.EditProgress("b", values => values.AddI("b", 1));
            fact.Value = false;

            AssertProgress(engine, "b", 1);
            Assert.That(engine.Evaluate().TryGetEntry("b-entry", out var closed), Is.True);
            Assert.That(closed.Participating, Is.False);
            int events = 0;
            engine.Changed += () => events++;
            Assert.That(engine.Refresh(), Is.True);
            AssertProgress(engine, "b", 0);
            Assert.That(engine.Instance.TryGetBoundary("root", out string boundary), Is.True);
            Assert.That(boundary, Is.EqualTo("a-entry"));
            string saved = JsonUtility.ToJson(engine.Instance);
            Assert.That(engine.Refresh(), Is.False);
            Assert.That(JsonUtility.ToJson(engine.Instance), Is.EqualTo(saved));
            Assert.That(events, Is.EqualTo(1));

            fact.Value = true;
            Assert.That(engine.Refresh(), Is.True);
            Assert.That(engine.Evaluate().TryGetEntry("b-entry", out var resumed), Is.True);
            Assert.That(resumed.CanAcceptInput, Is.True);
            AssertProgress(engine, "b", 0);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Preserve 후퇴는 개방 경계만 되돌리고 B Progress는 유지한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveEngine_Preserve후퇴는값을보존()
        {
            var fact = new ToggleCond(true);
            var spec = Sequence
            (
                new TaskChildEntry("a-entry", FactTask("a-node", fact)),
                new TaskChildEntry("b-entry", Task("b-node", "b", 2)),
                Counters("b")
            );
            var engine = Create(spec);
            engine.Activate();
            engine.EditProgress("b", values => values.AddI("b", 1));
            fact.Value = false;

            Assert.That(engine.Refresh(), Is.True);
            AssertProgress(engine, "b", 1);
            Assert.That(engine.Instance.TryGetBoundary("root", out string boundary), Is.True);
            Assert.That(boundary, Is.EqualTo("a-entry"));
            fact.Value = true;
            Assert.That(engine.Evaluate().TryGetEntry("b-entry", out var resumed), Is.True);
            Assert.That(resumed.CanAcceptInput, Is.True);
            AssertProgress(engine, "b", 1);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Reset 대상 Group의 하위 Progress·OncePassed·경계만 지운다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveEngine_중첩GroupReset은로컬값과이력만초기화()
        {
            var fact = new ToggleCond(true);
            var inner = new TaskGroupSpec
            (
                "inner", TaskExecutionMode.Sequential, TaskGroupCond.All(),
                new TaskChildEntry[]
                {
                    new
                    (
                        "b-entry", Task("b-node", "b", 2),
                        revalidationMode: TaskRevalidationMode.OncePassed
                    ),
                    new
                    (
                        "c-entry", Task("c-node", "c", 2),
                        rollbackMode: TaskRollbackMode.ResetOnRollback
                    )
                }
            );
            var second = new TaskChildEntry
            (
                "inner-entry", inner,
                rollbackMode: TaskRollbackMode.ResetOnRollback
            );
            var spec = Sequence
            (
                new TaskChildEntry("a-entry", FactTask("a-node", fact)),
                second, Counters("b", "c")
            );
            var engine = Create(spec);
            engine.Activate();
            engine.EditProgress("b", values => values.AddI("b", 2));
            engine.EditProgress("c", values => values.AddI("c", 1));
            Assert.That(engine.Instance.HasPassed("b-entry"), Is.True);
            Assert.That(engine.Instance.TryGetBoundary("inner", out string before), Is.True);
            Assert.That(before, Is.EqualTo("c-entry"));

            fact.Value = false;
            Assert.That(engine.Refresh(), Is.True);
            AssertProgress(engine, "b", 0);
            AssertProgress(engine, "c", 0);
            Assert.That(engine.Instance.HasPassed("b-entry"), Is.False);
            Assert.That(engine.Instance.TryGetBoundary("inner", out _), Is.False);
            Assert.That(engine.Instance.TryGetBoundary("root", out string after), Is.True);
            Assert.That(after, Is.EqualTo("a-entry"));
            Assert.That(engine.Refresh(), Is.False);

            fact.Value = true;
            Assert.That(engine.Refresh(), Is.True);
            Assert.That(engine.Evaluate().TryGetEntry("b-entry", out var reopened), Is.True);
            Assert.That(reopened.CanAcceptInput, Is.True);
            Assert.That(engine.Evaluate().TryGetEntry("c-entry", out var later), Is.True);
            Assert.That(later.CanAcceptInput, Is.False);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 두 계층 Sequential의 후퇴 경계가 각각 독립적으로 재계산된다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveEngine_중첩두단계후퇴는경계를각자복구()
        {
            var fact = new ToggleCond(true);
            var inner = new TaskGroupSpec
            (
                "inner", TaskExecutionMode.Sequential, TaskGroupCond.All(),
                new TaskChildEntry[]
                {
                    new("a-entry", FactTask("a-node", fact)),
                    new
                    (
                        "b-entry", Task("b-node", "b", 2),
                        rollbackMode: TaskRollbackMode.ResetOnRollback
                    )
                }
            );
            var second = new TaskChildEntry
            (
                "d-entry", Task("d-node", "d", 2),
                rollbackMode: TaskRollbackMode.ResetOnRollback
            );
            var spec = Sequence
            (
                new TaskChildEntry("inner-entry", inner),
                second, Counters("b", "d")
            );
            var engine = Create(spec);
            engine.Activate();
            engine.EditProgress("b", values => values.AddI("b", 2));
            engine.EditProgress("d", values => values.AddI("d", 1));
            fact.Value = false;

            Assert.That(engine.Refresh(), Is.True);
            AssertProgress(engine, "b", 0);
            AssertProgress(engine, "d", 0);
            Assert.That(engine.Instance.TryGetBoundary("inner", out string innerBoundary), Is.True);
            Assert.That(innerBoundary, Is.EqualTo("a-entry"));
            Assert.That(engine.Instance.TryGetBoundary("root", out string rootBoundary), Is.True);
            Assert.That(rootBoundary, Is.EqualTo("inner-entry"));
            Assert.That(engine.Refresh(), Is.False);
        }

    #endregion

    #region Sequential Save/Load·원자 예외

        // ------------------------------------------------------------
        /// <summary>
        /// 재바인딩 조회는 비발행이며 다음 Refresh에만 후퇴를 확정한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveEngine_Save복원후명시Refresh로Reset한번()
        {
            var fact = new ToggleCond(true);
            var second = new TaskChildEntry
            (
                "b-entry", Task("b-node", "b", 2),
                rollbackMode: TaskRollbackMode.ResetOnRollback
            );
            var spec = Sequence
            (
                new TaskChildEntry("a-entry", FactTask("a-node", fact)),
                second, Counters("b")
            );
            var first = Create(spec);
            first.Activate();
            first.EditProgress("b", values => values.AddI("b", 1));
            string saved = JsonUtility.ToJson(first.Instance);
            var restored = JsonUtility.FromJson<ObjectiveInstance>(saved);
            fact.Value = false;
            var registry = new ObjectiveSpecRegistry();
            registry.Register(spec);
            var loaded = new ObjectiveEngine(registry, restored);
            int notifications = 0;
            loaded.Changed += () => notifications++;

            Assert.That(loaded.Instance.TryGetBoundary("root", out string boundary), Is.True);
            Assert.That(boundary, Is.EqualTo("b-entry"));
            Assert.That(loaded.Evaluate().TryGetEntry("b-entry", out var disabled), Is.True);
            Assert.That(disabled.Participating, Is.False);
            AssertProgress(loaded, "b", 1);
            Assert.That(notifications, Is.Zero);
            Assert.That(loaded.Refresh(), Is.True);
            AssertProgress(loaded, "b", 0);
            Assert.That(loaded.Refresh(), Is.False);
            Assert.That(notifications, Is.EqualTo(1));
            AssertProgress(first, "b", 1);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 잘못된 통과·Group/Entry 경계는 복원 바인딩에서 거부한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveEngine_잘못된영속이력은Engine이거부()
        {
            var fact = new ToggleCond(true);
            var first = new TaskChildEntry
            (
                "a-entry", FactTask("a-node", fact),
                revalidationMode: TaskRevalidationMode.OncePassed
            );
            var spec = Sequence
            (
                first, new TaskChildEntry("b-entry", Task("b-node", "b", 2)),
                Counters("b")
            );
            var invalidPassed = new ObjectiveInstance("sequence", 1, "run", "world");
            Action<ObjectiveInstance> markInvalid = candidate =>
            {
                candidate.ChangeStatus(ObjectiveStatus.Active);
                candidate.MarkPassed("not-an-entry");
            };
            invalidPassed.Apply(markInvalid);
            Assert.Throws<InvalidOperationException>(() => new ObjectiveEngine(spec, invalidPassed));

            var invalidBoundary = new ObjectiveInstance("sequence", 1, "run2", "world");
            Action<ObjectiveInstance> setInvalid = candidate =>
            {
                candidate.ChangeStatus(ObjectiveStatus.Active);
                candidate.SetBoundary("root", "not-in-group");
            };
            invalidBoundary.Apply(setInvalid);
            Assert.Throws<InvalidOperationException>(() => new ObjectiveEngine(spec, invalidBoundary));
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 후보 Reset 뒤 Condition 예외가 나면 전체 영속 원본이 유지된다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveEngine_후퇴후Condition예외원자롤백()
        {
            var fact = new ToggleCond(true);
            var injected = new ThrowsAfterReset();
            var owned = new string[]
            {
                "b"
            };
            var b = new TaskSpec("b-node", injected, localProgressIDs: owned);
            var spec = Sequence
            (
                new TaskChildEntry("a-entry", FactTask("a-node", fact)),
                new TaskChildEntry("b-entry", b, rollbackMode: TaskRollbackMode.ResetOnRollback),
                Counters("b")
            );
            var engine = Create(spec);
            engine.Activate();
            engine.EditProgress("b", values => values.AddI("b", 1));
            fact.Value = false;
            injected.Enabled = true;
            string before = JsonUtility.ToJson(engine.Instance);
            int changed = 0;
            engine.Changed += () => changed++;

            Assert.Throws<InvalidOperationException>(() => engine.Refresh());
            Assert.That(JsonUtility.ToJson(engine.Instance), Is.EqualTo(before));
            Assert.That(changed, Is.Zero);
            AssertProgress(engine, "b", 1);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Inactive 또는 Terminal은 Refresh로 영속 상태를 갱신하지 않는다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveEngine_Inactive와TerminalRefresh는무변경()
        {
            var fact = new ToggleCond(true);
            var spec = Sequence
            (
                new TaskChildEntry("a-entry", FactTask("a-node", fact)),
                new TaskChildEntry("b-entry", Task("b-node", "b", 2)),
                Counters("b")
            );
            var engine = Create(spec);
            Assert.That(engine.Refresh(), Is.False);
            Assert.That(engine.Instance.TryGetBoundary("root", out _), Is.False);
            engine.Activate();
            engine.Instance.Apply(candidate => candidate.ChangeStatus(ObjectiveStatus.Cancelled));
            string before = JsonUtility.ToJson(engine.Instance);

            Assert.That(engine.Refresh(), Is.False);
            Assert.That(JsonUtility.ToJson(engine.Instance), Is.EqualTo(before));
        }

    #endregion

    #region Entry 참여 시각

        // ------------------------------------------------------------
        /// <summary>
        /// 선언한 Entry만 참여를 시작한 순간의 경과 시간을 기록한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveEngine_참여를시작한순간을기록한다()
        {
            var fact = new ToggleCond();
            var engine = CreateRace(fact);
            engine.Activate();
            Assert.That(engine.Instance.TryGetOpenTime("b-entry", out _), Is.False);

            engine.AdvanceTime(3);
            fact.Value = true;
            engine.Refresh();

            Assert.That(engine.Instance.TryGetOpenTime("b-entry", out double openTime), Is.True);
            Assert.That(openTime, Is.EqualTo(3));
            Assert.That(engine.Instance.TryGetOpenTime("a-entry", out _), Is.False);
            Assert.That(engine.Instance.EntryOpenings.Count, Is.EqualTo(1));
        }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/>구간 시작부터 제한 시간이 지나면 Condition이 실패를 만들고 Objective 전체 시간과는
        /// <br/>무관하다.
        /// </summary>
        // --------------------------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveEngine_구간제한시간으로실패한다()
        {
            var fact = new ToggleCond
            {
                Value = false
            };
            var engine = CreateRace(fact);
            engine.Activate();
            engine.AdvanceTime(100);
            Assert.That(engine.Instance.Status, Is.EqualTo(ObjectiveStatus.Active));

            fact.Value = true;
            engine.Refresh();
            engine.AdvanceTime(4);
            Assert.That(engine.Instance.Status, Is.EqualTo(ObjectiveStatus.Active));

            engine.AdvanceTime(1);
            Assert.That(engine.Instance.Status, Is.EqualTo(ObjectiveStatus.Failed));
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 참여를 멈추면 기록을 지우고 다시 열릴 때 새 시각으로 잰다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveEngine_참여를멈추면지우고다시열리면새로잰다()
        {
            var fact = new ToggleCond
            {
                Value = true
            };
            var engine = CreateRace(fact);
            engine.Activate();
            engine.AdvanceTime(2);
            Assert.That(engine.Instance.TryGetOpenTime("b-entry", out double first), Is.True);
            Assert.That(first, Is.Zero);

            fact.Value = false;
            engine.Refresh();
            Assert.That(engine.Instance.TryGetOpenTime("b-entry", out _), Is.False);
            Assert.That(engine.Instance.EntryOpenings.Count, Is.Zero);

            engine.AdvanceTime(1);
            fact.Value = true;
            engine.Refresh();
            Assert.That(engine.Instance.TryGetOpenTime("b-entry", out double second), Is.True);
            Assert.That(second, Is.EqualTo(3));
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 선언하지 않은 Entry는 기록하지 않고 Condition도 조회할 수 없다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveEngine_선언하지않으면기록하지않는다()
        {
            var fact = new ToggleCond
            {
                Value = true
            };
            var engine = CreateRace(fact, track: false);
            engine.Activate();
            engine.AdvanceTime(50);

            Assert.That(engine.Instance.EntryOpenings.Count, Is.Zero);
            Assert.That(engine.Instance.Status, Is.EqualTo(ObjectiveStatus.Active));
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 시간을 재지 않는 Entry나 없는 Entry의 기록은 복원 때 거부한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveEngine_알수없는기록은Engine이거부한다()
        {
            var fact = new ToggleCond
            {
                Value = true
            };
            var source = CreateRace(fact);
            var untimed = new ObjectiveInstance("race", 1, "run-untimed", "world");
            untimed.Apply
            (
                candidate =>
                {
                    candidate.ChangeStatus(ObjectiveStatus.Active);
                    candidate.MarkOpened("a-entry");
                }
            );

            Assert.Throws<InvalidOperationException>(() => new ObjectiveEngine(source.Spec, untimed));

            var unknown = new ObjectiveInstance("race", 1, "run-unknown", "world");
            unknown.Apply
            (
                candidate =>
                {
                    candidate.ChangeStatus(ObjectiveStatus.Active);
                    candidate.MarkOpened("missing-entry");
                }
            );

            Assert.Throws<InvalidOperationException>(() => new ObjectiveEngine(source.Spec, unknown));
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 저장 뒤 복원해도 영속한 시작 시각으로 구간 제한 시간이 이어진다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveEngine_저장하고복원해도구간시간이이어진다()
        {
            var fact = new ToggleCond();
            var engine = CreateRace(fact);
            engine.Activate();
            engine.AdvanceTime(2);
            fact.Value = true;
            engine.Refresh();
            engine.AdvanceTime(3);

            var restored = JsonUtility.FromJson<ObjectiveInstance>(JsonUtility.ToJson(engine.Instance));
            var registry = new ObjectiveSpecRegistry();
            registry.Register(engine.Spec);
            var saved = new[]
            {
                restored
            };
            var loaded = ObjectiveRuntimeBinding.Bind(registry, saved)[0];

            Assert.That(loaded.Instance.TryGetOpenTime("b-entry", out double openTime), Is.True);
            Assert.That(openTime, Is.EqualTo(2));
            loaded.AdvanceTime(1);
            Assert.That(loaded.Instance.Status, Is.EqualTo(ObjectiveStatus.Active));
            loaded.AdvanceTime(1);
            Assert.That(loaded.Instance.Status, Is.EqualTo(ObjectiveStatus.Failed));
        }

    #endregion

    #region ForceProgress

        // ----------------------------------------------------------------------
        /// <summary>
        /// 닫힌 Entry와 소유하지 않은 Key도 바꾸지만 일반 입력은 여전히 거부한다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveEngine_닫힌Entry와소유하지않은Key도바꾼다()
        {
            var engine = CreateFlagChain();
            engine.Activate();
            Assert.That(engine.EditProgress("b", values => values.SetFlag("b")), Is.False);
            Assert.That(engine.Instance.Progress.Values.HasFlag("b"), Is.False);

            bool changed = engine.ForceProgress
            (
                values =>
                {
                    values.SetFlag("b");
                    values.AddI("memo", 7);
                }
            );

            Assert.That(changed, Is.True);
            Assert.That(engine.Instance.Progress.Values.HasFlag("b"), Is.True);
            Assert.That(engine.Instance.Progress.Values.GetI("memo"), Is.EqualTo(7));
            Assert.That(engine.Instance.Status, Is.EqualTo(ObjectiveStatus.Active));
            Assert.Throws<InvalidOperationException>(() => engine.EditProgress("a", values => values.AddI("memo", 1)));
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 일반 명령처럼 순차 안정화와 최종 확정을 거치고 알림은 한 번 발행한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveEngine_일반명령과같은확정과알림을거친다()
        {
            var engine = CreateFlagChain();
            engine.Activate();
            int changes = 0;
            engine.Changed += () => changes++;

            engine.ForceProgress
            (
                values =>
                {
                    values.SetFlag("a");
                    values.SetFlag("b");
                }
            );

            Assert.That(engine.Instance.Status, Is.EqualTo(ObjectiveStatus.Succeeded));
            Assert.That(changes, Is.EqualTo(1));
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 값이 달라지지 않으면 false이고 알림도 없다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveEngine_변경이없으면false이다()
        {
            var engine = CreateFlagChain();
            engine.Activate();
            int changes = 0;
            engine.Changed += () => changes++;

            Assert.That(engine.ForceProgress(values => { }), Is.False);
            Assert.That(changes, Is.Zero);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Active가 아닌 실행은 거부하고 Progress는 그대로다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveEngine_Active가아니면거부한다()
        {
            var engine = CreateFlagChain();
            Assert.Throws<InvalidOperationException>(() => engine.ForceProgress(values => values.SetFlag("a")));

            engine.Activate();
            engine.Force(ObjectiveStatus.Cancelled);
            Assert.Throws<InvalidOperationException>(() => engine.ForceProgress(values => values.SetFlag("a")));
            Assert.That(engine.Instance.Progress.Values.HasFlag("a"), Is.False);
            Assert.Throws<ArgumentNullException>(() => engine.ForceProgress(null));
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 수정 도중 예외가 나면 아무것도 바뀌지 않고 알림도 없다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveEngine_예외면변경도알림도없다()
        {
            var engine = CreateFlagChain();
            engine.Activate();
            int changes = 0;
            engine.Changed += () => changes++;

            Action<ValueRegistry> inject = values =>
            {
                values.SetFlag("a");
                throw new InvalidOperationException("injected");
            };

            Assert.Throws<InvalidOperationException>(() => engine.ForceProgress(inject));

            Assert.That(engine.Instance.Progress.Values.HasFlag("a"), Is.False);
            Assert.That(changes, Is.Zero);
            Assert.That(engine.ForceProgress(values => values.SetFlag("a")), Is.True);
        }

    #endregion

    #region Entry 전이 이벤트

        // --------------------------------------------------------------------------------
        /// <summary>
        /// 앞 Entry가 성공하면 그 Entry와, 닫힌 사유가 풀린 다음 Entry가 평가 순서대로 알려진다.
        /// </summary>
        // --------------------------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveEngine_결과가바뀐Entry가순서대로발행된다()
        {
            var engine = CreateChain();
            engine.Activate();
            engine.EditProgress("first", values => values.SetI("first", 1));
            var log = RecordEntries(engine);
            var transitions = new List<TaskEntryTransition>();
            engine.EntryEvaluationChanged += transition => transitions.Add(transition);

            engine.EditProgress("first", values => values.SetI("first", 2));

            var expected = new string[]
            {
                "Changed", "first-entry:Pending->Succeeded", "second-entry:Pending->Pending", "Evaluation"
            };
            Assert.That(log, Is.EqualTo(expected));
            Assert.That(transitions[1].Previous.ClosedBy, Is.EqualTo(TaskClosedBy.Predecessor));
            Assert.That(transitions[1].Next.ClosedBy, Is.EqualTo(TaskClosedBy.None));
            Assert.That(transitions[1].Next.Participating, Is.True);
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/>Status 전이가 있으면 Changed, StatusChanged, Entry 전이,
        /// <br/>EvaluationChanged 순서로 나온다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveEngine_Status뒤Evaluation앞에발행된다()
        {
            var engine = CreateChain();
            engine.Activate();
            engine.EditProgress("first", values => values.SetI("first", 2));
            var log = RecordEntries(engine);

            engine.EditProgress("second", values => values.SetI("second", 1));

            var expected = new string[]
            {
                "Changed", "Active->Succeeded", "second-entry:Pending->Succeeded", "Evaluation"
            };
            Assert.That(log, Is.EqualTo(expected));
        }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/>진행 값만 바뀌어 Entry의 참여·결과·닫힌 사유가 그대로면 Entry 전이도 평가 변경도
        /// <br/>발행하지 않는다.
        /// </summary>
        // --------------------------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveEngine_진행값만바뀌면발행하지않는다()
        {
            var engine = CreateChain();
            engine.Activate();
            var log = RecordEntries(engine);

            engine.EditProgress("first", values => values.SetI("first", 1));

            var expected = new[]
            {
                "Changed"
            };
            Assert.That(log, Is.EqualTo(expected));
        }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// 비교할 이전 평가가 없는 첫 확정(활성화)과 복원(Bind)은 Entry 전이를 발행하지 않는다.
        /// </summary>
        // --------------------------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveEngine_기준평가가없으면발행하지않는다()
        {
            var engine = CreateChain();
            var log = RecordEntries(engine);

            engine.Activate();

            Assert.That(log.FindAll(entry => entry.Contains("-entry")), Is.Empty);

            var registry = new ObjectiveSpecRegistry();
            registry.Register(engine.Spec);
            var saved = new[]
            {
                engine.Instance
            };
            var restored = ObjectiveRuntimeBinding.Bind(registry, saved)[0];
            var restoredLog = RecordEntries(restored);

            Assert.That(restored.Refresh(), Is.False);
            Assert.That(restoredLog, Is.Empty);
        }

    #endregion

    #region Status 전이 이벤트

        // --------------------------------------------------------------------------------
        /// <summary>
        /// 활성화와 자동 성공이 이전·이후 상태와 함께 Changed 뒤 Evaluation 앞에 발행된다.
        /// </summary>
        // --------------------------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveEngine_전이가이전과이후상태로발행된다()
        {
            var engine = CreateCounting();
            var log = RecordStatus(engine);

            engine.Activate();
            var expected = new[]
            {
                "Changed",
                "Inactive->Active",
                "Evaluation"
            };
            Assert.That(log, Is.EqualTo(expected));

            log.Clear();
            engine.EditProgress("count", values => values.SetI("count", 1));
            Assert.That(log, Does.Not.Contain("Inactive->Active"));
            Assert.That(log.FindAll(entry => entry.Contains("->")), Is.Empty);

            log.Clear();
            engine.EditProgress("count", values => values.SetI("count", 2));
            var expected2 = new[]
            {
                "Changed",
                "Active->Succeeded",
                "Evaluation"
            };
            Assert.That(log, Is.EqualTo(expected2));
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 관리용 종료도 같은 이벤트로 알린다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveEngine_관리용종료도발행한다()
        {
            var engine = CreateCounting();
            engine.Activate();
            var log = RecordStatus(engine);

            engine.Force(ObjectiveStatus.Cancelled);

            var expected = new[]
            {
                "Active->Cancelled"
            };
            Assert.That(log.FindAll(entry => entry.Contains("->")), Is.EqualTo(expected));
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// 복원은 이벤트를 발행하지 않고, 같은 상태로의 확정은 전이로 치지 않는다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveEngine_복원과같은상태는발행하지않는다()
        {
            var source = CreateCounting();
            source.Activate();
            var registry = new ObjectiveSpecRegistry();
            registry.Register(source.Spec);

            var saved = new[]
            {
                source.Instance
            };
            var engine = ObjectiveRuntimeBinding.Bind(registry, saved)[0];
            var log = RecordStatus(engine);

            Assert.That(engine.Refresh(), Is.False);
            Assert.That(log, Is.Empty);
        }

    #endregion

    }
}
