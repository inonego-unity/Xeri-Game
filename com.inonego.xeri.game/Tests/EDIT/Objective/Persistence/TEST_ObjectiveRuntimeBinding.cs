/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : TEST_ObjectiveRuntimeBinding.cs
수정일 : 2026-11-25

# 설명
Save 루트의 전체 왕복, 배치 재바인딩, 외부 Fact 준비 및 초기화 비발행을 검증한다.

# 특이사항, 제약사항
외부 게임 Owner 대신 테스트용 Fact와 Save 루트를 사용하며 실제 Game Save I/O는 구현하지 않는다.
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

namespace inonego.Xeri.Game.TEST.Objective
{
    // ============================================================
    /// <summary>
    /// Save Owner 경계의 직접 영속 상태와 비영속 재바인딩을 검증한다.
    /// </summary>
    // ============================================================
    public sealed class TEST_ObjectiveRuntimeBinding
    {

    #region 테스트 준비

        [Serializable]
        private sealed class SaveRoot
        {
            [SerializeField] private ObjectiveInstance[] objectives;
            [SerializeField] private ObjectiveFlowState[] flows;

            internal SaveRoot(ObjectiveInstance[] objectives, ObjectiveFlowState[] flows = null)
            {
                this.objectives = objectives;
                this.flows = flows;
            }

            internal ObjectiveInstance[] GetObjectives() => objectives;
            internal ObjectiveFlowState[] GetFlows() => flows;
        }

        private sealed class Fact : ICond<ObjectiveContext>
        {
            internal bool Ready = true;
            internal bool Value = true;

            // ------------------------------------------------------------
            /// <summary>
            /// 외부 Owner가 준비되기 전에는 Condition 조회를 거부한다.
            /// </summary>
            // ------------------------------------------------------------
            public bool Check(ObjectiveContext context)
            {
                if (!Ready)
                {
                    throw new InvalidOperationException("Fact Owner is not ready.");
                }

                return Value;
            }
        }

        private static ObjectiveSpec Quest
        (
            Fact fact, TaskRevalidationMode mode = TaskRevalidationMode.OncePassed
        )
        {
            var first = new TaskChildEntry
            (
                "first", new TaskSpec("fact-node", fact),
                revalidationMode: mode
            );
            var owned = new string[]
            {
                "count"
            };
            var second = new TaskChildEntry
            (
                "second", new TaskSpec("counter-node", new CounterCond("count", 3), localProgressIDs: owned),
                rollbackMode: TaskRollbackMode.ResetOnRollback
            );
            var root = new TaskGroupSpec
            (
                "sequence", TaskExecutionMode.Sequential, TaskGroupCond.All(),
                new[]
                {
                    first,
                    second
                }
            );
            return new ObjectiveSpec
            (
                "quest", 4, root,
                ObjectiveCommitMode.Manual, ObjectiveCommitMode.Manual
            );
        }

        private static ObjectiveSpec Side() =>
            new("side", 1, new TaskSpec("side-root", new Fact()));

        private static ObjectiveSpec Manual()
        {
            var owned = new string[]
            {
                "done"
            };
            var progress = new string[]
            {
                "done"
            };
            var root = new TaskSpec("manual-root", new FlagCond("done"), localProgressIDs: owned);
            return new ObjectiveSpec("manual", 1, root);
        }

        private static ObjectiveInstance Instance(ObjectiveSpec spec, string id, string scope) =>
            new(spec.SpecID, spec.Version, id, scope);

        private static ObjectiveSpecRegistry Registry(params ObjectiveSpec[] specs)
        {
            var registry = new ObjectiveSpecRegistry();
            registry.RegisterRange(specs);
            return registry;
        }

        private static ObjectiveInstance CorruptAsInactive(ObjectiveInstance source)
        {
            string json = JsonUtility.ToJson(source);
            string current = "\"status\":" + (int)source.Status;
            Assert.That(json, Does.Contain(current));
            return JsonUtility.FromJson<ObjectiveInstance>(json.Replace(current, "\"status\":0"));
        }

    #endregion

