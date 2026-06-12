using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

/// <summary>
/// 接 UdpVideoStreamReceiver 的 dual-fisheye 输出, 通过 ComputeShader 实时拼成
/// equirectangular RenderTexture, 然后赋给球面 material 的 _BaseColorMap.
///
/// 不修改 UdpVideoStreamReceiver, 通过把它的 targetMaterial 接管来介入数据流:
///   1. Awake 时创建一个 HDRP/Unlit 的"源"material, 让 receiver 把
///      原始 dual-fisheye 纹理写到这个 material 的 _BaseColorMap.
///   2. 每帧 Update 时把这个纹理喂给 ComputeShader, 输出 equirect RT.
///   3. 同时创建一个 HDRP/Unlit 的"球面"material, 把 equirect RT 赋给它,
///      并把它装到 sphereRenderer 上.
///
/// AdaptiveFly 后续要做"视角解耦"时, 把 equirect RT 当全景纹理采样即可,
/// 渲染端逻辑跟传统 360 全景完全兼容.
/// </summary>
[AddComponentMenu("VR Locomotion/Dual Fisheye Stitcher")]
[RequireComponent(typeof(UdpVideoStreamReceiver))]
[DefaultExecutionOrder(-100)]   // 比 UdpVideoStreamReceiver 早 Awake, 提前接管 targetMaterial 等字段
public class DualFisheyeStitcher : MonoBehaviour
{
    [Header("Wiring")]
    [Tooltip("UDP video receiver providing dual-fisheye frames. 自动从同一 GameObject 获取.")]
    public UdpVideoStreamReceiver receiver;

    [Tooltip("将显示拼接后 equirect 的球面 renderer. 通常是 inside-out sphere.")]
    public Renderer sphereRenderer;

    [Header("Compute")]
    [Tooltip("DualFisheyeStitch.compute (kernel: DualFisheyeToEquirect)")]
    public ComputeShader computeShader;

    [Header("Output Resolution")]
    [Range(512, 8192)]
    public int equirectWidth = 2048;
    [Range(256, 4096)]
    public int equirectHeight = 1024;

    [Header("Lens Geometry")]
    [Tooltip("每个鱼眼 FOV (deg). Insta360 X5 标称 200°.")]
    [Range(120f, 240f)]
    public float fovDeg = 200f;

    [Tooltip("后镜头是否水平翻转 (X5: 是). 出现左右翻转再切.")]
    public bool backFlipX = true;

    [Header("IMU Stabilization")]
    [Tooltip("Receive X5 IMU roll/pitch correction from the same GameObject. Auto-added at runtime if missing.")]
    public X5ImuStabilizer imuStabilizer;

    [Tooltip("Compensate camera roll/pitch before sampling the dual-fisheye texture. Yaw is only lightly damped by X5ImuStabilizer.")]
    public bool enableImuStabilization = true;

    [Tooltip("Create X5ImuStabilizer automatically on this GameObject if no reference is assigned.")]
    public bool autoAddImuStabilizer = true;

    [Header("Debug")]
    [Tooltip("把第一帧到达 / 拼接结果概览打到 Console.")]
    public bool verboseLogging = true;

    [Tooltip("在 Game 视图右下角画一个 equirect 输出预览, 方便确认 ComputeShader 在出货.")]
    public bool drawEquirectOverlay = true;

    [Tooltip("如果第一帧老不来, 球面先填一个纯色, 证明球面渲染本身没问题.")]
    public Color noSignalDebugColor = new Color(0.3f, 0.0f, 0.0f, 1f);

    // --- runtime ---
    private Material _sourceMaterial;
    private Material _sphereMaterial;
    private RenderTexture _equirectRT;
    private int _kernel = -1;
    private bool _firstFrameLogged;
    private int _framesWithoutSource;
    private int _sourceTexturePropertyId;
    private string _sourceTexturePropertyName = "_UnlitColorMap";

