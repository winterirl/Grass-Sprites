using System.Reflection;
using Colossal.Serialization.Entities;
using Game;
using Game.Input;
using Game.Rendering;
using Game.SceneFlow;
using Game.Serialization;
using Game.Simulation;
using Game.Tools;
using UnityEngine;
using UnityEngine.VFX;

namespace GrassSprites {
    /// <summary>
    /// Runtime backend for the vanilla "Vegetation/FoliageVFX" vfx graph asset and my grasssprites mask.
    ///
    /// - FoliageVFX uses the green channel of "Terrain SplatMap" for foliage coverage.
    ///
    /// This system owns the runtime mask texture and feeds it to FoliageVFX.
    /// </summary>
    public partial class GrassSpritesRuntime : GameSystemBase {
        public static GrassSpritesRuntime Instance { get; private set; }

        private VegetationRenderSystem m_VegetationRenderSystem;
        private TerrainSystem m_TerrainSystem;
        private ToolSystem m_ToolSystem;
        private DefaultToolSystem m_DefaultToolSystem;
        private FoliageBrushToolSystem m_FoliageBrushToolSystem;
        private LoadGameSystem m_LoadGameSystem;
        private ProxyAction m_TogglePanelAction;
        private FieldInfo m_FoliageVfxField;

        private string m_CurrentSaveId;
        private bool m_LoggedNoSaveId;
        private bool m_PendingMaskAutoLoad;
        private float m_PendingMaskAutoLoadStartTime;
        private float m_LastPendingMaskAutoLoadAttemptTime;
        private const float kMaskAutoLoadRetrySeconds = 30f;
        private const float kMaskAutoLoadRetryIntervalSeconds = 1f;

        private Texture2D m_UserMaskSplatMap;
        private Texture2D m_DirtyPatchTexture;
        private byte[] m_UserMaskBytes;
        // Packed pixels for the runtime texture. m_UserMaskBytes is the canonical save/load format - this array only exists to upload that mask to the GPU.
        private ushort[] m_TexturePixels;
        private ushort[] m_DirtyPatchPixels;
        private int m_UserMaskSize;
        private int m_DirtyPatchWidth;
        private int m_DirtyPatchHeight;
        private bool m_LoggedCopyTextureFallback;
        private float m_LastPaintTime;
        private Vector3 m_LastPaintPosition = new Vector3(float.NaN, float.NaN, float.NaN);

        private bool m_LoggedMissingVfx;
        private bool m_LoggedFoundVfx;
        private bool m_LoggedMissingField;
        private bool m_LoggedPaintHit;
        private bool m_LastBrushToolActive;

        protected override void OnCreate() {
            base.OnCreate();
            Instance = this;

            m_VegetationRenderSystem = World.GetOrCreateSystemManaged<VegetationRenderSystem>();
            m_TerrainSystem = World.GetOrCreateSystemManaged<TerrainSystem>();
            m_ToolSystem = World.GetOrCreateSystemManaged<ToolSystem>();
            m_DefaultToolSystem = World.GetOrCreateSystemManaged<DefaultToolSystem>();
            m_FoliageBrushToolSystem = World.GetOrCreateSystemManaged<FoliageBrushToolSystem>();
            m_LoadGameSystem = World.GetOrCreateSystemManaged<LoadGameSystem>();

            if (m_LoadGameSystem != null) {
                m_LoadGameSystem.onOnSaveGameLoaded += OnSaveGameLoaded;
            }

            if (GameManager.instance != null) {
                GameManager.instance.onGameSaveLoad += OnGameSaveLoad;
            }

            m_FoliageVfxField = typeof(VegetationRenderSystem).GetField(
                "m_FoliageVFX",
                BindingFlags.Instance | BindingFlags.NonPublic
            );

            if (m_FoliageVfxField == null) {
                Mod.log.Warn("Could not find VegetationRenderSystem.m_FoliageVFX. GrassSprites will do nothing.");
                m_LoggedMissingField = true;
            }

        }

