using System.Collections;
using System.Collections.Generic;
using Unity.EditorCoroutines.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace SkyMavis.AxieMixer3D.Editor
{
    public class AxieGenesDecoder : EditorWindow
    {
        const string GraphqlURL = "https://graphql-gateway.axieinfinity.com/graphql";

        [MenuItem("Tools/Axie Mixer 3D/Axie Genes Decoder")]
        public static void Open() => GetWindow<AxieGenesDecoder>();

        [SerializeField]
        int _id;
        [SerializeField]
        string _genes;
        [SerializeField]
        AxieDescriptor _descriptor;

        Vector2 _scrollPosition;
        SerializedObject _serializedObject;
        UnityWebRequest _fetchGeneRequest;

        void OnEnable()
        {
            titleContent = new("Axie Genes Decoder");
            _serializedObject = new(this);
        }

        void OnGUI()
        {
            using var scrollView = new EditorGUILayout.ScrollViewScope(_scrollPosition);
            _scrollPosition = scrollView.scrollPosition;

            _serializedObject.Update();

            using (new EditorGUI.DisabledScope((_fetchGeneRequest?.result ?? UnityWebRequest.Result.Success) == UnityWebRequest.Result.InProgress))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.PropertyField(_serializedObject.FindProperty(nameof(_id)));
                    if (GUILayout.Button("Fetch")) this.StartCoroutine(FetchGenes());
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.PropertyField(_serializedObject.FindProperty(nameof(_genes)));
                    if (GUILayout.Button("Decode")) DecodeGenes();
                }
            }

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.PropertyField(_serializedObject.FindProperty(nameof(_descriptor)));
            }

            _serializedObject.ApplyModifiedProperties();

        }

        IEnumerator FetchGenes()
        {
            var json = JsonUtility.ToJson(new FetchGenesRequest { query = $"{{ axie (axieId: \"{_id}\") {{ id, genes, newGenes }} }}" });
            var payload = System.Text.Encoding.UTF8.GetBytes(json);
            _fetchGeneRequest = new UnityWebRequest(GraphqlURL, "POST")
            {
                uploadHandler = new UploadHandlerRaw(payload) { contentType = "application/json" },
                downloadHandler = new DownloadHandlerBuffer()
            };

            yield return _fetchGeneRequest.SendWebRequest();

            if (_fetchGeneRequest.result != UnityWebRequest.Result.Success) yield break;

            json = _fetchGeneRequest.downloadHandler.text;
            var response = JsonUtility.FromJson<FetchGenesResponse>(json);

            if (response.data.axie?.newGenes is { } newGenes)
            {
                _genes = newGenes;
                DecodeGenes();
            }
        }

        void DecodeGenes()
        {
            _descriptor = AxieDescriptor.FromGenes(_genes);
        }

        [System.Serializable]
        struct FetchGenesRequest
        {
            public string query;
        }

        [System.Serializable]
        struct FetchGenesResponse
        {
            public Data data;

            [System.Serializable]
            public struct Data
            {
                public Axie axie;

                [System.Serializable]
                public class Axie
                {
                    public string newGenes;
                }
            }
        }
    }
}
