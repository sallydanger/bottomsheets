using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.UIElements;

public class BottomSheetController : MonoBehaviour
{
    // What are we contained by?
    public Canvas parentCanvas;

    // "Detents" are the viable resting positions of the Sheet
    // https://developer.apple.com/documentation/uikit/uisheetpresentationcontroller/3801903-detents
    //
    // Here, they are the numbers we see in the UI when the menu is at it's lowest,
    // middles, and highest states.
    private const float DEFAULT_DETENT_RATIO_LOW = 0.4f;
    private const float DEFAULT_DETENT_RATIO_HIGH = 0.94f;
    private float[] detentRatios = new float[] { DEFAULT_DETENT_RATIO_LOW, DEFAULT_DETENT_RATIO_HIGH };
    // Used to bookmark which detent we are currently indexed to
    private int currDetentIdx = 0;

    // Touch tracking
    private bool isTouching = false;
    // Drag detection
    private bool didDrag = false;
    // Tap detection
    private float tapDuration = 0;
    private float maxTapDuration = 0.2f;

    // Variables needed to calculate if a swipe happened
    private float swipeToCloseSpeed = 5000;
    private float swipeToToggleSpeed = 500;
    private List<float> swipeCache = new List<float>();
    private Vector2 prevTouchPoint;

    // Anim speed: How many times can we travel the full height (1920) in one second?
    // In this case, five times.
    private float animPixelsPerSecond = 1920f * 3.5f;

    // The position the sheet should be to match movement with the touch input
    private Vector3 touchMatchmovePosition;

    // How much do we multiply the pixel resolution by in order to get it to
    // match the reference resolution? Which is HD, at the moment (1080x1920)
    private float hdScalar = 1f;

    // When they swipe it closed, destroy it
    private bool destroyOnAnimComplete = false;

    // UNITY FUNCTIONS
    //
    private void Awake()
    {
        // You don't need to do this, but otherwise it runs by 30 at default,
        // and the animation looks stuttery.
        Application.targetFrameRate = 90;
    }

    void Start()
    {
        // This is how the position values are calculated in Editor:
        CanvasScaler canvasScaler = parentCanvas.GetComponent<CanvasScaler>();
        Vector2 refRes = canvasScaler.referenceResolution;
        hdScalar = refRes.y / Screen.height;

        // Sanity-check in case detentRatios becomes serialized/public
        if (detentRatios.Length == 0)
        {
            detentRatios = new float[2] { DEFAULT_DETENT_RATIO_LOW, DEFAULT_DETENT_RATIO_HIGH };
        }
        Array.Sort(detentRatios);

        // Position
        transform.position = GetHiddenPosition();
        AnimateToDetentRatioWithIndex(0);
    }

    void Update()
    {
        if (isTouching)
        {
            tapDuration += Time.deltaTime;
            transform.position = Vector3.Lerp(transform.position, touchMatchmovePosition, Time.deltaTime * 20f);
        }
    }


    // PUBLIC FUNCTIONS
    //
    public void OnPointerDown(BaseEventData baseEventData)
    {
        // Initialize tracking variables to detect single-taps
        isTouching = true;
        didDrag = false;
        tapDuration = 0;
        // Initialize matchmove touch state
        touchMatchmovePosition = transform.position;
        // And prepare to track swipes!
        swipeCache.Clear();
        prevTouchPoint = ((PointerEventData)baseEventData).position;
    }

    public void OnPointerUp(BaseEventData baseEventData)
    {
        isTouching = false;

        if((!didDrag) && (tapDuration <= maxTapDuration))
        {
            // I thought we could use the PointerClick callback for this, but
            // it doesn't seem to fire. Must be for some other kind of action!
            OnTap();
        }
    }

    public void OnDrag(BaseEventData baseEventData)
    {
        // Cast the base event data to get the information we care about
        PointerEventData pointerEventData = (PointerEventData)baseEventData;

        // Touch point is a screen Point in pixel coordinates touched by the user
        Vector2 touchPoint = pointerEventData.position;

        // Move the menu along with the touch point
        MatchmoveMenuWithTouch(touchPoint);

        // Listen for swiping events
        TrackSwipeEvents(touchPoint);

        // Set this to true cuz we did it this frame
        didDrag = true;
    }

    // Btw, "Drop" as in the end state of "Drag" doesn't seem to fire, but
    // "EndDrag" does. So Drop must be fore something else!
    public void OnEndDrag(BaseEventData baseEventData)
    {
        // What's the average vertical pixel distance you traveled in a frame for the last 10 frames?
        float totalDeltaPixels = 0f;
        for (int i = 0; i < swipeCache.Count; i++)
        {
            totalDeltaPixels += swipeCache[i];
        }
        float averageDeltaPixels = totalDeltaPixels / swipeCache.Count;

        // What's the speed of that distance?
        float averageDeltaTime = Time.deltaTime;
        float deltaPixelsPerSecond = averageDeltaPixels / averageDeltaTime;

        if (Mathf.Abs(deltaPixelsPerSecond) >= swipeToToggleSpeed)
        {
            // Swipe up
            if (deltaPixelsPerSecond > 0)
            {
                // Swipe ALL the way up
                if (Mathf.Abs(deltaPixelsPerSecond) >= swipeToCloseSpeed)
                {
                    AnimateToDetentRatioWithIndex(detentRatios.Length - 1);
                }
                // Just swipe to the next detent
                else
                {
                    AnimateToDetentRatioWithIndex(currDetentIdx + 1);
                }
            }
            // Swipe down 
            else
            {
                // Swipe CLOSED
                if(Mathf.Abs(deltaPixelsPerSecond) >= swipeToCloseSpeed)
                {
                    OnUserClosedSheet();
                }
                // Just swipe to the next detent
                else
                {
                    AnimateToDetentRatioWithIndex(currDetentIdx - 1);
                }
            }
        }
        else
        {
            AnimateToNearestDetent();
        }
    }