        protected override void OnUpdate() {
            var settings = Mod.Settings;

            HandleRuntimeHotkeys(settings);
            HandlePendingMaskAutoLoad();

            if (settings == null || !settings.Enabled) {
                DeactivateBrushToolIfActive(closePanel: true);
                DisableVanillaFoliageSystemIfActive();
                return;
            }

            if (m_FoliageVfxField == null) {
                if (!m_LoggedMissingField) {
                    Mod.log.Warn("Could not find VegetationRenderSystem.m_FoliageVFX. GrassSprites will do nothing.");
                    m_LoggedMissingField = true;
                }
                return;
            }

            // Vanilla creates this system disabled.
            // Enabling it here makes the feature self-contained instead of relying on the hidden/dev toggle.
            if (m_VegetationRenderSystem != null && !m_VegetationRenderSystem.Enabled) {
                m_VegetationRenderSystem.Enabled = true;
                Mod.log.Info("Enabled vanilla VegetationRenderSystem for GrassSprites.");
            }

            var vfx = m_FoliageVfxField.GetValue(m_VegetationRenderSystem) as VisualEffect;
            if (vfx == null) {
                if (!m_LoggedMissingVfx) {
                    Mod.log.Info("FoliageVFX has not been created yet. Waiting for VegetationRenderSystem to create it.");
                    m_LoggedMissingVfx = true;
                }
                return;
            }

            if (!m_LoggedFoundVfx) {
                Mod.log.Info("Found live FoliageVFX instance. Applying GrassSprites foliage mask.");
                m_LoggedFoundVfx = true;
            }

            ApplyFoliageMask(vfx);
            UpdateBrushToolSelection(settings);
        }

        private void DisableVanillaFoliageSystemIfActive() {
            if (m_VegetationRenderSystem != null && m_VegetationRenderSystem.Enabled) {
                m_VegetationRenderSystem.Enabled = false;
                Mod.log.Info("Disabled vanilla VegetationRenderSystem because Grass Sprites is disabled.");
            }
        }

        private void HandleRuntimeHotkeys(Setting settings) {
            if (settings == null || !settings.Enabled) {
                return;
            }

            if (InputManager.instance != null && InputManager.instance.hasInputFieldFocus) {
                return;
            }

            var action = GetTogglePanelAction(settings);

            if (action == null || !action.WasPerformedThisFrame()) {
                return;
            }

            ToggleBrushPanel("hotkey");
        }

        private ProxyAction GetTogglePanelAction(Setting settings) {
            if (m_TogglePanelAction != null) {
                m_TogglePanelAction.shouldBeEnabled = true;
                return m_TogglePanelAction;
            }

            try {
                m_TogglePanelAction = settings.GetAction(Mod.kTogglePanelActionName);
                if (m_TogglePanelAction != null) {
                    m_TogglePanelAction.shouldBeEnabled = true;
                }
            }
            catch {
                m_TogglePanelAction = null;
            }

            return m_TogglePanelAction;
        }

        public void ToggleBrushPanel(string source = "ui") {
            var settings = Mod.Settings;
            if (settings == null || !settings.Enabled) {
                return;
            }

            var visible = !settings.BrushPanelVisible;
            settings.BrushPanelVisible = visible;
            settings.BrushToolActive = visible;
            if (!visible) {
                GrassSpritesUISystem.SetPointerOverPanel(false);
            }
            Mod.log.Info($"GrassSprites brush panel toggled by {source}: {(visible ? "open" : "closed")}");
        }

        public float GetApproxMaskMetersPerPixel() {
            if (m_TerrainSystem == null || m_UserMaskSize <= 0) {
                return 1f;
            }

            var bounds = m_TerrainSystem.GetTerrainBounds();
            var mx = bounds.size.x / Mathf.Max(1, m_UserMaskSize);
            var mz = bounds.size.z / Mathf.Max(1, m_UserMaskSize);
            return Mathf.Max(mx, mz);
        }

        public void ClearMask() {
            EnsureUserMask(resetToEmpty: true);
            m_LastPaintPosition = new Vector3(float.NaN, float.NaN, float.NaN);
            m_LastPaintTime = 0f;
            Mod.log.Info("GrassSprites foliage mask cleared to empty / no painted foliage.");
        }

        private void ApplyFoliageMask(VisualEffect vfx) {
            EnsureUserMask(resetToEmpty: false);
            vfx.SetTexture("Terrain SplatMap", m_UserMaskSplatMap);
        }

