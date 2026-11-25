/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : TEST_ExecutionTimeIntervalTrigger.cs
수정일 : 2026-11-24

# 설명
주기 Trigger의 발동 시각, 시간창 경계, 횟수 한도, 큰 시간 간격의 반복 호출과 재시작, Spec 정규화를 검증한다.
시간은 이진수로 정확히 표현되는 값(0.25 단위)만 써서 부동소수점 오차 없이 비교한다.

# 테스트 구성
 I: 주기 Trigger 발동·경계·정규화
========================================================================= BLOCK_HEADER_END */

using System;
using System.Collections;
using System.Collections.Generic;

using NUnit;
using NUnit.Framework;

namespace inonego.Xeri.Game.TEST._Execution
{
    using Xeri;
    using Xeri.Game;

    // ============================================================
    /// <summary>
    /// 주기 Trigger의 판정 계약을 검증하는 테스트 묶음.
    /// </summary>
    // ============================================================
    public sealed class TEST_ExecutionTimeIntervalTrigger
    {

    #region I-1: 발동 시각

        // ------------------------------------------------------------
        /// <summary>
        /// 기대하는 발동 시각 목록을 만든다.
        /// </summary>
        // ------------------------------------------------------------
        private static float[] Times(params float[] times) => times;

        // ----------------------------------------------------------------------
        /// <summary>
        /// 한 구간의 모든 발동을 false가 나올 때까지 반복 호출해 시각 목록으로 모은다.
        /// </summary>
        // ----------------------------------------------------------------------
        private static List<float> Collect(ExecutionTrigger trigger, float previousTime, float currentTime)
        {
            var times = new List<float>();
            while (trigger.TryEvaluate(previousTime, currentTime, out var effectiveTime))
            {
                times.Add(effectiveTime);
                Assert.Less(times.Count, 1000, "반복 호출이 끝나지 않습니다.");
            }

            return times;
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> 시간창 시작 시각부터 간격마다 한 번씩, 지나는 구간에서만 발동한다.
        /// <br/> 시간창 끝 시각은 제외한다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_ExecutionTimeIntervalTrigger_시작시각부터간격마다발동하고끝시각은제외()
        {
            var trigger = new ExecutionTimeIntervalTriggerSpec(0f, 1f, 0.25f).CreateTrigger();
            trigger.Begin();

            Assert.AreEqual(Times(0f), Collect(trigger, 0f, 0.125f));
            Assert.AreEqual(Times(), Collect(trigger, 0.125f, 0.1875f));
            Assert.AreEqual(Times(0.25f), Collect(trigger, 0.1875f, 0.3125f));
            Assert.AreEqual(Times(0.5f, 0.75f), Collect(trigger, 0.3125f, 0.875f));

            // 끝 시각(1)의 주기는 시간창 밖이다.
            Assert.AreEqual(Times(), Collect(trigger, 0.875f, 2f));
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> 한 구간에 주기가 여럿이면 하나씩 오름차순으로 반환하고, 구간 시작 시각의
        /// <br/> 주기는 이전 구간 소유라 이어지는 구간에서 다시 발동하지 않는다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_ExecutionTimeIntervalTrigger_큰시간간격은반복호출로모든주기를오름차순반환()
        {
            var trigger = new ExecutionTimeIntervalTriggerSpec(0.25f, 10f, 0.25f).CreateTrigger();
            trigger.Begin();

            Assert.AreEqual(Times(0.25f, 0.5f, 0.75f, 1f), Collect(trigger, 0f, 1f));

            // 1초 지점은 앞 구간이 소유하므로 뒤 구간은 그 다음 주기부터다.
            Assert.AreEqual(Times(1.25f), Collect(trigger, 1f, 1.25f));
            Assert.AreEqual(Times(), Collect(trigger, 1.25f, 1.25f));
        }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// 시간이 흐르지 않은 구간과 거꾸로 흐른 구간에서는 발동하지 않고 주기를 소비하지 않는다.
        /// </summary>
        // --------------------------------------------------------------------------------
        [Test]
        public void TEST_ExecutionTimeIntervalTrigger_시간이흐르지않은구간은발동하지않음()
        {
            var trigger = new ExecutionTimeIntervalTriggerSpec(0f, 1f, 0.25f).CreateTrigger();
            trigger.Begin();

            Assert.IsFalse(trigger.TryEvaluate(0f, 0f, out _));
            Assert.IsFalse(trigger.TryEvaluate(0.5f, 0.25f, out _));
            Assert.AreEqual(Times(0f, 0.25f), Collect(trigger, 0f, 0.25f));
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// 호출자가 평가하지 못한 앞 구간의 주기는 발동 없이 건너뛰고 지금 구간부터 반환한다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_ExecutionTimeIntervalTrigger_평가하지못한앞구간의주기는건너뜀()
        {
            var trigger = new ExecutionTimeIntervalTriggerSpec(0f, 10f, 0.25f).CreateTrigger();
            trigger.Begin();

            Assert.AreEqual(Times(1f), Collect(trigger, 0.875f, 1.125f));
        }

    #endregion

    #region I-2: 횟수 한도와 수명

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> 횟수 한도에 닿으면 시간창이 남아 있어도 더는 발동하지 않는다.
        /// <br/> 한도가 0이면 시간창 끝까지 제한이 없다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ExecutionTimeIntervalTrigger_횟수한도에닿으면더는발동하지않음()
        {
            var limited = new ExecutionTimeIntervalTriggerSpec(0f, 10f, 0.25f, maxCount: 3).CreateTrigger();
            limited.Begin();
            Assert.AreEqual(Times(0f, 0.25f, 0.5f), Collect(limited, 0f, 5f));

            var unlimited = new ExecutionTimeIntervalTriggerSpec(0f, 1f, 0.25f, maxCount: 0).CreateTrigger();
            unlimited.Begin();
            Assert.AreEqual(4, Collect(unlimited, 0f, 5f).Count);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> Begin은 발동 번호를 처음으로 되돌리고,
        /// <br/> 같은 Spec에서 만든 Trigger는 서로 상태를 공유하지 않는다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ExecutionTimeIntervalTrigger_Begin은처음으로되돌리고Trigger끼리상태를공유하지않음()
        {
            var spec = new ExecutionTimeIntervalTriggerSpec(0f, 1f, 0.5f);
            var first = spec.CreateTrigger();
            var second = spec.CreateTrigger();
            first.Begin();
            second.Begin();

            Assert.AreEqual(Times(0f, 0.5f), Collect(first, 0f, 1f));
            Assert.AreEqual(Times(0f, 0.5f), Collect(second, 0f, 1f));

            first.Begin();
            Assert.AreEqual(Times(0f, 0.5f), Collect(first, 0f, 1f));
        }

    #endregion

    #region I-3: Spec 정규화

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/> 너무 짧거나 유효하지 않은 간격은 최소 간격으로, 음수 횟수 한도는 0으로 정규화하고
        /// <br/> 시간창은 실행의 최대 시간 안으로 제한한다.
        /// </summary>
        // --------------------------------------------------------------------------------
        [Test]
        public void TEST_ExecutionTimeIntervalTrigger_Spec은간격과횟수와시간창을정규화()
        {
            var spec = new ExecutionTimeIntervalTriggerSpec(0f, 5f, 0f, maxCount: -3);
            Assert.AreEqual(ExecutionTimeIntervalTriggerSpec.MinimumInterval, spec.Interval);
            Assert.AreEqual(0, spec.MaxCount);

            var nan = new ExecutionTimeIntervalTriggerSpec(0f, 5f, float.NaN);
            Assert.AreEqual(ExecutionTimeIntervalTriggerSpec.MinimumInterval, nan.Interval);

            var window = new ExecutionTimeIntervalTriggerSpec(2f, 8f, 1f);
            window.Normalize(4f);
            Assert.AreEqual(2f, window.BeginTime);
            Assert.AreEqual(4f, window.EndTime);

            var inverted = new ExecutionTimeIntervalTriggerSpec(3f, 1f, 1f);
            Assert.AreEqual(3f, inverted.BeginTime);
            Assert.AreEqual(3f, inverted.EndTime);
            var empty = inverted.CreateTrigger();
            empty.Begin();
            Assert.AreEqual(0, Collect(empty, 0f, 10f).Count);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Spec이 null이면 Trigger를 만들지 않는다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ExecutionTimeIntervalTrigger_Spec이없으면ArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => new ExecutionTimeIntervalTrigger(null));
        }

    #endregion

    }
}
