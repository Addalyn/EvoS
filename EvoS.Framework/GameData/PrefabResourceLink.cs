using System;
using EvoS.Framework.Network.Unity;

namespace EvoS.Framework.GameData;

[Serializable]
public class PrefabResourceLink
{
	[SerializeField]
	private string m_resourcePath;
	[SerializeField]
	private string m_GUID;
	[SerializeField]
	private string m_debugPrefabPath;

	public string GUID => m_GUID;
	internal string ResourcePath => m_resourcePath;
	public bool IsEmpty => string.IsNullOrEmpty(m_resourcePath);


	public override string ToString()
	{
		return !string.IsNullOrEmpty(m_debugPrefabPath)
			? m_debugPrefabPath
			: $"GUID: {m_GUID}";
	}

	public string GetResourcePath()
	{
		return m_resourcePath;
	}
}