        private void UpdateBrushToolSelection(Setting settings) {
            if (m_ToolSystem == null || m_FoliageBrushToolSystem == null) {
                return;
            }

            var shouldBeActive = settings.BrushToolActive;

            // Do not force the brush active every frame because that steals focus back from menus and other tools.
            if (shouldBeActive && !m_LastBrushToolActive) {
                if (m_ToolSystem.activeTool != m_FoliageBrushToolSystem) {
                    m_ToolSystem.activeTool = m_FoliageBrushToolSystem;
                    Mod.log.Info("Activated GrassSprites native foliage brush tool.");
                }
            }
            else if (!shouldBeActive && m_LastBrushToolActive) {
                DeactivateBrushToolIfActive();
            }
            else if (shouldBeActive && m_ToolSystem.activeTool != m_FoliageBrushToolSystem) {
                // If another tool took over reflect that in our settings instead of immediately stealing focus back.
                settings.BrushToolActive = false;
                settings.BrushPanelVisible = false;
                shouldBeActive = false;
            }

            m_LastBrushToolActive = shouldBeActive;
        }

        private void DeactivateBrushToolIfActive(bool closePanel = false) {
            if (m_ToolSystem != null && m_FoliageBrushToolSystem != null && m_ToolSystem.activeTool == m_FoliageBrushToolSystem) {
                m_ToolSystem.activeTool = m_DefaultToolSystem;
                Mod.log.Info("Deactivated GrassSprites foliage brush tool.");
            }
            if (closePanel && Mod.Settings != null) {
                Mod.Settings.BrushPanelVisible = false;
                Mod.Settings.BrushToolActive = false;
                GrassSpritesUISystem.SetPointerOverPanel(false);
            }
            m_LastBrushToolActive = false;
        }

        public void PaintAtWorldPosition(Vector3 position, float radiusMeters, bool add, bool precisionDab = false) {
            if (m_TerrainSystem == null || m_UserMaskSplatMap == null || m_UserMaskBytes == null) {
                return;
            }

            // Avoid uploading the texture every single tool update while the pointer is stationary.
            var minMove = precisionDab ? 0.05f : Mathf.Max(0.25f, radiusMeters * 0.08f);
            if (UnityEngine.Time.realtimeSinceStartup - m_LastPaintTime < 0.05f &&
                !float.IsNaN(m_LastPaintPosition.x) &&
                Vector3.Distance(position, m_LastPaintPosition) < minMove) {
                return;
            }

            var terrainBounds = m_TerrainSystem.GetTerrainBounds();
            var painted = precisionDab
                ? PaintSinglePixel(position, terrainBounds, add, out var dirtyX, out var dirtyY, out var dirtyWidth, out var dirtyHeight)
                : PaintWorldCircle(position, radiusMeters, terrainBounds, add, out dirtyX, out dirtyY, out dirtyWidth, out dirtyHeight);

            if (!painted) {
                return;
            }

            UploadDirtyRect(dirtyX, dirtyY, dirtyWidth, dirtyHeight);

            m_LastPaintTime = UnityEngine.Time.realtimeSinceStartup;
            m_LastPaintPosition = position;

            if (!m_LoggedPaintHit) {
                Mod.log.Info($"GrassSprites brush painted at {position}.");
                m_LoggedPaintHit = true;
            }
        }

        private bool PaintSinglePixel(Vector3 worldPosition, Bounds terrainBounds, bool add, out int dirtyX, out int dirtyY, out int dirtyWidth, out int dirtyHeight) {
            dirtyX = 0;
            dirtyY = 0;
            dirtyWidth = 0;
            dirtyHeight = 0;

            if (terrainBounds.size.x <= 0f || terrainBounds.size.z <= 0f) {
                return false;
            }

            var min = terrainBounds.min;
            var size = terrainBounds.size;
            var u = Mathf.InverseLerp(min.x, min.x + size.x, worldPosition.x);
            var v = Mathf.InverseLerp(min.z, min.z + size.z, worldPosition.z);
            if (u < 0f || u > 1f || v < 0f || v > 1f) {
                return false;
            }

            var centerXFloat = u * (m_UserMaskSize - 1);
            var centerYFloat = v * (m_UserMaskSize - 1);
            var centerX = Mathf.Clamp(Mathf.RoundToInt(centerXFloat), 0, m_UserMaskSize - 1);
            var centerY = Mathf.Clamp(Mathf.RoundToInt(centerYFloat), 0, m_UserMaskSize - 1);
            var index = centerY * m_UserMaskSize + centerX;
            var current = m_UserMaskBytes[index];
            const int dabStep = 16;
            byte next = add
                ? (byte)Mathf.Min(255, current + dabStep)
                : (byte)Mathf.Max(0, current - dabStep);

            if (next == current) {
                return false;
            }

            m_UserMaskBytes[index] = next;
            SetTextureStoragePixel(index, next);
            dirtyX = centerX;
            dirtyY = centerY;
            dirtyWidth = 1;
            dirtyHeight = 1;
            return true;
        }

