using Gothic.Core.Services.Audio;
using Reflex.Attributes;
using UnityEngine;

namespace Gothic.Core.Adapters.Audio
{
    /// <summary>
    /// Provides the 2D AudioSource for non-positional script sounds (AudioService.PlaySound2D).
    /// </summary>
    public class ScriptSound : MonoBehaviour
    {
        [SerializeField] private AudioSource _audioSource;

        [Inject] private readonly AudioService _audioService;

        private void Start()
        {
            _audioService.SetScriptSoundSource(_audioSource);
        }
    }
}
