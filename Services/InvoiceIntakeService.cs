using System.Security.Cryptography;
using TestCaseSmartDocsHard.Models;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Exceptions;

namespace TestCaseSmartDocsHard.Services;

public sealed class InvoiceIntakeService(FileStorage storage, JobStore jobs)
{
    public const long MaxFileBytes = 20 * 1024 * 1024;
    public const long MaxRequestBytes = MaxFileBytes + 1024 * 1024;

    public async Task<Submission> SubmitAsync(IFormFile? file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
            throw new IntakeException("file_empty", "A non-empty file is required.");
        if (file.Length > MaxFileBytes)
            throw new IntakeException("file_too_large", "The file must not exceed 20 MB.");

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (extension is not (".pdf" or ".png" or ".jpg" or ".jpeg"))
            throw new IntakeException("format_unsupported", "Only PNG, JPG, JPEG and PDF files are supported.");

        var path = storage.CreatePath(extension);
        var retained = false;
        try
        {
            await using (var input = file.OpenReadStream())
            await using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true))
            {
                var buffer = new byte[65536];
                long length = 0;
                int count;
                while ((count = await input.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    length += count;
                    if (length > MaxFileBytes)
                        throw new IntakeException("file_too_large", "The file must not exceed 20 MB.");
                    await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
                }
                if (length == 0)
                    throw new IntakeException("file_empty", "A non-empty file is required.");
            }

            await using var saved = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);
            var header = new byte[8];
            var headerLength = await saved.ReadAsync(header, cancellationToken);
            if (!HasSignature(extension, header.AsSpan(0, headerLength)))
                throw new IntakeException("signature_invalid", "The file signature does not match its extension.");
            if (extension == ".pdf")
                ValidatePdf(path, cancellationToken);

            saved.Position = 0;
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(saved, cancellationToken));
            await saved.DisposeAsync();
            cancellationToken.ThrowIfCancellationRequested();
            var result = jobs.Submit(hash, path, extension);
            retained = !result.IsDuplicate;
            return result;
        }
        finally
        {
            if (!retained)
                storage.Delete(path);
        }
    }

    public static bool HasSignature(string extension, ReadOnlySpan<byte> header) => extension switch
    {
        ".png" => header.StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }),
        ".jpg" or ".jpeg" => header.StartsWith(new byte[] { 255, 216, 255 }),
        ".pdf" => header.StartsWith("%PDF-"u8),
        _ => false
    };

    private static void ValidatePdf(string path, CancellationToken cancellationToken)
    {
        try
        {
            using var pdf = PdfDocument.Open(path, new ParsingOptions { UseLenientParsing = false });
            if (pdf.IsEncrypted)
                throw new IntakeException("pdf_encrypted", "Password-protected PDF files are not supported.");
            if (pdf.NumberOfPages is < 1 or > 10)
                throw new IntakeException("pdf_page_limit", "PDF files must contain between 1 and 10 pages.");
            for (var i = 1; i <= pdf.NumberOfPages; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                _ = pdf.GetPage(i);
            }
        }
        catch (PdfDocumentEncryptedException)
        {
            throw new IntakeException("pdf_encrypted", "Password-protected PDF files are not supported.");
        }
        catch (Exception exception) when (exception is not (IntakeException or OperationCanceledException or IOException or UnauthorizedAccessException))
        {
            throw new IntakeException("pdf_invalid", "The PDF file cannot be read or is damaged.");
        }
    }
}
