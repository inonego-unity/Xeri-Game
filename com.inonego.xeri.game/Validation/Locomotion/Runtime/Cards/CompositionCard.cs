/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : CompositionCard.cs
수정일 : 2026-11-22

# 설명
Motion 합성 카드. 걷기 의도 위에 대시(SET)·넉백(ADD, 감쇠)·스턴(MUL 0)·둔화(MUL)·바람(ADD)·
저지 불가 대시(SET)·회전(SET)을 Order와 함께 켜고 꺼서 서로 가리고 곱해지는 결과를 눈으로 확인한다.

# 특이사항, 제약사항
접기 결과는 Core 연산 규칙으로 따로 계산한 기대 Planar와 매 Tick 비교한다.
가려진 대시는 Timer가 멈춰 가려짐 정지(사용처 책임)를 보여준다.
========================================================================= BLOCK_HEADER_END */

using System.Collections.Generic;

using UnityEngine;
using UnityEngine.InputSystem;

using inonego;
using inonego.Xeri;
using inonego.Xeri.Game;
using inonego.Xeri.Game.Motion;
using inonego.Xeri.Serializable;

namespace inonego.Xeri.Game.Validation.Locomotion
{
    // ============================================================
    /// <summary>
    /// 효과 겹침(Order·가려짐·곱하기)을 확인하는 카드.
    /// </summary>
    // ============================================================
    internal sealed class CompositionCard : ValidationCard
    {

    #region 필드

        private const float WALK_SPEED = 4f;

        private ValidationEffect dash;
        private ValidationEffect knockback;
        private ValidationEffect stun;
        private ValidationEffect slow;
        private ValidationEffect wind;
        private ValidationEffect unstoppable;
        private ValidationEffect spin;

        private Vector3 intent;

        public override string Title => "1. Motion 합성";

        public override string Summary =>
            "걷기 위에 대시·넉백·스턴·둔화·바람을 Order로 겹쳐 가려짐과 곱하기가 맞는지 본다.";

        public override string Expected =>
            "높은 Order의 SET(저지 불가 60)이 대시(10)를 가리고 Timer가 멈춘다. 스턴(50)은 걷기·대시 같은 자기 조작을 0으로 만들지만, 스턴 뒤(55)에 둔 외부 힘(넉백·바람)은 그대로 받아 밀린다. 결과 Planar가 기대값과 같아 PASS가 유지된다.";

        public override string[] Controls => new[]
        {
            "WASD 걷기(의도)", "1 대시(SET 10)  2 넉백(ADD 55, 감쇠, 외부 힘)", "3 스턴(MUL 0, 50: 걷기·대시만 막음)  4 둔화(MUL 0.5, 0)",
            "5 바람(ADD 55, 외부 힘)  6 저지불가 대시(SET 60)", "7 회전(Rotation SET 5)  C 모두 끝내기(Clear)"
        };

    #endregion

    #region 구성

        protected override void BuildContent()
        {
            CreateGround(new Vector3(30f, 0f, 30f));

            actors.Add(new ValidationActor("Cube", Root.transform, new Vector3(0f, 0.6f, 0f), new Color(0.3f, 0.7f, 1f), 1));

            dash = AddEffect(new ValidationEffect("대시", "Dash", 10, NumericFOperation.SET, MotionChannel.Planar, new Color(0.3f, 0.5f, 1f))
            {
                Linear = new Vector3(0f, 0f, 10f), Duration = 0.8f, ActionID = 1, PauseWhenHidden = true
            });
            knockback = AddEffect(new ValidationEffect("넉백", "Knockback", 55, NumericFOperation.ADD, MotionChannel.Planar, new Color(1f, 0.35f, 0.3f))
            {
                Linear = new Vector3(-9f, 0f, 0f), Decay = 2.5f
            });
            stun = AddEffect(new ValidationEffect("스턴", "Stun", 50, NumericFOperation.MUL, MotionChannel.Planar | MotionChannel.Rotation, new Color(1f, 0.9f, 0.2f))
            {
                UseUniform = true, Uniform = 0f, Duration = 2f
            });
            slow = AddEffect(new ValidationEffect("둔화", "Slow", 0, NumericFOperation.MUL, MotionChannel.Planar, new Color(0.7f, 0.5f, 1f))
            {
                UseUniform = true, Uniform = 0.5f, Duration = 4f
            });
            wind = AddEffect(new ValidationEffect("바람", "Wind", 55, NumericFOperation.ADD, MotionChannel.Planar, new Color(0.4f, 1f, 0.9f))
            {
                Linear = new Vector3(0f, 0f, -3f)
            });
            unstoppable = AddEffect(new ValidationEffect("저지불가", "Unstoppable", 60, NumericFOperation.SET, MotionChannel.Planar, new Color(1f, 0.55f, 0.1f))
            {
                Linear = new Vector3(7f, 0f, 0f), Duration = 1.2f, ActionID = 3
            });
            spin = AddEffect(new ValidationEffect("회전", "Spin", 5, NumericFOperation.SET, MotionChannel.Rotation, new Color(0.9f, 0.4f, 0.9f))
            {
                Angular = new Vector3(0f, 6f, 0f), Duration = 2f, ActionID = 2
            });

            Checks.Add(new ValidationCheck("접기 일치: Planar == Core 연산 순서 접기", CheckFold));
            Checks.Add(new ValidationCheck("스턴 중: 걷기·대시는 0, 외부 힘(넉백·바람)만 남음", CheckStun));
            Checks.Add(new ValidationCheck("높은 SET이 낮은 SET을 가림 (IsVisible)", CheckHidden));
            Checks.Add(new ValidationCheck("ActionID = 가장 뒤 SET의 값", CheckAction));
        }

    #endregion

    #region 초기화

