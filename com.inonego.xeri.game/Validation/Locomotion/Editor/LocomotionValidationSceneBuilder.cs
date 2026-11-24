/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : LocomotionValidationSceneBuilder.cs
수정일 : 2026-11-22

# 설명
Locomotion 검증 씬을 만드는 에디터 메뉴. 카메라와 LocomotionValidationScene 컴포넌트만 둔 빈 씬을 저장한다.
구역과 도형은 Play 때 코드가 만든다.

# 특이사항, 제약사항
패키지가 편집 가능한 위치(file: 참조, embedded)에 있으면 패키지의 Validation/Locomotion/Scenes에 저장하고,
읽기 전용이면 저장 위치를 묻는다.
========================================================================= BLOCK_HEADER_END */

using System.IO;

using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.SceneManagement;

using UnityEngine;

namespace inonego.Xeri.Game.Validation.Locomotion.Editor
{
    // ============================================================
    /// <summary>
    /// Locomotion 검증 씬 생성·열기 메뉴.
    /// </summary>
    // ============================================================
    public static class LocomotionValidationSceneBuilder
    {

    #region 필드

        private const string SCENE_NAME = "LocomotionValidation.unity";
        private const string MENU_ROOT = "Tools/Xeri Game/";

    #endregion

    #region 메뉴

        // ------------------------------------------------------------
        /// <summary>
        /// 검증 씬을 새로 만들어 저장하고 연다.
        /// </summary>
        // ------------------------------------------------------------
        [MenuItem(MENU_ROOT + "Locomotion 검증 씬 만들기")]
        public static void CreateScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            var path = ResolveScenePath();

            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var cameraObject = new GameObject("Main Camera") { tag = "MainCamera" };
            var camera = cameraObject.AddComponent<Camera>();

            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.08f, 0.09f, 0.12f);
            cameraObject.transform.position = new Vector3(0f, 9f, -12f);
            cameraObject.transform.rotation = Quaternion.Euler(30f, 0f, 0f);
            cameraObject.AddComponent<AudioListener>();

            new GameObject("Locomotion Validation").AddComponent<LocomotionValidationScene>();

            EditorSceneManager.SaveScene(scene, path);
            AssetDatabase.Refresh();

            Debug.Log($"Locomotion 검증 씬을 저장했습니다: {path}");
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 저장된 검증 씬을 연다. 없으면 만든다.
        /// </summary>
        // ------------------------------------------------------------
        [MenuItem(MENU_ROOT + "Locomotion 검증 씬 열기")]
        public static void OpenScene()
        {
            var path = FindPackageScenePath();

            if (!string.IsNullOrEmpty(path) && AssetDatabase.LoadAssetAtPath<SceneAsset>(path) != null && EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                EditorSceneManager.OpenScene(path);

                return;
            }

            CreateScene();
        }

    #endregion

    #region 경로

        private static string FindPackageScenePath()
        {
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(LocomotionValidationScene).Assembly);

            if (package == null || package.source == PackageSource.Registry || package.source == PackageSource.BuiltIn)
            {
                return null;
            }

            return $"{package.assetPath}/Validation/Locomotion/Scenes/{SCENE_NAME}";
        }

        private static string ResolveScenePath()
        {
            var packagePath = FindPackageScenePath();

            if (!string.IsNullOrEmpty(packagePath))
            {
                // 저장소에는 빈 폴더가 남지 않으므로 저장 전에 폴더를 만든다.
                var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(LocomotionValidationScene).Assembly);

                Directory.CreateDirectory(Path.Combine(package.resolvedPath, "Validation", "Locomotion", "Scenes"));
                AssetDatabase.Refresh();

                return packagePath;
            }

            // 읽기 전용 패키지면 프로젝트 안의 위치를 직접 고르게 한다.
            return EditorUtility.SaveFilePanelInProject("Locomotion 검증 씬 저장", "LocomotionValidation", "unity", "저장할 위치를 고르세요.");
        }

    #endregion

    }
}
