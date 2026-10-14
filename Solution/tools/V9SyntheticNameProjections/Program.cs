using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Jornada.Contracts;

if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: V9SyntheticNameProjections <observacoes.csv> <output.jsonl>");
    return 2;
}
var input = Path.GetFullPath(args[0]);
var output = Path.GetFullPath(args[1]);
Directory.CreateDirectory(Path.GetDirectoryName(output)!);
using var reader = new StreamReader(input, Encoding.UTF8);
using var writer = new StreamWriter(output, false, new UTF8Encoding(false));
var header = ParseCsvRecord(reader).ToArray();
var nameColumn = Array.IndexOf(header, "nome");
var motherColumn = Array.IndexOf(header, "nome_mae");
var idColumn = Array.IndexOf(header, "observacao_id");
if (nameColumn < 0 || motherColumn < 0 || idColumn < 0)
    throw new InvalidDataException("Expected observacao_id, nome and nome_mae");
int count = 0;
while (!reader.EndOfStream)
{
    var cells = ParseCsvRecord(reader).ToArray();
    if (cells.Length != header.Length)
        throw new InvalidDataException("CSV column count mismatch");
    var keys = BlockingProjectionKeyProjector.Project(cells[nameColumn], cells[motherColumn], null);
    string? Find(string feature) => keys.FirstOrDefault(k => k.Feature == feature)?.Value;
    var row = new
    {
        id = cells[idColumn],
        person_normalized = Find(BlockingFeatureNames.FullName),
        person_phonetic = Find(BlockingFeatureNames.FullNamePhoneticPtBr),
        mother_normalized = Find(BlockingFeatureNames.MotherFullName),
        mother_phonetic = Find(BlockingFeatureNames.MotherFullNamePhoneticPtBr),
        projector_version = BlockingProjectionKeyProjector.MethodVersion
    };
    writer.WriteLine(JsonSerializer.Serialize(row));
    count++;
}
Console.WriteLine($"Projected {count} synthetic observations with canonical normalized and phonetic keys.");
return 0;

static IEnumerable<string> ParseCsvRecord(StreamReader reader)
{
    var cells = new List<string>();
    var cell = new StringBuilder();
    bool quoted = false;
    while (true)
    {
        int next = reader.Read();
        if (next < 0)
        {
            if (quoted) throw new InvalidDataException("Unterminated quoted CSV field");
            if (cell.Length > 0 || cells.Count > 0) cells.Add(cell.ToString());
            return cells;
        }
        char ch = (char)next;
        if (ch == '"')
        {
            if (quoted && reader.Peek() == '"') { reader.Read(); cell.Append('"'); }
            else quoted = !quoted;
        }
        else if (ch == ',' && !quoted) { cells.Add(cell.ToString()); cell.Clear(); }
        else if ((ch == '\n' || ch == '\r') && !quoted)
        {
            if (ch == '\r' && reader.Peek() == '\n') reader.Read();
            cells.Add(cell.ToString());
            return cells;
        }
        else cell.Append(ch);
    }
}
