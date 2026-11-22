/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : LeaseCard.cs
수정일 : 2026-11-22

# 설명
Lease 카드. 사다리 구역에 들어가면 MotionInputBuffer.AcquireModifier로 오르기 항목을 얻고,
나오면 Lease를 해제해 종료 명령이 들어가는 흐름을 확인한다.

# 특이사항, 제약사항
해제 시점을 다른 코드(구역 판정)가 정하는 Modifier에 쓰는 Core Lease 패턴이다.
Lease를 두 번 해제해도 종료 명령은 한 번만 들어가야 한다(L 키로 확인).
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
    /// Lease로 얻고 해제하는 Modifier를 확인하는 카드.
    /// </summary>
    // ============================================================
    internal sealed class LeaseCard : ValidationCard
    {

    #region 필드

        private const int LADDER_ACTION = 5;

        private Bounds zone;
        private Lease lease;
        private MotionSourceKey ladderKey;
        private long ladderGeneration;
        private bool inside;
        private bool singleEndPending;
        private Vector3 intent;

        public override string Title => "7. Lease (사다리)";

        public override string Summary =>
            "사다리 구역에 들어가면 Lease로 오르기 SET을 얻고, 나오면 해제해 종료 명령이 한 번만 들어가는지 본다.";

        public override string Expected =>
            "구역 안에서는 Planar 0, Elevation 2로 올라가고 ActionID가 5다. 구역 밖에서는 항목이 없다. L로 같은 Lease를 두 번 해제해도 종료 명령은 한 번이다.";

        public override string[] Controls => new[]
        {
            "WASD 걷기 (오른쪽 +X의 노란 사다리로)", "L 지금 Lease를 두 번 해제(종료 명령 1번만 기대)", "R 카드 초기화"
        };

    #endregion

    #region 구성

        protected override void BuildContent()
        {
            CreateGround(new Vector3(24f, 0f, 16f));

            actors.Add(new ValidationActor("Cube", Root.transform, new Vector3(0f, 0.6f, 0f), new Color(0.5f, 1f, 0.95f), 7));

            // 사다리 구역: 눈에 보이는 얇은 기둥 둘과 바닥 표시.
            zone = new Bounds(At(6f, 1.5f, 0f), new Vector3(2f, 3f, 2f));

            ValidationFactory.CreateBox("LadderLeft", Root.transform, new Vector3(5.2f, 1.5f, 0f), new Vector3(0.15f, 3f, 0.15f), new Color(1f, 0.85f, 0.2f), false);
            ValidationFactory.CreateBox("LadderRight", Root.transform, new Vector3(6.8f, 1.5f, 0f), new Vector3(0.15f, 3f, 0.15f), new Color(1f, 0.85f, 0.2f), false);
            ValidationFactory.CreateBox("LadderMark", Root.transform, new Vector3(6f, 0.02f, 0f), new Vector3(2f, 0.04f, 2f), new Color(1f, 0.85f, 0.2f), false);

            Checks.Add(new ValidationCheck("구역 안: 오르기 항목·ActionID 5·Planar 0·Elevation 2", CheckInside));
            Checks.Add(new ValidationCheck("구역 밖: 항목 없음", CheckOutside));
            Checks.Add(new ValidationCheck("Lease를 두 번 해제해도 종료 명령 1번", CheckSingleEnd));
        }

    #endregion

    #region 초기화

        public override void ResetCard()
        {
            base.ResetCard();

            lease = null;
            inside = false;
            singleEndPending = false;

            if (Primary != null)
            {
                Primary.ResetState(At(0f, 0.6f, 0f));
            }
        }

    #endregion

    #region 입력

        public override void HandleInput()
        {
            if (ValidationInput.Pressed(Key.L))
            {
                DisposeTwice();
            }
        }

        private void DisposeTwice()
        {
            if (lease == null || lease.IsDisposed)
            {
                return;
            }

            // 같은 Lease를 두 번 해제한다. 종료 명령은 한 번만 들어가야 한다.
            lease.Dispose();
            lease.Dispose();
            singleEndPending = true;
        }

    #endregion

    #region Tick

        public override void BeforeTick(float dt)
        {
            inside = zone.Contains(Primary.Body.position);

            var held = lease != null && !lease.IsDisposed;

            if (inside && !held && !singleEndPending)
            {
                Acquire();
            }
            else if (!inside && held)
            {
                lease.Dispose();
            }

            // 걷기 의도는 항상 보내 구역 밖에서는 멈추고, 안에서는 오르기 SET이 덮어쓴다.
            intent = ValidationInput.Move() * 4f;

            Primary.Buffer.Add(new MotionIntent(MotionChannel.Planar, intent));
        }

        private void Acquire()
        {
            ladderKey = new MotionSourceKey("Ladder", ++ladderGeneration);

            var values = MotionChannelValues.Linear(MotionChannel.Planar | MotionChannel.Elevation, new Vector3(0f, 2f, 0f));

            lease = Primary.Buffer.AcquireModifier(MotionModifierCommand.Apply(ladderKey, 30, NumericFOperation.SET, values, LADDER_ACTION));
        }

    #endregion

    #region 점검

        private ValidationCheckResult CheckInside()
        {
            if (!inside || lease == null || lease.IsDisposed)
            {
                return ValidationCheckResult.Skip;
            }

            if (!Primary.Model.TryGetModifier(ladderKey, out var state))
            {
                return ValidationCheckResult.Fail("항목이 Model에 없음");
            }

            var frame = Primary.LastFrame;
            var ok = frame.ActionID == LADDER_ACTION && frame.Planar.magnitude < 1e-3f && Mathf.Abs(frame.Elevation - 2f) < 1e-3f && state.IsVisible;

            return ValidationCheckResult.Of(ok, $"ActionID {frame.ActionID} Planar {ValidationOverlay.V(frame.Planar)} Elevation {frame.Elevation:F2}");
        }

        private ValidationCheckResult CheckOutside()
        {
            if (inside || ladderGeneration == 0 || (lease != null && !lease.IsDisposed))
            {
                return ValidationCheckResult.Skip;
            }

            var absent = !Primary.Model.TryGetModifier(ladderKey, out _);

            return ValidationCheckResult.Of(absent, absent ? "항목 없음" : "해제했는데 항목이 남음");
        }

        private ValidationCheckResult CheckSingleEnd()
        {
            if (!singleEndPending)
            {
                return ValidationCheckResult.Skip;
            }

            singleEndPending = false;

            var accepted = 0;
            var notFound = 0;

            for (var index = 0; index < Primary.LastResults.Count; index++)
            {
                var result = Primary.LastResults[index];

                if (result.Action != MotionCommandAction.End)
                {
                    continue;
                }

                if (result.Status == MotionInputStatus.Accepted)
                {
                    accepted++;
                }
                else if (result.Status == MotionInputStatus.NotFound)
                {
                    notFound++;
                }
            }

            return ValidationCheckResult.Of(accepted == 1 && notFound == 0, $"End Accepted {accepted}, NotFound {notFound}");
        }

    #endregion

    #region 화면

        public override void DrawControls()
        {
            if (GUILayout.Button("[L] 현재 Lease 두 번 해제"))
            {
                DisposeTwice();
            }
        }

        public override void DrawPanel()
        {
            ValidationOverlay.Title("Lease");
            ValidationOverlay.Line($"구역 안 {inside}  Lease {(lease == null ? "없음" : lease.IsDisposed ? "해제됨" : "보유")}  세대 {ladderGeneration}");
        }

    #endregion

    }
}
