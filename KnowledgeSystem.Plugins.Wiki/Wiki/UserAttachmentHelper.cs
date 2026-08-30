using System.Text;
using KnowledgeSystem.Api;
using KnowledgeSystem.Plugins.Library.Tools.Workspace;

namespace KnowledgeSystem.Plugins.Wiki.Wiki;

public static class UserAttachmentHelper
{
    public static async Task<string> InjectAsync(
        ArtifactWorkspace workspace,
        IReadOnlyList<UserAttachment> attachments,
        Func<UserAttachment, CancellationToken, Task<byte[]>> download,
        CancellationToken cancellationToken)
    {
        var written = new List<string>();
        var skipped = new List<string>();

        foreach (var attachment in attachments)
        {
            if (attachment.Size > workspace.Config.MaxFileChars)
            {
                skipped.Add($"{attachment.FileName} (too large)");
                continue;
            }

            byte[] bytes;

            try
            {
                bytes = await download(attachment, cancellationToken);
            }
            catch (Exception e)
            {
                throw new InvalidOperationException($"failed to download user attachment '{attachment.FileName}': {e.Message}", e);
            }

            if (!TryDecodeUtf8(bytes, out var text))
            {
                skipped.Add($"{attachment.FileName} (binary, skipped)");
                continue;
            }

            var name = SanitizeName(attachment.FileName);

            if (name.Length == 0)
            {
                skipped.Add($"{attachment.FileName} (invalid name, skipped)");
                continue;
            }

            if (!workspace.TryWrite(name, text, out var error))
            {
                skipped.Add($"{attachment.FileName} ({error})");
                continue;
            }

            written.Add(name);
        }

        if (written.Count == 0 && skipped.Count == 0)
        {
            return string.Empty;
        }

        var parts = new List<string>();

        if (written.Count > 0)
        {
            parts.Add($"written to the workspace: {string.Join(", ", written)}");
        }

        if (skipped.Count > 0)
        {
            parts.Add($"skipped: {string.Join(", ", skipped)}");
        }

        return $"The user attached files to their message. {string.Join(". ", parts)}. Work on them with the artifact_* tools.";
    }

    public static bool TryDecodeUtf8(byte[] bytes, out string text)
    {
        try
        {
            text = new UTF8Encoding(false, true).GetString(bytes);
            return true;
        }
        catch (DecoderFallbackException)
        {
            text = string.Empty;
            return false;
        }
    }

    public static string SanitizeName(string name)
    {
        var sb = new StringBuilder(name.Length);

        foreach (var c in name)
        {
            sb.Append(char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-' ? c : '_');
        }

        return sb.ToString().Trim('_');
    }
}
