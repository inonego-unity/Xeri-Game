/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : TEST_Execution.cs
수정일 : 2026-11-12

# 설명
ExecutionSession의 시간·Commit·Behavior 수명·ValueRegistry 격리와 기본 Flow/Trigger 계약을 검증한다.
Ability 선택·비용 지불·Fake Host·Session Begin을 연결한 승인/실패 경계도 검증한다.

# 테스트 구성
 S: Session 구성·Runtime 소유권·시간·수명·Child
 H: HoldRelease 진행·해제 시간
 C: Command 판정과 Signal 전달
 T: Trigger 발동·정의·Behavior 연동·종료
 E: 시작·진행·Observer 실패 복구
========================================================================= BLOCK_HEADER_END */

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

using NUnit;
using NUnit.Framework;

namespace inonego.Xeri.Game.TEST._Execution
{
    using Xeri;
    using Xeri.Game;

    // ======================================================================
    /// <summary>
    /// Xeri Execution의 Session·Flow·Trigger Runtime 계약을 검증한다.
    /// </summary>
    // ======================================================================
    public sealed class TEST_Execution
    {

    #region 헬퍼

        // ============================================================
        /// <summary>
        /// Behavior 진행·알림·수명과 실패 조건을 관찰하는 공통 테스트 대역.
        /// </summary>
        // ============================================================
        private sealed class TraceBehaviorRuntime : ExecutionBehaviorRuntime
        {

        #region 관찰 상태

            private readonly string name;
            private readonly List<string> trace;
            private readonly bool failOnBegin;

            public int EndCount { get; private set; }
            public int SignalCount { get; private set; }
            public float LastDelta { get; private set; }
            public Action<ExecutionSession> OnBegin { get; set; }
            public Action<ExecutionSession, float> OnAdvance { get; set; }

        #endregion

        #region 생성자

            public TraceBehaviorRuntime
            (
                string name = "Probe",
                List<string> trace = null,
                bool failOnBegin = false
            )
            {
                this.name = name;
                this.trace = trace;
                this.failOnBegin = failOnBegin;
            }

        #endregion

        #region 실행 수명과 알림

            public override void Begin(ExecutionSession session)
            {
                trace?.Add($"{name}.Begin");

                if (failOnBegin)
                {
                    throw new InvalidOperationException($"{name} begin failure");
                }

                OnBegin?.Invoke(session);
            }

            public override void Advance(ExecutionSession session, float deltaTime)
            {
                LastDelta = deltaTime;
                trace?.Add($"{name}.Advance");

                OnAdvance?.Invoke(session, deltaTime);
            }

            public override void Signal(ExecutionSession session, in ExecutionSignal signal)
            {
                SignalCount++;
            }

            public override void End(ExecutionSession session, ExecutionEndReason reason)
            {
                EndCount++;
                trace?.Add($"{name}.End:{reason}");
            }

        #endregion

        }

        // ------------------------------------------------------------
        /// <summary>
        /// 지정한 시간·Behavior·문맥으로 독립 Session을 구성한다.
        /// </summary>
        // ------------------------------------------------------------
        private static ExecutionSession CreateTimelineSession
        (
            float duration = 1f,
            IReadOnlyList<ExecutionBehaviorRuntime> behavior = null,
            ExecutionContext context = default,
            ulong id = 1UL
        )
        {
            var flow = new FixedTimelineExecutionFlowDefinition(duration);
            return new ExecutionSession
            (
                id,
                context,
                flow.CreateRuntime(),
                behavior,
                flow.MaximumDuration
            );
        }

        // ============================================================
        /// <summary>
        /// 공통 Trigger를 서로 다른 Behavior에 결합하는 실행 테스트 어댑터.
        /// </summary>
        // ============================================================
        private sealed class TriggerTraceBehaviorRuntime : ExecutionBehaviorRuntime
        {

        #region 상태

            private readonly string name;
            private readonly ExecutionTrigger trigger;
            private readonly List<string> trace;
            private readonly List<float> times;

        #endregion

        #region 생성자

            public TriggerTraceBehaviorRuntime
            (
                string name, ExecutionTrigger trigger,
                List<string> trace, List<float> times
            )
            {
                this.name = name;
                this.trigger = trigger;
                this.trace = trace;
                this.times = times;
            }

        #endregion

        #region 실행 수명과 Signal

            public override void Begin(ExecutionSession session)
            {
                trigger.Begin();
            }

            public override void Advance(ExecutionSession session, float deltaTime)
            {
                if (trigger.TryEvaluate(session.PreviousTime, session.Time, out var effectiveTime))
                {
                    trace.Add(name);
                    times.Add(effectiveTime);
                }
            }

            public override void Signal(ExecutionSession session, in ExecutionSignal signal)
            {
                trigger.Signal(signal, session.Time);
            }

            public override void End(ExecutionSession session, ExecutionEndReason reason)
            {
                trigger.End();
            }

        #endregion

        }

        // ============================================================
        /// <summary>
        /// 실행별 Trigger 수명 호출과 실패 정리를 관찰하는 테스트 구현.
        /// </summary>
        // ============================================================
        private sealed class LifecycleTrigger : ExecutionTrigger
        {

        #region 상태

            private readonly bool failOnBegin;
            private readonly bool failOnEnd;

            public int BeginCount { get; private set; }
            public int EndCount { get; private set; }

        #endregion

        #region 생성자

            public LifecycleTrigger(bool failOnBegin = false, bool failOnEnd = false)
            {
                this.failOnBegin = failOnBegin;
                this.failOnEnd = failOnEnd;
            }

        #endregion

        #region 실행 수명과 평가

            public override void Begin()
            {
                BeginCount++;
                if (failOnBegin)
                {
                    throw new InvalidOperationException("Trigger begin failure");
                }
            }

            public override void End()
            {
                EndCount++;
                if (failOnEnd)
                {
                    throw new InvalidOperationException("Trigger end failure");
                }
            }

            public override bool TryEvaluate
            (
                float previousTime,
                float currentTime,
                out float effectiveTime
            )
            {
                effectiveTime = 0f;
                return false;
            }

        #endregion

        }

    #endregion

    #region S-1: Session 구성·Runtime 소유권

