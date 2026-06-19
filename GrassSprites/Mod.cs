using Colossal.IO.AssetDatabase;
using Colossal.Logging;
using Game;
using Game.Modding;
using Game.SceneFlow;
using System.IO;

namespace GrassSprites {
    public class Mod : IMod {
        public static ILog log = LogManager.GetLogger($"{nameof(GrassSprites)}.{nameof(Mod)}").SetShowsErrorsInUI(false);
        public static Setting Settings { get; private set; }
        public static Mod Instance { get; private set; }
        public const string kTogglePanelActionName = "ToggleBrushPanel";
        public static string ModDirectory { get; private set; }
        private Setting m_Setting;

        public void OnLoad(UpdateSystem updateSystem) {
            log.Info(nameof(OnLoad));
            Instance = this;

            if (GameManager.instance.modManager.TryGetExecutableAsset(this, out var asset)) {
                log.Info($"Current mod asset at {asset.path}");
                ModDirectory = Path.GetDirectoryName(asset.path);
                log.Info($"Current mod directory at {ModDirectory}");
            }

            m_Setting = new Setting(this);
            m_Setting.RegisterInOptionsUI();
            GameManager.instance.localizationManager.AddSource("en-US", new LocaleEN(m_Setting));

            AssetDatabase.global.LoadSettings(nameof(GrassSprites), m_Setting, new Setting(this));

            m_Setting.RegisterKeyBindings();
            m_Setting.BrushToolActive = false;
            m_Setting.BrushPanelVisible = false;
            Settings = m_Setting;

            updateSystem.UpdateAt<FoliageBrushToolSystem>(SystemUpdatePhase.ToolUpdate);
            updateSystem.UpdateAt<GrassSpritesRuntime>(SystemUpdatePhase.Rendering);
            updateSystem.UpdateAt<GrassSpritesUISystem>(SystemUpdatePhase.UIUpdate);
        }

        public void OnDispose() {
            log.Info(nameof(OnDispose));
            if (m_Setting != null) {
                m_Setting.UnregisterInOptionsUI();
                m_Setting = null;
                Settings = null;
            }

            Instance = null;
            ModDirectory = null;
        }
    }
}
