using ParquetSharp;

namespace SimCoach.Reference.Tests;

/// <summary>
/// Writes a structurally-valid parquet with TWO row groups so <c>ReferenceParquetCodec.Read</c> rejects it
/// with <see cref="InvalidDataException"/> ("a reference must be exactly one row group"). This is the
/// realistic corruption class the alien tier-1 fault isolation (M3) must survive — a third-party import
/// pointed at a multi-lap file. The schema is deliberately trivial: the row-group count is checked before
/// any column is read, so no reference schema is needed.
/// </summary>
internal static class CorruptReferenceParquet
{
    public static void WriteMultiRowGroup(string path)
    {
        Column[] columns = [new Column<int>("x")];
        using var writer = new ParquetFileWriter(path, columns);
        for (int g = 0; g < 2; g++)
        {
            using RowGroupWriter rowGroup = writer.AppendRowGroup();
            using LogicalColumnWriter<int> column = rowGroup.NextColumn().LogicalWriter<int>();
            column.WriteBatch(new[] { g });
        }

        writer.Close();
    }
}
