/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : TEST_Board2D.cs
수정일 : 2026-11-12

# 설명
Board2D 시스템의 핵심 기능 테스트. Edit Mode.

# 테스트 구성
 E: 기본 기능 (크기/배치/제거/공간/이벤트/종합)
 X: 예외 처리 (범위 밖 배치 / 중복 AddSpace)
========================================================================= BLOCK_HEADER_END */

using System;

using UnityEngine;

using NUnit;
using NUnit.Framework;

using inonego.Xeri.Game;

namespace inonego.Xeri.Game.TEST._Board
{

    // ============================================================
    /// <summary>
    /// Board2D 시스템의 핵심 기능 테스트 클래스.
    /// </summary>
    // ============================================================
    public class TEST_Board2D
    {

    #region 헬퍼

        // ------------------------------------------------------------
        /// <summary>
        /// 테스트용 기물입니다.
        /// </summary>
        // ------------------------------------------------------------
        public class TestPiece
        {
            public string Name;
            public TestPiece() {}
            public TestPiece(string name) { Name = name; }
            public override string ToString() => Name;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 테스트용 공간입니다.
        /// </summary>
        // ------------------------------------------------------------
        [Serializable]
        public class TestSpace : BoardSpace<TestPiece>
        {
            public TestSpace() {}
        }

    #endregion

    #region E-1: 크기 및 경계

        [Test]
        public void TEST_Board2D_크기_경계_확인()
        {
            var board = new Board2D<int, TestSpace, TestPiece>(3, 2);

            Assert.That(board.Width, Is.EqualTo(3));
            Assert.That(board.Height, Is.EqualTo(2));
            Assert.That(board.Size, Is.EqualTo(new Vector2Int(3, 2)));

            Assert.That(board[new Vector2Int(+0, +0)], Is.Not.Null);
            Assert.That(board[new Vector2Int(+2, +1)], Is.Not.Null);
            Assert.That(board[new Vector2Int(-1, +0)], Is.Null);
            Assert.That(board[new Vector2Int(+3, +1)], Is.Null);
            Assert.That(board[new Vector2Int(+2, +2)], Is.Null);
        }

    #endregion

    #region E-2: 배치 및 인덱서

        [Test]
        public void TEST_Board2D_Place_인덱서_접근()
        {
            var board = new Board2D<int, TestSpace, TestPiece>(2, 2);
            var a = new TestPiece("A");
            var b = new TestPiece("B");

            var p0 = new Vector2Int(0, 0);
            var p1 = new Vector2Int(1, 1);

            board.Place(p0, 0, a);
            Assert.That(board[p0, 0], Is.EqualTo(a));
            Assert.That(board[p0][0], Is.EqualTo(a));

            Assert.Throws<InvalidOperationException>(() => board.Place(p0, 0, b));

            board.Place(p0, 1, b);
            Assert.That(board[p0, 0], Is.EqualTo(a));
            Assert.That(board[p0, 1], Is.EqualTo(b));

            var c = new TestPiece("C");
            board.Place(p1, 0, c);
            Assert.That(board[p1, 0], Is.EqualTo(c));
            Assert.That(board[p1][0], Is.EqualTo(c));
        }

    #endregion

    #region E-3: 단일 Index 제거

        [Test]
        public void TEST_Board2D_Remove_단일_Index()
        {
            var board = new Board2D<int, TestSpace, TestPiece>(2, 2);
            var a = new TestPiece("A");
            var b = new TestPiece("B");
            var p = new Vector2Int(1, 0);

            board.Place(p, 0, a);
            board.Place(p, 1, b);

            board.Remove(p, 0);
            Assert.That(board[p], Is.Not.Null);
            Assert.That(board[p, 0], Is.Null);
            Assert.That(board[p, 1], Is.EqualTo(b));

            board.Remove(b);
            Assert.That(board[p, 1], Is.Null);
        }

    #endregion

    #region E-4: Vector 전체 제거

        [Test]
        public void TEST_Board2D_Remove_Vector_전체()
        {
            var board = new Board2D<int, TestSpace, TestPiece>(2, 2);
            var p = new Vector2Int(0, 0);

            var pieces = new[]
            {
                new TestPiece("P0"),
                new TestPiece("P1"),
                new TestPiece("P2"),
                new TestPiece("P3"),
            };

            for (int i = 0; i < pieces.Length; i++)
            {
                board.Place(p, i, pieces[i]);
            }

            for (int i = 0; i < pieces.Length; i++)
            {
                Assert.That(board[p, i], Is.EqualTo(pieces[i]));
            }

            board.Remove(p);

            Assert.That(board[p], Is.Not.Null);
            for (int i = 0; i < pieces.Length; i++)
            {
                Assert.That(board[p, i], Is.Null);
            }
        }

    #endregion

    #region E-5: 공간 추가/제거

        [Test]
        public void TEST_Board2D_AddSpace_RemoveSpace_이벤트()
        {
            var board = new Board2D<int, TestSpace, TestPiece>(1, 1);
            var pt = new Vector2Int(0, 0);

            Vector2Int? addEventVector = null;
            Vector2Int? removeEventVector = null;

            board.OnAddSpace += (v) => { addEventVector = v; };
            board.OnRemoveSpace += (v) => { removeEventVector = v; };

            Assert.That(board[pt], Is.Not.Null);

            board.RemoveSpace(pt);
            Assert.That(board[pt], Is.Null);
            Assert.That(removeEventVector, Is.EqualTo(pt));

            board.AddSpace(pt);
            Assert.That(board[pt], Is.Not.Null);
            Assert.That(addEventVector, Is.EqualTo(pt));

            var pieces = new[] { new TestPiece("A"), new TestPiece("B"), new TestPiece("C") };
            for (int i = 0; i < pieces.Length; i++)
            {
                board.Place(pt, i, pieces[i]);
            }

            board.RemoveSpace(pt);
            Assert.That(board[pt], Is.Null);
        }