    private static readonly int _SrcId       = Shader.PropertyToID("_SrcDualFisheye");
    private static readonly int _DstId       = Shader.PropertyToID("_DstEquirect");
    private static readonly int _SrcSizeId   = Shader.PropertyToID("_SrcSize");
    private static readonly int _DstSizeId   = Shader.PropertyToID("_DstSize");
    private static readonly int _FovDegId    = Shader.PropertyToID("_FovDeg");
    private static readonly int _BackFlipXId = Shader.PropertyToID("_BackFlipX");
    private static readonly int _EnableImuStabilizationId = Shader.PropertyToID("_EnableImuStabilization");
    private static readonly int _StabilizePitchDegId = Shader.PropertyToID("_StabilizePitchDeg");
    private static readonly int _StabilizeYawDegId = Shader.PropertyToID("_StabilizeYawDeg");
    private static readonly int _StabilizeRollDegId = Shader.PropertyToID("_StabilizeRollDeg");
    private static readonly int _BaseColorMapId  = Shader.PropertyToID("_BaseColorMap");
    private static readonly int _BaseColorId     = Shader.PropertyToID("_BaseColor");
    private static readonly int _UnlitColorMapId = Shader.PropertyToID("_UnlitColorMap");
    private static readonly int _UnlitColorId    = Shader.PropertyToID("_UnlitColor");
    private static readonly int _MainTexId       = Shader.PropertyToID("_MainTex");

    private void Reset()
    {
        receiver = GetComponent<UdpVideoStreamReceiver>();
    }

    private void Awake()
    {
        if (receiver == null) receiver = GetComponent<UdpVideoStreamReceiver>();
        if (imuStabilizer == null)
        {
            imuStabilizer = GetComponent<X5ImuStabilizer>();
            if (imuStabilizer == null && autoAddImuStabilizer)
            {
                imuStabilizer = gameObject.AddComponent<X5ImuStabilizer>();
            }
        }

        if (receiver == null)
        {
            Debug.LogError("[DualFisheyeStitcher] No UdpVideoStreamReceiver on this GameObject.");
            enabled = false;
            return;
        }

        Shader hdrpUnlit = Shader.Find("HDRP/Unlit");
        if (hdrpUnlit == null)
        {
            Debug.LogError("[DualFisheyeStitcher] HDRP/Unlit shader not found. Is HDRP installed?");
            enabled = false;
            return;
        }

        // --- 源 material: 装 dual-fisheye 纹理, 不显示 ---
        _sourceMaterial = new Material(hdrpUnlit) { name = "DualFisheyeSrc_Runtime" };
        HDMaterial.ValidateMaterial(_sourceMaterial);
        _sourceTexturePropertyName = ResolveTexturePropertyName(_sourceMaterial);
        _sourceTexturePropertyId = Shader.PropertyToID(_sourceTexturePropertyName);

        // --- 球面 material: 显示 equirect ---
        if (sphereRenderer != null)
        {
            _sphereMaterial = new Material(hdrpUnlit) { name = "DualFisheyeSphere_Runtime" };

            // HDRP 关键: 让球面双面渲染, 从内壁也能看到. 不再依赖 mesh 翻转 winding.
            _sphereMaterial.SetFloat("_DoubleSidedEnable", 1f);
            _sphereMaterial.SetInt("_CullMode", (int)UnityEngine.Rendering.CullMode.Off);
            _sphereMaterial.SetInt("_CullModeForward", (int)UnityEngine.Rendering.CullMode.Off);
            // 起点是纯色, 后面 AllocateRT 会把 equirect RT 绑到 _BaseColorMap + _UnlitColorMap
            SetColorIfPresent(_sphereMaterial, _BaseColorId, noSignalDebugColor);
            SetColorIfPresent(_sphereMaterial, _UnlitColorId, noSignalDebugColor);
            HDMaterial.ValidateMaterial(_sphereMaterial);

            sphereRenderer.sharedMaterial = _sphereMaterial;
            if (verboseLogging)
                Debug.Log($"[DualFisheyeStitcher] 球面 material 已挂到 {sphereRenderer.name}, " +
                          $"DoubleSided=ON, 初始颜色={noSignalDebugColor}");
        }
        else
        {
            Debug.LogWarning("[DualFisheyeStitcher] sphereRenderer 未设置, 拼接结果将不会显示.");
        }

        // --- 接管 receiver: 让它写到我们的 source material, 而不是自动建球面 ---
        receiver.targetMaterial = _sourceMaterial;
        receiver.targetTextureProperty = _sourceTexturePropertyName;
        receiver.targetRenderer = null;
        receiver.createRuntimePlaybackMaterial = false;
        receiver.autoFindOrCreateProjectionSphere = false;

        AllocateRT();

        // 兜底加载 ComputeShader (SceneBuilder 没填 / 用户手动建场景没拖)
        if (computeShader == null)
        {
#if UNITY_EDITOR
            string[] guids = UnityEditor.AssetDatabase.FindAssets("DualFisheyeStitch t:ComputeShader");
            if (guids != null && guids.Length > 0)
            {
                string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guids[0]);
                computeShader = UnityEditor.AssetDatabase.LoadAssetAtPath<ComputeShader>(path);
                if (computeShader != null)
                    Debug.Log($"[DualFisheyeStitcher] computeShader 字段空, 自动从 {path} 加载.");
            }
#endif
        }
        if (computeShader != null)
        {
            _kernel = computeShader.FindKernel("DualFisheyeToEquirect");
            Debug.Log($"[DualFisheyeStitcher] ComputeShader OK: kernel={_kernel}");
        }
        else
        {
            Debug.LogError("[DualFisheyeStitcher] ✗ computeShader 仍未赋值. 把 Assets/VRLocomotion/Shaders/DualFisheyeStitch.compute 拖到此组件的 Compute Shader 字段.");
        }

