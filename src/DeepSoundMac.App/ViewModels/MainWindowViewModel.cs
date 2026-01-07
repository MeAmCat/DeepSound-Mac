using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeepSoundMac.Core.Audio;
using DeepSoundMac.Core.Models;
using DeepSoundMac.Core.Steganography;

namespace DeepSoundMac.App.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _statusMessage = "Ready. Open a carrier audio file to begin.";

    [ObservableProperty]
    private string? _carrierFilePath;

    [ObservableProperty]
    private string? _outputDirectory;

    [ObservableProperty]
    private string _password = string.Empty;

    [ObservableProperty]
    private bool _useEncryption;

    [ObservableProperty]
    private bool _isProcessing;

    [ObservableProperty]
    private int _maxCapacityBytes;

    [ObservableProperty]
    private int _usedCapacityBytes;

    [ObservableProperty]
    private double _capacityPercentage;

    [ObservableProperty]
    private bool _hasHiddenData;

    [ObservableProperty]
    private bool _hiddenDataIsEncrypted;

    public ObservableCollection<SecretFileViewModel> SecretFiles { get; } = [];
    public ObservableCollection<SecretFileViewModel> ExtractedFiles { get; } = [];

    private WavFile? _carrierWav;

    public MainWindowViewModel()
    {
        OutputDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    partial void OnCarrierFilePathChanged(string? value)
    {
        LoadCarrierFile();
    }

    partial void OnUseEncryptionChanged(bool value)
    {
        if (!value)
        {
            Password = string.Empty;
        }
    }

    private void LoadCarrierFile()
    {
        if (string.IsNullOrEmpty(CarrierFilePath) || !File.Exists(CarrierFilePath))
        {
            _carrierWav = null;
            MaxCapacityBytes = 0;
            HasHiddenData = false;
            return;
        }

        try
        {
            _carrierWav = WavFileHandler.Read(CarrierFilePath);
            MaxCapacityBytes = _carrierWav.MaxHiddenDataBytes;
            HasHiddenData = AudioSteganography.ContainsHiddenData(_carrierWav);
            
            if (HasHiddenData)
            {
                HiddenDataIsEncrypted = AudioSteganography.IsEncrypted(_carrierWav);
                StatusMessage = HiddenDataIsEncrypted 
                    ? "Hidden data detected (encrypted). Enter password to extract."
                    : "Hidden data detected. Ready to extract.";
            }
            else
            {
                StatusMessage = $"Carrier loaded. Capacity: {FormatBytes(MaxCapacityBytes)}. Add files to hide.";
            }
            
            UpdateCapacity();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error loading file: {ex.Message}";
            _carrierWav = null;
        }
    }

    private void UpdateCapacity()
    {
        UsedCapacityBytes = SecretFiles.Sum(f => f.FileSize) + SecretFiles.Count * 100; // Rough estimate with overhead
        CapacityPercentage = MaxCapacityBytes > 0 ? (double)UsedCapacityBytes / MaxCapacityBytes * 100 : 0;
    }

    [RelayCommand]
    private void AddSecretFile(string filePath)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            return;

        var fileInfo = new FileInfo(filePath);
        var secretFile = new SecretFileViewModel
        {
            FileName = fileInfo.Name,
            FilePath = filePath,
            FileSize = (int)fileInfo.Length
        };

        SecretFiles.Add(secretFile);
        UpdateCapacity();
        StatusMessage = $"Added {secretFile.FileName}. Total files: {SecretFiles.Count}";
    }

    [RelayCommand]
    private void RemoveSecretFile(SecretFileViewModel file)
    {
        SecretFiles.Remove(file);
        UpdateCapacity();
        StatusMessage = $"Removed {file.FileName}. Total files: {SecretFiles.Count}";
    }

    [RelayCommand]
    private void ClearSecretFiles()
    {
        SecretFiles.Clear();
        UpdateCapacity();
        StatusMessage = "All secret files cleared.";
    }

    [RelayCommand]
    private async Task EncodeAsync()
    {
        if (_carrierWav == null || SecretFiles.Count == 0)
            return;

        IsProcessing = true;
        StatusMessage = "Encoding secret files into audio...";

        try
        {
            await Task.Run(() =>
            {
                var files = SecretFiles.Select(f => SecretFile.FromFile(f.FilePath)).ToList();
                string? password = UseEncryption && !string.IsNullOrEmpty(Password) ? Password : null;
                
                var outputWav = AudioSteganography.Encode(_carrierWav, files, password);
                
                string outputPath = Path.Combine(
                    OutputDirectory ?? Environment.CurrentDirectory,
                    $"stego_{Path.GetFileName(CarrierFilePath)}");
                
                WavFileHandler.Write(outputPath, outputWav);
                
                StatusMessage = $"Success! Output saved to: {outputPath}";
            });
        }
        catch (Exception ex)
        {
            StatusMessage = $"Encoding failed: {ex.Message}";
        }
        finally
        {
            IsProcessing = false;
        }
    }

    [RelayCommand]
    private async Task DecodeAsync()
    {
        if (_carrierWav == null || !HasHiddenData)
            return;

        if (HiddenDataIsEncrypted && string.IsNullOrEmpty(Password))
        {
            StatusMessage = "Please enter the password to decrypt hidden data.";
            return;
        }

        IsProcessing = true;
        StatusMessage = "Extracting hidden files...";

        try
        {
            SecretPayload? payload = null;
            
            await Task.Run(() =>
            {
                string? password = HiddenDataIsEncrypted ? Password : null;
                payload = AudioSteganography.Decode(_carrierWav, password);
            });

            if (payload != null)
            {
                ExtractedFiles.Clear();
                foreach (var file in payload.Files)
                {
                    ExtractedFiles.Add(new SecretFileViewModel
                    {
                        FileName = file.FileName,
                        FileSize = file.Data.Length,
                        Data = file.Data
                    });
                }
                
                StatusMessage = $"Extracted {payload.Files.Count} file(s). Click 'Save Extracted Files' to save.";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Extraction failed: {ex.Message}";
        }
        finally
        {
            IsProcessing = false;
        }
    }

    [RelayCommand]
    private async Task SaveExtractedFilesAsync()
    {
        if (ExtractedFiles.Count == 0 || string.IsNullOrEmpty(OutputDirectory))
            return;

        IsProcessing = true;
        StatusMessage = "Saving extracted files...";

        try
        {
            await Task.Run(() =>
            {
                foreach (var file in ExtractedFiles)
                {
                    if (file.Data != null)
                    {
                        string outputPath = Path.Combine(OutputDirectory, file.FileName);
                        File.WriteAllBytes(outputPath, file.Data);
                    }
                }
            });

            StatusMessage = $"Saved {ExtractedFiles.Count} file(s) to {OutputDirectory}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Save failed: {ex.Message}";
        }
        finally
        {
            IsProcessing = false;
        }
    }

    private static string FormatBytes(int bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes / (1024.0 * 1024.0):F2} MB";
    }
}

public partial class SecretFileViewModel : ObservableObject
{
    [ObservableProperty]
    private string _fileName = string.Empty;

    [ObservableProperty]
    private string _filePath = string.Empty;

    [ObservableProperty]
    private int _fileSize;

    public byte[]? Data { get; set; }

    public string FileSizeFormatted => FormatBytes(FileSize);

    private static string FormatBytes(int bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes / (1024.0 * 1024.0):F2} MB";
    }
}
