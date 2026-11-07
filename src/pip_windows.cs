using System.Collections.Generic;
using Parkitect.UI;
using UnityEngine;
using UnityEngine.UI;

namespace pip_cam
{
    // The game's windows need a UIWindow as their content, and the class itself has nothing to add.
    internal sealed class pip_window_content : UIWindow
    {
    }

    // Reads the hotkey and opens the view windows.
    internal sealed class pip_windows : MonoBehaviour
    {
        // Also the name the game remembers the windows' position and size under, shared by all of them.
        private const string window_name = "PipCamWindow";
        private static readonly Vector2 min_size = new Vector2(200f, 125f);
        private static readonly Vector2 default_size = new Vector2(420f, 260f);

        // In tiles from the middle of the view to its top edge: a person or a ride close up, a spot wider.
        private const float follow_half_height = 6f;
        private const float spot_half_height = 15f;

        // The game's camera looks down at this angle, which its map view also returns to.
        private const float game_tilt = 45f;
        // Used when the game's own camera can't be found, roughly its field of view.
        private const float fallback_fov = 30f;

        private const int ground_mask = LayerMasks.TERRAIN | LayerMasks.PATHS;

        private readonly List<pip_view> views = new List<pip_view>();
        private int next_id;

        private void Update()
        {
            GameController game = GameController.Instance;
            if (game == null || game.park == null || game.isLoadingGame)
            {
                return;
            }
            views.RemoveAll(view => view == null);
            if (!UIUtility.isInputFieldFocused() && InputManager.getKeyDown(pip_cam_mod.open_hotkey_id))
            {
                open();
            }
        }

        private void LateUpdate()
        {
            GameController game = GameController.Instance;
            if (game == null || game.park == null || game.isLoadingGame)
            {
                people_boost.restore_all();
                return;
            }
            views.RemoveAll(view => view == null);
            people_boost.update(game.park, views);
        }

        private void open()
        {
            if (views.Count >= pip_cam_settings.max_windows)
            {
                ErrorMessageController.showErrorMessage(texts.too_many_windows);
                return;
            }
            Camera main = Camera.main;
            if (main == null)
            {
                return;
            }

            // What to show: the selection, or the spot in the middle of the screen, facing the way the camera faces.
            SerializedMonoBehaviour target = selected_object.find(out _);
            Vector3 center = main.transform.position;
            if (Physics.Raycast(main.ScreenPointToRay(new Vector3(Screen.width / 2f, Screen.height / 2f, 0f)), out RaycastHit hit, 5000f, ground_mask, QueryTriggerInteraction.Collide))
            {
                center = hit.point;
            }
            float yaw = main.transform.eulerAngles.y;

            GameObject content_object = new GameObject(window_name, typeof(RectTransform));
            ((RectTransform)content_object.transform).sizeDelta = min_size;
            UIWindowSettings window_settings = content_object.AddComponent<UIWindowSettings>();
            window_settings.title = texts.window_title;
            window_settings.resizeability = UIWindowSettings.Resizeability.Horizontal | UIWindowSettings.Resizeability.Vertical;

            GameObject image_object = new GameObject("view", typeof(RectTransform));
            image_object.transform.SetParent(content_object.transform, false);
            RectTransform image_rect = (RectTransform)image_object.transform;
            image_rect.anchorMin = Vector2.zero;
            image_rect.anchorMax = Vector2.one;
            image_rect.offsetMin = Vector2.zero;
            image_rect.offsetMax = Vector2.zero;
            RawImage image = image_object.AddComponent<RawImage>();
            image.color = Color.white;
            pip_view view = image_object.AddComponent<pip_view>();

            pip_window_content content = content_object.AddComponent<pip_window_content>();
            UIWindowFrame frame = UIWindowsController.Instance.spawnWindow(content, "pip_cam_" + next_id++);

            // A new window starts larger than the smallest it can be resized to.
            LayoutElement size = content_object.GetComponent<LayoutElement>();
            if (size != null && Settings.Instance.getWindowSettings(window_name).size == Vector2.zero)
            {
                size.preferredWidth = default_size.x;
                size.preferredHeight = default_size.y;
            }

            view.attach(frame, image, transform, target, center, yaw, game_tilt, game_fov(), target != null ? follow_half_height : spot_half_height);
            views.Add(view);
        }

        // The game's own camera, even while the Perspective Camera mod has put it away, keeps its field of view.
        private static float game_fov()
        {
            foreach (CameraController controller in Resources.FindObjectsOfTypeAll<CameraController>())
            {
                if (controller != null && controller.GetType() == typeof(CameraController) && controller.gameObject.scene.IsValid())
                {
                    Camera camera = controller.GetComponent<Camera>();
                    if (camera != null)
                    {
                        return camera.fieldOfView;
                    }
                }
            }
            return fallback_fov;
        }

        private void OnDestroy()
        {
            foreach (pip_view view in views)
            {
                if (view != null)
                {
                    view.close();
                }
            }
            views.Clear();
            people_boost.restore_all();
        }
    }
}
