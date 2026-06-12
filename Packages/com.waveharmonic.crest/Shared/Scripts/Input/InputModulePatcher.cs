// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

#if d_UnityInputSystem && ENABLE_INPUT_SYSTEM
#define INPUT_SYSTEM_ENABLED
#endif

using UnityEngine;

namespace WaveHarmonic.Crest.Examples
{
#if !CREST_DEBUG
    [AddComponentMenu("")]
#endif
    sealed class InputModulePatcher : MonoBehaviour
    {
#if INPUT_SYSTEM_ENABLED
        void OnEnable()
        {
            GetComponent<UnityEngine.EventSystems.StandaloneInputModule>().enabled = false;
        }
#endif
    }
}
