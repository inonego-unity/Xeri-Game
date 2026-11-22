/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : OperationCard.cs
수정일 : 2026-11-22

# 설명
연산 카드. 자동으로 걷는 큐브에 ADD·SUB·MUL·DIV·MIN·MAX 항목을 Order 순서로 겹쳐
Core NumericFOperation 규칙(벡터는 성분별)이 Motion 접기와 같은 결과를 내는지 확인한다.

# 특이사항, 제약사항
MIN·MAX는 속도 크기 제한이 아니라 성분별 비교다. 예를 들어 MAX (2,0,0)은 x를 2 이상으로, z는 0 이상으로 올린다.
기대값은 Core NumericFOperationHelper로 따로 계산한다.
========================================================================= BLOCK_HEADER_END */

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
    /// Core 연산 6종의 접기 결과를 확인하는 카드.
    /// </summary>
    // ============================================================
    internal sealed class OperationCard : ValidationCard
    {

    #region 필드

        private const float SPEED = 4f;

        private ValidationEffect add;
        private ValidationEffect sub;
        private ValidationEffect mul;
        private ValidationEffect div;
        private ValidationEffect min;
        private ValidationEffect max;

        private bool autoWalk = true;
        private Vector3 intent;

        public override string Title => "5. 연산 (ADD·SUB·MUL·DIV·MIN·MAX)";

        public override string Summary =>
            "자동으로 걷는 큐브에 연산 항목을 Order 순서로 켜서 Core 연산 규칙과 같은 값이 나오는지 본다.";

        public override string Expected =>
            "항목을 어떤 조합으로 켜도 Motion의 Planar가 기대값과 같다. MIN·MAX는 성분별이라 MAX (2,0,0)은 뒤로 걷는 z를 0으로 올린다.";

        public override string[] Controls => new[]
        {
            "WASD 직접 걷기(없으면 자동 +X)  T 자동 걷기 ON/OFF", "1 ADD (1.5,0,0) o20  2 SUB (0.5,0,0) o25", "3 MUL 0.5 o35  4 DIV 2 o30",
            "5 MIN 3 o40(상한)  6 MAX (2,0,0) o45(하한)", "R 카드 초기화"
        };

    #endregion

    #region 구성

        protected override void BuildContent()
        {
            CreateGround(new Vector3(34f, 0f, 20f));

            actors.Add(new ValidationActor("Cube", Root.transform, new Vector3(-10f, 0.6f, 0f), new Color(0.95f, 0.75f, 0.3f), 5));

            add = AddEffect(new ValidationEffect("ADD", "Add", 20, NumericFOperation.ADD, MotionChannel.Planar, new Color(0.4f, 1f, 0.5f)) { Linear = new Vector3(1.5f, 0f, 0f) });
            sub = AddEffect(new ValidationEffect("SUB", "Sub", 25, NumericFOperation.SUB, MotionChannel.Planar, new Color(1f, 0.5f, 0.4f)) { Linear = new Vector3(0.5f, 0f, 0f) });
            mul = AddEffect(new ValidationEffect("MUL 0.5", "Mul", 35, NumericFOperation.MUL, MotionChannel.Planar, new Color(0.7f, 0.5f, 1f)) { UseUniform = true, Uniform = 0.5f });
            div = AddEffect(new ValidationEffect("DIV 2", "Div", 30, NumericFOperation.DIV, MotionChannel.Planar, new Color(0.4f, 0.8f, 1f)) { UseUniform = true, Uniform = 2f });
            min = AddEffect(new ValidationEffect("MIN 3", "Min", 40, NumericFOperation.MIN, MotionChannel.Planar, new Color(1f, 0.9f, 0.3f)) { UseUniform = true, Uniform = 3f });
            max = AddEffect(new ValidationEffect("MAX (2,0,0)", "Max", 45, NumericFOperation.MAX, MotionChannel.Planar, new Color(1f, 0.6f, 0.9f)) { Linear = new Vector3(2f, 0f, 0f) });

            Checks.Add(new ValidationCheck("접기 일치: Planar == Core 연산 순서 접기", CheckFold));
        }

    #endregion

    #region 초기화

        public override void ResetCard()
        {
            base.ResetCard();

            autoWalk = true;

            if (Primary != null)
            {
                Primary.ResetState(At(-10f, 0.6f, 0f));
            }
        }

    #endregion

    #region 입력

        public override void HandleInput()
        {
            switch (ValidationInput.Digit())
            {
                case 1:
                    Toggle(add);
                    break;
                case 2:
                    Toggle(sub);
                    break;
                case 3:
                    Toggle(mul);
                    break;
                case 4:
                    Toggle(div);
                    break;
                case 5:
                    Toggle(min);
                    break;
                case 6:
                    Toggle(max);
                    break;
            }

            if (ValidationInput.Pressed(Key.T))
            {
                autoWalk = !autoWalk;
            }
        }

    #endregion

    #region Tick

        public override void BeforeTick(float dt)
        {
            TickEffects(dt);

            var move = ValidationInput.Move();

            intent = move.sqrMagnitude > 0f ? move * SPEED : autoWalk ? new Vector3(SPEED, 0f, 0f) : Vector3.zero;

            Primary.Buffer.Add(new MotionIntent(MotionChannel.Planar, intent));
        }

        protected override void OnAfterTick(float dt)
        {
            // 자동 걷기로 바닥 끝에 닿으면 반대편으로 돌려놓는다.
            var position = Primary.Body.position - Origin;

            if (Mathf.Abs(position.x) > 15f)
            {
                Primary.Teleport(At(-Mathf.Sign(position.x) * 13f, 0.6f, position.z));
            }

            if (Mathf.Abs(position.z) > 8f)
            {
                Primary.Teleport(At(position.x, 0.6f, 0f));
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

            return ValidationCheckResult.Of
            (
                (frame.Planar - expected).magnitude < 1e-3f,
                $"Motion {ValidationOverlay.V(frame.Planar)} / 기대 {ValidationOverlay.V(expected)}"
            );
        }

    #endregion

    #region 화면

        public override void DrawControls()
        {
            if (EffectButton("1", add))
            {
                Toggle(add);
            }

            if (EffectButton("2", sub))
            {
                Toggle(sub);
            }

            if (EffectButton("3", mul))
            {
                Toggle(mul);
            }

            if (EffectButton("4", div))
            {
                Toggle(div);
            }

            if (EffectButton("5", min))
            {
                Toggle(min);
            }

            if (EffectButton("6", max))
            {
                Toggle(max);
            }

            if (GUILayout.Button($"[T] 자동 걷기: {(autoWalk ? "ON" : "OFF")}"))
            {
                autoWalk = !autoWalk;
            }
        }

        public override void DrawPanel()
        {
            ValidationOverlay.Title("연산 접기");
            ValidationOverlay.Line($"걷기 의도 {ValidationOverlay.V(intent)}");

            if (ValidationOracle.TryExpectPlanar(effects, true, intent, Primary.Model.Up, out var expected))
            {
                ValidationOverlay.Line($"Core 규칙 기대 {ValidationOverlay.V(expected)}");
            }
        }

    #endregion

    }
}
