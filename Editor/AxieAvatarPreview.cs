using UnityEditor;
using UnityEngine;

namespace SkyMavis.AxieMixer3D.Editor
{
    public class AxieAvatarPreview : EditorWindow
    {
        [MenuItem("Tools/Axie Mixer 3D/Axie Avatar Preview")]
        static void Open() => GetWindow<AxieAvatarPreview>();

        Vector2 _scrollPosition;

        void OnEnable()
        {
            titleContent = new("Axie Avatar Preview");
        }

        void Update()
        {
            Repaint();
        }

        void OnGUI()
        {
            if (Selection.activeGameObject is not { } gameObject || !gameObject.TryGetComponent<AxieCharacter3DBehaviour>(out var characterBehaviour))
            {
                EditorGUILayout.HelpBox("Select a GameObject with AxieCharacter3DBehaviour to see the avatar previews.", MessageType.Info);
                return;
            }

            using var scrollView = new EditorGUILayout.ScrollViewScope(_scrollPosition);
            _scrollPosition = scrollView.scrollPosition;

            DrawAvatar("Front", characterBehaviour.Character.Avatars.Front);
            DrawAvatar("Back", characterBehaviour.Character.Avatars.Back);
            DrawAvatar("Left", characterBehaviour.Character.Avatars.Left);
            DrawAvatar("Right", characterBehaviour.Character.Avatars.Right);
            DrawAvatar("FrontLeft", characterBehaviour.Character.Avatars.FrontLeft);
            DrawAvatar("FrontRight", characterBehaviour.Character.Avatars.FrontRight);
            DrawAvatar("FrontLeftTop", characterBehaviour.Character.Avatars.FrontLeftTop);
            DrawAvatar("FrontRightTop", characterBehaviour.Character.Avatars.FrontRightTop);

            static void DrawAvatar(string name, RenderTexture texture)
            {
                EditorGUILayout.ObjectField(name, texture, typeof(RenderTexture), false);
                var rect = GUILayoutUtility.GetAspectRect(1f, GUILayout.MaxWidth(256f));
                EditorGUI.DrawPreviewTexture(rect, texture);
            }
        }
    }
}
