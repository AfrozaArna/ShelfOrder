using System.Collections;
using UnityEngine;

public class BookClick : MonoBehaviour
{
    private StackManager stackManager;
    private Coroutine hoverRoutine;
    private Vector3 restPosition;
    private bool restCaptured;

    void Start()
    {
        stackManager = FindFirstObjectByType<StackManager>();
    }

    void OnMouseDown()
    {
        if (stackManager == null) return;

        GameObject topBook = stackManager.GetTopBook();

        if (topBook == null) return;

        if (topBook == this.gameObject)
        {
            stackManager.RemoveTopBook();
        }
        else
        {
            stackManager.NotifyWrongBook(gameObject);
        }
    }

    // Every book's collider stays enabled now (so wrong-book clicks can register at
    // all - see StackManager.UpdateBookColliders), so the hover-lift needs its own
    // explicit check to stay limited to the book the player can actually remove.
    void OnMouseEnter()
    {
        if (stackManager == null || stackManager.GetTopBook() != gameObject) return;

        if (!restCaptured)
        {
            restPosition = transform.position;
            restCaptured = true;
        }
        AnimateTo(restPosition + new Vector3(0f, 0.08f, 0f));
    }

    void OnMouseExit()
    {
        if (!restCaptured) return;
        AnimateTo(restPosition);
    }

    private void AnimateTo(Vector3 target)
    {
        if (hoverRoutine != null) StopCoroutine(hoverRoutine);
        hoverRoutine = StartCoroutine(AnimatePosition(target, 0.12f));
    }

    private IEnumerator AnimatePosition(Vector3 target, float duration)
    {
        Vector3 start = transform.position;
        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            transform.position = Vector3.Lerp(start, target, Mathf.Clamp01(t / duration));
            yield return null;
        }
        transform.position = target;
    }
}
