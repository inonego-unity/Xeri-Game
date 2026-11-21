/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : MotionModel.cs
수정일 : 2026-11-22

# 설명
Actor 하나의 기본 의도(Base)와 Modifier 항목을 채널별 Core MValue(Planar·Elevation·Rotation)로 접어
Tick마다 하나의 MotionFrame으로 합성한다.

# 특이사항, 제약사항
Motion은 받은 값을 Order 순서로 접기만 한다. 값을 만드는 일(Track 재생, 감쇠, 벽 법선 제거)과
수명(지속 시간), 효과의 의미, Order 배치는 사용처 책임이다.
접기 규칙·순서·가려짐은 Core MValue와 Modifier(Replaces)를 따른다. Planar는 접은 결과만 Up에 수직인 평면에 투영한다.
채널에 기본 의도도 보이는 SET도 없으면 Free다. Free 채널은 ADD·SUB만 Body 속도 증분으로 넘기고 나머지 연산은 적용하지 않는다.
채널 MValue와 Modifier 인스턴스는 외부에 노출하지 않는다. 항목 상태는 TryGetModifier로 값만 읽는다.
Up 축은 Step 입력으로 Tick마다 바꿀 수 있다. Free 채널의 반영한 몫은 월드 벡터로 기록해 Up이 바뀌어도 같은 기준으로 비교한다.
Snapshot은 항목 메타데이터 배열과 채널 MValue 복제(CloneFrom)로 하며 구성이 같으면 할당이 없다.
끝난 항목의 세대는 기억하지 않는다. 늦게 도착한 입력의 중복 제거는 Host·Adapter 책임이다.
========================================================================= BLOCK_HEADER_END */

using System;

using UnityEngine;

using inonego;
using inonego.Xeri;
using inonego.Xeri.Primitive;
using inonego.Xeri.Serializable;

namespace inonego.Xeri.Game.Motion
{
    // ================================================================================
    /// <summary>
    /// <br/> Actor 하나의 기본 의도와 Modifier를 채널별 MValue로 합성하는 결정적 Model.
    /// <br/> Tick마다 Step을 호출해 MotionFrame을 얻는다.
    /// </summary>
    // ================================================================================
    public sealed class MotionModel
    {

    #region 필드

        // ------------------------------------------------------------
        /// <summary>
        /// 2D 위에서 보기(XY 평면 이동) Up 축.
        /// </summary>
        // ------------------------------------------------------------
        public static Vector3 TopDown2DUp => Vector3.forward;

        // ------------------------------------------------------------
        /// <summary>
        /// 2D 옆에서 보기(X 이동, Y 수직)와 3D 기본 Up 축.
        /// </summary>
        // ------------------------------------------------------------
        public static Vector3 WorldUp => Vector3.up;

        // ------------------------------------------------------------
        /// <summary>
        /// Model이 소유한 Actor.
        /// </summary>
        // ------------------------------------------------------------
        public MotionActorKey Actor { get; }

        // ----------------------------------------------------------------------
        /// <summary>
        /// Planar와 Elevation을 나누는 정규화된 현재 Up 축. Step 입력으로 바꿀 수 있다.
        /// </summary>
        // ----------------------------------------------------------------------
        public Vector3 Up => up;
        private Vector3 up;

