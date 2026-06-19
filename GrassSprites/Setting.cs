using Colossal;
using Colossal.IO.AssetDatabase;
using Game.Input;
using Game.Modding;
using Game.Settings;
using Game.UI.Localization;
using Game.UI.Widgets;
using System.Collections.Generic;
using System.Linq;

namespace GrassSprites {
    [FileLocation(nameof(GrassSprites))]
    [SettingsUIGroupOrder(kSettingsGroup, kImportExportGroup)]
    [SettingsUIShowGroupName(kSettingsGroup, kImportExportGroup)]
    [SettingsUIKeyboardAction(Mod.kTogglePanelActionName, ActionType.Button, usages: new[] { "Game" })]
    public class Setting : ModSetting {
        public const string kSection = "Main";
        public const string kSettingsGroup = "GrassSpritesSettings";
        public const string kImportExportGroup = "ExportImport";

        public Setting(IMod mod) : base(mod) { }

        [SettingsUISection(kSection, kSettingsGroup)]
        public bool Enabled { get; set; }

        [SettingsUISection(kSection, kSettingsGroup)]
        [SettingsUIKeyboardBinding(BindingKeyboard.B, Mod.kTogglePanelActionName, ctrl: true)]
        public ProxyBinding TogglePanelBinding { get; set; } = new ProxyBinding();

        [SettingsUISection(kSection, kSettingsGroup)]
        public MaskResolutionOption MaskResolution { get; set; } = MaskResolutionOption.High8192;

        public int UserMaskResolution => MaskResolution == MaskResolutionOption.High8192 ? 8192 : 4096;

        // This is a destructive action.
        // This only clears the current in-memory foliage mas.
        // it is not written to disk until the next city save.
        [SettingsUIButton]
        [SettingsUISection(kSection, kSettingsGroup)]
        public bool ClearAllPaintedFoliage {
            set {
                if (value) {
                    GrassSpritesRuntime.Instance?.ClearMask();
                }
            }
        }

        // Export creates a portable file for sharing
        // import reads an export file into memory
        // This isn't persistant until the next city save.
        [SettingsUIButton]
        [SettingsUISection(kSection, kImportExportGroup)]
        public bool ExportPaintedFoliageMask {
            set {
                if (value) {
                    GrassSpritesRuntime.Instance?.ExportPaintedFoliageMask();
                }
            }
        }

        [SettingsUIButton]
        [SettingsUISection(kSection, kImportExportGroup)]
        public bool OpenExportFolder {
            set {
                if (value) {
                    GrassSpritesRuntime.Instance?.OpenExportFolder();
                }
            }
        }

        [SettingsUIDropdown(typeof(Setting), nameof(GetExportMaskFileItems))]
        [SettingsUIValueVersion(typeof(Setting), nameof(GetExportMaskFileListVersion))]
        [SettingsUISection(kSection, kImportExportGroup)]
        public string SelectedExportMaskFile { get; set; } = string.Empty;

        public int GetExportMaskFileListVersion() {
            return GrassSpritesMaskStorage.GetExportMaskListVersion();
        }

        public DropdownItem<string>[] GetExportMaskFileItems() {
            var files = GrassSpritesMaskStorage.GetExportMaskFileNames();
            if (files == null || files.Length == 0) {
                return new[] {
                    new DropdownItem<string> {
                        value = string.Empty,
                        displayName = LocalizedString.Value("No exported masks found")
                    }
                };
            }

            return files
                .Select(file => new DropdownItem<string> {
                    value = file,
                    displayName = LocalizedString.Value(file)
                })
                .ToArray();
        }

        [SettingsUIButton]
        [SettingsUISection(kSection, kImportExportGroup)]
        public bool ImportPaintedFoliageMask {
            set {
                if (value) {
                    GrassSpritesRuntime.Instance?.ImportPaintedFoliageMask();
                }
            }
        }

        // Runtime brush state.
        // These are not exposed in the options UI ~ the in-game panel owns these.
        [SettingsUIHidden]
        public bool BrushToolActive { get; set; } = false;
        [SettingsUIHidden]
        public bool BrushPanelVisible { get; set; } = false;
        [SettingsUIHidden]
        public bool PrecisionDabMode { get; set; } = false;
        [SettingsUIHidden]
        public float PaintRadiusMeters { get; set; } = 40f;

        public override void SetDefaults() {
            Enabled = true;
            TogglePanelBinding = new ProxyBinding();
            MaskResolution = MaskResolutionOption.High8192;
            BrushToolActive = false;
            BrushPanelVisible = false;
            PrecisionDabMode = false;
            PaintRadiusMeters = 40f;
            SelectedExportMaskFile = string.Empty;
        }

        public enum MaskResolutionOption {
            Standard4096,
            High8192,
        }
    }

    /// <summary>
    /// Localization duh.
    /// 
    /// I only know English well enough to do this. 
    /// 
    /// Maybe if I get some translation help I should break this into a new file.
    /// </summary>
    public class LocaleEN : IDictionarySource {
        private readonly Setting m_Setting;
        public LocaleEN(Setting setting) {
            m_Setting = setting;
        }
        public IEnumerable<KeyValuePair<string, string>> ReadEntries(IList<IDictionaryEntryError> errors, Dictionary<string, int> indexCounts) {
            return new Dictionary<string, string>
            {
                { m_Setting.GetSettingsLocaleID(), "Grass Sprites" },
                { m_Setting.GetOptionTabLocaleID(Setting.kSection), "Main" },

                { m_Setting.GetOptionGroupLocaleID(Setting.kSettingsGroup), "Grass Sprites Settings" },
                { m_Setting.GetOptionGroupLocaleID(Setting.kImportExportGroup), "Export / Import" },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.Enabled)), "Enable Grass Sprites" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.Enabled)), "Enables vanilla FoliageVFX system with a foliage mask." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.TogglePanelBinding)), "Set Hotkey" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.TogglePanelBinding)), "Hotkey used to open/close the brush panel. Ctrl+B is the default." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.MaskResolution)), "Mask Resolution" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.MaskResolution)), "Resolution of the foliage mask texture. 8k allows finer details, but 4k may perform better on lower-end systems. The grass sprites themselves may also have an impact on performance. Changing this value will clear all painted foliage." },
                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.ClearAllPaintedFoliage)), "Clear All Painted Foliage" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.ClearAllPaintedFoliage)), "Removes all painted foliage from the map." },
                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.ExportPaintedFoliageMask)), "Export Foliage Mask" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.ExportPaintedFoliageMask)), "Exports the current painted foliage to a shareable .grassmask file in the ../ModData/GrassSprites/Exports directory." },
                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.OpenExportFolder)), "Open Export Folder" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.OpenExportFolder)), "Opens the ../ModData/GrassSprites/Exports directory." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.SelectedExportMaskFile)), "Select Foliage Mask" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.SelectedExportMaskFile)), "Select a .grassmask file to import from the ../ModData/GrassSprites/Exports directory." },
                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.ImportPaintedFoliageMask)), "Import Foliage Mask" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.ImportPaintedFoliageMask)), "Loads the selected .grassmask file. This will not overwrite your save's existing grassmask until you save the map." },

                { m_Setting.GetBindingKeyLocaleID(Mod.kTogglePanelActionName), "Hotkey" },
                { m_Setting.GetBindingMapLocaleID(), "Grass Sprites" },

                { m_Setting.GetEnumValueLocaleID(Setting.MaskResolutionOption.Standard4096), "4096 Performance" },
                { m_Setting.GetEnumValueLocaleID(Setting.MaskResolutionOption.High8192), "8192 Precision" },
            };
        }

        public void Unload() { }
    }
}
