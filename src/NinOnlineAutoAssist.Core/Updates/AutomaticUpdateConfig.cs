namespace NinOnlineAutoAssist.Core.Updates;

public sealed record AutomaticUpdateConfig
{
    public string RepositoryUrl { get; init; } = "";
    public string? ValidatedRepositoryUrl()
    {
        if (string.IsNullOrWhiteSpace(RepositoryUrl)) return null;
        if (!Uri.TryCreate(RepositoryUrl.Trim(), UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
            !uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) || !uri.IsDefaultPort ||
            uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0)
            throw new ArgumentException("Use uma URL HTTPS de repositório público do GitHub, sem token.");
        var parts = uri.AbsolutePath.Trim('/').Split('/');
        if (parts.Length != 2 || !System.Text.RegularExpressions.Regex.IsMatch(parts[0], "^[A-Za-z0-9][A-Za-z0-9-]{0,38}$") ||
            !System.Text.RegularExpressions.Regex.IsMatch(parts[1], "^[A-Za-z0-9_.-]{1,100}$") || parts[1] is "." or "..")
            throw new ArgumentException("Repositório inválido: informe https://github.com/usuario/repositorio.");
        var repo = parts[1].EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? parts[1][..^4] : parts[1];
        if (repo.Length == 0 || repo is "." or "..") throw new ArgumentException("Repositório inválido.");
        return $"https://github.com/{parts[0]}/{repo}";
    }
}
