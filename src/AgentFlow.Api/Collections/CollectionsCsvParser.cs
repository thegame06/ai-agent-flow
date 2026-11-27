using System.Globalization;
using System.Text;

namespace AgentFlow.Api.Collections;

public sealed record CollectionsCsvParseResult(
    IReadOnlyList<(int RowNumber, CollectionLoanRow Row)> Rows,
    IReadOnlyList<CollectionImportError> Errors,
    int TotalRows);

/// <summary>
/// Parses a lender portfolio CSV: one row per loan. Accepts English or Spanish column names,
/// "," ";" or tab delimiters (auto-detected) and quoted fields. Row numbers in errors follow
/// spreadsheet numbering (header = row 1).
/// </summary>
public static class CollectionsCsvParser
{
    public static readonly string[] TemplateColumns =
    [
        "customer_id", "full_name", "phone", "email",
        "loan_id", "product", "currency", "original_amount", "disbursed_at", "payment_frequency",
        "total_balance", "overdue_principal", "overdue_interest", "overdue_penalties", "overdue_fees",
        "days_past_due", "next_payment_date", "next_payment_amount", "last_payment_date", "last_payment_amount", "status"
    ];

    private static readonly Dictionary<string, string> Aliases = BuildAliases(new Dictionary<string, string[]>
    {
        ["customer_id"] = ["cliente_id", "id_cliente", "customer"],
        ["full_name"] = ["nombre", "nombre_completo", "name"],
        ["phone"] = ["telefono", "celular", "movil", "phone_number"],
        ["email"] = ["correo", "correo_electronico"],
        ["loan_id"] = ["prestamo_id", "id_prestamo", "credito_id", "id_credito", "loan"],
        ["product"] = ["producto"],
        ["currency"] = ["moneda"],
        ["original_amount"] = ["monto_original", "monto_otorgado"],
        ["disbursed_at"] = ["fecha_desembolso", "fecha_disposicion"],
        ["payment_frequency"] = ["frecuencia_pago", "frecuencia"],
        ["total_balance"] = ["saldo_total", "saldo"],
        ["overdue_principal"] = ["capital_vencido"],
        ["overdue_interest"] = ["interes_vencido"],
        ["overdue_penalties"] = ["moratorios", "interes_moratorio"],
        ["overdue_fees"] = ["comisiones", "cargos"],
        ["overdue_balance"] = ["saldo_vencido", "monto_vencido"],
        ["days_past_due"] = ["dias_atraso", "dias_de_atraso", "dpd"],
        ["oldest_due_date"] = ["fecha_vencimiento_mas_antigua", "fecha_primer_vencimiento"],
        ["next_payment_date"] = ["fecha_proximo_pago"],
        ["next_payment_amount"] = ["monto_proximo_pago"],
        ["last_payment_date"] = ["fecha_ultimo_pago"],
        ["last_payment_amount"] = ["monto_ultimo_pago"],
        ["status"] = ["estado", "estatus"]
    });

    private static readonly string[] DateFormats =
        ["yyyy-MM-dd", "yyyy/MM/dd", "dd/MM/yyyy", "d/M/yyyy", "dd-MM-yyyy", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-ddTHH:mm:ssK"];

    public static CollectionsCsvParseResult Parse(string content)
    {
        var errors = new List<CollectionImportError>();
        var rows = new List<(int, CollectionLoanRow)>();
        content = content.TrimStart('﻿');
        var delimiter = DetectDelimiter(content);
        var records = ReadRecords(content, delimiter);
        if (records.Count == 0)
        {
            errors.Add(new CollectionImportError(1, "file", "El archivo está vacío."));
            return new CollectionsCsvParseResult(rows, errors, 0);
        }

        var header = records[0].Select(NormalizeHeader).ToList();
        var index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < header.Count; i++)
        {
            var canonical = Aliases.GetValueOrDefault(header[i], header[i]);
            index.TryAdd(canonical, i);
        }

        foreach (var required in new[] { "loan_id", "total_balance" })
        {
            if (!index.ContainsKey(required))
                errors.Add(new CollectionImportError(1, required, $"Falta la columna obligatoria '{required}'."));
        }
        if (!index.ContainsKey("customer_id") && !index.ContainsKey("phone"))
            errors.Add(new CollectionImportError(1, "customer_id", "Se necesita la columna 'customer_id' o 'phone' para identificar al cliente."));
        if (errors.Count > 0) return new CollectionsCsvParseResult(rows, errors, Math.Max(0, records.Count - 1));

        var decimalComma = delimiter == ';';
        var dataRows = 0;
        for (var r = 1; r < records.Count; r++)
        {
            var record = records[r];
            if (record.All(string.IsNullOrWhiteSpace)) continue;
            dataRows++;
            var rowNumber = r + 1;
            var rowErrors = new List<CollectionImportError>();

            string? Text(string column) =>
                index.TryGetValue(column, out var i) && i < record.Count && !string.IsNullOrWhiteSpace(record[i]) ? record[i].Trim() : null;

            decimal? Money(string column)
            {
                var raw = Text(column);
                if (raw is null) return null;
                if (TryParseDecimal(raw, decimalComma, out var value)) return value;
                rowErrors.Add(new CollectionImportError(rowNumber, column, $"'{raw}' no es un monto válido."));
                return null;
            }

            DateTimeOffset? Date(string column)
            {
                var raw = Text(column);
                if (raw is null) return null;
                if (DateTime.TryParseExact(raw, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var date))
                    return new DateTimeOffset(DateTime.SpecifyKind(date.Date, DateTimeKind.Utc));
                rowErrors.Add(new CollectionImportError(rowNumber, column, $"'{raw}' no es una fecha válida (use AAAA-MM-DD o DD/MM/AAAA)."));
                return null;
            }

            int? Integer(string column)
            {
                var raw = Text(column);
                if (raw is null) return null;
                if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)) return value;
                rowErrors.Add(new CollectionImportError(rowNumber, column, $"'{raw}' no es un número entero."));
                return null;
            }