    // PRIVATE FUNCTIONS
    //
    private Vector3 GetHiddenPosition()
    {
        Vector3 hiddenPosition = transform.position;
        hiddenPosition.y = 0;
        return hiddenPosition;
    }

    private void MatchmoveMenuWithTouch(Vector2 touchPoint)
    {
        // Rect point is the in-rectangle coordinates of the touchPoint
        Vector2 rectPoint;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            (RectTransform)parentCanvas.transform,
            touchPoint,
            parentCanvas.worldCamera,
            out rectPoint);

        // From the rect point, we can get the coordinates that speak to this Rect Transform
        // Ignore anything horizontal (or depth, which I don't think is possible!)
        touchMatchmovePosition = transform.position;
        touchMatchmovePosition.y = parentCanvas.transform.TransformPoint(rectPoint).y;
    }

    private void TrackSwipeEvents(Vector2 currTouchPoint)
    {
        // We are dragging our finger across the screen
        Vector2 deltaPixels = currTouchPoint - prevTouchPoint;

        // Track the speed so that we can ramp down
        swipeCache.Add(deltaPixels.y);

        // Track this to continue calculating deltas
        prevTouchPoint = currTouchPoint;
    }

    private void OnTap()
    {
        // Pop to the next detent and wrap around if needed
        currDetentIdx = (currDetentIdx + 1) % detentRatios.Length;
        AnimateToDetentRatio(detentRatios[currDetentIdx]);
    }

    private void AnimateToNearestDetent()
    {
        // Store your next position index
        int closestDetentRatioIdx = GetNearestDetentRatioIdx();
        
        // Animate to your next position
        AnimateToDetentRatioWithIndex(closestDetentRatioIdx);
    }

    private int GetNearestDetentRatioIdx()
    {
        // Animate to the nearest detent from this position
        float currDetentRatio = transform.position.y / Screen.height;

        // The distance to beat
        int closestDetentRatioIdx = 0;
        float minDist = Mathf.Abs(detentRatios[closestDetentRatioIdx] - currDetentRatio);

        // Get anyone closer than this distance
        for (int i = 1; i < detentRatios.Length; i++)
        {
            float nextDist = Mathf.Abs(detentRatios[i] - currDetentRatio);

            if (nextDist < minDist)
            {
                closestDetentRatioIdx = i;
                minDist = nextDist;
            }
        }

        return closestDetentRatioIdx;
    }

    private void OnUserClosedSheet()
    {
        AnimateToPosition(GetHiddenPosition());
        destroyOnAnimComplete = true;
    }

    private void AnimateToDetentRatioWithIndex(int detentIdx)
    {
        // Did you just swipe shut?
        if (detentIdx < 0)
        {
            detentIdx = 0;
            OnUserClosedSheet();
            return;
        }

        // Sanity-check, did you swipe up beyond extents?
        // Then have nothing >:)
        detentIdx = Mathf.Min(detentIdx, detentRatios.Length-1);

        float targetDetentRatio = detentRatios[detentIdx];
        AnimateToDetentRatio(targetDetentRatio);

        currDetentIdx = detentIdx;
    }

    private void AnimateToDetentRatio(float targetDetentRatio)
    {
        Vector3 targetPosition = transform.position;
        targetPosition.y = targetDetentRatio * Screen.height;
        AnimateToPosition(targetPosition);
    }

    private void AnimateToPosition(Vector3 targetPosition)
    {
        StartCoroutine(AnimateToPosition_helper(targetPosition));
    }

    private IEnumerator AnimateToPosition_helper(Vector3 targetPosition)
    {
        float currTime = 0f;
        Vector3 startPosition = transform.position;

        // The animation time is variable depending on how far we have to travel.
        // Because we want to maintain a constant animation speed no matter the distance!
        float totalDistance = Vector3.Distance(startPosition, targetPosition);
        float scaledDistance = totalDistance * hdScalar;
        float totalTime = scaledDistance / animPixelsPerSecond;

        while (currTime < totalTime)
        {
            currTime += Time.deltaTime;
            transform.position = Vector3.Lerp(startPosition, targetPosition, currTime/ totalTime);
            yield return null;
        }

        transform.position = targetPosition;

        // Did you just close the menu?
        if (destroyOnAnimComplete)
        {
            Destroy(gameObject);
        }
    }

}
