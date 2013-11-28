using Hazelcast.Client.Request.Base;
using Hazelcast.Client.Request.Multimap;
using Hazelcast.Serialization.Hook;


namespace Hazelcast.Client.Request.Multimap
{
	
	public class SizeRequest : MultiMapAllPartitionRequest, IRetryableRequest
	{
		public SizeRequest()
		{
		}

		public SizeRequest(string name) : base(name)
		{
		}

		public override int GetClassId()
		{
			return MultiMapPortableHook.Size;
		}
	}
}