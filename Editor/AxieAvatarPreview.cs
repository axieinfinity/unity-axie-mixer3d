using UnityEditor;
using UnityEngine;

namespace SkyMavis.AxieMixer3D.Editor
{
    public class AxieAvatarPreview : EditorWindow
    {
        [MenuItem("Tools/Axie Mixer 3D/Axie Avatar Preview")]
        static void Open() => GetWindow<AxieAvatarPreview>();

        [SerializeField]
        AxieAvatarRenderParams _renderParams = new();

        Vector2 _scrollPosition;
        RenderTexture _texture;
        SerializedObject _serializedObject;
        SerializedProperty _renderParamsProp;

        void OnEnable()
        {
            titleContent = new("Axie Avatar Preview");
            _texture = new(_renderParams.width, _renderParams.height, 16, RenderTextureFormat.ARGB32);
            _serializedObject = new(this);
            _renderParamsProp = _serializedObject.FindProperty(nameof(_renderParams));
        }

        void OnDisable()
        {
            DestroyImmediate(_texture);
        }

        void Update()
        {
            Repaint();
        }

        void OnGUI()
        {
            using var scrollView = new EditorGUILayout.ScrollViewScope(_scrollPosition);
            _scrollPosition = scrollView.scrollPosition;

            _serializedObject.Update();
            EditorGUILayout.PropertyField(_renderParamsProp);
            _serializedObject.ApplyModifiedProperties();

            if (
                Selection.activeGameObject is { } gameObject &&
                gameObject.TryGetComponent<AxieCharacter3DBehaviour>(out var characterBehaviour) &&
                characterBehaviour.Character is { } character
            )
            {
                character.RenderAvatar(_texture, _renderParams);
                EditorGUILayout.LabelField("Preview Avatar");
                DrawTexture(_texture);

                EditorGUILayout.Separator();
                EditorGUILayout.LabelField("AxieCharacter3DBehaviour.Avatars");

                foreach (var avatar in characterBehaviour.Avatars)
                {
                    DrawTexture(avatar);
                }
            }
            else
            {
                EditorGUILayout.HelpBox("Select a GameObject with an active AxieCharacter3DBehaviour to preview avatar.", MessageType.Info);
            }

            static void DrawTexture(Texture texture)
            {
                var rect = GUILayoutUtility.GetAspectRect((float)texture.width / texture.height, GUILayout.MaxWidth(texture.width));
                EditorGUI.DrawPreviewTexture(rect, texture);
            }
        }
    }
}
