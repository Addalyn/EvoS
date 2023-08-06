#nullable enable
using System;
using System.Collections.Generic;
using MongoDB.Bson.Serialization.Attributes;
using Newtonsoft.Json;

namespace EvoS.Framework.DataAccess.Daos
{
    public interface RegistrationCodeDao
    {
        public const int LIMIT = 25;
        
        public RegistrationCodeEntry? Find(string code);
        public List<RegistrationCodeEntry> FindBefore(DateTime dateTime);
        public void Save(RegistrationCodeEntry entry);

        public class RegistrationCodeEntry
        {
            [BsonId]
            public string Code;
            public long IssuedBy;
            public string IssuedTo;
            public DateTime IssuedAt;
            public DateTime ExpiresAt;
            public DateTime UsedAt;
            public long UsedBy;

            [JsonIgnore]
            public bool IsValid => UsedBy == 0 && ExpiresAt > DateTime.UtcNow;

            public RegistrationCodeEntry Use(long accountId)
            {
                return new RegistrationCodeEntry
                {
                    Code = Code,
                    IssuedBy = IssuedBy,
                    IssuedTo = IssuedTo,
                    IssuedAt = IssuedAt,
                    ExpiresAt = ExpiresAt,
                    UsedAt = DateTime.UtcNow,
                    UsedBy = accountId
                };
            }
        }
    }
}