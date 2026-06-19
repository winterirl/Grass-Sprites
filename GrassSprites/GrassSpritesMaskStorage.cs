using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEngine;

namespace GrassSprites {
    /// <summary>
    /// Handles all filesystem persistence for painted foliage masks.
    ///
    /// Normal save/load is keyed by the game's save GUID and stored in the Masks
    /// directory. 
    /// 
    /// Import/export uses separate portable .grassmask files in the
    /// Exports directory and never changes the active save association by itself.
    ///
    /// Runtime editing only changes memory. A normal save writes the current mask
    /// to the current save GUID; importing a mask only becomes associated with the
    /// current city after the next successful city save.
    /// 
    /// This means we do end up with a mask for every save... 
    /// But these are so compressed it should be okay. Basically:
    ///     - 4k max size is 16 MB
    ///     - 8k max size is 64 MB
    ///     
    /// And that's a worst-case scenario. In practice it will typically be way less.
    /// </summary>
    internal static class GrassSpritesMaskStorage {
        private const int kMagic = 0x4D505347; // It's a magic number! We use this to make sure we're actually loading a .grassmask file
        private const int kVersion = 1; // it's the storage format version ~ this will probs never change unless I have a reason to do it
        private const byte kEncodingRaw = 0; 
        private const byte kEncodingRunlength = 1;

        public static string MaskDirectory => Path.Combine(Application.persistentDataPath, "ModsData", "GrassSprites", "Masks");
        public static string ExportDirectory => Path.Combine(Application.persistentDataPath, "ModsData", "GrassSprites", "Exports");

        /// <summary>
        /// Saves the current in-memory mask for a specific city save
        ///
        /// The save id is normalized to the plain 32-character GUID before being used
        /// as the filename. 
        /// 
        /// This keeps Save As branches seperate from each other.
        /// </summary> 
        public static bool TrySave(string saveId, int maskSize, byte[] maskBytes) {
            saveId = NormalizeSaveId(saveId);
            if (string.IsNullOrEmpty(saveId)) {
                return false;
            }

            return TrySaveToPath(GetMaskPath(saveId), saveId, maskSize, maskBytes, "save id " + saveId);
        }

