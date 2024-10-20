using System;
using System.Collections.Generic;
using EvoS.Framework.DataAccess.Daos;

namespace EvoS.Framework.DataAccess.Mock
{
    public class ChatHistoryMockDao: ChatHistoryDao
    {
        public List<ChatHistoryDao.Entry> GetRelevantMessages(
            long accountId,
            bool includeBlocked,
            DateTime afterTime,
            DateTime beforeTime,
            int limit)
        {
            return new List<ChatHistoryDao.Entry>();
        }

        public void Save(ChatHistoryDao.Entry entry)
        {
        }
    }
}