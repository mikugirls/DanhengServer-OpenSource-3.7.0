using System.Collections.Concurrent;
using System.Globalization;
using EggLink.DanhengServer.Database.Account;
using EggLink.DanhengServer.Database.Quests;
using EggLink.DanhengServer.Internationalization;
using EggLink.DanhengServer.Util;
using SqlSugar;

namespace EggLink.DanhengServer.Database;

public class DatabaseHelper
{
    public static Logger logger = new("Database");
    public static SqlSugarScope? sqlSugarScope;
    public static DatabaseHelper? Instance;
    public static readonly ConcurrentDictionary<int, List<BaseDatabaseDataHelper>> UidInstanceMap = [];
    public static readonly List<int> ToSaveUidList = [];
    public static long LastSaveTick = DateTime.UtcNow.Ticks;
    public static Thread? SaveThread;
    public static bool LoadAccount;
    public static bool LoadAllData;

    public DatabaseHelper()
    {
        Instance = this;
    }
    public static void UpdateInstance<T>(T instance) where T : class, new()
{
    // 使用 Storageable 可以自动判断是插入还是更新（需要主键支持）
    // 或者简单使用 Updateable
    sqlSugarScope?.Updateable(instance).ExecuteCommand();
}
   public void Initialize()
{
    logger.Info(I18NManager.Translate("Server.ServerInfo.LoadingItem", I18NManager.Translate("Word.Database")));
    var config = ConfigManager.Config;
    DbType type;
    string connectionString;
    switch (config.Database.DatabaseType)
    {
        case "sqlite":
            type = DbType.Sqlite;
            var f = new FileInfo(config.Path.DatabasePath + "/" + config.Database.DatabaseName);
            if (!f.Exists && f.Directory != null) f.Directory.Create();
            connectionString = $"Data Source={f.FullName};";
            break;
        case "mysql":
            type = DbType.MySql;
            connectionString =
                $"server={config.Database.MySqlHost};Port={config.Database.MySqlPort};Database={config.Database.MySqlDatabase};Uid={config.Database.MySqlUser};Pwd={config.Database.MySqlPassword};";
            break;
        default:
            return;
    }

    sqlSugarScope = new SqlSugarScope(new ConnectionConfig
    {
        ConnectionString = connectionString,
        DbType = type,
        IsAutoCloseConnection = true,
        ConfigureExternalServices = new ConfigureExternalServices
        {
            SerializeService = new CustomSerializeService()
        }
    });

    switch (config.Database.DatabaseType)
    {
        case "sqlite":
            InitializeSqlite(); // for all database types
            break;
        case "mysql":
            InitializeMysql();
            break;
        default:
            logger.Error("Unsupported database type");
            break;
    }

    var baseType = typeof(BaseDatabaseDataHelper);
    var assembly = typeof(BaseDatabaseDataHelper).Assembly;

    // 获取所有数据库实体类型并转为 List 避免重复扫描
    var types = assembly.GetTypes().Where(t => t.IsSubclassOf(baseType)).ToList();

    // 加载账号数据
    var list = sqlSugarScope.Queryable<AccountData>()
        .Select(x => x)
        .ToList();

    if (list != null)
    {
        foreach (var inst in list.Select(instance => (instance as BaseDatabaseDataHelper)!))
        {
            if (!UidInstanceMap.TryGetValue(inst.Uid, out var value))
            {
                value = [];
                UidInstanceMap[inst.Uid] = value;
            }

            value.Add(inst); // add to the map
        }
    }

    // start dispatch server
    LoadAccount = true;

    // --- 关键修复：移除 Parallel.ForEach，改用顺序加载并增加异常保护 ---
    logger.Info("Starting to initialize database tables...");
    
    if (list != null)
    {
        foreach (var account in list)
        {
            foreach (var t in types)
            {
                if (t == typeof(AccountData)) continue; // skip the account data

                try
                {
                    // 使用反射安全调用 InitializeTable
                    var method = typeof(DatabaseHelper).GetMethod(nameof(InitializeTable))?.MakeGenericMethod(t);
                    method?.Invoke(null, [account.Uid]);
                }
                catch (Exception ex)
                {
                    // 即使某个玩家的某张表数据损坏（NULL/空白），也仅跳过该表，不影响服务器启动
                    logger.Error($"[DATABASE_ERROR] Failed to load table {t.Name} for UID {account.Uid}. This is likely caused by NULL or blank fields in your database.", ex);
                }
            }
        }
    }

    logger.Info("Database tables initialization completed.");

    LastSaveTick = DateTime.UtcNow.Ticks;

    SaveThread = new Thread(() =>
    {
        while (true) CalcSaveDatabase();
    });
    SaveThread.Start();

    LoadAllData = true;
}
   public static void InitializeTable<T>(int uid) where T : BaseDatabaseDataHelper, new()
{
    try
    {
        // 尝试正常查询数据库
        var list = sqlSugarScope?.Queryable<T>()
            .Where(x => x.Uid == uid)
            .ToList();

        if (list == null) return;

        // 正常映射到内存 Map
        foreach (var inst in list)
        {
            if (!UidInstanceMap.TryGetValue(inst.Uid, out var value))
            {
                value = new List<BaseDatabaseDataHelper>();
                UidInstanceMap[inst.Uid] = value;
            }

            value.Add(inst);
        }
    }
    catch (Exception ex)
    {
        // --- 核心修复：如果加载失败（通常是数据格式损坏） ---
        if (typeof(T) == typeof(EggLink.DanhengServer.Database.Gacha.GachaData))
        {
            // 记录一条警告，告知正在修复
            Logger.GetByClassName().Warn($"[DATABASE_REPAIR] UID {uid} 的抽卡数据损坏(NULL或空白)，正在尝试自动重建...");

            try
            {
                // 1. 删除数据库中导致崩溃的那条坏数据
                sqlSugarScope?.Deleteable<T>().Where(x => x.Uid == uid).ExecuteCommand();

                // 2. 创建一个全新的、干净的实例
                var newInst = new T { Uid = uid };
                
                // 3. 存入数据库（此时会应用类定义中的 DefaultValue）
                sqlSugarScope?.Insertable(newInst).ExecuteCommand();

                // 4. 将新实例放入内存 Map 确保游戏逻辑正常运行
                if (!UidInstanceMap.TryGetValue(uid, out var value))
                {
                    value = new List<BaseDatabaseDataHelper>();
                    UidInstanceMap[uid] = value;
                }
                value.Add(newInst);
                
                Logger.GetByClassName().Info($"[DATABASE_REPAIR] UID {uid} 的抽卡数据已重置并修复。");
            }
            catch (Exception repairEx)
            {
                Logger.GetByClassName().Error($"[DATABASE_REPAIR] 修复 UID {uid} 失败: {repairEx.Message}");
            }
        }
        else
        {
            // 如果是其他表报错且无法自动修复，至少打印出具体信息
            Logger.GetByClassName().Error($"[DATABASE_FATAL] 表 {typeof(T).Name} (UID: {uid}) 加载失败，跳过初始化。错误: {ex.Message}");
        }
    }
}

