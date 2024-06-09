using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class DemoButtonController : MonoBehaviour
{
    [SerializeField] private Canvas parentCanvas;
    [SerializeField] private BottomSheetController bottomSheetPrefab;

    private BottomSheetController bottomSheetInstance;

    public void OnUserPressButton()
    {
        if (bottomSheetInstance != null)
        {
            Destroy(bottomSheetInstance.gameObject);
        }
        else
        {
            bottomSheetInstance = Instantiate(bottomSheetPrefab, parentCanvas.transform);
            bottomSheetInstance.parentCanvas = parentCanvas;
        }
    }
}
