using UnityEngine;
using UnityEngine.EventSystems;

namespace pip_cam
{
    // Sits on a view window's title bar, which the game uses only for dragging the window. A click without a drag
    // calls back.
    internal sealed class title_click : MonoBehaviour, IPointerClickHandler
    {
        public System.Action on_click;

        public void OnPointerClick(PointerEventData data)
        {
            if (data.button == PointerEventData.InputButton.Left && !data.dragging)
            {
                on_click?.Invoke();
            }
        }
    }
}
