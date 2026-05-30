using System.Collections;
using UnityEngine;

namespace GUZ.Core.Manager
{
    /// <summary>
    /// Singleton DI wrapper to access Unity MonoBehaviour functions.
    ///
    /// The thing is: We can use MonoBehaviour as starting point for DI, but we can not inject a MonoBehaviour
    /// (of course, we could simply say GameObject.Find() but let's stick with DI logic for this special case)
    /// </summary>
    public class UnityMonoService
    {
        private MonoBehaviour _instance;

        public void SetMonoBehaviour(MonoBehaviour monoBehaviour)
        {
            _instance = monoBehaviour;
        }

        public Coroutine StartCoroutine(IEnumerator routine)
        {
            return _instance.StartCoroutine(routine);
        }

        public void StopCoroutine(Coroutine obj)
        {
            _instance.StopCoroutine(obj);
        }
    }
}
