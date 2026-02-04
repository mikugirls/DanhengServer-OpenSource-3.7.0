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
        // 1. 基础校验：无效 ID 直接拦截
        if (packet.CmdId <= 0)
        {
            Logger.Debug("Tried to send packet with missing cmd id!");
            return;
        }

        // 2. 黑名单校验：如果在 BannedPackets 里就不发
        if (BannedPackets.Contains(packet.CmdId)) return;

        // 3. 记录日志：在控制台打印发送记录
        LogPacket("Send", packet.CmdId, packet.Data);

        // 4. 构建原始二进制数据包
        var packetBytes = packet.BuildPacket();

        try
        {
            // 5. 执行加密并发送
            await SendPacket(packetBytes);
        }
        catch
        {
            // 忽略网络层面的发送失败
        }

        // ============================================================
        // 删掉的地方：从这里到方法结束，原本所有的 if (packet.CmdId == ...) 
        // 逻辑全部删除。
        // 不再下发 HandshakePacket，不再执行 Lua 脚本，不再弹窗。
        // ============================================================
    }
  

    public async Task SendPacket(int cmdId)
    {
        await SendPacket(new BasePacket((ushort)cmdId));
    }
}
