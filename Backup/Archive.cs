using System;
using System.IO;
using System.Net.Http;
using Exiled.API.Features;
using System.Threading.Tasks;
using ICSharpCode.SharpZipLib.Zip;
using System.Linq;
using System.Collections.Generic;
using Exiled.API.Features.Pools;
using System.Threading;

namespace Backup;
public class Archive
{
    public const int MaximumFileSizeBytes = 1041278;

    /// <summary>
    /// Creates archives and returns the path to the saved archives
    /// </summary>
    /// <param name="folders">Array with folders to be included in the archive</param>
    /// <param name="password">Password for the archive</param>
    /// <returns></returns>
    public static string[] CreateArchives(string[] folders, string[] files, string password)
    {
        List<List<string>> filesGroups = new();

        List<string> unparsedFiles = new();

        foreach (var folder in folders)
        {
            if (!Directory.Exists(folder))
            {
                Log.Warn($"Folder with path {folder} not found");
                continue;
            }

            List<string> ProcessFolder(string pathTo)
            {
                List<string> files = [.. Directory.GetFiles(pathTo)];

                foreach(var path in Directory.GetDirectories(pathTo))
                {
                    files.AddRange(ProcessFolder(path));
                }

                return files;
            }

            unparsedFiles.AddRange(ProcessFolder(folder));
        }

        Log.Debug("finihsed parsing folders, go");

        foreach (var file in files)
        {
            if (!File.Exists(file))
            {
                Log.Warn($"File with path {file} not found");
                continue;
            }

            unparsedFiles.Add(file);
        }

        Log.Debug("finihsed parsing files, go");

        while (unparsedFiles.Any())
        {
            int archiveSizeInBytes = 0;
            List<string> parsedFiles = new();

            while (true)
            {
                if (unparsedFiles.Count == 0)
                {
                    break;
                }

                foreach (var file in unparsedFiles.ToList())
                {
                    FileInfo fileInfo = new(file);

                    if (file.Length >= MaximumFileSizeBytes)
                    {
                        Log.Warn($"File size of {file} is too big to fit into webhook. Skipping.");
                        unparsedFiles.Remove(file);

                        continue;
                    }

                    if (archiveSizeInBytes + file.Length >= MaximumFileSizeBytes)
                    {
                        goto pleaseBreak;
                    }

                    archiveSizeInBytes += file.Length;

                    parsedFiles.Add(file);
                    unparsedFiles.Remove(file);
                }
            }

        pleaseBreak:
            archiveSizeInBytes = 0;
            parsedFiles.Clear();
            filesGroups.Add(parsedFiles);
        }

        Log.Debug("finihsed parsing groups, go");

        int archiveCount = 0;

        List<string> archivesPath = new();

        foreach (var filesGroup in filesGroups)
        {
            Log.Debug("finihsed parsing group, go");

            archiveCount++;

            string nameOfArchive = $"ArchiveBackup{archiveCount}.zip";

            using (ZipOutputStream zipStream = new(File.Create(nameOfArchive)))
            {
                zipStream.SetLevel(9);

                zipStream.Password = password;

                foreach (var file in filesGroup)
                {
                    // проверка выше, файла которого нет не может суда попасть
                    if (!File.Exists(file)) continue;

                    AddFile(file, zipStream);
                }

                zipStream.Finish();
                zipStream.Close();
            }

            archivesPath.Add(nameOfArchive);
        }

        return archivesPath.ToArray();
    }

    /// <summary>
    /// Adds files to the archive
    /// </summary>
    /// <param name="filePatch">File path</param>
    /// <param name="zipStream">ZipOutputStream</param>
    private static void AddFile(string filePatch, ZipOutputStream zipStream)
    {
        FileInfo fileInfo = new FileInfo(filePatch);
        DirectoryInfo directory = new DirectoryInfo(fileInfo.Directory.FullName);

        using (FileStream fileStream = File.OpenRead(filePatch))
        {
            byte[] buffer = new byte[fileStream.Length];
            fileStream.Read(buffer, 0, buffer.Length);

            string entryName = directory.Name + "//" + filePatch.Replace(directory.FullName, "").TrimStart('\\');
            ZipEntry entry = new ZipEntry(entryName);
            entry.DateTime = fileInfo.LastWriteTime;
            entry.Size = fileStream.Length;

            zipStream.PutNextEntry(entry);
            zipStream.Write(buffer, 0, buffer.Length);
        }
    }

    /// <summary>
    /// Sends an archive to the Discord channel through a bot
    /// </summary>
    /// <param name="archivePatch">Path to the archive</param>
    /// <param name="webhookUrl">Webhook URL</param>
    /// <returns></returns>
    public static async Task SendBackup(string archivePatch, string webhookUrl)
    {
        try
        {
            byte[] fileContent = File.ReadAllBytes(archivePatch);

            File.Delete(archivePatch);

            Log.Error(fileContent.Length);

            // <t> - Discord Timestamp
            MultipartFormDataContent form = new MultipartFormDataContent
            {
                { new StringContent($"New backup! Send process started in: <t:{DateTimeOffset.Now.ToUnixTimeSeconds()}>"), "content" },
                { new ByteArrayContent(fileContent), "file", "Backup.zip" }
            };

            _ = Task.Run(async () =>
            {
                using (HttpClient client = new())
                {
                    HttpResponseMessage response = await client.PostAsync(webhookUrl, form, CancellationToken.None);

                    if (!response.IsSuccessStatusCode)
                    {
                        Log.Warn("Failed send backup :(\n" + response.Content);
                    }
                }
            }, CancellationToken.None);
        }
        catch (System.Exception ex)
        {
            Log.Error(ex);
        }
    }

    /// <summary>
    /// Checks if it is time to create a backup
    /// </summary>
    /// <returns></returns>
    public static bool TimeToBackup()
    {
        if (!File.Exists("NextBackup.txt"))
        {
            File.WriteAllText("NextBackup.txt", DateTime.MinValue.ToShortDateString());
            return true;
        }

        DateTime date = DateTime.Parse(File.ReadAllText("NextBackup.txt"));

        if (DateTime.Now > date)
        {
            File.WriteAllText("NextBackup.txt", DateTime.Now.AddDays(Plugin.Singleton.Config.DayNextBackup).ToShortDateString());
            return true;
        }

        return false;
    }
}