        public override void ResetCard()
        {
            base.ResetCard();

            if (Primary != null)
            {
                Primary.ResetState(At(0f, 0.6f, 0f));
            }
        }

    #endregion

    #region 입력

        public override void HandleInput()
        {
            switch (ValidationInput.Digit())
            {
                case 1:
                    dash.Start(Primary);
                    break;
                case 2:
                    knockback.Start(Primary);
                    break;
                case 3:
                    Toggle(stun);
                    break;
                case 4:
                    Toggle(slow);
                    break;
                case 5:
                    Toggle(wind);
                    break;
                case 6:
                    unstoppable.Start(Primary);
                    break;
                case 7:
                    Toggle(spin);
                    break;
            }

            if (ValidationInput.Pressed(Key.C))
            {
                ClearAll();
            }
        }

        private void ClearAll()
        {
            Primary.Buffer.Add(MotionModifierCommand.Clear());

            for (var index = 0; index < effects.Count; index++)
            {
                effects[index].MarkInactive();
            }
        }

    #endregion

    #region Tick

        public override void BeforeTick(float dt)
        {
            TickEffects(dt);

            // 걷기 의도는 항상 보내 Planar를 Set으로 둔다(키를 떼면 0이라 멈춘다).
            intent = ValidationInput.Move() * WALK_SPEED;

            Primary.Buffer.Add(new MotionIntent(MotionChannel.Planar, intent));
        }

        protected override void OnAfterTick(float dt)
        {
            // 넓은 바닥 가장자리에서 떨어지지 않게 한다.
            var position = Primary.Body.position - Origin;

            if (Mathf.Abs(position.x) > 13f || Mathf.Abs(position.z) > 13f)
            {
                Primary.Teleport(At(0f, 0.6f, 0f));
            }
        }

    #endregion

    #region 점검

        private ValidationCheckResult CheckFold()
        {
            var frame = Primary.LastFrame;

            if (!ValidationOracle.TryExpectPlanar(effects, true, intent, frame.Up, out var expected))
            {
                return ValidationCheckResult.Skip;
            }

            var error = (frame.Planar - expected).magnitude;

            return ValidationCheckResult.Of
            (
                frame.PlanarMode == MotionChannelMode.Set && error < 1e-3f,
                $"Motion {ValidationOverlay.V(frame.Planar)} / 기대 {ValidationOverlay.V(expected)}"
            );
        }

        private ValidationCheckResult CheckStun()
        {
            if (!stun.IsActive || unstoppable.IsActive)
            {
                return ValidationCheckResult.Skip;
            }

            // 스턴 뒤 Order의 지속 항목(외부 힘)만 남는다. 걷기·대시·둔화는 스턴이 0으로 곱한다.
            var expected = Vector3.zero;

            for (var index = 0; index < effects.Count; index++)
            {
                var effect = effects[index];

                if (!effect.IsActive || effect.Order <= stun.Order || (effect.Channels & MotionChannel.Planar) == 0)
                {
                    continue;
                }

                if (effect.Operation == NumericFOperation.ADD)
                {
                    expected += effect.PlanarOperand;
                }
                else if (effect.Operation == NumericFOperation.SUB)
                {
                    expected -= effect.PlanarOperand;
                }
            }

            var frame = Primary.LastFrame;

            expected = ValidationOracle.Project(expected, frame.Up);

            return ValidationCheckResult.Of
            (
                (frame.Planar - expected).magnitude < 1e-3f,
                $"Planar {ValidationOverlay.V(frame.Planar)} / 외부 힘만 {ValidationOverlay.V(expected)}"
            );
        }

        private ValidationCheckResult CheckHidden()
        {
            if (!dash.IsActive || !unstoppable.IsActive)
            {
                return ValidationCheckResult.Skip;
            }

            var hasDash = Primary.Model.TryGetModifier(dash.Source, out var dashState);
            var hasTop = Primary.Model.TryGetModifier(unstoppable.Source, out var topState);

            if (!hasDash || !hasTop)
            {
                return ValidationCheckResult.Skip;
            }

            return ValidationCheckResult.Of(!dashState.IsVisible && topState.IsVisible, $"대시 보임 {dashState.IsVisible} / 저지불가 보임 {topState.IsVisible}");
        }

        private ValidationCheckResult CheckAction()
        {
            // 활성 SET 효과 중 (Order, 시작 순서)가 가장 뒤인 것이 현재 동작이다.
            ValidationEffect top = null;

            for (var index = 0; index < effects.Count; index++)
            {
                var effect = effects[index];

                if (!effect.IsActive || effect.Operation != NumericFOperation.SET)
                {
                    continue;
                }

                if (top == null || effect.Order > top.Order || (effect.Order == top.Order && effect.Sequence > top.Sequence))
                {
                    top = effect;
                }
            }

            var actual = Primary.LastFrame.ActionID;
            var expected = top == null ? 0 : top.ActionID;

            return ValidationCheckResult.Of(actual == expected, $"ActionID {actual} / 기대 {expected}");
        }

    #endregion

    #region 화면

        public override void DrawControls()
        {
            if (EffectButton("1", dash))
            {
                dash.Start(Primary);
            }

            if (EffectButton("2", knockback))
            {
                knockback.Start(Primary);
            }

            if (EffectButton("3", stun))
            {
                Toggle(stun);
            }

            if (EffectButton("4", slow))
            {
                Toggle(slow);
            }

            if (EffectButton("5", wind))
            {
                Toggle(wind);
            }

            if (EffectButton("6", unstoppable))
            {
                unstoppable.Start(Primary);
            }

            if (EffectButton("7", spin))
            {
                Toggle(spin);
            }

            if (GUILayout.Button("[C] 모두 끝내기 (Clear)"))
            {
                ClearAll();
            }
        }

    #endregion

    }
}
