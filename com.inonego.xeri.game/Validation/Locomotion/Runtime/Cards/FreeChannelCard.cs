/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : FreeChannelCard.cs
수정일 : 2026-11-22

# 설명
Free 채널 카드. 의도가 없는 채널은 Body 속도(중력·관성)를 그대로 두고 지속 ADD의 변화량과 충격만
Impulse로 더하는 규칙, 속도를 남기고 끝내는 keepVelocity를 중력이 있는 큐브로 확인한다.

# 특이사항, 제약사항
보존 법칙: Planar·Elevation이 모두 Free인 구간에서 누적 Impulse가 (지속 합 − 구간 시작 시점 합 + 남긴 몫 + 충격)과 같다.
이 식은 효과의 공개 값으로 다시 계산해 Motion 내부 구현과 독립이다.
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
    /// Free 채널 증분과 keepVelocity를 확인하는 카드.
    /// </summary>
    // ============================================================
    internal sealed class FreeChannelCard : ValidationCard
    {

    #region 필드

        private ValidationEffect wind;
        private ValidationEffect updraft;

        private long impulseGeneration;
        private Vector3 pendingImpulse;
        private Vector3 pendingKept;

        // 보존 법칙 상태.
        private bool streak;
        private Vector3 previousSustained;
        private Vector3 baseline;
        private Vector3 cumulativeBody;
        private Vector3 cumulativeKept;
        private Vector3 cumulativeImpulse;
        private Vector3 sustained;
        private Vector3 expectedBody;
        private bool quietTick;

        public override string Title => "2. Free 채널";

        public override string Summary =>
            "의도가 없으면 중력·관성이 유지되고, 바람 같은 지속 ADD와 점프 충격은 변화량만 Body 속도에 더해진다.";

        public override string Expected =>
            "W를 떼면 Planar가 Free라 흐르던 속도가 유지된다. 바람 끝내기(1)는 되돌리는 충격이 걸려 속도가 사라지고, 속도를 남기고 끝내기(2)는 속도가 유지된다. 누적 Impulse가 기대 합과 같다.";

        public override string[] Controls => new[]
        {
            "Space 점프(충격)  WASD 걷기(Set, 떼면 Free)", "1 바람 켜기/끄기(Impulse로 되돌림)", "2 바람 속도 남기고 끄기(keepVelocity)",
            "3 상승기류(ADD 세로)  4 옆 충격", "R 카드 초기화"
        };

    #endregion

    #region 구성

        protected override void BuildContent()
        {
            CreateGround(new Vector3(40f, 0f, 40f));

            actors.Add(new ValidationActor("Cube", Root.transform, new Vector3(0f, 0.6f, 0f), new Color(0.4f, 1f, 0.6f), 2));

            wind = AddEffect(new ValidationEffect("바람", "Wind", 20, NumericFOperation.ADD, MotionChannel.Planar, new Color(0.4f, 1f, 0.9f))
            {
                Linear = new Vector3(0f, 0f, 3f)
            });
            updraft = AddEffect(new ValidationEffect("상승기류", "Updraft", 30, NumericFOperation.ADD, MotionChannel.Planar | MotionChannel.Elevation, new Color(1f, 0.85f, 0.3f))
            {
                Linear = new Vector3(0f, 4f, 0f)
            });

            Checks.Add(new ValidationCheck("보존 법칙: 누적 Impulse == 지속 합 + 남긴 몫 + 충격", CheckLaw));
            Checks.Add(new ValidationCheck("아무 입력이 없으면 Impulse 0 (Body 속도 유지)", CheckQuiet));
        }

    #endregion

    #region 초기화

        public override void ResetCard()
        {
            base.ResetCard();

            streak = false;
            previousSustained = Vector3.zero;
            pendingImpulse = Vector3.zero;
            pendingKept = Vector3.zero;
            cumulativeBody = cumulativeKept = cumulativeImpulse = Vector3.zero;
            sustained = expectedBody = Vector3.zero;

            if (Primary != null)
            {
                Primary.ResetState(At(0f, 0.6f, 0f));
            }
        }

    #endregion

    #region 입력

        public override void HandleInput()
        {
            if (ValidationInput.Pressed(Key.Space))
            {
                Impulse("Jump", new Vector3(0f, 6f, 0f));
            }

            switch (ValidationInput.Digit())
            {
                case 1:
                    Toggle(wind);
                    break;
                case 2:
                    StopKeeping(wind);
                    break;
                case 3:
                    Toggle(updraft);
                    break;
                case 4:
                    Impulse("Push", new Vector3(4f, 0f, 0f));
                    break;
            }
        }

        private void Impulse(string key, Vector3 velocity)
        {
            var values = MotionChannelValues.Linear(MotionChannel.Planar | MotionChannel.Elevation, velocity);

            Primary.Buffer.Add(MotionModifierCommand.Impulse(new MotionSourceKey(key, ++impulseGeneration), 100, values));
            pendingImpulse += velocity;
        }

        private void StopKeeping(ValidationEffect effect)
        {
            if (!effect.IsActive)
            {
                return;
            }

            // 이미 Step을 거친 효과만 Body에 몫을 남겼다.
            if (effect.Stepped)
            {
                pendingKept += effect.Contribution(Primary.Model.Up);
            }

            effect.Stop(Primary, true);
        }

    #endregion

    #region Tick

        public override void BeforeTick(float dt)
        {
            TickEffects(dt);

            var move = ValidationInput.Move();

            if (move.sqrMagnitude > 0f)
            {
                Primary.Buffer.Add(new MotionIntent(MotionChannel.Planar, move * 3f));
            }
        }

        protected override void OnAfterTick(float dt)
        {
            var frame = Primary.LastFrame;
            var up = frame.Up;

            sustained = Vector3.zero;

            for (var index = 0; index < effects.Count; index++)
            {
                if (effects[index].IsActive)
                {
                    sustained += effects[index].Contribution(up);
                }
            }

            var bothFree = frame.PlanarMode == MotionChannelMode.Free && frame.ElevationMode == MotionChannelMode.Free;

            // 지속 효과도 충격도 남긴 몫도 없고 직전 Tick에도 없었다면 Free 채널은 건드리지 않아야 한다.
            quietTick = bothFree && sustained.sqrMagnitude < 1e-12f && previousSustained.sqrMagnitude < 1e-12f &&
                        pendingKept.sqrMagnitude < 1e-12f && pendingImpulse.sqrMagnitude < 1e-12f;

            if (!bothFree)
            {
                streak = false;
                pendingKept = Vector3.zero;
            }
            else
            {
                if (!streak)
                {
                    streak = true;
                    baseline = previousSustained;
                    cumulativeBody = cumulativeKept = cumulativeImpulse = Vector3.zero;
                }

                cumulativeBody += frame.Impulse;
                cumulativeKept += pendingKept;
                cumulativeImpulse += pendingImpulse;
                expectedBody = sustained - baseline + cumulativeKept + cumulativeImpulse;
                pendingKept = Vector3.zero;
            }

            previousSustained = sustained;
            pendingImpulse = Vector3.zero;

            // 넓은 바닥에서 벗어나면 가운데로 돌려놓는다. Model은 유지한다.
            var position = Primary.Body.position - Origin;

            if (Mathf.Abs(position.x) > 18f || Mathf.Abs(position.z) > 18f || position.y < -6f)
            {
                Primary.Teleport(At(0f, 0.6f, 0f));
            }
        }

    #endregion

    #region 점검

        private ValidationCheckResult CheckLaw()
        {
            if (!streak)
            {
                return ValidationCheckResult.Skip;
            }

            var error = (cumulativeBody - expectedBody).magnitude;

            return ValidationCheckResult.Of
            (
                error < 2e-3f * (1f + expectedBody.magnitude),
                $"누적 {ValidationOverlay.V(cumulativeBody)} / 기대 {ValidationOverlay.V(expectedBody)}"
            );
        }

        private ValidationCheckResult CheckQuiet()
        {
            if (!quietTick)
            {
                return ValidationCheckResult.Skip;
            }

            var impulse = Primary.LastFrame.Impulse;

            return ValidationCheckResult.Of(impulse.magnitude < 1e-4f, $"Impulse {ValidationOverlay.V(impulse)}");
        }

    #endregion

    #region 화면

        public override void DrawControls()
        {
            if (GUILayout.Button("[Space] 점프 충격"))
            {
                Impulse("Jump", new Vector3(0f, 6f, 0f));
            }

            if (EffectButton("1", wind))
            {
                Toggle(wind);
            }

            if (GUILayout.Button("[2] 바람 속도 남기고 끄기"))
            {
                StopKeeping(wind);
            }

            if (EffectButton("3", updraft))
            {
                Toggle(updraft);
            }

            if (GUILayout.Button("[4] 옆 충격"))
            {
                Impulse("Push", new Vector3(4f, 0f, 0f));
            }
        }

        public override void DrawPanel()
        {
            ValidationOverlay.Title("보존 법칙 (Planar·Elevation 모두 Free인 구간)");
            ValidationOverlay.Line($"구간 {(streak ? "진행 중" : "Set이 섞여 건너뜀")}");
            ValidationOverlay.Line($"지속 합 {ValidationOverlay.V(sustained)}  남긴 몫 누적 {ValidationOverlay.V(cumulativeKept)}");
            ValidationOverlay.Line($"누적 Impulse {ValidationOverlay.V(cumulativeBody)}");
            ValidationOverlay.Line($"기대 {ValidationOverlay.V(expectedBody)}");
        }

    #endregion

    }
}
