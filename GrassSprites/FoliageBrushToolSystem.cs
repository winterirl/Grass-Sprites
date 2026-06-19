using Game.Common;
using Game.Input;
using Game.Prefabs;
using Game.Rendering;
using Game.Tools;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GrassSprites {
    /// <summary>
    /// Native-like tool wrapper for foliage mask painting.
    ///
    /// This uses ToolBaseSystem for the native CS2 terrain raycast/focus lifecycle.
    /// 
    /// Painting input is read from mouse buttons ~
    /// left/right map to add/remove while the tool is active.
    /// </summary>
    public partial class FoliageBrushToolSystem : ToolBaseSystem {
        private GrassSpritesRuntime m_MaskSystem;
        private OverlayRenderSystem m_OverlayRenderSystem;
        private bool m_LoggedRaycastHit;

        public override string toolID => "GrassSprites Foliage Brush";
        public override bool brushing => true;

        protected override void OnCreate() {
            base.OnCreate();
            brushSize = 40f;
            brushStrength = 1f;
            m_OverlayRenderSystem = World.GetOrCreateSystemManaged<OverlayRenderSystem>();
        }

        protected override void OnStartRunning() {
            base.OnStartRunning();
            m_LoggedRaycastHit = false;
        }

        public override PrefabBase GetPrefab() {
            return null;
        }

        public override bool TrySetPrefab(PrefabBase prefab) {
            return false;
        }

        public override void InitializeRaycast() {
            base.InitializeRaycast();
            m_ToolRaycastSystem.typeMask = TypeMask.Terrain;
            m_ToolRaycastSystem.raycastFlags |= RaycastFlags.Outside;
        }

        protected override JobHandle OnUpdate(JobHandle inputDeps) {
            var settings = Mod.Settings;

            if (settings == null || !settings.Enabled || !settings.BrushToolActive) {
                return inputDeps;
            }

            if (m_MaskSystem == null) {
                m_MaskSystem = World.GetOrCreateSystemManaged<GrassSpritesRuntime>();
            }

            if (m_OverlayRenderSystem == null) {
                m_OverlayRenderSystem = World.GetOrCreateSystemManaged<OverlayRenderSystem>();
            }

            brushSize = Mathf.Max(1f, settings.PaintRadiusMeters);
            brushStrength = 1f;

            InitializeRaycast();

            if (WasCancelPressed()) {
                DeactivateTool();
                return inputDeps;
            }

            if (GrassSpritesUISystem.ShouldIgnoreBrushInput()) {
                return inputDeps;
            }

            if (!GetRaycastResult(out ControlPoint point)) {
                return inputDeps;
            }

            float3 hit = point.m_HitPosition;
            var worldPosition = new Vector3(hit.x, hit.y, hit.z);
            var paintAction = GetPaintAction();

            // draw the native overlay every frame while the brush is active, even when the user is only hovering
            inputDeps = DrawBrushOverlay(inputDeps, hit, settings, paintAction);

            if (paintAction == PaintAction.None) {
                return inputDeps;
            }

            m_MaskSystem.PaintAtWorldPosition(worldPosition, settings.PaintRadiusMeters, add: paintAction == PaintAction.Add, precisionDab: settings.PrecisionDabMode);

            if (!m_LoggedRaycastHit) {
                Mod.log.Info($"GrassSprites native brush raycast hit at {worldPosition}.");
                m_LoggedRaycastHit = true;
            }

            return inputDeps;
        }

        private enum PaintAction {
            None,
            Add,
            Remove
        }

        private PaintAction GetPaintAction() {
            var mouse = Mouse.current;
            
            if (mouse == null) {
                return PaintAction.None;
            }


            // also means remove fires if both buttons are pressed together
            if (mouse.rightButton.isPressed) {
                return PaintAction.Remove;
            }

            if (mouse.leftButton.isPressed) {
                return PaintAction.Add;
            }

            return PaintAction.None;
        }

        private bool WasCancelPressed() {
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame) {
                return true;
            }

            // Keep the game's cancel action as a fallback, but make sure right mouse doesn't trigger it.
            return cancelAction.WasPressedThisFrame() && !(Mouse.current?.rightButton.isPressed ?? false);
        }


        private void DeactivateTool() {
            if (Mod.Settings != null) {
                Mod.Settings.BrushToolActive = false;
                Mod.Settings.BrushPanelVisible = false;
            }

            if (m_ToolSystem != null && m_DefaultToolSystem != null && m_ToolSystem.activeTool == this) {
                m_ToolSystem.activeTool = m_DefaultToolSystem;
            }

            Mod.log.Info("GrassSprites foliage brush cancelled / deactivated.");
        }

        private JobHandle DrawBrushOverlay(JobHandle inputDeps, float3 worldPosition, Setting settings, PaintAction paintAction) {
            if (m_OverlayRenderSystem == null) {
                return inputDeps;
            }

            var overlayBuffer = m_OverlayRenderSystem.GetBuffer(out var overlayDeps);

            // precision only paints 1px, but 0.5f is a nice middleground between easily visible and accurate spatial representation
            var radius = settings.PrecisionDabMode
                ? Mathf.Max(0.35f, m_MaskSystem != null ? m_MaskSystem.GetApproxMaskMetersPerPixel() * 0.5f : 0.5f)
                : Mathf.Max(0.1f, settings.PaintRadiusMeters);

            var job = new FoliageBrushOverlayJob {
                m_OverlayBuffer = overlayBuffer,
                m_Position = worldPosition,
                m_Radius = radius,
                m_AddMode = paintAction != PaintAction.Remove,
                m_PrecisionMode = settings.PrecisionDabMode
            };

            var handle = job.Schedule(JobHandle.CombineDependencies(inputDeps, overlayDeps));
            m_OverlayRenderSystem.AddBufferWriter(handle);
            return handle;
        }

        private struct FoliageBrushOverlayJob : IJob {
            public OverlayRenderSystem.Buffer m_OverlayBuffer;
            public float3 m_Position;
            public float m_Radius;
            public bool m_AddMode;
            public bool m_PrecisionMode;

            public void Execute() {
                var radius = math.max(0.1f, m_Radius);

                // Green for add, red for remove. Green by default since we only remove when right click is held
                var outline = m_AddMode
                    ? new Color(0.2f, 1f, 0.25f, 1f)
                    : new Color(1f, 0.25f, 0.15f, 1f);

                var fill = m_AddMode
                    ? new Color(0.2f, 1f, 0.25f, m_PrecisionMode ? 0.2f : 0.11f)
                    : new Color(1f, 0.25f, 0.15f, m_PrecisionMode ? 0.2f : 0.11f);

                var outlineWidth = m_PrecisionMode
                    ? 0.1f
                    : math.clamp(radius * 0.018f, 0.08f, 0.45f);

                m_OverlayBuffer.DrawCircle(
                    outline,
                    fill,
                    outlineWidth,
                    OverlayRenderSystem.StyleFlags.Projected | OverlayRenderSystem.StyleFlags.DepthFadeBelow,
                    new float2(0f, 1f),
                    m_Position,
                    radius * 2f
                );
            }
        }
    }
}
