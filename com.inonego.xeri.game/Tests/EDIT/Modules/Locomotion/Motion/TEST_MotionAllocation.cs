/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : TEST_MotionAllocation.cs
수정일 : 2026-11-22

# 설명
Motion Model의 반복 경로(정상 상태 Step, 같은 세대 갱신, 같은 구성 Capture·Restore)가 GC 할당을 만들지 않는지 검증한다.
Unity Test Runner (Edit Mode) 에서 실행한다.

# 특이사항, 제약사항
Unity Test Framework의 AllocatingGCMemory 제약을 쓰므로 Unity 밖에서는 실행할 수 없다.
항목 시작(Modifier 생성)과 구성이 바뀌는 Restore는 할당이 있으므로 측정 전에 끝낸다.
========================================================================= BLOCK_HEADER_END */

using System;

using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

using NUnit;
using NUnit.Framework;

using inonego;
using inonego.Xeri;
using inonego.Xeri.Serializable;
using inonego.Xeri.Game;
using inonego.Xeri.Game.Motion;

namespace inonego.Xeri.Game.TEST.EDIT._Motion
{
    // ============================================================
    /// <summary>
    /// Motion Model 반복 경로의 GC 할당 테스트.
    /// </summary>
    // ============================================================
    public sealed class TEST_MotionAllocation
    {

    #region 헬퍼

        private const double DELTA = 0.02d;

        private static readonly MotionIntent[] walk =
        {
            new(MotionChannel.Planar, new Vector3(2f, 0f, 0f))
        };

        private static readonly MotionModifierCommand[] begin =
        {
            MotionModifierCommand.Apply
            (
                new MotionSourceKey("Knockback", Revision.FromUInt64(1)), 20, NumericFOperation.ADD,
                MotionChannelValues.Linear(MotionChannel.Planar | MotionChannel.Elevation, new Vector3(0f, 1f, 4f))
            ),
            MotionModifierCommand.Apply
            (
                new MotionSourceKey("Slow", Revision.FromUInt64(1)), 30, NumericFOperation.MUL, MotionChannelValues.Uniform(MotionChannel.Planar, 0.5f)
            ),
            MotionModifierCommand.Apply
            (
                new MotionSourceKey("Turn", Revision.FromUInt64(1)), 5, NumericFOperation.SET,
                new MotionChannelValues(MotionChannel.Rotation, rotation: new Vector3(0f, 1f, 0f))
            )
        };

        // 사용처가 매 Tick 감쇠한 값을 같은 세대로 넣는 경우.
        private static readonly MotionModifierCommand[] refresh =
        {
            MotionModifierCommand.Apply
            (
                new MotionSourceKey("Knockback", Revision.FromUInt64(1)), 20, NumericFOperation.ADD,
                MotionChannelValues.Linear(MotionChannel.Planar | MotionChannel.Elevation, new Vector3(0f, 1f, 5f))
            )
        };

        // 같은 세대에서 연산만 바꾸는 경우.
        private static readonly MotionModifierCommand[] flip =
        {
            MotionModifierCommand.Apply
            (
                new MotionSourceKey("Knockback", Revision.FromUInt64(1)), 20, NumericFOperation.SUB,
                MotionChannelValues.Linear(MotionChannel.Planar | MotionChannel.Elevation, new Vector3(0f, 1f, 5f))
            )
        };

        private static MotionModel CreateWarmModel()
        {
            var model = new MotionModel(new MotionActorKey(1, 1, Revision.FromUInt64(1)), Vector3.up);

            model.StepLocal(DELTA, new MotionStepInput(walk, begin));
            Step(model);
            StepRefresh(model);
            StepFlip(model);
            StepRefresh(model);

            return model;
        }

        private static void Step(MotionModel model)
        {
            model.StepLocal(DELTA, new MotionStepInput(walk));
        }

        private static void StepRefresh(MotionModel model)
        {
            model.StepLocal(DELTA, new MotionStepInput(walk, refresh));
        }

        private static void StepFlip(MotionModel model)
        {
            model.StepLocal(DELTA, new MotionStepInput(walk, flip));
        }

        private static void StepUp(MotionModel model, Vector3 up)
        {
            model.StepLocal(DELTA, new MotionStepInput(walk, refresh, up));
        }

    #endregion

    #region 할당

        // ------------------------------------------------------------
        /// <summary>
        /// 항목이 유지되는 동안의 Step은 할당하지 않는다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_MotionAllocation_정상상태Step_할당없음()
        {
            var model = CreateWarmModel();

            Assert.That(() => Step(model), Is.Not.AllocatingGCMemory());
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 같은 세대 갱신을 포함한 Step은 할당하지 않는다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_MotionAllocation_같은세대갱신_할당없음()
        {
            var model = CreateWarmModel();

            Assert.That(() => StepRefresh(model), Is.Not.AllocatingGCMemory());
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 같은 세대에서 연산을 바꾸는 Step은 할당하지 않는다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_MotionAllocation_같은세대연산변경_할당없음()
        {
            var model = CreateWarmModel();

            void Flip()
            {
                StepFlip(model);
                StepRefresh(model);
            }

            Assert.That(Flip, Is.Not.AllocatingGCMemory());
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 항목 상태 조회는 할당하지 않는다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_MotionAllocation_항목조회_할당없음()
        {
            var model = CreateWarmModel();
            var key = new MotionSourceKey("Knockback", Revision.FromUInt64(1));

            // 조회 결과는 쓰지 않으므로 반환값 없는 메서드로 감싸 TestDelegate로 받는다.
            void Lookup()
            {
                model.TryGetModifier(key, out _);
            }

            Lookup();

            Assert.That(Lookup, Is.Not.AllocatingGCMemory());
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 같은 구성의 Capture·Step·Restore는 할당하지 않는다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_MotionAllocation_같은구성저장복원_할당없음()
        {
            var model = CreateWarmModel();
            var snapshot = model.Capture();

            void RoundTrip()
            {
                model.Capture(snapshot);
                Step(model);
                model.Restore(snapshot);
            }

            RoundTrip();

            Assert.That(RoundTrip, Is.Not.AllocatingGCMemory());
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Up을 바꾸는 Step과 입력 버퍼의 Up 전달은 할당하지 않는다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_MotionAllocation_Up변경_할당없음()
        {
            var model = CreateWarmModel();
            var buffer = new MotionInputBuffer();

            void Tilt()
            {
                StepUp(model, Vector3.right);
                StepUp(model, Vector3.up);

                buffer.Add(walk[0]);
                buffer.SetUp(Vector3.forward);
                model.StepLocal(DELTA, buffer.AsStepInput());
                buffer.Clear();
            }

            Tilt();

            Assert.That(Tilt, Is.Not.AllocatingGCMemory());
        }

    #endregion

    }
}
