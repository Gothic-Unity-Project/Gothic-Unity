using UnityEngine;

namespace GUZ.Core.Adapters.Properties
{
    public abstract class AbstractProperties : MonoBehaviour
    {
        public GameObject Go => gameObject;


        public abstract string GetFocusName();
    }
}
