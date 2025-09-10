using UnityEngine;

namespace SkyMavis.AxieMixer3D.Samples.AxieAnimations
{
    public class CameraController : MonoBehaviour
    {
        public Vector2 sensitivity = .2f * Vector2.one;

        Vector3 _lastPosition;

        void Update()
        {
            if (!Input.GetKey(KeyCode.Mouse1)) return;

            if (Input.GetKeyDown(KeyCode.Mouse1))
            {
                _lastPosition = Input.mousePosition;
                return;
            }

            var deltaPosition = sensitivity * (Input.mousePosition - _lastPosition);
            _lastPosition = Input.mousePosition;

            var euler = transform.eulerAngles;
            euler.x = Mathf.Clamp(euler.x - deltaPosition.y, 5f, 75f);
            euler.y += deltaPosition.x;
            transform.eulerAngles = euler;
        }

        void OnGUI()
        {
            GUI.Box(
                new Rect(Screen.width - 300f, 0f, 300f, 64f),
                "Use right mouse button to rotate the camera.",
                new GUIStyle(GUI.skin.box)
                {
                    fontSize = 24,
                    wordWrap = true,
                }
            );
        }
    }
}
