using System;
using System.IO;
using System.Threading.Tasks;

namespace Pipelines.Sockets.Unofficial.Tests
{
    public static class TestReaderTests
    {
        public static async ValueTask<string> MeasureAndTime(TextReader reader)
        {

            int lineCount = 0, nonEmptyLineCount = 0;
            long charCount = 0;

            try
            {
                string line;
                while ((line = await reader.ReadLineAsync()) != null)
                {
                    charCount += line.Length;
                    if (!string.IsNullOrWhiteSpace(line)) nonEmptyLineCount++;
                    lineCount++;
                }
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
            return $"Lines: {lineCount} ({nonEmptyLineCount} non-empty, {charCount} characters)";
        }
    }
}
