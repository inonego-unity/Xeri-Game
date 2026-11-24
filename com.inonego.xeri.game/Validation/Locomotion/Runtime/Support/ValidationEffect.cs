/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : ValidationEffect.cs
수정일 : 2026-11-22

# 설명
검증 카드가 쓰는 효과 하나(대시, 넉백, 스턴 같은). 사용처(도메인) 쪽 수명을 흉내 낸다.
지속 시간은 Core Timer로 재고, 가려지면 Timer를 멈추고, 감쇠는 값을 직접 줄여 같은 세대로 다시 넣는다.

# 특이사항, 제약사항
Motion은 수명·감쇠·가려짐 정지를 모른다. 이 클래스가 문서의 사용처 책임을 그대로 따른다.
효과를 시작할 때마다 세대를 올려 새 항목으로 시작한다.
========================================================================= BLOCK_HEADER_END */

using System;

using UnityEngine;

using inonego;
using inonego.Xeri;
using inonego.Xeri.Game;
using inonego.Xeri.Game.Motion;
using inonego.Xeri.Serializable;
using inonego.Xeri.Utility;

namespace inonego.Xeri.Game.Validation.Locomotion
{
    // ============================================================
    /// <summary>
    /// Modifier 하나를 시작·갱신·종료하는 검증용 효과.
    /// </summary>
    // ============================================================
    internal sealed class ValidationEffect
    {

    #region 필드

        private static long sequenceCounter;

        public string Name { get; }
        public string Key { get; }
        public int Order { get; }
        public NumericFOperation Operation { get; }
        public MotionChannel Channels { get; }
        public Color Color { get; }

        // 값: 월드 선속도·각속도, 또는 모든 성분에 같은 값(배율).
        public Vector3 Linear;
        public Vector3 Angular;
        public float Uniform;
        public bool UseUniform;

        // 수명 옵션. Duration 0은 끝낼 때까지 유지한다.
        public float Duration;
        public float Decay;
        public int ActionID;
        public bool PauseWhenHidden;

        public bool IsActive { get; private set; }
        public long Sequence { get; private set; }
        public float Scale { get; private set; } = 1f;
        public float Elapsed { get; private set; }

        // 시작한 뒤 Step을 한 번 이상 거쳤는지. 반영한 몫이 있는지 판단한다.
        public bool Stepped { get; private set; }

        public Timer Timer { get; } = new Timer();

        private long generation;
        private bool ended;

    #endregion

    #region 생성자

        // ------------------------------------------------------------
        /// <summary>
        /// 이름·키·Order·연산·채널·표시 색으로 효과를 만든다.
        /// </summary>
        // ------------------------------------------------------------
        public ValidationEffect
        (
            string name, string key, int order, NumericFOperation operation, MotionChannel channels, Color color
        )
        {
            Name = name;
            Key = key;
            Order = order;
            Operation = operation;
            Channels = channels;
            Color = color;
            Timer.OnEnd += OnTimerEnd;
        }

        private void OnTimerEnd(object sender, TimerEndEventArgs args)
        {
            ended = true;
        }

    #endregion

    #region 값

        // 현재 세대를 포함한 Modifier 키.
        public MotionSourceKey Source => new MotionSourceKey(Key, Math.Max(1L, generation));

        // Planar 채널에 들어가는 피연산자(감쇠 반영).
        public Vector3 PlanarOperand => UseUniform ? new Vector3(Uniform, Uniform, Uniform) : Linear * Scale;

        // ------------------------------------------------------------
        /// <summary>
        /// 현재 감쇠를 반영한 채널 값을 만든다.
        /// </summary>
        // ------------------------------------------------------------
        public MotionChannelValues Values() => UseUniform
            ? MotionChannelValues.Uniform(Channels, Uniform)
            : MotionChannelValues.Linear(Channels, Linear * Scale, Angular * Scale);

