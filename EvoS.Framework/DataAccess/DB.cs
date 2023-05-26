using EvoS.Framework.DataAccess.Daos;
using EvoS.Framework.DataAccess.Mock;
using EvoS.Framework.DataAccess.Mongo;
using log4net;

namespace EvoS.Framework.DataAccess
{
    public class DB
    {
        private static readonly ILog log = LogManager.GetLogger(typeof(DB));
        
        private static DB Instance;
        public readonly AccountDao AccountDao;
        public readonly LoginDao LoginDao;
        public readonly MatchHistoryDao MatchHistoryDao;

        private DB()
        {
            switch (EvosConfiguration.GetDBConfig().Type)
            {
                case EvosConfiguration.DBType.Mongo:
                    log.Info("Using MongoDB");
                    AccountDao = new AccountDaoCached(new AccountMongoDao());
                    LoginDao = new LoginDaoCached(new LoginMongoDao());
                    MatchHistoryDao = new MatchHistoryDaoCached(new MatchHistoryMongoDao());
                    break;
                case EvosConfiguration.DBType.None:
                    log.Info("Not using any database, no data will be persisted");
                    AccountDao = new AccountDaoCached(new AccountMockDao());
                    LoginDao = new LoginDaoCached(new LoginMockDao());
                    MatchHistoryDao = new MatchHistoryDaoCached(new MatchHistoryMockDao());
                    break;
            }
        }

        public static DB Get()
        {
            return Instance ??= new DB();
        }
    }
}