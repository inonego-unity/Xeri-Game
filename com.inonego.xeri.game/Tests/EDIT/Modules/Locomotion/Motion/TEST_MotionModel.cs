/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : TEST_MotionModel.cs
수정일 : 2026-11-22

# 설명
Motion Model의 기본 의도와 Modifier 항목(SET·ADD·SUB·MUL 등·Impulse)을 채널별 MValue로 접는 합성,
Order에 따른 가려짐, 같은 세대 갱신, 입력 검증, Snapshot 재실행과 사용처 도구(Track 평가, 차단 법선 제거)를 검증한다.

# 특이사항, 제약사항
Physics와 Body 없이 순수 합성 결과(MotionFrame)만 검증한다.
수명과 시간에 따라 바뀌는 값은 사용처 몫이므로 테스트가 직접 Apply·End로 표현한다.

# 테스트 구성
 B: 기본 합성, Up 축, 항목 상태 조회
 S: SET 항목 (Order, 가려짐, Planar 투영)
 M: MUL 등 나머지 연산과 Order 배치
 A: ADD·SUB 항목 (갱신, Free 채널, 충격, 속도를 남기는 종료)
 U: Step별 Up 축
 I: 입력 검증, 용량, 입력 버퍼
 R: Snapshot 복원과 결정성
 D: 사용처 도구 (Track 평가, 차단 법선 제거, Up 정렬)
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
using inonego.Xeri.Game.TEST;
using inonego.Xeri.Game.Motion;

namespace inonego.Xeri.Game.TEST.EDIT._Motion
{
    // ============================================================
    /// <summary>
    /// Motion Model의 Modifier 합성과 항목 계약을 검증합니다.
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

        private static MotionFrame Step(MotionModel model, MotionIntent[] intents = null, params MotionModifierCommand[] modifiers)
        {
            return model.StepLocal(DELTA, new MotionStepInput(intents, modifiers));
        }

        private static MotionInputStatus[] StepWithStatuses(MotionModel model, MotionIntent[] intents, params MotionModifierCommand[] modifiers)
        {
            var statuses = new MotionInputStatus[modifiers.Length];

            model.StepLocal(DELTA, new MotionStepInput(intents, modifiers), new MotionStepStatuses(modifiers: statuses));

            return statuses;
        }

        private static void AssertVector(Vector3 expected, Vector3 actual, float tolerance = 1e-4f)
        {
            Assert.That((expected - actual).magnitude, Is.LessThan(tolerance), $"expected {expected} but was {actual}");
        }

        private static MotionSourceKey Key(string key, long generation = 1) => new(key, generation);

        private static MotionChannelValues Planar(Vector3 value) => new(MotionChannel.Planar, value);

        private static MotionModifierCommand Set(string key, int order, Vector3 linear, int actionID = 0) =>
            MotionModifierCommand.Apply(Key(key), order, NumericFOperation.SET, Planar(linear), actionID);

        private static MotionModifierCommand Add(string key, int order, Vector3 linear, long generation = 1) =>
            MotionModifierCommand.Apply(Key(key, generation), order, NumericFOperation.ADD, Planar(linear));

        private static MotionModifierCommand Scale(string key, MotionChannel channels, int order, float factor) =>
            MotionModifierCommand.Apply(Key(key), order, NumericFOperation.MUL, MotionChannelValues.Uniform(channels, factor));

        private static bool IsActive(MotionModel model, MotionSourceKey source) => model.TryGetModifier(source, out _);

        private static Quaternion Unit(Quaternion value)
        {
            var length = Mathf.Sqrt(value.x * value.x + value.y * value.y + value.z * value.z + value.w * value.w);

            return new Quaternion(value.x / length, value.y / length, value.z / length, value.w / length);
        }

        // 회전 벡터(축 × 라디안)를 쿼터니언으로 바꾼다. Unity 네이티브 호출 없이 계산한다.
        private static Quaternion Turn(Vector3 rotationVector)
        {
            var angle = rotationVector.magnitude;

            if (angle <= 0f)
            {
                return Quaternion.identity;
            }

            var axis = rotationVector / angle * Mathf.Sin(angle * 0.5f);

            return new Quaternion(axis.x, axis.y, axis.z, Mathf.Cos(angle * 0.5f));
        }

    #endregion

    #region B: 기본 합성과 Up 축

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
            AssertVector(Vector3.right, new MotionModel(actor, new Vector3(1e20f, 0f, 0f)).Up, 1e-6f);
            AssertVector(new Vector3(1f, 1f, 0f).normalized, new MotionModel(actor, new Vector3(3e38f, 3e38f, 0f)).Up, 1e-6f);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 항목 상태는 값으로 읽고, 보임 여부는 마지막 Step 기준입니다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_MotionModel_항목상태조회()
        {
            var model = new MotionModel(actor, Vector3.up);

            Step(model, walk);
            Step(model, walk, Set("Dash", 10, new Vector3(9f, 0f, 0f), actionID: 4));

            Assert.That(model.TryGetModifier(Key("Dash"), out var dash), Is.True);
            Assert.That(dash.Source, Is.EqualTo(Key("Dash")));
            Assert.That(dash.Order, Is.EqualTo(10));
            Assert.That(dash.Operation, Is.EqualTo(NumericFOperation.SET));
            Assert.That(dash.Values.Channels, Is.EqualTo(MotionChannel.Planar));
            AssertVector(new Vector3(9f, 0f, 0f), dash.Values.Planar);
            Assert.That(dash.ActionID, Is.EqualTo(4));
            Assert.That(dash.StartTick, Is.EqualTo(1));
            Assert.That(dash.IsImpulse, Is.False);
            Assert.That(dash.IsVisible, Is.True);

            // 다른 세대나 없는 키는 찾지 않는다.
            Assert.That(model.TryGetModifier(Key("Dash", 2), out _), Is.False);
            Assert.That(model.TryGetModifier(Key("None"), out _), Is.False);
            Assert.That(model.TryGetModifier(default, out _), Is.False);

            // 뒤의 SET에 가려지면 다음 Step부터 보이지 않는다.
            Step(model, walk, Set("Launch", 20, new Vector3(0f, 0f, 6f)));

            Assert.That(model.TryGetModifier(Key("Dash"), out dash), Is.True);
            Assert.That(dash.IsVisible, Is.False);
        }

    #endregion

    #region S: SET 항목