        // ------------------------------------------------------------
        /// <summary>
        /// 이 항목이 Free 채널에 반영하는 월드 속도 몫(ADD·SUB만). 보존 법칙 검사에 쓴다.
        /// </summary>
        // ------------------------------------------------------------
        public Vector3 Contribution(Vector3 up)
        {
            var sign = Operation == NumericFOperation.ADD ? 1f : Operation == NumericFOperation.SUB ? -1f : 0f;

            if (sign == 0f || UseUniform)
            {
                return Vector3.zero;
            }

            var linear = Linear * Scale * sign;
            var along = Vector3.Dot(linear, up);
            var contribution = Vector3.zero;

            if ((Channels & MotionChannel.Planar) != 0)
            {
                contribution += linear - up * along;
            }

            if ((Channels & MotionChannel.Elevation) != 0)
            {
                contribution += up * along;
            }

            return contribution;
        }

    #endregion

    #region 수명

        // ------------------------------------------------------------
        /// <summary>
        /// 새 세대로 시작한다. 이미 활성이면 먼저 끝내고 다시 시작한다.
        /// </summary>
        // ------------------------------------------------------------
        public void Start(ValidationActor actor)
        {
            if (IsActive)
            {
                Stop(actor, false);
            }

            generation++;
            Sequence = ++sequenceCounter;
            Scale = 1f;
            Elapsed = 0f;
            ended = false;
            Stepped = false;
            IsActive = true;

            Timer.Stop();

            if (Duration > 0f)
            {
                Timer.Start(Duration);
            }

            actor.Buffer.Add(MotionModifierCommand.Apply(Source, Order, Operation, Values(), ActionID));
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 같은 세대의 값을 다시 넣는다. 감쇠나 외부 값 변화를 반영한다.
        /// </summary>
        // ------------------------------------------------------------
        public void Refresh(ValidationActor actor)
        {
            if (IsActive)
            {
                actor.Buffer.Add(MotionModifierCommand.Apply(Source, Order, Operation, Values(), ActionID));
            }
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 종료 명령을 넣는다. keepVelocity면 Body에 남긴 속도를 되돌리지 않는다.
        /// </summary>
        // ------------------------------------------------------------
        public void Stop(ValidationActor actor, bool keepVelocity)
        {
            if (!IsActive)
            {
                return;
            }

            IsActive = false;
            Timer.Stop();
            actor.Buffer.Add(MotionModifierCommand.End(Source, keepVelocity));
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 명령 없이 활성 표시만 끈다. Model이 이미 지워졌을 때(Clear, 초기화) 쓴다.
        /// </summary>
        // ------------------------------------------------------------
        public void MarkInactive()
        {
            IsActive = false;
            Timer.Stop();
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Tick마다 호출한다. 가려지면 Timer를 멈추고 감쇠 값을 갱신하며 시간이 다 되면 끝낸다.
        /// </summary>
        // ------------------------------------------------------------
        public void Tick(ValidationActor actor, float dt)
        {
            if (!IsActive)
            {
                return;
            }

            var hidden = PauseWhenHidden && actor.Model.TryGetModifier(Source, out var state) && !state.IsVisible;

            if (Duration > 0f)
            {
                if (hidden)
                {
                    Timer.Pause();
                }
                else
                {
                    Timer.Resume();
                    Timer.Tick(dt);
                }
            }

            if (!hidden)
            {
                Elapsed += dt;
            }

            if (ended)
            {
                Stop(actor, false);
                return;
            }

            if (Decay > 0f)
            {
                Scale = Mathf.Exp(-Decay * Elapsed);

                if (Scale < 0.02f)
                {
                    Stop(actor, false);
                    return;
                }

                Refresh(actor);
            }
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Step을 한 번 거쳤음을 표시한다.
        /// </summary>
        // ------------------------------------------------------------
        public void MarkStepped()
        {
            if (IsActive)
            {
                Stepped = true;
            }
        }

    #endregion

    }
}
