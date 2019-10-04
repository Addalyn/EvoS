using System;

namespace EvoS.Framework.Assets.Serialized
{
    public class Hash128 : ISerializedItem
    {
        public byte[] Bytes = new byte[16];

        public void Deserialize(AssetFile assetFile, StreamReader stream)
        {
            for (int i = 0; i < Bytes.Length; i++)
            {
                Bytes[i] = (byte) stream.ReadInt32();
            }
        }

        public string ToHex()
        {
            return BitConverter.ToString(Bytes).Replace("-", "");
        }

        public override string ToString()
        {
            return $"{nameof(Hash128)}({ToHex()})";
        }
    }
}