    #region 복원 계약

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/>다중 Scope, Flow 선택, Progress와 시간·이력을 직접 저장한다.
        /// <br/>외부 Fact 준비 후 재바인딩해도 영속 상태를 변경하지 않는다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveRuntimeBinding_전체Save왕복과비발행()
        {
            var fact = new Fact();
            var quest = Quest(fact);
            var side = Side();
            var manual = Manual();
            var registry = Registry(quest, side, manual);
            var first = new ObjectiveEngine(quest, Instance(quest, "run-q", "world-a"));
            var other = new ObjectiveEngine(side, Instance(side, "run-s", "world-a"));
            var completed = new ObjectiveEngine(manual, Instance(manual, "run-m", "world-b"));

            Assert.That(first.Activate(), Is.True);
            Assert.That(first.EditProgress("count", values => values.AddI("count", 2)), Is.True);
            Assert.That(first.AdvanceTime(2.75), Is.True);
            Assert.That(first.Instance.HasPassed("first"), Is.True);
            Assert.That(first.Instance.TryGetBoundary("sequence", out string opened), Is.True);
            Assert.That(opened, Is.EqualTo("second"));

            Assert.That(completed.Activate(), Is.True);
            Assert.That(completed.EditProgress("done", values => values.SetFlag("done")), Is.True);
            Assert.That(completed.Instance.Status, Is.EqualTo(ObjectiveStatus.Succeeded));

            var flowEntries = new ObjectiveFlowEntry[]
            {
                new("quest"),
                new("side")
            };
            var alternatives = new string[]
            {
                "quest",
                "side"
            };
            var flowChoices = new ObjectiveFlowChoice[]
            {
                new("route", alternatives)
            };
            var flowDefinition = new ObjectiveFlowSpec("flow", flowEntries, flowChoices);
            var state = new ObjectiveFlowState("flow", "world-a");
            var flowEngines = new ObjectiveEngine[]
            {
                first,
                other
            };
            var flow = new ObjectiveFlow(flowDefinition, state, flowEngines);
            Assert.That(flow.SelectChoice("route", "quest"), Is.True);

            var savedInstances = new ObjectiveInstance[]
            {
                first.Instance,
                other.Instance,
                completed.Instance
            };
            var savedFlows = new ObjectiveFlowState[]
            {
                state
            };
            var source = new SaveRoot(savedInstances, savedFlows);
            string json = JsonUtility.ToJson(source);
            var loaded = JsonUtility.FromJson<SaveRoot>(json);
            Assert.That(loaded.GetObjectives()[0], Is.Not.SameAs(first.Instance));

            fact.Ready = false;
            Assert.Throws<InvalidOperationException>(() => ObjectiveRuntimeBinding.Bind(registry, loaded.GetObjectives()));
            Assert.That(JsonUtility.ToJson(loaded), Is.EqualTo(json));

            fact.Ready = true;
            var engines = ObjectiveRuntimeBinding.Bind(registry, loaded.GetObjectives());
            Assert.That(engines.Count, Is.EqualTo(3));
            Assert.That(JsonUtility.ToJson(loaded), Is.EqualTo(json));
            Assert.That(engines[0].Instance.ElapsedTime, Is.EqualTo(2.75).Within(0.000001));
            int current = engines[0].Instance.Progress.Values.GetI("count");
            Assert.That(current, Is.EqualTo(2));
            Assert.That(engines[0].Instance.HasPassed("first"), Is.True);
            Assert.That(engines[0].Instance.TryGetBoundary("sequence", out opened), Is.True);
            Assert.That(opened, Is.EqualTo("second"));
            Assert.That(engines[1].Instance.Status, Is.EqualTo(ObjectiveStatus.Cancelled));
            Assert.That(engines[2].Instance.Status, Is.EqualTo(ObjectiveStatus.Succeeded));
            bool done = engines[2].Instance.Progress.Values.HasFlag("done");
            Assert.That(done, Is.True);

            var boundFlowEngines = new ObjectiveEngine[]
            {
                engines[0],
                engines[1]
            };
            var restoredFlow = new ObjectiveFlow(flowDefinition, loaded.GetFlows()[0], boundFlowEngines);
            Assert.That(restoredFlow.State.TryGetSelection("route", out string selected), Is.True);
            Assert.That(selected, Is.EqualTo("quest"));

            int changes = 0;
            int evaluations = 0;
            engines[0].Changed += () => changes++;
            engines[0].EvaluationChanged += result => evaluations++;
            Assert.That(engines[0].Refresh(), Is.False);
            Assert.That(changes, Is.Zero);
            Assert.That(evaluations, Is.Zero);
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// 복원 시 WhileValid 후퇴를 미리 커밋하지 않고 첫 Refresh에서만 Reset한다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveRuntimeBinding_복원뒤Rollback한번()
        {
            var fact = new Fact();
            var spec = Quest(fact, TaskRevalidationMode.WhileValid);
            var engine = new ObjectiveEngine(spec, Instance(spec, "run", "scope"));
            engine.Activate();
            engine.EditProgress("count", values => values.AddI("count", 2));
            fact.Value = false;

            var savedInstances = new ObjectiveInstance[]
            {
                engine.Instance
            };
            var root = new SaveRoot(savedInstances);
            var loaded = JsonUtility.FromJson<SaveRoot>(JsonUtility.ToJson(root));
            var restored = ObjectiveRuntimeBinding.Bind(Registry(spec), loaded.GetObjectives())[0];
            int before = restored.Instance.Progress.Values.GetI("count");
            Assert.That(before, Is.EqualTo(2));
            Assert.That(restored.Instance.TryGetBoundary("sequence", out string boundary), Is.True);
            Assert.That(boundary, Is.EqualTo("second"));

            int changes = 0;
            restored.Changed += () => changes++;
            Assert.That(restored.Refresh(), Is.True);
            int after = restored.Instance.Progress.Values.GetI("count");
            Assert.That(after, Is.Zero);
            Assert.That(restored.Instance.TryGetBoundary("sequence", out boundary), Is.True);
            Assert.That(boundary, Is.EqualTo("first"));
            Assert.That(restored.Refresh(), Is.False);
            Assert.That(changes, Is.EqualTo(1));
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// 중복 ID와 잘못된 Version 배치는 어떤 부분 Engine 집합도 반환하지 않는다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveRuntimeBinding_중복과손상배치거부()
        {
            var spec = Side();
            var registry = Registry(spec);
            var original = Instance(spec, "same", "world");
            var duplicate = JsonUtility.FromJson<ObjectiveInstance>(JsonUtility.ToJson(original));
            var duplicates = new ObjectiveInstance[]
            {
                original,
                duplicate
            };
            Assert.Throws<InvalidOperationException>(() => ObjectiveRuntimeBinding.Bind(registry, duplicates));

            var otherScope = Instance(spec, "same", "account");
            var acrossScopes = new ObjectiveInstance[]
            {
                original,
                otherScope
            };
            Assert.That(ObjectiveRuntimeBinding.Bind(registry, acrossScopes).Count, Is.EqualTo(2));

            var separate = Instance(spec, "separate", "world");
            string json = JsonUtility.ToJson(separate);
            string invalidJson = json.Replace("\"specVersion\":1", "\"specVersion\":99");
            Assert.That(invalidJson, Is.Not.EqualTo(json));
            var invalid = JsonUtility.FromJson<ObjectiveInstance>(invalidJson);
            var corrupted = new ObjectiveInstance[]
            {
                original,
                invalid
            };
            Assert.Throws<ArgumentException>(() => ObjectiveRuntimeBinding.Bind(registry, corrupted));

            var withNull = new ObjectiveInstance[]
            {
                original,
                null
            };
            Assert.Throws<InvalidOperationException>(() => ObjectiveRuntimeBinding.Bind(registry, withNull));
            Assert.Throws<ArgumentNullException>(() => ObjectiveRuntimeBinding.Bind(null, acrossScopes));
            Assert.Throws<ArgumentNullException>(() => ObjectiveRuntimeBinding.Bind(registry, null));
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 저장된 Inactive에 진행·시간·통과 이력이 있으면 복원을 거부한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveRuntimeBinding_Inactive영속이력거부()
        {
            var fact = new Fact();
            var quest = Quest(fact);
            var manual = Manual();
            var neverFinished = new Fact();
            neverFinished.Value = false;
            var timed = new ObjectiveSpec
            (
                "timed", 1, new TaskSpec("timed-root", neverFinished)
            );
            var owned = new string[]
            {
                "count"
            };
            var progress = new string[]
            {
                "count"
            };
            var counter = new ObjectiveSpec
            (
                "counter", 1,
                new TaskSpec("counter-root", new CounterCond("count", 3), localProgressIDs: owned)
            );
            var registry = Registry(quest, manual, timed, counter);

            var history = new ObjectiveEngine(quest, Instance(quest, "history", "world"));
            Assert.That(history.Activate(), Is.True);
            Assert.That(history.Instance.HasPassed("first"), Is.True);

            var savedHistory = new ObjectiveInstance[]
            {
                CorruptAsInactive(history.Instance)
            };
            Assert.Throws<InvalidOperationException>(() => ObjectiveRuntimeBinding.Bind(registry, savedHistory));

            var progressed = new ObjectiveEngine(manual, Instance(manual, "manual-run", "world"));
            progressed.Activate();
            progressed.EditProgress("done", values => values.SetFlag("done"));
            var invalidManual = CorruptAsInactive(progressed.Instance);
            var savedManual = new ObjectiveInstance[]
            {
                invalidManual
            };
            Assert.Throws<InvalidOperationException>(() => ObjectiveRuntimeBinding.Bind(registry, savedManual));
            Assert.Throws<InvalidOperationException>(() => invalidManual.Validate());

            var counted = new ObjectiveEngine(counter, Instance(counter, "counter-run", "world"));
            counted.Activate();
            counted.EditProgress("count", values => values.AddI("count", 1));
            var invalidCounter = CorruptAsInactive(counted.Instance);
            var savedCounter = new ObjectiveInstance[]
            {
                invalidCounter
            };
            Assert.Throws<InvalidOperationException>(() => ObjectiveRuntimeBinding.Bind(registry, savedCounter));
            Assert.Throws<InvalidOperationException>(() => invalidCounter.Validate());

            var clock = new ObjectiveEngine(timed, Instance(timed, "clock", "world"));
            clock.Activate();
            clock.AdvanceTime(1.25);
            var savedTime = new ObjectiveInstance[]
            {
                CorruptAsInactive(clock.Instance)
            };
            Assert.Throws<InvalidOperationException>(() => ObjectiveRuntimeBinding.Bind(registry, savedTime));
        }

    #endregion

    }
}
