using System;
using System.Numerics;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using UnityEngine.EventSystems;

public class MapZoom : MonoBehaviour
{
    public GameObject heightMap;
    public float scaleSpeed = 0.08f;
    public float minScaleFactor = 0.00001f;
    public float maxScaleFactor = 50f;

    private float currentScaleFactor = 1f;

    private UnityEngine.Vector3 oldHeightScale;
    private UnityEngine.Vector3 oldTileScale;
    
    void Start()
    {
        oldTileScale = transform.localScale;
        oldHeightScale = heightMap.transform.localScale;
    }

    void Update()
    {
        UnityEngine.Vector2 mouseScreen = Mouse.current.position.ReadValue();
        UnityEngine.Vector3 mousePosition = ScreenToWorld(mouseScreen);
        
        float scrollInput = Mouse.current.scroll.ReadValue().y;

        if (scrollInput != 0)
        {
            float oldScaleFactor = currentScaleFactor;
            float scaleChange = Mathf.Sign(scrollInput) * scaleSpeed;
            currentScaleFactor = Mathf.Clamp(currentScaleFactor + scaleChange, minScaleFactor, maxScaleFactor);

            transform.localScale = new UnityEngine.Vector3(
                oldTileScale.x * currentScaleFactor,
                oldTileScale.y * currentScaleFactor,
                oldTileScale.z);

            heightMap.transform.localScale = new UnityEngine.Vector3(
                oldHeightScale.x * currentScaleFactor,
                oldHeightScale.y * currentScaleFactor,
                oldHeightScale.z);
            // Move both maps so the zoom centers on the mouse
            float zoomRatio = currentScaleFactor / oldScaleFactor;
            ZoomAroundPoint(transform, mousePosition, zoomRatio);
            ZoomAroundPoint(heightMap.transform, mousePosition, zoomRatio);
        }
 
        // When right mouse is held and it is on the world grid, move both maps with mouse position
    if (Mouse.current.rightButton.isPressed && MouseOverMap(mousePosition))
        {
            // Where the mouse was last frame (current spot minus how far it moved)
            UnityEngine.Vector3 lastMousePosition = ScreenToWorld(mouseScreen - Mouse.current.delta.ReadValue());
 
            transform.position += mousePosition - lastMousePosition;
            heightMap.transform.position += mousePosition - lastMousePosition;
        }
    }

    bool MouseOverMap(UnityEngine.Vector2 mousePosition)
    {
        UnityEngine.Vector2 point = mousePosition;
        return Physics2D.OverlapPoint(point, LayerMask.GetMask("Map")) != null;
    }
    // Turns a spot on the screen into a position in the world
    UnityEngine.Vector3 ScreenToWorld(UnityEngine.Vector2 screenPoint)
    {
        UnityEngine.Vector3 point = screenPoint;
        point.z = Mathf.Abs(Camera.main.transform.position.z - transform.position.z);
        return Camera.main.ScreenToWorldPoint(point);
    }
 
    // Pushes an object toward or away from a point by the zoom amount
    void ZoomAroundPoint(Transform map, UnityEngine.Vector3 point, float zoomRatio)
    {
        UnityEngine.Vector3 offset = map.position - point;
        map.position = new UnityEngine.Vector3(
            point.x + offset.x * zoomRatio,
            point.y + offset.y * zoomRatio,
            map.position.z);
    }
}
