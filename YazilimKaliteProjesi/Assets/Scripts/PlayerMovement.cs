using UnityEngine;

public class PlayerMovement : MonoBehaviour
{
    private Transform playerTransform;
    private GameInput gameInput;
    private Vector2 inputVector;
    private Vector3 moveDir;
    private bool isWalking = false;
    void Start()
    {
        playerTransform = GetComponent<Transform>();
        gameInput = FindAnyObjectByType<GameInput>();
        
    }
    // Update is called once per frame
    void Update()
    {
        Movement();
        PlayerWalkAnim();
    }

    private void Movement()
    {
        //inputVector = GameInput.Instance.GetMovementVector();
        inputVector = gameInput.GetMovementVector();
        //Vector3 moveDir = new Vector3(inputVector.x, 0f, inputVector.y);

        //
        moveDir = transform.right * inputVector.x +
          transform.forward * inputVector.y;
        moveDir.y = 0f;
        moveDir.Normalize();


        playerTransform.position += moveDir * Time.deltaTime * 3;

        playerTransform.forward = Vector3.Slerp(transform.forward, moveDir, Time.deltaTime);//player Rotation
        isWalking = moveDir != Vector3.zero;

    }
    private void PlayerWalkAnim()
    {
        if (isWalking)//wakling animations
        {
            PlayerAnimations.Instance.WalkAnim(true);
        }
        else
        {
            PlayerAnimations.Instance.WalkAnim(false);
        }
    }
}