    public void UpgradeDatabase()
    {
        logger.Info("Upgrading database...");

        foreach (var instance in GetAllInstance<MissionData>()!) instance.MoveFromOld();
    }

    public void MoveFromSqlite()
    {
        logger.Info("Moving from sqlite...");

        var config = ConfigManager.Config;
        var f = new FileInfo(config.Path.DatabasePath + "/" + config.Database.DatabaseName);
        var sqliteScope = new SqlSugarScope(new ConnectionConfig
        {
            ConnectionString = $"Data Source={f.FullName};",
            DbType = DbType.Sqlite,
            IsAutoCloseConnection = true,
            ConfigureExternalServices = new ConfigureExternalServices
            {
                SerializeService = new CustomSerializeService()
            }
        });

        var baseType = typeof(BaseDatabaseDataHelper);
        var assembly = typeof(BaseDatabaseDataHelper).Assembly;
        var types = assembly.GetTypes().Where(t => t.IsSubclassOf(baseType));
        foreach (var type in types)
            typeof(DatabaseHelper).GetMethod("MoveSqliteTable")?.MakeGenericMethod(type).Invoke(null, [sqliteScope]);

        // exit the program
        Environment.Exit(0);
    }

    public static void MoveSqliteTable<T>(SqlSugarScope scope) where T : class, new()
    {
        try
        {
            var list = scope.Queryable<T>().ToList();
            foreach (var instance in list!) sqlSugarScope?.Insertable(instance).ExecuteCommand();
        }
        catch (Exception e)
        {
            Logger.GetByClassName().Error("An error occurred while moving the table", e);
        }
    }

    public static void InitializeSqlite()
    {
        var baseType = typeof(BaseDatabaseDataHelper);
        var assembly = typeof(BaseDatabaseDataHelper).Assembly;
        var types = assembly.GetTypes().Where(t => t.IsSubclassOf(baseType));
        foreach (var type in types)
            typeof(DatabaseHelper).GetMethod("InitializeSqliteTable")?.MakeGenericMethod(type).Invoke(null, null);
    }

    public static void InitializeMysql()
    {
        sqlSugarScope?.DbMaintenance.CreateDatabase();
        InitializeSqlite();
    }

