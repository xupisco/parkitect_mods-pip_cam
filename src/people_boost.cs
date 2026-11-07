using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace pip_cam
{
    // The game animates people by how they look from its main camera: far away they get a simpler pre-recorded
    // animation, and off screen their animation only moves on every couple of seconds. Seen close up in a view, that
    // turns into stutter. People inside a view are told they are near and visible, so the game animates them fully,
    // and get their real state back from the game's own visibility tracking once no view shows them.
    internal static class people_boost
    {
        // Full animation costs more, so only this many people get it at once. The rest are drawn the simpler way.
        private const int max_people = 250;

        private const BindingFlags private_instance = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly FieldInfo listener_field = typeof(Person).GetField("cullingChangeListener", private_instance);
        private static readonly MethodInfo visibility_method = typeof(Person).GetMethod("onVisibilityChanged", private_instance, null, new[] { typeof(bool) }, null);
        private static readonly MethodInfo band_method = typeof(Person).GetMethod("onVisibilityDistanceBandIndexChanged", private_instance, null, new[] { typeof(int) }, null);

        private static bool usable => listener_field != null && visibility_method != null && band_method != null;

        // Each boosted person, with what preventUseBakedAnimations was before.
        private static readonly Dictionary<Person, bool> boosted = new Dictionary<Person, bool>();
        private static readonly HashSet<Person> wanted = new HashSet<Person>();
        private static readonly List<Person> leaving = new List<Person>();
        private static readonly object[] visible_argument = { true };
        private static readonly object[] near_argument = { 0 };

        public static void update(Park park, List<pip_view> views)
        {
            if (!usable)
            {
                return;
            }

            wanted.Clear();
            if (park != null && views.Count > 0)
            {
                foreach (Person person in park.getPeople())
                {
                    if (wanted.Count >= max_people)
                    {
                        break;
                    }
                    if (person == null || person.forceDisableRendering || !person.gameObject.activeInHierarchy)
                    {
                        continue;
                    }
                    Vector3 position = person.transform.position;
                    foreach (pip_view view in views)
                    {
                        if (view != null && view.sees(position))
                        {
                            wanted.Add(person);
                            break;
                        }
                    }
                }
            }

            // Again every frame, since the game sends a person their state whenever it changes from its side.
            foreach (Person person in wanted)
            {
                if (!boosted.ContainsKey(person))
                {
                    boosted[person] = person.preventUseBakedAnimations;
                }
                person.preventUseBakedAnimations = true;
                if (!person.isVisible)
                {
                    visibility_method.Invoke(person, visible_argument);
                }
                if (person.distanceBandIndex != 0)
                {
                    band_method.Invoke(person, near_argument);
                }
            }

            leaving.Clear();
            foreach (Person person in boosted.Keys)
            {
                if (!wanted.Contains(person))
                {
                    leaving.Add(person);
                }
            }
            foreach (Person person in leaving)
            {
                restore(person, boosted[person]);
                boosted.Remove(person);
            }
        }

        public static void restore_all()
        {
            foreach (KeyValuePair<Person, bool> entry in boosted)
            {
                restore(entry.Key, entry.Value);
            }
            boosted.Clear();
            wanted.Clear();
        }

        // The game's tracking keeps the person's real visibility and distance, and sends them again.
        private static void restore(Person person, bool prevent_baked)
        {
            if (person == null)
            {
                return;
            }
            person.preventUseBakedAnimations = prevent_baked;
            if (listener_field.GetValue(person) is CullingChangeListener listener)
            {
                listener.triggerVisibilityChanged();
                listener.triggerDistanceBandIndexChanged();
            }
        }
    }
}