            var row = new CollectionLoanRow
            {
                CustomerId = Text("customer_id"),
                FullName = Text("full_name"),
                Phone = Text("phone"),
                Email = Text("email"),
                LoanId = Text("loan_id"),
                Product = Text("product"),
                Currency = Text("currency"),
                OriginalAmount = Money("original_amount"),
                DisbursedAt = Date("disbursed_at"),
                PaymentFrequency = Text("payment_frequency"),
                TotalBalance = Money("total_balance"),
                OverduePrincipal = Money("overdue_principal"),
                OverdueInterest = Money("overdue_interest"),
                OverduePenalties = Money("overdue_penalties"),
                OverdueFees = Money("overdue_fees"),
                OverdueBalance = Money("overdue_balance"),
                DaysPastDue = Integer("days_past_due"),
                OldestDueDate = Date("oldest_due_date"),
                NextPaymentDate = Date("next_payment_date"),
                NextPaymentAmount = Money("next_payment_amount"),
                LastPaymentDate = Date("last_payment_date"),
                LastPaymentAmount = Money("last_payment_amount"),
                Status = Text("status")
            };

            if (rowErrors.Count > 0) errors.AddRange(rowErrors);
            else rows.Add((rowNumber, row));
        }

        return new CollectionsCsvParseResult(rows, errors, dataRows);
    }

    /// <summary>A header line plus two example rows (one customer with two loans).</summary>
    public static string BuildTemplate()
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(',', TemplateColumns));
        sb.AppendLine("CLI-001,Ana López,+5215550000001,ana@example.com,PR-1001,Personal,MXN,20000,2026-01-15,quincenal,12500,1500,320,80,50,18,2026-12-05,1800,2026-10-31,1800,activo");
        sb.AppendLine("CLI-001,Ana López,+5215550000001,ana@example.com,PR-1002,Nómina,MXN,8000,2026-06-01,mensual,5200,0,0,0,0,0,2026-12-15,950,2026-11-15,950,activo");
        return sb.ToString();
    }

    internal static bool TryParseDecimal(string raw, bool decimalComma, out decimal value)
    {
        var cleaned = raw.Replace("$", string.Empty).Replace(" ", string.Empty).Trim();
        if (decimalComma && cleaned.Contains(',') && !cleaned.Contains('.'))
            cleaned = cleaned.Replace(',', '.');
        else
            cleaned = cleaned.Replace(",", string.Empty); // thousands separator
        return decimal.TryParse(cleaned, NumberStyles.Number, CultureInfo.InvariantCulture, out value);
    }

    private static char DetectDelimiter(string content)
    {
        var firstLine = content.Split('\n', 2)[0];
        var counts = new[] { (',', firstLine.Count(c => c == ',')), (';', firstLine.Count(c => c == ';')), ('\t', firstLine.Count(c => c == '\t')) };
        return counts.OrderByDescending(x => x.Item2).First().Item1;
    }

    private static List<List<string>> ReadRecords(string content, char delimiter)
    {
        var records = new List<List<string>>();
        var record = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < content.Length; i++)
        {
            var c = content[i];
            if (inQuotes)
            {
                if (c == '"' && i + 1 < content.Length && content[i + 1] == '"') { field.Append('"'); i++; }
                else if (c == '"') inQuotes = false;
                else field.Append(c);
                continue;
            }

            if (c == '"') inQuotes = true;
            else if (c == delimiter) { record.Add(field.ToString()); field.Clear(); }
            else if (c == '\n' || c == '\r')
            {
                if (c == '\r' && i + 1 < content.Length && content[i + 1] == '\n') i++;
                record.Add(field.ToString()); field.Clear();
                records.Add(record); record = new List<string>();
            }
            else field.Append(c);
        }

        if (field.Length > 0 || record.Count > 0)
        {
            record.Add(field.ToString());
            records.Add(record);
        }

        // Blank lines are kept (and skipped by Parse) so error row numbers match the file.
        return records;
    }

    private static string NormalizeHeader(string header)
    {
        var decomposed = header.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            sb.Append(char.IsWhiteSpace(ch) || ch == '-' ? '_' : ch);
        }
        return sb.ToString();
    }

    private static Dictionary<string, string> BuildAliases(Dictionary<string, string[]> source)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (canonical, aliases) in source)
        {
            map[canonical] = canonical;
            foreach (var alias in aliases) map[alias] = canonical;
        }
        return map;
    }
}
