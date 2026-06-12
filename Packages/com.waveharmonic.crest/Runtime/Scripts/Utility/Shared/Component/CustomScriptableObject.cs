// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

using UnityEngine;

namespace WaveHarmonic.Crest.Internal
{
    /// <summary>
    /// Implements logic to smooth out Unity's wrinkles.
    /// </summary>
    public abstract partial class CustomScriptableObject : ScriptableObject
    {

    }

    partial class CustomScriptableObject : ISerializationCallbackReceiver
    {
#pragma warning disable 414
        [@SerializeField, @HideInInspector]
        private protected int _Version;
#pragma warning restore 414

        private protected virtual int Version => 0;

        private protected CustomScriptableObject()
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
