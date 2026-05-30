using UnityEngine;
using UnityEngine.InputSystem;

namespace GUZ.Flat.Components
{
    public class FlatPlayerMovement : MonoBehaviour
    {
        [SerializeField] private Rigidbody _rigidbody;
        
        private const float _moveSpeed = 5f;
        private const float _turnSpeed = 2.5f;
        private const float _jumpHeight = 5f;

        private const float _maxGroundedVelocity = 0.001f;
        
        private float playerSpeed = 2.0f;
        private float jumpHeight = 1.0f;
        private float gravityValue = -10f;

        
        void Update()
        {
            ExecuteWalk();
            ExecuteRotation();
            ExecuteJump();
        }

        private void ExecuteWalk()
        {
            if (IsPressed(Key.W) || IsPressed(Key.UpArrow))
                transform.Translate(Vector3.forward * Time.deltaTime * _moveSpeed);
            if (IsPressed(Key.S) || IsPressed(Key.DownArrow))
                transform.Translate(-1 * Vector3.forward * Time.deltaTime * _moveSpeed);
        }

        private void ExecuteRotation()
        {
            if (IsPressed(Key.A) || IsPressed(Key.LeftArrow))
                transform.Rotate(0, -_turnSpeed, 0);
            if (IsPressed(Key.D) || IsPressed(Key.RightArrow))
                transform.Rotate(0, _turnSpeed, 0);
        }
        
        private void ExecuteJump()
        {
            if (IsPressed(Key.Space) && Mathf.Abs(_rigidbody.linearVelocity.y) < _maxGroundedVelocity)
            {
                _rigidbody.linearVelocity += Vector3.up * _jumpHeight;
            }
        }
        
        private bool IsPressed(Key key)
        {
            return Keyboard.current[key].isPressed;
        }
    }
}