        private bool PaintWorldCircle(Vector3 worldPosition, float radiusMeters, Bounds terrainBounds, bool add, out int dirtyX, out int dirtyY, out int dirtyWidth, out int dirtyHeight) {
            dirtyX = 0;
            dirtyY = 0;
            dirtyWidth = 0;
            dirtyHeight = 0;

            if (terrainBounds.size.x <= 0f || terrainBounds.size.z <= 0f) {
                return false;
            }

            var min = terrainBounds.min;
            var size = terrainBounds.size;

            var u = Mathf.InverseLerp(min.x, min.x + size.x, worldPosition.x);
            var v = Mathf.InverseLerp(min.z, min.z + size.z, worldPosition.z);

            if (u < 0f || u > 1f || v < 0f || v > 1f) {
                return false;
            }

            var centerXFloat = u * (m_UserMaskSize - 1);
            var centerYFloat = v * (m_UserMaskSize - 1);
            var centerX = Mathf.RoundToInt(centerXFloat);
            var centerY = Mathf.RoundToInt(centerYFloat);

            var radiusXFloat = radiusMeters / size.x * m_UserMaskSize;
            var radiusYFloat = radiusMeters / size.z * m_UserMaskSize;

            var radiusX = Mathf.Max(1, Mathf.CeilToInt(radiusXFloat));
            var radiusY = Mathf.Max(1, Mathf.CeilToInt(radiusYFloat));

            var minX = Mathf.Clamp(centerX - radiusX, 0, m_UserMaskSize - 1);
            var maxX = Mathf.Clamp(centerX + radiusX, 0, m_UserMaskSize - 1);
            var minY = Mathf.Clamp(centerY - radiusY, 0, m_UserMaskSize - 1);
            var maxY = Mathf.Clamp(centerY + radiusY, 0, m_UserMaskSize - 1);

            var value = add ? (byte)255 : (byte)0;
            var changedPixels = 0;
            var actualMinX = m_UserMaskSize;
            var actualMinY = m_UserMaskSize;
            var actualMaxX = -1;
            var actualMaxY = -1;

            for (var y = minY; y <= maxY; y++) {
                var normalizedY = radiusY == 0 ? 0f : (float)(y - centerY) / radiusY;
                for (var x = minX; x <= maxX; x++) {
                    var normalizedX = radiusX == 0 ? 0f : (float)(x - centerX) / radiusX;
                    var distSq = normalizedX * normalizedX + normalizedY * normalizedY;
                    if (distSq > 1f) {
                        continue;
                    }

                    var index = y * m_UserMaskSize + x;
                    var currentValue = m_UserMaskBytes[index];
                    if (currentValue == value) {
                        continue;
                    }

                    m_UserMaskBytes[index] = value;
                    SetTextureStoragePixel(index, value);
                    changedPixels++;
                    if (x < actualMinX) actualMinX = x;
                    if (y < actualMinY) actualMinY = y;
                    if (x > actualMaxX) actualMaxX = x;
                    if (y > actualMaxY) actualMaxY = y;
                }
            }

            if (changedPixels == 0) {
                return false;
            }

            dirtyX = actualMinX;
            dirtyY = actualMinY;
            dirtyWidth = actualMaxX - actualMinX + 1;
            dirtyHeight = actualMaxY - actualMinY + 1;
            return true;
        }

