using System.Globalization;
using System.Text;
using AmarTools.Voting.Models;

namespace AmarTools.Voting.Services
{
    /// <summary>Builds the downloadable results file shared by the owner and admin Results pages.</summary>
    public static class ResultsCsv
    {
        public static byte[] Build(VotingProgram program, IReadOnlyList<CandidateResultViewModel> results)
        {
            var total = results.Sum(r => r.VoteCount);
            var sb = new StringBuilder();

            sb.AppendLine("Rank,Candidate Code,Candidate Name,Votes,Percentage");
            for (int i = 0; i < results.Count; i++)
            {
                var r = results[i];
                var percent = total == 0 ? 0 : 100.0 * r.VoteCount / total;
                sb.Append(i + 1).Append(',')
                  .Append(Field(r.Candidate.CandidateCode)).Append(',')
                  .Append(Field(r.Candidate.Name)).Append(',')
                  .Append(r.VoteCount).Append(',')
                  .AppendLine(percent.ToString("0.00", CultureInfo.InvariantCulture));
            }
            sb.Append(",,Total,").Append(total).AppendLine(",100.00");

            // UTF-8 BOM so Excel opens non-ASCII names (e.g. Bangla) correctly.
            return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
        }

        public static string FileName(VotingProgram program)
        {
            var safe = new string(program.ProgramName
                .Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray()).Trim('-');
            return $"results-{(safe.Length == 0 ? program.Id.ToString() : safe)}.csv";
        }

        /// <summary>
        /// Quotes a value for CSV and neutralises spreadsheet formulas: candidate names are
        /// typed by program owners, and a name like "=HYPERLINK(...)" would otherwise run
        /// when the file is opened in Excel (CSV injection).
        /// </summary>
        internal static string Field(string? value)
        {
            value ??= string.Empty;
            if (value.Length > 0 && "=+-@\t\r".Contains(value[0]))
                value = "'" + value;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
    }
}
