/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : PlatformCard.cs
수정일 : 2026-11-22

# 설명
이동 플랫폼 카드. 좌우로 움직이는 발판 위에서 상대 속도는 의도로, 발판 속도는 ADD 항목으로 넣고
떠날 때 keepVelocity 여부에 따라 관성이 남거나 사라지는 것을 확인한다.

# 특이사항, 제약사항
발판 위에서는 항상 Planar 의도(걷지 않으면 0)를 보내 Set으로 두어 발판 속도가 두 번 더해지지 않게 한다.
공중에서는 의도를 보내지 않아 Free가 되고, 이때 Body 속도가 관성이다.
발판 속도는 발판 위치 변화로 계산하고(지면 접점 속도를 GroundChecker에서 받는 연결은 사용처 몫) 이 카드는 같은 패턴을 직접 만든다.
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
    /// 이동 플랫폼 속도 상속과 keepVelocity를 확인하는 카드.
    /// </summary>
    // ============================================================
    internal sealed class PlatformCard : ValidationCard
    {

    #region 필드

        private const float AMPLITUDE = 7f;
        private const float PERIOD = 6f;
        private const float WALK_SPEED = 3f;
        private const int LEAVE_TRACK_TICKS = 20;

        private Rigidbody platformBody;
        private ValidationEffect carrier;

        private float platformTime;
        private Vector3 platformVelocity;
        private bool standing;
        private int standingTicks;
        private bool keepOnLeave = true;
        private Vector3 intent;

        // 떠난 뒤 관성 추적.
        private bool leaving;
        private int leaveTicks;
        private Vector3 leaveExpected;

        public override string Title => "3. 이동 플랫폼";

        public override string Summary =>
            "움직이는 발판 위에서 발판 속도를 ADD로 넣고, 떠날 때 keepVelocity로 관성을 남기거나 지운다.";

        public override string Expected =>
            "발판 위의 Body 수평 속도는 발판 속도와 같다. 떠날 때 K가 ON이면 점프 후에도 발판 방향으로 계속 날아가고, OFF이면 수평 속도가 사라져 제자리에 떨어진다.";

        public override string[] Controls => new[]
        {
            "WASD 발판 위에서 걷기(상대 속도, 공중에서는 무시)", "Space 점프(발판을 떠남)", "K 떠날 때 keepVelocity ON/OFF", "R 카드 초기화"
        };

        public override Vector3 CameraOffset => new Vector3(0f, 8f, -13f);

    #endregion

    #region 구성

        protected override void BuildContent()
        {
            CreateGround(new Vector3(40f, 0f, 24f));

            var platform = ValidationFactory.CreateBox("Platform", Root.transform, new Vector3(0f, 0.9f, 0f), new Vector3(7f, 0.4f, 7f), new Color(1f, 0.6f, 0.2f));

            platformBody = platform.AddComponent<Rigidbody>();
            platformBody.isKinematic = true;

            actors.Add(new ValidationActor("Cube", Root.transform, new Vector3(0f, 1.7f, 0f), new Color(1f, 0.95f, 0.3f), 3));

            carrier = AddEffect(new ValidationEffect("발판", "Platform", 60, NumericFOperation.ADD, MotionChannel.Planar | MotionChannel.Elevation, new Color(1f, 0.6f, 0.2f)));

            Checks.Add(new ValidationCheck("발판 위: Body 수평 속도 == 발판 속도 + 걷기", CheckRiding));
            Checks.Add(new ValidationCheck("떠난 뒤: 수평 속도 == 걷기 + (keep면 발판 속도)", CheckInertia));
        }

    #endregion

    #region 초기화

        public override void ResetCard()
        {
            base.ResetCard();

            platformTime = 0f;
            platformVelocity = Vector3.zero;
            standing = false;
            standingTicks = 0;
            leaving = false;

            if (platformBody != null)
            {
                platformBody.position = At(0f, 0.9f, 0f);
                platformBody.transform.position = At(0f, 0.9f, 0f);
            }

            if (Primary != null)
            {
                Primary.ResetState(At(0f, 1.7f, 0f));
            }
        }

    #endregion

    #region 입력

        public override void HandleInput()
        {
            if (ValidationInput.Pressed(Key.K))
            {
                keepOnLeave = !keepOnLeave;
            }

            if (ValidationInput.Pressed(Key.Space) && standing)
            {
                Jump();
            }
        }

        private void Jump()
        {
            var values = MotionChannelValues.Linear(MotionChannel.Planar | MotionChannel.Elevation, new Vector3(0f, 7f, 0f));

            Primary.Buffer.Add(MotionModifierCommand.Impulse(new MotionSourceKey("Jump", Revision.FromUInt64((ulong)(Primary.Model.LastTick + 2))), 100, values));
        }

    #endregion

    #region Tick

        public override void BeforeTick(float dt)
        {
            MovePlatform(dt);

            standing = IsStanding();
            standingTicks = standing ? standingTicks + 1 : 0;

            if (standing)
            {
                // 발판 위: 걷기 의도(0 포함)와 발판 속도를 함께 넣는다.
                intent = ValidationInput.Move() * WALK_SPEED;
                carrier.Linear = platformVelocity;

                if (carrier.IsActive)
                {
                    carrier.Refresh(Primary);
                }
                else
                {
                    carrier.Start(Primary);
                }

                Primary.Buffer.Add(new MotionIntent(MotionChannel.Planar, intent));
            }
            else if (carrier.IsActive)
            {
                // 떠나는 Tick: 직전에 반영한 발판 속도를 keepVelocity 여부에 따라 남기거나 되돌린다.
                leaving = true;
                leaveTicks = 0;
                leaveExpected = intent + (keepOnLeave ? carrier.Linear : Vector3.zero);

                carrier.Stop(Primary, keepOnLeave);
            }
        }

        protected override void OnAfterTick(float dt)
        {
            if (leaving)
            {
                leaveTicks++;

                if (leaveTicks > LEAVE_TRACK_TICKS || standing)
                {
                    leaving = false;
                }
            }

            // 발판에서 너무 멀어지면 처음 자리로 돌려놓는다.
            var position = Primary.Body.position - Origin;

            if (Mathf.Abs(position.x) > 16f || Mathf.Abs(position.z) > 10f || position.y < -6f)
            {
                carrier.MarkInactive();
                Primary.ResetState(At(0f, 1.7f, 0f));
                platformTime = 0f;
            }
        }

        private void MovePlatform(float dt)
        {
            platformTime += dt;

            var target = At(AMPLITUDE * Mathf.Sin(2f * Mathf.PI * platformTime / PERIOD), 0.9f, 0f);

            platformVelocity = (target - platformBody.position) / dt;
            platformVelocity.y = 0f;

            platformBody.MovePosition(target);
        }

        private bool IsStanding()
        {
            var origin = Primary.Body.position;

            return Physics.Raycast(origin, Vector3.down, out var hit, 0.75f) && hit.rigidbody == platformBody;
        }

    #endregion

    #region 점검

        private ValidationCheckResult CheckRiding()
        {
            if (!standing || standingTicks < 6 || Mathf.Abs(Primary.Body.linearVelocity.y) > 0.3f)
            {
                return ValidationCheckResult.Skip;
            }

            var body = Primary.Body.linearVelocity;
            var planar = new Vector3(body.x, 0f, body.z);
            var expected = platformVelocity + intent;

            return ValidationCheckResult.Of
            (
                (planar - expected).magnitude < 0.35f,
                $"Body {ValidationOverlay.V(planar)} / 기대 {ValidationOverlay.V(expected)}"
            );
        }

        private ValidationCheckResult CheckInertia()
        {
            if (!leaving || standing || leaveTicks < 3)
            {
                return ValidationCheckResult.Skip;
            }

            var body = Primary.Body.linearVelocity;
            var planar = new Vector3(body.x, 0f, body.z);

            return ValidationCheckResult.Of
            (
                (planar - leaveExpected).magnitude < 0.5f,
                $"Body {ValidationOverlay.V(planar)} / 기대 {ValidationOverlay.V(leaveExpected)} (keep {(keepOnLeave ? "ON" : "OFF")})"
            );
        }

    #endregion

    #region 화면

        public override void DrawControls()
        {
            if (GUILayout.Button($"[K] 떠날 때 keepVelocity: {(keepOnLeave ? "ON" : "OFF")}"))
            {
                keepOnLeave = !keepOnLeave;
            }

            if (GUILayout.Button("[Space] 점프 (발판 위에서)") && standing)
            {
                Jump();
            }
        }

        public override void DrawPanel()
        {
            ValidationOverlay.Title("발판");
            ValidationOverlay.Line($"발판 속도 {ValidationOverlay.V(platformVelocity)}  위에 서 있음 {standing}");
            ValidationOverlay.Line($"떠날 때 keepVelocity {(keepOnLeave ? "ON (관성 유지)" : "OFF (속도 되돌림)")}");

            if (leaving)
            {
                ValidationOverlay.Line($"이탈 추적 {leaveTicks}/{LEAVE_TRACK_TICKS} Tick, 기대 수평 속도 {ValidationOverlay.V(leaveExpected)}");
            }
        }

    #endregion

    }
}
