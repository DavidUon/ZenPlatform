using System;

namespace ZenPlatform.SessionManager
{
    public sealed class SessionManagerRuntimeState
    {
        public EntryTriggerRuntimeState EntryTrigger { get; set; } = new();
        public SameDirectionBlockRuntimeState SameDirectionBlock { get; set; } = new();
        public AutoRolloverRuntimeState AutoRollover { get; set; } = new();

        public void CopyFrom(SessionManagerRuntimeState? source)
        {
            if (source == null)
            {
                ResetForStrategyStart();
                return;
            }

            EntryTrigger.CopyFrom(source.EntryTrigger);
            SameDirectionBlock.CopyFrom(source.SameDirectionBlock);
            AutoRollover.CopyFrom(source.AutoRollover);
        }

        public void ResetForStrategyStart()
        {
            EntryTrigger.CopyFrom(null);
            SameDirectionBlock.CopyFrom(null);
            AutoRollover.CopyFrom(null);
        }
    }

    public sealed class EntryTriggerRuntimeState
    {
        // M1/M3 base state
        public bool M1WaitShortAfterUpBreak { get; set; }
        public bool M1WaitLongAfterDownBreak { get; set; }

        // M2 state
        public bool M2WaitLongAfterUpTouch { get; set; }
        public bool M2WaitShortAfterDownTouch { get; set; }
        public bool M2HasLastTickPrice { get; set; }
        public decimal M2LastTickPrice { get; set; }

        public void CopyFrom(EntryTriggerRuntimeState? source)
        {
            if (source == null)
            {
                M1WaitShortAfterUpBreak = false;
                M1WaitLongAfterDownBreak = false;
                M2WaitLongAfterUpTouch = false;
                M2WaitShortAfterDownTouch = false;
                M2HasLastTickPrice = false;
                M2LastTickPrice = 0m;
                return;
            }

            M1WaitShortAfterUpBreak = source.M1WaitShortAfterUpBreak;
            M1WaitLongAfterDownBreak = source.M1WaitLongAfterDownBreak;
            M2WaitLongAfterUpTouch = source.M2WaitLongAfterUpTouch;
            M2WaitShortAfterDownTouch = source.M2WaitShortAfterDownTouch;
            M2HasLastTickPrice = source.M2HasLastTickPrice;
            M2LastTickPrice = source.M2LastTickPrice;
        }
    }

    public sealed class SameDirectionBlockRuntimeState
    {
        public DateTime? LastAllowedEntryTime { get; set; }
        public int LastAllowedEntrySide { get; set; } // 1=buy, -1=sell
        public decimal LastAllowedEntryPrice { get; set; }

        public void CopyFrom(SameDirectionBlockRuntimeState? source)
        {
            if (source == null)
            {
                LastAllowedEntryTime = null;
                LastAllowedEntrySide = 0;
                LastAllowedEntryPrice = 0m;
                return;
            }

            LastAllowedEntryTime = source.LastAllowedEntryTime;
            LastAllowedEntrySide = source.LastAllowedEntrySide;
            LastAllowedEntryPrice = source.LastAllowedEntryPrice;
        }
    }

    public sealed class AutoRolloverRuntimeState
    {
        public DateTime? LastExecutedDate { get; set; }

        public void CopyFrom(AutoRolloverRuntimeState? source)
        {
            if (source == null)
            {
                LastExecutedDate = null;
                return;
            }

            LastExecutedDate = source.LastExecutedDate;
        }
    }
}