        Debug.Log($"[DualFisheyeStitcher] Awake 完成. receiver={(receiver!=null?receiver.name:"NULL")}, " +
                  $"sphereRenderer={(sphereRenderer!=null?sphereRenderer.name:"NULL")}, " +
                  $"sourceMaterial={(_sourceMaterial!=null?"OK":"NULL")}, " +
                  $"sphereMaterial={(_sphereMaterial!=null?"OK":"NULL")}, " +
                  $"equirectRT={(_equirectRT!=null?$"{_equirectRT.width}x{_equirectRT.height}":"NULL")}");
    }

    private void OnDestroy()
    {
        if (_equirectRT != null)
        {
            _equirectRT.Release();
            DestroyImmediate(_equirectRT);
            _equirectRT = null;
        }
        if (_sourceMaterial != null) { DestroyImmediate(_sourceMaterial); _sourceMaterial = null; }
        if (_sphereMaterial != null) { DestroyImmediate(_sphereMaterial); _sphereMaterial = null; }
    }

    private void AllocateRT()
    {
        if (_equirectRT != null
            && _equirectRT.width  == equirectWidth
            && _equirectRT.height == equirectHeight)
            return;

        if (_equirectRT != null) { _equirectRT.Release(); DestroyImmediate(_equirectRT); }

        _equirectRT = new RenderTexture(equirectWidth, equirectHeight, 0, RenderTextureFormat.ARGB32)
        {
            enableRandomWrite = true,
            filterMode        = FilterMode.Bilinear,
            wrapModeU         = TextureWrapMode.Repeat,
            wrapModeV         = TextureWrapMode.Clamp,
            name              = "DualFisheyeStitcher_EquirectRT"
        };
        _equirectRT.Create();

        if (_sphereMaterial != null)
        {
            // HDRP/Unlit 同时认 _BaseColorMap 和 _UnlitColorMap (内部别名), 全部写, 保证生效
            SetTextureIfPresent(_sphereMaterial, _BaseColorMapId, _equirectRT);
            SetTextureIfPresent(_sphereMaterial, _UnlitColorMapId, _equirectRT);
            SetTextureIfPresent(_sphereMaterial, _MainTexId, _equirectRT);
        }
    }

    private void Update()
    {
        // 每 5 秒打一次状态, 没有静默退出
        if (computeShader == null)
        {
            if (Time.frameCount % 300 == 0)
                Debug.LogWarning("[DualFisheyeStitcher] computeShader 仍未赋值 (Update).");
            return;
        }
        if (_sourceMaterial == null)
        {
            if (Time.frameCount % 300 == 0)
                Debug.LogWarning("[DualFisheyeStitcher] _sourceMaterial 是 null (Awake 失败?).");
            return;
        }

        // Prefer the receiver's decoded texture directly. The hidden material path is kept as a
        // fallback only, because HDRP shader texture property names changed between versions.
        Texture src = receiver != null ? receiver.VideoTexture : null;
        if (src == null) src = GetTextureIfPresent(_sourceMaterial, _sourceTexturePropertyId);
        if (src == null) src = GetTextureIfPresent(_sourceMaterial, _UnlitColorMapId);
        if (src == null) src = GetTextureIfPresent(_sourceMaterial, _BaseColorMapId);
        if (src == null) src = GetTextureIfPresent(_sourceMaterial, _MainTexId);
        if (src == null)
        {
            _framesWithoutSource++;
            if (verboseLogging && _framesWithoutSource == 120)
                Debug.LogWarning("[DualFisheyeStitcher] 已经 120 帧没拿到 dual-fisheye 纹理. " +
                                 $"检查: receiver.targetMaterial 是否被覆盖? receiver.targetTextureProperty='{receiver?.targetTextureProperty}'? " +
                                 "FFmpeg 是否真的有输出?");
            return;
        }

        AllocateRT();
        if (_kernel < 0) _kernel = computeShader.FindKernel("DualFisheyeToEquirect");
        if (_kernel < 0) return;

        if (!_firstFrameLogged)
        {
            _firstFrameLogged = true;
            // 拿到信号了, tint 从 noSignalDebugColor 拨回白色
            if (_sphereMaterial != null)
            {
                SetColorIfPresent(_sphereMaterial, _BaseColorId, Color.white);
                SetColorIfPresent(_sphereMaterial, _UnlitColorId, Color.white);
            }
            if (verboseLogging)
                Debug.Log($"[DualFisheyeStitcher] ✓ 首帧到达: {src.width}x{src.height} ({src.GetType().Name}), " +
                          $"等待 {_framesWithoutSource} 帧. 开始 dispatch compute kernel.");
        }

        computeShader.SetTexture(_kernel, _SrcId, src);
        computeShader.SetTexture(_kernel, _DstId, _equirectRT);
        computeShader.SetInts(_SrcSizeId, src.width, src.height);
        computeShader.SetInts(_DstSizeId, _equirectRT.width, _equirectRT.height);
        computeShader.SetFloat(_FovDegId, fovDeg);
        computeShader.SetFloat(_BackFlipXId, backFlipX ? 1f : 0f);
        Vector3 stabilizationEuler = Vector3.zero;
        bool hasImuStabilization = enableImuStabilization && imuStabilizer != null && imuStabilizer.HasPose;
        if (hasImuStabilization)
        {
            stabilizationEuler = imuStabilizer.CompensationEulerDeg;
        }

        computeShader.SetFloat(_EnableImuStabilizationId, hasImuStabilization ? 1f : 0f);
        computeShader.SetFloat(_StabilizePitchDegId, stabilizationEuler.x);
        computeShader.SetFloat(_StabilizeYawDegId, stabilizationEuler.y);
        computeShader.SetFloat(_StabilizeRollDegId, stabilizationEuler.z);

        int gx = (_equirectRT.width  + 7) / 8;
        int gy = (_equirectRT.height + 7) / 8;
        computeShader.Dispatch(_kernel, gx, gy, 1);
    }

    private static string ResolveTexturePropertyName(Material material)
    {
        if (material == null) return "_UnlitColorMap";
        if (material.HasProperty(_UnlitColorMapId)) return "_UnlitColorMap";
        if (material.HasProperty(_BaseColorMapId)) return "_BaseColorMap";
        if (material.HasProperty(_MainTexId)) return "_MainTex";
        return "_UnlitColorMap";
    }

    private static Texture GetTextureIfPresent(Material material, int propertyId)
    {
        if (material == null || !material.HasProperty(propertyId))
        {
            return null;
        }

        return material.GetTexture(propertyId);
    }

    private static void SetTextureIfPresent(Material material, int propertyId, Texture texture)
    {
        if (material != null && texture != null && material.HasProperty(propertyId))
        {
            material.SetTexture(propertyId, texture);
        }
    }

    private static void SetColorIfPresent(Material material, int propertyId, Color color)
    {
        if (material != null && material.HasProperty(propertyId))
        {
            material.SetColor(propertyId, color);
        }
    }

    private void OnGUI()
    {
        if (!drawEquirectOverlay || _equirectRT == null) return;
        int w = Mathf.Min(480, Screen.width / 3);
        int h = w / 2;
        var rect = new Rect(Screen.width - w - 16, Screen.height - h - 16, w, h);
        // 半透明黑底, 方便看清边界
        GUI.color = new Color(0, 0, 0, 0.6f);
        GUI.DrawTexture(new Rect(rect.x - 4, rect.y - 4, rect.width + 8, rect.height + 8), Texture2D.whiteTexture);
        GUI.color = Color.white;
        GUI.DrawTexture(rect, _equirectRT, ScaleMode.StretchToFill, false);
        GUI.Label(new Rect(rect.x, rect.y - 18, rect.width, 16),
            $"Stitched equirect {_equirectRT.width}x{_equirectRT.height}");
    }
}
