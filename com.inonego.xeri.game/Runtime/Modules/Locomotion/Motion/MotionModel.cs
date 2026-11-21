/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : MotionModel.cs
수정일 : 2026-11-21

# 설명
Actor 하나의 운동 기여를 기본 의도·Override·제어 배율·외부 속도 네 층으로 보관하고 Tick마다 하나의 MotionFrame으로 합성한다.

# 특이사항, 제약사항
층별 입력을 ReadOnlySpan 값 묶음(MotionStepInput)으로 받고 Step 중 외부 코드를 호출하지 않는다.
층 상태는 고정 용량 값 구조체 배열이며 Snapshot 복사에 할당이 없다.
배율 연산 규칙은 Core NumericFModifier.Modify와 같다. 같은 우선순위 Override는 나중에 시작한 쪽이 위다.
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
    // ============================================================
    /// <summary>
    /// <br/> Actor 하나의 운동 기여를 네 층으로 합성하는 결정적 Model.
    /// <br/> Tick마다 Step을 호출해 MotionFrame을 얻는다.
    /// </summary>
    // ============================================================
    public sealed class MotionModel
    {

    #region 내부 데이터

        private const float EXTERNAL_EPSILON_SQR = 1e-8f;

        private struct Composition
        {
            // ------------------------------------------------------------
            /// <summary>
            /// Planar 채널의 합성 모드.
            /// </summary>
            // ------------------------------------------------------------
            public MotionChannelMode PlanarMode;

            // ------------------------------------------------------------
            /// <summary>
            /// 합성된 Planar 속도.
            /// </summary>
            // ------------------------------------------------------------
            public Vector3 Planar;

            // ------------------------------------------------------------
            /// <summary>
            /// Elevation 채널의 합성 모드.
            /// </summary>
            // ------------------------------------------------------------
            public MotionChannelMode ElevationMode;

            // ------------------------------------------------------------
            /// <summary>
            /// 합성된 Elevation 속도.
            /// </summary>
            // ------------------------------------------------------------
            public float Elevation;

            // ------------------------------------------------------------
            /// <summary>
            /// Rotation 채널의 합성 모드.
            /// </summary>
            // ------------------------------------------------------------
            public MotionChannelMode RotationMode;

            // ------------------------------------------------------------
            /// <summary>
            /// 합성된 각속도.
            /// </summary>
            // ------------------------------------------------------------
            public Vector3 Rotation;

            // ------------------------------------------------------------
            /// <summary>
            /// 이번 Tick의 충격 속도 합.
            /// </summary>
            // ------------------------------------------------------------
            public Vector3 Impulse;

            // ------------------------------------------------------------
            /// <summary>
            /// 합성 결과의 동작 식별자.
            /// </summary>
            // ------------------------------------------------------------
            public int ActionID;

            // ------------------------------------------------------------
            /// <summary>
            /// 현재 동작이 시작된 Tick.
            /// </summary>
            // ------------------------------------------------------------
            public long ActionStartTick;

            // ------------------------------------------------------------
            /// <summary>
            /// 차단 법선으로 막힌 채널.
            /// </summary>
            // ------------------------------------------------------------
            public MotionChannel Blocked;
        }

    #endregion

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

        // ------------------------------------------------------------
        /// <summary>
        /// Planar와 Elevation을 나누는 정규화된 Up 축.
        /// </summary>
        // ------------------------------------------------------------
        public Vector3 Up { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 층별 항목 용량.
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

        private readonly MotionTrackLibrary tracks;

        private readonly MotionOverrideEntry[] overrides;
        private readonly MotionScaleEntry[] scales;
        private readonly MotionExternalEntry[] externals;
        private int overrideCount;
        private int scaleCount;
        private int externalCount;
        private Vector3 appliedExternal;
        private long nextSequence = 1;

        // 한 Tick 수명의 기본 의도와 합성 중 찾은 채널별 최상위 Override.
        private MotionChannel intentChannels;
        private Vector3 intentPlanar;
        private float intentElevation;
        private Vector3 intentRotation;
        private int topPlanar = -1;
        private int topElevation = -1;
        private int topRotation = -1;

        // 한 Compose 안에서 같은 Track Override를 채널마다 다시 평가하지 않도록 마지막 평가를 보관한다.
        private int evaluatedOverride = -1;
        private Vector3 evaluatedLinear;
        private Vector3 evaluatedAngular;

    #endregion

    #region 생성자

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> Actor·Up 축·Track Library·층별 용량으로 Model을 만든다.
        /// <br/> Up 축은 정규화해 보관한다.
        /// </summary>
        // ------------------------------------------------------------
        public MotionModel(MotionActorKey actor, Vector3 up, MotionTrackLibrary tracks = null, int capacity = 16)
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
            Up = unitUp;
            Capacity = capacity;
            this.tracks = tracks ?? MotionTrackLibrary.Empty;
            overrides = new MotionOverrideEntry[capacity];
            scales = new MotionScaleEntry[capacity];
            externals = new MotionExternalEntry[capacity];
        }

    #endregion

    #region Step

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> Host가 정한 Tick으로 층별 입력을 적용하고 합성 결과를 만든다.
        /// <br/> statuses를 넘기면 층별 입력 결과를 같은 순서로 기록한다.
        /// </summary>
        // ----------------------------------------------------------------------
        public MotionFrame Step
        (
            in MotionTick tick, in MotionStepInput input, in MotionFeedback feedback,
            in MotionStepStatuses statuses = default
        )
        {
            // 잘못된 Tick·Impulse·결과 버퍼는 상태를 바꾸기 전에 거절한다.
            if (!tick.IsValid || !((float)tick.Delta > 0f))
            {
                throw new ArgumentOutOfRangeException(nameof(tick));
            }

            if (tick.Index <= lastTick)
            {
                throw new ArgumentOutOfRangeException(nameof(tick), "Tick은 마지막 실행 Tick보다 커야 합니다.");
            }

            if (!input.Impulse.IsFinite())
            {
                throw new ArgumentOutOfRangeException(nameof(input), "Impulse는 유한해야 합니다.");
            }

            ValidateStatuses(statuses.Intents, input.Intents.Length);
            ValidateStatuses(statuses.Overrides, input.Overrides.Length);
            ValidateStatuses(statuses.Scales, input.Scales.Length);
            ValidateStatuses(statuses.Externals, input.Externals.Length);

            ApplyInput(input, tick.Index, statuses);

            // 직전 Step의 차단 법선으로 외부 속도를 깎은 뒤 합성하고, 그다음 시간을 흘린다.
            var delta = (float)tick.Delta;
            RemoveBlockedExternal(feedback);
            var composition = Compose(delta, feedback, input.Impulse);
            Advance(delta);

            lastTick = tick.Index;
            stepVersion = checked(stepVersion + 1);

            return new MotionFrame
            (
                Actor, tick, stepVersion, Up,
                composition.PlanarMode, composition.Planar,
                composition.ElevationMode, composition.Elevation,
                composition.RotationMode, composition.Rotation,
                composition.Impulse, composition.ActionID, composition.ActionStartTick, composition.Blocked
            );
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 마지막 Tick 다음 번호로 Step을 실행한다.
        /// </summary>
        // ------------------------------------------------------------
        public MotionFrame StepLocal
        (
            double delta, in MotionStepInput input, in MotionFeedback feedback,
            in MotionStepStatuses statuses = default
        )
        {
            if (lastTick == long.MaxValue)
            {
                throw new InvalidOperationException("Step Index의 범위를 초과했습니다.");
            }

            return Step(new MotionTick(lastTick + 1, delta), input, feedback, statuses);
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
            // 층끼리는 순서 의존이 없으므로 층마다 Span 순서대로 적용한다.
            ClearIntent();

            for (var index = 0; index < input.Intents.Length; index++)
            {
                Record(statuses.Intents, index, ApplyIntent(input.Intents[index]));
            }

            for (var index = 0; index < input.Overrides.Length; index++)
            {
                ref readonly var command = ref input.Overrides[index];
                var status = command.Action switch
                {
                    MotionCommandAction.Set => BeginOverride(command, tickIndex),
                    MotionCommandAction.Remove => EndOverride(command.Source),
                    _ => MotionInputStatus.InvalidInput
                };

                Record(statuses.Overrides, index, status);
            }

            for (var index = 0; index < input.Scales.Length; index++)
            {
                ref readonly var command = ref input.Scales[index];
                var status = command.Action switch
                {
                    MotionCommandAction.Set => AddScale(command),
                    MotionCommandAction.Remove => RemoveScale(command.Source),
                    _ => MotionInputStatus.InvalidInput
                };

                Record(statuses.Scales, index, status);
            }

            for (var index = 0; index < input.Externals.Length; index++)
            {
                ref readonly var command = ref input.Externals[index];
                var status = command.Action switch
                {
                    MotionCommandAction.Set => SetExternal(command),
                    MotionCommandAction.Remove => RemoveExternal(command.Source),
                    MotionCommandAction.Clear => ClearExternal(),
                    _ => MotionInputStatus.InvalidInput
                };

                Record(statuses.Externals, index, status);
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

    #region 기본 의도 층

        private MotionInputStatus ApplyIntent(in MotionIntent input)
        {
            if (!IsValidChannels(input.Channels) || !input.Linear.IsFinite() || !input.Angular.IsFinite())
            {
                return MotionInputStatus.InvalidInput;
            }

            SplitLinear(input.Linear, out var planar, out var elevation);

            // 같은 Tick의 여러 의도는 채널별로 나중 값이 이긴다.
            if ((input.Channels & MotionChannel.Planar) != 0)
            {
                intentPlanar = planar;
            }

            if ((input.Channels & MotionChannel.Elevation) != 0)
            {
                intentElevation = elevation;
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

    #region Override 층

        private MotionInputStatus BeginOverride(in MotionOverrideCommand input, long tickIndex)
        {
            if (!input.Source.IsValid || !IsValidChannels(input.Channels) ||
                !input.Linear.IsFinite() || !input.Angular.IsFinite() || !IsValidDuration(input.Duration))
            {
                return MotionInputStatus.InvalidInput;
            }

            if (input.TrackID != 0)
            {
                if (input.TrackID < 0 || !input.TrackRate.IsFinite() || input.TrackRate <= 0f)
                {
                    return MotionInputStatus.InvalidInput;
                }

                if (!tracks.TryGet(input.TrackID, out _))
                {
                    return MotionInputStatus.UnknownTrack;
                }
            }

            // 같은 슬롯의 이전 세대는 새 수명이 대신하고, 더 새로운 세대가 있으면 거절한다.
            var existing = FindOverride(input.Source.ID);

            if (existing >= 0)
            {
                var status = CompareGeneration(overrides[existing].Source, input.Source);

                if (status != MotionInputStatus.Accepted)
                {
                    return status;
                }

                RemoveOverrideAt(existing);
            }

            if (overrideCount >= Capacity)
            {
                return MotionInputStatus.CapacityReached;
            }

            overrides[overrideCount++] = new MotionOverrideEntry
            {
                Source = input.Source,
                Channels = input.Channels,
                Priority = input.Priority,
                Sequence = IssueSequence(),
                Linear = input.Linear,
                Angular = input.Angular,
                HasDuration = input.Duration > 0f,
                Remaining = input.Duration,
                TrackID = input.TrackID,
                TrackRate = input.TrackID != 0 ? input.TrackRate : 0f,
                TrackElapsed = 0f,
                ActionID = input.ActionID,
                StartTick = tickIndex
            };

            return MotionInputStatus.Accepted;
        }

        private MotionInputStatus EndOverride(MotionSourceKey source)
        {
            var index = FindOverride(source.ID);

            if (index < 0 || !overrides[index].Source.Equals(source))
            {
                return MotionInputStatus.NotFound;
            }

            RemoveOverrideAt(index);
            return MotionInputStatus.Accepted;
        }

        private int FindOverride(long id)
        {
            for (var index = 0; index < overrideCount; index++)
            {
                if (overrides[index].Source.ID == id)
                {
                    return index;
                }
            }

            return -1;
        }

        private void RemoveOverrideAt(int index)
        {
            Array.Copy(overrides, index + 1, overrides, index, overrideCount - index - 1);
            overrides[--overrideCount] = default;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 같은 식별자의 Override가 활성인지 확인한다.
        /// </summary>
        // ------------------------------------------------------------
        public bool IsOverrideActive(MotionSourceKey source)
        {
            var index = FindOverride(source.ID);
            return index >= 0 && overrides[index].Source.Equals(source);
        }

    #endregion

    #region 제어 배율 층

        private MotionInputStatus AddScale(in MotionScaleCommand input)
        {
            if (!input.Source.IsValid || !IsValidChannels(input.Channels) || !IsValidLayers(input.Targets) ||
                input.Operation < NumericFOperation.SET || input.Operation > NumericFOperation.DIV ||
                !input.Value.IsFinite() || !IsValidDuration(input.Duration))
            {
                return MotionInputStatus.InvalidInput;
            }

            var existing = FindScale(input.Source.ID);

            if (existing >= 0)
            {
                var status = CompareGeneration(scales[existing].Source, input.Source);

                if (status != MotionInputStatus.Accepted)
                {
                    return status;
                }

                RemoveScaleAt(existing);
            }

            if (scaleCount >= Capacity)
            {
                return MotionInputStatus.CapacityReached;
            }

            var entry = new MotionScaleEntry
            {
                Source = input.Source,
                Channels = input.Channels,
                Targets = input.Targets,
                Order = input.Order,
                Sequence = IssueSequence(),
                Operation = input.Operation,
                Value = input.Value,
                HasDuration = input.Duration > 0f,
                Remaining = input.Duration
            };

            // Order 오름차순, 같은 Order는 먼저 추가한 항목이 먼저 적용된다.
            var position = scaleCount;

            while (position > 0 && IsAfter(scales[position - 1].Order, scales[position - 1].Sequence, entry.Order, entry.Sequence))
            {
                scales[position] = scales[position - 1];
                position--;
            }

            scales[position] = entry;
            scaleCount++;
            return MotionInputStatus.Accepted;
        }

        private static bool IsValidLayers(MotionLayer layers) =>
            layers != MotionLayer.None && (layers & ~MotionLayer.All) == MotionLayer.None;

        private MotionInputStatus RemoveScale(MotionSourceKey source)
        {
            var index = FindScale(source.ID);

            if (index < 0 || !scales[index].Source.Equals(source))
            {
                return MotionInputStatus.NotFound;
            }

            RemoveScaleAt(index);
            return MotionInputStatus.Accepted;
        }

        private int FindScale(long id)
        {
            for (var index = 0; index < scaleCount; index++)
            {
                if (scales[index].Source.ID == id)
                {
                    return index;
                }
            }

            return -1;
        }

        private void RemoveScaleAt(int index)
        {
            Array.Copy(scales, index + 1, scales, index, scaleCount - index - 1);
            scales[--scaleCount] = default;
        }

        private float EvaluateScale(MotionChannel channel, MotionLayer layer)
        {
            var value = 1f;

            for (var index = 0; index < scaleCount; index++)
            {
                ref readonly var entry = ref scales[index];

                if ((entry.Channels & channel) != 0 && (entry.Targets & layer) != 0)
                {
                    value = ApplyScalar(entry.Operation, value, entry.Value);
                }
            }

            return value;
        }

        // Core NumericFModifier.Modify와 같은 연산 규칙.
        private static float ApplyScalar(NumericFOperation operation, float current, float value) => operation switch
        {
            NumericFOperation.SET => value,
            NumericFOperation.ADD => current + value,
            NumericFOperation.SUB => current - value,
            NumericFOperation.MUL => current * value,
            NumericFOperation.DIV => value == 0f ? current : current / value,
            _ => current
        };

        // ------------------------------------------------------------
        /// <summary>
        /// 같은 식별자의 제어 배율 항목이 활성인지 확인한다.
        /// </summary>
        // ------------------------------------------------------------
        public bool IsScaleActive(MotionSourceKey source)
        {
            var index = FindScale(source.ID);
            return index >= 0 && scales[index].Source.Equals(source);
        }

    #endregion

    #region 외부 속도 층

        private MotionInputStatus SetExternal(in MotionExternalCommand input)
        {
            var operation = input.Operation;
            var isVectorOperation = operation == NumericFOperation.SET ||
                operation == NumericFOperation.ADD || operation == NumericFOperation.SUB;

            if (!input.Source.IsValid || !isVectorOperation || !input.Velocity.IsFinite() ||
                !input.Decay.IsFinite() || input.Decay < 0f || !IsValidDuration(input.Duration))
            {
                return MotionInputStatus.InvalidInput;
            }

            // 같은 세대는 값과 남은 시간을 갱신하고 시작 순서를 유지한다(영역의 매 Tick 갱신).
            var sequence = 0L;
            var existing = FindExternal(input.Source.ID);

            if (existing >= 0)
            {
                var current = externals[existing].Source;

                if (current.Equals(input.Source))
                {
                    sequence = externals[existing].Sequence;
                }
                else
                {
                    var status = CompareGeneration(current, input.Source);

                    if (status != MotionInputStatus.Accepted)
                    {
                        return status;
                    }
                }

                RemoveExternalAt(existing);
            }

            if (externalCount >= Capacity)
            {
                return MotionInputStatus.CapacityReached;
            }

            var entry = new MotionExternalEntry
            {
                Source = input.Source,
                Order = input.Order,
                Sequence = sequence != 0L ? sequence : IssueSequence(),
                Operation = operation,
                Velocity = input.Velocity,
                Decay = input.Decay,
                HasDuration = input.Duration > 0f,
                Remaining = input.Duration
            };

            var position = externalCount;

            while (position > 0 && IsAfter(externals[position - 1].Order, externals[position - 1].Sequence, entry.Order, entry.Sequence))
            {
                externals[position] = externals[position - 1];
                position--;
            }

            externals[position] = entry;
            externalCount++;
            return MotionInputStatus.Accepted;
        }

        private MotionInputStatus RemoveExternal(MotionSourceKey source)
        {
            var index = FindExternal(source.ID);

            if (index < 0 || !externals[index].Source.Equals(source))
            {
                return MotionInputStatus.NotFound;
            }

            RemoveExternalAt(index);
            return MotionInputStatus.Accepted;
        }

        private MotionInputStatus ClearExternal()
        {
            Array.Clear(externals, 0, externalCount);
            externalCount = 0;
            return MotionInputStatus.Accepted;
        }

        private int FindExternal(long id)
        {
            for (var index = 0; index < externalCount; index++)
            {
                if (externals[index].Source.ID == id)
                {
                    return index;
                }
            }

            return -1;
        }

        private void RemoveExternalAt(int index)
        {
            Array.Copy(externals, index + 1, externals, index, externalCount - index - 1);
            externals[--externalCount] = default;
        }

        private void RemoveBlockedExternal(in MotionFeedback feedback)
        {
            // 합계가 아니라 항목마다 깎아야 벽에서 떨어질 때 막혔던 성분이 되살아나지 않는다.
            for (var index = 0; index < externalCount; index++)
            {
                ref var entry = ref externals[index];
                var sign = entry.Operation == NumericFOperation.SUB ? -1f : 1f;
                var contribution = entry.Velocity * sign;

                for (var normalIndex = 0; normalIndex < feedback.BlockingNormalCount; normalIndex++)
                {
                    var normal = feedback.GetBlockingNormal(normalIndex);
                    var into = Vector3.Dot(contribution, normal);

                    if (into < 0f)
                    {
                        contribution -= normal * into;
                    }
                }

                entry.Velocity = contribution * sign;
            }
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 같은 식별자의 외부 속도 항목이 활성인지 확인한다.
        /// </summary>
        // ------------------------------------------------------------
        public bool IsExternalActive(MotionSourceKey source)
        {
            var index = FindExternal(source.ID);
            return index >= 0 && externals[index].Source.Equals(source);
        }

    #endregion

    #region 합성

        private Composition Compose(float delta, in MotionFeedback feedback, Vector3 impulse)
        {
            var result = new Composition();

            // 외부 속도는 Order 순서로 접고, 이미 Body에 반영한 몫과의 차이를 Free 채널에 넘긴다.
            var external = Vector3.zero;

            for (var index = 0; index < externalCount; index++)
            {
                ref readonly var entry = ref externals[index];
                external = ApplyVector(entry.Operation, external, entry.Velocity);
            }

            SplitLinear(external, out var externalPlanar, out var externalElevation);
            SplitLinear(appliedExternal, out var appliedPlanar, out var appliedElevation);
            FindTopOverrides();
            evaluatedOverride = -1;

            // Planar: Override → 기본 의도 → Free 순서로 기준을 정한다.
            if (TryResolveBase(MotionChannel.Planar, topPlanar, delta, feedback, out var planarLayer, out var linear, out _))
            {
                SplitLinear(linear, out var planar, out _);

                if (planarLayer == MotionLayer.Intent)
                {
                    planar = intentPlanar;
                }

                result.PlanarMode = MotionChannelMode.Set;
                result.Planar = planar * EvaluateScale(MotionChannel.Planar, planarLayer) + externalPlanar;
            }
            else
            {
                result.PlanarMode = MotionChannelMode.Free;
                impulse += externalPlanar - appliedPlanar;
            }

            // Elevation: 같은 규칙. Free면 Body의 중력 등 물리 속도를 유지한다.
            if (TryResolveBase(MotionChannel.Elevation, topElevation, delta, feedback, out var elevationLayer, out linear, out _))
            {
                SplitLinear(linear, out _, out var elevation);

                if (elevationLayer == MotionLayer.Intent)
                {
                    elevation = intentElevation;
                }

                result.ElevationMode = MotionChannelMode.Set;
                result.Elevation = elevation * EvaluateScale(MotionChannel.Elevation, elevationLayer) + externalElevation;
            }
            else
            {
                result.ElevationMode = MotionChannelMode.Free;
                impulse += Up * (externalElevation - appliedElevation);
            }

            // Rotation: 외부 속도는 선속도만 다루므로 기준과 배율만 적용한다.
            if (TryResolveBase(MotionChannel.Rotation, topRotation, delta, feedback, out var rotationLayer, out _, out var angular))
            {
                if (rotationLayer == MotionLayer.Intent)
                {
                    angular = intentRotation;
                }

                result.RotationMode = MotionChannelMode.Set;
                result.Rotation = angular * EvaluateScale(MotionChannel.Rotation, rotationLayer);
            }
            else
            {
                result.RotationMode = MotionChannelMode.Free;
            }

            appliedExternal = external;
            result.Impulse = impulse;
            result.Blocked = FindBlockedChannels();
            ResolveAction(out result.ActionID, out result.ActionStartTick);
            return result;
        }

        private static Vector3 ApplyVector(NumericFOperation operation, Vector3 current, Vector3 value) => operation switch
        {
            NumericFOperation.SET => value,
            NumericFOperation.ADD => current + value,
            NumericFOperation.SUB => current - value,
            _ => current
        };

        private bool TryResolveBase
        (
            MotionChannel channel, int top, float delta, in MotionFeedback feedback,
            out MotionLayer layer, out Vector3 linear, out Vector3 angular
        )
        {
            if (top >= 0)
            {
                layer = MotionLayer.Override;
                EvaluateOverride(top, delta, feedback, out linear, out angular);
                return true;
            }

            linear = Vector3.zero;
            angular = Vector3.zero;

            if ((intentChannels & channel) != 0)
            {
                layer = MotionLayer.Intent;
                return true;
            }

            layer = MotionLayer.None;
            return false;
        }

        private void EvaluateOverride(int index, float delta, in MotionFeedback feedback, out Vector3 linear, out Vector3 angular)
        {
            ref readonly var entry = ref overrides[index];

            if (entry.TrackID == 0 || !tracks.TryGet(entry.TrackID, out var track))
            {
                linear = entry.Linear;
                angular = entry.Angular;
                return;
            }

            if (index == evaluatedOverride)
            {
                linear = evaluatedLinear;
                angular = evaluatedAngular;
                return;
            }

            // Track의 로컬 변위를 Body 회전으로 월드에 놓고, Yaw 변화는 Up 축 회전으로 바꾼다.
            var from = entry.TrackElapsed;
            var to = from + delta * entry.TrackRate;
            track.EvaluateDelta(from, to, out var localDelta, out var yawDeltaDegrees);

            linear = feedback.Rotation * localDelta / delta;
            angular = Up * (yawDeltaDegrees * Mathf.Deg2Rad / delta);

            evaluatedOverride = index;
            evaluatedLinear = linear;
            evaluatedAngular = angular;
        }

        private void FindTopOverrides()
        {
            topPlanar = topElevation = topRotation = -1;

            for (var index = 0; index < overrideCount; index++)
            {
                var channels = overrides[index].Channels;

                if ((channels & MotionChannel.Planar) != 0 && IsAbove(index, topPlanar))
                {
                    topPlanar = index;
                }

                if ((channels & MotionChannel.Elevation) != 0 && IsAbove(index, topElevation))
                {
                    topElevation = index;
                }

                if ((channels & MotionChannel.Rotation) != 0 && IsAbove(index, topRotation))
                {
                    topRotation = index;
                }
            }
        }

        private bool IsAbove(int candidate, int current)
        {
            if (current < 0)
            {
                return true;
            }

            ref readonly var a = ref overrides[candidate];
            ref readonly var b = ref overrides[current];
            return a.Priority != b.Priority ? a.Priority > b.Priority : a.Sequence > b.Sequence;
        }

        private MotionChannel FindBlockedChannels()
        {
            var blocked = MotionChannel.None;

            if (EvaluateScale(MotionChannel.Planar, MotionLayer.Intent) == 0f)
            {
                blocked |= MotionChannel.Planar;
            }

            if (EvaluateScale(MotionChannel.Elevation, MotionLayer.Intent) == 0f)
            {
                blocked |= MotionChannel.Elevation;
            }

            if (EvaluateScale(MotionChannel.Rotation, MotionLayer.Intent) == 0f)
            {
                blocked |= MotionChannel.Rotation;
            }

            return blocked;
        }

        private void ResolveAction(out int actionID, out long startTick)
        {
            var top = -1;

            for (var index = 0; index < overrideCount; index++)
            {
                if (IsAbove(index, top))
                {
                    top = index;
                }
            }

            actionID = top >= 0 ? overrides[top].ActionID : 0;
            startTick = top >= 0 ? overrides[top].StartTick : -1;
        }

    #endregion

    #region 시간 진행

        private void Advance(float delta)
        {
            // 어느 채널에서도 보이지 않는 Override는 시간이 흐르지 않는다.
            for (var index = overrideCount - 1; index >= 0; index--)
            {
                if (index != topPlanar && index != topElevation && index != topRotation)
                {
                    continue;
                }

                ref var entry = ref overrides[index];
                var expired = false;

                if (entry.TrackID != 0)
                {
                    entry.TrackElapsed += delta * entry.TrackRate;

                    if (!entry.HasDuration && tracks.TryGet(entry.TrackID, out var track) && entry.TrackElapsed >= track.Length)
                    {
                        expired = true;
                    }
                }

                if (entry.HasDuration)
                {
                    entry.Remaining -= delta;
                    expired |= entry.Remaining <= 0f;
                }

                if (expired)
                {
                    RemoveOverrideAt(index);
                }
            }

            for (var index = scaleCount - 1; index >= 0; index--)
            {
                ref var entry = ref scales[index];

                if (!entry.HasDuration)
                {
                    continue;
                }

                entry.Remaining -= delta;

                if (entry.Remaining <= 0f)
                {
                    RemoveScaleAt(index);
                }
            }

            // 외부 속도는 지수 감쇠하고, 거의 0이 되거나 시간이 끝나면 제거한다.
            for (var index = externalCount - 1; index >= 0; index--)
            {
                ref var entry = ref externals[index];
                var expired = false;

                if (entry.Decay > 0f)
                {
                    entry.Velocity *= Mathf.Exp(-entry.Decay * delta);
                    expired = entry.Velocity.sqrMagnitude < EXTERNAL_EPSILON_SQR;
                }

                if (entry.HasDuration)
                {
                    entry.Remaining -= delta;
                    expired |= entry.Remaining <= 0f;
                }

                if (expired)
                {
                    RemoveExternalAt(index);
                }
            }
        }

    #endregion

    #region Snapshot

        // ------------------------------------------------------------
        /// <summary>
        /// 현재 층 상태를 새 Snapshot에 복사한다.
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
        /// <br/> 현재 층 상태를 기존 Snapshot에 복사한다.
        /// <br/> 같은 용량의 Snapshot을 재사용하면 할당이 없다.
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

            Array.Copy(overrides, target.Overrides, Capacity);
            Array.Copy(scales, target.Scales, Capacity);
            Array.Copy(externals, target.Externals, Capacity);
            target.OverrideCount = overrideCount;
            target.ScaleCount = scaleCount;
            target.ExternalCount = externalCount;
            target.AppliedExternal = appliedExternal;
            target.NextSequence = nextSequence;
            target.Actor = Actor;
            target.LastTick = lastTick;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> 같은 Actor의 Snapshot으로 층 상태를 되돌린다.
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

            Array.Copy(snapshot.Overrides, overrides, Capacity);
            Array.Copy(snapshot.Scales, scales, Capacity);
            Array.Copy(snapshot.Externals, externals, Capacity);
            overrideCount = snapshot.OverrideCount;
            scaleCount = snapshot.ScaleCount;
            externalCount = snapshot.ExternalCount;
            appliedExternal = snapshot.AppliedExternal;
            nextSequence = snapshot.NextSequence;
            lastTick = snapshot.LastTick;
            stepVersion = checked(stepVersion + 1);
        }

    #endregion

    #region 유틸

        private void SplitLinear(Vector3 linear, out Vector3 planar, out float elevation)
        {
            elevation = Vector3.Dot(linear, Up);
            planar = linear - Up * elevation;
        }

        private long IssueSequence()
        {
            var sequence = nextSequence;
            nextSequence = checked(nextSequence + 1);
            return sequence;
        }

        private static MotionInputStatus CompareGeneration(MotionSourceKey current, MotionSourceKey incoming)
        {
            if (incoming.Generation == current.Generation)
            {
                return MotionInputStatus.Duplicate;
            }

            return incoming.Generation < current.Generation
                ? MotionInputStatus.StaleGeneration
                : MotionInputStatus.Accepted;
        }

        private static bool IsAfter(int order, long sequence, int otherOrder, long otherSequence) =>
            order != otherOrder ? order > otherOrder : sequence > otherSequence;

        private static bool IsValidChannels(MotionChannel channels) =>
            channels != MotionChannel.None && (channels & ~MotionChannel.All) == MotionChannel.None;

        private static bool IsValidDuration(float duration) => duration.IsFinite() && duration >= 0f;

    #endregion

    }
}