        /// <summary>
        /// Loads the mask for the exact save GUID. Must be an exact match.
        /// </summary>
        public static bool TryLoad(string saveId, int expectedMaskSize, out byte[] maskBytes) {
            maskBytes = null;

            var canonicalSaveId = NormalizeSaveId(saveId);
            if (string.IsNullOrEmpty(canonicalSaveId) || expectedMaskSize <= 0) {
                return false;
            }

            var path = GetMaskPath(canonicalSaveId);
            Mod.log.Info($"GrassSprites exact mask path: {path}");
            if (TryLoadFromPath(path, expectedMaskSize, canonicalSaveId, out maskBytes, out var _)) {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Writes a portable copy of the current in-memory mask to the Exports folder.
        ///
        /// Exporting does not change the active save id and does not mark the mask as saved for the current city. 
        /// </summary>
        public static bool TryExport(int maskSize, byte[] maskBytes, out string path) {
            var fileName = "GrassSprites-export-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".grassmask";
            path = Path.Combine(ExportDirectory, fileName);
            return TrySaveToPath(path, "export", maskSize, maskBytes, "export " + fileName);
        }

        /// <summary>
        /// This pulls the names of files in the moddata Exports folder so the UI can show them in the dropdown.
        /// </summary>
        public static string[] GetExportMaskFileNames() {
            try {
                EnsureDirectory(ExportDirectory);
                return new DirectoryInfo(ExportDirectory)
                    .EnumerateFiles("*.grassmask", SearchOption.TopDirectoryOnly)
                    .OrderBy(file => file.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(file => file.Name)
                    .ToArray();
            }
            catch (Exception ex) {
                Mod.log.Warn($"GrassSprites could not enumerate export folder '{ExportDirectory}'. {ex.GetType().Name}: {ex.Message}");
                return Array.Empty<string>();
            }
        }

        /// <summary>
        /// Returns a version number for the export file dropdown.
        ///
        /// The settings UI can use this to refresh the dropdown when files in the folder change.
        /// </summary>
        public static int GetExportMaskListVersion() {
            unchecked {
                var hash = 17;
                try {
                    EnsureDirectory(ExportDirectory);
                    foreach (var file in new DirectoryInfo(ExportDirectory)
                        .EnumerateFiles("*.grassmask", SearchOption.TopDirectoryOnly)
                        .OrderBy(file => file.Name, StringComparer.OrdinalIgnoreCase)) {
                        hash = hash * 31 + StringComparer.OrdinalIgnoreCase.GetHashCode(file.Name);
                        hash = hash * 31 + file.Length.GetHashCode();
                        hash = hash * 31 + file.LastWriteTimeUtc.Ticks.GetHashCode();
                    }
                }
                catch {
                    return 0;
                }
                return hash;
            }
        }

        /// <summary>
        /// Loads a selected .grassmask file from the Exports folder into memory.
        ///
        /// The filename must be a file name, not a path. This keeps imports limited to the moddata export directory.
        /// 
        /// Importing does not write to the current save ~ that only happens after a real game save.
        /// </summary>
        public static bool TryImportFromExportFile(string exportFileName, int expectedMaskSize, out byte[] maskBytes, out string path) {
            maskBytes = null;
            path = null;

            if (expectedMaskSize <= 0) {
                return false;
            }

            if (string.IsNullOrWhiteSpace(exportFileName)) {
                Mod.log.Warn("GrassSprites import skipped because no export file is selected.");
                return false;
            }

            exportFileName = exportFileName.Trim();
            if (!string.Equals(Path.GetFileName(exportFileName), exportFileName, StringComparison.Ordinal) ||
                !exportFileName.EndsWith(".grassmask", StringComparison.OrdinalIgnoreCase)) {
                Mod.log.Warn($"GrassSprites import skipped because '{exportFileName}' is not a valid exported .grassmask filename.");
                return false;
            }

            EnsureDirectory(ExportDirectory);
            path = Path.Combine(ExportDirectory, exportFileName);
            return TryLoadFromPath(path, expectedMaskSize, null, out maskBytes, out var _);
        }

        public static bool OpenExportDirectory() {
            return OpenDirectory(ExportDirectory);
        }

        private static void EnsureDirectory(string directory) {
            if (!string.IsNullOrEmpty(directory)) {
                Directory.CreateDirectory(directory);
            }
        }

        private static bool OpenDirectory(string directory) {
            try {
                EnsureDirectory(directory);
                Process.Start(new ProcessStartInfo {
                    FileName = directory,
                    UseShellExecute = true
                });
                return true;
            }
            catch (Exception ex) {
                Mod.log.Warn($"GrassSprites could not open folder '{directory}'. {ex.GetType().Name}: {ex.Message}");
                return false;
            }
        }

        public static string GetMaskPath(string saveId) {
            return Path.Combine(MaskDirectory, SanitizeFileName(NormalizeSaveId(saveId)) + ".grassmask");
        }

        /// <summary>
        /// Extracts the 32-character GUID from CS2 save identifiers.
        ///
        /// Some game APIs expose a plain GUID, while others expose a larger string that contains the GUID inside it. 
        /// The storage layer only wants the plain GUID so all save/load paths use the same filename key.
        /// </summary>
        public static string NormalizeSaveId(string saveId) {
            if (string.IsNullOrEmpty(saveId)) {
                return null;
            }

            saveId = saveId.Trim();
            if (saveId.Length >= 32) {
                for (var start = 0; start <= saveId.Length - 32; start++) {
                    var allHex = true;
                    for (var i = 0; i < 32; i++) {
                        if (!IsHex(saveId[start + i])) {
                            allHex = false;
                            break;
                        }
                    }

                    if (allHex) {
                        return saveId.Substring(start, 32).ToLowerInvariant();
                    }
                }
            }

            return saveId.ToLowerInvariant();
        }

        /// <summary>
        /// Writes a .grassmask file to the requested path.
        ///
        /// The file is first written to a temporary path and then moved into place so a failed 
        /// or interrupted write doesn't leave a partially-written mask at the final filename.
        /// </summary>
        private static bool TrySaveToPath(string path, string fileSaveId, int maskSize, byte[] maskBytes, string logLabel) {
            if (string.IsNullOrEmpty(path) || maskSize <= 0 || maskBytes == null) {
                return false;
            }

            var expectedLength = maskSize * maskSize;
            if (maskBytes.Length != expectedLength) {
                Mod.log.Warn($"GrassSprites mask save skipped because data length {maskBytes.Length} did not match expected {expectedLength} for {maskSize}x{maskSize}.");
                return false;
            }

            try {
                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory)) {
                    Directory.CreateDirectory(directory);
                }

                var payload = BuildPayload(maskBytes, out var encoding);
                var checksum = ComputeFnv1a(maskBytes);
                var tempPath = path + ".tmp";

                using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
                using (var writer = new BinaryWriter(stream)) {
                    writer.Write(kMagic);
                    writer.Write(kVersion);
                    writer.Write(maskSize);
                    writer.Write(maskSize);
                    writer.Write(encoding);
                    writer.Write(fileSaveId ?? string.Empty);
                    writer.Write(maskBytes.Length);
                    writer.Write(payload.Length);
                    writer.Write(checksum);
                    writer.Write(payload);
                }

                if (File.Exists(path)) {
                    File.Delete(path);
                }
                File.Move(tempPath, path);
                return true;
            }
            catch (Exception ex) {
                Mod.log.Warn($"GrassSprites failed to save foliage mask for {logLabel}. {ex.GetType().Name}: {ex.Message}");
                return false;
            }
        }

        private static bool IsHex(char c) {
            return (c >= '0' && c <= '9') ||
                   (c >= 'a' && c <= 'f') ||
                   (c >= 'A' && c <= 'F');
        }

        private static string SanitizeFileName(string value) {
            if (string.IsNullOrEmpty(value)) {
                return string.Empty;
            }

            var invalid = Path.GetInvalidFileNameChars();
            var chars = value.ToCharArray();
            for (var i = 0; i < chars.Length; i++) {
                for (var j = 0; j < invalid.Length; j++) {
                    if (chars[i] == invalid[j]) {
                        chars[i] = '_';
                        break;
                    }
                }
            }
            return new string(chars);
        }

        /// <summary>
        /// Reads, validates, and decodes a .grassmask file.
        ///
        /// Validation checks the magic number, version, dimensions, payload length, encoding,
        /// decoded length, and checksum before returning mask bytes to the runtime.
        /// </summary>
        private static bool TryLoadFromPath(string path, int expectedMaskSize, string requireSaveId, out byte[] maskBytes, out string fileSaveId) {
            maskBytes = null;
            fileSaveId = null;

            if (string.IsNullOrEmpty(path) || expectedMaskSize <= 0 || !File.Exists(path)) {
                Mod.log.Info($"GrassSprites mask file was not found: {path}");
                return false;
            }

            try {
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var reader = new BinaryReader(stream)) {
                    var magic = reader.ReadInt32();
                    var version = reader.ReadInt32();

                    if (magic != kMagic || version != kVersion) {
                        Mod.log.Warn($"GrassSprites ignored unsupported mask file '{path}'. magic={magic}, version={version}");
                        return false;
                    }

                    var width = reader.ReadInt32();
                    var height = reader.ReadInt32();
                    var encoding = reader.ReadByte();
                    fileSaveId = reader.ReadString();
                    var uncompressedLength = reader.ReadInt32();
                    var payloadLength = reader.ReadInt32();
                    var expectedChecksum = reader.ReadUInt32();

                    if (width != expectedMaskSize || height != expectedMaskSize) {
                        Mod.log.Warn($"GrassSprites ignored mask '{path}' because it is {width}x{height}, but the current setting is {expectedMaskSize}x{expectedMaskSize}.");
                        return false;
                    }

                    var expectedLength = expectedMaskSize * expectedMaskSize;
                    if (uncompressedLength != expectedLength || payloadLength < 0 || payloadLength > stream.Length - stream.Position) {
                        Mod.log.Warn($"GrassSprites ignored corrupt mask header '{path}'.");
                        return false;
                    }

                    if (!string.IsNullOrEmpty(requireSaveId) && !string.IsNullOrEmpty(fileSaveId) && !string.Equals(fileSaveId, requireSaveId, StringComparison.OrdinalIgnoreCase)) {
                        // Older builds could write a correct filename with a different internal header id
                        // so... treat the header id as advisory instead of rejecting an otherwise valid exact-path load.
                        // I should probs remove this at some point, the public will never experience this issue.
                        Mod.log.Warn($"GrassSprites mask file header id differed from exact filename key. Filename key={requireSaveId}, file header={fileSaveId}. Loading by filename key.");
                    }

                    var payload = reader.ReadBytes(payloadLength);
                    byte[] decoded;

                    if (encoding == kEncodingRaw) {
                        decoded = payload;
                    }
                    else if (encoding == kEncodingRunlength) {
                        decoded = DecodeRunlength(payload, uncompressedLength);
                    }
                    else {
                        Mod.log.Warn($"GrassSprites ignored mask '{path}' because encoding {encoding} is unsupported.");
                        return false;
                    }

                    if (decoded == null || decoded.Length != expectedLength) {
                        Mod.log.Warn($"GrassSprites ignored corrupt mask payload '{path}'.");
                        return false;
                    }

                    var actualChecksum = ComputeFnv1a(decoded);
                    if (actualChecksum != expectedChecksum) {
                        Mod.log.Warn($"GrassSprites ignored mask '{path}' because its checksum did not match.");
                        return false;
                    }

                    maskBytes = decoded;
                    return true;
                }
            }
            catch (Exception ex) {
                Mod.log.Warn($"GrassSprites failed to load foliage mask '{path}'. {ex.GetType().Name}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Chooses the smaller on-disk representation for the mask.
        ///
        /// Painted masks often contain long runs of identical values, so RLE can make
        /// exports and saves much smaller. If RLE would not help, the raw bytes are
        /// stored instead.
        /// </summary>
        private static byte[] BuildPayload(byte[] maskBytes, out byte encoding) {
            var rle = EncodeRunlength(maskBytes, maskBytes.Length);
            if (rle != null && rle.Length < maskBytes.Length) {
                encoding = kEncodingRunlength;
                return rle;
            }

            var raw = new byte[maskBytes.Length];
            Buffer.BlockCopy(maskBytes, 0, raw, 0, maskBytes.Length);
            encoding = kEncodingRaw;
            return raw;
        }

        /// <summary>
        /// Encodes the mask as value/count pairs.
        ///
        /// Returns null as soon as the encoded stream grows to the raw byte length,
        /// letting the caller fall back to raw storage without wasting more work.
        /// </summary>
        private static byte[] EncodeRunlength(byte[] data, int rawLengthLimit) {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream)) {
                var i = 0;
                while (i < data.Length) {
                    var value = data[i];
                    var count = 1;
                    i++;

                    while (i < data.Length && data[i] == value && count < int.MaxValue) {
                        count++;
                        i++;
                    }

                    writer.Write(value);
                    writer.Write(count);

                    // If RLE is already bigger than raw, stop early and write raw instead.
                    if (stream.Length >= rawLengthLimit) {
                        return null;
                    }
                }

                return stream.ToArray();
            }
        }

        /// <summary>
        /// Decodes value/count pairs back into the full mask byte array.
        ///
        /// Invalid counts or payloads that do not exactly fill the expected output length are treated as corrupt data.
        /// </summary>
        private static byte[] DecodeRunlength(byte[] payload, int expectedLength) {
            var result = new byte[expectedLength];
            var offset = 0;

            using (var stream = new MemoryStream(payload))
            using (var reader = new BinaryReader(stream)) {
                while (stream.Position < stream.Length) {
                    var value = reader.ReadByte();
                    var count = reader.ReadInt32();
                    if (count < 0 || offset + count > expectedLength) {
                        return null;
                    }

                    for (var i = 0; i < count; i++) {
                        result[offset + i] = value;
                    }
                    offset += count;
                }
            }

            return offset == expectedLength ? result : null;
        }

        /// <summary>
        /// Computes a small checksum for corruption detection.
        ///
        /// Not cryopographic or meant to be a security thing at all
        /// </summary>
        private static uint ComputeFnv1a(byte[] data) {
            unchecked {
                const uint offset = 2166136261;
                const uint prime = 16777619;
                var hash = offset;
                for (var i = 0; i < data.Length; i++) {
                    hash ^= data[i];
                    hash *= prime;
                }
                return hash;
            }
        }
    }
}
