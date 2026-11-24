/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : AbilityTestBase.cs
수정일 : 2026-11-19

# 설명
Ability 테스트가 공유하는 검증용 Spec과 요청 맥락, Ability Instance 생성 도우미를 제공한다.
========================================================================= BLOCK_HEADER_END */

using System;
using System.Collections;
using System.Collections.Generic;

namespace inonego.Xeri.Game.TEST._Ability.Fixture
{
    using Xeri;
    using Xeri.Game;

    // ============================================================
    /// <summary>
    /// Ability 테스트가 공유하는 검증용 Spec과 Instance 생성 도우미이다.
    /// </summary>
    // ============================================================
    public abstract class AbilityTestBase
    {

    #region 검증용 Spec과 Instance 생성

        protected sealed class TestSpec
        {
            // ------------------------------------------------------------
            /// <summary>
            /// 테스트용 Ability Spec의 이름을 반환한다.
            /// </summary>
            // ------------------------------------------------------------
            public string Name { get; }

            // ------------------------------------------------------------
            /// <summary>
            /// 이름으로 테스트용 Ability Spec을 생성한다. 이름은 생략할 수 있다.
            /// </summary>
            // ------------------------------------------------------------
            public TestSpec(string name = null)
            {
                Name = name;
            }
        }

        protected readonly struct TestPayload
        {
            // ------------------------------------------------------------
            /// <summary>
            /// 테스트용 요청 맥락의 버전을 반환한다.
            /// </summary>
            // ------------------------------------------------------------
            public int Version { get; }

            // ------------------------------------------------------------
            /// <summary>
            /// 버전으로 테스트용 요청 맥락을 생성한다.
            /// </summary>
            // ------------------------------------------------------------
            public TestPayload(int version)
            {
                Version = version;
            }
        }

        protected static AbilityInstance<TestSpec> CreateInstance(string key, string name = null)
        {
            var state = new AbilityState(new AbilityKey(key));
            return new AbilityInstance<TestSpec>
            (
                new TestSpec(name ?? key),
                state
            );
        }

    #endregion

    }
}
