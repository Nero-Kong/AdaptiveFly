// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

namespace WaveHarmonic.Crest
{
    /// <summary>
    /// Samples horizontal motion of water volume
    /// </summary>
    sealed class FlowQuery : QueryBaseSimple, IFlowProvider
    {
        public FlowQuery() : base(WaterRenderer.Instance.FlowLod) { }
        public FlowQuery(WaterRenderer water) : base(water.FlowLod) { }
        protected override int Kernel => 1;
    }

    sealed class FlowQueryPerCamera : QueryPerCameraSimple<FlowQuery>, IFlowProvider
    {
        public FlowQueryPerCamera(WaterRenderer water) : base(water) { }
    }
}
