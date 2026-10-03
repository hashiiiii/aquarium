using System;
using Aquarium.Core;

namespace Aquarium.Runtime
{
    public sealed class RecoveryResult
    {
        public LoadResult load;
        public bool savingAllowed = true;
        public string notice;
        public string warning;
    }

    /// <summary>Recover a backup even when the primary vanished during a previous interrupted recovery.</summary>
    public static class AquariumRecovery
    {
        public static RecoveryResult Load(AquariumFileStore store, IAquariumSerializer serializer, DateTime utcNow)
        {
            var result = new RecoveryResult { load = new AquariumSaveService(store, serializer).Load(utcNow) };
            if (result.load.restored) return result;
            var unreadable = !string.IsNullOrEmpty(result.load.warning);
            if (unreadable)
            {
                try { store.PreserveUnreadable(); }
                catch (Exception)
                {
                    result.savingAllowed = false;
                    result.warning = "Save recovery blocked: this session will not overwrite your saved reef.";
                    return result;
                }
            }
            var backup = new AquariumSaveService(new AquariumFileStore(store.BackupPath), serializer).Load(utcNow);
            if (backup.restored)
            {
                result.load = backup;
                result.notice = unreadable
                    ? "Recovered your last backup. The unreadable save was preserved."
                    : "Recovered your last backup after an interrupted save.";
            }
            else if (unreadable)
                result.notice = "Started a new reef. Your unreadable save was preserved for recovery.";
            return result;
        }
    }
}
