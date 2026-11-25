/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : TEST_ObjectiveFlow.cs
수정일 : 2026-11-25

# 설명
ObjectiveFlow의 선행·Choice, 시작 조건·Auto 시작, 새 회차 교체·Choice 재개방, 실행 소유와 명세·저장 검증을 검증한다.

# 특이사항, 제약사항
선택 기록은 ObjectiveFlowState가 소유하며 Objective Progress는 각 Instance가 소유한다. 공용 Condition은 Support/ObjectiveTestConds에 있다.
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
    /// <br/>ObjectiveFlow의 선행·Choice, 시작 조건·Auto 시작, 새 회차 교체·Choice 재개방,
    /// <br/>실행 소유와 명세·저장 검증을 검증한다.
    /// </summary>
    // ================================================================================
    public sealed class TEST_ObjectiveFlow
    {

    #region 테스트 도우미

        private sealed class Hook : ICond<ObjectiveContext>
        {
            // ------------------------------------------------------------
            /// <summary>
            /// 평가 중에 실행할 동작.
            /// </summary>
            // ------------------------------------------------------------
            public Action During { get; set; }

            // ------------------------------------------------------------
            /// <summary>
            /// 동작을 실행하고 항상 미완료를 반환한다.
            /// </summary>
            // ------------------------------------------------------------
            public bool Check(ObjectiveContext context)
            {
                During?.Invoke();
                return false;
            }
        }

        private static ObjectiveEngine Engine(string id, bool completed = false, string scope = "world") =>
            Engine(id, new ToggleCond(completed), scope);

        private static ObjectiveEngine Engine(string id, ICond<ObjectiveContext> condition, string scope = "world")
        {
            var spec = new ObjectiveSpec
            (
                id, 1, new TaskSpec("root", condition)
            );
            var instance = new ObjectiveInstance(id, 1, "run-" + id, scope);
            return new ObjectiveEngine(spec, instance);
        }

        private static ObjectiveFlowSpec ThreeWaySpec(string flowID)
        {
            var entries = new ObjectiveFlowEntry[]
            {
                new("a"),
                new("b"),
                new("c")
            };
            var alternatives = new string[]
            {
                "a",
                "b",
                "c"
            };
            var groups = new ObjectiveFlowChoice[]
            {
                new("choice", alternatives)
            };
            return new ObjectiveFlowSpec(flowID, entries, groups);
        }

        private static ObjectiveFlowSpec Spec()
        {
            var first = new string[]
            {
                "begin"
            };
            var last = new string[]
            {
                "left"
            };
            var alternatives = new string[]
            {
                "left",
                "right"
            };
            var entries = new ObjectiveFlowEntry[]
            {
                new("begin"),
                new("left", first),
                new("right", first),
                new("end", last)
            };
            var choice = new ObjectiveFlowChoice[]
            {
                new("route", alternatives)
            };
            return new ObjectiveFlowSpec("flow", entries, choice);
        }

        private static ObjectiveEngine[] Engines()
        {
            return new[]
            {
                Engine("begin", true),
                Engine("left"),
                Engine("right"),
                Engine("end")
            };
        }

        private static ObjectiveEngine RegisteredEngine
        (
            string id, ObjectiveDirectory directory, bool done = false, bool repeatable = false
        )
        {
            var toggle = new ToggleCond
            {
                Value = done
            };
            var spec = new ObjectiveSpec
            (
                id, 1, new TaskSpec("root", toggle),
                repeatMode: repeatable ? ObjectiveRepeatMode.Repeatable : ObjectiveRepeatMode.Once
            );
            var engine = new ObjectiveEngine(spec, new ObjectiveInstance(id, 1, id + "-1", "world"), directory: directory);
            directory.Register(engine);
            return engine;
        }

        private static ObjectiveFlow BuildFlow
        (
            IEnumerable<ObjectiveFlowEntry> entries, IEnumerable<ObjectiveEngine> engines,
            IEnumerable<ObjectiveFlowChoice> choices = null
        )
        {
            return new ObjectiveFlow(new ObjectiveFlowSpec("flow", entries, choices), new ObjectiveFlowState("flow", "world"), engines);
        }

        private static ObjectiveEngine RepeatEngine
        (
            string id, bool completed = false, bool repeatable = true, ObjectiveDirectory directory = null
        )
        {
            var toggle = new ToggleCond
            {
                Value = completed
            };
            var spec = new ObjectiveSpec
            (
                id, 1, new TaskSpec("root", toggle),
                repeatMode: repeatable ? ObjectiveRepeatMode.Repeatable : ObjectiveRepeatMode.Once
            );
            var engine = new ObjectiveEngine(spec, new ObjectiveInstance(id, 1, id + "-1", "world"), directory: directory);
            directory?.Register(engine);
            return engine;
        }

        // begin → (left | right) → end(left 뒤)
        private static ObjectiveFlowSpec RepeatSpec()
        {
            var afterBegin = new string[]
            {
                "begin"
            };
            var afterLeft = new string[]
            {
                "left"
            };
            var alternatives = new string[]
            {
                "left",
                "right"
            };
            var entries = new ObjectiveFlowEntry[]
            {
                new("begin"),
                new("left", afterBegin),
                new("right", afterBegin),
                new("end", afterLeft)
            };
            var choices = new ObjectiveFlowChoice[]
            {
                new("route", alternatives)
            };
            return new ObjectiveFlowSpec("flow", entries, choices);
        }

        private static ObjectiveEngine[] RepeatEngines
        (
            bool leftRepeatable = true, bool rightRepeatable = true, ObjectiveDirectory directory = null
        )
        {
            return new[]
            {
                RepeatEngine("begin", true, directory: directory),
                RepeatEngine("left", repeatable: leftRepeatable, directory: directory),
                RepeatEngine("right", repeatable: rightRepeatable, directory: directory),
                RepeatEngine("end", repeatable: false, directory: directory)
            };
        }

        private static string NextRun(string objectiveID) => objectiveID + "-2";

    #endregion

    #region 선행·Choice

        // ------------------------------------------------------------
        /// <summary>
        /// 선행 성공과 선택을 만족할 때만 해당 Objective를 시작한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveFlow_선행성공과Choice개방()
        {
            var sources = Engines();
            var flow = new ObjectiveFlow(Spec(), new ObjectiveFlowState("flow", "world"), sources);

            Assert.That(flow.CanActivate("left"), Is.False);
            Assert.That(flow.CanActivate("end"), Is.False);
            Assert.That(flow.Activate("begin"), Is.True);
            Assert.That(sources[0].Instance.Status, Is.EqualTo(ObjectiveStatus.Succeeded));
            Assert.That(flow.CanActivate("left"), Is.False);
            Assert.That(flow.SelectChoice("route", "left"), Is.True);
            Assert.That(sources[2].Instance.Status, Is.EqualTo(ObjectiveStatus.Cancelled));
            Assert.That(flow.CanActivate("left"), Is.True);
            Assert.That(flow.CanActivate("right"), Is.False);
            Assert.That(flow.Activate("left"), Is.True);
            Assert.That(flow.CanActivate("end"), Is.False);
            Assert.That(flow.Activate("right"), Is.False);
            Assert.That(flow.SelectChoice("route", "left"), Is.False);
            Assert.Throws<InvalidOperationException>(() => flow.SelectChoice("route", "right"));
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Flow Choice는 한 실행에서 한 번만 선택하고 다른 분기를 취소한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveFlow_Choice단회취소()
        {
            var sources = Engines();
            var flow = new ObjectiveFlow(Spec(), new ObjectiveFlowState("flow", "world"), sources);
            Assert.That(flow.SelectChoice("route", "right"), Is.True);
            Assert.That(flow.State.TryGetSelection("route", out var selected), Is.True);
            Assert.That(selected, Is.EqualTo("right"));
            Assert.That(sources[1].Instance.Status, Is.EqualTo(ObjectiveStatus.Cancelled));
            Assert.That(sources[2].Instance.Status, Is.EqualTo(ObjectiveStatus.Inactive));
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 종료된 다른 대안은 강제 취소할 수 없어 선택을 거부한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveFlow_다른대안Terminal이면거부()
        {
            var sources = Engines();
            sources[1].Instance.Apply(x => x.ChangeStatus(ObjectiveStatus.Active));
            sources[1].Force(ObjectiveStatus.Succeeded);
            var flow = new ObjectiveFlow(Spec(), new ObjectiveFlowState("flow", "world"), sources);

            Assert.Throws<InvalidOperationException>(() => flow.SelectChoice("route", "right"));
            Assert.That(flow.State.TryGetSelection("route", out _), Is.False);
            Assert.That(sources[1].Instance.Status, Is.EqualTo(ObjectiveStatus.Succeeded));
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 성공한 선행이 없는 Objective는 Flow 자체로 활성화할 수 없다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveFlow_선행실패는개방하지않는다()
        {
            var sources = Engines();
            sources[0].Instance.Apply(x => x.ChangeStatus(ObjectiveStatus.Active));
            sources[0].Force(ObjectiveStatus.Failed);
            var flow = new ObjectiveFlow(Spec(), new ObjectiveFlowState("flow", "world"), sources);
            flow.SelectChoice("route", "left");
            Assert.That(flow.CanActivate("left"), Is.False);
            Assert.That(flow.Activate("left"), Is.False);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Choice 구독 예외 전에도 모든 대안의 취소가 일괄 반영된다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveFlow_구독예외전모든대안취소()
        {
            var entries = new ObjectiveFlowEntry[]
            {
                new("a"),
                new("b"),
                new("c")
            };
            var alternatives = new string[]
            {
                "a",
                "b",
                "c"
            };
            var groups = new ObjectiveFlowChoice[]
            {
                new("choice", alternatives)
            };
            var spec = new ObjectiveFlowSpec("three", entries, groups);
            var engines = new ObjectiveEngine[]
            {
                Engine("a"),
                Engine("b"),
                Engine("c")
            };
            var state = new ObjectiveFlowState("three", "world");
            var flow = new ObjectiveFlow(spec, state, engines);

            bool allCancelledOnCallback = false;
            engines[1].Changed += () =>
            {
                allCancelledOnCallback =
                    engines[1].Instance.Status == ObjectiveStatus.Cancelled
                    && engines[2].Instance.Status == ObjectiveStatus.Cancelled;
                throw new InvalidOperationException("Subscriber failed after commit.");
            };

            Assert.Throws<InvalidOperationException>(() => flow.SelectChoice("choice", "a"));
            Assert.That(allCancelledOnCallback, Is.True);
            Assert.That(state.TryGetSelection("choice", out var selected), Is.True);
            Assert.That(selected, Is.EqualTo("a"));

            flow.Dispose();
            Assert.That(new ObjectiveFlow(spec, state, engines), Is.Not.Null);
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/>한 대안의 취소를 계획할 수 없으면 선택도 다른 취소도 반영하지 않는다.
        /// <br/>저장 상태는 그대로 다시 열 수 있다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveFlow_Choice취소계획이실패하면선택도취소도없다()
        {
            var hook = new Hook();
            var engines = new ObjectiveEngine[]
            {
                Engine("a"),
                Engine("b", hook),
                Engine("c")
            };
            var spec = ThreeWaySpec("atomic");
            var state = new ObjectiveFlowState("atomic", "world");
            var flow = new ObjectiveFlow(spec, state, engines);

            // b가 평가 중이면 b의 취소를 계획할 수 없다.
            Exception failure = null;
            hook.During = () =>
            {
                hook.During = null;
                try
                {
                    flow.SelectChoice("choice", "a");
                }
                catch (Exception exception)
                {
                    failure = exception;
                }
            };
            engines[1].Evaluate();

            Assert.That(failure, Is.InstanceOf<InvalidOperationException>());
            Assert.That(state.TryGetSelection("choice", out _), Is.False);
            foreach (var engine in engines)
            {
                Assert.That(engine.Instance.Status, Is.EqualTo(ObjectiveStatus.Inactive));
            }

            flow.Dispose();
            var reopened = new ObjectiveFlow(spec, state, engines);
            Assert.That(reopened.SelectChoice("choice", "a"), Is.True);
            Assert.That(engines[1].Instance.Status, Is.EqualTo(ObjectiveStatus.Cancelled));
            Assert.That(engines[2].Instance.Status, Is.EqualTo(ObjectiveStatus.Cancelled));
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 한 대안의 구독자가 실패해도 다른 대안의 취소 알림은 바로 전달된다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveFlow_한대안의구독예외에도다른대안알림을전달한다()
        {
            var engines = new ObjectiveEngine[]
            {
                Engine("a"),
                Engine("b"),
                Engine("c")
            };
            var flow = new ObjectiveFlow(ThreeWaySpec("notify"), new ObjectiveFlowState("notify", "world"), engines);
            int cChanged = 0;
            engines[1].Changed += () => throw new InvalidOperationException("Subscriber failed.");
            engines[2].Changed += () => cChanged++;

            Assert.Throws<InvalidOperationException>(() => flow.SelectChoice("choice", "a"));
            Assert.That(cChanged, Is.EqualTo(1));
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Choice로 취소된 대안도 StatusChanged로 알린다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveFlow_Choice로취소된대안도StatusChanged를발행한다()
        {
            var left = Engine("left");
            var right = Engine("right");
            var entries = new ObjectiveFlowEntry[]
            {
                new("left"),
                new("right")
            };
            var afterRoute = new string[]
            {
                "left",
                "right"
            };
            var choices = new ObjectiveFlowChoice[]
            {
                new("route", afterRoute)
            };
            var sources = new[]
            {
                left,
                right
            };
            using var flow = new ObjectiveFlow(new ObjectiveFlowSpec("flow", entries, choices), new ObjectiveFlowState("flow", "world"), sources);
            var log = new List<string>();
            right.StatusChanged += (before, after) => log.Add($"{before}->{after}");

            flow.SelectChoice("route", "left");

            var expected = new[]
            {
                "Inactive->Cancelled"
            };
            Assert.That(log, Is.EqualTo(expected));
        }

    #endregion

    #region 실행 소유

        // ------------------------------------------------------------
        /// <summary>
        /// <br/>한 FlowState는 살아 있는 Flow 하나만 실행한다.
        /// <br/>Dispose로 소유를 놓으면 같은 상태로 다시 만들 수 있다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveFlow_한FlowState는살아있는Flow하나만실행한다()
        {
            var spec = Spec();
            var state = new ObjectiveFlowState("flow", "world");
            var first = new ObjectiveFlow(spec, state, Engines());

            var others = Engines();
            Assert.Throws<InvalidOperationException>(() => new ObjectiveFlow(spec, state, others));

            first.Dispose();
            var second = new ObjectiveFlow(spec, state, others);
            Assert.That(second.State, Is.SameAs(state));
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// 한 Engine은 서로 다른 FlowState의 Flow 두 개에 동시에 속할 수 없다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveFlow_한Engine은살아있는Flow하나만실행한다()
        {
            var spec = Spec();
            var sources = Engines();
            var first = new ObjectiveFlow(spec, new ObjectiveFlowState("flow", "world"), sources);
            var otherState = new ObjectiveFlowState("flow", "world");

            Assert.Throws<InvalidOperationException>(() => new ObjectiveFlow(spec, otherState, sources));

            // 소유 확인은 모든 검증 뒤에 하므로 거부된 생성은 아무것도 차지하지 않는다.
            first.Dispose();
            Assert.That(new ObjectiveFlow(spec, otherState, sources), Is.Not.Null);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Dispose한 Flow의 명령과 조회는 거부한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveFlow_Dispose뒤명령은거부한다()
        {
            var flow = new ObjectiveFlow(Spec(), new ObjectiveFlowState("flow", "world"), Engines());
            flow.Dispose();
            flow.Dispose();

            Assert.Throws<ObjectDisposedException>(() => flow.CanActivate("begin"));
            Assert.Throws<ObjectDisposedException>(() => flow.Activate("begin"));
            Assert.Throws<ObjectDisposedException>(() => flow.SelectChoice("route", "left"));
        }

        // ------------------------------------------------------------
        /// <summary>
        /// <br/>선택한 대안의 선행이 자동 실패하면 그 대안은 막힌 채 남는다.
        /// <br/>선택 기록과 취소는 일치하므로 저장은 그대로 다시 열린다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveFlow_선택한대안의선행이실패해도저장은다시열린다()
        {
            var failure = new ToggleCond(false);
            var prerequisite = new ObjectiveEngine
            (
                new ObjectiveSpec("gate", 1, new TaskSpec("root", new ToggleCond(false), failure)),
                new ObjectiveInstance("gate", 1, "run-gate", "world")
            );
            var chosen = Engine("chosen");
            var other = Engine("other");
            var prerequisites = new string[]
            {
                "gate"
            };
            var entries = new ObjectiveFlowEntry[]
            {
                new("gate"),
                new("chosen", prerequisites),
                new("other")
            };
            var alternatives = new string[]
            {
                "chosen",
                "other"
            };
            var groups = new ObjectiveFlowChoice[]
            {
                new("route", alternatives)
            };
            var spec = new ObjectiveFlowSpec("blocked", entries, groups);
            var state = new ObjectiveFlowState("blocked", "world");
            var sources = new ObjectiveEngine[]
            {
                prerequisite,
                chosen,
                other
            };
            var flow = new ObjectiveFlow(spec, state, sources);

            Assert.That(flow.Activate("gate"), Is.True);
            Assert.That(flow.SelectChoice("route", "chosen"), Is.True);
            failure.Value = true;
            prerequisite.Refresh();

            Assert.That(prerequisite.Instance.Status, Is.EqualTo(ObjectiveStatus.Failed));
            Assert.That(chosen.Instance.Status, Is.EqualTo(ObjectiveStatus.Inactive));
            Assert.That(other.Instance.Status, Is.EqualTo(ObjectiveStatus.Cancelled));
            Assert.That(flow.CanActivate("chosen"), Is.False);

            flow.Dispose();
            var reopened = new ObjectiveFlow(spec, state, sources);
            Assert.That(reopened.State.TryGetSelection("route", out var selected), Is.True);
            Assert.That(selected, Is.EqualTo("chosen"));
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/>Flow가 소유한 Engine의 직접 시작은 선행 게이트를 우회하므로 거부한다.
        /// <br/>Flow를 놓으면 다시 직접 시작할 수 있다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveFlow_Flow소유Engine의직접시작은거부한다()
        {
            var sources = Engines();
            var flow = new ObjectiveFlow(Spec(), new ObjectiveFlowState("flow", "world"), sources);

            Assert.Throws<InvalidOperationException>(() => sources[1].Activate());
            Assert.That(sources[1].Instance.Status, Is.EqualTo(ObjectiveStatus.Inactive));
            Assert.That(flow.Activate("left"), Is.False);
            Assert.That(flow.Activate("begin"), Is.True);

            flow.Dispose();
            Assert.That(sources[1].Activate(), Is.True);
        }

    #endregion

    #region 명세·저장 검증

        // ------------------------------------------------------------
        /// <summary>
        /// Choice와 명세가 Objective의 소속 대안을 조회한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveFlow_Choice는소속대안을조회한다()
        {
            var spec = Spec();

            Assert.That(spec.TryGetChoice("left", out var route), Is.True);
            Assert.That(route.ChoiceID, Is.EqualTo("route"));
            Assert.That(spec.TryGetChoice("right", out var same), Is.True);
            Assert.That(same, Is.SameAs(route));
            Assert.That(route.Includes("right"), Is.True);
            Assert.That(route.Includes("begin"), Is.False);
            Assert.That(route.Includes(null), Is.False);
            Assert.That(spec.TryGetChoice("begin", out _), Is.False);
            Assert.That(spec.TryGetChoice(null, out _), Is.False);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Flow 선행 그래프는 참조 누락과 순환을 거부한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveFlow_명세참조및순환검증()
        {
            var missingID = new string[]
            {
                "missing"
            };
            var aPrerequisites = new string[]
            {
                "b"
            };
            var bPrerequisites = new string[]
            {
                "a"
            };
            var missing = new ObjectiveFlowEntry[]
            {
                new("a", missingID)
            };
            var cyclic = new ObjectiveFlowEntry[]
            {
                new("a", aPrerequisites),
                new("b", bPrerequisites)
            };
            Assert.Throws<ArgumentException>(() => new ObjectiveFlowSpec("bad", missing));
            Assert.Throws<ArgumentException>(() => new ObjectiveFlowSpec("cyclic", cyclic));
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 중복 Objective와 Choice 대안은 저작 시점에 거부한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveFlow_중복및잘못된Choice거부()
        {
            var duplicated = new ObjectiveFlowEntry[]
            {
                new("a"),
                new("a")
            };
            var single = new ObjectiveFlowEntry[]
            {
                new("a")
            };
            var duplicatedIDs = new string[]
            {
                "a",
                "a"
            };
            var invalidChoice = new ObjectiveFlowChoice[]
            {
                new("choice", duplicatedIDs)
            };
            Assert.Throws<ArgumentException>(() => new ObjectiveFlowSpec("duplicate", duplicated));
            Assert.Throws<ArgumentException>(() => new ObjectiveFlowSpec("bad", single, invalidChoice));
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 외부 Scope 엔진이나 누락 Objective를 Flow에 잘못 연결할 수 없다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveFlow_다른Scope와누락엔진거부()
        {
            var sources = Engines();
            sources[0] = Engine("begin", scope: "other");
            var spec = Spec();
            var state = new ObjectiveFlowState("flow", "world");
            Assert.Throws<ArgumentException>(() => new ObjectiveFlow(spec, state, sources));

            var fewer = new ObjectiveEngine[]
            {
                Engine("begin"),
                Engine("left")
            };
            Assert.Throws<ArgumentException>(() => new ObjectiveFlow(spec, state, fewer));
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Flow 선택을 저장·복원해도 보상·진행·알림은 재발행하지 않는다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveFlow_선택JsonUtility왕복비발행()
        {
            var sources = Engines();
            var state = new ObjectiveFlowState("flow", "world");
            var flow = new ObjectiveFlow(Spec(), state, sources);
            flow.SelectChoice("route", "left");
            string json = JsonUtility.ToJson(state);

            var restoredState = JsonUtility.FromJson<ObjectiveFlowState>(json);
            var loaded = new ObjectiveEngine[sources.Length];
            int events = 0;
            for (int index = 0; index < sources.Length; index++)
            {
                var engine = sources[index];
                var instance = JsonUtility.FromJson<ObjectiveInstance>(JsonUtility.ToJson(engine.Instance));
                loaded[index] = new ObjectiveEngine(engine.Spec, instance);
                loaded[index].Changed += () => events++;
            }

            var restored = new ObjectiveFlow(Spec(), restoredState, loaded);
            Assert.That(restored.State.TryGetSelection("route", out var selected), Is.True);
            Assert.That(selected, Is.EqualTo("left"));
            Assert.That(loaded[2].Instance.Status, Is.EqualTo(ObjectiveStatus.Cancelled));
            Assert.That(events, Is.Zero);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 저장된 Flow 선택은 정확한 정의 Version에만 바인딩된다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveFlow_Version불일치는복원을거부한다()
        {
            var state = new ObjectiveFlowState("flow", "world", version: 2);
            Assert.Throws<ArgumentException>(() => new ObjectiveFlow(Spec(), state, Engines()));
            Assert.Throws<InvalidOperationException>(() => new ObjectiveFlowState("flow", "world", version: 0));
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 구조는 맞아도 Choice 기록과 취소가 불일치하면 복원 시 거부한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveFlow_모순된선택복원거부()
        {
            var sources = Engines();
            var state = new ObjectiveFlowState("flow", "world");
            var flow = new ObjectiveFlow(Spec(), state, sources);
            flow.SelectChoice("route", "left");
            flow.Dispose();

            var broken = Engines();
            Assert.Throws<InvalidOperationException>(() => new ObjectiveFlow(Spec(), state, broken));
        }

        // ------------------------------------------------------------
        /// <summary>
        /// <br/>같은 Choice의 다른 대안을 선행으로 요구하는 대안은
        /// <br/>시작할 수 없으므로 거부한다(간접 선행 포함).
        /// <br/>Choice 밖 Objective가 대안을 요구하는 것은 허용한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveFlow_같은Choice대안을선행으로요구하면거부한다()
        {
            var alternatives = new string[]
            {
                "a",
                "b"
            };
            var choices = new ObjectiveFlowChoice[]
            {
                new("choice", alternatives)
            };
            var afterA = new string[]
            {
                "a"
            };
            var afterB = new string[]
            {
                "b"
            };
            var afterC = new string[]
            {
                "c"
            };
            var direct = new ObjectiveFlowEntry[]
            {
                new("a", afterB),
                new("b")
            };
            var indirect = new ObjectiveFlowEntry[]
            {
                new("a", afterC),
                new("b"),
                new("c", afterB)
            };
            var allowed = new ObjectiveFlowEntry[]
            {
                new("a"),
                new("b"),
                new("c", afterA)
            };

            Assert.Throws<ArgumentException>(() => new ObjectiveFlowSpec("direct", direct, choices));
            Assert.Throws<ArgumentException>(() => new ObjectiveFlowSpec("indirect", indirect, choices));
            Assert.That(new ObjectiveFlowSpec("allowed", allowed, choices), Is.Not.Null);
        }

    #endregion

    #region 시작 조건(ActivationCond)

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/>"A 또는 B가 성공한 뒤" 같은 규칙을 ActivationCond로 표현하고, 선행 AND와 함께 쓸
        /// <br/>수 있다.
        /// </summary>
        // --------------------------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveFlow_ActivationCond로OR와실패후규칙을표현한다()
        {
            var directory = new ObjectiveDirectory();
            var a = RegisteredEngine("a", directory);
            var b = RegisteredEngine("b", directory);
            var rescue = RegisteredEngine("rescue", directory);
            var reward = RegisteredEngine("reward", directory);
            var entries = new ObjectiveFlowEntry[]
            {
                new("a"), new("b"),
                new("rescue", activationCond: ObjectiveCond.DependsOn("a", ObjectiveStatus.Failed)),
                new("reward", activationCond: Cond.OR(ObjectiveCond.DependsOn("a", ObjectiveStatus.Succeeded), ObjectiveCond.DependsOn("b", ObjectiveStatus.Succeeded)))
            };
            var values = new[]
            {
                a,
                b,
                rescue,
                reward
            };
            using var flow = BuildFlow(entries, values);
            flow.Activate("a");
            flow.Activate("b");
            Assert.That(flow.CanActivate("rescue"), Is.False);
            Assert.That(flow.CanActivate("reward"), Is.False);

            b.Force(ObjectiveStatus.Succeeded);
            a.Force(ObjectiveStatus.Failed);

            Assert.That(flow.CanActivate("rescue"), Is.True);
            Assert.That(flow.CanActivate("reward"), Is.True);
            Assert.That(flow.Activate("rescue"), Is.True);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 시작 조건은 선행과 Choice 요구를 대신하지 않고 함께 만족해야 한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveFlow_시작조건은선행과함께만족해야한다()
        {
            var directory = new ObjectiveDirectory();
            var gate = new ToggleCond
            {
                Value = false
            };
            var first = RegisteredEngine("first", directory);
            var second = RegisteredEngine("second", directory);
            var afterSecond = new[]
            {
                "first"
            };
            var entries = new ObjectiveFlowEntry[]
            {
                new("first"),
                new("second", afterSecond, gate)
            };
            var values = new[]
            {
                first,
                second
            };
            using var flow = BuildFlow(entries, values);

            Assert.That(flow.CanActivate("second"), Is.False);
            flow.Activate("first");
            first.Force(ObjectiveStatus.Succeeded);
            Assert.That(flow.CanActivate("second"), Is.False);

            gate.Value = true;
            Assert.That(flow.CanActivate("second"), Is.True);
        }

    #endregion

    #region 자동 시작(Auto)

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/>생성자는 시작하지 않고, Refresh가 선행 없는 Auto Entry를 시작하며 끝날 때마다 다음
        /// <br/>Entry를 이어서 시작한다.
        /// </summary>
        // --------------------------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveFlow_Auto는조건이충족되는순간연쇄로시작한다()
        {
            var directory = new ObjectiveDirectory();
            var intro = RegisteredEngine("intro", directory);
            var middle = RegisteredEngine("middle", directory, done: true);
            var finale = RegisteredEngine("finale", directory);
            var afterIntro = new string[]
            {
                "intro"
            };
            var afterMiddle = new string[]
            {
                "middle"
            };
            var entries = new ObjectiveFlowEntry[]
            {
                new("intro", auto: true),
                new("middle", afterIntro, auto: true),
                new("finale", afterMiddle, auto: true)
            };
            var values = new[]
            {
                intro,
                middle,
                finale
            };
            using var flow = BuildFlow(entries, values);
            Assert.That(intro.Instance.Status, Is.EqualTo(ObjectiveStatus.Inactive));

            Assert.That(flow.Refresh(), Is.EqualTo(1));
            Assert.That(intro.Instance.Status, Is.EqualTo(ObjectiveStatus.Active));
            Assert.That(middle.Instance.Status, Is.EqualTo(ObjectiveStatus.Inactive));

            intro.Force(ObjectiveStatus.Succeeded);

            Assert.That(middle.Instance.Status, Is.EqualTo(ObjectiveStatus.Succeeded));
            Assert.That(finale.Instance.Status, Is.EqualTo(ObjectiveStatus.Active));
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Auto가 아닌 Entry는 조건이 충족돼도 스스로 시작하지 않는다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveFlow_Auto가아니면시작하지않는다()
        {
            var directory = new ObjectiveDirectory();
            var first = RegisteredEngine("first", directory);
            var second = RegisteredEngine("second", directory);
            var afterSecond = new[]
            {
                "first"
            };
            var entries = new ObjectiveFlowEntry[]
            {
                new("first"),
                new("second", afterSecond)
            };
            var values = new[]
            {
                first,
                second
            };
            using var flow = BuildFlow(entries, values);
            flow.Activate("first");

            first.Force(ObjectiveStatus.Succeeded);

            Assert.That(second.Instance.Status, Is.EqualTo(ObjectiveStatus.Inactive));
            Assert.That(flow.Refresh(), Is.Zero);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 외부 Fact로 조건이 충족되는 Auto Entry는 Refresh에서 시작한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveFlow_외부Fact로충족되면Refresh에서시작한다()
        {
            var directory = new ObjectiveDirectory();
            var open = new ToggleCond();
            var timer = RegisteredEngine("timer", directory);
            var entries = new ObjectiveFlowEntry[]
            {
                new("timer", activationCond: open, auto: true)
            };
            var values = new[]
            {
                timer
            };
            using var flow = BuildFlow(entries, values);
            Assert.That(flow.Refresh(), Is.Zero);

            open.Value = true;

            Assert.That(flow.Refresh(), Is.EqualTo(1));
            Assert.That(timer.Instance.Status, Is.EqualTo(ObjectiveStatus.Active));
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Choice 대안은 선택된 뒤에만 자동으로 시작한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveFlow_Choice대안은선택된뒤에만시작한다()
        {
            var directory = new ObjectiveDirectory();
            var intro = RegisteredEngine("intro", directory, done: true);
            var left = RegisteredEngine("left", directory);
            var right = RegisteredEngine("right", directory);
            var afterIntro = new string[]
            {
                "intro"
            };
            var entries = new ObjectiveFlowEntry[]
            {
                new("intro"),
                new("left", afterIntro, auto: true),
                new("right", afterIntro, auto: true)
            };
            var afterRoute = new[]
            {
                "left",
                "right"
            };
            var choices = new ObjectiveFlowChoice[]
            {
                new("route", afterRoute)
            };
            var values = new[]
            {
                intro,
                left,
                right
            };
            using var flow = BuildFlow(entries, values, choices);
            flow.Activate("intro");
            Assert.That(flow.Refresh(), Is.Zero);
            Assert.That(left.Instance.Status, Is.EqualTo(ObjectiveStatus.Inactive));

            flow.SelectChoice("route", "left");

            Assert.That(left.Instance.Status, Is.EqualTo(ObjectiveStatus.Active));
            Assert.That(right.Instance.Status, Is.EqualTo(ObjectiveStatus.Cancelled));
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// 회차를 교체해도 새 Engine의 상태 전이를 이어서 구독해 자동 시작이 계속된다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveFlow_회차교체뒤에도자동시작이이어진다()
        {
            var directory = new ObjectiveDirectory();
            var intro = RegisteredEngine("intro", directory, done: true, repeatable: true);
            var next = RegisteredEngine("next", directory, repeatable: true);
            var afterNext = new[]
            {
                "intro"
            };
            var entries = new ObjectiveFlowEntry[]
            {
                new("intro"),
                new("next", afterNext, auto: true)
            };
            var values = new[]
            {
                intro,
                next
            };
            using var flow = BuildFlow(entries, values);
            flow.Activate("intro");
            Assert.That(next.Instance.Status, Is.EqualTo(ObjectiveStatus.Active));
            next.Force(ObjectiveStatus.Cancelled);

            var introAgain = flow.Repeat("intro", "intro-2");
            var nextAgain = flow.Repeat("next", "next-2");
            flow.Activate("intro");

            Assert.That(introAgain.Instance.Status, Is.EqualTo(ObjectiveStatus.Succeeded));
            Assert.That(nextAgain.Instance.Status, Is.EqualTo(ObjectiveStatus.Active));
            Assert.That(intro.Flow, Is.Null);
        }

    #endregion

    #region Directory 변경 전파

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/>Directory에 등록한 Flow는 시작 조건이 읽은 다른 Objective가 바뀌면
        /// <br/>RefreshDirty에서 자동으로 시작한다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveFlow_읽은Objective가바뀌면RefreshDirty가Flow를갱신한다()
        {
            var directory = new ObjectiveDirectory();
            var outside = RegisteredEngine("outside", directory);
            var bonus = RegisteredEngine("bonus", directory);
            var entries = new ObjectiveFlowEntry[]
            {
                new("bonus", activationCond: ObjectiveCond.DependsOn("outside", ObjectiveStatus.Succeeded), auto: true)
            };
            var values = new[]
            {
                bonus
            };
            using var flow = BuildFlow(entries, values);
            directory.RegisterFlow(flow);
            outside.Activate();
            Assert.That(flow.Refresh(), Is.Zero);
            Assert.That(directory.RefreshDirty(), Is.Zero);

            outside.Force(ObjectiveStatus.Succeeded);

            Assert.That(bonus.Instance.Status, Is.EqualTo(ObjectiveStatus.Inactive));
            Assert.That(directory.RefreshDirty(), Is.EqualTo(1));
            Assert.That(bonus.Instance.Status, Is.EqualTo(ObjectiveStatus.Active));
        }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// 이름 붙은 Fact 소스가 바뀌어도 등록한 Flow가 갱신되고, 등록하지 않은 Flow는 그대로이다.
        /// </summary>
        // --------------------------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveFlow_Fact소스변경도등록한Flow만갱신한다()
        {
            var directory = new ObjectiveDirectory();
            var account = new ValueRegistry();
            directory.RegisterSource("account", account);
            var registered = RegisteredEngine("registered", directory);
            var plain = RegisteredEngine("plain", directory);
            var cond = ObjectiveCond.MinValueI("kills", 3, source: "account");
            var sources = new ObjectiveFlowEntry[]
            {
                new("registered", activationCond: cond, auto: true)
            };
            var values = new[]
            {
                registered
            };
            using var first = BuildFlow(sources, values);
            var sources2 = new ObjectiveFlowEntry[]
            {
                new("plain", activationCond: cond, auto: true)
            };
            var sources3 = new[]
            {
                plain
            };
            var second = new ObjectiveFlow(new ObjectiveFlowSpec("plain-flow", sources2), new ObjectiveFlowState("plain-flow", "world"), sources3);
            directory.RegisterFlow(first);
            first.Refresh();
            second.Refresh();

            account.SetI("kills", 3);
            directory.RefreshDirty();

            Assert.That(registered.Instance.Status, Is.EqualTo(ObjectiveStatus.Active));
            Assert.That(plain.Instance.Status, Is.EqualTo(ObjectiveStatus.Inactive));
            second.Dispose();
        }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/>Dispose한 Flow는 Directory에서 빠져 더 이상 갱신하지 않고, 다른 Directory의
        /// <br/>Engine을 가진 Flow는 등록을 거부한다.
        /// </summary>
        // --------------------------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveFlow_Dispose하면등록에서빠지고다른Directory는거부한다()
        {
            var directory = new ObjectiveDirectory();
            var outside = RegisteredEngine("outside", directory);
            var bonus = RegisteredEngine("bonus", directory);
            var entries = new ObjectiveFlowEntry[]
            {
                new("bonus", activationCond: ObjectiveCond.DependsOn("outside", ObjectiveStatus.Succeeded), auto: true)
            };
            var values = new[]
            {
                bonus
            };
            var flow = BuildFlow(entries, values);
            directory.RegisterFlow(flow);
            outside.Activate();
            flow.Refresh();

            flow.Dispose();
            outside.Force(ObjectiveStatus.Succeeded);

            Assert.That(directory.RefreshDirty(), Is.Zero);
            Assert.That(bonus.Instance.Status, Is.EqualTo(ObjectiveStatus.Inactive));
            Assert.That(directory.UnregisterFlow(flow), Is.False);

            var other = new ObjectiveDirectory();
            var stranger = RegisteredEngine("stranger", directory);
            var sources = new ObjectiveFlowEntry[]
            {
                new("stranger")
            };
            var values2 = new[]
            {
                stranger
            };
            using var foreign = BuildFlow(sources, values2);
            Assert.Throws<InvalidOperationException>(() => other.RegisterFlow(foreign));
            Assert.Throws<ArgumentNullException>(() => directory.RegisterFlow(null));
        }

    #endregion

    #region 새 회차 교체

        // --------------------------------------------------------------------------------
        /// <summary>
        /// 종료한 선행을 새 회차로 교체하면 Flow가 새 Engine을 쓰고 선행 조건은 다시 요구한다.
        /// </summary>
        // --------------------------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveFlow_종료한Objective를새회차로교체한다()
        {
            var directory = new ObjectiveDirectory();
            var sources = RepeatEngines(directory: directory);
            var flow = new ObjectiveFlow(RepeatSpec(), new ObjectiveFlowState("flow", "world"), sources);
            flow.Activate("begin");
            Assert.That(flow.SelectChoice("route", "left"), Is.True);
            Assert.That(flow.CanActivate("left"), Is.True);

            var repeated = flow.Repeat("begin", "begin-2");

            Assert.That(repeated.Instance.InstanceID, Is.EqualTo("begin-2"));
            Assert.That(repeated.Instance.Status, Is.EqualTo(ObjectiveStatus.Inactive));
            Assert.That(flow.GetEngine("begin"), Is.SameAs(repeated));
            Assert.That(sources[0].Flow, Is.Null);
            Assert.That(repeated.Flow, Is.SameAs(flow));
            Assert.That(directory.TryGetEngine("world", "begin", out var current), Is.True);
            Assert.That(current, Is.SameAs(repeated));
            Assert.That(flow.CanActivate("left"), Is.False);

            Assert.That(flow.Activate("begin"), Is.True);
            Assert.That(flow.CanActivate("left"), Is.True);
        }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// 끝나지 않았거나 반복할 수 없거나 선택받지 못한 대안은 교체를 거부하고 아무것도 바꾸지 않는다.
        /// </summary>
        // --------------------------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveFlow_교체할수없으면거부한다()
        {
            var sources = RepeatEngines();
            var flow = new ObjectiveFlow(RepeatSpec(), new ObjectiveFlowState("flow", "world"), sources);
            flow.Activate("begin");
            flow.SelectChoice("route", "left");

            Assert.Throws<InvalidOperationException>(() => flow.Repeat("left", "left-2"));
            Assert.Throws<InvalidOperationException>(() => flow.Repeat("end", "end-2"));
            Assert.Throws<InvalidOperationException>(() => flow.Repeat("right", "right-2"));
            Assert.Throws<ArgumentException>(() => flow.Repeat("missing", "x"));
            Assert.Throws<ArgumentException>(() => flow.Repeat("begin", "begin-1"));
            Assert.That(flow.GetEngine("begin"), Is.SameAs(sources[0]));
            Assert.That(sources[0].Flow, Is.SameAs(flow));

            flow.Dispose();
            Assert.Throws<ObjectDisposedException>(() => flow.Repeat("begin", "begin-2"));
        }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// Flow가 소유한 Engine은 직접 새 회차를 만들 수 없고, Flow를 놓으면 다시 만들 수 있다.
        /// </summary>
        // --------------------------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveFlow_Flow소유Engine의직접반복을거부한다()
        {
            var directory = new ObjectiveDirectory();
            var sources = RepeatEngines(directory: directory);
            var flow = new ObjectiveFlow(RepeatSpec(), new ObjectiveFlowState("flow", "world"), sources);
            flow.Activate("begin");
            Assert.That(sources[0].Instance.IsTerminal, Is.True);

            Assert.Throws<InvalidOperationException>(() => sources[0].CreateRepeat("begin-2"));
            Assert.That(directory.TryGetEngine("world", "begin", out var current), Is.True);
            Assert.That(current, Is.SameAs(sources[0]));

            flow.Dispose();
            Assert.That(sources[0].CreateRepeat("begin-2").Instance.InstanceID, Is.EqualTo("begin-2"));
        }

    #endregion

    #region Choice 재개방

        // --------------------------------------------------------------------------------
        /// <summary>
        /// 진행 중인 선택을 종료하고 선택 기록을 지운 뒤 모든 대안을 새 회차로 바꿔 다시 고르게 한다.
        /// </summary>
        // --------------------------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveFlow_Choice를다시열고새회차로고른다()
        {
            var directory = new ObjectiveDirectory();
            var sources = RepeatEngines(directory: directory);
            var flow = new ObjectiveFlow(RepeatSpec(), new ObjectiveFlowState("flow", "world"), sources);
            flow.Activate("begin");
            flow.SelectChoice("route", "left");
            flow.Activate("left");
            int cancelled = 0;
            sources[1].Changed += () => cancelled++;

            var reopened = flow.ReopenChoice("route", NextRun);

            Assert.That(reopened.Count, Is.EqualTo(2));
            Assert.That(sources[1].Instance.Status, Is.EqualTo(ObjectiveStatus.Cancelled));
            Assert.That(cancelled, Is.EqualTo(1));
            Assert.That(flow.State.TryGetSelection("route", out _), Is.False);
            Assert.That(reopened[0].Instance.InstanceID, Is.EqualTo("left-2"));
            Assert.That(reopened[1].Instance.InstanceID, Is.EqualTo("right-2"));
            Assert.That(flow.GetEngine("left"), Is.SameAs(reopened[0]));
            Assert.That(flow.GetEngine("right"), Is.SameAs(reopened[1]));
            Assert.That(directory.TryGetEngine("world", "right", out var current), Is.True);
            Assert.That(current, Is.SameAs(reopened[1]));
            Assert.That(flow.CanActivate("left"), Is.False);
            Assert.That(flow.CanActivate("right"), Is.False);

            Assert.That(flow.SelectChoice("route", "right"), Is.True);
            Assert.That(flow.Activate("right"), Is.True);
            Assert.That(reopened[1].Instance.Status, Is.EqualTo(ObjectiveStatus.Active));
            Assert.That(reopened[0].Instance.Status, Is.EqualTo(ObjectiveStatus.Cancelled));
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// 이미 끝난 선택은 종료를 다시 하지 않고 교체만 하며, 교체 뒤 저장은 다시 열린다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveFlow_종료한선택을다시열어도저장이열린다()
        {
            var sources = RepeatEngines();
            var state = new ObjectiveFlowState("flow", "world");
            var flow = new ObjectiveFlow(RepeatSpec(), state, sources);
            flow.Activate("begin");
            flow.SelectChoice("route", "left");
            flow.Activate("left");
            sources[1].Force(ObjectiveStatus.Failed);

            var reopened = flow.ReopenChoice("route", NextRun);
            flow.Dispose();

            var current = new[]
            {
                sources[0], reopened[0], reopened[1], sources[3]
            };
            using var restored = new ObjectiveFlow(RepeatSpec(), state, current);
            Assert.That(restored.State.TryGetSelection("route", out _), Is.False);
            Assert.That(restored.SelectChoice("route", "left"), Is.True);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 열 수 없는 조건이면 선택 기록·선택한 대안·모든 Engine이 그대로이다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveFlow_열수없으면아무것도바뀌지않는다()
        {
            var sources = RepeatEngines(rightRepeatable: false);
            var flow = new ObjectiveFlow(RepeatSpec(), new ObjectiveFlowState("flow", "world"), sources);
            Assert.Throws<InvalidOperationException>(() => flow.ReopenChoice("route", NextRun));

            flow.Activate("begin");
            flow.SelectChoice("route", "left");
            flow.Activate("left");

            // 대안 하나가 Repeatable이 아니면 선택한 대안의 종료도 반영하지 않는다.
            Assert.Throws<InvalidOperationException>(() => flow.ReopenChoice("route", NextRun));
            AssertUnchanged(flow, sources);

            // 새 InstanceID가 기존과 같으면 마찬가지다.
            var repeatable = RepeatEngines();
            using var other = new ObjectiveFlow(RepeatSpec(), new ObjectiveFlowState("flow", "world"), repeatable);
            other.Activate("begin");
            other.SelectChoice("route", "left");
            other.Activate("left");
            Assert.Throws<ArgumentException>(() => other.ReopenChoice("route", id => id + "-1"));
            AssertUnchanged(other, repeatable);
            Assert.Throws<ArgumentNullException>(() => other.ReopenChoice("route", null));
            Assert.Throws<ArgumentException>(() => other.ReopenChoice("missing", NextRun));
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 성공한 선택은 다시 열 수 없다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveFlow_성공한선택은다시열수없다()
        {
            var sources = RepeatEngines();
            var flow = new ObjectiveFlow(RepeatSpec(), new ObjectiveFlowState("flow", "world"), sources);
            flow.Activate("begin");
            flow.SelectChoice("route", "left");
            flow.Activate("left");
            sources[1].Force(ObjectiveStatus.Succeeded);

            Assert.Throws<InvalidOperationException>(() => flow.ReopenChoice("route", NextRun));
            AssertUnchanged(flow, sources);
        }

        private static void AssertUnchanged(ObjectiveFlow flow, ObjectiveEngine[] sources)
        {
            Assert.That(flow.State.TryGetSelection("route", out var selected), Is.True);
            Assert.That(selected, Is.EqualTo("left"));
            Assert.That(flow.GetEngine("left"), Is.SameAs(sources[1]));
            Assert.That(flow.GetEngine("right"), Is.SameAs(sources[2]));
            Assert.That(sources[1].Instance.Status, Is.Not.EqualTo(ObjectiveStatus.Cancelled));
        }

    #endregion

    }
}