        // ------------------------------------------------------------
        /// <summary>
        /// 동시에 유지할 수 있는 Modifier 항목 수.
        /// </summary>
        // ------------------------------------------------------------
        public int Capacity { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 마지막으로 실행한 Tick. 실행 전이면 -1.
        /// </summary>
        // ------------------------------------------------------------
        public long LastTick => lastTick;
        private long lastTick = -1;

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> Step과 Restore마다 증가하는 발급 토큰.
        /// <br/> Restore로 되돌리지 않아 이전 결과를 구분한다.
        /// </summary>
        // ------------------------------------------------------------
        public long StepVersion => stepVersion;
        private long stepVersion;

        // 채널 값. Base는 기본 의도, Modifier는 항목이다.
        private readonly MValue<Vector3> planar = new();
        private readonly MValue<float> elevation = new();
        private readonly MValue<Vector3> rotation = new();

        // 항목 메타데이터는 MValue와 같은 (Order, 시작 순서) 순으로 정렬한다.
        private readonly MotionModifierEntry[] entries;
        private int entryCount;

        // 항목별로 채널 MValue에 등록한 Modifier. 항목 배열과 같은 인덱스이며 Snapshot에 넣지 않는다.
        private readonly Vector3FModifier[] planarModifiers;
        private readonly NumericFModifier[] elevationModifiers;
        private readonly Vector3FModifier[] rotationModifiers;

        // 이미 Body 속도에 반영한 지속 ADD·SUB 합(월드 벡터).
        private Vector3 appliedLinear;

        // 한 Tick 수명의 기본 의도.
        private MotionChannel intentChannels;
        private Vector3 intentPlanar;
        private float intentElevation;
        private Vector3 intentRotation;

        // 한 번의 합성 안에서 채널별 보임 여부.
        private readonly bool[] visiblePlanar;
        private readonly bool[] visibleElevation;

    #endregion

    #region 생성자

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> Actor·Up 축·항목 용량으로 Model을 만든다.
        /// <br/> Up 축은 정규화해 보관한다.
        /// </summary>
        // ------------------------------------------------------------
        public MotionModel(MotionActorKey actor, Vector3 up, int capacity = 16)
        {
            if (actor.ScopeID <= 0)
            {
                throw new ArgumentException("유효한 Actor가 필요합니다.", nameof(actor));
            }

            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            if (!up.TryNormalize(out var unitUp))
            {
                throw new ArgumentOutOfRangeException(nameof(up));
            }

            Actor = actor;
            this.up = unitUp;
            Capacity = capacity;
            entries = new MotionModifierEntry[capacity];
            planarModifiers = new Vector3FModifier[capacity];
            elevationModifiers = new NumericFModifier[capacity];
            rotationModifiers = new Vector3FModifier[capacity];
            visiblePlanar = new bool[capacity];
            visibleElevation = new bool[capacity];
        }

    #endregion

    #region Step

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> Host가 정한 Tick으로 입력을 적용하고 합성 결과를 만든다.
        /// <br/> statuses를 넘기면 입력별 결과를 같은 순서로 기록한다.
        /// </summary>
        // ------------------------------------------------------------
        public MotionFrame Step
        (
            in MotionTick tick, in MotionStepInput input, in MotionStepStatuses statuses = default
        )
        {
            // 잘못된 Tick·결과 버퍼는 상태를 바꾸기 전에 거절한다.
            if (!tick.IsValid || !((float)tick.Delta > 0f))
            {
                throw new ArgumentOutOfRangeException(nameof(tick));
            }

            if (tick.Index <= lastTick)
            {
                throw new ArgumentOutOfRangeException(nameof(tick), "Tick은 마지막 실행 Tick보다 커야 합니다.");
            }

            ValidateStatuses(statuses.Intents, input.Intents.Length);
            ValidateStatuses(statuses.Modifiers, input.Modifiers.Length);

            var nextUp = ResolveUp(input);
            var upChanged = nextUp.x != up.x || nextUp.y != up.y || nextUp.z != up.z;

            up = nextUp;

            // 입력 적용은 채널마다 한 번만 다시 접도록 묶음 범위 안에서 한다.
            using (planar.BeginBatch())
            {
                using (elevation.BeginBatch())
                {
                    using (rotation.BeginBatch())
                    {
                        if (upChanged)
                        {
                            RefreshWorldElevation();
                        }

                        ApplyInput(input, tick.Index, statuses);

                        planar.Set(intentPlanar, invokeEvent: false);
                        elevation.Set(intentElevation, invokeEvent: false);
                        rotation.Set(intentRotation, invokeEvent: false);
                    }
                }
            }

            var frame = Compose(tick);

            RemoveImpulses();

            return frame;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 마지막 Tick 다음 번호로 Step을 실행한다.
        /// </summary>
        // ------------------------------------------------------------
        public MotionFrame StepLocal
        (
            double delta, in MotionStepInput input, in MotionStepStatuses statuses = default
        )
        {
            if (lastTick == long.MaxValue)
            {
                throw new InvalidOperationException("Step Index의 범위를 초과했습니다.");
            }

            return Step(new MotionTick(lastTick + 1, delta), input, statuses);
        }

        private Vector3 ResolveUp(in MotionStepInput input)
        {
            var requested = input.Up;

            // 비운 Up(영벡터)은 현재 Up을 유지한다.
            if (requested.x == 0f && requested.y == 0f && requested.z == 0f)
            {
                return up;
            }

            if (!requested.TryNormalize(out var unitUp))
            {
                throw new ArgumentOutOfRangeException(nameof(input), "Up은 유한한 비영 벡터여야 합니다.");
            }

            return unitUp;
        }

        private static void ValidateStatuses(Span<MotionInputStatus> statuses, int inputCount)
        {
            if (statuses.Length != 0 && statuses.Length < inputCount)
            {
                throw new ArgumentException("입력 결과 버퍼가 입력 수보다 작습니다.", nameof(statuses));
            }
        }

    #endregion

    #region 입력 적용

        private void ApplyInput(in MotionStepInput input, long tickIndex, in MotionStepStatuses statuses)
        {
            ClearIntent();

            for (var index = 0; index < input.Intents.Length; index++)
            {
                Record(statuses.Intents, index, ApplyIntent(input.Intents[index]));
            }

            for (var index = 0; index < input.Modifiers.Length; index++)
            {
                ref readonly var command = ref input.Modifiers[index];
                var status = command.Action switch
                {
                    MotionCommandAction.Apply => ApplyModifier(command, tickIndex),
                    MotionCommandAction.End => EndModifier(command),
                    MotionCommandAction.Clear => ClearModifiers(),
                    _ => MotionInputStatus.InvalidInput
                };

                Record(statuses.Modifiers, index, status);
            }
        }

        private static void Record(Span<MotionInputStatus> statuses, int index, MotionInputStatus status)
        {
            if (statuses.Length != 0)
            {
                statuses[index] = status;
            }
        }

    #endregion

    #region 기본 의도

        private MotionInputStatus ApplyIntent(in MotionIntent input)
        {
            if (!IsValidChannels(input.Channels) || !input.Linear.IsFinite() || !input.Angular.IsFinite())
            {
                return MotionInputStatus.InvalidInput;
            }

            var elevationValue = Vector3.Dot(input.Linear, Up);

            // 같은 Tick의 여러 의도는 채널별로 나중 값이 이긴다.
            if ((input.Channels & MotionChannel.Planar) != 0)
            {
                intentPlanar = input.Linear - Up * elevationValue;
            }

            if ((input.Channels & MotionChannel.Elevation) != 0)
            {
                intentElevation = elevationValue;
            }

            if ((input.Channels & MotionChannel.Rotation) != 0)
            {
                intentRotation = input.Angular;
            }

            intentChannels |= input.Channels;
            return MotionInputStatus.Accepted;
        }

        private void ClearIntent()
        {
            intentChannels = MotionChannel.None;
            intentPlanar = Vector3.zero;
            intentElevation = 0f;
            intentRotation = Vector3.zero;
        }

    #endregion

    #region Modifier 항목

        private MotionInputStatus ApplyModifier(in MotionModifierCommand input, long tickIndex)
        {
            if (!IsValid(input))
            {
                return MotionInputStatus.InvalidInput;
            }

            var existing = FindEntry(input.Source.Key);

            // 같은 키의 이전 세대는 새 항목이 대신하고, 같은 세대는 연산과 값만 갱신한다.
            if (existing >= 0)
            {
                ref var current = ref entries[existing];

                if (input.Source.Generation < current.Source.Generation)
                {
                    return MotionInputStatus.StaleGeneration;
                }

                if (input.Source.Generation == current.Source.Generation)
                {
                    // 채널·Order·충격 여부를 바꾸려면 세대를 올려 새로 시작해야 한다.
                    if (current.Channels != input.Channels || current.Order != input.Order || current.IsImpulse != input.IsImpulse)
                    {
                        return MotionInputStatus.InvalidInput;
                    }

                    UpdateEntry(existing, input);
                    return MotionInputStatus.Updated;
                }

                RemoveEntryAt(existing);
                InsertEntry(input, tickIndex);

                return MotionInputStatus.Accepted;
            }

            if (entryCount >= Capacity)
            {
                return MotionInputStatus.CapacityReached;
            }

            InsertEntry(input, tickIndex);
            return MotionInputStatus.Accepted;
        }

        private static bool IsValid(in MotionModifierCommand input)
        {
            if (!input.Source.IsValid || !IsValidChannels(input.Channels) || !input.Operation.IsDefined())
            {
                return false;
            }

            // 켠 채널이 쓰는 값만 검사한다. 월드 선속도는 Elevation도 Planar 값에서 나눈다.
            var channels = input.Channels;
            var values = input.Values;
            var usesPlanar = (channels & MotionChannel.Planar) != 0 || (values.IsWorldLinear && (channels & MotionChannel.Elevation) != 0);
            var usesElevation = !values.IsWorldLinear && (channels & MotionChannel.Elevation) != 0;

            return (!usesPlanar || values.Planar.IsFinite()) &&
                   (!usesElevation || values.Elevation.IsFinite()) &&
                   ((channels & MotionChannel.Rotation) == 0 || values.Rotation.IsFinite());
        }

        private void InsertEntry(in MotionModifierCommand input, long tickIndex)
        {
            var entry = new MotionModifierEntry
            {
                Source = input.Source,
                Channels = input.Channels,
                Order = input.Order,
                Operation = input.Operation,
                Values = Normalize(input.Values),
                ActionID = input.ActionID,
                StartTick = tickIndex,
                IsImpulse = input.IsImpulse
            };

            // MValue와 같은 순서(Order 오름차순, 같은 Order는 나중에 시작한 항목이 뒤)를 유지한다.
            var position = entryCount;

            while (position > 0 && entries[position - 1].Order > entry.Order)
            {
                position--;
            }

            ShiftRight(position);

            entries[position] = entry;
            entryCount++;

            Register(position);
        }

        private void UpdateEntry(int index, in MotionModifierCommand input)
        {
            // 시작 순서·시작 Tick은 유지하고 연산과 값만 바꾼다(사용처가 매 Tick 값을 갱신하는 경우).
            ref var entry = ref entries[index];

            entry.Operation = input.Operation;
            entry.Values = Normalize(input.Values);
            entry.ActionID = input.ActionID;

            WriteModifiers(index);
        }

        private MotionInputStatus EndModifier(in MotionModifierCommand input)
        {
            var index = FindEntry(input.Source);

            if (index < 0)
            {
                return MotionInputStatus.NotFound;
            }

            // 속도를 남기고 끝내면 이 항목이 Body에 남긴 몫을 반영한 몫에서 빼서
            // Free 채널에 그만큼 되돌리는 충격을 만들지 않는다.
            if (input.KeepVelocity)
            {
                appliedLinear -= entries[index].Contribution;
            }

            RemoveEntryAt(index);
            return MotionInputStatus.Accepted;
        }

        private MotionInputStatus ClearModifiers()
        {
            for (var index = entryCount - 1; index >= 0; index--)
            {
                RemoveEntryAt(index);
            }

            return MotionInputStatus.Accepted;
        }

        private void RemoveImpulses()
        {
            // 충격은 한 Tick만 적용한다.
            using (planar.BeginBatch())
            {
                using (elevation.BeginBatch())
                {
                    using (rotation.BeginBatch())
                    {
                        for (var index = entryCount - 1; index >= 0; index--)
                        {
                            if (entries[index].IsImpulse)
                            {
                                RemoveEntryAt(index);
                            }
                        }
                    }
                }
            }
        }

        private int FindEntry(string key)
        {
            for (var index = 0; index < entryCount; index++)
            {
                if (string.Equals(entries[index].Source.Key, key, StringComparison.Ordinal))
                {
                    return index;
                }
            }

            return -1;
        }

        private int FindEntry(MotionSourceKey source)
        {
            var index = source.IsValid ? FindEntry(source.Key) : -1;
            return index >= 0 && entries[index].Source.Generation == source.Generation ? index : -1;
        }

        private void RemoveEntryAt(int index)
        {
            Unregister(index);
            ShiftLeft(index);
            entryCount--;
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> 같은 키·Generation의 Modifier 항목 상태를 읽는다.
        /// <br/> 충격 항목은 Step이 끝나면 사라지므로 Step 뒤에는 찾을 수 없다.
        /// </summary>
        // ----------------------------------------------------------------------
        public bool TryGetModifier(MotionSourceKey source, out MotionModifierState state)
        {
            var index = FindEntry(source);

            if (index < 0)
            {
                state = default;
                return false;
            }

            ref readonly var entry = ref entries[index];

            state = new MotionModifierState
            (
                entry.Source, entry.Order, entry.Operation, entry.Values,
                entry.ActionID, entry.StartTick, entry.IsImpulse, entry.IsVisible
            );

            return true;
        }

    #endregion

    #region 채널 등록

        private void Register(int index)
        {
            ref readonly var entry = ref entries[index];
            var key = entry.Source.Key;
            var values = entry.Values;

            if ((entry.Channels & MotionChannel.Planar) != 0)
            {
                var modifier = new Vector3FModifier(entry.Operation, values.Planar);

                planar.AddModifier(key, modifier, entry.Order, invokeEvent: false);
                planarModifiers[index] = modifier;
            }

            if ((entry.Channels & MotionChannel.Elevation) != 0)
            {
                var modifier = new NumericFModifier(entry.Operation, ElevationOf(values));

                elevation.AddModifier(key, modifier, entry.Order, invokeEvent: false);
                elevationModifiers[index] = modifier;
            }

            if ((entry.Channels & MotionChannel.Rotation) != 0)
            {
                var modifier = new Vector3FModifier(entry.Operation, values.Rotation);

                rotation.AddModifier(key, modifier, entry.Order, invokeEvent: false);
                rotationModifiers[index] = modifier;
            }
        }

        private void Unregister(int index)
        {
            var key = entries[index].Source.Key;

            if (planarModifiers[index] != null)
            {
                planar.RemoveModifier(key, invokeEvent: false);
            }

            if (elevationModifiers[index] != null)
            {
                elevation.RemoveModifier(key, invokeEvent: false);
            }

            if (rotationModifiers[index] != null)
            {
                rotation.RemoveModifier(key, invokeEvent: false);
            }
        }

        private void WriteModifiers(int index)
        {
            ref readonly var entry = ref entries[index];
            var values = entry.Values;

            if (planarModifiers[index] != null)
            {
                planarModifiers[index].Operation = entry.Operation;
                planarModifiers[index].Value = values.Planar;
            }

            if (elevationModifiers[index] != null)
            {
                elevationModifiers[index].Operation = entry.Operation;
                elevationModifiers[index].Value = ElevationOf(values);
            }

            if (rotationModifiers[index] != null)
            {
                rotationModifiers[index].Operation = entry.Operation;
                rotationModifiers[index].Value = values.Rotation;
            }
        }

        private void ShiftRight(int position)
        {
            for (var index = entryCount; index > position; index--)
            {
                entries[index] = entries[index - 1];
                planarModifiers[index] = planarModifiers[index - 1];
                elevationModifiers[index] = elevationModifiers[index - 1];
                rotationModifiers[index] = rotationModifiers[index - 1];
            }

            planarModifiers[position] = null;
            elevationModifiers[position] = null;
            rotationModifiers[position] = null;
        }

        private void ShiftLeft(int position)
        {
            var last = entryCount - 1;

            for (var index = position; index < last; index++)
            {
                entries[index] = entries[index + 1];
                planarModifiers[index] = planarModifiers[index + 1];
                elevationModifiers[index] = elevationModifiers[index + 1];
                rotationModifiers[index] = rotationModifiers[index + 1];
            }

            entries[last] = default;
            planarModifiers[last] = null;
            elevationModifiers[last] = null;
            rotationModifiers[last] = null;
        }

        private void ResolveModifiers()
        {
            // Restore 뒤 채널 MValue가 가진 인스턴스를 항목 순서에 다시 연결한다.
            for (var index = 0; index < Capacity; index++)
            {
                planarModifiers[index] = null;
                elevationModifiers[index] = null;
                rotationModifiers[index] = null;

                if (index >= entryCount)
                {
                    continue;
                }

                var key = entries[index].Source.Key;

                if (planar.Modifiers.TryGetValue(key, out var planarModifier))
                {
                    planarModifiers[index] = (Vector3FModifier)planarModifier;
                }

                if (elevation.Modifiers.TryGetValue(key, out var elevationModifier))
                {
                    elevationModifiers[index] = (NumericFModifier)elevationModifier;
                }

                if (rotation.Modifiers.TryGetValue(key, out var rotationModifier))
                {
                    rotationModifiers[index] = (Vector3FModifier)rotationModifier;
                }
            }
        }

    #endregion

    #region 합성

        private MotionFrame Compose(in MotionTick tick)
        {
            for (var index = 0; index < entryCount; index++)
            {
                entries[index].IsVisible = false;
            }

            Array.Clear(visiblePlanar, 0, entryCount);
            Array.Clear(visibleElevation, 0, entryCount);

            var hasPlanarReplace = MarkVisible(planar, MotionChannel.Planar, visiblePlanar);
            var hasElevationReplace = MarkVisible(elevation, MotionChannel.Elevation, visibleElevation);
            var hasRotationReplace = MarkVisible(rotation, MotionChannel.Rotation, null);

            // 기본 의도나 보이는 SET이 있으면 Set, 없으면 Free다.
            var planarMode = ResolveMode(MotionChannel.Planar, hasPlanarReplace);
            var elevationMode = ResolveMode(MotionChannel.Elevation, hasElevationReplace);
            var rotationMode = ResolveMode(MotionChannel.Rotation, hasRotationReplace);

            SumAdditive(out var sustained, out var planarImpulse, out var elevationImpulse);

            // Free 채널은 지속 ADD·SUB 합의 이번 변화량과 충격만 Body 속도에 더한다.
            // 반영한 몫은 월드 벡터라 Up이 바뀌어도 같은 기준으로 비교하고,
            // Set 채널에서도 기록해 Free로 바뀔 때 같은 값을 다시 더하지 않는다.
            var change = sustained - appliedLinear;
            var frameImpulse = Vector3.zero;

            if (planarMode == MotionChannelMode.Free)
            {
                frameImpulse += ProjectPlanar(change) + planarImpulse;
            }

            if (elevationMode == MotionChannelMode.Free)
            {
                frameImpulse += Up * (Vector3.Dot(change, Up) + elevationImpulse);
            }

            appliedLinear = sustained;

            ResolveAction(out var actionID, out var actionStartTick);

            lastTick = tick.Index;
            stepVersion = checked(stepVersion + 1);

            return new MotionFrame
            (
                Actor, tick, stepVersion, Up,
                planarMode, planarMode == MotionChannelMode.Set ? ProjectPlanar(planar.Modified) : Vector3.zero,
                elevationMode, elevationMode == MotionChannelMode.Set ? elevation.Modified : 0f,
                rotationMode, rotationMode == MotionChannelMode.Set ? rotation.Modified : Vector3.zero,
                frameImpulse, actionID, actionStartTick
            );
        }

        private MotionChannelMode ResolveMode(MotionChannel flag, bool hasReplace) =>
            (intentChannels & flag) != 0 || hasReplace ? MotionChannelMode.Set : MotionChannelMode.Free;

        private bool MarkVisible<T>(MValue<T> channel, MotionChannel flag, bool[] channelVisible)
        {
            // 채널 목록에서 마지막으로 입력을 덮어쓰는 Modifier보다 앞의 항목은 그 채널에서 가려진다.
            var modifiers = channel.Modifiers;
            var lastReplace = -1;

            for (var position = 0; position < modifiers.Count; position++)
            {
                var modifier = modifiers[position].Value;

                if (modifier != null && modifier.Replaces)
                {
                    lastReplace = position;
                }
            }

            // 항목 배열은 채널 목록과 같은 순서이므로 채널 안 순위로 위치를 맞춘다.
            var rank = 0;

            for (var index = 0; index < entryCount; index++)
            {
                if ((entries[index].Channels & flag) == 0)
                {
                    continue;
                }

                if (rank >= lastReplace)
                {
                    entries[index].IsVisible = true;

                    if (channelVisible != null)
                    {
                        channelVisible[index] = true;
                    }
                }

                rank++;
            }

            return lastReplace >= 0;
        }

        private void SumAdditive(out Vector3 sustained, out Vector3 planarImpulse, out float elevationImpulse)
        {
            sustained = planarImpulse = Vector3.zero;
            elevationImpulse = 0f;

            for (var index = 0; index < entryCount; index++)
            {
                ref var entry = ref entries[index];

                entry.Contribution = Vector3.zero;

                var sign = entry.Operation switch
                {
                    NumericFOperation.ADD => 1f,
                    NumericFOperation.SUB => -1f,
                    _ => 0f
                };

                if (sign == 0f)
                {
                    continue;
                }

                // 채널마다 보이는 항목만 더한다. 충격은 지속 몫과 따로 모은다.
                var planarValue = visiblePlanar[index] ? ProjectPlanar(entry.Values.Planar * sign) : Vector3.zero;
                var elevationValue = visibleElevation[index] ? ElevationOf(entry.Values) * sign : 0f;

                if (entry.IsImpulse)
                {
                    planarImpulse += planarValue;
                    elevationImpulse += elevationValue;
                    continue;
                }

                // 지속 항목은 이번에 반영한 몫을 기록해 두고, 속도를 남기고 끝낼 때 반영한 몫에서 뺀다.
                entry.Contribution = planarValue + Up * elevationValue;
                sustained += entry.Contribution;
            }
        }

        private void ResolveAction(out int actionID, out long startTick)
        {
            // 배열에서 가장 뒤의 SET이 현재 동작이다. 자기 채널에서 뒤에 SET이 없으므로 항상 보인다.
            actionID = 0;
            startTick = -1;

            for (var index = entryCount - 1; index >= 0; index--)
            {
                if (entries[index].Operation == NumericFOperation.SET)
                {
                    actionID = entries[index].ActionID;
                    startTick = entries[index].StartTick;
                    return;
                }
            }
        }

    #endregion

    #region Snapshot

        // ------------------------------------------------------------
        /// <summary>
        /// 현재 상태를 새 Snapshot에 복사한다.
        /// </summary>
        // ------------------------------------------------------------
        public MotionModelSnapshot Capture()
        {
            var snapshot = new MotionModelSnapshot(Capacity);

            Capture(snapshot);

            return snapshot;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> 현재 상태를 기존 Snapshot에 복사한다.
        /// <br/> 같은 용량의 Snapshot을 재사용하고 구성이 같으면 할당이 없다.
        /// </summary>
        // ------------------------------------------------------------
        public void Capture(MotionModelSnapshot target)
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            if (target.Capacity != Capacity)
            {
                throw new ArgumentException("Snapshot 용량이 Model과 다릅니다.", nameof(target));
            }

            Array.Copy(entries, target.Entries, Capacity);
            target.EntryCount = entryCount;
            target.AppliedLinear = appliedLinear;
            target.Up = up;
            target.Actor = Actor;
            target.LastTick = lastTick;

            target.Planar.CloneFrom(planar);
            target.Elevation.CloneFrom(elevation);
            target.Rotation.CloneFrom(rotation);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> 같은 Actor의 Snapshot으로 상태를 되돌린다.
        /// <br/> StepVersion은 되돌리지 않고 새로 발급한다.
        /// </summary>
        // ------------------------------------------------------------
        public void Restore(MotionModelSnapshot snapshot)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            if (!snapshot.Actor.Equals(Actor))
            {
                throw new ArgumentException("다른 Actor의 Snapshot입니다.", nameof(snapshot));
            }

            if (snapshot.Capacity != Capacity)
            {
                throw new ArgumentException("Snapshot 용량이 Model과 다릅니다.", nameof(snapshot));
            }

            Array.Copy(snapshot.Entries, entries, Capacity);
            entryCount = snapshot.EntryCount;
            appliedLinear = snapshot.AppliedLinear;
            up = snapshot.Up;
            lastTick = snapshot.LastTick;

            planar.CloneFrom(snapshot.Planar);
            elevation.CloneFrom(snapshot.Elevation);
            rotation.CloneFrom(snapshot.Rotation);

            ResolveModifiers();

            stepVersion = checked(stepVersion + 1);
        }

    #endregion

    #region 유틸

        private Vector3 ProjectPlanar(Vector3 value) => value - Up * Vector3.Dot(value, Up);

        // 쓰지 않는 값은 0으로 보관해 검사하지 않은 값이 상태에 남지 않게 한다.
        private static MotionChannelValues Normalize(in MotionChannelValues values)
        {
            var channels = values.Channels;
            var world = values.IsWorldLinear;
            var keepPlanar = (channels & MotionChannel.Planar) != 0 || (world && (channels & MotionChannel.Elevation) != 0);
            var keepElevation = !world && (channels & MotionChannel.Elevation) != 0;

            return new MotionChannelValues
            (
                channels,
                keepPlanar ? values.Planar : Vector3.zero,
                keepElevation ? values.Elevation : 0f,
                (channels & MotionChannel.Rotation) != 0 ? values.Rotation : Vector3.zero,
                world
            );
        }

        // 월드 선속도는 현재 Up 방향 성분을, 직접 지정한 값은 그대로 Elevation으로 쓴다.
        private float ElevationOf(in MotionChannelValues values) =>
            values.IsWorldLinear ? Vector3.Dot(values.Planar, up) : values.Elevation;

        private void RefreshWorldElevation()
        {
            // Up이 바뀐 Tick에는 월드 선속도 항목의 Elevation 값을 새 Up으로 다시 나눈다.
            for (var index = 0; index < entryCount; index++)
            {
                if (entries[index].Values.IsWorldLinear && elevationModifiers[index] != null)
                {
                    elevationModifiers[index].Value = ElevationOf(entries[index].Values);
                }
            }
        }

        private static bool IsValidChannels(MotionChannel channels) =>
            channels != MotionChannel.None && (channels & ~MotionChannel.All) == MotionChannel.None;

    #endregion

    }
}