    // ReSharper disable once UnusedMember.Global
    public static void InitializeSqliteTable<T>() where T : class, new()
    {
        try
        {
            sqlSugarScope?.CodeFirst.InitTables<T>();
        }
        catch
        {
            // ignored
        }
    }

    public T? GetInstance<T>(int uid) where T : class, new()
    {
        try
        {
            if (UidInstanceMap.TryGetValue(uid, out var value))
                return value.OfType<T>().Select(instance => instance).FirstOrDefault();
            value = [];
            UidInstanceMap[uid] = value;

            return value.OfType<T>().Select(instance => instance).FirstOrDefault();
        }
        catch (Exception e)
        {
            logger.Error("Unsupported type", e);
            return null;
        }
    }

    public T GetInstanceOrCreateNew<T>(int uid) where T : BaseDatabaseDataHelper, new()
    {
        var instance = GetInstance<T>(uid);
        if (instance != null) return instance;
        // judge if exists (maybe the instance is not in the map)

        var t = sqlSugarScope?.Queryable<T>()
            .Where(x => x.Uid == uid)
            .ToList();

        if (t is { Count: > 0 }) // exists in the database
        {
            instance = t[0];
            if (!UidInstanceMap.TryGetValue(uid, out var value))
            {
                value = [];
                UidInstanceMap[uid] = value;
            }

            value.Add(instance); // add to the map
            return instance;
        }

        // create a new instance
        instance = new T
        {
            Uid = uid
        };
        SaveInstance(instance);

        return instance;
    }

    public static List<T>? GetAllInstance<T>() where T : class, new()
    {
        try
        {
            return sqlSugarScope?.Queryable<T>()
                .Select(x => x)
                .ToList();
        }
        catch (Exception e)
        {
            logger.Error("Unsupported type", e);
            return null;
        }
    }

    public static List<T>? GetAllInstanceFromMap<T>() where T : class, new()
    {
        try
        {
            var list = UidInstanceMap.Values.SelectMany(x => x).ToList();
            return list.OfType<T>().Select(instance => instance).ToList();
        }
        catch (Exception e)
        {
            logger.Error("Unsupported type", e);
            return null;
        }
    }

    public static void SaveInstance<T>(T instance) where T : class, new()
    {
        sqlSugarScope?.Insertable(instance).ExecuteCommand();
        UidInstanceMap[(instance as BaseDatabaseDataHelper)!.Uid]
            .Add((instance as BaseDatabaseDataHelper)!); // add to the map
    }

    public void CalcSaveDatabase() // per 5 min
    {
        if (LastSaveTick + TimeSpan.TicksPerMinute * 5 > DateTime.UtcNow.Ticks) return;
        SaveDatabase();
    }

    public void SaveDatabase() // per 5 min
    {
        try
        {
            var prev = DateTime.Now;
            var list = ToSaveUidList.ToList(); // copy the list to avoid the exception
            foreach (var uid in list)
            {
                var value = UidInstanceMap[uid];
                var baseType = typeof(BaseDatabaseDataHelper);
                var assembly = typeof(BaseDatabaseDataHelper).Assembly;
                var types = assembly.GetTypes().Where(t => t.IsSubclassOf(baseType));
                foreach (var type in types)
                {
                    var instance = value.Find(x => x.GetType() == type);
                    if (instance != null)
                        typeof(DatabaseHelper).GetMethod("SaveDatabaseType")?.MakeGenericMethod(type)
                            .Invoke(null, [instance]);
                }
            }

            var t = (DateTime.Now - prev).TotalSeconds;
            logger.Info(I18NManager.Translate("Server.ServerInfo.SaveDatabase",
                Math.Round(t, 2).ToString(CultureInfo.InvariantCulture)));

            ToSaveUidList.Clear();
        }
        catch (Exception e)
        {
            logger.Error("An error occurred while saving the database", e);
        }

        LastSaveTick = DateTime.UtcNow.Ticks;
    }

    public static void SaveDatabaseType<T>(T instance) where T : class, new()
    {
        try
        {
            sqlSugarScope?.Updateable(instance).ExecuteCommand();
        }
        catch (Exception e)
        {
            logger.Error("An error occurred while saving the database", e);
        }
    }

    public void DeleteInstance<T>(T instance) where T : class, new()
    {
        sqlSugarScope?.Deleteable(instance).ExecuteCommand();
        UidInstanceMap[(instance as BaseDatabaseDataHelper)!.Uid]
            .Remove((instance as BaseDatabaseDataHelper)!); // remove from the map
        ToSaveUidList.Remove((instance as BaseDatabaseDataHelper)!.Uid); // remove from the save list
    }
}