// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

using UnityEditor;
using UnityEngine;

#if UNITY_EDITOR
using MonoBehaviour = WaveHarmonic.Crest.Internal.EditorBehaviour;
#else
using MonoBehaviour = UnityEngine.MonoBehaviour;
#endif

namespace WaveHarmonic.Crest.Internal
{
    /// <summary>
    /// Implements logic to smooth out Unity's wrinkles.
    /// </summary>
    public abstract partial class CustomBehaviour : MonoBehaviour
    {
        bool _AfterStart;

#pragma warning disable 114
        private protected virtual void Awake()
        {
#if UNITY_EDITOR
            base.Awake();
#endif
        }

        /// <summary>
        /// Unity's Start method. Make sure to call base if overriden.
        /// </summary>
        protected void Start()
        {
            _AfterStart = true;

#if UNITY_EDITOR
            base.Start();
            if (!enabled) return;
#endif

            OnStart();
        }
#pragma warning restore 114

        /// <summary>
        /// Called in OnEnable only after Start has ran.
        /// </summary>
        private protected virtual void Initialize()
        {

        }

        /// <summary>
        /// Replaces Start. Only called in the editor if passes validation.
        /// </summary>
        private protected virtual void OnStart()
        {
            Initialize();
        }

        /// <summary>
        /// Unity's OnEnable method. Make sure to call base if overriden.
        /// </summary>
        private protected virtual void OnEnable()
        {
            if (!_AfterStart) return;
            Initialize();
        }

#if UNITY_EDITOR
        [InitializeOnEnterPlayMode]
        static void OnEnterPlayModeInEditor(EnterPlayModeOptions options)
        {
            foreach (var @object in Helpers.FindObjectsByType<CustomBehaviour>(FindObjectsInactive.Include))
            {
                @object._AfterStart = false;
            }
        }
#endif
    }

    partial class CustomBehaviour : ISerializationCallbackReceiver
    {
#pragma warning disable 414
        [@SerializeField, @HideInInspector]
        private protected int _Version;
#pragma warning restore 414

        private protected virtual int Version => 0;

        private protected CustomBehaviour()
        {
            // Sets the default version. Overriden by serialized field above.
            _Version = Version;
        }

        private protected virtual void OnMigrate()
        {

        }

        void ISerializationCallbackReceiver.OnBeforeSerialize()
        {
            if (_Version < Version)
            {
                OnMigrate();
                _Version = Version;
            }
        }

        void ISerializationCallbackReceiver.OnAfterDeserialize()
        {

        }
    }
}
