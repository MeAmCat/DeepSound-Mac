namespace DeepSoundMac.Core.Models;

/// <summary>
/// Represents a secret file to be hidden or extracted.
/// </summary>
public class SecretFile
{
    /// <summary>
    /// The original file name.
    /// </summary>
    public string FileName { get; set; } = string.Empty;
    
    /// <summary>
    /// The file data.
    /// </summary>
    public byte[] Data { get; set; } = [];
    
    /// <summary>
    /// Creates a SecretFile from a file path.
    /// </summary>
    public static SecretFile FromFile(string filePath)
    {
        return new SecretFile
        {
            FileName = Path.GetFileName(filePath),
            Data = File.ReadAllBytes(filePath)
        };
    }
    
    /// <summary>
    /// Saves the secret file to the specified directory.
    /// </summary>
    public void SaveTo(string directoryPath)
    {
        string outputPath = Path.Combine(directoryPath, FileName);
        File.WriteAllBytes(outputPath, Data);
    }
}

/// <summary>
/// Represents a payload containing multiple secret files.
/// </summary>
public class SecretPayload
{
    /// <summary>
    /// The list of secret files in this payload.
    /// </summary>
    public List<SecretFile> Files { get; set; } = [];
    
    /// <summary>
    /// Whether the payload is encrypted.
    /// </summary>
    public bool IsEncrypted { get; set; }
}
