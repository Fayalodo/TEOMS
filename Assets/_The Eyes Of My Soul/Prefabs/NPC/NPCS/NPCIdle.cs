using UnityEngine;

public class NPCIdle : MonoBehaviour
{
    Animator animator;
    float timer;
    float interval = 10f;

    void Start()
    {
        animator = GetComponent<Animator>();
    }

    void Update()
    {
        timer += Time.deltaTime;
        if (timer >= interval)
        {
            timer = 0;
            int random = Random.Range(2, 5); // 2-4 (не 1 чтобы не повторять Idle1)
            animator.SetInteger("IdleState", random);
            StartCoroutine(ResetIdle());
        }
    }

    System.Collections.IEnumerator ResetIdle()
    {
        yield return new WaitForSeconds(2f); // подожди пока анимация закончится
        animator.SetInteger("IdleState", 1); // вернись на Idle1
    }
}