        private void UploadDirtyRect(int x, int y, int width, int height) {
            if (m_UserMaskSplatMap == null || m_TexturePixels == null || width <= 0 || height <= 0) {
                return;
            }

            // The mask texture is kept in a compact format because FoliageVFX only samples the green channel for coverage.
            // Upload only the changed patch to a temporary texture, then copy that patch into the live mask texture on the GPU.
            try {
                EnsureDirtyPatchTexture(width, height);
                if (m_DirtyPatchTexture == null || m_DirtyPatchPixels == null) {
                    UploadWholeTexture();
                    return;
                }

                for (var row = 0; row < height; row++) {
                    var sourceIndex = (y + row) * m_UserMaskSize + x;
                    var targetIndex = row * width;
                    System.Array.Copy(m_TexturePixels, sourceIndex, m_DirtyPatchPixels, targetIndex, width);
                }

                m_DirtyPatchTexture.SetPixelData(m_DirtyPatchPixels, 0);
                m_DirtyPatchTexture.Apply(false, false);
                Graphics.CopyTexture(m_DirtyPatchTexture, 0, 0, 0, 0, width, height, m_UserMaskSplatMap, 0, 0, x, y);
            }
            catch (System.Exception ex) {
                // fallback if the compacted texture is problematic
                if (!m_LoggedCopyTextureFallback) {
                    Mod.log.Warn($"GrassSprites dirty patch upload failed; falling back to full texture uploads. {ex.GetType().Name}: {ex.Message}");
                    m_LoggedCopyTextureFallback = true;
                }
                UploadWholeTexture();
            }
        }

        private void EnsureDirtyPatchTexture(int width, int height) {
            if (m_DirtyPatchTexture != null &&
                m_DirtyPatchWidth == width &&
                m_DirtyPatchHeight == height &&
                m_DirtyPatchPixels != null &&
                m_DirtyPatchPixels.Length == width * height) {
                return;
            }

            if (m_DirtyPatchTexture != null) {
                Object.Destroy(m_DirtyPatchTexture);
                m_DirtyPatchTexture = null;
            }

            m_DirtyPatchWidth = width;
            m_DirtyPatchHeight = height;
            m_DirtyPatchPixels = new ushort[width * height];
            m_DirtyPatchTexture = new Texture2D(width, height, TextureFormat.RGB565, false, true) {
                name = $"GrassSprites_DirtyPatch_{width}x{height}",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Point
            };
        }

        private void SetTextureStoragePixel(int index, byte value) {
            if (m_TexturePixels == null) {
                return;
            }

            // TextureFormat.RGB565 stores green in the middle six bits.
            var g6 = (ushort)(value >> 2);
            m_TexturePixels[index] = (ushort)(g6 << 5);
        }

        private void RebuildTexturePixelsFromMaskBytes() {
            if (m_UserMaskBytes == null || m_TexturePixels == null) {
                return;
            }

            var count = System.Math.Min(m_UserMaskBytes.Length, m_TexturePixels.Length);
            for (var i = 0; i < count; i++) {
                SetTextureStoragePixel(i, m_UserMaskBytes[i]);
            }
        }

        private void EnsureUserMask(bool resetToEmpty) {
            var settings = Mod.Settings;
            var desiredSize = Mathf.Clamp(settings?.UserMaskResolution ?? 8192, 128, 8192);

            var replacingExistingMask = m_UserMaskBytes != null && m_UserMaskSize > 0 && m_UserMaskSize != desiredSize;

            if (m_UserMaskSplatMap == null || m_UserMaskSize != desiredSize) {
                if (m_UserMaskSplatMap != null) {
                    Object.Destroy(m_UserMaskSplatMap);
                }

                m_UserMaskSize = desiredSize;
                m_UserMaskSplatMap = new Texture2D(m_UserMaskSize, m_UserMaskSize, TextureFormat.RGB565, false, true) {
                    name = $"GrassSprites_UserFoliageMask_{m_UserMaskSize}",
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Point
                };

                var pixelCount = m_UserMaskSize * m_UserMaskSize;
                m_UserMaskBytes = new byte[pixelCount];
                m_TexturePixels = new ushort[pixelCount];
                m_DirtyPatchPixels = null;
                m_DirtyPatchWidth = 0;
                m_DirtyPatchHeight = 0;
                m_LoggedCopyTextureFallback = false;
                if (m_DirtyPatchTexture != null) {
                    Object.Destroy(m_DirtyPatchTexture);
                    m_DirtyPatchTexture = null;
                }

                var approxMegabytes = (m_UserMaskSize * m_UserMaskSize * 2f) / (1024f * 1024f);
                Mod.log.Info($"Created GrassSprites runtime foliage mask texture: {m_UserMaskSize}x{m_UserMaskSize} (~{approxMegabytes:0.#} MB GPU, plus CPU mask storage).");
                resetToEmpty = true;
            }

            if (!resetToEmpty || m_UserMaskBytes == null) {
                return;
            }

            for (var i = 0; i < m_UserMaskBytes.Length; i++) {
                m_UserMaskBytes[i] = 0;
                SetTextureStoragePixel(i, 0);
            }

            UploadWholeTexture();

            if (replacingExistingMask) {
                Mod.log.Info($"GrassSprites mask resolution changed to {m_UserMaskSize}; painted foliage was cleared.");
            }
        }

