using System.Collections.Concurrent;
using System.Net;
using EggLink.DanhengServer.Kcp.KcpSharp;
using EggLink.DanhengServer.Util;

namespace EggLink.DanhengServer.Kcp;

public class DanhengConnection
{
    public const int HANDSHAKE_SIZE = 20;
    public static readonly ConcurrentBag<int> BannedPackets = [];
    private static readonly Logger Logger = new("GameServer");
    public static readonly ConcurrentDictionary<int, string> LogMap = [];

    public static readonly ConcurrentBag<int> IgnoreLog =
    [
        CmdIds.PlayerHeartBeatCsReq, CmdIds.PlayerHeartBeatScRsp, CmdIds.SceneEntityMoveCsReq,
        CmdIds.SceneEntityMoveScRsp, CmdIds.GetShopListCsReq, CmdIds.GetShopListScRsp, CmdIds.FightHeartBeatCsReq,
        CmdIds.FightHeartBeatScRsp
    ];

    protected readonly CancellationTokenSource CancelToken;
    protected readonly KcpConversation Conversation;
    public readonly IPEndPoint RemoteEndPoint;

    public string DebugFile = "";
    public bool IsOnline = true;
    public StreamWriter? Writer;

    public DanhengConnection(KcpConversation conversation, IPEndPoint remote)
    {
        Conversation = conversation;
        RemoteEndPoint = remote;
        CancelToken = new CancellationTokenSource();
        if (ConfigManager.Config.GameServer.UsePacketEncryption) XorKey = Crypto.ClientSecretKey!.GetXorKey();

        Start();
    }

    public byte[]? XorKey { get; set; }
    public ulong ClientSecretKeySeed { get; set; }

    public long? ConversationId => Conversation.ConversationId;

    public SessionStateEnum State { get; set; } = SessionStateEnum.INACTIVE;
    //public PlayerInstance? Player { get; set; }

    public virtual void Start()
    {
        Logger.Info($"New connection from {RemoteEndPoint}.");
        State = SessionStateEnum.WAITING_FOR_TOKEN;
    }

    public virtual void Stop()
    {
        //Player?.OnLogoutAsync();
        //Listener.UnregisterConnection(this);
        Conversation.Dispose();
        try
        {
            CancelToken.Cancel();
            CancelToken.Dispose();
        }
        catch
        {
        }

        IsOnline = false;
    }

    public void LogPacket(string sendOrRecv, ushort opcode, byte[] payload)
    {
        if (!ConfigManager.Config.ServerOption.LogOption.EnableGamePacketLog) return;

        try
        {
            if (IgnoreLog.Contains(opcode)) return;

            if (ConfigManager.Config.ServerOption.LogOption.DisableLogDetailPacket) throw new Exception();

            var asJson = PacketLogHelper.ConvertPacketToJson(opcode, payload);
            var output = $"{sendOrRecv}: {LogMap[opcode]}({opcode})\r\n{asJson}";

            if (ConfigManager.Config.ServerOption.LogOption.LogPacketToConsole)
                Logger.Debug(output);

            if (DebugFile == "" || !ConfigManager.Config.ServerOption.LogOption.SavePersonalDebugFile) return;
            var sw = GetWriter();
            sw.WriteLine($"[{DateTime.Now:HH:mm:ss}] [GameServer] [DEBUG] " + output);
            sw.Flush();
        }
        catch
        {
            var output = $"{sendOrRecv}: {LogMap.GetValueOrDefault(opcode, "UnknownPacket")}({opcode})";

            if (ConfigManager.Config.ServerOption.LogOption.LogPacketToConsole)
                Logger.Debug(output);

            if (DebugFile != "" && ConfigManager.Config.ServerOption.LogOption.SavePersonalDebugFile)
            {
                var sw = GetWriter();
                sw.WriteLine($"[{DateTime.Now:HH:mm:ss}] [GameServer] [DEBUG] " + output);
                sw.Flush();
            }
        }
    }

    private StreamWriter GetWriter()
    {
        // Create the file if it doesn't exist
        var file = new FileInfo(DebugFile);
        if (!file.Exists)
        {
            Directory.CreateDirectory(file.DirectoryName!);
            File.Create(DebugFile).Dispose();
        }

        Writer ??= new StreamWriter(DebugFile, true);
        return Writer;
    }

    public async Task SendPacket(byte[] packet)
    {
        try
        {
            if (ConfigManager.Config.GameServer.UsePacketEncryption)
                Crypto.Xor(packet, XorKey!);

            _ = await Conversation.SendAsync(packet, CancelToken.Token);
        }
        catch
        {
            // ignore
        }
    }

