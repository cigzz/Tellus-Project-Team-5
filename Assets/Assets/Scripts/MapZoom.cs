using System;
using System.Numerics;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

public class MapZoom : MonoBehaviour
{
    public GameObject heightMap;
    public float scaleSpeed = 0.04f;
    public float minScaleFactor = 0.5f;
    public float maxScaleFactor = 50f;

    private float currentScaleFactor = 1f;

    private UnityEngine.Vector3 oldHeightScale;
    private UnityEngine.Vector3 oldTileScale;
    
    void Start()
    {
        oldTileScale = transform.localScale;
        oldHeightScale = heightMap.transform.localScale;
    }

    // Update is called once per frame
    void Update()
    {
        float scrollInput = Mouse.current.scroll.ReadValue().y;

        if (scrollInput != 0)
        {
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
        }
    }
}
