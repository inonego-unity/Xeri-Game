/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : ValidationActor.cs
수정일 : 2026-11-22

# 설명
검증 카드 하나가 쓰는 큐브 Actor. Rigidbody 큐브, MotionModel, Motor, 입력 버퍼를 묶고
Tick마다 합성 → 적용 → Physics 시작 → Physics 완료 순서를 지킨다.

# 특이사항, 제약사항
Physics.Simulate는 호출자(씬)가 Prepare와 Complete 사이에서 한 번 실행한다.
입력별 결과는 마지막 Step 기준으로 기록해 카드가 읽는다.
Model은 ResetState에서 새로 만든다. Motor는 Body와 같은 수명이라 유지한다.
========================================================================= BLOCK_HEADER_END */

using System;
using System.Collections.Generic;

using UnityEngine;

using inonego;
using inonego.Xeri;
using inonego.Xeri.Game;
using inonego.Xeri.Game.Motion;
using inonego.Xeri.Game.Motor;

namespace inonego.Xeri.Game.Validation.Locomotion
{
    // ============================================================
    /// <summary>
    /// 한 Tick에 Modifier 명령 하나가 받은 결과.
    /// </summary>
    // ============================================================
    internal readonly struct ValidationCommandResult
    {
        public readonly MotionCommandAction Action;
        public readonly string Key;
        public readonly long Generation;
        public readonly bool IsImpulse;
        public readonly MotionInputStatus Status;

        public ValidationCommandResult(in MotionModifierCommand command, MotionInputStatus status)
        {
            Action = command.Action;
            Key = command.Source.Key ?? "(none)";
            Generation = command.Source.Generation;
            IsImpulse = command.IsImpulse;
            Status = status;
        }

        public override string ToString() =>
            $"{Action}{(IsImpulse ? "(Impulse)" : string.Empty)} {Key}#{Generation} → {Status}";
    }

    // ============================================================
    /// <summary>
    /// 큐브 Body와 Motion·Motor를 묶은 검증용 Actor.
    /// </summary>
    // ============================================================
    internal sealed class ValidationActor
    {

    #region 필드

        private const int LOG_LIMIT = 14;

        public string Name { get; }
        public GameObject Cube { get; }
        public Rigidbody Body { get; }
        public MotionModel Model { get; private set; }
        public Motor<MotorState3D> Mover { get; }
        public MotionInputBuffer Buffer { get; } = new MotionInputBuffer();
        public MotionActorKey Key { get; }
        public int Capacity { get; }

        // 마지막 Step의 합성 결과와 Body 적용 성공 여부.
        public MotionFrame LastFrame { get; private set; }
        public bool HasFrame { get; private set; }
        public bool LastApplied { get; private set; }

        // 마지막 Step에서 명령별로 받은 결과와 누적 기록.
        public List<ValidationCommandResult> LastResults { get; } = new List<ValidationCommandResult>();
        public List<string> Log { get; } = new List<string>();

        private MotionInputStatus[] intentStatuses = new MotionInputStatus[16];
        private MotionInputStatus[] modifierStatuses = new MotionInputStatus[32];

        private readonly LineRenderer bodyLine;
        private readonly LineRenderer frameLine;

    #endregion

    #region 생성자

        // ------------------------------------------------------------
        /// <summary>
        /// 큐브와 Rigidbody, Model, Motor를 만든다.
        /// </summary>
        // ------------------------------------------------------------
        public ValidationActor
        (
            string name, Transform parent, Vector3 localPosition, Color color, int actorId, int capacity = 16
        )
        {
            Name = name;
            Capacity = capacity;
            Cube = ValidationFactory.CreateBox(name, parent, localPosition, Vector3.one, color);

            // 바라보는 쪽(+Z)을 작은 어두운 상자로 표시한다.
            ValidationFactory.CreateBox("Front", Cube.transform, new Vector3(0f, 0f, 0.55f), new Vector3(0.5f, 0.5f, 0.2f), ValidationFactory.Darken(color, 0.4f), false);

            Body = Cube.AddComponent<Rigidbody>();
            Body.mass = 1f;
            Body.linearDamping = 0f;
            Body.angularDamping = 0f;
            Body.sleepThreshold = 0f;
            Body.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;

            Key = new MotionActorKey(1, actorId, 1);
            Model = new MotionModel(Key, Vector3.up, capacity);
            Mover = new Motor<MotorState3D>(Key, new RigidbodyMotorBackend3D(Body));

            bodyLine = ValidationFactory.CreateLine("BodyVelocity", Cube.transform, Color.white, 0.07f);
            frameLine = ValidationFactory.CreateLine("FramePlanar", Cube.transform, new Color(0.2f, 1f, 0.3f), 0.07f);
        }

