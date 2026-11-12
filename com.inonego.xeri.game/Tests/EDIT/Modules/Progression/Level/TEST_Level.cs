/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : TEST_Level.cs
수정일 : 2026-11-12

# 설명
Level 및 LevelxEXP 클래스에 대한 Edit Mode 테스트.

# 테스트 구성
 E: Level 기본 기능 (생성/레벨업/제한)
 P: LevelxEXP 경험치 처리 (생성/EXP/오버플로우/Value 리셋/조회)
 J: JSON 직렬화 (Level / LevelxEXP)
 X: 예외 처리 (음수 Max/음수 EXP/null 테이블/음수 테이블/빈 테이블/0포함 테이블)
========================================================================= BLOCK_HEADER_END */

using UnityEngine;

using NUnit;
using NUnit.Framework;

using inonego.Xeri.Game;

namespace inonego.Xeri.Game.TEST._Level
{

    // ============================================================
    /// <summary>
    /// Level 및 LevelxEXP 시스템의 핵심 기능 테스트 클래스.
    /// </summary>
    // ============================================================
    public class TEST_Level
    {

    #region E-1: Level 기본 생성

        [Test]
        public void TEST_Level_기본_생성_초기값()
        {
            var level = new Level(10);

            Assert.AreEqual(0, level.Value, "초기 레벨은 0이어야 합니다");
            Assert.AreEqual(0, level.Min, "최소 레벨은 0이어야 합니다");
            Assert.AreEqual(10, level.Max, "최대 레벨은 설정값과 같아야 합니다");
            Assert.AreEqual(10, level.FullMax, "FullMax는 설정값과 같아야 합니다");
            Assert.AreEqual(10, level.LimitMax, "LimitMax는 초기에 FullMax와 같아야 합니다");
            Assert.IsTrue(level.CanLevelUp, "초기에는 레벨업이 가능해야 합니다");
            Assert.IsFalse(level.BlockLevelUp, "초기에는 레벨업 블록이 해제되어야 합니다");
        }

    #endregion

    #region E-2: Level 레벨업

        [Test]
        public void TEST_Level_LevelUp_단일_이벤트_발생()
        {
            var level = new Level(5);
            bool eventFired = false;
            int eventLevel = -1;

            level.OnLevelUp += (sender, e) => {
                eventFired = true;
                eventLevel = e.Level;
            };

            level.LevelUp();

            Assert.AreEqual(1, level.Value, "레벨업 후 레벨이 1 증가해야 합니다");
            Assert.IsTrue(eventFired, "레벨업 이벤트가 발생해야 합니다");
            Assert.AreEqual(1, eventLevel, "이벤트에서 올바른 레벨을 전달해야 합니다");
        }

        [Test]
        public void TEST_Level_LevelUp_다중()
        {
            var level = new Level(10);
            int eventCount = 0;

            level.OnLevelUp += (sender, e) => eventCount++;

            level.LevelUp(3);

            Assert.AreEqual(3, level.Value, "3번 레벨업하면 레벨 3이 되어야 합니다");
            Assert.AreEqual(3, eventCount, "3번의 레벨업 이벤트가 발생해야 합니다");
        }

    #endregion

    #region E-3: Level 레벨업 제한

        [Test]
        public void TEST_Level_최대레벨_도달시_레벨업_불가()
        {
            var level = new Level(3);
            level.Value = 3;
            bool eventFired = false;

            level.OnLevelUp += (sender, e) => eventFired = true;

            level.LevelUp();

            Assert.AreEqual(3, level.Value, "최대 레벨에서는 레벨이 증가하지 않아야 합니다");
            Assert.IsFalse(level.CanLevelUp, "최대 레벨에서는 레벨업이 불가능해야 합니다");
            Assert.IsFalse(eventFired, "최대 레벨에서는 레벨업 이벤트가 발생하지 않아야 합니다");
        }

        [Test]
        public void TEST_Level_BlockLevelUp_True시_레벨업_차단()
        {
            var level = new Level(5);
            level.BlockLevelUp = true;
            bool eventFired = false;

            level.OnLevelUp += (sender, e) => eventFired = true;

            level.LevelUp();

            Assert.AreEqual(0, level.Value, "레벨업이 차단되면 레벨이 증가하지 않아야 합니다");
            Assert.IsFalse(level.CanLevelUp, "레벨업이 차단되면 CanLevelUp이 false여야 합니다");
            Assert.IsFalse(eventFired, "레벨업이 차단되면 레벨업 이벤트가 발생하지 않아야 합니다");
        }

