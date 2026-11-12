/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : DeliveryTestFixtureBase.cs
수정일 : 2026-11-12

# 설명
Delivery 테스트에서 생성한 Unity GameObject의 소유권과 테스트별 정리를 공통화한다.
========================================================================= BLOCK_HEADER_END */

using System;
using System.Collections;
using System.Collections.Generic;

using UnityEngine;

using NUnit;
using NUnit.Framework;

namespace inonego.Xeri.Game.TEST._Delivery
{
    // ============================================================
    /// <summary>
    /// <br/>테스트 픽스처가 생성한 Unity 객체를 추적하고 정리한다.
    /// <br/>차원별 Collider 종류와 배치 방식은 구체 테스트가 결정한다.
    /// </summary>
    // ============================================================
    public abstract class DeliveryTestFixtureBase
    {

    #region 생성 및 정리

        // ------------------------------------------------------------
        /// <summary>
        /// 테스트가 소유할 GameObject를 생성하고 정리 대상으로 등록한다.
        /// </summary>
        // ------------------------------------------------------------
        protected GameObject CreateObject(Vector3 position, string name = "Delivery Physics Test")
        {
            var gameObject = new GameObject(name);
            gameObject.layer = 30;
            gameObject.transform.position = position;
            createdObjects.Add(gameObject);
            return gameObject;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var gameObject in createdObjects)
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
            }

            createdObjects.Clear();
        }

    #endregion

    #region 필드

        private readonly List<GameObject> createdObjects = new();

    #endregion

    }
}
