using Colossal.UI;
using Colossal.UI.Binding;
using Game;
using Game.Input;
using Game.UI;
using Game.SceneFlow;
using System.IO;
using UnityEngine;

namespace GrassSprites {
    /// <summary>
    /// Bridge for the GrassSprites top-left button and React/Coherent brush panel UI.
    /// </summary>
    public partial class GrassSpritesUISystem : UISystemBase {
        public const string kBindingGroup = "GrassSprites";

        public override GameMode gameMode => GameMode.GameOrEditor;

        private ValueBinding<bool> m_EnabledBinding;
        private ValueBinding<bool> m_PanelVisibleBinding;
        private ValueBinding<bool> m_PrecisionModeBinding;
        private ValueBinding<float> m_BrushRadiusBinding;

        private static bool s_PointerOverPanel;


        protected override void OnCreate() {
            base.OnCreate();

            RegisterUIHostLocation();

            AddBinding(m_EnabledBinding = new ValueBinding<bool>(
                kBindingGroup,
                "enabled",
                Mod.Settings?.Enabled ?? false
            ));

            AddBinding(m_PanelVisibleBinding = new ValueBinding<bool>(
                kBindingGroup,
                "panelVisible",
                (Mod.Settings?.Enabled ?? false) && (Mod.Settings?.BrushPanelVisible ?? false)
            ));

            AddBinding(m_PrecisionModeBinding = new ValueBinding<bool>(
                kBindingGroup,
                "precisionMode",
                Mod.Settings?.PrecisionDabMode ?? false
            ));

            AddBinding(m_BrushRadiusBinding = new ValueBinding<float>(
                kBindingGroup,
                "brushRadius",
                Mod.Settings?.PaintRadiusMeters ?? 40f
            ));

            AddBinding(new TriggerBinding(kBindingGroup, "togglePanel", TogglePanel));
            AddBinding(new TriggerBinding(kBindingGroup, "setNormalBrush", SetNormalBrush));
            AddBinding(new TriggerBinding(kBindingGroup, "setPrecisionBrush", SetPrecisionBrush));
            AddBinding(new TriggerBinding<float>(kBindingGroup, "setBrushRadius", SetBrushRadius));
            AddBinding(new TriggerBinding<bool>(kBindingGroup, "setPointerIsOverPanel", SetPointerIsOverPanel));
        }

        private void RegisterUIHostLocation() {
            try {
                var modDir = Mod.ModDirectory;
                if (string.IsNullOrWhiteSpace(modDir) || !Directory.Exists(modDir)) {
                    Mod.log.Warn($"GrassSprites UI host location not registered because mod directory is unavailable: {modDir}");
                    return;
                }

                // AddHostLocation only exposes files through coui://ui-mods/
                // the game's mod manager pushes the module entrypoint into appBindings via AddActiveUIModLocation
                // without this, the JS file can exist but not get imported by the UI runtime
                UIManager.defaultUISystem.AddHostLocation("ui-mods", modDir, false);
                var couiPath = "coui://ui-mods/GrassSprites.js";
                GameManager.instance.userInterface.appBindings.AddActiveUIModLocation(new[] { couiPath });

                Mod.log.Info($"Registered GrassSprites UI host location: {modDir}");
                Mod.log.Info($"Registered GrassSprites active UI module: {couiPath}");
            }
            catch (System.Exception ex) {
                Mod.log.Warn($"Failed to register GrassSprites UI host location: {ex}");
            }
        }

        protected override void OnUpdate() {
            base.OnUpdate();
            UpdateBindings();
        }

        private void TogglePanel() {
            var settings = Mod.Settings;

            if (settings == null || !settings.Enabled) {
                UpdateBindings(force: true);
                return;
            }

            if (GrassSpritesRuntime.Instance != null) {
                GrassSpritesRuntime.Instance.ToggleBrushPanel("native UI");
            }
            else {
                settings.BrushPanelVisible = !settings.BrushPanelVisible;
                settings.BrushToolActive = settings.BrushPanelVisible;
            }

            UpdateBindings(force: true);
        }

        private void SetNormalBrush() {
            if (Mod.Settings == null) {
                return;
            }

            Mod.Settings.PrecisionDabMode = false;
            UpdateBindings(force: true);
        }

        private void SetPrecisionBrush() {
            if (Mod.Settings == null) {
                return;
            }

            Mod.Settings.PrecisionDabMode = true;
            UpdateBindings(force: true);
        }

        private void SetBrushRadius(float radius) {
            if (Mod.Settings == null) {
                return;
            }

            Mod.Settings.PaintRadiusMeters = Mathf.Clamp(radius, 1f, 250f);
            UpdateBindings(force: true);
        }

        public static void SetPointerOverPanel(bool pointerOverPanel) {
            s_PointerOverPanel = pointerOverPanel;
        }

        public static bool ShouldIgnoreBrushInput() {
            if (s_PointerOverPanel) {
                return true;
            }

            return InputManager.instance != null && InputManager.instance.mouseOverUI;
        }

        private void SetPointerIsOverPanel(bool pointerOverPanel) {
            SetPointerOverPanel(pointerOverPanel);
        }

        private void UpdateBindings(bool force = false) {
            var settings = Mod.Settings;
            var enabled = settings?.Enabled ?? false;
            var visible = enabled && (settings?.BrushPanelVisible ?? false);
            var precision = settings?.PrecisionDabMode ?? false;
            var radius = settings?.PaintRadiusMeters ?? 40f;

            if (force || m_EnabledBinding.value != enabled) {
                m_EnabledBinding.Update(enabled);
            }

            if (force || m_PanelVisibleBinding.value != visible) {
                m_PanelVisibleBinding.Update(visible);
            }

            if (force || m_PrecisionModeBinding.value != precision) {
                m_PrecisionModeBinding.Update(precision);
            }

            if (force || Mathf.Abs(m_BrushRadiusBinding.value - radius) > 0.01f) {
                m_BrushRadiusBinding.Update(radius);
            }
        }
    }
}
