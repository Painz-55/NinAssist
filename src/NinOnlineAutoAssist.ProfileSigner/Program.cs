using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NinOnlineAutoAssist.Live;

if (args.Length is < 2 or > 3 || (args.Length == 3 && args[2] != "--approve-actuation"))
{
    Console.Error.WriteLine("Uso: ProfileSigner <profiles.json> <chave-privada.pem fora do repositório> [--approve-actuation após validação ao vivo]");
    return 2;
}
var manifest = Path.GetFullPath(args[0]);
var keyFile = Path.GetFullPath(args[1]);
var repo = Directory.GetParent(Path.GetDirectoryName(manifest)!)!.FullName + Path.DirectorySeparatorChar;
if (keyFile.StartsWith(repo, StringComparison.OrdinalIgnoreCase))
    throw new InvalidOperationException("Guarde a chave privada fora do repositório.");
using var key = ECDsa.Create();
key.ImportFromPem(File.ReadAllText(keyFile));
if (!key.ExportSubjectPublicKeyInfoPem().AsSpan().Trim().SequenceEqual(SignedProfileCatalog.PublicKey.AsSpan().Trim()))
    throw new InvalidOperationException("A chave não corresponde à chave pública embutida no aplicativo.");
var payload = File.ReadAllBytes(manifest);
using var json = JsonDocument.Parse(payload);
if (json.RootElement.GetProperty("schema").GetInt32() != 1) throw new InvalidOperationException("Schema inválido.");
var profiles = json.RootElement.GetProperty("profiles");
if (profiles.GetArrayLength() > 32) throw new InvalidOperationException("Perfis demais.");
foreach (var entry in profiles.EnumerateArray())
{
    if (entry.GetProperty("ActuationValidated").GetBoolean() && args.Length != 3)
        throw new InvalidOperationException("Perfil que envia teclas exige --approve-actuation após validação semântica ao vivo.");
    var game = entry.GetProperty("Sha256").GetString()!;
    var common = entry.GetProperty("CommonSha256").GetString()!;
    // Validate the complete mapping before creating a signature.
    var signature = key.SignData(payload, HashAlgorithmName.SHA256);
    _ = SignedProfileCatalog.VerifyAndSelect(payload, signature, SignedProfileCatalog.PublicKey, game, common)
        ?? throw new InvalidOperationException("Perfil não selecionável.");
}
File.WriteAllText(manifest + ".sig", Convert.ToBase64String(key.SignData(payload, HashAlgorithmName.SHA256)) + "\n", Encoding.ASCII);
Console.WriteLine("Manifesto validado e assinado. NÃO publique a chave privada; publique JSON e .sig juntos.");
return 0;
