/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : RollbackCard.cs
수정일 : 2026-11-22

# 설명
롤백·재실행 카드. 정해진 입력 스크립트(대시, 감쇠 넉백, 충격, Up 변경, 월드 선속도, 스턴, 회전)를
기록한 뒤 체크포인트로 되돌려 같은 입력을 다시 넣어 프레임이 모두 같은지 비교한다.

# 특이사항, 제약사항
입력은 Tick 번호만의 함수라 도메인 상태 없이 재현된다. Snapshot에는 Motion Model(항목, Up, 반영 몫)과
Motor Body 상태(위치·속도)를 함께 저장한다.
프레임 비교는 StepGeneration을 제외한 모든 필드의 정확한 일치다. Body 위치는 Physics 재실행이라 차이만 보여준다.
H 키는 재실행에서 충격 입력 하나를 일부러 빼, 점검이 실제로 어긋남을 잡는지 확인한다.
========================================================================= BLOCK_HEADER_END */

using System.Collections.Generic;

using UnityEngine;
using UnityEngine.InputSystem;

using inonego;
using inonego.Xeri;
using inonego.Xeri.Game;
using inonego.Xeri.Game.Motion;
using inonego.Xeri.Game.Motor;
using inonego.Xeri.Serializable;

namespace inonego.Xeri.Game.Validation.Locomotion
{
    // ============================================================
    /// <summary>
    /// Snapshot 복원 뒤 재실행 일치를 확인하는 카드.
    /// </summary>
    // ============================================================
    internal sealed class RollbackCard : ValidationCard
    {

    #region 내부 데이터

        private enum Phase { Idle, Recording, Recorded, Replaying, Done }

        private readonly struct FrameRecord
        {
            public readonly MotionChannelMode PlanarMode;
            public readonly MotionChannelMode ElevationMode;
            public readonly MotionChannelMode RotationMode;
            public readonly Vector3 Planar;
            public readonly float Elevation;
            public readonly Vector3 Rotation;
            public readonly Vector3 Impulse;
            public readonly Vector3 Up;
            public readonly int ActionID;
            public readonly long ActionStartTick;

            public FrameRecord(in MotionFrame frame)
            {
                PlanarMode = frame.PlanarMode;
                ElevationMode = frame.ElevationMode;
                RotationMode = frame.RotationMode;
                Planar = frame.Planar;
                Elevation = frame.Elevation;
                Rotation = frame.Rotation;
                Impulse = frame.Impulse;
                Up = frame.Up;
                ActionID = frame.ActionID;
                ActionStartTick = frame.ActionStartTick;
            }

            public bool Equals(in FrameRecord other) =>
                PlanarMode == other.PlanarMode && ElevationMode == other.ElevationMode && RotationMode == other.RotationMode &&
                Same(Planar, other.Planar) && Elevation == other.Elevation && Same(Rotation, other.Rotation) &&
                Same(Impulse, other.Impulse) && Same(Up, other.Up) && ActionID == other.ActionID && ActionStartTick == other.ActionStartTick;

            private static bool Same(Vector3 a, Vector3 b) => a.x == b.x && a.y == b.y && a.z == b.z;
        }

    #endregion

    #region 필드

        private const int TICKS = 160;

        private readonly List<FrameRecord> recorded = new List<FrameRecord>();
        private readonly List<FrameRecord> replayed = new List<FrameRecord>();

        private Phase phase;
        private MotionModelSnapshot checkpoint;
        private MotorState3D checkpointBody;
        private long startTick;
        private bool sabotage;

        private Vector3 recordedPosition;
        private Vector3 recordedVelocity;
        private Vector3 replayedPosition;
        private Vector3 replayedVelocity;
        private int diffCount;
        private long firstDiffTick = -1;
        private bool justFinished;
        private bool lastWasSabotage;

        public override string Title => "4. 롤백·재실행";

        public override string Summary =>
            "정해진 입력을 160 Tick 기록한 뒤 체크포인트로 되돌려 다시 넣어, Motion 프레임이 한 필드도 다르지 않은지 비교한다.";

        public override string Expected =>
            "F로 기록 → G로 재실행하면 프레임 불일치 0이라 PASS. H(입력 하나 일부러 누락)는 불일치가 잡혀 FAIL이 나와야 점검이 제대로 동작하는 것이다. Body 위치 차이는 Physics 재실행 오차라 작아야 한다.";

        public override string[] Controls => new[]
        {
            "F 체크포인트 저장 + 160 Tick 기록", "G 되돌려 같은 입력으로 재실행(일치해야 PASS)", "H 되돌려 재실행하되 충격 하나 누락(FAIL이 정상)", "R 카드 초기화"
        };

    #endregion

    #region 구성

        protected override void BuildContent()
        {
            CreateGround(new Vector3(70f, 0f, 70f));

            actors.Add(new ValidationActor("Cube", Root.transform, new Vector3(0f, 0.6f, 0f), new Color(0.85f, 0.5f, 1f), 4));

            Checks.Add(new ValidationCheck("재실행 프레임 == 기록 프레임 (모든 필드)", CheckReplay));
        }

