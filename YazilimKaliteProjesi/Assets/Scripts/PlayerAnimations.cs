using UnityEngine;

public class PlayerAnimations : MonoBehaviour
{
    public static PlayerAnimations Instance;
    private Animator playerAnimator;
    void Start()
    {
        Instance = this;
        playerAnimator = GetComponent<Animator>();
    }
    //ahmet
    public void WalkAnim(bool b)
    {
        playerAnimator.SetBool("Walk", b);
    }
}
