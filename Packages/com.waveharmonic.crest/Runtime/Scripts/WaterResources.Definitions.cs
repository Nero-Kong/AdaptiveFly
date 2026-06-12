// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace WaveHarmonic.Crest
{
    partial class WaterResources
    {
        internal ComputeLibrary _ComputeLibrary;

        public sealed class ComputeLibrary
        {
            public BlitCompute _BlitCompute;
            public BlurCompute _BlurCompute;
            public ClearCompute _ClearCompute;
            public ShapeCombineCompute _ShapeCombineCompute;
            public GerstnerCompute _GerstnerCompute;
            public ComputeLibrary(WaterResources resources)
            {
                _BlitCompute = new(resources.Compute._Blit);
                _BlurCompute = new(resources.Compute._Blur);
                _ClearCompute = new(resources.Compute._Clear);
                _ShapeCombineCompute = new(resources.Compute._ShapeCombine);
                _GerstnerCompute = new(resources.Compute._Gerstner);
            }
        }

        public abstract class UtilityCompute
        {
            public readonly ComputeShader _Shader;
            public readonly LocalKeyword _Float1Keyword;
            public readonly LocalKeyword _Float2Keyword;
            public readonly LocalKeyword _Float3Keyword;
            public readonly LocalKeyword _Float4Keyword;

            public UtilityCompute(ComputeShader shader)
            {
                _Shader = shader;

                var keywords = shader.keywordSpace;
                _Float1Keyword = keywords.FindKeyword("d_Float1");
                _Float2Keyword = keywords.FindKeyword("d_Float2");
                _Float3Keyword = keywords.FindKeyword("d_Float3");
                _Float4Keyword = keywords.FindKeyword("d_Float4");
            }

            public void SetVariantForFormat<T>(T wrapper, GraphicsFormat format) where T : IPropertyWrapperVariants
            {
                var count = GraphicsFormatUtility.GetComponentCount(format);
                wrapper.SetKeyword(_Float1Keyword, count == 1);
                wrapper.SetKeyword(_Float2Keyword, count == 2);
                wrapper.SetKeyword(_Float3Keyword, count == 3);
                wrapper.SetKeyword(_Float4Keyword, count == 4);
            }
        }

        public sealed class ClearCompute : UtilityCompute
        {
            public readonly int _KernelClearTarget;
            public readonly int _KernelClearTargetBoundaryX;
            public readonly int _KernelClearTargetBoundaryY;

            public ClearCompute(ComputeShader shader) : base(shader)
            {
                // Using FindKernel can fail if upgrading Crest, and is quite tricky to get right.
                _KernelClearTarget = 0;
                _KernelClearTargetBoundaryX = 1;
                _KernelClearTargetBoundaryY = 2;
            }
        }

        public sealed class BlitCompute : UtilityCompute
        {
            public readonly int _KernelAdd;

            public BlitCompute(ComputeShader shader) : base(shader)
            {
                _KernelAdd = 0;
            }
        }

        public sealed class BlurCompute : UtilityCompute
        {
            public readonly int _KernelHorizontal;
            public readonly int _KernelVertical;

            public BlurCompute(ComputeShader shader) : base(shader)
            {
                _KernelHorizontal = 0;
                _KernelVertical = 1;
            }
        }

        public sealed class ShapeCombineCompute
        {
            public readonly ComputeShader _Shader;
            public readonly LocalKeyword _CombineKeyword;
            public readonly LocalKeyword _DynamicWavesKeyword;
            public readonly int _CopyAnimatedWavesKernel;
            public readonly int _CombineAnimatedWavesKernel;
            public readonly int _CombineDynamicWavesKernel;

            public ShapeCombineCompute(ComputeShader shader)
            {
                _Shader = shader;

                var keywords = shader.keywordSpace;
                _CombineKeyword = keywords.FindKeyword("d_Combine");
                _DynamicWavesKeyword = keywords.FindKeyword("d_DynamicWaves");

                _CombineAnimatedWavesKernel = 0;
                _CopyAnimatedWavesKernel = 1;
                _CombineDynamicWavesKernel = 2;
            }
        }

        public sealed class GerstnerCompute
        {
            public readonly ComputeShader _Shader;
            public readonly LocalKeyword _WavePairsKeyword;
            public readonly int _ExecuteKernel;

            public GerstnerCompute(ComputeShader shader)
            {
                _Shader = shader;

                var keywords = shader.keywordSpace;
                _WavePairsKeyword = keywords.FindKeyword("d_WavePairs");

                _ExecuteKernel = 0;
            }
        }
    }
}
