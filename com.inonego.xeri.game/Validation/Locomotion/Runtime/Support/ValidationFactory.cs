/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : ValidationFactory.cs
수정일 : 2026-11-22

# 설명
Locomotion 검증 씬이 쓰는 큐브·바닥·선 같은 단순 도형과 단색 머티리얼을 코드로 만든다.

# 특이사항, 제약사항
모델링 자산 없이 Unity 프리미티브만 쓴다.
렌더 파이프라인(HDRP·URP·Built-in)에 맞는 Unlit 셰이더를 찾아 조명 없이도 색이 보이게 한다.
검증용 도구라 에디터 Play 모드에서만 쓴다.
========================================================================= BLOCK_HEADER_END */

using System.Collections.Generic;

using UnityEngine;

namespace inonego.Xeri.Game.Validation.Locomotion
{
    // ============================================================
    /// <summary>
    /// 검증 씬의 도형·머티리얼 생성 도구.
    /// </summary>
    // ============================================================
    internal static class ValidationFactory
    {

    #region 내부 데이터

        // 파이프라인별 Unlit 셰이더를 앞에서부터 찾는다.
        private static readonly string[] shaderNames =
        {
            "HDRP/Unlit", "Universal Render Pipeline/Unlit", "Unlit/Color", "Sprites/Default"
        };

        private static readonly Dictionary<Color, Material> materials = new();

        private static PhysicsMaterial noFriction;

    #endregion

    #region 머티리얼

        // ------------------------------------------------------------
        /// <summary>
        /// 밝기만 줄인 색을 돌려준다. 알파는 항상 1이다.
        /// </summary>
        // ------------------------------------------------------------
        public static Color Darken(Color color, float factor) => new Color(color.r * factor, color.g * factor, color.b * factor, 1f);

        // ------------------------------------------------------------
        /// <summary>
        /// 마찰이 없는 물리 머티리얼. 이동은 Motion 합성 결과로만 일어나게 한다.
        /// </summary>
        // ------------------------------------------------------------
        public static PhysicsMaterial NoFriction
        {
            get
            {
                if (noFriction == null)
                {
                    noFriction = new PhysicsMaterial("ValidationNoFriction")
                    {
                        dynamicFriction = 0f,
                        staticFriction = 0f,
                        bounciness = 0f,
                        frictionCombine = PhysicsMaterialCombine.Minimum,
                        bounceCombine = PhysicsMaterialCombine.Minimum
                    };
                }

                return noFriction;
            }
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 색 하나를 가진 단색 머티리얼을 돌려준다. 같은 색은 재사용한다.
        /// </summary>
        // ------------------------------------------------------------
        public static Material GetMaterial(Color color)
        {
            if (materials.TryGetValue(color, out var cached) && cached != null)
            {
                return cached;
            }

            var material = CreateMaterial(color);

            materials[color] = material;

            return material;
        }

        private static Material CreateMaterial(Color color)
        {
            for (var index = 0; index < shaderNames.Length; index++)
            {
                var shader = Shader.Find(shaderNames[index]);

                if (shader == null)
                {
                    continue;
                }

                var material = new Material(shader);

                // 셰이더마다 색 속성 이름이 달라 있는 것에만 넣는다.
                SetColor(material, "_UnlitColor", color);
                SetColor(material, "_BaseColor", color);
                SetColor(material, "_Color", color);

                return material;
            }

            // 알려진 Unlit이 없으면 프리미티브의 기본 머티리얼을 복사해 쓴다.
            var temp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var fallback = new Material(temp.GetComponent<Renderer>().sharedMaterial);

            Object.Destroy(temp);

            SetColor(fallback, "_BaseColor", color);
            SetColor(fallback, "_Color", color);

            return fallback;
        }

        private static void SetColor(Material material, string property, Color color)
        {
            if (material.HasProperty(property))
            {
                material.SetColor(property, color);
            }
        }

    #endregion

    #region 도형

        // ------------------------------------------------------------
        /// <summary>
        /// 색과 크기를 가진 상자를 만든다. collider를 원하지 않으면 렌더링만 한다.
        /// </summary>
        // ------------------------------------------------------------
        public static GameObject CreateBox
        (
            string name, Transform parent, Vector3 localPosition, Vector3 size, Color color, bool withCollider = true
        )
        {
            return CreateShape(PrimitiveType.Cube, name, parent, localPosition, size, color, withCollider);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 색과 크기를 가진 구를 만든다.
        /// </summary>
        // ------------------------------------------------------------
        public static GameObject CreateSphere
        (
            string name, Transform parent, Vector3 localPosition, float diameter, Color color, bool withCollider = true
        )
        {
            return CreateShape(PrimitiveType.Sphere, name, parent, localPosition, Vector3.one * diameter, color, withCollider);
        }

        private static GameObject CreateShape
        (
            PrimitiveType type, string name, Transform parent, Vector3 localPosition, Vector3 size, Color color, bool withCollider
        )
        {
            var shape = GameObject.CreatePrimitive(type);

            shape.name = name;
            shape.transform.SetParent(parent, false);
            shape.transform.localPosition = localPosition;
            shape.transform.localScale = size;
            shape.GetComponent<Renderer>().sharedMaterial = GetMaterial(color);

            var collider = shape.GetComponent<Collider>();

            if (!withCollider)
            {
                // 같은 프레임의 Physics에 남지 않게 바로 지운다.
                Object.DestroyImmediate(collider);
            }
            else
            {
                collider.sharedMaterial = NoFriction;
            }

            return shape;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 월드 좌표로 그리는 선 하나를 만든다. 속도 화살표에 쓴다.
        /// </summary>
        // ------------------------------------------------------------
        public static LineRenderer CreateLine(string name, Transform parent, Color color, float width)
        {
            var line = new GameObject(name);

            line.transform.SetParent(parent, false);

            var renderer = line.AddComponent<LineRenderer>();

            renderer.useWorldSpace = true;
            renderer.positionCount = 2;
            renderer.startWidth = width;
            renderer.endWidth = width;
            renderer.sharedMaterial = GetMaterial(color);
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            return renderer;
        }

    #endregion

    }
}