        [Test]
        public void TEST_Level_LimitMax_설정시_Max_갱신()
        {
            var level = new Level(10);

            level.LimitMax = 5;

            Assert.AreEqual(5, level.Max, "Max는 LimitMax와 FullMax 중 작은 값이어야 합니다");
            Assert.AreEqual(5, level.LimitMax, "LimitMax가 올바르게 설정되어야 합니다");
        }

    #endregion

    #region P-1: LevelxEXP 기본 생성

        [Test]
        public void TEST_LevelxEXP_기본_생성_초기값()
        {
            var expTable = new int[] { 10, 20, 30, 40, 50 };

            var level = new LevelxEXP(expTable);

            Assert.AreEqual(0, level.Value, "초기 레벨은 0이어야 합니다");
            Assert.AreEqual(0, level.EXP, "초기 경험치는 0이어야 합니다");
            Assert.AreEqual(5, level.FullMax, "FullMax는 경험치 테이블 크기와 같아야 합니다");
            Assert.AreEqual(10, level.MaxEXP, "레벨 0의 최대 경험치는 10이어야 합니다");
            Assert.IsTrue(level.CanLevelUp, "초기에는 레벨업이 가능해야 합니다");
        }

    #endregion

    #region P-2: LevelxEXP EXP 설정 및 자동 레벨업

        [Test]
        public void TEST_LevelxEXP_EXP_설정_레벨업_미만()
        {
            var expTable = new int[] { 10, 20, 30, 40, 50 };
            var level = new LevelxEXP(expTable);

            level.EXP = 5;

            Assert.AreEqual(5, level.EXP, "경험치가 올바르게 설정되어야 합니다");
            Assert.AreEqual(0, level.Value, "경험치가 최대치 미만이면 레벨업하지 않아야 합니다");
        }

        [Test]
        public void TEST_LevelxEXP_EXP_정확히_도달시_자동_레벨업()
        {
            var expTable = new int[] { 10, 20, 30, 40, 50 };
            var level = new LevelxEXP(expTable);
            bool eventFired = false;
            int eventLevel = -1;

            level.OnLevelUp += (sender, e) => {
                eventFired = true;
                eventLevel = e.Level;
            };

            level.EXP = 10;

            Assert.AreEqual(1, level.Value, "경험치가 최대치에 도달하면 레벨업해야 합니다");
            Assert.AreEqual(0, level.EXP, "레벨업 후 경험치는 0이 되어야 합니다");
            Assert.IsTrue(eventFired, "레벨업 이벤트가 발생해야 합니다");
            Assert.AreEqual(1, eventLevel, "이벤트에서 올바른 레벨을 전달해야 합니다");
        }

    #endregion

    #region P-3: LevelxEXP 오버플로우 및 다중 레벨업

        [Test]
        public void TEST_LevelxEXP_EXP_초과시_오버플로우_이월()
        {
            var expTable = new int[] { 10, 20, 30, 40, 50 };
            var level = new LevelxEXP(expTable);

            level.EXP = 15;

            Assert.AreEqual(1, level.Value, "레벨업이 발생해야 합니다");
            Assert.AreEqual(5, level.EXP, "남은 경험치가 다음 레벨로 이월되어야 합니다");
            Assert.AreEqual(20, level.MaxEXP, "레벨 1의 최대 경험치는 20이어야 합니다");
        }

        [Test]
        public void TEST_LevelxEXP_EXP_큰값_다중_레벨업()
        {
            var expTable = new int[] { 10, 20, 30, 40, 50 };
            var level = new LevelxEXP(expTable);
            int eventCount = 0;

            level.OnLevelUp += (sender, e) => eventCount++;

            level.EXP = 35;

            Assert.AreEqual(2, level.Value, "35 경험치로 레벨 2가 되어야 합니다");
            Assert.AreEqual(5, level.EXP, "5의 경험치가 남아야 합니다");
            Assert.AreEqual(2, eventCount, "2번의 레벨업 이벤트가 발생해야 합니다");
        }

