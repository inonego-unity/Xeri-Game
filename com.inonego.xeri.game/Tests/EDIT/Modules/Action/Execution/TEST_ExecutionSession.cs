/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : TEST_ExecutionSession.cs
수정일 : 2026-11-19

# 설명
ExecutionSession의 구성과 Runtime 소유권, 시간 진행과 완료, 수명과 Commit, Parent·Child 연결과 Context Mode 계약을 검증한다.

# 테스트 구성
 S: Session 구성·Runtime 소유권·시간·수명·Child·Context Mode
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

    // ============================================================
    /// <summary>
    /// ExecutionSession의 구성·시간·수명 계약을 검증하는 테스트 묶음.
    /// </summary>
    // ============================================================
    public sealed class TEST_ExecutionSession : ExecutionTestBase
    {

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

            // 목록 뷰는 쓰기 가능한 IList가 아니라서 캐스팅으로도 내용을 바꿀 수 없다.
            Assert.IsFalse(((object)session.Behaviors) is IList<ExecutionBehaviorRuntime>);

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
            var flow = new FixedTimelineExecutionFlowSpec(0.5f).CreateRuntime();
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
            var unusedFlow = new FixedTimelineExecutionFlowSpec(1f).CreateRuntime();
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
        /// Spec이 바뀌어도 생성된 Flow Runtime의 동작 설정이 유지된다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ExecutionFlow_Runtime은생성당시설정을보존()
        {
            var timeline = new FixedTimelineExecutionFlowSpec(0.5f);
            var timelineSession = new ExecutionSession
            (
                56UL, default, timeline.CreateRuntime(),
                maximumTime: timeline.MaximumDuration
            );

            timelineSession.Begin();
            var durationField = typeof(FixedTimelineExecutionFlowSpec)
                .GetField("duration", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(durationField);
            durationField.SetValue(timeline, 0.9f);
            timelineSession.Advance(0.5f);

            Assert.AreEqual(0.9f, timeline.Duration, 0.0001f);
            Assert.IsTrue(timelineSession.Flow.IsComplete);

            var hold = new HoldReleaseExecutionFlowSpec("Release", 0.2f, 0.5f, 0.25f);
            var holdSession = new ExecutionSession
            (
                57UL, default, hold.CreateRuntime(),
                maximumTime: hold.MaximumDuration
            );

            holdSession.Begin();
            var maxHoldField = typeof(HoldReleaseExecutionFlowSpec)
                .GetField("maxHoldTime", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(maxHoldField);
            maxHoldField.SetValue(hold, 0.9f);
            holdSession.Advance(0.75f);

            Assert.AreEqual(0.9f, hold.MaxHoldTime, 0.0001f);
            Assert.AreEqual(0.5f, holdSession.Flow.ReleaseTime, 0.0001f);
            Assert.IsTrue(holdSession.Flow.IsComplete);
        }

        // ============================================================
        /// <summary>
        /// 지정한 Runtime을 그대로 돌려주는 Behavior Spec이다.
        /// </summary>
        // ============================================================
        private sealed class FixedBehaviorSpec : ExecutionBehaviorSpec
        {
            private readonly ExecutionBehaviorRuntime runtime;

            internal FixedBehaviorSpec(ExecutionBehaviorRuntime runtime)
            {
                this.runtime = runtime;
            }

            // ------------------------------------------------------------
            /// <summary>
            /// 생성 때 받은 Runtime을 그대로 반환하며 null일 수도 있다.
            /// </summary>
            // ------------------------------------------------------------
            public override ExecutionBehaviorRuntime CreateRuntime() => runtime;
        }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/> Spec에서 Session을 만들 때 타입을 고르지 않은 빈 Behavior 항목은 건너뛰고,
        /// <br/> Behavior Spec이 Runtime을 만들지 못하면 Flow와 같이 예외로 거부하는지 검증한다.
        /// </summary>
        // --------------------------------------------------------------------------------
        [Test]
        public void TEST_ExecutionSession_Spec의빈Behavior항목은건너뛰고Runtime생성실패는거부()
        {
            var flow = new FixedTimelineExecutionFlowSpec(1f);
            var behaviors = new ExecutionBehaviorSpec[]
            {
                null,
                new FixedBehaviorSpec(new TraceBehaviorRuntime()),
            };
            var session = new ExecutionSpec(flow, behaviors).CreateSession(1, new ExecutionContext(1, 1));
            Assert.AreEqual(1, session.Behaviors.Count);

            var broken = new ExecutionBehaviorSpec[]
            {
                new FixedBehaviorSpec(null),
            };
            Assert.Throws<InvalidOperationException>
            (
                () => new ExecutionSpec(flow, broken).CreateSession(2, new ExecutionContext(1, 1))
            );
        }

    #endregion

    #region S-1a: ExecutionContext Mode

        // ----------------------------------------------------------------------
        /// <summary>
        /// Mode를 지정하지 않은 Context와 기본 Context가 권위 실행인지 검증한다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_ExecutionContext_Mode기본값은Authoritative()
        {
            var owner = new object();

            Assert.AreEqual(ExecutionMode.Authoritative, new ExecutionContext(owner, owner).Mode);
            Assert.AreEqual(ExecutionMode.Authoritative, default(ExecutionContext).Mode);
        }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// 지정한 Mode가 Owner·Instigator와 함께 Session Context로 전달되는지 검증한다.
        /// </summary>
        // --------------------------------------------------------------------------------
        [Test]
        public void TEST_ExecutionContext_Mode는Session으로전달()
        {
            var owner = new object();
            var instigator = new object();

            foreach (ExecutionMode mode in Enum.GetValues(typeof(ExecutionMode)))
            {
                var session = CreateTimelineSession
                (
                    context: new ExecutionContext(owner, instigator, mode)
                );

                Assert.AreEqual(mode, session.Context.Mode);
                Assert.AreSame(owner, session.Context.Owner);
                Assert.AreSame(instigator, session.Context.Instigator);
            }
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
            var flow = new FixedTimelineExecutionFlowSpec(2f);
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

            var window = new ExecutionTimeWindowTriggerSpec(0.5f, 0.9f).CreateTrigger();
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

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> Child 종료 콜백이 다른 Child를 분리해도 Parent 종료는 중단되지 않고
        /// <br/> 분리된 Child는 종료하지 않는다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_ExecutionSession_Child종료콜백이다른Child를분리해도Parent종료를완료()
        {
            var parent = CreateTimelineSession(id: 50UL);
            var first = CreateTimelineSession(id: 51UL);
            var second = CreateTimelineSession(id: 52UL);
            parent.Begin();
            first.Begin();
            second.Begin();
            parent.AttachChild(first);
            parent.AttachChild(second);
            var parentEnded = 0;
            parent.OnEnded += (_, _) => parentEnded++;
            second.OnEnded += (_, _) => parent.DetachChild(first);

            Assert.DoesNotThrow(() => parent.End(ExecutionEndReason.Cancelled));

            Assert.IsTrue(parent.HasEnded);
            Assert.AreEqual(1, parentEnded);
            Assert.IsTrue(second.HasEnded);
            Assert.IsTrue(first.IsActive);

            first.End(ExecutionEndReason.Cancelled);
        }

    #endregion

    }
}
