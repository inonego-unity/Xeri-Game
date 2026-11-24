/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : FakeAbilityNetwork.cs
수정일 : 2026-11-19

# 설명
고정 Tick과 전달 조건을 제어하는 테스트 전용 메시지 전송 환경이다.
서버 끝점 하나와 연결(Connection)마다 Client 끝점을 두며, 보낸 쪽의 연결 신원은 전송 계층이 메시지에 붙인다.
순서 보장 채널로 보낸 메시지는 받는 끝점마다 보낸 순서대로 도착하며 지연·역순 주입에도 순서가 바뀌지 않는다.
그 밖의 메시지는 지연·중복·유실·역순으로 순서가 바뀔 수 있다.
========================================================================= BLOCK_HEADER_END */

using System;
using System.Collections;
using System.Collections.Generic;

namespace inonego.Xeri.Game.TEST._Ability.Fixture
{
    // ============================================================
    /// <summary>
    /// 전송 계층이 보낸 쪽의 Epoch·Tick·연결 신원을 붙인 메시지이다.
    /// </summary>
    // ============================================================
    internal readonly struct FakeAbilityMessage<T>
    {
        internal readonly T Payload;
        internal readonly int Epoch;
        internal readonly long SentTick;

        // ------------------------------------------------------------
        /// <summary>
        /// 보낸 끝점의 연결 신원이다. 전송 계층이 붙이며 페이로드와 무관하다.
        /// </summary>
        // ------------------------------------------------------------
        internal readonly int Connection;

        internal FakeAbilityMessage(T payload, int epoch, long sentTick, int connection)
        {
            Payload = payload;
            Epoch = epoch;
            SentTick = sentTick;
            Connection = connection;
        }
    }

    // ============================================================
    /// <summary>
    /// 연결 하나의 수신함과 현재 Epoch를 가진 끝점이다.
    /// </summary>
    // ============================================================
    internal sealed class FakeAbilityEndpoint<T>
    {

    #region 구성

        internal int Connection { get; }
        internal int Epoch { get; private set; }

        internal IReadOnlyList<FakeAbilityMessage<T>> Received => received;
        private readonly List<FakeAbilityMessage<T>> received = new();

        internal FakeAbilityEndpoint(int connection)
        {
            Connection = connection;
        }

    #endregion

    #region 세대와 수신

        internal void ChangeEpoch(int epoch)
        {
            Epoch = epoch;
        }

        internal void Receive(FakeAbilityMessage<T> message)
        {
            received.Add(message);
        }

    #endregion

    }

    // ============================================================
    /// <summary>
    /// 서버와 연결별 Client 끝점 사이의 메시지 전송을 흉내 낸다.
    /// </summary>
    // ============================================================
    internal sealed class FakeAbilityNetwork<T>
    {

    #region 내부 데이터

        private sealed class PendingMessage
        {
            internal FakeAbilityEndpoint<T> Destination;
            internal FakeAbilityMessage<T> Message;
            internal long DeliveryTick;
            internal bool Ordered;
        }

    #endregion

    #region 구성

        internal const int ServerConnection = 0;

        private readonly List<PendingMessage> pending = new();
        private readonly Dictionary<int, FakeAbilityEndpoint<T>> clients = new();
        private readonly Dictionary<FakeAbilityEndpoint<T>, long> orderedTails = new();
        private readonly double tickDuration;

        internal FakeAbilityEndpoint<T> Server { get; } = new(ServerConnection);
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

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> 연결 신원의 Client 끝점을 반환하며 처음이면 만든다.
        /// <br/> 서버 연결 신원과 음수는 Client 연결로 쓸 수 없다.
        /// </summary>
        // ------------------------------------------------------------
        internal FakeAbilityEndpoint<T> Connect(int connection)
        {
            if (connection <= ServerConnection)
            {
                throw new ArgumentOutOfRangeException(nameof(connection));
            }

            if (!clients.TryGetValue(connection, out var endpoint))
            {
                endpoint = new FakeAbilityEndpoint<T>(connection);
                clients.Add(connection, endpoint);
            }

            return endpoint;
        }

    #endregion

    #region 전송

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/> 보낸 끝점의 Epoch·Tick·연결 신원을 붙여 지연 뒤 전달할 메시지로 쌓는다.
        /// <br/> copies만큼 중복하고 drop이면 아무것도 쌓지 않는다. ordered이면 받는 끝점의
        /// <br/> 순서 보장 채널로 보내 앞서 보낸 순서 보장 메시지보다 먼저 도착하지 않으며,
        /// <br/> 이 채널에는 중복과 유실을 주입할 수 없다.
        /// </summary>
        // --------------------------------------------------------------------------------
        internal void Send
        (
            FakeAbilityEndpoint<T> source, FakeAbilityEndpoint<T> destination,
            T payload, int delayTicks = 0, int copies = 1, bool drop = false, bool ordered = false
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

            if (ordered && (copies != 1 || drop))
            {
                throw new ArgumentException("순서 보장 채널에는 중복과 유실을 주입할 수 없습니다.", nameof(ordered));
            }

            if (drop) return;

            var deliveryTick = checked(Tick + delayTicks);
            if (ordered)
            {
                // 지연이 줄어도 앞서 보낸 순서 보장 메시지보다 먼저 도착하지 않는다.
                if (orderedTails.TryGetValue(destination, out var tail) && tail > deliveryTick)
                {
                    deliveryTick = tail;
                }

                orderedTails[destination] = deliveryTick;
            }

            var message = new FakeAbilityMessage<T>(payload, source.Epoch, Tick, source.Connection);
            for (var i = 0; i < copies; i++)
            {
                pending.Add
                (
                    new PendingMessage
                    {
                        Destination = destination,
                        Message = message,
                        DeliveryTick = deliveryTick,
                        Ordered = ordered,
                    }
                );
            }
        }

    #endregion

    #region 시간과 전달

        internal void AdvanceTick()
        {
            Tick = checked(Tick + 1);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> 전달 Tick이 된 메시지를 발송 순서대로 받는 끝점에 넣는다.
        /// <br/> reverseOrder이면 같은 Tick에 준비된 메시지를 역순으로 넣되
        /// <br/> 순서 보장 채널의 메시지끼리는 보낸 순서를 지킨다.
        /// </summary>
        // ------------------------------------------------------------
        internal void DeliverReady(bool reverseOrder = false)
        {
            var ready = new List<PendingMessage>();
            var waiting = new List<PendingMessage>();
            foreach (var item in pending)
            {
                (item.DeliveryTick > Tick ? waiting : ready).Add(item);
            }

            pending.Clear();
            pending.AddRange(waiting);
            if (reverseOrder)
            {
                ReverseUnordered(ready);
            }

            foreach (var item in ready)
            {
                item.Destination.Receive(item.Message);
            }
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> 준비된 메시지를 역순으로 바꾼 뒤 순서 보장 메시지가 놓인 자리에는
        /// <br/> 그 메시지들을 보낸 순서대로 다시 채운다.
        /// </summary>
        // ----------------------------------------------------------------------
        private static void ReverseUnordered(List<PendingMessage> ready)
        {
            var ordered = ready.FindAll(item => item.Ordered);
            ready.Reverse();
            var next = 0;
            for (var index = 0; index < ready.Count; index++)
            {
                if (ready[index].Ordered)
                {
                    ready[index] = ordered[next++];
                }
            }
        }

    #endregion

    }
}
