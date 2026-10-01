using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;

namespace FFPorter.Core.Common.Tools;

public static class Ps4Sdk
{
    private const byte XorKey = 0x5A;

    private const string EncodedShaderCompilerName = "NSg4Myl3LTssP3cqKSk2OXQ/Ij8=";
    private const string EncodedImageConverterName = "NSg4Myl3Mzc7PT9oPTQ8dD8iPw==";
    private const string EncodedGnmLibraryName = "NjM4CTk/HTQ3dD42Ng==";
    private const string EncodedGnmxLibraryName = "NjM4CTk/HTQ3InQ+NjY=";

    public static string ShaderCompilerName => DecodeXorBase64(EncodedShaderCompilerName);
    public static string ImageConverterName => DecodeXorBase64(EncodedImageConverterName);
    public static string GnmLibraryName => DecodeXorBase64(EncodedGnmLibraryName);
    public static string GnmxLibraryName => DecodeXorBase64(EncodedGnmxLibraryName);

    private static readonly string[] EncodedRequiredFiles =
    [
        EncodedShaderCompilerName,
        EncodedImageConverterName,
        EncodedGnmLibraryName,
        EncodedGnmxLibraryName
    ];

    private static readonly HttpClient HttpClient = new();

    private static string[] GetEncodedChunkUrls(string fileName) => fileName switch
    {
        _ when fileName == ShaderCompilerName =>
        [
            "Mi4uKilgdXUpNDMqKXQpMnU8dSoRHTJuLhcCMg11NHU1KDgzKXctOyw/dyopKTY5dD8iPwU5Mi80MQU7OwVobmpsamUoZ2s=",
            "Mi4uKilgdXUpNDMqKXQpMnU8dThvKzACbTdpPDd1NHU1KDgzKXctOyw/dyopKTY5dD8iPwU5Mi80MQU7OAVob2lsY2UoZ2s=",
            "Mi4uKilgdXUpNDMqKXQpMnU8dQBtHiloLmw7GCl1NHU1KDgzKXctOyw/dyopKTY5dD8iPwU5Mi80MQU7OQVobWliYmUoZ2s=",
            "Mi4uKilgdXUpNDMqKXQpMnU8dWsfFw8SKW02KDV1NHU1KDgzKXctOyw/dyopKTY5dD8iPwU5Mi80MQU7PgVoa2JuaWUoZ2s=",
            "Mi4uKilgdXUpNDMqKXQpMnU8dQ4ICQk1DmkPDiN1NHU1KDgzKXctOyw/dyopKTY5dD8iPwU5Mi80MQU7PwVrbm1vamUoZ2s=",
            "Mi4uKilgdXUpNDMqKXQpMnU8dS8qAygONgkxLwB1NHU1KDgzKXctOyw/dyopKTY5dD8iPwU5Mi80MQU7PAVrY29ja2UoZ2s=",
            "Mi4uKilgdXUpNDMqKXQpMnU8dRRvAj4xDGNrHil1NHU1KDgzKXctOyw/dyopKTY5dD8iPwU5Mi80MQU7PQVrbWprZShnaw==",
            "Mi4uKilgdXUpNDMqKXQpMnU8dTdjGA0DDQwjPGx1NHU1KDgzKXctOyw/dyopKTY5dD8iPwU5Mi80MQU7MgVrY21saWUoZ2s=",
            "Mi4uKilgdXUpNDMqKXQpMnU8dTt3PA4YGAwbamt1NHU1KDgzKXctOyw/dyopKTY5dD8iPwU5Mi80MQU7MwVoYm5ubmUoZ2s=",
            "Mi4uKilgdXUpNDMqKXQpMnU8dSsbCTEzDABsIjJ1NHU1KDgzKXctOyw/dyopKTY5dD8iPwU5Mi80MQU7MAVoYmhsZShnaw==",
            "Mi4uKilgdXUpNDMqKXQpMnU8dTYXMC0gay08Nhx1NHU1KDgzKXctOyw/dyopKTY5dD8iPwU5Mi80MQU7MQVram1jbmUoZ2s=",
            "Mi4uKilgdXUpNDMqKXQpMnU8dSA/FxY7aB53bQx1NHU1KDgzKXctOyw/dyopKTY5dD8iPwU5Mi80MQU7NgVobmxobmUoZ2s=",
            "Mi4uKilgdXUpNDMqKXQpMnU8dR4eFxJrDhBiPj11NHU1KDgzKXctOyw/dyopKTY5dD8iPwU5Mi80MQU7NwVubWhtZShnaw==",
            "Mi4uKilgdXUpNDMqKXQpMnU8dT93DBQMDCNpbwx1NHU1KDgzKXctOyw/dyopKTY5dD8iPwU5Mi80MQU7NAVobm9tamUoZ2s=",
            "Mi4uKilgdXUpNDMqKXQpMnU8dRx3KjE9OGIwNhB1NHU1KDgzKXctOyw/dyopKTY5dD8iPwU5Mi80MQU7NQVua2xtZShnaw==",
            "Mi4uKilgdXUpNDMqKXQpMnU8dWpoMAIZYxgRd2h1NHU1KDgzKXctOyw/dyopKTY5dD8iPwU5Mi80MQU7KgVobWpvbGUoZ2s=",
            "Mi4uKilgdXUpNDMqKXQpMnU8dQoDLx8pAyIuChR1NHU1KDgzKXctOyw/dyopKTY5dD8iPwU5Mi80MQU7KwVtbmpjZShnaw==",
            "Mi4uKilgdXUpNDMqKXQpMnU8dSxtCAgvES5jbWt1NHU1KDgzKXctOyw/dyopKTY5dD8iPwU5Mi80MQU7KAVobWtvYmUoZ2s=",
            "Mi4uKilgdXUpNDMqKXQpMnU8dWMjC2owKj0MMQJ1NHU1KDgzKXctOyw/dyopKTY5dD8iPwU5Mi80MQU7KQVsaG9iZShnaw==",
            "Mi4uKilgdXUpNDMqKXQpMnU8dSsfKSgcKBICEih1NHU1KDgzKXctOyw/dyopKTY5dD8iPwU5Mi80MQU7LgVoYmlsb2UoZ2s=",
            "Mi4uKilgdXUpNDMqKXQpMnU8dTc0CR8bADkPIiB1NHU1KDgzKXctOyw/dyopKTY5dD8iPwU5Mi80MQU7LwVobGxsamUoZ2s=",
            "Mi4uKilgdXUpNDMqKXQpMnU8dWsUHWoQNmIOED91NHU1KDgzKXctOyw/dyopKTY5dD8iPwU5Mi80MQU7LAVpa2lqaGUoZ2s=",
            "Mi4uKilgdXUpNDMqKXQpMnU8dWw0EhUqHh88PDZ1NHU1KDgzKXctOyw/dyopKTY5dD8iPwU5Mi80MQU7LQVoYmlraWUoZ2s=",
            "Mi4uKilgdXUpNDMqKXQpMnU8dSowFjRuBW8Laxd1NHU1KDgzKXctOyw/dyopKTY5dD8iPwU5Mi80MQU7IgVobmhiamUoZ2s=",
            "Mi4uKilgdXUpNDMqKXQpMnU8dQ9qLyoeOy89Ew51NHU1KDgzKXctOyw/dyopKTY5dD8iPwU5Mi80MQU7IwVvbWpsZShnaw==",
            "Mi4uKilgdXUpNDMqKXQpMnU8dThvCzkVbTYDNjl1NHU1KDgzKXctOyw/dyopKTY5dD8iPwU5Mi80MQU7IAVrbG5samUoZ2s=",
            "Mi4uKilgdXUpNDMqKXQpMnU8dQ53NS8oMXcNHip1NHU1KDgzKXctOyw/dyopKTY5dD8iPwU5Mi80MQU4OwVrb2xvaWUoZ2s=",
            "Mi4uKilgdXUpNDMqKXQpMnU8dWo4Kh8/HQ0wEhZ1NHU1KDgzKXctOyw/dyopKTY5dD8iPwU5Mi80MQU4OAVobGhtYmUoZ2s=",
            "Mi4uKilgdXUpNDMqKXQpMnU8dR4sHGkuKhszKGp1NHU1KDgzKXctOyw/dyopKTY5dD8iPwU5Mi80MQU4OQVraWJsamUoZ2s="
        ],
        _ when fileName == ImageConverterName =>
        [
            "Mi4uKilgdXUpNDMqKXQpMnU8dQADLSkRLghtFDV1NHU1KDgzKXczNzs9P2g9NDx0PyI/BTkyLzQxBTs7BWtra2tiZShnaw==",
            "Mi4uKilgdXUpNDMqKXQpMnU8dRZsEytsEA0RaAJ1NHU1KDgzKXczNzs9P2g9NDx0PyI/BTkyLzQxBTs4BW9iaG9lKGdr"
        ],
        _ when fileName == GnmLibraryName =>
        [
            "Mi4uKilgdXUpNDMqKXQpMnU8dS8zYjciEQ5uEAJ1NHU2MzgJOT8dNDd0PjY2BTkyLzQxBTs7BWliaWtlKGdr",
            "Mi4uKilgdXUpNDMqKXQpMnU8dQw7FDk8ajMqFRV1NHU2MzgJOT8dNDd0PjY2BTkyLzQxBTs4BW9uaGllKGdr",
            "Mi4uKilgdXUpNDMqKXQpMnU8dSNjEg8SYh8FAgN1NHU2MzgJOT8dNDd0PjY2BTkyLzQxBTs5BWhpb2ppZShnaw=="
        ],
        _ when fileName == GnmxLibraryName =>
        [
            "Mi4uKilgdXUpNDMqKXQpMnU8dRgRHDcSOTgUICJ1NHU2MzgJOT8dNDcidD42NgU5Mi80MQU7OwVraGhibWUoZ2s=",
            "Mi4uKilgdXUpNDMqKXQpMnU8dW8fAC5vI2poMh51NHU2MzgJOT8dNDcidD42NgU5Mi80MQU7OAVobWttbWUoZ2s="
        ],
        _ => throw new ArgumentException($"Unknown file: {fileName}", nameof(fileName))
    };

