/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : TEST_HP_I.cs
수정일 : 2026-11-12

# 설명
HP_I (int 기반 체력) 유닛 테스트.
Unity Test Runner (Edit Mode) 에서 실행한다.

# 테스트 구성
 E: 기본 기능 (생성/상태 전환/힐·데미지/MaxValue/Ratio)
 V: 이벤트 (OnValueChange/OnMaxValueChange/OnStateChange/OnHeal/OnDamage)
 U: 유틸리티 (CalculateApplyAmount)
========================================================================= BLOCK_HEADER_END */

using NUnit;
using NUnit.Framework;

using inonego.Xeri.Game;

namespace inonego.Xeri.Game.TEST._HP
{

    // ============================================================
    /// <summary>
    /// HP_I (int 기반 체력) 핵심 기능 테스트.
    /// </summary>
    // ============================================================
    public class TEST_HP_I
    {

    #region E-1: 기본 생성

        [Test]
        public void TEST_HP_I_기본_생성_초기값()
        {
            var hp = new HP_I();

            Assert.AreEqual(0, hp.Value);
            Assert.AreEqual(0, hp.MaxValue);

            Assert.AreEqual(0.0f, hp.Ratio);

            Assert.AreEqual(HPState.Dead, hp.Current);
            Assert.IsFalse(hp.IsAlive);
            Assert.IsTrue(hp.IsDead);
        }

    #endregion

    #region E-2: 상태 변경

        [Test]
        public void TEST_HP_I_MakeAlive_MakeDead_Value_자동_상태_전환()
        {
            var hp = new HP_I();
            hp.MaxValue = 100;

            hp.MakeAlive();

            Assert.AreEqual(HPState.Alive, hp.Current);
            Assert.AreEqual(100, hp.Value);
            Assert.AreEqual(100, hp.MaxValue);
            Assert.IsTrue(hp.IsAlive);
            Assert.IsFalse(hp.IsDead);

            hp.MakeDead();

            Assert.AreEqual(HPState.Dead, hp.Current);
            Assert.AreEqual(0, hp.Value);
            Assert.IsFalse(hp.IsAlive);
            Assert.IsTrue(hp.IsDead);

            hp.Value = 50;

            Assert.AreEqual(HPState.Alive, hp.Current);
            Assert.AreEqual(50, hp.Value);
            Assert.IsTrue(hp.IsAlive);

            hp.Value = 0;

            Assert.AreEqual(HPState.Dead, hp.Current);
            Assert.AreEqual(0, hp.Value);
            Assert.IsTrue(hp.IsDead);
        }

    #endregion

    #region E-3: 힐과 데미지

        [Test]
        public void TEST_HP_I_ApplyHeal_ApplyDamage_사망후_무시()
        {
            var hp = new HP_I();
            hp.MaxValue = 100;
            hp.MakeAlive();
            hp.Value = 50;

            hp.ApplyHeal(30);

            Assert.AreEqual(80, hp.Value);
            Assert.AreEqual(0.8f, hp.Ratio);

            hp.ApplyHeal(50);

            Assert.AreEqual(100, hp.Value);
            Assert.AreEqual(1.0f, hp.Ratio);

            hp.ApplyDamage(40);

            Assert.AreEqual(60, hp.Value);
            Assert.AreEqual(0.6f, hp.Ratio);

            hp.ApplyDamage(100);

            Assert.AreEqual(0, hp.Value);
            Assert.AreEqual(HPState.Dead, hp.Current);
            Assert.IsTrue(hp.IsDead);

            hp.ApplyHeal(50);

            Assert.AreEqual(0, hp.Value);
            Assert.IsTrue(hp.IsDead);

            hp.ApplyDamage(30);

            Assert.AreEqual(0, hp.Value);
            Assert.IsTrue(hp.IsDead);
        }

    #endregion

    #region E-4: 최대 체력 변경

        [Test]
        public void TEST_HP_I_MaxValue_변경_클램핑_및_사망_전환()
        {
            var hp = new HP_I();
            hp.MaxValue = 100;
            hp.MakeAlive();
            hp.Value = 80;

            hp.MaxValue = 150;

            Assert.AreEqual(150, hp.MaxValue);
            Assert.AreEqual(80, hp.Value);
            Assert.AreEqual(80.0f / 150.0f, hp.Ratio);

            hp.MaxValue = 50;

            Assert.AreEqual(50, hp.MaxValue);
            Assert.AreEqual(50, hp.Value);
            Assert.AreEqual(1.0f, hp.Ratio);

            hp.MaxValue = 0;

            Assert.AreEqual(0, hp.MaxValue);
            Assert.AreEqual(0, hp.Value);
            Assert.AreEqual(0.0f, hp.Ratio);
            Assert.IsTrue(hp.IsDead);
        }

    #endregion

    #region E-5: Ratio 계산

