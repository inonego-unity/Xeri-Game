/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : TEST_AbilityExecutionDrive.cs
수정일 : 2026-11-19

# 설명
Host가 소유하는 지속 비용 소모와 부족 시 실행 종료 경계를 검증한다.

# 테스트 구성
 S: 지속 비용 소모와 실행 종료
========================================================================= BLOCK_HEADER_END */

using System;

using NUnit;
using NUnit.Framework;

namespace inonego.Xeri.Game.TEST._Ability
{
    using Xeri;
    using Xeri.Game;
    using Xeri.Game.TEST;
    using Xeri.Game.TEST._Ability;
    using Xeri.Game.TEST._Ability.Fixture;

    // ============================================================
    /// <summary>
    /// 지속 사용의 비용 소모와 종료를 실제 Session으로 검증한다.
    /// </summary>
    // ============================================================
    public sealed class TEST_AbilityExecutionDrive
    {

    #region 검증용 Host

        private sealed class SustainHost
        {
            private readonly ResourceFixture resource;
            private readonly object owner;
            internal ExecutionSession Session { get; }
            internal int EndCount { get; private set; }

            internal SustainHost(ResourceFixture resource, object owner)
            {
                this.resource = resource;
                this.owner = owner;
                Session = CreateSession(1);
                Session.OnEnded += (_, _) => EndCount++;
            }

            internal void Tick(bool endRequested = false)
            {
                if (!Session.IsActive) return;

                var paid = resource.TryDebit(owner, 1);
                if (!paid || endRequested)
                {
                    Session.End(ExecutionEndReason.Interrupted);
                }
            }
        }

        private static ExecutionSession CreateSession(ulong id)
        {
            var flow = new FixedTimelineExecutionFlowSpec(10f);
            return new ExecutionSession(id, default, flow.CreateRuntime(), maximumTime: flow.MaximumDuration);
        }

    #endregion

    #region S-1: 지속 비용 소모와 실행 종료

        // ------------------------------------------------------------
        /// <summary>
        /// 자원 부족은 지속 실행을 중단하고 후속 실행을 허용한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_AbilityExecutionDrive_지속비용부족후다른실행()
        {
            var resource = new ResourceFixture();
            var owner = new object();
            resource.Add(owner, 2);
            var host = new SustainHost(resource, owner);
            host.Session.Begin();
            host.Tick();
            host.Tick();
            Assert.AreEqual(0, resource.Get(owner));
            Assert.IsTrue(host.Session.IsActive);
            host.Tick();
            host.Tick();
            Assert.AreEqual(0, resource.Get(owner));
            Assert.IsTrue(host.Session.HasEnded);
            Assert.AreEqual(ExecutionEndReason.Interrupted, host.Session.EndReason);
            Assert.AreEqual(1, host.EndCount);

            var next = CreateSession(2);
            next.Begin();
            Assert.IsTrue(next.IsActive);
            Assert.IsTrue(next.End(ExecutionEndReason.Completed));
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 소비 실패와 종료 요청이 겹쳐도 종료는 한 번만 확정된다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_AbilityExecutionDrive_소비실패와종료가겹치고종료Observer가실패()
        {
            var resource = new ResourceFixture();
            var owner = new object();
            resource.Add(owner, 0);
            var host = new SustainHost(resource, owner);
            var failingCalls = 0;
            host.Session.OnEnded += (_, _) =>
            {
                failingCalls++;
                throw new InvalidOperationException("end observer");
            };
            host.Session.Begin();
            Assert.Throws<InvalidOperationException>(() => host.Tick(endRequested: true));
            Assert.IsTrue(host.Session.HasEnded);
            Assert.AreEqual(ExecutionEndReason.Interrupted, host.Session.EndReason);
            Assert.AreEqual(1, host.EndCount);
            Assert.AreEqual(1, failingCalls);
            Assert.AreEqual(0, resource.Get(owner));
            host.Tick(endRequested: true);
            Assert.AreEqual(1, host.EndCount);
            Assert.AreEqual(1, failingCalls);
            Assert.AreEqual(0, resource.Get(owner));
        }

    #endregion

    }
}
