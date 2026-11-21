/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : TEST_MotionModel.cs
수정일 : 2026-11-21

# 설명
Motion Model의 네 층(기본 의도·Override·제어 배율·외부 속도) 합성, 시간 진행, 층별 입력 검증, Snapshot 재실행을 검증한다.

# 특이사항, 제약사항
Physics와 Body 없이 순수 합성 결과(MotionFrame)만 검증한다.

# 테스트 구성
 B: 기본 합성과 Up 축
 O: Override 층 (우선순위, 수명, Track)
 S: 제어 배율 (Scale)
 E: 외부 속도 (External)
 I: 입력 검증, 용량, 입력 버퍼
 R: Snapshot 복원과 결정성 (Restore)
========================================================================= BLOCK_HEADER_END */

using System;
using System.Collections;
using System.Collections.Generic;

using UnityEngine;

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
    /// Motion Model의 층 합성과 수명 계약을 검증합니다.
    /// </summary>
    // ============================================================
    public sealed class TEST_MotionModel
    {

    #region 헬퍼

        private const double DELTA = 0.1d;

        private static readonly MotionActorKey actor = new(1, 1, 1);

        private static readonly MotionIntent[] walk =
        {
            new(MotionChannel.Planar, new Vector3(2f, 0f, 0f))
        };

        private static readonly MotionIntent[] stop =
        {
            new(MotionChannel.Planar, Vector3.zero)
        };

        private static T[] Of<T>(params T[] items) => items;

        private static MotionFrame Step
        (
            MotionModel model, MotionIntent[] intents = null, MotionOverrideCommand[] overrides = null,
            MotionScaleCommand[] scales = null, MotionExternalCommand[] externals = null,
            Vector3 impulse = default
        )
        {
            var input = new MotionStepInput(intents, overrides, scales, externals, impulse);
            return model.StepLocal(DELTA, input, MotionFeedback.None);
        }

        private static void AssertVector(Vector3 expected, Vector3 actual, float tolerance = 1e-4f)
        {
            Assert.That((expected - actual).magnitude, Is.LessThan(tolerance), $"expected {expected} but was {actual}");
        }

        private static MotionSourceKey Key(long id, long generation = 1) => new(id, generation);

    #endregion

    #region B-1: 기본 합성과 Up 축

        // ----------------------------------------------------------------------
        /// <summary>
        /// 의도는 Up 축으로 Planar와 Elevation에 나뉘고 한 Tick만 유지됩니다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_MotionModel_의도는Up축분리와한Tick수명()
        {
            var model = new MotionModel(actor, Vector3.up);

            var frame = Step(model, Of(new MotionIntent(MotionChannel.Planar | MotionChannel.Elevation, new Vector3(3f, 4f, 5f))));
            Assert.That(frame.PlanarMode, Is.EqualTo(MotionChannelMode.Set));
            AssertVector(new Vector3(3f, 0f, 5f), frame.Planar);
            Assert.That(frame.ElevationMode, Is.EqualTo(MotionChannelMode.Set));
            Assert.That(frame.Elevation, Is.EqualTo(4f).Within(1e-5f));
            Assert.That(frame.RotationMode, Is.EqualTo(MotionChannelMode.Free));

            var next = Step(model);
            Assert.That(next.PlanarMode, Is.EqualTo(MotionChannelMode.Free));
            Assert.That(next.ElevationMode, Is.EqualTo(MotionChannelMode.Free));

            // 2D 위에서 보기는 Up이 Z라 XY 전체가 Planar다.
            var topDown = new MotionModel(actor, MotionModel.TopDown2DUp);
            var flat = Step(topDown, Of(new MotionIntent(MotionChannel.Planar, new Vector3(3f, 4f, 0f))));
            AssertVector(new Vector3(3f, 4f, 0f), flat.Planar);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Up 축은 유한한 비영 벡터만 받아 같은 값으로 정규화합니다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_MotionModel_Up축검증()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new MotionModel(actor, Vector3.zero));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MotionModel(actor, new Vector3(float.NaN, 1f, 0f)));
            Assert.Throws<ArgumentException>(() => new MotionModel(default, Vector3.up));

            AssertVector(Vector3.up, new MotionModel(actor, new Vector3(0f, 2f, 0f)).Up, 1e-6f);
            AssertVector(Vector3.right, new MotionModel(actor, new Vector3(2e-6f, 0f, 0f)).Up, 1e-6f);

            // float 제곱이 넘치는 큰 유한 축도 double로 계산해 같은 단위 벡터가 된다.
            AssertVector(Vector3.right, new MotionModel(actor, new Vector3(1e20f, 0f, 0f)).Up, 1e-6f);
            AssertVector(new Vector3(1f, 1f, 0f).normalized, new MotionModel(actor, new Vector3(3e38f, 3e38f, 0f)).Up, 1e-6f);
        }

    #endregion

    #region O-1: Override 우선순위와 수명

        // ------------------------------------------------------------
        /// <summary>
        /// 대시 Override가 끝나면 다시 획득하지 않아도 걷기가 이어집니다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_MotionModel_대시종료후의도자동복귀()
        {
            var model = new MotionModel(actor, Vector3.up);
            var dash = Key(1);
            var begin = Of(MotionOverrideCommand.Begin(dash, MotionChannel.Planar, 10, new Vector3(9f, 0f, 0f), duration: 0.25f));

            AssertVector(new Vector3(9f, 0f, 0f), Step(model, walk, begin).Planar);
            AssertVector(new Vector3(9f, 0f, 0f), Step(model, walk).Planar);
            AssertVector(new Vector3(9f, 0f, 0f), Step(model, walk).Planar);
            Assert.That(model.IsOverrideActive(dash), Is.False);
            AssertVector(new Vector3(2f, 0f, 0f), Step(model, walk).Planar);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 높은 우선순위가 위이고, 같은 우선순위는 나중에 시작한 쪽이 위입니다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_MotionModel_우선순위와시작순서()
        {
            var model = new MotionModel(actor, Vector3.up);
            var first = Key(1);
            var second = Key(2);

            Step(model, overrides: Of(MotionOverrideCommand.Begin(first, MotionChannel.Planar, 5, new Vector3(1f, 0f, 0f))));
            AssertVector(new Vector3(2f, 0f, 0f), Step(model, overrides: Of(MotionOverrideCommand.Begin(second, MotionChannel.Planar, 5, new Vector3(2f, 0f, 0f)))).Planar);
            AssertVector(new Vector3(2f, 0f, 0f), Step(model, overrides: Of(MotionOverrideCommand.Begin(Key(3), MotionChannel.Planar, 3, new Vector3(3f, 0f, 0f)))).Planar);
            AssertVector(new Vector3(1f, 0f, 0f), Step(model, overrides: Of(MotionOverrideCommand.End(second))).Planar);

            // 채널이 다른 Override는 서로 가리지 않는다.
            var turn = Step(model, overrides: Of(MotionOverrideCommand.Begin(Key(4), MotionChannel.Rotation, 9, Vector3.zero, new Vector3(0f, 2f, 0f))));
            AssertVector(new Vector3(1f, 0f, 0f), turn.Planar);
            Assert.That(turn.RotationMode, Is.EqualTo(MotionChannelMode.Set));
            AssertVector(new Vector3(0f, 2f, 0f), turn.Rotation);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 어느 채널에서도 가려진 Override는 시간이 흐르지 않습니다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_MotionModel_가려진Override는시간정지()
        {
            var model = new MotionModel(actor, Vector3.up);
            var low = Key(1);
            var high = Key(2);

            Step
            (
                model, overrides: Of
                (
                    MotionOverrideCommand.Begin(low, MotionChannel.Planar, 1, new Vector3(1f, 0f, 0f), duration: 0.15f),
                    MotionOverrideCommand.Begin(high, MotionChannel.Planar, 9, new Vector3(5f, 0f, 0f))
                )
            );
            AssertVector(new Vector3(5f, 0f, 0f), Step(model).Planar);
            AssertVector(new Vector3(5f, 0f, 0f), Step(model).Planar);

            AssertVector(new Vector3(1f, 0f, 0f), Step(model, overrides: Of(MotionOverrideCommand.End(high))).Planar);
            AssertVector(new Vector3(1f, 0f, 0f), Step(model).Planar);
            Assert.That(Step(model).PlanarMode, Is.EqualTo(MotionChannelMode.Free));
        }

    #endregion

    #region O-2: Track Override

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> Track Override는 Body 회전 기준 변위를 속도로 평가하고
        /// <br/> 지속 시간이 0이면 Track 끝에서 종료합니다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_MotionModel_Track평가와종료()
        {
            var positions = Of(Vector3.zero, new Vector3(0f, 0f, 1f), new Vector3(0f, 0f, 2f));
            var yaw = Of(0f, 9f, 18f);
            var track = new MotionTrack(10f, positions, yaw);
            var library = new MotionTrackLibrary(Of(new KeyValuePair<int, MotionTrack>(1, track)));
            var model = new MotionModel(actor, Vector3.up, library);
            var facing = new MotionFeedback(Quaternion.Euler(0f, 90f, 0f), true);
            var attack = Key(1);
            var begins = Of
            (
                MotionOverrideCommand.BeginTrack(attack, MotionChannel.Planar | MotionChannel.Rotation, 5, 1, actionID: 4),
                MotionOverrideCommand.BeginTrack(Key(2), MotionChannel.Planar, 1, 99)
            );
            var statuses = new MotionInputStatus[begins.Length];

            var first = model.StepLocal(DELTA, new MotionStepInput(overrides: begins), facing, new MotionStepStatuses(overrides: statuses));

            Assert.That(statuses[0], Is.EqualTo(MotionInputStatus.Accepted));
            Assert.That(statuses[1], Is.EqualTo(MotionInputStatus.UnknownTrack));
            AssertVector(new Vector3(10f, 0f, 0f), first.Planar, 1e-3f);
            AssertVector(new Vector3(0f, 90f * Mathf.Deg2Rad, 0f), first.Rotation, 1e-3f);
            Assert.That(first.ActionID, Is.EqualTo(4));

            AssertVector(new Vector3(10f, 0f, 0f), model.StepLocal(DELTA, default, facing).Planar, 1e-3f);
            Assert.That(model.IsOverrideActive(attack), Is.False);
            Assert.That(model.StepLocal(DELTA, default, facing).PlanarMode, Is.EqualTo(MotionChannelMode.Free));

            // 재생 속도 2배는 같은 Tick에 두 배 변위를 평가한다.
            var fast = Of(MotionOverrideCommand.BeginTrack(Key(3), MotionChannel.Planar, 5, 1, 2f));
            AssertVector(new Vector3(20f, 0f, 0f), model.StepLocal(DELTA, new MotionStepInput(overrides: fast), facing).Planar, 1e-3f);
        }

    #endregion

    #region S-1: 제어 배율

        // ------------------------------------------------------------
        /// <summary>
        /// 스턴은 의도·Override를 막지만 외부 속도(넉백)는 유지합니다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_MotionModel_스턴중넉백유지()
        {
            var model = new MotionModel(actor, Vector3.up);
            var stun = Key(1);

            var frame = Step
            (
                model, walk,
                scales: Of(MotionScaleCommand.Add(stun, MotionChannel.All, MotionLayer.All, 100, NumericFOperation.SET, 0f)),
                externals: Of(MotionExternalCommand.Set(Key(2), 0, NumericFOperation.ADD, new Vector3(0f, 0f, 4f)))
            );
            AssertVector(new Vector3(0f, 0f, 4f), frame.Planar);
            Assert.That(frame.BlockedChannels, Is.EqualTo(MotionChannel.All));

            var dash = Step(model, walk, Of(MotionOverrideCommand.Begin(Key(3), MotionChannel.Planar, 10, new Vector3(9f, 0f, 0f))));
            AssertVector(new Vector3(0f, 0f, 4f), dash.Planar);

            var released = Step(model, walk, Of(MotionOverrideCommand.End(Key(3))), Of(MotionScaleCommand.Remove(stun)));
            AssertVector(new Vector3(2f, 0f, 4f), released.Planar);
            Assert.That(released.BlockedChannels, Is.EqualTo(MotionChannel.None));
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 배율은 Order 순서로 적용되고 지정한 층에만 적용됩니다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_MotionModel_배율순서와대상층()
        {
            var model = new MotionModel(actor, Vector3.up);

            // SET 3(Order 1) → MUL 0.5(Order 2) = 1.5 배.
            var scales = Of
            (
                MotionScaleCommand.Add(Key(1), MotionChannel.Planar, MotionLayer.Intent, 2, NumericFOperation.MUL, 0.5f),
                MotionScaleCommand.Add(Key(2), MotionChannel.Planar, MotionLayer.Intent, 1, NumericFOperation.SET, 3f, duration: 0.1f)
            );
            AssertVector(new Vector3(3f, 0f, 0f), Step(model, walk, scales: scales).Planar);

            // 지속 시간이 끝난 SET은 사라지고, Intent 대상 배율은 Override에 적용되지 않는다.
            AssertVector(new Vector3(1f, 0f, 0f), Step(model, walk).Planar);
            AssertVector(new Vector3(8f, 0f, 0f), Step(model, walk, Of(MotionOverrideCommand.Begin(Key(3), MotionChannel.Planar, 1, new Vector3(8f, 0f, 0f)))).Planar);
            Assert.That(model.IsScaleActive(Key(2)), Is.False);
        }

    #endregion

    #region E-1: 외부 속도 합성과 벽 차단

        // ------------------------------------------------------------
        /// <summary>
        /// 걷는 동안 넉백이 다음 Tick에 사라지지 않고 감쇠합니다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_MotionModel_걷는중넉백유지와감쇠()
        {
            var model = new MotionModel(actor, Vector3.up);
            var knockback = Key(1);
            var halfLife = Mathf.Log(2f) / 0.1f;
            var hit = Of(MotionExternalCommand.Set(knockback, 0, NumericFOperation.ADD, new Vector3(0f, 0f, 10f), halfLife));

            AssertVector(new Vector3(2f, 0f, 10f), Step(model, walk, externals: hit).Planar);
            AssertVector(new Vector3(2f, 0f, 5f), Step(model, walk).Planar, 1e-3f);
            AssertVector(new Vector3(2f, 0f, 2.5f), Step(model, walk).Planar, 1e-3f);

            for (var index = 0; index < 40; index++)
            {
                Step(model, walk);
            }

            Assert.That(model.IsExternalActive(knockback), Is.False);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 외부 속도는 Order 순서로 SET·ADD를 적용하고 같은 키는 갱신합니다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_MotionModel_외부속도순서와갱신()
        {
            var model = new MotionModel(actor, Vector3.up);
            var wind = Key(3);
            var refresh = Of(MotionExternalCommand.Set(wind, 20, NumericFOperation.ADD, new Vector3(4f, 0f, 0f), duration: 0.15f));
            var start = Of
            (
                MotionExternalCommand.Set(Key(1), 0, NumericFOperation.ADD, new Vector3(1f, 0f, 0f)),
                MotionExternalCommand.Set(Key(2), 10, NumericFOperation.SET, new Vector3(0f, 0f, 3f)),
                MotionExternalCommand.Set(wind, 20, NumericFOperation.ADD, new Vector3(2f, 0f, 0f), duration: 0.15f)
            );

            AssertVector(new Vector3(2f, 0f, 3f), Step(model, stop, externals: start).Planar);

            // 영역은 매 Tick 갱신해 유효 시간을 늘리고, 갱신을 멈추면 사라진다.
            AssertVector(new Vector3(4f, 0f, 3f), Step(model, stop, externals: refresh).Planar);
            AssertVector(new Vector3(4f, 0f, 3f), Step(model, stop, externals: refresh).Planar);
            AssertVector(new Vector3(4f, 0f, 3f), Step(model, stop).Planar);
            AssertVector(new Vector3(0f, 0f, 3f), Step(model, stop).Planar);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Free 채널에는 외부 속도의 변화량만 Impulse로 넘깁니다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_MotionModel_Free채널은외부속도변화량만충격()
        {
            var model = new MotionModel(actor, Vector3.up);
            var push = Key(1);

            var start = Step(model, externals: Of(MotionExternalCommand.Set(push, 0, NumericFOperation.ADD, new Vector3(3f, 2f, 0f))));
            Assert.That(start.PlanarMode, Is.EqualTo(MotionChannelMode.Free));
            Assert.That(start.ElevationMode, Is.EqualTo(MotionChannelMode.Free));
            AssertVector(new Vector3(3f, 2f, 0f), start.Impulse);

            AssertVector(Vector3.zero, Step(model).Impulse);
            AssertVector(new Vector3(-3f, -2f, 0f), Step(model, externals: Of(MotionExternalCommand.Remove(push))).Impulse);

            // Set 채널에서 이미 반영한 몫은 Free로 바뀌어도 다시 더하지 않는다.
            var held = Step(model, walk, externals: Of(MotionExternalCommand.Set(Key(2), 0, NumericFOperation.ADD, new Vector3(0f, 0f, 1f))));
            AssertVector(new Vector3(2f, 0f, 1f), held.Planar);
            AssertVector(Vector3.zero, Step(model).Impulse);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 벽 법선 성분은 항목마다 제거되어 벽에서 떨어져도 되살아나지 않습니다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_MotionModel_벽법선은항목별제거()
        {
            var model = new MotionModel(actor, Vector3.up);
            var wall = MotionFeedback.None.WithBlockingNormal(new Vector3(-2f, 0f, 0f));

            Step(model, stop, externals: Of(MotionExternalCommand.Set(Key(1), 0, NumericFOperation.ADD, new Vector3(3f, 0f, 4f))));
            var blocked = model.StepLocal(DELTA, new MotionStepInput(stop), wall);
            AssertVector(new Vector3(0f, 0f, 4f), blocked.Planar);

            AssertVector(new Vector3(0f, 0f, 4f), Step(model, stop).Planar);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Impulse는 그 Tick에만 나타나고 Clear는 모든 외부 속도를 지웁니다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_MotionModel_Impulse와외부속도전체제거()
        {
            var model = new MotionModel(actor, Vector3.up);
            var forward = Of
            (
                MotionExternalCommand.Set(Key(1), 0, NumericFOperation.ADD, Vector3.forward),
                MotionExternalCommand.Set(Key(2), 0, NumericFOperation.ADD, Vector3.forward)
            );

            AssertVector(new Vector3(0f, 5f, 0f), Step(model, impulse: new Vector3(0f, 5f, 0f)).Impulse);
            AssertVector(Vector3.zero, Step(model).Impulse);

            Step(model, walk, externals: forward);
            AssertVector(new Vector3(2f, 0f, 0f), Step(model, walk, externals: Of(MotionExternalCommand.Clear())).Planar);
            Assert.That(model.IsExternalActive(Key(1)), Is.False);
        }

    #endregion

    #region I-1: 입력 검증과 용량

        // ------------------------------------------------------------
        /// <summary>
        /// 같은 슬롯의 세대 규칙과 잘못된 입력을 층별 결과로 알립니다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_MotionModel_세대규칙과입력검증()
        {
            var model = new MotionModel(actor, Vector3.up);
            var linear = new Vector3(1f, 0f, 0f);
            var intents = Of(new MotionIntent(MotionChannel.None, linear));
            var overrides = Of
            (
                MotionOverrideCommand.Begin(Key(1, 1), MotionChannel.Planar, 0, linear),
                MotionOverrideCommand.Begin(Key(1, 1), MotionChannel.Planar, 0, linear),
                MotionOverrideCommand.Begin(Key(1, 2), MotionChannel.Planar, 0, linear),
                MotionOverrideCommand.Begin(Key(1, 1), MotionChannel.Planar, 0, linear),
                MotionOverrideCommand.End(Key(1, 1)),
                MotionOverrideCommand.Begin(default, MotionChannel.Planar, 0, linear),
                MotionOverrideCommand.Begin(Key(2), MotionChannel.None, 0, linear),
                MotionOverrideCommand.Begin(Key(3), MotionChannel.Planar, 0, new Vector3(float.NaN, 0f, 0f)),
                MotionOverrideCommand.Begin(Key(4), MotionChannel.Planar, 0, linear, duration: -1f)
            );
            var scales = Of(MotionScaleCommand.Add(Key(5), MotionChannel.Planar, MotionLayer.None, 0, NumericFOperation.MUL, 0.5f));
            var externals = Of(MotionExternalCommand.Set(Key(6), 0, NumericFOperation.MUL, linear));
            var intentStatuses = new MotionInputStatus[intents.Length];
            var overrideStatuses = new MotionInputStatus[overrides.Length];
            var scaleStatuses = new MotionInputStatus[scales.Length];
            var externalStatuses = new MotionInputStatus[externals.Length];

            model.StepLocal
            (
                DELTA, new MotionStepInput(intents, overrides, scales, externals), MotionFeedback.None,
                new MotionStepStatuses(intentStatuses, overrideStatuses, scaleStatuses, externalStatuses)
            );

            Assert.That(intentStatuses[0], Is.EqualTo(MotionInputStatus.InvalidInput));
            Assert.That(overrideStatuses[0], Is.EqualTo(MotionInputStatus.Accepted));
            Assert.That(overrideStatuses[1], Is.EqualTo(MotionInputStatus.Duplicate));
            Assert.That(overrideStatuses[2], Is.EqualTo(MotionInputStatus.Accepted));
            Assert.That(overrideStatuses[3], Is.EqualTo(MotionInputStatus.StaleGeneration));
            Assert.That(overrideStatuses[4], Is.EqualTo(MotionInputStatus.NotFound));
            Assert.That(overrideStatuses[5], Is.EqualTo(MotionInputStatus.InvalidInput));
            Assert.That(overrideStatuses[6], Is.EqualTo(MotionInputStatus.InvalidInput));
            Assert.That(overrideStatuses[7], Is.EqualTo(MotionInputStatus.InvalidInput));
            Assert.That(overrideStatuses[8], Is.EqualTo(MotionInputStatus.InvalidInput));
            Assert.That(scaleStatuses[0], Is.EqualTo(MotionInputStatus.InvalidInput));
            Assert.That(externalStatuses[0], Is.EqualTo(MotionInputStatus.InvalidInput));
            Assert.That(model.IsOverrideActive(Key(1, 2)), Is.True);
            Assert.That(model.IsOverrideActive(Key(1, 1)), Is.False);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 층별 용량을 넘는 항목은 기존 항목을 건드리지 않고 거절합니다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_MotionModel_용량초과거절()
        {
            var model = new MotionModel(actor, Vector3.up, capacity: 2);
            var overrides = Of
            (
                MotionOverrideCommand.Begin(Key(1), MotionChannel.Planar, 0, Vector3.right),
                MotionOverrideCommand.Begin(Key(2), MotionChannel.Planar, 0, Vector3.right),
                MotionOverrideCommand.Begin(Key(3), MotionChannel.Planar, 0, Vector3.right)
            );
            var statuses = new MotionInputStatus[overrides.Length];

            model.StepLocal
            (
                DELTA, new MotionStepInput(overrides: overrides), MotionFeedback.None,
                new MotionStepStatuses(overrides: statuses)
            );

            Assert.That(statuses[2], Is.EqualTo(MotionInputStatus.CapacityReached));
            Assert.That(model.IsOverrideActive(Key(1)), Is.True);
            Assert.That(model.IsOverrideActive(Key(2)), Is.True);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 잘못된 Tick·Impulse·결과 버퍼는 상태를 바꾸기 전에 거절합니다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_MotionModel_Tick검증은상태변경전거절()
        {
            var model = new MotionModel(actor, Vector3.up);
            model.Step(new MotionTick(5, DELTA), new MotionStepInput(walk), MotionFeedback.None);
            var version = model.StepVersion;
            var twoIntents = Of(walk[0], walk[0]);

            Assert.Throws<ArgumentOutOfRangeException>(() => model.Step(default, new MotionStepInput(walk), MotionFeedback.None));
            Assert.Throws<ArgumentOutOfRangeException>(() => model.Step(new MotionTick(5, DELTA), new MotionStepInput(walk), MotionFeedback.None));
            Assert.Throws<ArgumentOutOfRangeException>(() => model.Step(new MotionTick(3, DELTA), new MotionStepInput(walk), MotionFeedback.None));
            Assert.Throws<ArgumentOutOfRangeException>
            (
                () => model.Step(new MotionTick(6, DELTA), new MotionStepInput(impulse: new Vector3(float.NaN, 0f, 0f)), MotionFeedback.None)
            );
            Assert.Throws<ArgumentException>
            (
                () => model.Step
                (
                    new MotionTick(6, DELTA), new MotionStepInput(twoIntents), MotionFeedback.None,
                    new MotionStepStatuses(new MotionInputStatus[1])
                )
            );
            Assert.Throws<ArgumentOutOfRangeException>(() => model.StepLocal(0d, new MotionStepInput(walk), MotionFeedback.None));

            Assert.That(model.LastTick, Is.EqualTo(5));
            Assert.That(model.StepVersion, Is.EqualTo(version));
        }

    #endregion

    #region I-2: 입력 버퍼

        // ------------------------------------------------------------
        /// <summary>
        /// 입력 버퍼의 Lease를 해제하면 종료 명령이 들어갑니다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_MotionModel_입력버퍼Lease()
        {
            var model = new MotionModel(actor, Vector3.up);
            var buffer = new MotionInputBuffer(1);
            var ladder = Key(1);

            var lease = buffer.AcquireOverride(MotionOverrideCommand.Begin(ladder, MotionChannel.All, 50, new Vector3(0f, 1f, 0f)));
            buffer.Add(walk[0]);
            buffer.Add(MotionOverrideCommand.Begin(Key(2), MotionChannel.Rotation, 1, Vector3.zero));
            buffer.AddImpulse(new Vector3(0f, 1f, 0f));
            buffer.AddImpulse(new Vector3(0f, 2f, 0f));
            Assert.That(buffer.Count, Is.EqualTo(3));
            Assert.That(buffer.Impulse, Is.EqualTo(new Vector3(0f, 3f, 0f)));

            var frame = model.StepLocal(DELTA, buffer.AsStepInput(), MotionFeedback.None);
            AssertVector(new Vector3(0f, 3f, 0f), frame.Impulse);
            buffer.Clear();
            Assert.That(model.IsOverrideActive(ladder), Is.True);
            Assert.That(buffer.Impulse, Is.EqualTo(Vector3.zero));

            lease.Release();
            Assert.That(buffer.Count, Is.EqualTo(1));
            model.StepLocal(DELTA, buffer.AsStepInput(), MotionFeedback.None);
            Assert.That(model.IsOverrideActive(ladder), Is.False);
            Assert.Throws<ArgumentException>(() => buffer.AcquireOverride(MotionOverrideCommand.End(ladder)));
        }

    #endregion

    #region R-1: Snapshot 복원과 결정성

        // ------------------------------------------------------------
        /// <summary>
        /// Snapshot으로 되돌려 같은 입력을 재실행하면 같은 결과가 나옵니다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_MotionModel_Snapshot복원후재실행일치()
        {
            var model = new MotionModel(actor, Vector3.up);
            Step
            (
                model, walk,
                Of(MotionOverrideCommand.Begin(Key(1), MotionChannel.Planar, 5, new Vector3(6f, 0f, 0f), duration: 0.35f)),
                Of(MotionScaleCommand.Add(Key(2), MotionChannel.Planar, MotionLayer.All, 0, NumericFOperation.MUL, 0.5f, duration: 0.25f)),
                Of(MotionExternalCommand.Set(Key(3), 0, NumericFOperation.ADD, new Vector3(0f, 0f, 8f), decay: 3f))
            );
            var snapshot = model.Capture();
            var version = model.StepVersion;
            var original = new MotionFrame[4];

            for (var index = 0; index < original.Length; index++)
            {
                original[index] = Step(model, walk);
            }

            model.Restore(snapshot);
            Assert.That(model.LastTick, Is.EqualTo(snapshot.LastTick));
            Assert.That(model.StepVersion, Is.GreaterThan(version + original.Length));

            for (var index = 0; index < original.Length; index++)
            {
                var replayed = Step(model, walk);
                Assert.That(replayed.Tick.Index, Is.EqualTo(original[index].Tick.Index));
                Assert.That(replayed.Planar, Is.EqualTo(original[index].Planar));
                Assert.That(replayed.Impulse, Is.EqualTo(original[index].Impulse));
                Assert.That(replayed.StepVersion, Is.Not.EqualTo(original[index].StepVersion));
            }

            // 같은 용량 Snapshot은 재사용할 수 있다.
            model.Capture(snapshot);
            Assert.That(snapshot.LastTick, Is.EqualTo(model.LastTick));
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 다른 Actor나 다른 용량의 Snapshot은 복원하지 않습니다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_MotionModel_다른ActorSnapshot복원거절()
        {
            var model = new MotionModel(actor, Vector3.up);
            var other = new MotionModel(new MotionActorKey(2, 1, 1), Vector3.up);
            var small = new MotionModel(actor, Vector3.up, capacity: 4);

            Assert.Throws<ArgumentException>(() => model.Restore(other.Capture()));
            Assert.Throws<ArgumentException>(() => model.Restore(small.Capture()));
            Assert.Throws<ArgumentException>(() => model.Capture(new MotionModelSnapshot(4)));
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 같은 입력이면 서로 다른 Model 인스턴스가 같은 결과를 냅니다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_MotionModel_같은입력같은결과()
        {
            var server = new MotionModel(new MotionActorKey(1, 7, 1), Vector3.up);
            var client = new MotionModel(new MotionActorKey(2, 7, 1), Vector3.up);
            var overrides = Of(MotionOverrideCommand.Begin(Key(1), MotionChannel.Planar, 5, new Vector3(6f, 0f, 1f), duration: 0.2f));
            var externals = Of(MotionExternalCommand.Set(Key(2), 0, NumericFOperation.ADD, new Vector3(0f, 3f, 2f), decay: 2f));

            for (var index = 0; index < 5; index++)
            {
                var a = index == 0 ? Step(server, walk, overrides, externals: externals) : Step(server, walk);
                var b = index == 0 ? Step(client, walk, overrides, externals: externals) : Step(client, walk);

                Assert.That(b.Planar, Is.EqualTo(a.Planar));
                Assert.That(b.Elevation, Is.EqualTo(a.Elevation));
                Assert.That(b.Impulse, Is.EqualTo(a.Impulse));
            }
        }

    #endregion

    }
}
