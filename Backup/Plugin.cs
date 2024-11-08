using System;
using System.IO;
using System.Threading.Tasks;
using Exiled.API.Enums;
using Exiled.API.Features;
using Server = Exiled.Events.Handlers.Server;

namespace Backup;
public class Plugin : Plugin<Config>
{
    public override string Prefix { get; } = "Backup";
    public override string Name { get; } = "Backup";
    public override string Author { get; } = "XLEB_YSHEK & adarkaz";
    public override Version Version { get; } = new Version(3, 3, 0);
    public override PluginPriority Priority { get; } = PluginPriority.Low;

    public static Plugin Singleton;

    public override void OnEnabled()
    {
        Singleton = this;

        Server.WaitingForPlayers += OnWaitingForPlayers;

        base.OnEnabled();
    }

    public override void OnDisabled()
    {
        Singleton = null;

        Server.WaitingForPlayers -= OnWaitingForPlayers;

        base.OnDisabled();
    }

    public void OnWaitingForPlayers()
    {
        if (!Config.Debug && !Archive.TimeToBackup()) return;

        Log.Warn("Starting backup.");

        Task.Run(() =>
        {
            string[] archivesPath = Archive.CreateArchives(Config.LogFolders, Config.LogFiles, Config.ArchivePassword);

            foreach (var _archivePath in archivesPath)
            {
                string archivePath = _archivePath;

                if (Config.UseArchiveEncryption)
                {
                    byte[] key = Encrypt.GetKeyFromFile(Config.KeyPatch);
                    archivePath = Encrypt.EncryptFile(archivePath, key);
                }

                Log.Info("Backup is trying be sended to discord.");

                try
                {
                    Log.Error(archivePath);
                    _ = Task.Run(() => Archive.SendBackup(archivePath, Config.DiscordWebhookUrl));
                } catch(Exception ex)
                {
                    Log.Error(ex);
                }
            }
        });
    }
}
