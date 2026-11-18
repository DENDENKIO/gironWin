using gironWin.Shared;
using System;
using System.Collections.Generic;
using System.IO;

namespace gironWin.LogReader
{
    public partial class MainWindow
    {
        internal static MainWindow FromValidatedPayload(LogReaderPayload payload)
        {
            ArgumentNullException.ThrowIfNull(payload);
            if (payload.Records == null || payload.Records.Count == 0)
                throw new InvalidDataException("表示対象のログがありません。");

            // The empty path bypasses the legacy constructor's second read
            // and unconditional deletion. Loaded renders this exact list.
            var window = new MainWindow(string.Empty);
            window._records = new List<TransferRecord>(payload.Records);
            window._currentIndex = Math.Clamp(payload.StartIndex, 0, window._records.Count - 1);
            window._pipeId = payload.PipeId ?? string.Empty;
            return window;
        }
    }
}