        // ------------------------------------------------------------
        /// <summary>
        /// 대시 SET을 끝내면 다시 등록하지 않아도 걷기가 이어집니다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_MotionModel_대시종료후의도자동복귀()
        {
            var model = new MotionModel(actor, Vector3.up);

            AssertVector(new Vector3(9f, 0f, 0f), Step(model, walk, Set("Dash", 10, new Vector3(9f, 0f, 0f))).Planar);
            AssertVector(new Vector3(9f, 0f, 0f), Step(model, walk).Planar);
            AssertVector(new Vector3(2f, 0f, 0f), Step(model, walk, MotionModifierCommand.End(Key("Dash"))).Planar);
            Assert.That(IsActive(model, Key("Dash")), Is.False);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 높은 Order가 뒤이고, 같은 Order는 나중에 시작한 쪽이 뒤입니다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_MotionModel_Order와시작순서()
        {
            var model = new MotionModel(actor, Vector3.up);

            Step(model, null, Set("First", 5, new Vector3(1f, 0f, 0f), actionID: 1));

            var second = Step(model, null, Set("Second", 5, new Vector3(2f, 0f, 0f), actionID: 2));

            AssertVector(new Vector3(2f, 0f, 0f), second.Planar);
            Assert.That(second.ActionID, Is.EqualTo(2));
            Assert.That(model.TryGetModifier(Key("First"), out var first) && !first.IsVisible, Is.True);
            Assert.That(model.TryGetModifier(Key("Second"), out var later) && later.IsVisible, Is.True);
            AssertVector(new Vector3(2f, 0f, 0f), Step(model, null, Set("Low", 3, new Vector3(3f, 0f, 0f))).Planar);
            AssertVector(new Vector3(1f, 0f, 0f), Step(model, null, MotionModifierCommand.End(Key("Second"))).Planar);

            // 채널이 다른 항목은 서로 가리지 않는다.
            var turn = Step
            (
                model, null,
                MotionModifierCommand.Apply
                (
                    Key("Turn"), 9, NumericFOperation.SET,
                    new MotionChannelValues(MotionChannel.Rotation, rotation: new Vector3(0f, 2f, 0f))
                )
            );

            AssertVector(new Vector3(1f, 0f, 0f), turn.Planar);
            Assert.That(turn.RotationMode, Is.EqualTo(MotionChannelMode.Set));
            AssertVector(new Vector3(0f, 2f, 0f), turn.Rotation);
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> Planar 값은 접은 결과만 평면에 투영합니다.
        /// <br/> 균일 배율은 투영 전후가 같고 Elevation 채널에는 영향을 주지 않습니다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_MotionModel_Planar는접은결과만투영()
        {
            var model = new MotionModel(actor, Vector3.up);

            var frame = Step
            (
                model, null,
                Set("Dash", 10, new Vector3(3f, 5f, 4f)),
                Scale("Slow", MotionChannel.Planar, 20, 0.5f)
            );

            AssertVector(new Vector3(1.5f, 0f, 2f), frame.Planar);
            Assert.That(frame.ElevationMode, Is.EqualTo(MotionChannelMode.Free));

            // Linear 생성 함수는 Up 축으로 Planar와 Elevation을 나눈다.
            var values = MotionChannelValues.Linear(MotionChannel.Planar | MotionChannel.Elevation, new Vector3(3f, 5f, 4f));
            var lifted = Step(model, null, MotionModifierCommand.Apply(Key("Lift"), 30, NumericFOperation.SET, values));

            AssertVector(new Vector3(3f, 0f, 4f), lifted.Planar);
            Assert.That(lifted.Elevation, Is.EqualTo(5f).Within(1e-5f));
        }

    #endregion

    #region M: MUL 등 나머지 연산과 Order 배치

        // ------------------------------------------------------------
        /// <summary>
        /// 스턴(MUL 0)이 넉백을 함께 멈출지는 Order 배치로 정해집니다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_MotionModel_스턴과넉백은Order로선택()
        {
            var stun = Scale("Stun", MotionChannel.All, 50, 0f);

            // 넉백이 스턴보다 앞이면 함께 멈춘다.
            var stopped = new MotionModel(actor, Vector3.up);

            AssertVector(Vector3.zero, Step(stopped, walk, stun, Add("Knockback", 0, new Vector3(0f, 0f, 4f))).Planar);

            // 넉백이 스턴보다 뒤면 스턴 중에도 밀린다.
            var pushed = new MotionModel(actor, Vector3.up);

            AssertVector(new Vector3(0f, 0f, 4f), Step(pushed, walk, stun, Add("Knockback", 60, new Vector3(0f, 0f, 4f))).Planar);

            // 스턴보다 앞의 대시도 멈춘다.
            AssertVector(new Vector3(0f, 0f, 4f), Step(pushed, walk, Set("Dash", 10, new Vector3(9f, 0f, 0f))).Planar);

            var released = Step(pushed, walk, MotionModifierCommand.End(Key("Dash")), MotionModifierCommand.End(Key("Stun")));

            AssertVector(new Vector3(2f, 0f, 4f), released.Planar);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 둔화가 대시에도 걸릴지는 대시보다 뒤에 두는지로 정해집니다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_MotionModel_둔화는Order위치로대상선택()
        {
            var model = new MotionModel(actor, Vector3.up);

            // 대시보다 앞의 둔화는 대시가 덮어써 가려진다.
            AssertVector(new Vector3(1f, 0f, 0f), Step(model, walk, Scale("Front", MotionChannel.Planar, 0, 0.5f)).Planar);
            AssertVector(new Vector3(8f, 0f, 0f), Step(model, walk, Set("Dash", 10, new Vector3(8f, 0f, 0f))).Planar);

            // 대시보다 뒤의 둔화는 대시에도 걸린다.
            AssertVector(new Vector3(4f, 0f, 0f), Step(model, walk, Scale("Behind", MotionChannel.Planar, 20, 0.5f)).Planar);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Core 연산(DIV·MIN·MAX 포함)을 그대로 받아 성분별로 접습니다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_MotionModel_Core연산을그대로접음()
        {
            var model = new MotionModel(actor, Vector3.up);
            var intents = Of(new MotionIntent(MotionChannel.Planar, new Vector3(4f, 0f, -4f)));

            var frame = Step
            (
                model, intents,
                MotionModifierCommand.Apply(Key("Div"), 0, NumericFOperation.DIV, MotionChannelValues.Uniform(MotionChannel.Planar, 2f)),
                MotionModifierCommand.Apply(Key("Min"), 1, NumericFOperation.MIN, MotionChannelValues.Uniform(MotionChannel.Planar, 1f)),
                MotionModifierCommand.Apply(Key("Max"), 2, NumericFOperation.MAX, MotionChannelValues.Uniform(MotionChannel.Planar, -1f))
            );

            AssertVector(new Vector3(1f, 0f, -1f), frame.Planar);
        }

    #endregion

    #region A: ADD·SUB 항목

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> 넉백은 End 전까지 유지되고, 사용처가 같은 세대로 값을 갱신하면
        /// <br/> 시작 순서를 유지한 채 감쇠를 표현할 수 있습니다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_MotionModel_넉백유지와사용처감쇠()
        {
            var model = new MotionModel(actor, Vector3.up);

            AssertVector(new Vector3(2f, 0f, 10f), Step(model, walk, Add("Knockback", 20, new Vector3(0f, 0f, 10f))).Planar);
            AssertVector(new Vector3(2f, 0f, 10f), Step(model, walk).Planar);

            var statuses = StepWithStatuses(model, walk, Add("Knockback", 20, new Vector3(0f, 0f, 5f)));

            Assert.That(statuses[0], Is.EqualTo(MotionInputStatus.Updated));
            AssertVector(new Vector3(2f, 0f, 5f), Step(model, walk).Planar);
            AssertVector(new Vector3(2f, 0f, 0f), Step(model, walk, MotionModifierCommand.End(Key("Knockback"))).Planar);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 저지 불가 대시는 넉백을 가리고, 대시를 끊는 넉백은 대시를 가립니다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_MotionModel_대시와넉백의가림은Order로정해짐()
        {
            var model = new MotionModel(actor, Vector3.up);

            Step(model, walk, Add("Knockback", 20, new Vector3(0f, 0f, 4f)));

            // 넉백보다 뒤의 SET(저지 불가 대시)은 넉백을 가린다.
            var unstoppable = Step(model, walk, Set("Unstoppable", 30, new Vector3(9f, 0f, 0f), actionID: 3));

            AssertVector(new Vector3(9f, 0f, 0f), unstoppable.Planar);
            Assert.That(unstoppable.ActionID, Is.EqualTo(3));

            // 넉백보다 앞의 대시에는 넉백이 더해진다.
            Step(model, walk, MotionModifierCommand.End(Key("Unstoppable")));

            AssertVector(new Vector3(9f, 0f, 4f), Step(model, walk, Set("Dash", 10, new Vector3(9f, 0f, 0f), actionID: 1)).Planar);

            // 대시보다 뒤의 SET 넉백은 대시를 끊고 현재 동작이 된다.
            var interrupted = Step(model, walk, Set("Launch", 40, new Vector3(0f, 0f, 6f), actionID: 7));

            AssertVector(new Vector3(0f, 0f, 6f), interrupted.Planar);
            Assert.That(interrupted.ActionID, Is.EqualTo(7));
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// 같은 키·같은 세대의 적용은 시작 순서를 유지한 채 연산과 값을 갱신합니다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_MotionModel_같은세대는갱신()
        {
            var model = new MotionModel(actor, Vector3.up);

            AssertVector(new Vector3(3f, 0f, 0f), Step(model, stop, Add("Base", 0, new Vector3(1f, 0f, 0f)), Add("Wind", 20, new Vector3(2f, 0f, 0f))).Planar);

            // 영역은 매 Tick 값을 갱신하고, 사용처가 영역을 벗어나면 End로 끝낸다.
            var statuses = StepWithStatuses(model, stop, Add("Wind", 20, new Vector3(4f, 0f, 0f)));

            Assert.That(statuses[0], Is.EqualTo(MotionInputStatus.Updated));
            AssertVector(new Vector3(5f, 0f, 0f), Step(model, stop).Planar);

            // 같은 세대에서 연산도 바꿀 수 있다.
            var sub = MotionModifierCommand.Apply(Key("Wind"), 20, NumericFOperation.SUB, Planar(new Vector3(4f, 0f, 0f)));

            AssertVector(new Vector3(-3f, 0f, 0f), Step(model, stop, sub).Planar);
            AssertVector(new Vector3(1f, 0f, 0f), Step(model, stop, MotionModifierCommand.End(Key("Wind"))).Planar);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Free 채널에는 지속 ADD·SUB의 변화량만 Impulse로 넘깁니다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_MotionModel_Free채널은변화량만충격()
        {
            var model = new MotionModel(actor, Vector3.up);
            var push = MotionModifierCommand.Apply
            (
                Key("Push"), 0, NumericFOperation.ADD,
                MotionChannelValues.Linear(MotionChannel.Planar | MotionChannel.Elevation, new Vector3(3f, 2f, 0f))
            );

            var start = Step(model, null, push);

            Assert.That(start.PlanarMode, Is.EqualTo(MotionChannelMode.Free));
            Assert.That(start.ElevationMode, Is.EqualTo(MotionChannelMode.Free));
            AssertVector(new Vector3(3f, 2f, 0f), start.Impulse);
            AssertVector(Vector3.zero, Step(model).Impulse);
            AssertVector(new Vector3(-3f, -2f, 0f), Step(model, null, MotionModifierCommand.End(Key("Push"))).Impulse);

            // Set 채널에서 이미 반영한 몫은 Free로 바뀌어도 다시 더하지 않는다.
            var held = Step(model, walk, Add("Hold", 0, new Vector3(0f, 0f, 1f)));

            AssertVector(new Vector3(2f, 0f, 1f), held.Planar);
            AssertVector(Vector3.zero, Step(model).Impulse);

            // Free 채널의 ADD·SUB 외 연산은 Body 속도에 적용하지 않는다.
            var scaled = Step
            (
                model, null,
                Scale("Scale", MotionChannel.Planar, 50, 0f),
                MotionModifierCommand.Apply(Key("Max"), 60, NumericFOperation.MAX, MotionChannelValues.Uniform(MotionChannel.Planar, 9f))
            );

            Assert.That(scaled.PlanarMode, Is.EqualTo(MotionChannelMode.Free));
            AssertVector(Vector3.zero, scaled.Impulse);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> 충격은 한 Tick만 적용되고 Free 채널에서는 되돌리지 않으며
        /// <br/> 뒤에 보이는 SET이 있으면 가려집니다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_MotionModel_충격은한Tick과가려짐()
        {
            var model = new MotionModel(actor, Vector3.up);
            var up = new MotionChannelValues(MotionChannel.Elevation, elevation: 5f);

            AssertVector(new Vector3(0f, 5f, 0f), Step(model, null, MotionModifierCommand.Impulse(Key("Jump"), 0, up)).Impulse);
            AssertVector(Vector3.zero, Step(model).Impulse);
            Assert.That(IsActive(model, Key("Jump")), Is.False);

            // Set 채널에서는 그 Tick의 값에 더해진다.
            var shove = MotionModifierCommand.Impulse(Key("Shove"), 0, Planar(new Vector3(1f, 0f, 0f)));

            AssertVector(new Vector3(3f, 0f, 0f), Step(model, walk, shove).Planar);
            AssertVector(new Vector3(2f, 0f, 0f), Step(model, walk).Planar);

            // 뒤의 SET이 가리면 적용되지 않는다.
            var hidden = Step(model, null, shove, Set("Dash", 10, new Vector3(9f, 0f, 0f)));

            AssertVector(new Vector3(9f, 0f, 0f), hidden.Planar);
            AssertVector(Vector3.zero, hidden.Impulse);

            // 한 Tick의 충격은 키마다 하나이므로 여러 충격은 서로 다른 키로 넣어 합친다.
            var separate = new MotionModel(actor, Vector3.up);
            var both = Step
            (
                separate, null,
                MotionModifierCommand.Impulse(Key("JumpA"), 0, new MotionChannelValues(MotionChannel.Elevation, elevation: 3f)),
                MotionModifierCommand.Impulse(Key("JumpB"), 0, new MotionChannelValues(MotionChannel.Elevation, elevation: 4f))
            );

            AssertVector(new Vector3(0f, 7f, 0f), both.Impulse);

            // 같은 키·세대의 지속 항목을 충격으로 바꾸는 적용은 거절된다.
            Step(separate, null, MotionModifierCommand.Apply(Key("Push"), 0, NumericFOperation.ADD, up));

            var statuses = StepWithStatuses(separate, null, MotionModifierCommand.Impulse(Key("Push"), 0, up));

            Assert.That(statuses[0], Is.EqualTo(MotionInputStatus.InvalidInput));
            Assert.That(IsActive(separate, Key("Push")), Is.True);
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// Free 채널의 Planar 증분은 Up 성분을 빼고, SUB는 반대 방향으로 넘깁니다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_MotionModel_Free채널Planar투영과SUB부호()
        {
            var model = new MotionModel(actor, Vector3.up);

            // Elevation 채널을 켜지 않은 Planar 값의 Up 성분은 증분에 들어가지 않는다.
            AssertVector(new Vector3(3f, 0f, 0f), Step(model, null, Add("Push", 0, new Vector3(3f, 5f, 0f))).Impulse);
            AssertVector
            (
                new Vector3(1f, 0f, 2f),
                Step(model, null, MotionModifierCommand.Impulse(Key("Shove"), 0, Planar(new Vector3(1f, 4f, 2f)))).Impulse
            );

            var pull = MotionModifierCommand.Apply(Key("Pull"), 0, NumericFOperation.SUB, Planar(new Vector3(2f, 0f, 0f)));

            AssertVector(new Vector3(-2f, 0f, 0f), Step(model, null, pull).Impulse);
        }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// Set 채널에서 가려졌던 ADD는 반영하지 않았으므로 Free로 바뀔 때 증분으로 넘깁니다.
        /// </summary>
        // --------------------------------------------------------------------------------
        [Test]
        public void TEST_MotionModel_가려진ADD는반영몫에서제외()
        {
            var model = new MotionModel(actor, Vector3.up);

            var dashed = Step(model, walk, Add("Push", 0, new Vector3(0f, 0f, 3f)), Set("Dash", 10, new Vector3(9f, 0f, 0f)));

            AssertVector(new Vector3(9f, 0f, 0f), dashed.Planar);

            var released = Step(model, null, MotionModifierCommand.End(Key("Dash")));

            Assert.That(released.PlanarMode, Is.EqualTo(MotionChannelMode.Free));
            AssertVector(new Vector3(0f, 0f, 3f), released.Impulse);
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// 채널이 다른 보이는 SET이 여럿이면 배열에서 가장 뒤의 항목이 현재 동작입니다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_MotionModel_채널이다른SET중가장뒤가동작()
        {
            var turn = new MotionChannelValues(MotionChannel.Rotation, rotation: new Vector3(0f, 1f, 0f));
            var low = new MotionModel(actor, Vector3.up);
            var high = new MotionModel(actor, Vector3.up);

            var a = Step
            (
                low, null,
                Set("Dash", 10, new Vector3(9f, 0f, 0f), actionID: 1),
                MotionModifierCommand.Apply(Key("Turn"), 20, NumericFOperation.SET, turn, actionID: 2)
            );
            var b = Step
            (
                high, null,
                Set("Dash", 30, new Vector3(9f, 0f, 0f), actionID: 1),
                MotionModifierCommand.Apply(Key("Turn"), 20, NumericFOperation.SET, turn, actionID: 2)
            );

            Assert.That(a.ActionID, Is.EqualTo(2));
            Assert.That(b.ActionID, Is.EqualTo(1));
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> 속도를 남기고 끝내면 Free 채널에 되돌리는 충격이 없고,
        /// <br/> Set 채널에서 Free로 바뀌어도 그 항목의 몫이 Body에 남습니다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_MotionModel_속도를남기는종료()
        {
            var model = new MotionModel(actor, Vector3.up);

            AssertVector(new Vector3(3f, 0f, 0f), Step(model, null, Add("Push", 0, new Vector3(3f, 0f, 0f))).Impulse);
            AssertVector(Vector3.zero, Step(model, null, MotionModifierCommand.End(Key("Push"), keepVelocity: true)).Impulse);
            Assert.That(IsActive(model, Key("Push")), Is.False);

            // 플랫폼 위를 걷다가(상대 속도는 의도, 플랫폼 속도는 ADD) 의도 없이 떠나는 경우.
            var carried = new MotionModel(actor, Vector3.up);
            var dropped = new MotionModel(actor, Vector3.up);

            foreach (var target in Of(carried, dropped))
            {
                AssertVector(new Vector3(2f, 0f, 3f), Step(target, walk, Add("Carrier", 60, new Vector3(0f, 0f, 3f))).Planar);
            }

            var kept = Step(carried, null, MotionModifierCommand.End(Key("Carrier"), keepVelocity: true));
            var lost = Step(dropped, null, MotionModifierCommand.End(Key("Carrier")));

            Assert.That(kept.PlanarMode, Is.EqualTo(MotionChannelMode.Free));
            AssertVector(Vector3.zero, kept.Impulse);
            AssertVector(new Vector3(0f, 0f, -3f), lost.Impulse);

            // 가려져 반영하지 않은 항목은 남길 몫이 없다.
            var hidden = new MotionModel(actor, Vector3.up);

            Step(hidden, walk, Add("Push", 0, new Vector3(3f, 0f, 0f)), Set("Dash", 10, new Vector3(9f, 0f, 0f)));

            var released = Step(hidden, null, MotionModifierCommand.End(Key("Push"), keepVelocity: true), MotionModifierCommand.End(Key("Dash")));

            AssertVector(Vector3.zero, released.Impulse);

            // 보이다가 가려진 항목은 가려진 동안 반영한 몫이 0이므로 남길 몫도 0이다.
            var covered = new MotionModel(actor, Vector3.up);

            AssertVector(new Vector3(3f, 0f, 0f), Step(covered, null, Add("Push", 0, new Vector3(3f, 0f, 0f))).Impulse);

            Step(covered, null, Set("Dash", 10, new Vector3(9f, 0f, 0f)));

            var uncovered = Step(covered, null, MotionModifierCommand.End(Key("Push"), keepVelocity: true), MotionModifierCommand.End(Key("Dash")));

            AssertVector(Vector3.zero, uncovered.Impulse);

            // 같은 세대로 ADD를 MUL로 바꾸면 그 항목이 반영한 몫도 0이 되므로 남길 몫이 없다.
            var changed = new MotionModel(actor, Vector3.up);
            var scale = MotionModifierCommand.Apply(Key("Push"), 0, NumericFOperation.MUL, MotionChannelValues.Uniform(MotionChannel.Planar, 2f));

            Step(changed, null, Add("Push", 0, new Vector3(3f, 0f, 0f)));

            AssertVector(new Vector3(-3f, 0f, 0f), Step(changed, null, scale).Impulse);
            AssertVector(Vector3.zero, Step(changed, null, MotionModifierCommand.End(Key("Push"), keepVelocity: true)).Impulse);
            AssertVector(Vector3.zero, Step(changed).Impulse);
        }

    #endregion

    #region U: Step별 Up 축

        // ----------------------------------------------------------------------
        /// <summary>
        /// Step 입력으로 Up을 바꾸면 그 Tick부터 새 Up으로 나누고, 비우면 유지합니다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_MotionModel_Step별Up변경()
        {
            var model = new MotionModel(actor, Vector3.up);
            var lift = Of(new MotionIntent(MotionChannel.Planar | MotionChannel.Elevation, new Vector3(2f, 3f, 0f)));

            var tilted = model.StepLocal(DELTA, new MotionStepInput(lift, up: new Vector3(2f, 0f, 0f)));

            AssertVector(Vector3.right, model.Up);
            AssertVector(Vector3.right, tilted.Up);
            AssertVector(new Vector3(0f, 3f, 0f), tilted.Planar);
            Assert.That(tilted.Elevation, Is.EqualTo(2f).Within(1e-5f));

            // 비운 Up은 현재 Up을 유지한다.
            Step(model, lift);

            AssertVector(Vector3.right, model.Up);

            // 잘못된 Up은 상태를 바꾸기 전에 거절한다.
            var version = model.StepVersion;

            Assert.Throws<ArgumentOutOfRangeException>
            (
                () => model.StepLocal(DELTA, new MotionStepInput(lift, up: new Vector3(float.NaN, 1f, 0f)))
            );
            AssertVector(Vector3.right, model.Up);
            Assert.That(model.StepVersion, Is.EqualTo(version));

            // 입력 버퍼도 Up을 넘기고 Clear하면 비운다.
            var buffer = new MotionInputBuffer();

            buffer.SetUp(Vector3.forward);

            Assert.That(buffer.AsStepInput().Up, Is.EqualTo(Vector3.forward));

            buffer.Clear();

            Assert.That(buffer.AsStepInput().Up, Is.EqualTo(Vector3.zero));
        }

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> Up이 바뀌어도 Free 채널 증분의 합은 지속 ADD 합과 같아
        /// <br/> 가짜 충격이 쌓이지 않습니다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_MotionModel_Up변경시Free채널증분일치()
        {
            var model = new MotionModel(actor, Vector3.up);

            // 직접 지정한 Elevation은 Up 방향 크기라 Up을 따라 돈다.
            var push = MotionModifierCommand.Apply
            (
                Key("Push"), 0, NumericFOperation.ADD,
                new MotionChannelValues(MotionChannel.Planar | MotionChannel.Elevation, new Vector3(3f, 0f, 0f), 2f)
            );

            var body = Step(model, null, push).Impulse;

            AssertVector(new Vector3(3f, 2f, 0f), body);

            // Up이 X가 되면 Planar 값 (3,0,0)은 평면에서 사라지고 Elevation 2는 X 방향이 된다.
            body += model.StepLocal(DELTA, new MotionStepInput(up: Vector3.right)).Impulse;

            AssertVector(new Vector3(2f, 0f, 0f), body);

            body += Step(model, null, MotionModifierCommand.End(Key("Push"))).Impulse;

            AssertVector(Vector3.zero, body);

            // 월드 선속도(Linear)는 Up이 바뀌어도 월드 방향이 그대로라 충격이 없다.
            var world = new MotionModel(actor, Vector3.up);
            var wind = MotionModifierCommand.Apply
            (
                Key("Wind"), 0, NumericFOperation.ADD, MotionChannelValues.Linear(MotionChannel.Planar | MotionChannel.Elevation, new Vector3(3f, 2f, 0f))
            );

            AssertVector(new Vector3(3f, 2f, 0f), Step(world, null, wind).Impulse);
            AssertVector(Vector3.zero, world.StepLocal(DELTA, new MotionStepInput(up: Vector3.right)).Impulse);

            // Up을 바꾸는 Tick에 넣은 월드 선속도도 새 Up으로 나뉜다.
            var tilted = new MotionModel(actor, Vector3.up);
            var dash = MotionModifierCommand.Apply
            (
                Key("Dash"), 0, NumericFOperation.SET, MotionChannelValues.Linear(MotionChannel.Planar | MotionChannel.Elevation, new Vector3(3f, 2f, 0f))
            );
            var frame = tilted.StepLocal(DELTA, new MotionStepInput(modifiers: Of(dash), up: Vector3.right));

            AssertVector(new Vector3(0f, 2f, 0f), frame.Planar);
            Assert.That(frame.Elevation, Is.EqualTo(3f).Within(1e-5f));
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> 이미 있는 월드 선속도 항목은 Up이 바뀐 Tick에 새 Up으로 다시 나뉘고,
        /// <br/> 같은 세대 갱신과 Elevation만 켠 월드 값도 같은 규칙을 따릅니다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_MotionModel_월드선속도는Up변경에다시나뉨()
        {
            var model = new MotionModel(actor, Vector3.up);
            var push = MotionModifierCommand.Apply
            (
                Key("Push"), 0, NumericFOperation.SET, MotionChannelValues.Linear(MotionChannel.Planar | MotionChannel.Elevation, new Vector3(3f, 4f, 5f))
            );

            var upright = Step(model, null, push);

            AssertVector(new Vector3(3f, 0f, 5f), upright.Planar);
            Assert.That(upright.Elevation, Is.EqualTo(4f).Within(1e-5f));

            // 새 명령 없이 Up만 바꿔도 이미 있는 항목이 새 Up으로 나뉜다.
            var tilted = model.StepLocal(DELTA, new MotionStepInput(up: Vector3.right));

            AssertVector(new Vector3(0f, 4f, 5f), tilted.Planar);
            Assert.That(tilted.Elevation, Is.EqualTo(3f).Within(1e-5f));

            // 같은 세대 갱신도 현재 Up으로 나뉜다.
            var refresh = MotionModifierCommand.Apply
            (
                Key("Push"), 0, NumericFOperation.SET, MotionChannelValues.Linear(MotionChannel.Planar | MotionChannel.Elevation, new Vector3(1f, 2f, 0f))
            );
            var refreshed = Step(model, null, refresh);

            AssertVector(new Vector3(0f, 2f, 0f), refreshed.Planar);
            Assert.That(refreshed.Elevation, Is.EqualTo(1f).Within(1e-5f));

            // Elevation만 켠 월드 값은 선속도를 보관해 Up 방향 성분만 쓴다.
            var lift = new MotionModel(actor, Vector3.up);
            var rise = MotionModifierCommand.Apply
            (
                Key("Rise"), 0, NumericFOperation.SET, MotionChannelValues.Linear(MotionChannel.Elevation, new Vector3(3f, 4f, 5f))
            );
            var risen = Step(lift, null, rise);

            Assert.That(risen.PlanarMode, Is.EqualTo(MotionChannelMode.Free));
            Assert.That(risen.Elevation, Is.EqualTo(4f).Within(1e-5f));
            Assert.That(lift.TryGetModifier(Key("Rise"), out var state) && state.Values.IsWorldLinear, Is.True);
            AssertVector(new Vector3(3f, 4f, 5f), state.Values.Planar);
            Assert.That(lift.StepLocal(DELTA, new MotionStepInput(up: Vector3.right)).Elevation, Is.EqualTo(3f).Within(1e-5f));

            // 그 선속도가 유한하지 않으면 거절한다.
            var broken = MotionModifierCommand.Apply
            (
                Key("Broken"), 0, NumericFOperation.SET, MotionChannelValues.Linear(MotionChannel.Elevation, new Vector3(float.NaN, 1f, 0f))
            );

            Assert.That(StepWithStatuses(lift, null, broken)[0], Is.EqualTo(MotionInputStatus.InvalidInput));
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// Up도 Snapshot에 들어가 복원 뒤 같은 입력으로 재실행하면 같은 결과가 나옵니다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_MotionModel_Up복원후재실행일치()
        {
            var model = new MotionModel(actor, Vector3.up);

            Step(model, null, Add("Push", 0, new Vector3(3f, 2f, 0f)));

            var snapshot = model.Capture();
            var first = model.StepLocal(DELTA, new MotionStepInput(walk, up: Vector3.right));
            var second = Step(model, walk);

            model.Restore(snapshot);

            AssertVector(Vector3.up, model.Up);

            var replayFirst = model.StepLocal(DELTA, new MotionStepInput(walk, up: Vector3.right));
            var replaySecond = Step(model, walk);

            Assert.That(replayFirst.Planar, Is.EqualTo(first.Planar));
            Assert.That(replayFirst.Impulse, Is.EqualTo(first.Impulse));
            Assert.That(replaySecond.Elevation, Is.EqualTo(second.Elevation));
            Assert.That(replaySecond.Impulse, Is.EqualTo(second.Impulse));
        }

    #endregion

    #region I: 입력 검증과 용량

        // ------------------------------------------------------------
        /// <summary>
        /// 같은 키의 세대 규칙과 잘못된 입력을 입력별 결과로 알립니다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_MotionModel_세대규칙과입력검증()
        {
            var model = new MotionModel(actor, Vector3.up);
            var linear = Planar(new Vector3(1f, 0f, 0f));
            var intents = Of(new MotionIntent(MotionChannel.None, Vector3.right));
            var modifiers = Of
            (
                MotionModifierCommand.Apply(Key("A", 1), 0, NumericFOperation.SET, linear),
                MotionModifierCommand.Apply(Key("A", 1), 0, NumericFOperation.SET, linear),
                MotionModifierCommand.Apply(Key("A", 2), 0, NumericFOperation.SET, linear),
                MotionModifierCommand.Apply(Key("A", 1), 0, NumericFOperation.SET, linear),
                MotionModifierCommand.End(Key("A", 1)),
                MotionModifierCommand.Apply(Key("Hidden"), 0, NumericFOperation.ADD, new MotionChannelValues(MotionChannel.Planar, Vector3.one, float.NaN)),
                MotionModifierCommand.Apply(default, 0, NumericFOperation.SET, linear),
                MotionModifierCommand.Apply(Key("B"), 0, NumericFOperation.SET, default),
                MotionModifierCommand.Apply(Key("C"), 0, NumericFOperation.SET, Planar(new Vector3(float.NaN, 0f, 0f))),
                MotionModifierCommand.Apply(Key("D"), 0, (NumericFOperation)99, linear),
                MotionModifierCommand.Apply(Key("E"), 0, NumericFOperation.SET, new MotionChannelValues((MotionChannel)64, Vector3.one)),
                MotionModifierCommand.Apply(Key("F"), 0, NumericFOperation.SET, new MotionChannelValues(MotionChannel.Elevation, elevation: float.NaN)),
                MotionModifierCommand.Apply
                (
                    Key("G"), 0, NumericFOperation.SET,
                    new MotionChannelValues(MotionChannel.Rotation, rotation: new Vector3(0f, float.PositiveInfinity, 0f))
                ),
                MotionModifierCommand.Apply(Key("A", 2), 5, NumericFOperation.SET, linear),
                MotionModifierCommand.Apply(Key("A", 2), 0, NumericFOperation.SET, new MotionChannelValues(MotionChannel.All, Vector3.one))
            );
            var intentStatuses = new MotionInputStatus[intents.Length];
            var modifierStatuses = new MotionInputStatus[modifiers.Length];

            model.StepLocal(DELTA, new MotionStepInput(intents, modifiers), new MotionStepStatuses(intentStatuses, modifierStatuses));

            Assert.That(intentStatuses[0], Is.EqualTo(MotionInputStatus.InvalidInput));
            Assert.That(modifierStatuses[0], Is.EqualTo(MotionInputStatus.Accepted));
            Assert.That(modifierStatuses[1], Is.EqualTo(MotionInputStatus.Updated));
            Assert.That(modifierStatuses[2], Is.EqualTo(MotionInputStatus.Accepted));
            Assert.That(modifierStatuses[3], Is.EqualTo(MotionInputStatus.StaleGeneration));
            Assert.That(modifierStatuses[4], Is.EqualTo(MotionInputStatus.NotFound));

            // 켜지 않은 채널의 값은 검사하지 않는다.
            Assert.That(modifierStatuses[5], Is.EqualTo(MotionInputStatus.Accepted));

            for (var index = 6; index < modifierStatuses.Length; index++)
            {
                Assert.That(modifierStatuses[index], Is.EqualTo(MotionInputStatus.InvalidInput), $"modifier {index}");
            }

            Assert.That(IsActive(model, Key("A", 1)), Is.False);

            // 켜지 않은 채널의 값은 0으로 보관한다.
            Assert.That(model.TryGetModifier(Key("Hidden"), out var hidden), Is.True);
            Assert.That(hidden.Values.Elevation, Is.EqualTo(0f));
            AssertVector(Vector3.one, hidden.Values.Planar);

            // 같은 세대 갱신에서도 켜지 않은 채널의 값은 0으로 보관한다.
            var refresh = MotionModifierCommand.Apply
            (
                Key("Hidden"), 0, NumericFOperation.ADD,
                new MotionChannelValues(MotionChannel.Planar, Vector3.one, float.PositiveInfinity, new Vector3(float.NaN, 0f, 0f))
            );

            Assert.That(StepWithStatuses(model, null, refresh)[0], Is.EqualTo(MotionInputStatus.Updated));
            Assert.That(model.TryGetModifier(Key("Hidden"), out hidden), Is.True);
            Assert.That(hidden.Values.Elevation, Is.EqualTo(0f));
            AssertVector(Vector3.zero, hidden.Values.Rotation);

            // 같은 세대로 Order·채널을 바꾸는 적용은 거절되어 기존 항목이 그대로다.
            Assert.That(model.TryGetModifier(Key("A", 2), out var state), Is.True);
            Assert.That(state.Order, Is.EqualTo(0));
            Assert.That(state.Values.Channels, Is.EqualTo(MotionChannel.Planar));
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 같은 세대 갱신은 시작 Tick과 시작 순서를 유지합니다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_MotionModel_같은세대갱신은시작Tick유지()
        {
            var model = new MotionModel(actor, Vector3.up);

            // 0이 아닌 Tick에서 시작해 시작 Tick이 실제 시작 시점인지 확인한다.
            Step(model, walk);
            Step(model, walk);

            var first = Step(model, walk, Set("Dash", 10, new Vector3(9f, 0f, 0f), actionID: 7));

            Assert.That(first.Tick.Index, Is.EqualTo(2));
            Assert.That(first.ActionStartTick, Is.EqualTo(2));

            Step(model, walk);

            var refreshed = Step(model, walk, Set("Dash", 10, new Vector3(5f, 0f, 0f), actionID: 8));

            AssertVector(new Vector3(5f, 0f, 0f), refreshed.Planar);
            Assert.That(refreshed.ActionID, Is.EqualTo(8));
            Assert.That(refreshed.ActionStartTick, Is.EqualTo(first.Tick.Index));

            // 같은 Order의 나중 항목보다 앞 순서도 유지되어 여전히 가려진다.
            Step(model, walk, Set("Later", 10, new Vector3(1f, 0f, 0f)));

            AssertVector(new Vector3(1f, 0f, 0f), Step(model, walk, Set("Dash", 10, new Vector3(6f, 0f, 0f))).Planar);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 용량을 넘는 항목은 기존 항목을 건드리지 않고 거절합니다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_MotionModel_용량초과거절()
        {
            var model = new MotionModel(actor, Vector3.up, capacity: 2);
            var statuses = StepWithStatuses(model, null, Add("A", 0, Vector3.right), Add("B", 0, Vector3.right), Add("C", 0, Vector3.right));

            Assert.That(statuses[2], Is.EqualTo(MotionInputStatus.CapacityReached));
            Assert.That(IsActive(model, Key("A")), Is.True);
            Assert.That(IsActive(model, Key("B")), Is.True);
            Assert.That(IsActive(model, Key("C")), Is.False);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 잘못된 Tick과 결과 버퍼는 상태를 바꾸기 전에 거절합니다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_MotionModel_Tick검증은상태변경전거절()
        {
            var model = new MotionModel(actor, Vector3.up);

            model.Step(new MotionTick(5, DELTA), new MotionStepInput(walk));

            var version = model.StepVersion;
            var twoIntents = Of(walk[0], walk[0]);

            Assert.Throws<ArgumentOutOfRangeException>(() => model.Step(default, new MotionStepInput(walk)));
            Assert.Throws<ArgumentOutOfRangeException>(() => model.Step(new MotionTick(5, DELTA), new MotionStepInput(walk)));
            Assert.Throws<ArgumentOutOfRangeException>(() => model.Step(new MotionTick(3, DELTA), new MotionStepInput(walk)));
            Assert.Throws<ArgumentException>
            (
                () => model.Step(new MotionTick(6, DELTA), new MotionStepInput(twoIntents), new MotionStepStatuses(new MotionInputStatus[1]))
            );
            Assert.Throws<ArgumentOutOfRangeException>(() => model.StepLocal(0d, new MotionStepInput(walk)));

            Assert.That(model.LastTick, Is.EqualTo(5));
            Assert.That(model.StepVersion, Is.EqualTo(version));
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Clear는 모든 항목을 끝냅니다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_MotionModel_Clear는모든항목종료()
        {
            var model = new MotionModel(actor, Vector3.up);

            Step(model, walk, Add("A", 0, Vector3.forward), Set("B", 10, Vector3.forward));

            AssertVector(new Vector3(2f, 0f, 0f), Step(model, walk, MotionModifierCommand.Clear()).Planar);
            Assert.That(IsActive(model, Key("A")), Is.False);
            Assert.That(IsActive(model, Key("B")), Is.False);
        }

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
            var ladder = Key("Ladder");
            var climb = new MotionChannelValues(MotionChannel.All, Vector3.zero, 1f, Vector3.zero);

            var lease = buffer.AcquireModifier(MotionModifierCommand.Apply(ladder, 50, NumericFOperation.SET, climb));

            buffer.Add(walk[0]);
            buffer.Add(MotionModifierCommand.Apply(Key("Turn"), 1, NumericFOperation.SET, new MotionChannelValues(MotionChannel.Rotation)));

            Assert.That(buffer.Count, Is.EqualTo(3));

            var frame = model.StepLocal(DELTA, buffer.AsStepInput());

            AssertVector(Vector3.zero, frame.Planar);
            Assert.That(frame.Elevation, Is.EqualTo(1f).Within(1e-5f));

            buffer.Clear();

            Assert.That(IsActive(model, ladder), Is.True);

            lease.Release();

            Assert.That(buffer.Count, Is.EqualTo(1));

            model.StepLocal(DELTA, buffer.AsStepInput());

            Assert.That(IsActive(model, ladder), Is.False);
            Assert.Throws<ArgumentException>(() => buffer.AcquireModifier(MotionModifierCommand.End(ladder)));
            Assert.Throws<ArgumentException>(() => buffer.AcquireModifier(MotionModifierCommand.Impulse(ladder, 0, climb)));
        }

    #endregion

    #region R: Snapshot 복원과 결정성

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> Snapshot으로 되돌려 같은 입력을 재실행하면 같은 결과가 나옵니다.
        /// <br/> Snapshot 뒤 항목이 생기거나 끝나도 같습니다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_MotionModel_Snapshot복원후재실행일치()
        {
            var model = new MotionModel(actor, Vector3.up);

            Step
            (
                model, walk,
                Set("Dash", 5, new Vector3(6f, 0f, 0f), actionID: 2),
                Scale("Slow", MotionChannel.Planar, 7, 0.5f),
                Add("Knockback", 9, new Vector3(0f, 0f, 8f))
            );

            var snapshot = model.Capture();
            var version = model.StepVersion;
            var inputs = Of
            (
                Of(Add("Knockback", 9, new Vector3(0f, 0f, 4f))),
                Of(Add("Late", 20, new Vector3(1f, 0f, 0f)), MotionModifierCommand.End(Key("Slow"))),
                Of(MotionModifierCommand.End(Key("Dash"))),
                Array.Empty<MotionModifierCommand>()
            );
            var original = new MotionFrame[inputs.Length];

            for (var index = 0; index < original.Length; index++)
            {
                original[index] = Step(model, walk, inputs[index]);
            }

            model.Restore(snapshot);

            Assert.That(model.LastTick, Is.EqualTo(snapshot.LastTick));
            Assert.That(model.StepVersion, Is.GreaterThan(version + original.Length));

            for (var index = 0; index < original.Length; index++)
            {
                var replayed = Step(model, walk, inputs[index]);

                Assert.That(replayed.Tick.Index, Is.EqualTo(original[index].Tick.Index));
                Assert.That(replayed.Planar, Is.EqualTo(original[index].Planar));
                Assert.That(replayed.Impulse, Is.EqualTo(original[index].Impulse));
                Assert.That(replayed.ActionID, Is.EqualTo(original[index].ActionID));
                Assert.That(replayed.StepVersion, Is.Not.EqualTo(original[index].StepVersion));
            }

            // 같은 용량 Snapshot은 재사용할 수 있다.
            model.Capture(snapshot);

            Assert.That(snapshot.LastTick, Is.EqualTo(model.LastTick));
        }

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> Free 채널에 이미 반영한 몫도 Snapshot에 들어가
        /// <br/> 복원 뒤 재실행해도 같은 증분을 넘깁니다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_MotionModel_반영몫복원후재실행일치()
        {
            var model = new MotionModel(actor, Vector3.up);
            var push = MotionModifierCommand.Apply
            (
                Key("Push"), 0, NumericFOperation.ADD,
                MotionChannelValues.Linear(MotionChannel.Planar | MotionChannel.Elevation, new Vector3(2f, 1f, 0f))
            );

            Step(model, walk, push);

            var snapshot = model.Capture();
            var inputs = Of
            (
                Array.Empty<MotionModifierCommand>(),
                Of(Add("Push", 0, new Vector3(4f, 0f, 0f))),
                Of(MotionModifierCommand.End(Key("Push")))
            );
            var original = new MotionFrame[inputs.Length];

            for (var index = 0; index < original.Length; index++)
            {
                original[index] = Step(model, null, inputs[index]);
            }

            // 첫 Free Tick은 Set 채널에서 반영한 몫을 다시 더하지 않는다.
            AssertVector(Vector3.zero, original[0].Impulse);

            model.Restore(snapshot);

            for (var index = 0; index < original.Length; index++)
            {
                Assert.That(Step(model, null, inputs[index]).Impulse, Is.EqualTo(original[index].Impulse));
            }
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Restore로 되살아난 항목도 이후 갱신이 합성에 반영됩니다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_MotionModel_복원된항목갱신반영()
        {
            var model = new MotionModel(actor, Vector3.up);

            Step(model, walk, Add("Wind", 0, new Vector3(0f, 0f, 1f)));

            var snapshot = model.Capture();

            Step(model, walk, MotionModifierCommand.End(Key("Wind")));
            model.Restore(snapshot);

            AssertVector(new Vector3(2f, 0f, 1f), Step(model, walk).Planar);
            AssertVector(new Vector3(2f, 0f, 3f), Step(model, walk, Add("Wind", 0, new Vector3(0f, 0f, 3f))).Planar);
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
            var knockback = new Vector3(0f, 3f, 2f);

            for (var index = 0; index < 5; index++)
            {
                // 사용처가 매 Tick 감쇠한 값을 같은 세대로 넣는다.
                var values = MotionChannelValues.Linear(MotionChannel.Planar | MotionChannel.Elevation, knockback);
                var commands = Of
                (
                    Set("Dash", 5, new Vector3(6f, 0f, 1f)),
                    MotionModifierCommand.Apply(Key("Knockback"), 9, NumericFOperation.ADD, values)
                );

                var a = Step(server, walk, commands);
                var b = Step(client, walk, commands);

                Assert.That(b.Planar, Is.EqualTo(a.Planar));
                Assert.That(b.Elevation, Is.EqualTo(a.Elevation));
                Assert.That(b.Impulse, Is.EqualTo(a.Impulse));

                knockback *= Mathf.Exp(-2f * (float)DELTA);
            }
        }

    #endregion

    #region D: 사용처 도구

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> Track은 Body 회전 기준 변위를 속도로 평가하고,
        /// <br/> 사용처가 재생 시간을 가지며 매 Tick 같은 세대로 값을 넣습니다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_MotionModel_Track평가값을사용처가갱신()
        {
            var positions = Of(Vector3.zero, new Vector3(0f, 0f, 1f), new Vector3(0f, 0f, 2f));
            var yaw = Of(0f, 9f, 18f);
            var track = new MotionTrack(10f, positions, yaw);
            var model = new MotionModel(actor, Vector3.up);
            var half = Mathf.PI * 0.25f;
            var facing = new Quaternion(0f, Mathf.Sin(half), 0f, Mathf.Cos(half));
            var delta = (float)DELTA;

            Assert.Throws<ArgumentOutOfRangeException>(() => track.EvaluateVelocity(0f, 0.1f, 0f, facing, out _, out _));

            var time = 0f;
            var startTick = -1L;

            while (time < track.Length)
            {
                track.EvaluateVelocity(time, time + delta, delta, facing, out var linear, out var angular);

                var values = MotionChannelValues.Linear(MotionChannel.Planar | MotionChannel.Rotation, linear, angular);
                var frame = Step(model, walk, MotionModifierCommand.Apply(Key("Attack"), 5, NumericFOperation.SET, values, actionID: 4));

                AssertVector(new Vector3(10f, 0f, 0f), frame.Planar, 1e-3f);
                AssertVector(new Vector3(0f, 90f * Mathf.Deg2Rad, 0f), frame.Rotation, 1e-3f);
                Assert.That(frame.ActionID, Is.EqualTo(4));

                startTick = startTick < 0 ? frame.ActionStartTick : startTick;

                Assert.That(frame.ActionStartTick, Is.EqualTo(startTick));

                time += delta;
            }

            // Track이 끝나면 사용처가 End로 끝낸다.
            AssertVector(new Vector3(2f, 0f, 0f), Step(model, walk, MotionModifierCommand.End(Key("Attack"))).Planar);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 차단 법선 쪽 성분만 제거하고 벽에서 멀어지는 성분은 둡니다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_MotionModel_차단법선성분제거()
        {
            var wall = MotionFeedback.None.WithBlockingNormal(new Vector3(-2f, 0f, 0f));

            AssertVector(new Vector3(0f, 0f, 4f), wall.RemoveBlocked(new Vector3(3f, 0f, 4f)));
            AssertVector(new Vector3(-3f, 0f, 4f), wall.RemoveBlocked(new Vector3(-3f, 0f, 4f)));
            AssertVector(new Vector3(3f, 0f, 4f), MotionFeedback.None.RemoveBlocked(new Vector3(3f, 0f, 4f)));

            // 두 벽이 모두 막으면 두 성분 모두 제거한다.
            var corner = wall.WithBlockingNormal(new Vector3(0f, 0f, -1f));

            AssertVector(Vector3.zero, corner.RemoveBlocked(new Vector3(3f, 0f, 4f)));
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> Up 정렬 각속도로 delta초 돌리면 Body의 Up이 목표 Up이 되고,
        /// <br/> 이미 맞으면 0, 정반대면 Forward를 축으로 반 바퀴 회전합니다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_MotionModel_Up정렬각속도()
        {
            var delta = (float)DELTA;

            AssertVector(Vector3.zero, MotionOrientation.AlignUp(Quaternion.identity, Vector3.up, delta));

            // Y에서 X로: -Z 축으로 90도.
            AssertVector(new Vector3(0f, 0f, -Mathf.PI * 0.5f / delta), MotionOrientation.AlignUp(Quaternion.identity, Vector3.right, delta));

            // 정반대면 Forward를 축으로 반 바퀴 돌아 바라보던 방향을 유지한다.
            AssertVector(new Vector3(0f, 0f, Mathf.PI / delta), MotionOrientation.AlignUp(Quaternion.identity, Vector3.down, delta), 1e-3f);

            // 임의 회전에서도 정반대 Up으로 뒤집고 Forward는 그대로이며, 일반 목표에도 Up이 맞춰진다.
            foreach (var raw in Of(new Quaternion(0.3f, 0.5f, -0.2f, 0.78f), new Quaternion(-0.6f, 0.1f, 0.4f, 0.3f), new Quaternion(0.2f, -0.7f, 0.5f, 0.1f)))
            {
                var body = Unit(raw);
                var bodyUp = body * Vector3.up;
                var bodyForward = body * Vector3.forward;
                var flipTurn = Turn(MotionOrientation.AlignUp(body, -bodyUp, delta) * delta);

                AssertVector(-bodyUp, flipTurn * bodyUp, 1e-3f);
                AssertVector(bodyForward, flipTurn * bodyForward, 1e-3f);

                var tilted = new Vector3(0.2f, 0.9f, -0.4f).normalized;

                AssertVector(tilted, Turn(MotionOrientation.AlignUp(body, tilted, delta) * delta) * bodyUp, 1e-3f);
            }

            // 크기가 1이 아닌 회전은 정규화해 쓴다(Z축 90도를 두 배 크기로).
            var half = Mathf.Sqrt(0.5f);
            var unit = new Quaternion(0f, 0f, half, half);
            var scaled = new Quaternion(0f, 0f, half * 2f, half * 2f);

            AssertVector(MotionOrientation.AlignUp(unit, Vector3.up, delta), MotionOrientation.AlignUp(scaled, Vector3.up, delta), 1e-3f);

            Assert.Throws<ArgumentOutOfRangeException>(() => MotionOrientation.AlignUp(Quaternion.identity, Vector3.up, 0f));
            Assert.Throws<ArgumentOutOfRangeException>(() => MotionOrientation.AlignUp(Quaternion.identity, Vector3.right, 1e-7f));
            Assert.Throws<ArgumentOutOfRangeException>(() => MotionOrientation.AlignUp(Quaternion.identity, Vector3.zero, delta));
            Assert.Throws<ArgumentOutOfRangeException>(() => MotionOrientation.AlignUp(Quaternion.identity, new Vector3(float.NaN, 1f, 0f), delta));
            var zero = Assert.Throws<ArgumentOutOfRangeException>(() => MotionOrientation.AlignUp(new Quaternion(0f, 0f, 0f, 0f), Vector3.up, delta));

            Assert.That(zero.ParamName, Is.EqualTo("rotation"));
            Assert.Throws<ArgumentOutOfRangeException>(() => MotionOrientation.AlignUp(new Quaternion(float.NaN, 0f, 0f, 1f), Vector3.up, delta));
        }

    #endregion

    }
}