        [Test]
        public void TEST_LevelxEXP_최대레벨_도달시_EXP_제한()
        {
            var expTable = new int[] { 10, 20 };
            var level = new LevelxEXP(expTable);
            level.Value = 2;
            bool eventFired = false;

            level.OnLevelUp += (sender, e) => eventFired = true;

            level.EXP = 100;

            Assert.AreEqual(2, level.Value, "최대 레벨을 초과해서는 안 됩니다");
            Assert.AreEqual(0, level.EXP, "최대 레벨에서는 경험치가 0으로 제한되어야 합니다");
            Assert.IsFalse(level.CanLevelUp, "최대 레벨에서는 레벨업이 불가능해야 합니다");
            Assert.IsFalse(eventFired, "최대 레벨에서는 레벨업 이벤트가 발생하지 않아야 합니다");
        }

    #endregion

    #region P-4: LevelxEXP Value 직접 설정

        [Test]
        public void TEST_LevelxEXP_Value_직접_설정시_EXP_리셋()
        {
            var expTable = new int[] { 10, 20, 30, 40, 50 };
            var level = new LevelxEXP(expTable);
            level.EXP = 15;

            level.Value = 3;

            Assert.AreEqual(3, level.Value, "레벨이 직접 설정되어야 합니다");
            Assert.AreEqual(0, level.EXP, "레벨을 직접 설정하면 경험치가 리셋되어야 합니다");
            Assert.AreEqual(40, level.MaxEXP, "레벨 3의 최대 경험치는 40이어야 합니다");
        }

    #endregion

    #region P-5: LevelxEXP 조회

        [Test]
        public void TEST_LevelxEXP_GetRequiredEXPToLevelUp_범위내외()
        {
            var expTable = new int[] { 10, 20, 30, 40, 50 };
            var level = new LevelxEXP(expTable);

            Assert.AreEqual(10, level.GetRequiredEXPToLevelUp(0), "레벨 0의 필요 경험치는 10이어야 합니다");
            Assert.AreEqual(20, level.GetRequiredEXPToLevelUp(1), "레벨 1의 필요 경험치는 20이어야 합니다");
            Assert.AreEqual(50, level.GetRequiredEXPToLevelUp(4), "레벨 4의 필요 경험치는 50이어야 합니다");
            Assert.AreEqual(0, level.GetRequiredEXPToLevelUp(5), "범위 밖 레벨의 필요 경험치는 0이어야 합니다");
            Assert.AreEqual(0, level.GetRequiredEXPToLevelUp(-1), "음수 레벨의 필요 경험치는 0이어야 합니다");
        }

    #endregion

    #region J-1: Level JSON 직렬화

        [Test]
        public void TEST_Level_JSON_직렬화_역직렬화_상태_복원()
        {
            var originalLevel = new Level(10);
            originalLevel.LevelUp(3);
            originalLevel.LimitMax = 5;

            string json = JsonUtility.ToJson(originalLevel);
            var deserializedLevel = JsonUtility.FromJson<Level>(json);

            Assert.AreEqual(originalLevel.Value, deserializedLevel.Value, "레벨 값이 올바르게 복원되어야 합니다");
            Assert.AreEqual(originalLevel.Min, deserializedLevel.Min, "최소 레벨이 올바르게 복원되어야 합니다");
            Assert.AreEqual(originalLevel.Max, deserializedLevel.Max, "최대 레벨이 올바르게 복원되어야 합니다");
            Assert.AreEqual(originalLevel.FullMax, deserializedLevel.FullMax, "FullMax가 올바르게 복원되어야 합니다");
            Assert.AreEqual(originalLevel.LimitMax, deserializedLevel.LimitMax, "LimitMax가 올바르게 복원되어야 합니다");
        }

    #endregion

    #region J-2: LevelxEXP JSON 직렬화

