using System;
using System.IO;
using System.Text;

namespace Aquarium.Online
{
    /// <summary>Atomic command journal. An explicit committed marker is a cleared journal.
    /// Failed replacement never deletes the live journal. Corruption is never auto-reset.</summary>
    public sealed class FilePendingCommandStore : IPendingCommandStore, IDisposable
    {
        public const int MaximumJournalBytes = 65536;
        private const string ClearedMarker = "{\"journalState\":\"clear\",\"version\":1}";
        public string Path { get; }
        private readonly object gate = new object();
        private readonly FileStream lease;
        private bool disposed;

        public FilePendingCommandStore(string path)
        {
            Path = path ?? throw new ArgumentNullException(nameof(path));
            var directory = System.IO.Path.GetDirectoryName(Path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            // Same identity/file may have only one live writer, including across processes.
            // The lock file itself is retained; OS closes release the lock after a crash.
            lease = new FileStream(Path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }

        public bool TryRead(out string contents)
        {
            lock (gate)
            {
                EnsureOpen();
                contents = null;
                if (!File.Exists(Path)) return false;
                if (new FileInfo(Path).Length > MaximumJournalBytes)
                    throw new InvalidDataException("Pending command journal exceeds the size limit.");
                contents = File.ReadAllText(Path, new UTF8Encoding(false, true));
                // Empty/truncated files are corruption, not evidence that a command settled.
                return contents != ClearedMarker;
            }
        }

        public void Write(string contents)
        {
            lock (gate)
            {
                EnsureOpen();
                if (contents == null) throw new ArgumentNullException(nameof(contents));
                var bytes = new UTF8Encoding(false, true).GetBytes(contents);
                if (bytes.Length > MaximumJournalBytes) throw new InvalidDataException("Pending command journal exceeds the size limit.");
                var temporary = Path + ".tmp";
                using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }
                if (File.Exists(Path)) File.Replace(temporary, Path, null);
                else File.Move(temporary, Path);
            }
        }

        public void Clear() { Write(ClearedMarker); }

        private void EnsureOpen()
        {
            if (disposed) throw new ObjectDisposedException(nameof(FilePendingCommandStore));
        }

        public void Dispose()
        {
            lock (gate)
            {
                if (disposed) return;
                disposed = true;
                lease.Dispose();
            }
        }
    }
}