        [Test]
        public void TEST_HP_I_Ratio_다양한_값_및_MaxValue_0()
        {
            var hp = new HP_I();
            hp.MaxValue = 100;
            hp.MakeAlive();

            hp.Value = 0;
            Assert.AreEqual(0.0f, hp.Ratio);

            hp.Value = 25;
            Assert.AreEqual(0.25f, hp.Ratio);

            hp.Value = 50;
            Assert.AreEqual(0.5f, hp.Ratio);

            hp.Value = 75;
            Assert.AreEqual(0.75f, hp.Ratio);

            hp.Value = 100;
            Assert.AreEqual(1.0f, hp.Ratio);

            hp.MaxValue = 0;
            Assert.AreEqual(0.0f, hp.Ratio);
        }

    #endregion

    #region V-1: 이벤트 발생

        [Test]
        public void TEST_HP_I_이벤트_통합()
        {
            var hp = new HP_I();

            bool valueChangeFired    = false;
            bool maxValueChangeFired = false;
            bool stateChangeFired    = false;
            bool healFired           = false;
            bool damageFired         = false;

            HP_I valueChangeSender    = null;
            HP_I maxValueChangeSender = null;
            HP_I stateChangeSender    = null;
            HP_I healSender           = null;
            HP_I damageSender         = null;

            ValueChangeEventArgs<int>     valueChangeArgs = default;
            ValueChangeEventArgs<int>     maxValueArgs    = default;
            ValueChangeEventArgs<HPState> stateChangeArgs = default;
            HPApplyEventArgs<int>         healArgs        = default;
            HPApplyEventArgs<int>         damageArgs      = default;

            void Reset()
            {
                valueChangeFired = maxValueChangeFired = stateChangeFired = healFired = damageFired = false;

                valueChangeSender = maxValueChangeSender = stateChangeSender = healSender = damageSender = null;

                valueChangeArgs = maxValueArgs = default;
                stateChangeArgs = default;
                healArgs = damageArgs = default;
            }

            hp.OnValueChange    += (sender, e) => { valueChangeFired    = true; valueChangeSender    = sender as HP_I; valueChangeArgs = e; };
            hp.OnMaxValueChange += (sender, e) => { maxValueChangeFired = true; maxValueChangeSender = sender as HP_I; maxValueArgs    = e; };
            hp.OnStateChange    += (sender, e) => { stateChangeFired    = true; stateChangeSender    = sender as HP_I; stateChangeArgs = e; };
            hp.OnHeal           += (sender, e) => { healFired           = true; healSender           = sender as HP_I; healArgs        = e; };
            hp.OnDamage         += (sender, e) => { damageFired         = true; damageSender         = sender as HP_I; damageArgs      = e; };

            // OnMaxValueChange
            hp.MaxValue = 100;

            Assert.IsTrue(maxValueChangeFired);
            Assert.AreEqual(hp,  maxValueChangeSender);
            Assert.AreEqual(0,   maxValueArgs.Previous);
            Assert.AreEqual(100, maxValueArgs.Current);

            Reset();

            // OnStateChange
            hp.MakeAlive();

            Assert.IsTrue(stateChangeFired);
            Assert.AreEqual(hp,            stateChangeSender);
            Assert.AreEqual(HPState.Dead,  stateChangeArgs.Previous);
            Assert.AreEqual(HPState.Alive, stateChangeArgs.Current);

            Reset();

            // OnValueChange
            hp.Value = 50;

            Assert.IsTrue(valueChangeFired);
            Assert.AreEqual(hp,  valueChangeSender);
            Assert.AreEqual(100, valueChangeArgs.Previous);
            Assert.AreEqual(50,  valueChangeArgs.Current);

            Reset();

            // OnHeal
            hp.ApplyHeal(30);

            Assert.IsTrue(healFired);
            Assert.AreEqual(hp, healSender);
            Assert.AreEqual(30, healArgs.Amount);

            Reset();

            // OnDamage
            hp.ApplyDamage(20);

            Assert.IsTrue(damageFired);
            Assert.AreEqual(hp, damageSender);
            Assert.AreEqual(20, damageArgs.Amount);
        }

    #endregion

    #region U-1: CalculateApplyAmount

        [Test]
        public void TEST_HP_I_CalculateApplyAmount_비율_타입별()
        {
            var hp = new HP_I();
            hp.MaxValue = 100;
            hp.MakeAlive();
            hp.Value = 60;

            int amount1 = hp.CalculateApplyAmount(0.5f, HPApplyRatioType.ByValue);

            Assert.AreEqual(30, amount1);

            int amount2 = hp.CalculateApplyAmount(0.3f, HPApplyRatioType.ByMaxValue);

            Assert.AreEqual(30, amount2);

            int amount3 = hp.CalculateApplyAmount(0.5f, HPApplyRatioType.ByMissingValue);

            Assert.AreEqual(20, amount3);
        }

    #endregion

    }

}