        [Test]
        public void TEST_LevelxEXP_JSON_직렬화_역직렬화_상태_복원()
        {
            var expTable = new int[] { 10, 20, 30, 40, 50 };
            var originalLevelxEXP = new LevelxEXP(expTable);
            originalLevelxEXP.EXP = 25;
            originalLevelxEXP.LimitMax = 3;

            string json = JsonUtility.ToJson(originalLevelxEXP);
            var deserializedLevelxEXP = JsonUtility.FromJson<LevelxEXP>(json);

            Assert.AreEqual(originalLevelxEXP.Value, deserializedLevelxEXP.Value, "레벨 값이 올바르게 복원되어야 합니다");
            Assert.AreEqual(originalLevelxEXP.EXP, deserializedLevelxEXP.EXP, "경험치가 올바르게 복원되어야 합니다");
            Assert.AreEqual(originalLevelxEXP.MaxEXP, deserializedLevelxEXP.MaxEXP, "최대 경험치가 올바르게 복원되어야 합니다");
            Assert.AreEqual(originalLevelxEXP.FullMax, deserializedLevelxEXP.FullMax, "FullMax가 올바르게 복원되어야 합니다");
            Assert.AreEqual(originalLevelxEXP.LimitMax, deserializedLevelxEXP.LimitMax, "LimitMax가 올바르게 복원되어야 합니다");

            Assert.IsNotNull(deserializedLevelxEXP.RequiredEXPToLevelUpArray, "경험치 테이블이 직렬화되어야 합니다");
            var deserializedExpTable = deserializedLevelxEXP.RequiredEXPToLevelUpArray;
            Assert.AreEqual(expTable.Length, deserializedExpTable.Count, "경험치 테이블 길이가 올바르게 복원되어야 합니다");
            for (int i = 0; i < expTable.Length; i++)
            {
                Assert.AreEqual(expTable[i], deserializedExpTable[i], $"경험치 테이블[{i}] 값이 올바르게 복원되어야 합니다");
            }
        }

    #endregion

    #region X-1: Level 음수 Max 예외

        [Test]
        public void TEST_Level_음수_Max_InvalidMaxLevelException()
        {
            Assert.Throws<Level.InvalidMaxLevelException>(() => new Level(-1));
        }

    #endregion

    #region X-2: LevelxEXP 음수 EXP 예외

        [Test]
        public void TEST_LevelxEXP_음수_EXP_설정_InvalidEXPException()
        {
            var expTable = new int[] { 10, 20, 30 };
            var level = new LevelxEXP(expTable);
            level.EXP = 5;

            Assert.Throws<LevelxEXP.InvalidEXPException>(() => level.EXP = -10);

            Assert.AreEqual(5, level.EXP, "예외 발생 시 기존 경험치는 변경되지 않아야 합니다");
            Assert.AreEqual(0, level.Value, "예외 발생 시 레벨도 변경되지 않아야 합니다");
        }

    #endregion

    #region X-3: LevelxEXP 빈 테이블

        [Test]
        public void TEST_LevelxEXP_빈_테이블_레벨업_불가()
        {
            var expTable = new int[] { };

            var level = new LevelxEXP(expTable);

            Assert.AreEqual(0, level.FullMax, "빈 테이블의 FullMax는 0이어야 합니다");
            Assert.AreEqual(0, level.MaxEXP, "빈 테이블의 MaxEXP는 0이어야 합니다");
            Assert.IsFalse(level.CanLevelUp, "빈 테이블에서는 레벨업이 불가능해야 합니다");
        }

    #endregion

    #region X-4: LevelxEXP 0 포함 테이블

        [Test]
        public void TEST_LevelxEXP_0_포함_테이블_부분_레벨업()
        {
            var expTable = new int[] { 0, 10, 0, 20 };
            var level = new LevelxEXP(expTable);

            level.EXP = 1;

            Assert.AreEqual(1, level.Value, "레벨 0에서 1로 레벨업하고, 레벨 1에서 10EXP 미만이므로 더 이상 레벨업하지 않습니다");
            Assert.AreEqual(1, level.EXP, "레벨 1에서 1 경험치가 남아야 합니다");
        }

    #endregion

    #region X-5: LevelxEXP null 테이블 예외

        [Test]
        public void TEST_LevelxEXP_생성자_null_테이블_NullEXPTableException()
        {
            Assert.Throws<LevelxEXP.NullEXPTableException>(() => new LevelxEXP(null));
        }

    #endregion

    #region X-6: LevelxEXP 음수 포함 테이블 예외

        [Test]
        public void TEST_LevelxEXP_생성자_음수_포함_InvalidEXPTableException()
        {
            var expTable = new int[] { 10, -5, 20 };

            Assert.Throws<LevelxEXP.InvalidEXPTableException>(() => new LevelxEXP(expTable));
        }

    #endregion

    }

}