    #endregion

    #region E-6: OnPlace 이벤트

        [Test]
        public void TEST_Board2D_OnPlace_이벤트_발생()
        {
            var board = new Board2D<int, TestSpace, TestPiece>(2, 2);
            var piece = new TestPiece("E");
            var p = new Vector2Int(0, 0);

            Vector2Int? eventVector = null;
            int? eventIndex = null;
            TestPiece eventPiece = null;
            int eventCount = 0;

            board.OnPlace += (v, i, pl) =>
            {
                eventVector = v;
                eventIndex = i;
                eventPiece = pl;
                eventCount++;
            };

            board.Place(p, 5, piece);

            Assert.That(eventCount, Is.EqualTo(1));
            Assert.That(eventVector, Is.EqualTo(p));
            Assert.That(eventIndex, Is.EqualTo(5));
            Assert.That(eventPiece, Is.EqualTo(piece));
        }

    #endregion

    #region E-7: OnRemove 이벤트

        [Test]
        public void TEST_Board2D_OnRemove_이벤트_발생()
        {
            var board = new Board2D<int, TestSpace, TestPiece>(2, 2);
            var piece = new TestPiece("R");
            var p = new Vector2Int(1, 0);

            Vector2Int? eventVector = null;
            int? eventIndex = null;
            TestPiece eventPiece = null;
            int eventCount = 0;

            board.OnRemove += (v, i, pl) =>
            {
                eventVector = v;
                eventIndex = i;
                eventPiece = pl;
                eventCount++;
            };

            board.Place(p, 3, piece);
            board.Remove(p, 3);

            Assert.That(eventCount, Is.EqualTo(1));
            Assert.That(eventVector, Is.EqualTo(p));
            Assert.That(eventIndex, Is.EqualTo(3));
            Assert.That(eventPiece, Is.EqualTo(piece));

            eventCount = 0;
            board.Place(p, 0, new TestPiece("A"));
            board.Place(p, 1, new TestPiece("B"));
            board.Place(p, 2, new TestPiece("C"));

            board.Remove(p);
            Assert.That(eventCount, Is.EqualTo(3));
        }

    #endregion

    #region E-8: invokeEvent 파라미터

        [Test]
        public void TEST_Board2D_invokeEvent_false시_이벤트_미발생()
        {
            var board = new Board2D<int, TestSpace, TestPiece>(2, 2);
            var piece = new TestPiece("Q");
            var p = new Vector2Int(0, 0);

            int placeEventCount = 0;
            int removeEventCount = 0;

            board.OnPlace += (v, i, pl) => placeEventCount++;
            board.OnRemove += (v, i, pl) => removeEventCount++;

            board.Place(p, 0, piece, invokeEvent: false);
            Assert.That(placeEventCount, Is.EqualTo(0));
            Assert.That(board[p, 0], Is.EqualTo(piece));

            board.Remove(p, 0, invokeEvent: false);
            Assert.That(removeEventCount, Is.EqualTo(0));
            Assert.That(board[p, 0], Is.Null);

            board.Place(p, 0, piece);
            Assert.That(placeEventCount, Is.EqualTo(1));

            board.Remove(p, 0);
            Assert.That(removeEventCount, Is.EqualTo(1));
        }

    #endregion

    #region E-9: 종합 시나리오

        [Test]
        public void TEST_Board2D_복합_시나리오()
        {
            var board = new Board2D<int, TestSpace, TestPiece>(3, 3);

            for (int x = 0; x < 3; x++)
            {
                for (int y = 0; y < 3; y++)
                {
                    var vector = new Vector2Int(x, y);
                    for (int index = 0; index < 2; index++)
                    {
                        var piece = new TestPiece($"[{x},{y}][{index}]");
                        board.Place(vector, index, piece);
                    }
                }
            }

            for (int x = 0; x < 3; x++)
            {
                for (int y = 0; y < 3; y++)
                {
                    var vector = new Vector2Int(x, y);
                    for (int index = 0; index < 2; index++)
                    {
                        Assert.That(board[vector, index], Is.Not.Null);
                        Assert.That(board[vector, index].Name, Is.EqualTo($"[{x},{y}][{index}]"));
                    }
                }
            }

            var targetVector = new Vector2Int(1, 1);
            board.Remove(targetVector);

            Assert.That(board[targetVector], Is.Not.Null);
            Assert.That(board[targetVector, 0], Is.Null);
            Assert.That(board[targetVector, 1], Is.Null);

            Assert.That(board[new Vector2Int(0, 0), 0], Is.Not.Null);
            Assert.That(board[new Vector2Int(2, 2), 1], Is.Not.Null);
        }

    #endregion

    #region X-1: 범위 밖 배치 예외

        [Test]
        public void TEST_Board2D_범위밖_Place_InvalidOperationException()
        {
            var board = new Board2D<int, TestSpace, TestPiece>(2, 2);
            var piece = new TestPiece("O");
            var point = new Vector2Int(5, 5);

            Assert.Throws<InvalidOperationException>(() => board.Place(point, 0, piece));
        }

    #endregion

    #region X-2: 중복 AddSpace 예외

        [Test]
        public void TEST_Board2D_중복_AddSpace_InvalidOperationException()
        {
            var board = new Board2D<int, TestSpace, TestPiece>(1, 1);
            var point = new Vector2Int(0, 0);

            Assert.Throws<InvalidOperationException>(() => board.AddSpace(point));
        }

    #endregion

    }

}