        private void UploadWholeTexture() {
            if (m_UserMaskSplatMap == null || m_TexturePixels == null) {
                return;
            }

            m_UserMaskSplatMap.SetPixelData(m_TexturePixels, 0);
            m_UserMaskSplatMap.Apply(false, false);
        }


        private void OnSaveGameLoaded(Context context) {
            var saveId = SaveIdToString(context.instigatorGuid);

            // A new game started from a map also raises this event but its 
            // instigatorGuid is the map asset id, not a SaveGameMetadata id.
            //
            // Never autoload masks for new games/maps, otherwise every new city started from the same map
            // can inherit a mask that was accidentally saved under the map id.
            //
            // Which was honestly just a fallback from an error I made earlier b/c I'm dumb
            if (context.purpose != Purpose.LoadGame) {
                m_CurrentSaveId = null;
                m_PendingMaskAutoLoad = false;
                m_LoggedNoSaveId = false;

                EnsureUserMask(resetToEmpty: true);

                Mod.log.Info($"GrassSprites detected non-save load context: purpose={context.purpose}, instigatorGuid={saveId ?? "<none>"}. Starting with an empty foliage mask and skipping autoload.");
                return;
            }

            if (string.IsNullOrEmpty(saveId)) {
                Mod.log.Info($"GrassSprites save load event did not include a valid save id. purpose={context.purpose}, version={context.version}");
                m_CurrentSaveId = null;
                return;
            }

            m_CurrentSaveId = saveId;
            m_LoggedNoSaveId = false;

            EnsureUserMask(resetToEmpty: true);

            Mod.log.Info($"GrassSprites loaded save id: {m_CurrentSaveId} (purpose={context.purpose}, version={context.version}). Cleared runtime mask; autoload will use this exact save id only.");
            QueueMaskAutoLoad();
        }

        // Try to load once immediately, then rety once per second for up to 30 seconds
        // Then it just gives up. like a quitter.
        private void QueueMaskAutoLoad() {
            m_PendingMaskAutoLoad = true;
            m_PendingMaskAutoLoadStartTime = UnityEngine.Time.realtimeSinceStartup;
            m_LastPendingMaskAutoLoadAttemptTime = -999f;

            TryPendingMaskAutoLoad(force: true);
        }

        private void HandlePendingMaskAutoLoad() {
            if (!m_PendingMaskAutoLoad) {
                return;
            }

            TryPendingMaskAutoLoad(force: false);
        }

        private void TryPendingMaskAutoLoad(bool force) {
            if (string.IsNullOrEmpty(m_CurrentSaveId)) {
                m_PendingMaskAutoLoad = false;
                return;
            }

            var now = UnityEngine.Time.realtimeSinceStartup;
            if (!force && now - m_LastPendingMaskAutoLoadAttemptTime < kMaskAutoLoadRetryIntervalSeconds) {
                return;
            }

            m_LastPendingMaskAutoLoadAttemptTime = now;

            if (LoadMaskForCurrentSave(clearIfMissing: false)) {
                m_PendingMaskAutoLoad = false;
                return;
            }

            if (now - m_PendingMaskAutoLoadStartTime >= kMaskAutoLoadRetrySeconds) {
                m_PendingMaskAutoLoad = false;
                EnsureUserMask(resetToEmpty: true);
                Mod.log.Info($"GrassSprites found no compatible foliage mask for exact save id {m_CurrentSaveId} after retrying; starting empty.");
            }
        }

