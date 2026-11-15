using System.Reflection;

using FinanceFlow.Application.Abstractions;
using FinanceFlow.Application.Banking.Dtos;
using FinanceFlow.Domain.Banking;

using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace FinanceFlow.Infrastructure.Export;

/// <summary>
/// Renders the receipt of a transfer as a PDF file.
/// The document carries the identity of BarsiG and states clearly that the money is virtual.
/// </summary>
public sealed class ReceiptPdfRenderer : IReceiptRenderer
{
    /// <summary>The colour of the night, the darkest tone of the palette.</summary>
    private static readonly Color Midnight = Color.FromHex("#101820");

    /// <summary>The colour of the slate, the second tone of the palette.</summary>
    private static readonly Color Slate = Color.FromHex("#1C2B35");

    /// <summary>The colour of the sage, the accent of the palette.</summary>
    private static readonly Color Sage = Color.FromHex("#477C71");

    /// <summary>The colour of the off white paper of the palette.</summary>
    private static readonly Color OffWhite = Color.FromHex("#F4F6F4");

    static ReceiptPdfRenderer()
    {
        // The project is a university simulation without any commercial use.
        QuestPDF.Settings.License = LicenseType.Community;
    }

    /// <inheritdoc />
    public Task<byte[]> RenderAsync(
        TransferReceiptData receipt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(receipt);

        return Task.Run(
            () =>
            {
                byte[] logo = LoadLogo();

                return Document.Create(container =>
                {
                    container.Page(page =>
                    {
                        page.Size(PageSizes.A4);
                        page.Margin(2.2f, Unit.Centimetre);
                        page.DefaultTextStyle(style => style.FontSize(10).FontColor(Slate));

                        page.Header().Column(column =>
                        {
                            column.Spacing(4);

                            column.Item().Row(row =>
                            {
                                row.RelativeItem().Image(new MemoryStream(logo));

                                row.ConstantItem(190).Column(column =>
                                {
                                    column.Item().Text("BarsiG")
                                        .FontSize(20).Bold().FontColor(Midnight);

                                    column.Item().Text("Digital Banking Simulator")
                                        .FontSize(9).FontColor(Sage);

                                    column.Item().Text("Receipt of a virtual transaction")
                                        .FontSize(8).FontColor(Slate);
                                });
                            });

                            column.Item().LineHorizontal(1, Unit.Point).LineColor(Sage);
                        });

                        page.Content().Column(column =>
                        {
                            column.Spacing(6);

                            column.Item().Text("Payment receipt")
                                .FontSize(16).Bold().FontColor(Midnight);

                            AddRow(column, "Transaction number", receipt.TransferId.ToString().ToUpperInvariant());
                            AddRow(column, "Status", DescribeStatus(receipt.Status));
                            AddRow(column, "Type", DescribeKind(receipt.Kind));
                            AddRow(column, "Sender", receipt.SenderName);
                            AddRow(column, "Sender account", receipt.SenderAccountNumber);
                            AddRow(column, "Recipient", receipt.RecipientName);
                            AddRow(column, "Recipient account", receipt.RecipientAccountNumber);
                            AddRow(column, "Description", receipt.Description);
                            AddRow(column, "Amount", $"{receipt.Amount.Format()} {receipt.Currency}");
                            AddRow(column, "Requested", FormatMoment(receipt.CreatedAtUtc));
                            AddRow(
                                column,
                                "Completed",
                                receipt.CompletedAtUtc is null ? "-" : FormatMoment(receipt.CompletedAtUtc.Value));

                            column.Item().Background(OffWhite).Text(text =>
                            {
                                text.Span("This receipt was produced by BarsiG, an educational banking simulator. ")
                                    .FontSize(9).FontColor(Slate);

                                text.Span("All amounts are virtual money. No real bank account, no real transfer and no payment network is involved.")
                                    .FontSize(9).Bold().FontColor(Midnight);
                            });
                        });

                        page.Footer().AlignCenter().Text(text =>
                        {
                            text.Span("BarsiG — Digital Banking Simulator — the university project of the course FP 3222")
                                .FontSize(8).FontColor(Sage);
                        });
                    });
                }).GeneratePdf();
            },
            cancellationToken);
    }

    /// <summary>Adds one line with a label and a value to the receipt.</summary>
    private static void AddRow(ColumnDescriptor column, string label, string value)
    {
        column.Item().Text(text =>
        {
            text.Span($"{label}: ").FontSize(9).Bold().FontColor(Slate);
            text.Span(value).FontSize(9).FontColor(Midnight);
        });
    }

    /// <summary>Describes the status of a transfer.</summary>
    private static string DescribeStatus(LedgerTransferStatus status) => status switch
    {
        LedgerTransferStatus.Completed => "Completed",
        LedgerTransferStatus.Failed => "Rejected",
        _ => "Unknown"
    };

    /// <summary>Describes the kind of a transfer.</summary>
    private static string DescribeKind(LedgerTransferKind kind) => kind switch
    {
        LedgerTransferKind.UserTransfer => "Transfer between two customers",
        LedgerTransferKind.AdminFunding => "Funding of the Demo Bank",
        LedgerTransferKind.DepositContribution => "Contribution to a deposit",
        LedgerTransferKind.DepositWithdrawal => "Withdrawal from a deposit",
        LedgerTransferKind.InterestAccrual => "Monthly interest of a deposit",
        _ => "Movement"
    };

    /// <summary>Formats a moment for the receipt.</summary>
    private static string FormatMoment(DateTimeOffset moment) =>
        moment.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss");

    /// <summary>Reads the logo of BarsiG out of the resources of the assembly.</summary>
    private static byte[] LoadLogo()
    {
        Assembly assembly = typeof(ReceiptPdfRenderer).Assembly;

        using Stream? stream = assembly.GetManifestResourceStream("BarsiG_Logo.png");

        if (stream is null)
        {
            return [];
        }

        using MemoryStream buffer = new();
        stream.CopyTo(buffer);

        return buffer.ToArray();
    }
}