    #endregion

    #region 초기화

        public override void ResetCard()
        {
            base.ResetCard();

            phase = Phase.Idle;
            recorded.Clear();
            replayed.Clear();
            diffCount = 0;
            firstDiffTick = -1;
            justFinished = false;

            if (Primary != null)
            {
                Primary.ResetState(At(0f, 0.6f, 0f));
            }
        }

    #endregion

    #region 입력

        public override void HandleInput()
        {
            if (ValidationInput.Pressed(Key.F))
            {
                StartRecording();
            }

            if (ValidationInput.Pressed(Key.G))
            {
                StartReplay(false);
            }

            if (ValidationInput.Pressed(Key.H))
            {
                StartReplay(true);
            }
        }

        private void StartRecording()
        {
            Primary.ResetState(At(0f, 0.6f, 0f));

            // 초기 Tick에서 체크포인트를 저장하고 그 시점부터 기록한다.
            startTick = Primary.Model.LastTick + 1;
            checkpoint = Primary.Model.Capture();
            checkpointBody = Primary.Mover.ReadCurrentState();
            recorded.Clear();
            replayed.Clear();
            phase = Phase.Recording;
        }

        private void StartReplay(bool dropImpulse)
        {
            if (checkpoint == null || (phase != Phase.Recorded && phase != Phase.Done))
            {
                return;
            }

            // Model은 Snapshot으로, Body는 저장한 상태로 되돌린다.
            Primary.Model.Restore(checkpoint);
            Primary.Mover.ApplyCurrentState(checkpointBody);
            Primary.Buffer.Clear();

            sabotage = dropImpulse;
            replayed.Clear();
            phase = Phase.Replaying;
        }

    #endregion

    #region Tick

        public override void BeforeTick(float dt)
        {
            if (phase != Phase.Recording && phase != Phase.Replaying)
            {
                Primary.Buffer.Add(new MotionIntent(MotionChannel.Planar, Vector3.zero));

                return;
            }

            var k = Primary.Model.LastTick + 1 - startTick;

            Script((int)k, dt, phase == Phase.Replaying && sabotage);
        }

        protected override void OnAfterTick(float dt)
        {
            if (phase == Phase.Recording)
            {
                recorded.Add(new FrameRecord(Primary.LastFrame));

                if (recorded.Count >= TICKS)
                {
                    recordedPosition = Primary.Body.position;
                    recordedVelocity = Primary.Body.linearVelocity;
                    phase = Phase.Recorded;
                }
            }
            else if (phase == Phase.Replaying)
            {
                replayed.Add(new FrameRecord(Primary.LastFrame));

                if (replayed.Count >= TICKS)
                {
                    replayedPosition = Primary.Body.position;
                    replayedVelocity = Primary.Body.linearVelocity;
                    Compare();
                    phase = Phase.Done;
                    justFinished = true;
                    lastWasSabotage = sabotage;
                }
            }
        }

        private void Compare()
        {
            diffCount = 0;
            firstDiffTick = -1;

            for (var index = 0; index < TICKS; index++)
            {
                var a = recorded[index];
                var b = replayed[index];

                if (!a.Equals(b))
                {
                    diffCount++;

                    if (firstDiffTick < 0)
                    {
                        firstDiffTick = startTick + index;
                    }
                }
            }
        }