    #endregion

    #region 상태

        // ------------------------------------------------------------
        /// <summary>
        /// 월드 위치로 Body를 되돌리고 Model·입력·기록을 새로 만든다.
        /// </summary>
        // ------------------------------------------------------------
        public void ResetState(Vector3 worldPosition)
        {
            Body.linearVelocity = Vector3.zero;
            Body.angularVelocity = Vector3.zero;
            Body.position = worldPosition;
            Body.rotation = Quaternion.identity;
            Cube.transform.SetPositionAndRotation(worldPosition, Quaternion.identity);

            Physics.SyncTransforms();

            Model = new MotionModel(Key, Vector3.up, Capacity);
            Buffer.Clear();
            HasFrame = false;
            LastApplied = false;
            LastResults.Clear();
            Log.Clear();
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 월드 위치로 Body만 옮기고 속도를 지운다. Model은 유지한다.
        /// </summary>
        // ------------------------------------------------------------
        public void Teleport(Vector3 worldPosition)
        {
            Body.linearVelocity = Vector3.zero;
            Body.position = worldPosition;
            Cube.transform.position = worldPosition;

            Physics.SyncTransforms();
        }

    #endregion

    #region Tick

        // ------------------------------------------------------------
        /// <summary>
        /// 버퍼의 입력으로 Step을 실행하고 Body에 적용한 뒤 Physics Step을 시작한다.
        /// </summary>
        // ------------------------------------------------------------
        public void Prepare(float dt)
        {
            var input = Buffer.AsStepInput();
            var intentCount = input.Intents.Length;
            var modifierCount = input.Modifiers.Length;

            EnsureStatusCapacity(intentCount, modifierCount);

            var statuses = new MotionStepStatuses
            (
                new Span<MotionInputStatus>(intentStatuses, 0, intentCount),
                new Span<MotionInputStatus>(modifierStatuses, 0, modifierCount)
            );

            LastFrame = Model.StepLocal(dt, input, statuses);
            HasFrame = true;

            // 입력별 결과는 Updated(매 Tick 갱신)를 빼고 기록해 로그가 갱신으로 넘치지 않게 한다.
            LastResults.Clear();

            for (var index = 0; index < modifierCount; index++)
            {
                var result = new ValidationCommandResult(input.Modifiers[index], modifierStatuses[index]);

                LastResults.Add(result);

                if (result.Status != MotionInputStatus.Updated)
                {
                    PushLog($"t{LastFrame.Tick.Index} {result}");
                }
            }

            Buffer.Clear();

            LastApplied = Mover.TryApply(LastFrame);
            Mover.BeginPhysicsStep();
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Physics Step이 끝난 뒤 Motor에 완료를 알린다.
        /// </summary>
        // ------------------------------------------------------------
        public void Complete()
        {
            Mover.CompletePhysicsStep(LastFrame.Tick.Index);
        }

        private void EnsureStatusCapacity(int intentCount, int modifierCount)
        {
            if (intentStatuses.Length < intentCount)
            {
                intentStatuses = new MotionInputStatus[intentCount * 2];
            }

            if (modifierStatuses.Length < modifierCount)
            {
                modifierStatuses = new MotionInputStatus[modifierCount * 2];
            }
        }

        private void PushLog(string line)
        {
            Log.Add(line);

            if (Log.Count > LOG_LIMIT)
            {
                Log.RemoveAt(0);
            }
        }

    #endregion

    #region 표시

        // ------------------------------------------------------------
        /// <summary>
        /// Body 속도(흰색)와 합성 Planar 값(초록) 화살표를 갱신한다.
        /// </summary>
        // ------------------------------------------------------------
        public void UpdateVisuals()
        {
            var origin = Body.position + Vector3.up * 0.8f;

            bodyLine.SetPosition(0, origin);
            bodyLine.SetPosition(1, origin + Body.linearVelocity * 0.3f);

            var planar = HasFrame && LastFrame.PlanarMode == MotionChannelMode.Set ? LastFrame.Planar : Vector3.zero;

            frameLine.SetPosition(0, origin + Vector3.up * 0.15f);
            frameLine.SetPosition(1, origin + Vector3.up * 0.15f + planar * 0.3f);
        }

    #endregion

    }
}
