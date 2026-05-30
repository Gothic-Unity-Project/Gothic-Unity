using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.XR;

public class HandPresence : MonoBehaviour
{
    [FormerlySerializedAs("controllerCharacteristics")]
    public InputDeviceCharacteristics ControllerCharacteristics;

    [FormerlySerializedAs("handModelPrefab")]
    public GameObject HandModelPrefab;

    private InputDevice _targetDevice;
    private GameObject _spawnedHandModel;
    private Animator _handAnimator;


    // Start is called before the first frame update
    private void Start()
    {
        InvokeRepeating("InstanciateControllerDevice", 1f, 10f);
    }


    private void Update()
    {
        if (_spawnedHandModel)
        {
            UpdateHandAnimation();
        }
    }


    private void InstanciateControllerDevice()
    {
        // If we already instanciated a valid Controller pair, we can cancel the following checks.
        if (_targetDevice.isValid)
        {
            CancelInvoke("GetDevices");
            return;
        }

        var devices = new List<InputDevice>();
        InputDevices.GetDevicesWithCharacteristics(ControllerCharacteristics, devices);

        //foreach(var item in devices)
        //{
        //    GUZLogger.Log(item.name + item.characteristics);
        //}

        if (devices.Count > 0)
        {
            _spawnedHandModel = Instantiate(HandModelPrefab, transform);
            _handAnimator = _spawnedHandModel.GetComponent<Animator>();
        }
    }


    private void UpdateHandAnimation()
    {
        //targetDevice.TryGetFeatureValue(CommonUsages.primaryButton, out bool primarybuttonvalue);
        //if (primarybuttonvalue == true)
        //{
        //    spawnedHandModel.SetActive(false);
        //}

        //targetDevice.TryGetFeatureValue(CommonUsages.secondaryButton, out bool secondarybuttonvalue);
        //if (secondarybuttonvalue == true)
        //{
        //    spawnedHandModel.SetActive(true);
        //}

        if (_targetDevice.TryGetFeatureValue(CommonUsages.trigger, out var triggerValue))
        {
            _handAnimator.SetFloat("Trigger", triggerValue);
        }
        else
        {
            _handAnimator.SetFloat("Trigger", 0);
        }

        if (_targetDevice.TryGetFeatureValue(CommonUsages.grip, out var gripValue))
        {
            _handAnimator.SetFloat("Grip", gripValue);
        }
        else
        {
            _handAnimator.SetFloat("Grip", 0);
        }
    }
}
