using Gothic.Core.Extensions;
using Gothic.VR.Interaction.Models;
using Gothic.VR.Interaction.Ports;
using Reflex.Attributes;
using UnityEngine;

namespace Gothic.VR.Adapters.Player.Debugging
{
    public class VRHandVelocityDebugger : MonoBehaviour
    {
        [Inject] private readonly IVrInput _vrInput;

        [SerializeField] private GameObject _leftHandModel;
        [SerializeField] private GameObject _rightHandModel;
        [SerializeField] private Rigidbody _leftHandRigidbody;
        [SerializeField] private Rigidbody _rightHandRigidbody;


        private LineRenderer _lineRendererLeft;
        private LineRenderer _lineRendererRight1;
        private LineRenderer _lineRendererRight2;
        private LineRenderer _lineRendererRight3;
        private IVrRig _rig;

        private void Start()
        {
            _rig = GetComponent<IVrRig>();

            // Add a LineRenderer component
            _lineRendererLeft = _leftHandModel.AddComponent<LineRenderer>();
            _lineRendererRight1 = _rightHandModel.AddComponent<LineRenderer>();
            var goRight2 = new GameObject("LineRendererRight2");
            goRight2.SetParent(_rightHandModel);
            _lineRendererRight2 = goRight2.AddComponent<LineRenderer>();
            var goRight3 = new GameObject("LineRendererRight3");
            goRight3.SetParent(_rightHandModel);
            _lineRendererRight3 = goRight3.AddComponent<LineRenderer>();


            // Set the material
            _lineRendererLeft.material = new Material(Shader.Find("Sprites/Default"));
            _lineRendererRight1.material = new Material(Shader.Find("Sprites/Default"));
            _lineRendererRight2.material = new Material(Shader.Find("Sprites/Default"));
            _lineRendererRight3.material = new Material(Shader.Find("Sprites/Default"));

            // Set the width
            _lineRendererLeft.startWidth = 0.05f;
            _lineRendererLeft.endWidth = 0.05f;
            _lineRendererRight1.startWidth = 0.05f;
            _lineRendererRight1.endWidth = 0.05f;
            _lineRendererRight2.startWidth = 0.05f;
            _lineRendererRight2.endWidth = 0.05f;
            _lineRendererRight3.startWidth = 0.05f;
            _lineRendererRight3.endWidth = 0.05f;

            // Set the color
            _lineRendererLeft.startColor = Color.red;
            _lineRendererLeft.endColor = Color.red;

            _lineRendererRight1.startColor = Color.green;
            _lineRendererRight1.endColor = Color.green;
            _lineRendererRight2.startColor = Color.yellow;
            _lineRendererRight2.endColor = Color.yellow;
            _lineRendererRight3.startColor = Color.red;
            _lineRendererRight3.endColor = Color.red;
        }

        private void Update()
        {
            var rightPosition = _rightHandModel.transform.position;
            var rawVelocity = _vrInput.GetControllerVelocity(VrHandSide.Right);

            _lineRendererRight1.SetPosition(0, rightPosition);
            _lineRendererRight1.SetPosition(1, rightPosition + rawVelocity * 10);

            _lineRendererRight2.SetPosition(0, rightPosition);
            _lineRendererRight2.SetPosition(1, rightPosition + _rig.Root.TransformDirection(rawVelocity) * 10);

            _lineRendererRight3.SetPosition(0, rightPosition);
            _lineRendererRight3.SetPosition(1, rightPosition + _rig.TrackingSpace.TransformDirection(rawVelocity) * 10);
        }
    }
}