        // ------------------------------------------------------------
        /// <summary>
        /// 외부 배열과 View 변경이 내부 Behavior에 영향을 주지 않아야 한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ExecutionSession_Behavior목록은외부에서교체할수없음()
        {
            var original = new TraceBehaviorRuntime();
            var supplied = new ExecutionBehaviorRuntime[]
            {
                original
            };
            var session = CreateTimelineSession(behavior: supplied);

            supplied[0] = new TraceBehaviorRuntime();
            Assert.Throws<NotSupportedException>
            (
                () => ((IList<ExecutionBehaviorRuntime>)session.Behaviors)[0] = new TraceBehaviorRuntime()
            );

            session.Begin();
            session.End(ExecutionEndReason.Cancelled);

            Assert.AreSame(original, session.Behaviors[0]);
            Assert.AreEqual(1, original.EndCount);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 이미 귀속된 Flow Runtime은 종료 후에도 재사용할 수 없어야 한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ExecutionSession_FlowRuntime은다른Session에재사용할수없음()
        {
            var flow = new FixedTimelineExecutionFlowDefinition(0.5f).CreateRuntime();
            var original = new ExecutionSession(20UL, default, flow);

            Assert.Throws<InvalidOperationException>
            (
                () => new ExecutionSession(21UL, default, flow)
            );

            original.Begin();
            original.Advance(0.5f);
            Assert.IsTrue(original.IsComplete);
            original.End(ExecutionEndReason.Completed);

            Assert.Throws<InvalidOperationException>
            (
                () => new ExecutionSession(22UL, default, flow)
            );
            Assert.IsTrue(flow.IsComplete);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 중복·재사용 거부 후에도 다른 Runtime의 정상 귀속을 보장한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ExecutionSession_BehaviorRuntime은중복과다른Session재사용을거부()
        {
            var behavior = new TraceBehaviorRuntime();
            var duplicated = new ExecutionBehaviorRuntime[]
            {
                behavior,
                behavior
            };

            Assert.Throws<ArgumentException>
            (
                () => CreateTimelineSession(behavior: duplicated)
            );

            var originalBehaviors = new ExecutionBehaviorRuntime[]
            {
                behavior
            };
            var original = CreateTimelineSession(behavior: originalBehaviors);
            var availableBehavior = new TraceBehaviorRuntime();
            var unusedFlow = new FixedTimelineExecutionFlowDefinition(1f).CreateRuntime();
            var invalidBehaviors = new ExecutionBehaviorRuntime[]
            {
                availableBehavior,
                behavior
            };

            Assert.Throws<InvalidOperationException>
            (
                () => new ExecutionSession(23UL, default, unusedFlow, invalidBehaviors)
            );

            var replacement = new TraceBehaviorRuntime();
            var independentBehaviors = new ExecutionBehaviorRuntime[]
            {
                availableBehavior,
                replacement
            };
            var independent = new ExecutionSession(24UL, default, unusedFlow, independentBehaviors);

            original.Begin();
            independent.Begin();
            original.End(ExecutionEndReason.Cancelled);
            independent.End(ExecutionEndReason.Cancelled);

            Assert.AreEqual(1, behavior.EndCount);
            Assert.AreEqual(1, availableBehavior.EndCount);
            Assert.AreEqual(1, replacement.EndCount);

            Assert.Throws<InvalidOperationException>
            (
                () => CreateTimelineSession(behavior: originalBehaviors)
            );
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 각 Session의 변수와 Flag는 독립된 저장소를 사용한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ExecutionSession_ValueRegistry가_Session별로격리()
        {
            var first = CreateTimelineSession();
            var second = CreateTimelineSession();

            first.Variables.SetI("HitCount", 3);
            first.Variables.SetFlag("Charged");

            Assert.AreEqual(3, first.Variables.GetI("HitCount"));
            Assert.IsTrue(first.Variables.HasFlag("Charged"));
            Assert.IsFalse(second.Variables.Has("HitCount"));
            Assert.IsFalse(second.Variables.HasFlag("Charged"));
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 정의가 바뀌어도 생성된 Flow Runtime의 동작 설정이 유지된다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ExecutionFlow_Runtime은생성당시설정을보존()
        {
            var timeline = new FixedTimelineExecutionFlowDefinition(0.5f);
            var timelineSession = new ExecutionSession
            (
                56UL, default, timeline.CreateRuntime(),
                maximumTime: timeline.MaximumDuration
            );

            timelineSession.Begin();
            var durationField = typeof(FixedTimelineExecutionFlowDefinition)
                .GetField("duration", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(durationField);
            durationField.SetValue(timeline, 0.9f);
            timelineSession.Advance(0.5f);

            Assert.AreEqual(0.9f, timeline.Duration, 0.0001f);
            Assert.IsTrue(timelineSession.Flow.IsComplete);

            var hold = new HoldReleaseExecutionFlowDefinition("Release", 0.2f, 0.5f, 0.25f);
            var holdSession = new ExecutionSession
            (
                57UL, default, hold.CreateRuntime(),
                maximumTime: hold.MaximumDuration
            );

            holdSession.Begin();
            var maxHoldField = typeof(HoldReleaseExecutionFlowDefinition)
                .GetField("maxHoldTime", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(maxHoldField);
            maxHoldField.SetValue(hold, 0.9f);
            holdSession.Advance(0.75f);

            Assert.AreEqual(0.9f, hold.MaxHoldTime, 0.0001f);
            Assert.AreEqual(0.5f, holdSession.Flow.ReleaseTime, 0.0001f);
            Assert.IsTrue(holdSession.Flow.IsComplete);
        }

    #endregion

    #region S-2: Session 시간 진행·완료

        // ------------------------------------------------------------
        /// <summary>
        /// 완료 시 Behavior 종료 순서와 실행 시간 경계를 검증한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ExecutionSession_시간진행과_Behavior역순종료()
        {
            var trace = new List<string>();
            var first = new TraceBehaviorRuntime("First", trace);
            var second = new TraceBehaviorRuntime("Second", trace);
            var session = CreateTimelineSession
            (
                0.5f,
                new ExecutionBehaviorRuntime[]
                {
                    first,
                    second
                }
            );

            session.Begin();
            session.Advance(0.3f);
            session.Advance(0.3f);

            Assert.IsTrue(session.IsComplete);
            Assert.AreEqual(0.3f, session.PreviousTime, 0.0001f);
            Assert.AreEqual(0.5f, session.Time, 0.0001f);
            Assert.IsTrue(session.TryComplete());
            Assert.IsTrue(session.HasEnded);
            Assert.AreEqual(ExecutionEndReason.Completed, session.EndReason);
            Assert.IsFalse(session.TryComplete());

            CollectionAssert.AreEqual
            (
                new[]
                {
                    "First.Begin",
                    "Second.Begin",
                    "First.Advance",
                    "Second.Advance",
                    "First.Advance",
                    "Second.Advance",
                    "Second.End:Completed",
                    "First.End:Completed",
                },
                trace
            );
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 큰 Delta가 적용 시간 상한을 넘더라도 진행량은 제한된다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ExecutionSession_큰Tick도_실제시간만Flow와Behavior에반영()
        {
            var probe = new TraceBehaviorRuntime();
            var flow = new FixedTimelineExecutionFlowDefinition(2f);
            var session = new ExecutionSession
            (
                50UL, default, flow.CreateRuntime(),
                new ExecutionBehaviorRuntime[]
                {
                    probe
                },
                maximumTime: 0.75f
            );

            session.Begin();
            session.Advance(10f);

            Assert.AreEqual(10f, session.RequestedDelta, 0.0001f);
            Assert.AreEqual(0.75f, session.AppliedDelta, 0.0001f);
            Assert.AreEqual(0f, session.PreviousTime, 0.0001f);
            Assert.AreEqual(0.75f, session.Time, 0.0001f);
            Assert.AreEqual(0.75f, probe.LastDelta, 0.0001f);
            Assert.IsFalse(session.Flow.IsComplete);
            Assert.IsTrue(session.IsComplete);

            var window = new ExecutionTimeWindowTriggerDefinition(0.5f, 0.9f).CreateTrigger();
            window.Begin();
            Assert.IsTrue(window.TryEvaluate(session.PreviousTime, session.Time, out var effectiveTime));
            Assert.AreEqual(0.5f, effectiveTime, 0.0001f);

            Assert.IsTrue(session.TryComplete());
        }

    #endregion

    #region S-3: Session 수명·Commit

        // ------------------------------------------------------------
        /// <summary>
        /// Commit은 최초 한 번만 성공하며 종료 후 다시 발생하지 않는다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ExecutionSession_Commit은_한번만확정()
        {
            var session = CreateTimelineSession();
            var committedCount = 0;
            session.OnCommitted += _ => committedCount++;

            session.Begin();

            Assert.IsTrue(session.TryCommit());
            Assert.IsFalse(session.TryCommit());
            Assert.IsTrue(session.IsCommitted);
            Assert.AreEqual(1, committedCount);

            session.End(ExecutionEndReason.Cancelled);

            Assert.IsFalse(session.TryCommit());
            Assert.AreEqual(1, committedCount);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Begin·Commit·End 이벤트 순서와 호출 횟수 계약을 확인한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ExecutionSession_LocalLifecycleHook은_BeginCommitEnd순서로한번만발생()
        {
            var trace = new List<string>();
            var session = CreateTimelineSession();

            session.OnBegun += _ => trace.Add("Begun");
            session.OnCommitted += _ => trace.Add("Committed");
            session.OnEnded += (_, reason) => trace.Add($"Ended:{reason}");

            Assert.IsFalse(session.HasBegun);
            Assert.IsFalse(session.HasEnded);
            session.Begin();
            Assert.IsTrue(session.HasBegun);
            Assert.IsFalse(session.HasEnded);
            Assert.IsTrue(session.TryCommit());
            Assert.IsFalse(session.TryCommit());

            session.Advance(1f);

            Assert.IsTrue(session.TryComplete());
            Assert.IsFalse(session.TryComplete());
            Assert.IsTrue(session.HasBegun);
            Assert.IsTrue(session.HasEnded);
            CollectionAssert.AreEqual
            (
                new[]
                {
                    "Begun",
                    "Committed",
                    "Ended:Completed"
                },
                trace
            );
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 실행 전후 무효 호출은 진행하거나 재시작할 수 없어야 한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ExecutionSession_Active전후_InvalidLifecycle호출을거부()
        {
            var session = CreateTimelineSession();
            var signal = new ExecutionSignal("Confirm");

            Assert.IsFalse(session.TryCommit());
            Assert.IsFalse(session.TryComplete());
            Assert.IsFalse(session.End(ExecutionEndReason.Cancelled));
            Assert.Throws<InvalidOperationException>(() => session.PublishSignal(signal));
            Assert.Throws<InvalidOperationException>
            (
                () => session.Advance(0.1f)
            );

            session.Begin();

            Assert.Throws<InvalidOperationException>
            (
                () => session.Begin()
            );
            Assert.IsTrue(session.End(ExecutionEndReason.Cancelled));
            Assert.IsFalse(session.TryCommit());
            Assert.IsFalse(session.TryComplete());
            Assert.IsFalse(session.End(ExecutionEndReason.Cancelled));
            Assert.Throws<InvalidOperationException>(() => session.PublishSignal(signal));
            Assert.Throws<InvalidOperationException>
            (
                () => session.Advance(0.1f)
            );
        }

    #endregion

    #region S-4: Parent·Child 연결 수명

        // ------------------------------------------------------------
        /// <summary>
        /// 부모 종료 시 연결된 자식도 취소 종료되어야 한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ExecutionSession_AttachedChild를_Parent종료시정리()
        {
            var parent = CreateTimelineSession(id: 10UL);
            var child = CreateTimelineSession(id: 11UL);

            parent.Begin();
            child.Begin();

            Assert.IsTrue
            (
                parent.AttachChild(child)
            );

            parent.End(ExecutionEndReason.Interrupted);

            Assert.IsTrue(parent.HasEnded);
            Assert.IsTrue(child.HasEnded);
            Assert.AreEqual(ExecutionEndReason.Cancelled, child.EndReason);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 분리한 자식은 부모의 종료 영향을 받지 않아야 한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ExecutionSession_DetachedChild는_Parent종료후에도유지()
        {
            var parent = CreateTimelineSession(id: 20UL);
            var child = CreateTimelineSession(id: 21UL);

            parent.Begin();
            child.Begin();

            Assert.IsTrue(parent.AttachChild(child));
            Assert.IsTrue(parent.DetachChild(child));

            parent.End(ExecutionEndReason.Completed);

            Assert.IsTrue(parent.HasEnded);
            Assert.IsTrue(child.IsActive);

            child.End(ExecutionEndReason.Cancelled);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 자식이 부모를 재연결해 소유권 순환을 만들 수 없다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ExecutionSession_AttachedOwnership순환을거부()
        {
            var parent = CreateTimelineSession(id: 40UL);
            var child = CreateTimelineSession(id: 41UL);

            parent.Begin();
            child.Begin();
            parent.AttachChild(child);

            Assert.Throws<InvalidOperationException>
            (
                () => child.AttachChild(parent)
            );

            parent.End(ExecutionEndReason.Cancelled);
        }

    #endregion

    #region H-1: HoldRelease 진행·시간 경계

        // ------------------------------------------------------------
        /// <summary>
        /// 최소 유지 전 요청은 지연되고 해제 후 회복 시간이 적용된다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_HoldRelease_MinHold뒤_Command로Release하고_Recovery후완료()
        {
            var definition = new HoldReleaseExecutionFlowDefinition
            (
                "Confirm",
                0.2f,
                1f,
                0.1f
            );
            var session = new ExecutionSession
            (
                2UL,
                default,
                definition.CreateRuntime(),
                maximumTime: definition.MaximumDuration
            );

            session.Begin();
            session.Advance(0.1f);
            Assert.AreEqual(ExecutionCommandResult.Deferred, session.Command(new ExecutionCommand("Confirm")));
            Assert.IsFalse(session.Flow.HasReleased);

            session.Advance(0.1f);
            Assert.IsTrue(session.Flow.HasReleased);
            Assert.AreEqual(0.2f, session.Flow.ReleaseTime, 0.0001f);

            session.Advance(0.1f);
            Assert.IsTrue(session.IsComplete);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Command 없이 최대 유지 시간에 도달하면 자동 해제되어야 한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_HoldRelease_MaxHold에서도Command없이자동Release()
        {
            var definition = new HoldReleaseExecutionFlowDefinition
            (
                "Release",
                0.1f,
                0.5f,
                0.2f
            );
            var session = new ExecutionSession
            (
                3UL,
                default,
                definition.CreateRuntime(),
                maximumTime: definition.MaximumDuration
            );

            session.Begin();
            session.Advance(0.5f);

            Assert.IsTrue(session.Flow.HasReleased);
            Assert.AreEqual(0.5f, session.Flow.ReleaseTime, 0.0001f);
            Assert.AreEqual(1f, session.Flow.ReleaseProgress, 0.0001f);
            Assert.IsFalse(session.IsComplete);

            session.Advance(0.2f);

            Assert.IsTrue(session.IsComplete);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 한 번에 시간이 초과돼도 자동 해제는 원래 경계에서 일어난다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_HoldRelease_큰Tick에서도자동해제시각을보존()
        {
            var definition = new HoldReleaseExecutionFlowDefinition("Release", 0.2f, 0.7f, 0.3f);
            var session = new ExecutionSession
            (
                51UL, default, definition.CreateRuntime(),
                maximumTime: definition.MaximumDuration
            );

            session.Begin();
            session.Advance(8f);

            Assert.AreEqual(0.7f, session.Flow.ReleaseTime, 0.0001f);
            Assert.AreEqual(1f, session.Time, 0.0001f);
            Assert.IsTrue(session.IsComplete);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 지연된 Command는 큰 Tick에서도 최소 유지 시각에 해제된다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_HoldRelease_지연Release_큰Tick에서도최소유지시각을보존()
        {
            var definition = new HoldReleaseExecutionFlowDefinition("Release", 0.25f, 0.8f, 0.2f);
            var session = new ExecutionSession
            (
                52UL, default, definition.CreateRuntime(),
                maximumTime: definition.MaximumDuration
            );

            session.Begin();
            Assert.AreEqual
            (
                ExecutionCommandResult.Deferred,
                session.Command(new ExecutionCommand("Release"))
            );
            session.Advance(9f);

            Assert.AreEqual(0.25f, session.Flow.ReleaseTime, 0.0001f);
            Assert.AreEqual(1f, session.Time, 0.0001f);
            Assert.IsTrue(session.IsComplete);
        }

    #endregion

    #region C-1: Command·Signal 처리

        // ------------------------------------------------------------
        /// <summary>
        /// Signal 전달은 해제 Command가 아니며 Flow 상태를 바꾸지 않는다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_HoldRelease_Signal알림은Flow를변경하지않음()
        {
            var definition = new HoldReleaseExecutionFlowDefinition("Release", 0.2f, 0.8f, 0.1f);
            var probe = new TraceBehaviorRuntime();
            var session = new ExecutionSession
            (
                55UL, default, definition.CreateRuntime(),
                new ExecutionBehaviorRuntime[]
                {
                    probe
                },
                maximumTime: definition.MaximumDuration
            );

            session.Begin();
            session.PublishSignal(new ExecutionSignal("Release"));
            session.Advance(0.3f);

            Assert.AreEqual(1, probe.SignalCount);
            Assert.IsTrue(session.Flow.IsHolding);
            Assert.IsFalse(session.Flow.HasReleased);

            Assert.AreEqual
            (
                ExecutionCommandResult.Accepted,
                session.Command(new ExecutionCommand("Release"))
            );
            Assert.AreEqual(0.3f, session.Flow.ReleaseTime, 0.0001f);
            Assert.AreEqual(1, probe.SignalCount);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 거부된 Command와 Behavior에 전달되는 Signal은 독립적이다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ExecutionSession_Command거부와Signal전달은별개()
        {
            var probe = new TraceBehaviorRuntime();
            var session = CreateTimelineSession
            (
                behavior: new ExecutionBehaviorRuntime[]
                {
                    probe
                }
            );

            session.Begin();
            Assert.AreEqual
            (
                ExecutionCommandResult.Rejected,
                session.Command(new ExecutionCommand("Release"))
            );
            Assert.AreEqual(0, probe.SignalCount);
            session.PublishSignal(new ExecutionSignal("Release"));
            Assert.AreEqual(1, probe.SignalCount);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 유효한 지연 Command와 잘못된 Command의 결과를 구별한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_HoldRelease_Command수락지연과거부상태를구별()
        {
            var definition = new HoldReleaseExecutionFlowDefinition("Release", 0.3f, 0.8f, 0.2f);
            var probe = new TraceBehaviorRuntime();
            var session = new ExecutionSession
            (
                53UL, default, definition.CreateRuntime(),
                new ExecutionBehaviorRuntime[]
                {
                    probe
                },
                maximumTime: definition.MaximumDuration
            );

            session.Begin();
            Assert.AreEqual
            (
                ExecutionCommandResult.Rejected,
                session.Command(new ExecutionCommand("Unknown"))
            );
            Assert.AreEqual
            (
                ExecutionCommandResult.Deferred,
                session.Command(new ExecutionCommand("Release"))
            );
            Assert.AreEqual(0, probe.SignalCount);
            session.PublishSignal(new ExecutionSignal("Release"));
            Assert.AreEqual(1, probe.SignalCount);

            session.Advance(0.7f);
            Assert.AreEqual(0.3f, session.Flow.ReleaseTime, 0.0001f);
            Assert.AreEqual
            (
                ExecutionCommandResult.Rejected,
                session.Command(new ExecutionCommand("Release"))
            );
            Assert.AreEqual(1, probe.SignalCount);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 최소 유지가 지난 Command는 현재 시각에 즉시 해제된다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_HoldRelease_최소유지후Command는즉시수락()
        {
            var definition = new HoldReleaseExecutionFlowDefinition("Release", 0.2f, 0.8f, 0.2f);
            var session = new ExecutionSession
            (
                54UL, default, definition.CreateRuntime(),
                maximumTime: definition.MaximumDuration
            );

            session.Begin();
            session.Advance(0.4f);
            Assert.AreEqual
            (
                ExecutionCommandResult.Accepted,
                session.Command(new ExecutionCommand("Release"))
            );
            Assert.AreEqual(0.4f, session.Flow.ReleaseTime, 0.0001f);
        }

    #endregion

    #region T-1: Trigger 시점·Signal 판정

        // ------------------------------------------------------------
        /// <summary>
        /// 정지 Tick에서는 발동하지 않고 지정 시각 최초 통과만 감지한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ExecutionTimePointTrigger_시간점을_처음통과할때만실행()
        {
            var runtime = new ExecutionTimePointTriggerDefinition(0f).CreateTrigger();
            runtime.Begin();

            Assert.IsFalse(runtime.TryEvaluate(0f, 0f, out _));
            Assert.IsTrue(runtime.TryEvaluate(0f, 0.1f, out var effectiveTime));
            Assert.AreEqual(0f, effectiveTime, 0.0001f);
            Assert.IsFalse(runtime.TryEvaluate(0.1f, 0.2f, out _));
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 시간창과 Tick의 겹침 및 교차 시작 시각이 정확해야 한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ExecutionTimeWindowTrigger_Tick과Window가겹치면활성()
        {
            var definition = new ExecutionTimeWindowTriggerDefinition(0.2f, 0.5f);
            var runtime = definition.CreateTrigger();

            Assert.IsFalse(runtime.TryEvaluate(0f, 0.1f, out _));
            Assert.IsTrue(runtime.TryEvaluate(0.1f, 0.3f, out var enteredTime));
            Assert.AreEqual(0.2f, enteredTime, 0.0001f);
            Assert.IsTrue(runtime.TryEvaluate(0.3f, 0.6f, out var overlapTime));
            Assert.AreEqual(0.3f, overlapTime, 0.0001f);
            Assert.IsFalse(runtime.TryEvaluate(0.6f, 0.7f, out _));
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Signal 수신과 Offset이 충족된 뒤 한 번만 발동해야 한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ExecutionSignalTrigger_Signal시각과Offset으로_한번만실행()
        {
            var definition = new ExecutionSignalTriggerDefinition("Detonate", 0.2f);
            var runtime = definition.CreateTrigger();
            runtime.Begin();
            runtime.Signal(new ExecutionSignal("Detonate"), 0.3f);

            Assert.IsFalse(runtime.TryEvaluate(0.3f, 0.49f, out _));
            Assert.IsTrue(runtime.TryEvaluate(0.49f, 0.5f, out var effectiveTime));
            Assert.AreEqual(0.5f, effectiveTime, 0.0001f);
            Assert.IsFalse(runtime.TryEvaluate(0.5f, 0.8f, out _));
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 빈 창·역방향 구간에서는 발동하지 않고 정상 경계만 인정한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ExecutionTimeWindowTrigger_빈Window와정지역행Tick에서는발동하지않음()
        {
            var empty = new ExecutionTimeWindowTriggerDefinition(0.4f, 0.4f).CreateTrigger();
            Assert.IsFalse(empty.TryEvaluate(0.2f, 0.5f, out _));

            var window = new ExecutionTimeWindowTriggerDefinition(0.2f, 0.5f).CreateTrigger();
            Assert.IsFalse(window.TryEvaluate(0.3f, 0.3f, out _));
            Assert.IsFalse(window.TryEvaluate(0.4f, 0.2f, out _));
            Assert.IsFalse(window.TryEvaluate(float.NaN, 0.3f, out _));
            Assert.IsFalse(window.TryEvaluate(0.3f, float.NaN, out _));
            Assert.IsFalse(window.TryEvaluate(0.5f, 0.6f, out _));

            Assert.IsTrue(window.TryEvaluate(0.1f, 0.2f, out var atBegin));
            Assert.AreEqual(0.2f, atBegin, 0.0001f);
            Assert.IsTrue(window.TryEvaluate(0.4f, 0.5f, out var atEnd));
            Assert.AreEqual(0.4f, atEnd, 0.0001f);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 공유 정의의 Trigger마다 발동 상태가 분리되고 재시작된다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ExecutionTimePointTrigger_각인스턴스의일회성및Begin재시작()
        {
            var definition = new ExecutionTimePointTriggerDefinition(0.25f);
            var first = definition.CreateTrigger();
            var second = definition.CreateTrigger();
            first.Begin();
            second.Begin();

            Assert.IsTrue(first.TryEvaluate(0f, 0.3f, out var firstTime));
            Assert.AreEqual(0.25f, firstTime, 0.0001f);
            Assert.IsFalse(first.TryEvaluate(0.3f, 0.6f, out _));

            Assert.IsFalse(second.TryEvaluate(0f, 0.2f, out _));
            Assert.IsTrue(second.TryEvaluate(0.2f, 0.3f, out _));
            first.Begin();
            Assert.IsTrue(first.TryEvaluate(0f, 0.3f, out _));
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 첫 일치 Signal만 소비하고 재시작 시 수신 상태를 비운다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ExecutionSignalTrigger_첫매칭Signal만받고재시작시초기화()
        {
            var trigger = new ExecutionSignalTriggerDefinition("Launch", 0.2f).CreateTrigger();
            trigger.Begin();
            trigger.Signal(new ExecutionSignal("Other"), 0.1f);
            Assert.IsFalse(trigger.TryEvaluate(0.1f, 0.2f, out _));

            trigger.Signal(new ExecutionSignal("Launch"), 0.3f);
            trigger.Signal(new ExecutionSignal("Launch"), 0.4f);
            Assert.IsFalse(trigger.TryEvaluate(0.3f, 0.49f, out _));
            Assert.IsTrue(trigger.TryEvaluate(0.49f, 0.5f, out var firedTime));
            Assert.AreEqual(0.5f, firedTime, 0.0001f);
            Assert.IsFalse(trigger.TryEvaluate(0.5f, 0.8f, out _));

            trigger.Begin();
            Assert.IsFalse(trigger.TryEvaluate(0.8f, 0.9f, out _));
            trigger.Signal(new ExecutionSignal("Launch"), 0.9f);
            Assert.IsTrue(trigger.TryEvaluate(0.9f, 1.1f, out _));
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 역방향·NaN 구간은 무시하고 정상 동일 시각에서 발동한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ExecutionSignalTrigger_잘못된시간구간은소비하지않고_Offset0에서즉시발동()
        {
            var trigger = new ExecutionSignalTriggerDefinition("Go", 0f).CreateTrigger();
            trigger.Begin();
            trigger.Signal(new ExecutionSignal("Go"), 0.25f);

            Assert.IsFalse(trigger.TryEvaluate(0.9f, 0.3f, out _));
            Assert.IsFalse(trigger.TryEvaluate(float.NaN, 0.3f, out _));

            Assert.IsTrue(trigger.TryEvaluate(0.25f, 0.25f, out var effectiveTime));
            Assert.AreEqual(0.25f, effectiveTime, 0.0001f);
            Assert.IsFalse(trigger.TryEvaluate(0.25f, 0.3f, out _));
        }

    #endregion

    #region T-2: Trigger 정의·Behavior 조합

        // ------------------------------------------------------------
        /// <summary>
        /// 정의 정규화는 이미 생성된 Trigger의 시각 설정을 변경하지 않는다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ExecutionTrigger_정의공유와생성후정규화시실행별설정유지()
        {
            var windowDefinition = new ExecutionTimeWindowTriggerDefinition(0.2f, 0.8f);
            var original = windowDefinition.CreateTrigger();
            windowDefinition.Normalize(0.4f);
            var normalized = windowDefinition.CreateTrigger();

            Assert.IsTrue(original.TryEvaluate(0.5f, 0.6f, out var originalTime));
            Assert.AreEqual(0.5f, originalTime, 0.0001f);
            Assert.IsFalse(normalized.TryEvaluate(0.5f, 0.6f, out _));

            var pointDefinition = new ExecutionTimePointTriggerDefinition(0.7f);
            var first = pointDefinition.CreateTrigger();
            pointDefinition.Normalize(0.3f);
            var second = pointDefinition.CreateTrigger();

            Assert.IsTrue(first.TryEvaluate(0f, 0.8f, out var firstTime));
            Assert.IsTrue(second.TryEvaluate(0f, 0.8f, out var secondTime));
            Assert.AreEqual(0.7f, firstTime, 0.0001f);
            Assert.AreEqual(0.3f, secondTime, 0.0001f);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 동일 Behavior 형식에서 서로 다른 Trigger가 독립 발동한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ExecutionTrigger_같은Behavior형식에서다른Trigger를조합()
        {
            var trace = new List<string>();
            var times = new List<float>();
            var point = new TriggerTraceBehaviorRuntime
            (
                "Point", new ExecutionTimePointTriggerDefinition(0.2f).CreateTrigger(),
                trace, times
            );
            var signal = new TriggerTraceBehaviorRuntime
            (
                "Signal", new ExecutionSignalTriggerDefinition("Launch", 0.1f).CreateTrigger(),
                trace, times
            );
            var session = CreateTimelineSession
            (
                duration: 1f,
                behavior: new ExecutionBehaviorRuntime[]
                {
                    point,
                    signal
                }
            );

            session.Begin();
            session.Advance(0.25f);
            session.PublishSignal(new ExecutionSignal("Launch"));
            session.Advance(0.1f);
            session.Advance(0.1f);

            CollectionAssert.AreEqual
            (
                new[]
                {
                    "Point",
                    "Signal"
                },
                trace
            );
            Assert.AreEqual(0.2f, times[0], 0.0001f);
            Assert.AreEqual(0.35f, times[1], 0.0001f);
            session.End(ExecutionEndReason.Completed);
        }

    #endregion

    #region T-3: Trigger 수명·오류 복구

        // ------------------------------------------------------------
        /// <summary>
        /// 소유 Behavior의 종료는 Trigger를 한 번만 종료해야 한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ExecutionTrigger_Session종료시소유Behavior가Trigger를종료()
        {
            var trigger = new LifecycleTrigger();
            var trace = new List<string>();
            var times = new List<float>();
            var behavior = new TriggerTraceBehaviorRuntime("Probe", trigger, trace, times);
            var session = CreateTimelineSession
            (
                behavior: new ExecutionBehaviorRuntime[]
                {
                    behavior
                }
            );

            session.Begin();
            Assert.AreEqual(1, trigger.BeginCount);
            Assert.AreEqual(0, trigger.EndCount);

            Assert.IsTrue(session.End(ExecutionEndReason.Cancelled));
            Assert.AreEqual(1, trigger.EndCount);
            Assert.IsFalse(session.End(ExecutionEndReason.Cancelled));
            Assert.AreEqual(1, trigger.EndCount);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Trigger 시작 예외가 발생해도 자신의 종료 처리가 보장된다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ExecutionTrigger_Begin실패시해당Trigger도종료()
        {
            var trigger = new LifecycleTrigger(failOnBegin: true);
            var trace = new List<string>();
            var times = new List<float>();
            var behavior = new TriggerTraceBehaviorRuntime("Probe", trigger, trace, times);
            var session = CreateTimelineSession
            (
                behavior: new ExecutionBehaviorRuntime[]
                {
                    behavior
                }
            );

            Assert.Throws<InvalidOperationException>(() => session.Begin());
            Assert.IsTrue(session.HasEnded);
            Assert.AreEqual(ExecutionEndReason.Cancelled, session.EndReason);
            Assert.AreEqual(1, trigger.BeginCount);
            Assert.AreEqual(1, trigger.EndCount);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 일부 Trigger 종료 실패가 다른 Trigger 정리를 막지 않는다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ExecutionTrigger_한Trigger의종료오류에도다른Trigger를종료()
        {
            var first = new LifecycleTrigger(failOnEnd: true);
            var second = new LifecycleTrigger();
            var trace = new List<string>();
            var times = new List<float>();
            var session = CreateTimelineSession
            (
                behavior: new ExecutionBehaviorRuntime[]
                {
                    new TriggerTraceBehaviorRuntime("First", first, trace, times),
                    new TriggerTraceBehaviorRuntime("Second", second, trace, times)
                }
            );

            session.Begin();

            Assert.Throws<InvalidOperationException>
            (
                () => session.End(ExecutionEndReason.Cancelled)
            );
            Assert.IsTrue(session.HasEnded);
            Assert.AreEqual(1, first.EndCount);
            Assert.AreEqual(1, second.EndCount);
        }

    #endregion

    #region E-1: Session 시작 실패·롤백

        // ------------------------------------------------------------
        /// <summary>
        /// Behavior 시작 실패 시 시도한 대상까지 역순 정리된다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ExecutionSession_BehaviorBegin실패시_시작된Behavior를역순정리()
        {
            var trace = new List<string>();
            var first = new TraceBehaviorRuntime("First", trace);
            var failing = new TraceBehaviorRuntime("Failing", trace, failOnBegin: true);
            var session = CreateTimelineSession
            (
                behavior: new ExecutionBehaviorRuntime[]
                {
                    first,
                    failing
                }
            );

            Assert.Throws<InvalidOperationException>(() => session.Begin());

            Assert.IsTrue(session.HasEnded);
            Assert.AreEqual(ExecutionEndReason.Cancelled, session.EndReason);
            Assert.AreEqual(1, first.EndCount);
            Assert.AreEqual(1, failing.EndCount);
            CollectionAssert.AreEqual
            (
                new[]
                {
                    "First.Begin",
                    "Failing.Begin",
                    "Failing.End:Cancelled",
                    "First.End:Cancelled",
                },
                trace
            );
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 시작 중 재진입 종료는 거부되며 Session은 실패 종료된다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ExecutionSession_Begin중재진입End를막고정리()
        {
            var probe = new TraceBehaviorRuntime
            {
                OnBegin = session => session.End(ExecutionEndReason.Cancelled)
            };
            var session = CreateTimelineSession
            (
                behavior: new ExecutionBehaviorRuntime[]
                {
                    probe
                }
            );

            Assert.Throws<InvalidOperationException>(() => session.Begin());
            Assert.IsTrue(session.HasEnded);
            Assert.AreEqual(ExecutionEndReason.Cancelled, session.EndReason);
            Assert.AreEqual(1, probe.EndCount);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 시작 알림 예외에도 종료 통지와 자원 정리가 유지된다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ExecutionSession_OnBegun예외가Cancelled정리를보장()
        {
            var probe = new TraceBehaviorRuntime();
            var session = CreateTimelineSession
            (
                behavior: new ExecutionBehaviorRuntime[]
                {
                    probe
                }
            );
            var endedCount = 0;
            session.OnEnded += (_, reason) =>
            {
                Assert.AreEqual(ExecutionEndReason.Cancelled, reason);
                endedCount++;
            };
            session.OnBegun += _ => throw new InvalidOperationException("start observer failure");

            Assert.Throws<InvalidOperationException>(() => session.Begin());
            Assert.AreEqual(1, endedCount);
            Assert.IsFalse(session.IsActive);
            Assert.IsTrue(session.HasEnded);
            Assert.AreEqual(ExecutionEndReason.Cancelled, session.EndReason);
            Assert.AreEqual(1, probe.EndCount);
        }

    #endregion

    #region E-2: Session 진행·Observer 실패

        // ------------------------------------------------------------
        /// <summary>
        /// 진행 도중 재진입을 차단하며 실패 후 시간과 종료 상태를 보존한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ExecutionSession_Advance재진입은실행시간을변경하지않고실패종료()
        {
            var attempted = false;
            var probe = new TraceBehaviorRuntime
            {
                OnAdvance = (running, _) =>
                {
                    if (attempted)
                    {
                        return;
                    }

                    attempted = true;
                    running.Advance(0.3f);
                }
            };
            var session = CreateTimelineSession
            (
                behavior: new ExecutionBehaviorRuntime[]
                {
                    probe
                }
            );

            session.Begin();
            Assert.Throws<InvalidOperationException>(() => session.Advance(0.2f));

            Assert.IsTrue(attempted);
            Assert.AreEqual(0.2f, session.Time, 0.0001f);
            Assert.AreEqual(0f, session.PreviousTime, 0.0001f);
            Assert.AreEqual(0.2f, session.AppliedDelta, 0.0001f);
            Assert.IsTrue(session.HasEnded);
            Assert.AreEqual(ExecutionEndReason.Cancelled, session.EndReason);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Commit 알림 예외가 발생해도 실패 종료가 완료된다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ExecutionSession_OnCommitted예외가Cancelled정리를보장()
        {
            var probe = new TraceBehaviorRuntime();
            var session = CreateTimelineSession
            (
                behavior: new ExecutionBehaviorRuntime[]
                {
                    probe
                }
            );
            session.OnCommitted += _ => throw new InvalidOperationException("commit observer failure");

            session.Begin();
            Assert.Throws<InvalidOperationException>(() => session.TryCommit());
            Assert.IsFalse(session.IsActive);
            Assert.IsTrue(session.HasEnded);
            Assert.AreEqual(ExecutionEndReason.Cancelled, session.EndReason);
            Assert.AreEqual(1, probe.EndCount);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Behavior 진행 예외에도 Session과 Behavior가 취소 종료된다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ExecutionSession_Advance예외가Cancelled정리를보장()
        {
            var probe = new TraceBehaviorRuntime
            {
                OnAdvance = (_, _) => throw new InvalidOperationException("Advance probe failure")
            };
            var session = CreateTimelineSession
            (
                behavior: new ExecutionBehaviorRuntime[]
                {
                    probe
                }
            );

            session.Begin();
            Assert.Throws<InvalidOperationException>(() => session.Advance(0.1f));
            Assert.IsTrue(session.HasEnded);
            Assert.AreEqual(ExecutionEndReason.Cancelled, session.EndReason);
            Assert.AreEqual(1, probe.EndCount);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 종료 Observer 일부가 실패해도 나머지 Observer를 호출한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ExecutionSession_OnEnded실패에도모든Observer에게통지()
        {
            var session = CreateTimelineSession();
            var observed = 0;
            session.OnEnded += (_, _) => throw new InvalidOperationException("end observer failure");
            session.OnEnded += (_, _) => observed++;

            session.Begin();
            Assert.Throws<InvalidOperationException>
            (
                () => session.End(ExecutionEndReason.Cancelled)
            );
            Assert.AreEqual(1, observed);
            Assert.IsTrue(session.HasEnded);
            Assert.AreEqual(ExecutionEndReason.Cancelled, session.EndReason);
            Assert.IsFalse(session.End(ExecutionEndReason.Cancelled));
        }

    #endregion

    #region F-1: Ability 선택·Host 승인·Execution 연결

        // ============================================================
        /// <summary>
        /// 선택 승인 슬롯과 Session 시작을 분리해 관찰하는 테스트 Host.
        /// </summary>
        // ============================================================
        private sealed class FakeExecutionHost
        {
            public bool IsOccupied => occupied;
            private bool occupied;

            public ExecutionSession Session { get; private set; }

            public FakeExecutionHost(bool occupied = false)
            {
                this.occupied = occupied;
            }

            public bool TryAcquire()
            {
                if (occupied)
                {
                    return false;
                }

                occupied = true;
                return true;
            }

            public ExecutionSession Begin
            (
                ExecutionDefinition selected,
                in ExecutionContext context,
                ulong id,
                Action<ExecutionSession> configure = null
            )
            {
                if (!occupied)
                {
                    throw new InvalidOperationException("Host 실행 슬롯이 승인되지 않았습니다.");
                }

                var session = selected.CreateSession(id, context);
                configure?.Invoke(session);
                try
                {
                    session.Begin();
                    Session = session;
                    return session;
                }
                catch
                {
                    occupied = false;
                    throw;
                }
            }

            public void Release()
            {
                if (Session?.IsActive == true)
                {
                    Session.End(ExecutionEndReason.Cancelled);
                }

                Session = null;
                occupied = false;
            }
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// 선택된 Definition과 Cost Policy를 한 요청에 묶고 선택 횟수를 관찰한다.
        /// </summary>
        // ----------------------------------------------------------------------
        private static AbilityUseSelection<ExecutionDefinition, int, ExecutionDefinition> SelectExecution
        (
            AbilityInstance<ExecutionDefinition> ability,
            ExecutionDefinition execution,
            object owner,
            Action onSelect = null,
            Action transition = null
        )
        {
            var policy = new AbilityActivationPolicy<ExecutionDefinition, int>
            (
                cost: new IAbilityActivationCost<ExecutionDefinition, int>[]
                {
                    new AbilityChargeCost<ExecutionDefinition, int>(),
                }
            );
            var selector = new AbilityUseSelector<ExecutionDefinition, int, ExecutionDefinition>
            (
                new Func<AbilityActivationContext<ExecutionDefinition, int>,
                    AbilityUseChoice<ExecutionDefinition, int, ExecutionDefinition>>[]
                {
                    request =>
                    {
                        onSelect?.Invoke();
                        return new AbilityUseChoice<ExecutionDefinition, int, ExecutionDefinition>
                        (
                            execution,
                            policy,
                            () => ability.State.Charge.Current > 0,
                            transition
                        );
                    },
                }
            );
            var context = new AbilityActivationContext<ExecutionDefinition, int>
            (
                owner,
                owner,
                new AbilityBinding<ExecutionDefinition>(new AbilityEntryKey("Q"), ability),
                0
            );

            Assert.IsTrue(selector.TrySelect(context, out var selection, out _));
            return selection;
        }

        [Test]
        public void TEST_ExecutionHost_선택후Host시작거부는비용을소비하지않음()
        {
            var owner = new object();
            var execution = new ExecutionDefinition(new FixedTimelineExecutionFlowDefinition(1f));
            var ability = new AbilityInstance<ExecutionDefinition>
            (
                execution,
                new AbilityState(new AbilityKey("Skill"))
            );
            ability.State.Control.ConfigureCharge(2, 1f);
            var selectionCount = 0;
            var selection = SelectExecution(ability, execution, owner, () => selectionCount++);

            // Host 승인 이전에는 비용 지불을 시작하지 않는다.
            var host = new FakeExecutionHost(occupied: true);
            Assert.IsFalse(host.TryAcquire());
            Assert.IsTrue(host.IsOccupied);
            Assert.IsNull(host.Session);
            Assert.AreEqual(2, ability.State.Charge.Current);
            Assert.AreEqual(1, selectionCount);
            Assert.AreSame(execution, selection.Execution);
        }

        [Test]
        public void TEST_ExecutionHost_비용지불후SessionBegin실패는자동환급하지않음()
        {
            var owner = new object();
            var execution = new ExecutionDefinition(new FixedTimelineExecutionFlowDefinition(1f));
            var ability = new AbilityInstance<ExecutionDefinition>
            (
                execution,
                new AbilityState(new AbilityKey("Skill"))
            );
            ability.State.Control.ConfigureCharge(2, 1f);
            var selectionCount = 0;
            var selection = SelectExecution(ability, execution, owner, () => selectionCount++);

            // 슬롯을 먼저 확보한 뒤 비용을 지불한다.
            var host = new FakeExecutionHost();
            Assert.IsTrue(host.TryAcquire());
            Assert.IsTrue(selection.TryActivate(out var activation));
            Assert.IsTrue(activation.Succeeded);
            Assert.AreEqual(1, ability.State.Charge.Current);

            // Begin 예외에는 Host를 정리하지만 소비한 비용은 환급하지 않는다.
            ExecutionSession failedSession = null;
            Assert.Throws<InvalidOperationException>
            (
                () => host.Begin
                (
                    selection.Execution,
                    new ExecutionContext(owner, owner),
                    101UL,
                    session =>
                    {
                        failedSession = session;
                        session.OnBegun += _ => throw new InvalidOperationException("Fake Host Begin failed");
                    }
                )
            );

            Assert.IsTrue(failedSession.HasEnded);
            Assert.AreEqual(ExecutionEndReason.Cancelled, failedSession.EndReason);
            Assert.IsFalse(host.IsOccupied);
            Assert.IsNull(host.Session);
            Assert.AreEqual(1, ability.State.Charge.Current);
            Assert.IsFalse(selection.TryActivate(out var repeated));
            Assert.AreEqual(AbilityActivationFailure.InvalidState, repeated.Failure);
            Assert.AreEqual(1, selectionCount);
        }

        [Test]
        public void TEST_ExecutionHost_선택한Execution을재선택없이시작하고상태전이는한번만적용()
        {
            var owner = new object();
            var execution = new ExecutionDefinition(new FixedTimelineExecutionFlowDefinition(1f));
            var ability = new AbilityInstance<ExecutionDefinition>
            (
                execution,
                new AbilityState(new AbilityKey("Skill"))
            );
            ability.State.Control.ConfigureCharge(2, 1f);
            var selectionCount = 0;
            var transitionCount = 0;
            var selection = SelectExecution
            (
                ability,
                execution,
                owner,
                () => selectionCount++,
                () => transitionCount++
            );

            var host = new FakeExecutionHost();
            Assert.IsTrue(host.TryAcquire());
            Assert.IsTrue(selection.TryActivate(out var activation));
            Assert.IsTrue(activation.Succeeded);
            Assert.AreEqual(1, transitionCount);
            Assert.AreSame(execution, selection.Execution);
            var session = host.Begin(selection.Execution, new ExecutionContext(owner, owner), 102UL);
            Assert.IsTrue(host.IsOccupied);
            Assert.AreSame(session, host.Session);
            Assert.IsTrue(session.IsActive);
            Assert.IsFalse(selection.TryActivate(out var repeated));
            Assert.AreEqual(AbilityActivationFailure.InvalidState, repeated.Failure);
            Assert.AreEqual(1, transitionCount);
            Assert.AreEqual(1, selectionCount);
            Assert.AreEqual(1, ability.State.Charge.Current);

            session.Advance(1f);
            Assert.IsTrue(session.TryComplete());
            Assert.AreEqual(ExecutionEndReason.Completed, session.EndReason);
            host.Release();
            Assert.IsFalse(host.IsOccupied);
            Assert.IsNull(host.Session);
            Assert.AreEqual(1, ability.State.Charge.Current);
        }

    #endregion

    }
}