        private void OnGameSaveLoad(string saveName, string previewUri, bool start, bool success) {
            if (start) {
                return;
            }

            if (!success) {
                Mod.log.Info($"GrassSprites skipped mask save because game save failed: {saveName}");
                return;
            }

            var previousSaveId = m_CurrentSaveId;
            if (TryGetLastSaveMetadataId(out var newSaveId)) {
                m_CurrentSaveId = newSaveId;
                m_LoggedNoSaveId = false;

                if (!string.Equals(previousSaveId, newSaveId, System.StringComparison.OrdinalIgnoreCase)) {
                    Mod.log.Info($"GrassSprites save id changed after successful game save: {previousSaveId ?? "<none>"} -> {newSaveId}. Saving current mask under the new save id only.");
                }
            }

            // painting only changes the in-memory mask until the game save succeeds.
            // this prevents Save As from overwriting an older save branch before the game has assigned the new save metadata id.
            SaveMaskForCurrentSave();
        }

        // All filenames use the plain 32-character save GUID. 
        // Normalizing everything here keeps Save, Save As, and Load using the same stable file key.
        private static string SaveIdToString(Colossal.Hash128 guid) {
            if (!guid.isValid) {
                return null;
            }

            return GrassSpritesMaskStorage.NormalizeSaveId(guid.ToString());
        }

