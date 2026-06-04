using UnityEngine;

namespace Gothic.Core.Adapters.Properties
{
    public abstract class AbstractProperties : MonoBehaviour
    {
        public GameObject Go => gameObject;


        public abstract string GetFocusName();
    }
}