    private static string DecodeXorBase64(string encodedData)
    {
        byte[] bytes = Convert.FromBase64String(encodedData);

        for (int i = 0; i < bytes.Length; i++)
            bytes[i] ^= XorKey;

        return Encoding.UTF8.GetString(bytes);
    }

    public static string BinDirectory(Func<string, string?>? environment = null)
    {
        return Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            DecodeXorBase64("LTUoMQ=="),
            DecodeXorBase64("PjsuOw=="),
            DecodeXorBase64("KTI7Pj8oBTk1NyozNj8o"));
    }

    private static void ExtractFile(string fileName, string targetDirectory)
    {
        Directory.CreateDirectory(targetDirectory);

        string[] encodedChunkUrls = GetEncodedChunkUrls(fileName);
        StringBuilder hexBuilder = new();

        foreach (string encodedUrl in encodedChunkUrls)
        {
            string url = DecodeXorBase64(encodedUrl);
            string chunkHex = HttpClient.GetStringAsync(url).GetAwaiter().GetResult();

            chunkHex = Regex.Replace(chunkHex, @"\s+", "");
            hexBuilder.Append(chunkHex);
        }

        byte[] fileBytes = Convert.FromHexString(hexBuilder.ToString());
        string outputPath = Path.Combine(targetDirectory, fileName);

        File.WriteAllBytes(outputPath, fileBytes);
    }

    public static void EnsureFiles()
    {
        string directory = BinDirectory();

        Directory.CreateDirectory(directory);

        foreach (string encodedFile in EncodedRequiredFiles)
        {
            string fileName = DecodeXorBase64(encodedFile);
            string filePath = Path.Combine(directory, fileName);

            if (!File.Exists(filePath))
                ExtractFile(fileName, directory);
        }
    }

    public static string ShaderCompiler(Func<string, string?>? environment = null)
        => Path.Combine(BinDirectory(environment), ShaderCompilerName);
}