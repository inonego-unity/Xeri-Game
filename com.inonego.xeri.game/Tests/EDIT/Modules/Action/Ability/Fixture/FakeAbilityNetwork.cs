/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : FakeAbilityNetwork.cs
수정일 : 2026-11-17

# 설명
고정 Tick과 전달 조건을 제어하는 테스트 전용 양방향 메시지 전송 환경이다.
========================================================================= BLOCK_HEADER_END */

using System;
using System.Collections;
using System.Collections.Generic;

namespace inonego.Xeri.Game.TEST._Ability.Fixture
{
    internal readonly struct FakeAbilityMessage<T>
    {
        internal readonly T Payload;
        internal readonly int Epoch;
        internal readonly long SentTick;

        internal FakeAbilityMessage(T payload, int epoch, long sentTick)
        {
            Payload = payload;
            Epoch = epoch;
            SentTick = sentTick;
        }
    }

    internal sealed class FakeAbilityEndpoint<T>
    {
        internal IReadOnlyList<FakeAbilityMessage<T>> Received => received;

        private readonly List<FakeAbilityMessage<T>> received = new();
        internal int Epoch { get; private set; }

        internal void ChangeEpoch(int epoch)
        {
            Epoch = epoch;
        }

        internal void Receive(FakeAbilityMessage<T> message)
        {
            received.Add(message);
        }
    }

    internal sealed class FakeAbilityNetwork<T>
    {
        private sealed class PendingMessage
        {
            internal FakeAbilityEndpoint<T> Destination;
            internal FakeAbilityMessage<T> Message;
            internal long DeliveryTick;
        }

        private readonly List<PendingMessage> pending = new();
        private readonly double tickDuration;

        internal FakeAbilityEndpoint<T> Server { get; } = new();
        internal FakeAbilityEndpoint<T> Client { get; } = new();
        internal long Tick { get; private set; }
        internal double Now => Tick * tickDuration;
        internal float TickDelta => (float)tickDuration;
        internal int PendingCount => pending.Count;

        internal FakeAbilityNetwork(double tickDuration)
        {
            if (double.IsNaN(tickDuration) || double.IsInfinity(tickDuration) || tickDuration <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(tickDuration));
            }

            this.tickDuration = tickDuration;
        }

        internal void Send
        (
            FakeAbilityEndpoint<T> source, FakeAbilityEndpoint<T> destination,
            T payload, int delayTicks = 0, int copies = 1, bool drop = false
        )
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            if (destination == null)
            {
                throw new ArgumentNullException(nameof(destination));
            }

            if (delayTicks < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(delayTicks));
            }

            if (copies < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(copies));
            }

            if (drop) return;

            var deliveryTick = checked(Tick + delayTicks);
            var message = new FakeAbilityMessage<T>(payload, source.Epoch, Tick);
            for (var i = 0; i < copies; i++)
            {
                pending.Add
                (
                    new PendingMessage
                    {
                        Destination = destination,
                        Message = message,
                        DeliveryTick = deliveryTick,
                    }
                );
            }
        }

        internal void AdvanceTick()
        {
            Tick = checked(Tick + 1);
        }

        internal void DeliverReady(bool reverseOrder = false)
        {
            var ready = new List<PendingMessage>();
            for (var i = pending.Count - 1; i >= 0; i--)
            {
                if (pending[i].DeliveryTick > Tick) continue;

                ready.Add(pending[i]);
                pending.RemoveAt(i);
            }

            // 역순 수집을 되돌리면 기본 전달 순서는 발송 순서다.
            if (!reverseOrder)
            {
                ready.Reverse();
            }

            foreach (var item in ready)
            {
                item.Destination.Receive(item.Message);
            }
        }
    }
}