        /// <summary>
        /// GameManager.onGameSaveLoad gives only the save name, not the metadata id.
        /// 
        /// After a successful save, the game stores the new SaveGameMetadata in settings.userState.lastSaveGameMetadata.
        /// Reflection keeps this mod from needing Harmony just to read that id.
        /// </summary>
        private static bool TryGetLastSaveMetadataId(out string saveId) {
            saveId = null;

            
            try {
                var gameManager = GameManager.instance;
                var settings = gameManager?.settings;
                if (settings == null) {
                    return false;
                }

                var settingsType = settings.GetType();
                var userStateProp = settingsType.GetProperty("userState", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                var userStateField = settingsType.GetField("userState", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                var userState = userStateProp != null ? userStateProp.GetValue(settings, null) : userStateField?.GetValue(settings);
                if (userState == null) {
                    return false;
                }

                var userStateType = userState.GetType();
                var lastSaveProp = userStateType.GetProperty("lastSaveGameMetadata", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                var lastSaveField = userStateType.GetField("lastSaveGameMetadata", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                var metadata = lastSaveProp != null ? lastSaveProp.GetValue(userState, null) : lastSaveField?.GetValue(userState);
                if (metadata == null) {
                    return false;
                }

                var metaType = metadata.GetType();
                var idProp = metaType.GetProperty("id", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                var idField = metaType.GetField("id", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                var idValue = idProp != null ? idProp.GetValue(metadata, null) : idField?.GetValue(metadata);
                if (idValue is Colossal.Hash128 guid) {
                    saveId = SaveIdToString(guid);
                    return !string.IsNullOrEmpty(saveId);
                }

                if (idValue != null) {
                    saveId = GrassSpritesMaskStorage.NormalizeSaveId(idValue.ToString());
                    return !string.IsNullOrEmpty(saveId);
                }
            }
            catch (System.Exception ex) {
                Mod.log.Warn($"GrassSprites could not read last save metadata id. {ex.GetType().Name}: {ex.Message}");
            }

            return false;
        }

        private bool LoadMaskForCurrentSave(bool clearIfMissing) {
            if (string.IsNullOrEmpty(m_CurrentSaveId)) {
                Mod.log.Info("GrassSprites cannot load a foliage mask because no current save id is available yet.");
                return false;
            }

            EnsureUserMask(resetToEmpty: false);
            if (m_UserMaskSize <= 0 || m_UserMaskBytes == null) {
                return false;
            }

            Mod.log.Info($"GrassSprites looking for exact foliage mask save id: {m_CurrentSaveId}");
            if (GrassSpritesMaskStorage.TryLoad(m_CurrentSaveId, m_UserMaskSize, out var loadedMask)) {
                ApplyLoadedMask(loadedMask, m_CurrentSaveId);
                return true;
            }

            if (clearIfMissing) {
                EnsureUserMask(resetToEmpty: true);
                Mod.log.Info($"GrassSprites found no compatible foliage mask for exact save id {m_CurrentSaveId}; starting empty.");
            }

            return false;
        }

        private void ApplyLoadedMask(byte[] loadedMask, string loadedSaveId) {
            if (loadedMask.Length != m_UserMaskBytes.Length) {
                Mod.log.Warn($"GrassSprites mask load returned {loadedMask.Length} bytes, expected {m_UserMaskBytes.Length}. Ignoring.");
                return;
            }

            System.Buffer.BlockCopy(loadedMask, 0, m_UserMaskBytes, 0, loadedMask.Length);
            RebuildTexturePixelsFromMaskBytes();
            UploadWholeTexture();
            Mod.log.Info($"GrassSprites loaded foliage mask for save id {loadedSaveId} at {m_UserMaskSize}x{m_UserMaskSize}.");
        }


        private void SaveMaskForCurrentSave() {
            if (string.IsNullOrEmpty(m_CurrentSaveId)) {
                if (!m_LoggedNoSaveId) {
                    Mod.log.Info("GrassSprites cannot save a foliage mask because no current save id is available yet.");
                    m_LoggedNoSaveId = true;
                }
                return;
            }

            EnsureUserMask(resetToEmpty: false);
            if (m_UserMaskBytes == null || m_UserMaskSize <= 0) {
                return;
            }

            if (GrassSpritesMaskStorage.TrySave(m_CurrentSaveId, m_UserMaskSize, m_UserMaskBytes)) {
                Mod.log.Info($"GrassSprites saved foliage mask for save id {m_CurrentSaveId} at {m_UserMaskSize}x{m_UserMaskSize} after successful game save.");
            }
        }

        public void OpenExportFolder() {
            if (GrassSpritesMaskStorage.OpenExportDirectory()) {
                Mod.log.Info($"GrassSprites opened export folder: {GrassSpritesMaskStorage.ExportDirectory}");
            }
        }

        public void ExportPaintedFoliageMask() {
            EnsureUserMask(resetToEmpty: false);
            if (m_UserMaskBytes == null || m_UserMaskSize <= 0) {
                Mod.log.Warn("GrassSprites could not export the foliage mask because the runtime mask is not ready yet.");
                return;
            }

            if (GrassSpritesMaskStorage.TryExport(m_UserMaskSize, m_UserMaskBytes, out var path)) {
                Mod.log.Info($"GrassSprites exported painted foliage mask to: {path}. Rename or share this file from the Exports folder if desired.");
            }
        }

        public void ImportPaintedFoliageMask() {
            EnsureUserMask(resetToEmpty: false);
            if (m_UserMaskBytes == null || m_UserMaskSize <= 0) {
                Mod.log.Warn("GrassSprites could not import a foliage mask because the runtime mask is not ready yet.");
                return;
            }

            var selectedExportFile = Mod.Settings?.SelectedExportMaskFile;
            if (!GrassSpritesMaskStorage.TryImportFromExportFile(selectedExportFile, m_UserMaskSize, out var importedMask, out var path)) {
                Mod.log.Warn($"GrassSprites could not import a foliage mask. Choose a .grassmask file from the Exported Mask to Import dropdown. Export folder: {GrassSpritesMaskStorage.ExportDirectory}");
                return;
            }

            m_PendingMaskAutoLoad = false;
            ApplyLoadedMask(importedMask, "import");
            Mod.log.Info($"GrassSprites imported painted foliage mask from: {path}. It is now in memory only and will be saved to the current city save GUID after the next successful city save.");
        }

        protected override void OnDestroy() {
            DeactivateBrushToolIfActive();

            if (m_TogglePanelAction != null) {
                m_TogglePanelAction.shouldBeEnabled = false;
                m_TogglePanelAction = null;
            }

            if (m_LoadGameSystem != null) {
                m_LoadGameSystem.onOnSaveGameLoaded -= OnSaveGameLoaded;
            }

            if (GameManager.instance != null) {
                GameManager.instance.onGameSaveLoad -= OnGameSaveLoad;
            }


            if (Instance == this) {
                Instance = null;
            }

            if (m_UserMaskSplatMap != null) {
                Object.Destroy(m_UserMaskSplatMap);
                m_UserMaskSplatMap = null;
            }

            if (m_DirtyPatchTexture != null) {
                Object.Destroy(m_DirtyPatchTexture);
                m_DirtyPatchTexture = null;
            }

            base.OnDestroy();
        }
    }
}
