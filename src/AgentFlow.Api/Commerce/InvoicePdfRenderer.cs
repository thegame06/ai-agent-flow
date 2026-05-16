using System.Globalization;
using System.Text;

namespace AgentFlow.Api.Commerce;

internal static class InvoicePdfRenderer
{
    public static byte[] Render(CommerceInvoiceDocument invoice, CommercePartyDocument? party, CommerceSaleDocument? sale)
    {
        var lines = new List<string>
        {
            "Invoice",
            $"Number: {invoice.Number}",
            $"Status: {invoice.Status}",
            $"Issued: {invoice.IssuedAt?.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) ?? "-"}",
            $"Customer: {party?.FullName ?? party?.DisplayName ?? party?.Identifier ?? invoice.PartyId}",
            $"Phone: {party?.Phone ?? "-"}",
            $"Email: {party?.Email ?? "-"}",
            $"Currency: {invoice.Currency}",
            $"Total: {invoice.Total.ToString("0.00", CultureInfo.InvariantCulture)}",
        };

        if (sale is not null)
        {
            lines.Add("Items:");
            foreach (var item in sale.Items)
                lines.Add($"- {item.Quantity.ToString("0.##", CultureInfo.InvariantCulture)} x {item.Name} ({item.Sku}) = {(item.UnitPrice * item.Quantity).ToString("0.00", CultureInfo.InvariantCulture)}");
            lines.Add($"Subtotal: {sale.Subtotal.ToString("0.00", CultureInfo.InvariantCulture)}");
            lines.Add($"Discount: {sale.Discount.ToString("0.00", CultureInfo.InvariantCulture)}");
            lines.Add($"Tax: {sale.Tax.ToString("0.00", CultureInfo.InvariantCulture)}");
        }

        var content = BuildContentStream(lines);
        return BuildPdf(content);
    }

    private static string BuildContentStream(IReadOnlyList<string> lines)
    {
        var sb = new StringBuilder();
        sb.AppendLine("BT");
        sb.AppendLine("/F1 12 Tf");
        sb.AppendLine("50 780 Td");
        for (var i = 0; i < lines.Count; i++)
        {
            if (i > 0)
                sb.AppendLine("0 -18 Td");
            sb.Append('(').Append(Escape(lines[i])).AppendLine(") Tj");
        }
        sb.AppendLine("ET");
        return sb.ToString();
    }

    private static byte[] BuildPdf(string contentStream)
    {
        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
            $"<< /Length {Encoding.ASCII.GetByteCount(contentStream)} >>\nstream\n{contentStream}endstream"
        };

        var pdf = new StringBuilder();
        pdf.Append("%PDF-1.4\n");
        var offsets = new List<int> { 0 };

        for (var i = 0; i < objects.Count; i++)
        {
            offsets.Add(Encoding.ASCII.GetByteCount(pdf.ToString()));
            pdf.Append(i + 1).Append(" 0 obj\n").Append(objects[i]).Append("\nendobj\n");
        }

        var xrefOffset = Encoding.ASCII.GetByteCount(pdf.ToString());
        pdf.Append("xref\n0 ").Append(objects.Count + 1).Append('\n');
        pdf.Append("0000000000 65535 f \n");
        for (var i = 1; i < offsets.Count; i++)
            pdf.Append(offsets[i].ToString("D10", CultureInfo.InvariantCulture)).Append(" 00000 n \n");
        pdf.Append("trailer\n<< /Size ").Append(objects.Count + 1).Append(" /Root 1 0 R >>\n");
        pdf.Append("startxref\n").Append(xrefOffset).Append("\n%%EOF");
        return Encoding.ASCII.GetBytes(pdf.ToString());
    }

    private static string Escape(string value)
        => value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("(", "\\(", StringComparison.Ordinal)
            .Replace(")", "\\)", StringComparison.Ordinal);
}