        // Tick 번호 k만으로 정해지는 입력 스크립트. 도메인 상태가 없어 같은 입력이 재현된다.
        private void Script(int k, float dt, bool skipImpulse)
        {
            var buffer = Primary.Buffer;
            var angle = k * 0.05f;

            buffer.Add(new MotionIntent(MotionChannel.Planar, new Vector3(3f * Mathf.Cos(angle), 0f, 3f * Mathf.Sin(angle))));

            // 10~40: 대시(SET), 40에 끝.
            if (k == 10)
            {
                buffer.Add(MotionModifierCommand.Apply(new MotionSourceKey("S_Dash", Revision.FromUInt64(1)), 10, NumericFOperation.SET, MotionChannelValues.Linear(MotionChannel.Planar, new Vector3(0f, 0f, 9f)), 1));
            }
            else if (k == 40)
            {
                buffer.Add(MotionModifierCommand.End(new MotionSourceKey("S_Dash", Revision.FromUInt64(1))));
            }

            // 20~80: 감쇠하는 넉백(같은 세대 갱신), 80에 속도를 남기고 끝.
            if (k >= 20 && k < 80)
            {
                var scale = Mathf.Exp(-3f * (k - 20) * dt);

                buffer.Add(MotionModifierCommand.Apply(new MotionSourceKey("S_Knock", Revision.FromUInt64(1)), 20, NumericFOperation.ADD, MotionChannelValues.Linear(MotionChannel.Planar | MotionChannel.Elevation, new Vector3(-8f * scale, 2f * scale, 0f))));
            }
            else if (k == 80)
            {
                buffer.Add(MotionModifierCommand.End(new MotionSourceKey("S_Knock", Revision.FromUInt64(1)), true));
            }

            // 30: 점프 충격.
            if (k == 30 && !skipImpulse)
            {
                buffer.Add(MotionModifierCommand.Impulse(new MotionSourceKey("S_Jump", Revision.FromUInt64(1)), 100, MotionChannelValues.Linear(MotionChannel.Planar | MotionChannel.Elevation, new Vector3(0f, 6f, 0f))));
            }

            // 50~75: 회전(SET).
            if (k == 50)
            {
                buffer.Add(MotionModifierCommand.Apply(new MotionSourceKey("S_Spin", Revision.FromUInt64(1)), 5, NumericFOperation.SET, MotionChannelValues.Linear(MotionChannel.Rotation, Vector3.zero, new Vector3(0f, 4f, 0f)), 2));
            }
            else if (k == 75)
            {
                buffer.Add(MotionModifierCommand.End(new MotionSourceKey("S_Spin", Revision.FromUInt64(1))));
            }

            // 60~100: Up 기울이기.
            if (k == 60)
            {
                buffer.SetUp(new Vector3(0.3f, 1f, 0f));
            }
            else if (k == 100)
            {
                buffer.SetUp(Vector3.up);
            }

            // 70~120: 월드 선속도 바람, 120에 속도를 남기고 끝.
            if (k == 70)
            {
                buffer.Add(MotionModifierCommand.Apply(new MotionSourceKey("S_World", Revision.FromUInt64(1)), 25, NumericFOperation.ADD, MotionChannelValues.Linear(MotionChannel.Planar | MotionChannel.Elevation, new Vector3(2f, 3f, 1f))));
            }
            else if (k == 120)
            {
                buffer.Add(MotionModifierCommand.End(new MotionSourceKey("S_World", Revision.FromUInt64(1)), true));
            }

            // 90~110: 스턴(MUL 0).
            if (k == 90)
            {
                buffer.Add(MotionModifierCommand.Apply(new MotionSourceKey("S_Stun", Revision.FromUInt64(1)), 50, NumericFOperation.MUL, MotionChannelValues.Uniform(MotionChannel.Planar, 0f)));
            }
            else if (k == 110)
            {
                buffer.Add(MotionModifierCommand.End(new MotionSourceKey("S_Stun", Revision.FromUInt64(1))));
            }
        }

    #endregion

    #region 점검

        private ValidationCheckResult CheckReplay()
        {
            if (!justFinished)
            {
                return ValidationCheckResult.Skip;
            }

            justFinished = false;

            var detail = diffCount == 0
                ? "불일치 0"
                : $"불일치 {diffCount} Tick, 처음 Tick {firstDiffTick}{(lastWasSabotage ? " (충격 누락 — FAIL이 정상)" : string.Empty)}";

            return ValidationCheckResult.Of(diffCount == 0, detail);
        }

    #endregion

    #region 화면

        public override void DrawControls()
        {
            if (GUILayout.Button("[F] 저장 + 160 Tick 기록"))
            {
                StartRecording();
            }

            if (GUILayout.Button("[G] 되돌려 재실행 (PASS 기대)"))
            {
                StartReplay(false);
            }

            if (GUILayout.Button("[H] 충격 누락 재실행 (FAIL 기대)"))
            {
                StartReplay(true);
            }
        }

        public override void DrawPanel()
        {
            ValidationOverlay.Title("롤백·재실행");

            var progress = phase == Phase.Recording ? recorded.Count : phase == Phase.Replaying ? replayed.Count : TICKS;

            ValidationOverlay.Line($"상태 {phase}  진행 {progress}/{TICKS}  시작 Tick {startTick}");

            if (phase == Phase.Recorded || phase == Phase.Done)
            {
                ValidationOverlay.Line($"기록 끝: 위치 {ValidationOverlay.V(recordedPosition)} 속도 {ValidationOverlay.V(recordedVelocity)}");
            }

            if (phase == Phase.Done)
            {
                var ok = diffCount == 0;

                ValidationOverlay.Line($"프레임 비교: {(ok ? ValidationOverlay.Paint("일치 (PASS)", ValidationOverlay.PassColor) : ValidationOverlay.Paint($"불일치 {diffCount} Tick (첫 Tick {firstDiffTick})", ValidationOverlay.FailColor))}");
                ValidationOverlay.Line($"재실행 끝: 위치 {ValidationOverlay.V(replayedPosition)} 속도 {ValidationOverlay.V(replayedVelocity)}");
                ValidationOverlay.Line($"Body 차이: 위치 {(replayedPosition - recordedPosition).magnitude:F4}  속도 {(replayedVelocity - recordedVelocity).magnitude:F4}");

                if (lastWasSabotage)
                {
                    ValidationOverlay.Line("이번 재실행은 충격을 일부러 뺐으므로 불일치가 잡히는 것이 정상입니다.", ValidationOverlay.DimColor);
                }
            }
        }

    #endregion

    }
}
