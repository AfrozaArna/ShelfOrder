using System.Collections;
using UnityEngine;

public class VisitorClick : MonoBehaviour
{
    private QueueManager queueManager;
    private Coroutine hoverRoutine;
    private Vector3 restPosition;
    private bool restCaptured;

    void Start()
    {
        queueManager = FindFirstObjectByType<QueueManager>();
    }

    void OnMouseDown()
    {
        if (queueManager == null) return;

        GameObject frontVisitor = queueManager.GetFrontVisitor();
        if (frontVisitor == null) return;

        if (frontVisitor == this.gameObject)
        {
            queueManager.ServeVisitor();
        }
        else
        {
            queueManager.NotifyWrongVisitor(gameObject);
        }
    }

    // Every visitor's collider stays enabled (see QueueManager.UpdateVisitorColliders) so
    // wrong-visitor clicks can register, so the hover-lift needs its own explicit check to
    // stay limited to the visitor the player can actually serve.
    void OnMouseEnter()
    {
        if (queueManager == null || queueManager.GetFrontVisitor() != gameObject) return;

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
