using System;
using System.IO;
using System.Text;
using Aquarium.Core;

namespace Aquarium.Runtime
{
    /// <summary>Crash-safe same-directory replacement. Never falls back to deleting the live save.</summary>
    public sealed class AquariumFileStore : IAquariumSaveStore
    {
        public string Path { get; }
        public string BackupPath => Path + ".bak";

        public AquariumFileStore(string path)
        {
            Path = path ?? throw new ArgumentNullException(nameof(path));
        }

        public bool TryRead(out string contents)
        {
            contents = null;
            if (!File.Exists(Path)) return false;
            // Bound reads before allocating or parsing untrusted/corrupt data.
            if (new FileInfo(Path).Length > AquariumSaveService.MaximumSaveCharacters * 4L)
                throw new InvalidDataException("Aquarium save exceeds the size limit.");
            contents = File.ReadAllText(Path, Encoding.UTF8);
            return true;
        }

        public void Write(string contents)
        {
            var directory = System.IO.Path.GetDirectoryName(Path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            var temporary = Path + ".tmp";
            using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var bytes = new UTF8Encoding(false).GetBytes(contents);
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }
            if (File.Exists(Path))
                File.Replace(temporary, Path, BackupPath, true);
            else
                File.Move(temporary, Path);
        }

        /// <summary>Preserve unreadable/newer saves rather than silently destroying progress.</summary>
        public string PreserveUnreadable()
        {
            if (!File.Exists(Path)) return null;
            var preserved = Path + ".preserved-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmss") + "-" + Guid.NewGuid().ToString("N");
            File.Move(Path, preserved);
            return preserved;
        }
    }
}
