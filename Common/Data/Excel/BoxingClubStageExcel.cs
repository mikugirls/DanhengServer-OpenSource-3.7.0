using EggLink.DanhengServer.Data;
using EggLink.DanhengServer.Data.Excel;

namespace EggLink.DanhengServer.Data.Excel;

[ResourceEntity("BoxingClubStage.json")]
public class BoxingClubStageExcel : ExcelResource
{
    // 事件 ID (对应协议中的 StageID)
    public int EventID { get; set; }

    // 默认加成的 Buff ID
    public int BuffID { get; set; }

    // 可选的 Buff 列表 (三选一逻辑使用)
    public List<int> BuffOptionalList { get; set; } = [];

    // 怪物波次索引
    public int MonsterWaveIndex { get; set; }

    // 注意：JSON 中的 BubbleTalkPlayer, BubbleTalkEnemy, Name 是多语言哈希对象
    // 在服务器逻辑中如果不处理对话，可以暂时不定义它们，或者定义为 object 占位
    // public object Name { get; set; }

    public override int GetId()
    {
        return EventID;
    }

    public override void Loaded()
    {
        // 将加载的数据存入 GameData
        // 请确保在 GameData 类中已经定义了：
        // public static readonly Dictionary<int, BoxingClubStageExcel> BoxingClubStageData = new();
        GameData.BoxingClubStageData.TryAdd(EventID, this);
    }
}