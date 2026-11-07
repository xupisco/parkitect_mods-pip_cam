using Parkitect.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace pip_cam
{
    // One view window: a camera of its own drawing the park into the window's picture, from the same angle as the
    // game's camera. The game draws some things for its main camera only, so before each picture this does for its
    // camera what the game's park screenshot does for its own: the instanced scenery, the ride lights, the people the
    // main camera doesn't see, and the mask that cuts the terrain open for tunnels.
    internal sealed class pip_view : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IDragHandler, IScrollHandler, IPointerClickHandler
    {
        private const int ground_mask = LayerMasks.TERRAIN | LayerMasks.PATHS;
        private const float min_half_height = 0.5f;
        private const float max_half_height = 120f;
        private const float zoom_step = 0.12f;
        private const float turn_per_pixel = 0.3f;
        private const int min_texture_size = 64;

        private static readonly int mask_id = Shader.PropertyToID("TerrainClipsMaskTexture");
        private static readonly int mask_color_id = Shader.PropertyToID("TerrainClipsMaskTextureColor");
        private static readonly int clip_to_world_id = Shader.PropertyToID("clipToWorld");

        private UIWindowFrame frame;
        private RawImage image;

        private Camera view;
        private Camera mask_view;

        private const int ui_layer = 5;

        // What hangs on the main camera, its own object included, is only meant for its own picture: seen from a
        // view, it shows as a plane floating where the main camera is. Hidden while the view draws.
        private const float main_renderers_refresh = 2f;
        private Camera main_renderers_owner;
        private Renderer[] main_renderers = new Renderer[0];
        private float next_main_renderers;
        private readonly System.Collections.Generic.List<Renderer> hidden_renderers = new System.Collections.Generic.List<Renderer>();

        // The game's cloud shadows are a quad it places across the far end of the main camera's view every frame, which
        // any other camera sees as a grey plane. Its shader only works for the main camera (moved in front of a view it
        // covers the whole picture in grey), so it is hidden while a view draws.
        private Renderer cloud_shadow;
        private float next_cloud_shadow_search;

        private void hide_cloud_shadow()
        {
            if (cloud_shadow == null && Time.unscaledTime >= next_cloud_shadow_search)
            {
                next_cloud_shadow_search = Time.unscaledTime + main_renderers_refresh;
                ScreenSpaceCloudShadow found = FindObjectOfType<ScreenSpaceCloudShadow>();
                cloud_shadow = found != null ? found.GetComponent<Renderer>() : null;
            }
            if (cloud_shadow != null && cloud_shadow.enabled)
            {
                cloud_shadow.enabled = false;
                hidden_renderers.Add(cloud_shadow);
            }
        }

        private void find_main_camera_renderers(Camera main)
        {
            if (main == main_renderers_owner && Time.unscaledTime < next_main_renderers)
            {
                return;
            }
            main_renderers_owner = main;
            next_main_renderers = Time.unscaledTime + main_renderers_refresh;
            main_renderers = main.GetComponentsInChildren<Renderer>(true);
        }
        private RenderTexture picture;
        private RenderTexture mask_color;
        private RenderTexture mask_depth;

        // Followed while set; the view stays on center otherwise.
        private SerializedMonoBehaviour target;
        private Vector3 center;
        private float yaw;
        private float tilt;
        private float fov;
        // In tiles, from the middle of the view to its top edge.
        private float half_height;
        private float distance;

        private float next_picture;
        private int camera_locks;
        private readonly PersonLODManager hidden_people = new PersonLODManager();
        private readonly Plane[] frustum = new Plane[6];

        // The game's values, put back after this camera's picture.
        private bool swapped;
        private bool mask_swapped;
        private float saved_shadow_distance;
        private Texture saved_mask;
        private Texture saved_mask_color;
        private Matrix4x4 saved_clip_to_world;

        public void attach(UIWindowFrame window, RawImage picture_image, Transform holder, SerializedMonoBehaviour followed, Vector3 spot, float view_yaw, float view_tilt, float view_fov, float view_half_height)
        {
            frame = window;
            image = picture_image;
            target = followed;
            center = spot;
            yaw = view_yaw;
            tilt = view_tilt;
            fov = view_fov;
            half_height = view_half_height;
            frame.OnClose += on_closed;
            update_title();
            if (frame.windowTitlebar != null)
            {
                frame.windowTitlebar.gameObject.AddComponent<title_click>().on_click = open_target_window;
            }

            Camera main = Camera.main;
            GameObject camera_object = new GameObject("pip_cam_view");
            camera_object.transform.SetParent(holder, false);
            view = camera_object.AddComponent<Camera>();
            if (main != null)
            {
                view.CopyFrom(main);
                view.depthTextureMode = main.depthTextureMode;
                view.depth = main.depth - 10f;
            }
            view.targetTexture = null;
            view.allowHDR = false;
            view.orthographic = false;
            view.enabled = false;
            // The water reads what is behind it from a copy of the picture, which the game makes on its own camera.
            camera_object.AddComponent<GrabWaterBackground>();

            TerrainClipsMaskCamera clips = TerrainClipsMaskCamera.Instance;
            if (clips != null && clips.clipMaskCamera != null)
            {
                GameObject mask_object = new GameObject("pip_cam_mask");
                mask_object.transform.SetParent(holder, false);
                mask_view = mask_object.AddComponent<Camera>();
                mask_view.CopyFrom(clips.clipMaskCamera);
                mask_view.targetTexture = null;
                mask_view.enabled = false;
            }

            Camera.onPreCull += on_pre_cull;
            Camera.onPostRender += on_post_render;
        }

        public void close()
        {
            if (frame != null)
            {
                frame.close();
            }
        }

        private void on_closed(UIWindowFrame closed)
        {
            frame = null;
        }

        private void OnDisable()
        {
            if (view != null)
            {
                view.enabled = false;
            }
        }

        private void OnDestroy()
        {
            Camera.onPreCull -= on_pre_cull;
            Camera.onPostRender -= on_post_render;
            put_back();
            release_camera_locks();
            if (view != null)
            {
                Destroy(view.gameObject);
            }
            if (mask_view != null)
            {
                Destroy(mask_view.gameObject);
            }
            release_textures();
            if (frame != null)
            {
                frame.OnClose -= on_closed;
            }
        }

        private void LateUpdate()
        {
            if (view == null)
            {
                return;
            }
            put_back();
            GameController game = GameController.Instance;
            Camera main = Camera.main;
            if (game == null || game.park == null || main == null || Time.unscaledTime < next_picture)
            {
                view.enabled = false;
                return;
            }
            next_picture = Time.unscaledTime + 1f / pip_cam_settings.frames_per_second;

            if (!make_textures())
            {
                view.enabled = false;
                return;
            }
            place();

            // What the player hides with the game's view toggles, and the night sky, follow the main camera. Interface
            // never belongs in a view of the park.
            view.cullingMask = main.cullingMask & ~(1 << ui_layer);
            find_main_camera_renderers(main);
            view.clearFlags = main.clearFlags;
            view.backgroundColor = main.backgroundColor;

            draw_for_this_camera(game);
            if (mask_view != null)
            {
                mask_view.transform.SetPositionAndRotation(view.transform.position, view.transform.rotation);
                mask_view.orthographic = false;
                mask_view.fieldOfView = view.fieldOfView;
                mask_view.nearClipPlane = view.nearClipPlane;
                mask_view.farClipPlane = view.farClipPlane;
                mask_view.aspect = view.aspect;
                mask_view.Render();
            }
            view.enabled = true;
        }

        // The picture follows the window's size on screen, scaled by the resolution setting. False while the window
        // has no size yet.
        private bool make_textures()
        {
            Canvas canvas = image.canvas;
            float scale = (canvas != null ? canvas.scaleFactor : 1f) * pip_cam_settings.resolution;
            Rect rect = image.rectTransform.rect;
            int width = Mathf.Max(min_texture_size, Mathf.RoundToInt(rect.width * scale));
            int height = Mathf.Max(min_texture_size, Mathf.RoundToInt(rect.height * scale));
            if (rect.width <= 1f || rect.height <= 1f)
            {
                return false;
            }
            if (picture != null && picture.width == width && picture.height == height)
            {
                return true;
            }

            release_textures();
            picture = new RenderTexture(width, height, 24, RenderTextureFormat.Default);
            picture.Create();
            view.targetTexture = picture;
            image.texture = picture;
            if (mask_view != null)
            {
                // The same pair of textures the game's own mask camera draws into: color, and depth for the shaders.
                mask_color = new RenderTexture(width, height, 0, RenderTextureFormat.Default);
                mask_color.Create();
                mask_depth = new RenderTexture(width, height, 24, RenderTextureFormat.Depth);
                mask_depth.Create();
                mask_view.SetTargetBuffers(mask_color.colorBuffer, mask_depth.depthBuffer);
            }
            return true;
        }

        private void release_textures()
        {
            if (view != null)
            {
                view.targetTexture = null;
            }
            if (mask_view != null)
            {
                mask_view.targetTexture = null;
            }
            foreach (RenderTexture texture in new[] { picture, mask_color, mask_depth })
            {
                if (texture != null)
                {
                    texture.Release();
                    Destroy(texture);
                }
            }
            picture = null;
            mask_color = null;
            mask_depth = null;
        }

        // Looks at the spot from the game's angle, far enough back to show half_height above and below it.
        private void place()
        {
            // Gone from the park, demolished or put away: the view stays where they were last seen.
            if (!ReferenceEquals(target, null) && (target == null || !target.gameObject.activeInHierarchy))
            {
                stop_following();
            }
            Vector3 spot = center;
            if (target != null)
            {
                spot = follow_point(target);
                center = spot;
            }
            else if (ground_at(center, out float ground))
            {
                spot.y = ground;
            }

            Quaternion rotation = Quaternion.Euler(tilt, yaw, 0f);
            distance = half_height / Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad);
            view.transform.SetPositionAndRotation(spot - rotation * Vector3.forward * distance, rotation);
            view.fieldOfView = fov;
            view.nearClipPlane = Mathf.Max(0.05f, distance * 0.04f);
            view.farClipPlane = distance * 2f + 150f;
            GeometryUtility.CalculateFrustumPlanes(view, frustum);
            has_frustum = true;
        }

        private bool has_frustum;

        // Whether a person standing there is in the picture, as of the last one.
        public bool sees(Vector3 position)
        {
            return has_frustum && isActiveAndEnabled && GeometryUtility.TestPlanesAABB(frustum, person_bounds(position));
        }

        private static Bounds person_bounds(Vector3 position)
        {
            return new Bounds(position + Vector3.up * 0.4f, new Vector3(0.6f, 1f, 0.6f));
        }

        // A person where they are, and a coaster or boat ride at its first car or boat, which is what moves.
        private static Vector3 follow_point(SerializedMonoBehaviour followed)
        {
            switch (followed)
            {
                case TrackedRide tracked_ride when tracked_ride.Track != null:
                    foreach (Train train in tracked_ride.Track.trains)
                    {
                        if (train != null && train.cars != null && train.cars.Count > 0 && train.cars[0] != null)
                        {
                            return train.cars[0].transform.position;
                        }
                    }
                    break;
                case BoatRide boat_ride:
                    foreach (Boat boat in boat_ride.boats)
                    {
                        if (boat != null)
                        {
                            return boat.transform.position;
                        }
                    }
                    break;
            }
            return followed.transform.position;
        }

        // Clicking the title opens the window of whoever or whatever the view follows, as clicking them in the park does.
        private void open_target_window()
        {
            if (target != null && target.gameObject.activeInHierarchy)
            {
                target.onOpenInfoWindow();
            }
        }

        private void stop_following()
        {
            target = null;
            update_title();
        }

        private void update_title()
        {
            if (frame != null)
            {
                frame.setTitle(target != null ? texts.window_title + " – " + target.getName() : texts.window_title);
            }
        }

        private static bool ground_at(Vector3 position, out float ground)
        {
            if (Physics.Raycast(new Vector3(position.x, 2000f, position.z), Vector3.down, out RaycastHit hit, 4000f, ground_mask, QueryTriggerInteraction.Collide))
            {
                ground = hit.point.y;
                return true;
            }
            ground = 0f;
            return false;
        }

        private void draw_for_this_camera(GameController game)
        {
            MeshChunker chunks = MeshChunker.Instance;
            Light key_light = game.lightRig != null ? game.lightRig.keyLight : null;
            if (chunks != null && key_light != null)
            {
                chunks.render(view, key_light);
            }

            foreach (LightSequence light_sequence in game.park.getLightSequences())
            {
                if (light_sequence != null)
                {
                    light_sequence.draw(view);
                }
            }

            // People in the view are normally fully animated and drawn by the game (see people_boost). Past its limit,
            // the ones the main camera doesn't see would not be drawn at all, so they are drawn here, the simpler way
            // the game draws people far away.
            hidden_people.clear();
            foreach (Person person in game.park.getPeople())
            {
                if (person == null || person.isVisible || person.forceDisableRendering || !person.gameObject.activeInHierarchy)
                {
                    continue;
                }
                if (GeometryUtility.TestPlanesAABB(frustum, person_bounds(person.transform.position)))
                {
                    hidden_people.add(person, tryToUseMeshAnimation: true, forceAdd: true);
                }
            }
            hidden_people.draw();
        }

        // Only around this camera's own picture: its terrain mask, its clip matrix, and shadows reaching as far as it
        // looks, which the game otherwise sets for the main camera's zoom.
        private void on_pre_cull(Camera camera)
        {
            if (camera != view || swapped)
            {
                return;
            }
            swapped = true;
            hide_cloud_shadow();
            foreach (Renderer renderer in main_renderers)
            {
                if (renderer != null && renderer.enabled)
                {
                    renderer.enabled = false;
                    hidden_renderers.Add(renderer);
                }
            }
            saved_shadow_distance = QualitySettings.shadowDistance;
            QualitySettings.shadowDistance = Mathf.Max(saved_shadow_distance, distance * 1.5f + 50f);
            saved_clip_to_world = Shader.GetGlobalMatrix(clip_to_world_id);
            Shader.SetGlobalMatrix(clip_to_world_id, clip_to_world(view));
            mask_swapped = mask_depth != null;
            if (mask_swapped)
            {
                saved_mask = Shader.GetGlobalTexture(mask_id);
                saved_mask_color = Shader.GetGlobalTexture(mask_color_id);
                Shader.SetGlobalTexture(mask_id, mask_depth);
                Shader.SetGlobalTexture(mask_color_id, mask_color);
            }
        }

        private void on_post_render(Camera camera)
        {
            if (camera == view)
            {
                put_back();
            }
        }

        // Also run before the next picture, in case this camera's picture never finished, so nothing of the main
        // camera stays hidden.
        private void put_back()
        {
            if (!swapped)
            {
                return;
            }
            swapped = false;
            foreach (Renderer renderer in hidden_renderers)
            {
                if (renderer != null)
                {
                    renderer.enabled = true;
                }
            }
            hidden_renderers.Clear();
            QualitySettings.shadowDistance = saved_shadow_distance;
            Shader.SetGlobalMatrix(clip_to_world_id, saved_clip_to_world);
            if (mask_swapped)
            {
                mask_swapped = false;
                Shader.SetGlobalTexture(mask_id, saved_mask);
                Shader.SetGlobalTexture(mask_color_id, saved_mask_color);
                saved_mask = null;
                saved_mask_color = null;
            }
        }

        // The same matrix the game works out for its main camera every frame.
        private static Matrix4x4 clip_to_world(Camera camera)
        {
            Matrix4x4 projection = GL.GetGPUProjectionMatrix(camera.projectionMatrix, false);
            projection[3, 2] = 0f;
            projection[2, 3] = 0f;
            projection[3, 3] = 1f;
            return Matrix4x4.Inverse(projection * camera.worldToCameraMatrix) * Matrix4x4.TRS(new Vector3(0f, 0f, -projection[2, 2]), Quaternion.identity, Vector3.one);
        }

        // While the middle or right button drags in the view, the game's camera keeps still: the game's own camera
        // pans with the middle button even over windows.
        public void OnPointerDown(PointerEventData data)
        {
            if (data.button != PointerEventData.InputButton.Left && GameController.Instance != null)
            {
                GameController.Instance.pushCameraControlLock();
                camera_locks++;
            }
        }

        public void OnPointerUp(PointerEventData data)
        {
            if (data.button != PointerEventData.InputButton.Left && camera_locks > 0 && GameController.Instance != null)
            {
                GameController.Instance.popCameraControlLock();
                camera_locks--;
            }
        }

        private void release_camera_locks()
        {
            while (camera_locks > 0)
            {
                camera_locks--;
                GameController.Instance?.popCameraControlLock();
            }
        }

        public void OnDrag(PointerEventData data)
        {
            if (data.button == PointerEventData.InputButton.Middle)
            {
                // The ground under the cursor moves with it. Seen at an angle, the ground is stretched up the screen.
                float world_per_pixel = 2f * half_height / Mathf.Max(1f, image.rectTransform.rect.height * (image.canvas != null ? image.canvas.scaleFactor : 1f));
                Quaternion heading = Quaternion.Euler(0f, yaw, 0f);
                Vector3 right = heading * Vector3.right;
                Vector3 forward = heading * Vector3.forward;
                float stretch = 1f / Mathf.Max(0.2f, Mathf.Sin(tilt * Mathf.Deg2Rad));
                if (target != null)
                {
                    stop_following();
                }
                center -= (right * data.delta.x + forward * data.delta.y * stretch) * world_per_pixel;
            }
            else if (data.button == PointerEventData.InputButton.Right)
            {
                yaw += data.delta.x * turn_per_pixel;
            }
        }

        public void OnScroll(PointerEventData data)
        {
            float wheel = data.scrollDelta.y;
            if (wheel != 0f)
            {
                half_height = Mathf.Clamp(half_height * Mathf.Pow(1f - zoom_step, wheel), min_half_height, max_half_height);
            }
        }

        // A click takes the main camera to the spot clicked, the same way the game's own buttons move it.
        public void OnPointerClick(PointerEventData data)
        {
            if (data.button != PointerEventData.InputButton.Left || data.dragging || view == null)
            {
                return;
            }
            RectTransform rect = image.rectTransform;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, data.position, data.pressEventCamera, out Vector2 local))
            {
                return;
            }
            Vector2 viewport = Rect.PointToNormalized(rect.rect, local);
            if (!Physics.Raycast(view.ViewportPointToRay(new Vector3(viewport.x, viewport.y, 0f)), out RaycastHit hit, 5000f, ground_mask, QueryTriggerInteraction.Collide))
            {
                return;
            }
            CameraController controller = GameController.Instance != null ? GameController.Instance.cameraController : null;
            if (controller != null)
            {
                controller.panTo(hit.point);
            }
        }
    }
}
