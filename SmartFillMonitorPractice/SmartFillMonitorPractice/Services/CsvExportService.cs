using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CsvHelper;

namespace SmartFillMonitorPractice.Services
{
    public class CsvExportService : IExportService
    {
        public async Task<string> ExportAsync<T>(IEnumerable<T> records, string filePath)
        {
            var data = records?.ToList() ?? new List<T>();
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await using var writer = new StreamWriter(filePath, false, new UTF8Encoding(true));
            await using var csv = new CsvWriter(writer, CultureInfo.InvariantCulture);
            await csv.WriteRecordsAsync(data);
            await writer.FlushAsync();
            return Path.GetFullPath(filePath);
        }
    }
}