    public async Task SendPacket(BasePacket packet)
    {
        // Test
        if (packet.CmdId <= 0)
        {
            Logger.Debug("Tried to send packet with missing cmd id!");
            return;
        }

        // DO NOT REMOVE (unless we find a way to validate code before sending to client which I don't think we can)
        if (BannedPackets.Contains(packet.CmdId)) return;
        LogPacket("Send", packet.CmdId, packet.Data);
        // Header
        var packetBytes = packet.BuildPacket();

        try
        {
            await SendPacket(packetBytes);
        }
        catch
        {
            // ignore
        }

        if (packet.CmdId == CmdIds.SetClientPausedScRsp)
	{
    BasePacket lData;
    switch (ConfigManager.Config.ServerOption.Language)
    {
        case "CHS":
            // 修正：Lua脚本现在仅保留原始UID显示，不再追加 DanhengServer 文字
            lData = new HandshakePacket(Convert.FromBase64String(
                "bG9jYWwgZnVuY3Rpb24gbW9kaWZ5X3RleHRzKCkKICAgIGxvY2FsIHVpZCA9IENTLlVuaXR5RW5naW5lLkdhbWVPYmplY3QuRmluZCgiVmVyc2lvblRleHQiKTpHZXRDb21wb25lbnQoIlRleHQiKQogICAgaWYgdWlkIHRoZW4KICAgICAgICAtLSBCeXBhc3MgbW9kaWZpY2F0aW9uLCBrZWVwIG9yaWdpbmFsIHRleHQgKHdoaWNoIGlzIHRoZSBVSUQpCiAgICBlbmQKZW5kCgpsb2NhbCBzdGF0dXMsIGVyciA9IHBjYWxsKG1vZGlmeV90ZXh0cykKaWYgbm90IHN0YXR1cyB0aGVuCiAgICBsb2NhbCBmaWxlcyA9IGlvLm9wZW4oIi4vZXJyb3IudHh0IiwgInciKQogICAgZmlsZXM6d3JpdGUoZXJyKQogICAgZmlsZXM6Y2xvc2UoKQplbmQ="));
            break;
        case "CHT":
            // 繁体同理修正
            lData = new HandshakePacket(Convert.FromBase64String(
                "bG9jYWwgZnVuY3Rpb24gbW9kaWZ5X3RleHRzKCkKICAgIGxvY2FsIHVpZCA9IENTLlVuaXR5RW5naW5lLkdhbWVPYmplY3QuRmluZCgiVmVyc2lvblRleHQiKTpHZXRDb21wb25lbnQoIlRleHQiKQogICAgaWYgdWlkIHRoZW4KICAgICAgICAtLSBrZWVwIG9yaWdpbmFsCiAgICBlbmQKZW5kCgpsb2NhbCBzdGF0dXMsIGVyciA9IHBjYWxsKG1vZGlmeV90ZXh0cykKaWYgbm90IHN0YXR1cyB0aGVuCiAgICBsb2NhbCBmaWxlcyA9IGlvLm9wZW4oIi4vZXJyb3IudHh0IiwgInciKQogICAgZmlsZXM6d3JpdGUoZXJyKQogICAgZmlsZXM6Y2xvc2UoKQplbmQ="));
            break;
        default:
            // 英文同理修正
            lData = new HandshakePacket(Convert.FromBase64String(
                "bG9jYWwgZnVuY3Rpb24gbW9kaWZ5X3RleHRzKCkKICAgIGxvY2FsIHVpZCA9IENTLlVuaXR5RW5naW5lLkdhbWVPYmplY3QuRmluZCgiVmVyc2lvblRleHQiKTpHZXRDb21wb25lbnQoIlRleHQiKQogICAgaWYgdWlkIHRoZW4KICAgICAgICAtLSBrZWVwIG9yaWdpbmFsCiAgICBlbmQKZW5kCgpsb2NhbCBzdGF0dXMsIGVyciA9IHBjYWxsKG1vZGlmeV90ZXh0cykKaWYgbm90IHN0YXR1cyB0aGVuCiAgICBsb2NhbCBmaWxlcyA9IGlvLm9wZW4oIi4vZXJyb3IudHh0IiwgInciKQogICAgZmlsZXM6d3JpdGUoZXJyKQogICAgZmlsZXM6Y2xvc2UoKQplbmQ="));
            break;
		}

    await SendPacket(lData.BuildPacket());
	}
	if (packet.CmdId == CmdIds.GetTutorialScRsp)
	{
    // 将其改为不执行任何操作的 Lua 脚本
    BasePacket lData = new HandshakePacket(Convert.FromBase64String("LS0gRW1wdHk=")); 
    await SendPacket(lData.BuildPacket());
	}	
       
    }

    public async Task SendPacket(int cmdId)
    {
        await SendPacket(new BasePacket((ushort)cmdId));
    }
}
